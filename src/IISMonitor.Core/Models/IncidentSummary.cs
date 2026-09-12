using IISMonitor.Core.Enums;

namespace IISMonitor.Core.Models;

/// <summary>
/// Summary representation of an identified CPU incident per Section 19.
/// </summary>
public record IncidentSummary
{
    public string IncidentId { get; init; } = string.Empty;
    public string ServerName { get; init; } = Environment.MachineName;
    public DateTime StartTimeUtc { get; init; }
    public DateTime? EndTimeUtc { get; init; }
    public double DurationSeconds { get; init; }
    public TimeSpan Duration => DurationSeconds > 0
        ? TimeSpan.FromSeconds(DurationSeconds)
        : ((EndTimeUtc ?? DateTime.UtcNow) > StartTimeUtc
            ? (EndTimeUtc ?? DateTime.UtcNow) - StartTimeUtc
            : TimeSpan.FromSeconds(1));
    public double PeakCpuPercent { get; init; }
    public double AverageCpuPercent { get; init; }
    public double TriggerThresholdPercent { get; init; }
    public int SampleCount { get; init; }
    public int AffectedProcessCount { get; init; }
    public int AffectedAppPoolCount { get; init; }
    public string Status { get; init; } = "Active"; // Active, Recovered, AutoRecovered
    public IncidentClassification Classification { get; init; } = IncidentClassification.UnknownCpuSource;

    public string? TopProcessName { get; init; }
    public int? TopProcessId { get; init; }
    public double TopProcessPeakCpu { get; init; }

    public string? TopAppPoolName { get; init; }
    public double TopAppPoolPeakCpu { get; init; }

    public string? FingerprintHash { get; init; }
}
