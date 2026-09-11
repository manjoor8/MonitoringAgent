using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IISMonitor.Core.Enums;

namespace IISMonitor.Data.Entities;

[Table("Servers")]
public class ServerEntity
{
    [Key]
    public string ServerId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string Environment { get; set; } = "Production";
    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}

[Table("Incidents")]
public class IncidentEntity
{
    [Key]
    public string IncidentId { get; set; } = string.Empty; // e.g. INC-20260911-001
    public string ServerName { get; set; } = string.Empty;
    public DateTime StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }
    public double DurationSeconds { get; set; }
    public double PeakCpu { get; set; }
    public double AverageCpu { get; set; }
    public double TriggerThreshold { get; set; }
    public int SampleCount { get; set; }
    public int AffectedProcessCount { get; set; }
    public int AffectedAppPoolCount { get; set; }
    public string Status { get; set; } = "Active"; // Active, Recovered, AutoRecovered
    public IncidentClassification Classification { get; set; } = IncidentClassification.UnknownCpuSource;

    public string? TopProcessName { get; set; }
    public int? TopProcessId { get; set; }
    public double TopProcessPeakCpu { get; set; }

    public string? TopAppPoolName { get; set; }
    public double TopAppPoolPeakCpu { get; set; }

    public string? FingerprintHash { get; set; }
    public string? RootCauseSummaryJson { get; set; }
}

[Table("MetricSamples")]
public class MetricSampleEntity
{
    [Key]
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IncidentId { get; set; }
    public double TotalCpu { get; set; }
    public double UserCpu { get; set; }
    public double PrivilegedCpu { get; set; }
    public double ProcessorQueueLength { get; set; }
    public double AvailableMemoryMb { get; set; }
    public double CommittedMemoryPercent { get; set; }
    public bool IsBaseline { get; set; }
    public bool IsHighDetail { get; set; }
}

[Table("ProcessSamples")]
public class ProcessSampleEntity
{
    [Key]
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IncidentId { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public double CpuTimeMs { get; set; }
    public long PrivateBytes { get; set; }
    public long WorkingSet { get; set; }
    public long VirtualMemory { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public DateTime? StartTimeUtc { get; set; }
    public double UptimeSeconds { get; set; }
    public int? ParentProcessId { get; set; }
}
