namespace IISMonitor.Core.Models;

/// <summary>
/// Process-level resource and execution metrics.
/// </summary>
public record ProcessMetrics
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public double CpuPercent { get; init; }
    public double TotalProcessorTimeMs { get; init; }
    public long PrivateBytes { get; init; }
    public long WorkingSet { get; init; }
    public long VirtualMemory { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public DateTime? StartTimeUtc { get; init; }
    public double UptimeSeconds { get; init; }
    public int? ParentProcessId { get; init; }
    public bool IsW3wp => string.Equals(ProcessName, "w3wp", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(ProcessName, "w3wp.exe", StringComparison.OrdinalIgnoreCase);
    public bool IsSecurityProcess => string.Equals(ProcessName, "MsMpEng", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(ProcessName, "MsMpEng.exe", StringComparison.OrdinalIgnoreCase) ||
                                     ProcessName.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
                                     ProcessName.Contains("EDR", StringComparison.OrdinalIgnoreCase);
}
