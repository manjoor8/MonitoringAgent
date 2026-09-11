using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;

namespace IISMonitor.Core.StateMachine;

public record StateTransitionResult(
    bool Changed,
    MonitoringState PreviousState,
    MonitoringState CurrentState,
    double CpuPercent,
    string Reason);

/// <summary>
/// State machine for CPU incident detection with hysteresis and anti-flapping per Section 5.
/// </summary>
public class CpuMonitoringStateMachine
{
    private readonly object _syncRoot = new();
    private readonly MonitoringOptions _options;

    public MonitoringState CurrentState { get; private set; } = MonitoringState.Normal;
    public DateTime LastTransitionTimeUtc { get; private set; } = DateTime.UtcNow;
    public int ConsecutiveRecoverySamples { get; private set; }
    public string? CurrentIncidentId { get; private set; }

    public event EventHandler<StateTransitionResult>? StateChanged;

    public CpuMonitoringStateMachine(MonitoringOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Processes a new CPU sample through the state machine and returns the transition result.
    /// </summary>
    public StateTransitionResult ProcessCpuSample(double cpuPercent)
    {
        lock (_syncRoot)
        {
            var previousState = CurrentState;
            MonitoringState nextState = previousState;
            string reason = string.Empty;

            switch (previousState)
            {
                case MonitoringState.Normal:
                    if (cpuPercent >= _options.CpuThresholdPercent)
                    {
                        nextState = MonitoringState.IncidentStarting;
                        ConsecutiveRecoverySamples = 0;
                        CurrentIncidentId = GenerateIncidentId();
                        reason = $"CPU ({cpuPercent:F1}%) exceeded incident threshold ({_options.CpuThresholdPercent}%).";
                    }
                    break;

                case MonitoringState.IncidentStarting:
                    if (cpuPercent >= _options.CriticalCpuThresholdPercent)
                    {
                        nextState = MonitoringState.Critical;
                        reason = $"CPU ({cpuPercent:F1}%) escalated directly to critical ({_options.CriticalCpuThresholdPercent}%).";
                    }
                    else if (cpuPercent >= _options.CpuThresholdPercent)
                    {
                        nextState = MonitoringState.HighDetail;
                        reason = $"CPU ({cpuPercent:F1}%) sustained above incident threshold ({_options.CpuThresholdPercent}%).";
                    }
                    else if (cpuPercent < _options.RecoveryThresholdPercent)
                    {
                        // False alarm or brief spike that immediately cleared
                        nextState = MonitoringState.Normal;
                        CurrentIncidentId = null;
                        reason = $"CPU ({cpuPercent:F1}%) dropped below recovery threshold ({_options.RecoveryThresholdPercent}%) immediately.";
                    }
                    break;

                case MonitoringState.HighDetail:
                    if (cpuPercent >= _options.CriticalCpuThresholdPercent)
                    {
                        nextState = MonitoringState.Critical;
                        ConsecutiveRecoverySamples = 0;
                        reason = $"CPU ({cpuPercent:F1}%) reached critical threshold ({_options.CriticalCpuThresholdPercent}%).";
                    }
                    else if (cpuPercent < _options.RecoveryThresholdPercent)
                    {
                        nextState = MonitoringState.Recovery;
                        ConsecutiveRecoverySamples = 1;
                        reason = $"CPU ({cpuPercent:F1}%) dropped below recovery threshold ({_options.RecoveryThresholdPercent}%). Starting recovery sample verification (1/{_options.RecoveryConsecutiveSamples}).";
                    }
                    break;

                case MonitoringState.Critical:
                    if (cpuPercent < _options.RecoveryThresholdPercent)
                    {
                        nextState = MonitoringState.Recovery;
                        ConsecutiveRecoverySamples = 1;
                        reason = $"CPU ({cpuPercent:F1}%) dropped below recovery threshold ({_options.RecoveryThresholdPercent}%) from critical. (1/{_options.RecoveryConsecutiveSamples}).";
                    }
                    else if (cpuPercent < _options.CriticalCpuThresholdPercent)
                    {
                        nextState = MonitoringState.HighDetail;
                        reason = $"CPU ({cpuPercent:F1}%) dropped below critical threshold ({_options.CriticalCpuThresholdPercent}%) but remains elevated.";
                    }
                    break;

                case MonitoringState.Recovery:
                    if (cpuPercent >= _options.CriticalCpuThresholdPercent)
                    {
                        // Re-spike to critical
                        nextState = MonitoringState.Critical;
                        ConsecutiveRecoverySamples = 0;
                        reason = $"CPU re-spiked to critical ({cpuPercent:F1}%).";
                    }
                    else if (cpuPercent >= _options.CpuThresholdPercent)
                    {
                        // Re-spike to high detail
                        nextState = MonitoringState.HighDetail;
                        ConsecutiveRecoverySamples = 0;
                        reason = $"CPU re-spiked to elevated ({cpuPercent:F1}%).";
                    }
                    else if (cpuPercent < _options.RecoveryThresholdPercent)
                    {
                        ConsecutiveRecoverySamples++;
                        if (ConsecutiveRecoverySamples >= _options.RecoveryConsecutiveSamples)
                        {
                            nextState = MonitoringState.Normal;
                            reason = $"CPU ({cpuPercent:F1}%) remained below recovery threshold for {_options.RecoveryConsecutiveSamples} consecutive samples. Incident concluded.";
                        }
                        else
                        {
                            reason = $"Recovery sample {ConsecutiveRecoverySamples}/{_options.RecoveryConsecutiveSamples} verified at {cpuPercent:F1}%.";
                        }
                    }
                    else
                    {
                        // In between recovery threshold (60%) and incident threshold (70%) - hysteresis band
                        // Stay in recovery, but reset consecutive sample counter
                        ConsecutiveRecoverySamples = 0;
                        reason = $"CPU ({cpuPercent:F1}%) within hysteresis band ({_options.RecoveryThresholdPercent}% - {_options.CpuThresholdPercent}%). Awaiting full normalization.";
                    }
                    break;
            }

            bool changed = nextState != previousState;
            if (changed)
            {
                CurrentState = nextState;
                LastTransitionTimeUtc = DateTime.UtcNow;
            }

            var result = new StateTransitionResult(changed, previousState, nextState, cpuPercent, reason);

            if (changed)
            {
                StateChanged?.Invoke(this, result);
            }

            return result;
        }
    }

    /// <summary>
    /// For testing and state reset.
    /// </summary>
    public void Reset()
    {
        lock (_syncRoot)
        {
            CurrentState = MonitoringState.Normal;
            ConsecutiveRecoverySamples = 0;
            CurrentIncidentId = null;
            LastTransitionTimeUtc = DateTime.UtcNow;
        }
    }

    private static string GenerateIncidentId()
    {
        return $"INC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    }
}
