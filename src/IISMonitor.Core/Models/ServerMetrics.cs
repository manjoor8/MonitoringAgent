namespace IISMonitor.Core.Models;

/// <summary>
/// Server-level performance and resource utilization metrics.
/// </summary>
public record ServerMetrics
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public double TotalCpuPercent { get; init; }
    public double UserCpuPercent { get; init; }
    public double PrivilegedCpuPercent { get; init; }
    public double ProcessorQueueLength { get; init; }
    public double AvailableMemoryMb { get; init; }
    public double CommittedMemoryPercent { get; init; }
    public int LogicalProcessorCount { get; init; } = Environment.ProcessorCount;
    public double[]? PerCoreCpuPercents { get; init; }
}
