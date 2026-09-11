namespace IISMonitor.Core.Models;

/// <summary>
/// Composite monitoring sample captured at a single collection interval.
/// Used in the rolling baseline circular buffer and batched database writes.
/// </summary>
public record MonitoringSample
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string? IncidentId { get; init; }
    public bool IsBaseline { get; init; } = true;
    public bool IsHighDetail { get; init; } = false;

    public ServerMetrics ServerMetrics { get; init; } = new();
    public List<ProcessMetrics> TopProcesses { get; init; } = new();
    public List<AppPoolMetrics> AppPoolMetrics { get; init; } = new();
}
