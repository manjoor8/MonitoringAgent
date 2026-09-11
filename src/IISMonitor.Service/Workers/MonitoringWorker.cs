using System.Threading.Channels;
using IISMonitor.Core.Collections;
using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using IISMonitor.Core.StateMachine;
using IISMonitor.Data.Context;
using IISMonitor.Data.Entities;
using IISMonitor.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Service.Workers;

/// <summary>
/// Core monitoring background service orchestrating the 30s/5s collection loop,
/// state machine transitions, baseline promotion, extended diagnostics, and auto-recovery.
/// </summary>
public class MonitoringWorker : BackgroundService
{
    private readonly MonitoringOptions _monitoringOptions;
    private readonly DiagnosticsOptions _diagnosticsOptions;
    private readonly IServerMetricsCollector _serverMetricsCollector;
    private readonly IProcessMetricsCollector _processMetricsCollector;
    private readonly IIisMonitor _iisMonitor;
    private readonly CpuMonitoringStateMachine _stateMachine;
    private readonly CircularBuffer<MonitoringSample> _baselineBuffer;
    private readonly Channel<MetricWriteBatch> _writeChannel;
    private readonly IIncidentService _incidentService;
    private readonly IAutoRecoveryService _autoRecoveryService;
    private readonly IProcDumpService _procDumpService;
    private readonly IWindowsEventCollector _eventCollector;
    private readonly IScheduledTaskCollector _taskCollector;
    private readonly IDotNetRuntimeCollector _dotNetCollector;
    private readonly IDbContextFactory<IISMonitorDbContext> _contextFactory;
    private readonly IAlertService _alertService;
    private readonly ILogger<MonitoringWorker> _logger;

    private IncidentSummary? _currentIncident;
    private bool _hasCapturedHighDetailThisCycle;

    public MonitoringWorker(
        MonitoringOptions monitoringOptions,
        DiagnosticsOptions diagnosticsOptions,
        IServerMetricsCollector serverMetricsCollector,
        IProcessMetricsCollector processMetricsCollector,
        IIisMonitor iisMonitor,
        CpuMonitoringStateMachine stateMachine,
        CircularBuffer<MonitoringSample> baselineBuffer,
        Channel<MetricWriteBatch> writeChannel,
        IIncidentService incidentService,
        IAutoRecoveryService autoRecoveryService,
        IProcDumpService procDumpService,
        IWindowsEventCollector eventCollector,
        IScheduledTaskCollector taskCollector,
        IDotNetRuntimeCollector dotNetCollector,
        IDbContextFactory<IISMonitorDbContext> contextFactory,
        IAlertService alertService,
        ILogger<MonitoringWorker> logger)
    {
        _monitoringOptions = monitoringOptions ?? throw new ArgumentNullException(nameof(monitoringOptions));
        _diagnosticsOptions = diagnosticsOptions ?? throw new ArgumentNullException(nameof(diagnosticsOptions));
        _serverMetricsCollector = serverMetricsCollector ?? throw new ArgumentNullException(nameof(serverMetricsCollector));
        _processMetricsCollector = processMetricsCollector ?? throw new ArgumentNullException(nameof(processMetricsCollector));
        _iisMonitor = iisMonitor ?? throw new ArgumentNullException(nameof(iisMonitor));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _baselineBuffer = baselineBuffer ?? throw new ArgumentNullException(nameof(baselineBuffer));
        _writeChannel = writeChannel ?? throw new ArgumentNullException(nameof(writeChannel));
        _incidentService = incidentService ?? throw new ArgumentNullException(nameof(incidentService));
        _autoRecoveryService = autoRecoveryService ?? throw new ArgumentNullException(nameof(autoRecoveryService));
        _procDumpService = procDumpService ?? throw new ArgumentNullException(nameof(procDumpService));
        _eventCollector = eventCollector ?? throw new ArgumentNullException(nameof(eventCollector));
        _taskCollector = taskCollector ?? throw new ArgumentNullException(nameof(taskCollector));
        _dotNetCollector = dotNetCollector ?? throw new ArgumentNullException(nameof(dotNetCollector));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _alertService = alertService ?? throw new ArgumentNullException(nameof(alertService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _stateMachine.StateChanged += OnStateMachineTransition;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IIS Monitoring Engine started. Normal cadence: {Interval}s.",
            _monitoringOptions.NormalIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var loopStartTime = DateTime.UtcNow;

            try
            {
                await PerformMonitoringCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during monitoring cycle.");
            }

            // Determine interval based on state machine (Normal: 30s, Incident: 5s per Section 21)
            int intervalSeconds = (_stateMachine.CurrentState == MonitoringState.Normal)
                ? _monitoringOptions.NormalIntervalSeconds
                : _monitoringOptions.IncidentIntervalSeconds;

            var elapsed = DateTime.UtcNow - loopStartTime;
            int delayMs = Math.Max(500, (intervalSeconds * 1000) - (int)elapsed.TotalMilliseconds);

            try
            {
                await Task.Delay(delayMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("IIS Monitoring Engine stopped.");
    }

    private async Task PerformMonitoringCycleAsync(CancellationToken ct)
    {
        // 1. Collect Server Metrics
        var serverMetrics = await _serverMetricsCollector.CollectAsync(ct);

        // 2. Collect Process Metrics
        var processes = await _processMetricsCollector.CollectAsync(ct);

        // 3. Worker process mappings & AppPool metrics
        var mappings = await _iisMonitor.GetWorkerProcessMappingsAsync(ct);
        var appPoolMetrics = new List<AppPoolMetrics>();

        foreach (var m in mappings)
        {
            var proc = processes.FirstOrDefault(p => p.ProcessId == m.ProcessId);
            if (proc != null)
            {
                appPoolMetrics.Add(new AppPoolMetrics
                {
                    AppPoolName = m.AppPoolName,
                    ProcessId = m.ProcessId,
                    CpuPercent = proc.CpuPercent,
                    TotalProcessorTimeMs = proc.TotalProcessorTimeMs,
                    PrivateMemoryBytes = proc.PrivateBytes,
                    WorkingSetBytes = proc.WorkingSet,
                    ThreadCount = proc.ThreadCount,
                    HandleCount = proc.HandleCount,
                    ProcessAgeSeconds = proc.UptimeSeconds
                });
            }
        }

        // 4. Process CPU through State Machine
        var transition = _stateMachine.ProcessCpuSample(serverMetrics.TotalCpuPercent);

        bool isIncidentActive = _stateMachine.CurrentState != MonitoringState.Normal;
        string? activeIncidentId = _stateMachine.CurrentIncidentId;

        var sample = new MonitoringSample
        {
            TimestampUtc = serverMetrics.TimestampUtc,
            IncidentId = activeIncidentId,
            IsBaseline = !isIncidentActive,
            IsHighDetail = isIncidentActive,
            ServerMetrics = serverMetrics,
            TopProcesses = processes.Take(10).ToList(),
            AppPoolMetrics = appPoolMetrics
        };

        // 5. Store in circular buffer (rolling baseline)
        _baselineBuffer.Push(sample);

        // 6. Push to persistence channel
        _writeChannel.Writer.TryWrite(new MetricWriteBatch(sample, processes.Take(10).ToList(), appPoolMetrics));

        // 7. If in incident, collect extended diagnostics and evaluate recovery
        if (isIncidentActive && activeIncidentId != null)
        {
            await HandleActiveIncidentCycleAsync(activeIncidentId, serverMetrics, processes, appPoolMetrics, ct);
        }
    }

    private async Task HandleActiveIncidentCycleAsync(
        string incidentId,
        ServerMetrics serverMetrics,
        IReadOnlyList<ProcessMetrics> processes,
        IReadOnlyList<AppPoolMetrics> appPools,
        CancellationToken ct)
    {
        var topProcess = processes.OrderByDescending(p => p.CpuPercent).FirstOrDefault();
        var topPool = appPools.OrderByDescending(a => a.CpuPercent).FirstOrDefault();

        // Update active incident summary in repository
        if (_currentIncident != null)
        {
            _currentIncident = _currentIncident with
            {
                PeakCpuPercent = Math.Max(_currentIncident.PeakCpuPercent, serverMetrics.TotalCpuPercent),
                SampleCount = _currentIncident.SampleCount + 1,
                TopProcessName = topProcess?.ProcessName,
                TopProcessId = topProcess?.ProcessId,
                TopProcessPeakCpu = Math.Max(_currentIncident.TopProcessPeakCpu, topProcess?.CpuPercent ?? 0),
                TopAppPoolName = topPool?.AppPoolName,
                TopAppPoolPeakCpu = Math.Max(_currentIncident.TopAppPoolPeakCpu, topPool?.CpuPercent ?? 0)
            };
            await _incidentService.UpdateIncidentAsync(_currentIncident, ct);
        }

        // Evaluate Automatic Recovery per Sections 51-66
        if (_autoRecoveryService.IsRecoveryEligible(
            serverMetrics.TotalCpuPercent,
            processes,
            appPools,
            out string? culpritPool,
            out int culpritPid,
            out double culpritCpu))
        {
            _logger.LogWarning("Auto-recovery eligible: Culprit pool '{Pool}' (PID: {Pid}, CPU: {Cpu:F1}%).",
                culpritPool, culpritPid, culpritCpu);

            var action = await _autoRecoveryService.ExecuteRecoveryAsync(
                incidentId, culpritPool!, culpritPid, culpritCpu, serverMetrics.TotalCpuPercent, ct);

            if (action.Result == "Successful")
            {
                _ = Task.Run(() => _autoRecoveryService.VerifyRecoveryAsync(action, async () =>
                {
                    var m = await _serverMetricsCollector.CollectAsync(CancellationToken.None);
                    return m.TotalCpuPercent;
                }, CancellationToken.None));
            }
        }

        // Extended diagnostics (Windows events, tasks, ProcDump if trigger reached)
        if (!_hasCapturedHighDetailThisCycle)
        {
            _hasCapturedHighDetailThisCycle = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    await CaptureExtendedDiagnosticsAsync(incidentId, topProcess, topPool, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error capturing extended diagnostics.");
                }
            }, ct);
        }
    }

    private async Task CaptureExtendedDiagnosticsAsync(
        string incidentId,
        ProcessMetrics? topProcess,
        AppPoolMetrics? topPool,
        CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // 1. Windows Events around incident window
        var now = DateTime.UtcNow;
        var events = await _eventCollector.CollectEventsAsync(now.AddMinutes(-30), now.AddMinutes(5), null, ct);
        var eventEntities = events.Select(e => new WindowsEventEntity
        {
            TimestampUtc = e.TimestampUtc,
            IncidentId = incidentId,
            Provider = e.ProviderName,
            EventId = e.EventId,
            Level = e.Level,
            Message = e.Message
        }).ToList();
        await context.WindowsEvents.AddRangeAsync(eventEntities, ct);

        // 2. Scheduled Tasks running during incident
        var tasks = await _taskCollector.GetTasksAsync(ct);
        var taskEntities = tasks.Select(t => new ScheduledTaskEntity
        {
            TimestampUtc = now,
            IncidentId = incidentId,
            TaskName = t.TaskName,
            TaskPath = t.TaskPath,
            LastRunTime = t.LastRunTime,
            NextRunTime = t.NextRunTime,
            LastResult = t.LastTaskResult,
            State = t.State,
            IsRunning = t.IsRunning
        }).ToList();
        await context.ScheduledTasks.AddRangeAsync(taskEntities, ct);

        // 3. .NET Runtime metrics if w3wp
        if (topProcess != null && topProcess.IsW3wp && topPool != null)
        {
            var dotNetMetrics = await _dotNetCollector.CollectMetricsAsync(topProcess.ProcessId, topPool.AppPoolName, ct);
            var dotNetEntities = dotNetMetrics.Select(d => new DotNetRuntimeMetricEntity
            {
                TimestampUtc = now,
                IncidentId = incidentId,
                ProcessId = topProcess.ProcessId,
                AppPoolName = topPool.AppPoolName,
                MetricName = d.MetricName,
                MetricValue = d.MetricValue,
                Source = d.Source,
                IsAvailable = d.IsAvailable
            }).ToList();
            await context.DotNetRuntimeMetrics.AddRangeAsync(dotNetEntities, ct);
        }

        // 4. ProcDump trigger check per Section 15
        if (_diagnosticsOptions.ProcDumpEnabled &&
            topProcess != null && topProcess.IsW3wp &&
            topProcess.CpuPercent >= _diagnosticsOptions.TriggerCpuPercent &&
            topPool != null)
        {
            await _procDumpService.TriggerDumpAsync(topProcess.ProcessId, topPool.AppPoolName, incidentId, ct);
        }

        await context.SaveChangesAsync(ct);
        _logger.LogInformation("Persisted extended diagnostics for incident {IncidentId}.", incidentId);
    }

    private void OnStateMachineTransition(object? sender, StateTransitionResult e)
    {
        _logger.LogInformation("State Machine Transition: {Prev} -> {Current} (CPU: {Cpu:F1}%). Reason: {Reason}",
            e.PreviousState, e.CurrentState, e.CpuPercent, e.Reason);

        if (e.CurrentState == MonitoringState.IncidentStarting && e.PreviousState == MonitoringState.Normal)
        {
            string incidentId = _stateMachine.CurrentIncidentId!;
            _hasCapturedHighDetailThisCycle = false;

            _ = Task.Run(async () =>
            {
                _currentIncident = await _incidentService.StartIncidentAsync(incidentId, e.CpuPercent);

                // Promote baseline buffer samples into SQLite associated with this incident
                await PromoteBaselineBufferToIncidentAsync(incidentId);

                await _alertService.SendAlertAsync(AlertType.IncidentStarted, _currentIncident);
            });
        }
        else if (e.CurrentState == MonitoringState.Critical)
        {
            if (_currentIncident != null)
            {
                _ = _alertService.SendAlertAsync(AlertType.CriticalCpu, _currentIncident, "CPU escalated to critical threshold (>=90%).");
            }
        }
        else if (e.CurrentState == MonitoringState.Normal && e.PreviousState == MonitoringState.Recovery)
        {
            string? incidentId = _stateMachine.CurrentIncidentId;
            if (incidentId != null)
            {
                _ = Task.Run(async () =>
                {
                    var closed = await _incidentService.EndIncidentAsync(incidentId);
                    if (closed != null)
                    {
                        await _alertService.SendAlertAsync(AlertType.IncidentRecovered, closed);
                    }
                    _currentIncident = null;
                });
            }
        }
    }

    private async Task PromoteBaselineBufferToIncidentAsync(string incidentId)
    {
        try
        {
            var snapshot = _baselineBuffer.Snapshot();
            _logger.LogInformation("Promoting {Count} baseline samples to incident {IncidentId}.",
                snapshot.Length, incidentId);

            await using var context = await _contextFactory.CreateDbContextAsync();
            var promoted = new List<MetricSampleEntity>();

            foreach (var s in snapshot)
            {
                promoted.Add(new MetricSampleEntity
                {
                    TimestampUtc = s.TimestampUtc,
                    IncidentId = incidentId,
                    TotalCpu = s.ServerMetrics.TotalCpuPercent,
                    UserCpu = s.ServerMetrics.UserCpuPercent,
                    PrivilegedCpu = s.ServerMetrics.PrivilegedCpuPercent,
                    ProcessorQueueLength = s.ServerMetrics.ProcessorQueueLength,
                    AvailableMemoryMb = s.ServerMetrics.AvailableMemoryMb,
                    CommittedMemoryPercent = s.ServerMetrics.CommittedMemoryPercent,
                    IsBaseline = true,
                    IsHighDetail = false
                });
            }

            await context.MetricSamples.AddRangeAsync(promoted);
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to promote baseline buffer to incident {IncidentId}.", incidentId);
        }
    }
}
