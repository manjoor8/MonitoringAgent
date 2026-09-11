namespace IISMonitor.Core.Models;

/// <summary>
/// Execution metrics collected for an IIS Application Pool worker process.
/// </summary>
public record AppPoolMetrics
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string AppPoolName { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public double CpuPercent { get; init; }
    public double TotalProcessorTimeMs { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public long WorkingSetBytes { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public DateTime? ProcessStartTimeUtc { get; init; }
    public double ProcessAgeSeconds { get; init; }
    public long RequestCount { get; init; }
    public int QueueLength { get; init; }
    public string State { get; init; } = "Started";
}
