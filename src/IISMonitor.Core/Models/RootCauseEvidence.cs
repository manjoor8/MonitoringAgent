using IISMonitor.Core.Enums;

namespace IISMonitor.Core.Models;

/// <summary>
/// Root cause evidence report per Section 33.
/// Presents objective correlation metrics without asserting unwarranted definitive claims.
/// </summary>
public record RootCauseEvidence
{
    public string IncidentId { get; init; } = string.Empty;

    // Primary CPU Consumer
    public string PrimaryProcessName { get; init; } = string.Empty;
    public int? PrimaryProcessId { get; init; }
    public double PrimaryProcessPeakCpu { get; init; }
    public CorrelationAssessment PrimaryProcessConfidence { get; init; } = CorrelationAssessment.Unknown;

    // Primary App Pool
    public string? PrimaryAppPoolName { get; init; }
    public double PrimaryAppPoolPeakCpu { get; init; }
    public CorrelationAssessment PrimaryAppPoolConfidence { get; init; } = CorrelationAssessment.Unknown;

    // Traffic Correlation
    public double TrafficChangePercent { get; init; }
    public string TrafficSummary { get; init; } = string.Empty;
    public CorrelationAssessment TrafficAssessment { get; init; } = CorrelationAssessment.Unknown;

    // Memory Correlation
    public long BaselinePrivateMemoryBytes { get; init; }
    public long PeakPrivateMemoryBytes { get; init; }
    public string MemorySummary { get; init; } = string.Empty;
    public CorrelationAssessment MemoryAssessment { get; init; } = CorrelationAssessment.Unknown;

    // GC Correlation
    public double Gen2CollectionsDelta { get; init; }
    public string GcSummary { get; init; } = string.Empty;
    public CorrelationAssessment GcAssessment { get; init; } = CorrelationAssessment.Unknown;

    // Scheduled Tasks
    public List<string> CorrelatedScheduledTasks { get; init; } = new();
    public CorrelationAssessment ScheduledTaskAssessment { get; init; } = CorrelationAssessment.Unknown;

    // Antivirus / EDR
    public double SecurityProcessPeakCpu { get; init; }
    public string SecuritySummary { get; init; } = string.Empty;
    public CorrelationAssessment SecurityAssessment { get; init; } = CorrelationAssessment.Unknown;

    // Associated websites
    public List<string> AffectedWebsites { get; init; } = new();
}
