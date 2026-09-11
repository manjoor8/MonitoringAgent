namespace IISMonitor.Core.Models;

/// <summary>
/// Fingerprint representation of an incident for historical similarity comparison per Section 42.
/// </summary>
public record IncidentFingerprint
{
    public string IncidentId { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public string PrimaryAppPool { get; init; } = string.Empty;
    public double PeakCpu { get; init; }

    /// <summary>
    /// Resampled CPU trajectory buckets (normalized 0.0 - 1.0 across incident duration).
    /// </summary>
    public double[] CpuProfileBuckets { get; init; } = Array.Empty<double>();

    public double MemoryDeltaMb { get; init; }
    public double GcActivityScore { get; init; }
    public double RequestRatePerSec { get; init; }
    public bool HadScheduledTaskRunning { get; init; }
    public string FingerprintHash { get; init; } = string.Empty;
}

public record SimilarIncidentResult(
    string IncidentId,
    DateTime StartTimeUtc,
    double SimilarityScore,
    string AppPoolName,
    double PeakCpu);
