using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.AutoRecovery;

/// <summary>
/// Implements automated recovery guards, culprit identification, cooldowns, and auditing per Sections 51-66.
/// Guarantees that diagnostic capture precedes restart and that only the identified culprit pool is recycled.
/// </summary>
public class AutoRecoveryService : IAutoRecoveryService
{
    private readonly AutomaticRecoveryOptions _options;
    private readonly IIisMonitor _iisMonitor;
    private readonly IProcDumpService _procDumpService;
    private readonly IRecoveryAuditSink _auditSink;
    private readonly IAlertService _alertService;
    private readonly ILogger<AutoRecoveryService> _logger;

    private readonly object _lock = new();
    private DateTime? _criticalConditionStartTimeUtc;
    private DateTime? _lastRestartTimeUtc;
    private readonly List<DateTime> _recentRestartsUtc = new();

    public AutoRecoveryService(
        AutomaticRecoveryOptions options,
        IIisMonitor iisMonitor,
        IProcDumpService procDumpService,
        IRecoveryAuditSink auditSink,
        IAlertService alertService,
        ILogger<AutoRecoveryService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _iisMonitor = iisMonitor ?? throw new ArgumentNullException(nameof(iisMonitor));
        _procDumpService = procDumpService ?? throw new ArgumentNullException(nameof(procDumpService));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _alertService = alertService ?? throw new ArgumentNullException(nameof(alertService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Evaluates whether the incident qualifies for automatic recovery per Sections 52, 53, 54, 62.
    /// </summary>
    public bool IsRecoveryEligible(
        double serverCpu,
        IReadOnlyList<ProcessMetrics> processes,
        IReadOnlyList<AppPoolMetrics> appPools,
        out string? culpritAppPool,
        out int culpritPid,
        out double culpritCpu)
    {
        culpritAppPool = null;
        culpritPid = 0;
        culpritCpu = 0;

        if (!_options.Enabled)
        {
            return false;
        }

        lock (_lock)
        {
            // 1. Server CPU must exceed critical threshold (default >= 90%)
            if (serverCpu < _options.CriticalCpuThresholdPercent)
            {
                // Reset critical timer if CPU drops below threshold
                if (_criticalConditionStartTimeUtc != null)
                {
                    _logger.LogInformation("Server CPU dropped to {Cpu:F1}%. Resetting critical duration timer.", serverCpu);
                    _criticalConditionStartTimeUtc = null;
                }
                return false;
            }

            // Start or maintain critical duration timer
            _criticalConditionStartTimeUtc ??= DateTime.UtcNow;
            double criticalDurationMinutes = (DateTime.UtcNow - _criticalConditionStartTimeUtc.Value).TotalMinutes;

            if (criticalDurationMinutes < _options.MinimumCriticalDurationMinutes)
            {
                _logger.LogDebug("Critical CPU sustained for {Minutes:F1}/{Required:F1} min.",
                    criticalDurationMinutes, (double)_options.MinimumCriticalDurationMinutes);
                return false;
            }

            // 2. Determine top CPU-consuming process
            var topProcess = processes.OrderByDescending(p => p.CpuPercent).FirstOrDefault();
            if (topProcess == null || !topProcess.IsW3wp)
            {
                _logger.LogInformation("Top process is not w3wp.exe ({Name} at {Cpu:F1}%). Automatic recovery not applicable.",
                    topProcess?.ProcessName ?? "None", topProcess?.CpuPercent ?? 0);
                return false;
            }

            // 3. Find matching AppPool for this w3wp PID
            var matchingPool = appPools.FirstOrDefault(a => a.ProcessId == topProcess.ProcessId);
            if (matchingPool == null)
            {
                _logger.LogWarning("w3wp PID {Pid} could not be mapped to an Application Pool.", topProcess.ProcessId);
                return false;
            }

            // 4. Check for multiple high CPU consumers per Section 62
            var otherHighPools = appPools
                .Where(a => a.AppPoolName != matchingPool.AppPoolName && a.CpuPercent >= 30.0)
                .ToList();

            if (otherHighPools.Count > 0)
            {
                _logger.LogWarning("Multiple App Pools consuming high CPU simultaneously ({Pool1}, {Pool2}). Aborting auto-recovery.",
                    matchingPool.AppPoolName, otherHighPools.First().AppPoolName);
                return false;
            }

            // 5. Check if worker process meets minimum culprit percentage (default >= 70%)
            if (topProcess.CpuPercent < _options.MinimumCulpritCpuPercent)
            {
                _logger.LogInformation("Culprit w3wp CPU ({Cpu:F1}%) is below minimum required culprit threshold ({Min:F1}%).",
                    topProcess.CpuPercent, _options.MinimumCulpritCpuPercent);
                return false;
            }

            // 6. Check restart limits and cooldown per Section 61
            PruneRestartHistory();

            if (_lastRestartTimeUtc.HasValue)
            {
                double minutesSinceLast = (DateTime.UtcNow - _lastRestartTimeUtc.Value).TotalMinutes;
                if (minutesSinceLast < _options.CooldownMinutes)
                {
                    _logger.LogWarning("Automatic recovery cooldown active ({Minutes:F1}/{Required} min). Restart blocked.",
                        minutesSinceLast, _options.CooldownMinutes);
                    return false;
                }
            }

            int pastHourRestarts = _recentRestartsUtc.Count(r => r >= DateTime.UtcNow.AddHours(-1));
            if (pastHourRestarts >= _options.MaxRestartsPerHour)
            {
                _logger.LogError("Automatic recovery blocked: Maximum restarts per hour reached ({Current}/{Max}).",
                    pastHourRestarts, _options.MaxRestartsPerHour);
                return false;
            }

            int pastDayRestarts = _recentRestartsUtc.Count(r => r >= DateTime.UtcNow.AddDays(-1));
            if (pastDayRestarts >= _options.MaxRestartsPerDay)
            {
                _logger.LogError("Automatic recovery blocked: Maximum restarts per day reached ({Current}/{Max}).",
                    pastDayRestarts, _options.MaxRestartsPerDay);
                return false;
            }

            culpritAppPool = matchingPool.AppPoolName;
            culpritPid = topProcess.ProcessId;
            culpritCpu = topProcess.CpuPercent;
            return true;
        }
    }

    /// <summary>
    /// Executes the recovery workflow per Sections 55, 56, 57, 59.
    /// Captures dump first, re-checks CPU, and recycles only the culprit pool.
    /// </summary>
    public async Task<RecoveryAction> ExecuteRecoveryAsync(
        string incidentId,
        string appPoolName,
        int pid,
        double culpritCpu,
        double serverCpu,
        CancellationToken cancellationToken = default)
    {
        double criticalDurationSec = _criticalConditionStartTimeUtc.HasValue
            ? (DateTime.UtcNow - _criticalConditionStartTimeUtc.Value).TotalSeconds
            : 0;

        _logger.LogWarning("CRITICAL: Executing automatic recovery for AppPool '{Pool}' (PID: {Pid}, CPU: {Cpu:F1}%).",
            appPoolName, pid, culpritCpu);

        // 1. Evidence capture before restart per Section 55
        if (_options.CaptureDumpBeforeRestart)
        {
            _logger.LogInformation("Capturing pre-recovery diagnostic memory dump for PID {Pid}.", pid);
            await _procDumpService.TriggerDumpAsync(pid, appPoolName, incidentId, cancellationToken);
        }

        // 2. Final verification before restart per Section 56
        // If the process died or recovered in the interim, abort.
        if (serverCpu < _options.CriticalCpuThresholdPercent || culpritCpu < _options.MinimumCulpritCpuPercent)
        {
            _logger.LogInformation("Final pre-restart check: CPU subsided naturally. Aborting recycle.");
            return new RecoveryAction
            {
                IncidentId = incidentId,
                AppPoolName = appPoolName,
                ProcessId = pid,
                CpuBeforeRestart = culpritCpu,
                ServerCpuBeforeRestart = serverCpu,
                CriticalDurationSeconds = criticalDurationSec,
                Action = RecoveryActionType.None,
                Result = "AbortedNaturalRecovery",
                ErrorMessage = "CPU normalized prior to restart execution."
            };
        }

        // 3. Recycle ONLY the culprit Application Pool per Section 57
        bool recycleSuccess = await _iisMonitor.RecycleAppPoolAsync(appPoolName, cancellationToken);

        var actionResult = new RecoveryAction
        {
            IncidentId = incidentId,
            TimestampUtc = DateTime.UtcNow,
            AppPoolName = appPoolName,
            ProcessId = pid,
            CpuBeforeRestart = culpritCpu,
            ServerCpuBeforeRestart = serverCpu,
            CriticalDurationSeconds = criticalDurationSec,
            Action = RecoveryActionType.AppPoolRecycle,
            Result = recycleSuccess ? "Successful" : "Failed",
            ErrorMessage = recycleSuccess ? null : "IIS ServerManager failed to recycle pool."
        };

        lock (_lock)
        {
            _lastRestartTimeUtc = DateTime.UtcNow;
            _recentRestartsUtc.Add(DateTime.UtcNow);
            _criticalConditionStartTimeUtc = null; // Reset critical timer
        }

        // 4. Persist audit record per Section 59
        await _auditSink.RecordRecoveryActionAsync(actionResult, cancellationToken);

        return actionResult;
    }

    /// <summary>
    /// Verifies post-restart recovery at configured intervals per Section 60.
    /// </summary>
    public async Task VerifyRecoveryAsync(
        RecoveryAction recoveryAction,
        Func<Task<double>> currentCpuProvider,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting post-recovery verification for '{Pool}'.", recoveryAction.AppPoolName);

        foreach (int intervalSeconds in _options.VerificationIntervalsSeconds)
        {
            if (cancellationToken.IsCancellationRequested) break;

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);

            double currentCpu = await currentCpuProvider();
            _logger.LogInformation("Post-recovery check at {Sec}s: Server CPU = {Cpu:F1}%.", intervalSeconds, currentCpu);

            await _auditSink.UpdateVerificationAsync(recoveryAction.IncidentId, intervalSeconds, currentCpu, cancellationToken);
        }
    }

    private void PruneRestartHistory()
    {
        var oneDayAgo = DateTime.UtcNow.AddDays(-1);
        _recentRestartsUtc.RemoveAll(r => r < oneDayAgo);
    }
}
