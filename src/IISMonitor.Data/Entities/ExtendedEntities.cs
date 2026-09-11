using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IISMonitor.Core.Enums;

namespace IISMonitor.Data.Entities;

[Table("ApplicationPools")]
public class ApplicationPoolEntity
{
    [Key]
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = "Started";
    public string ManagedRuntimeVersion { get; set; } = string.Empty;
    public string ManagedPipelineMode { get; set; } = string.Empty;
    public string StartMode { get; set; } = string.Empty;
    public string IdentityType { get; set; } = string.Empty;
    public DateTime LastDiscoveredUtc { get; set; } = DateTime.UtcNow;
}

[Table("ApplicationPoolSamples")]
public class ApplicationPoolSampleEntity
{
    [Key]
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IncidentId { get; set; }
    public string AppPoolName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public double CpuPercent { get; set; }
    public double CpuTimeMs { get; set; }
    public long PrivateMemory { get; set; }
    public long WorkingSet { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public double ProcessAgeSeconds { get; set; }
    public long RequestCount { get; set; }
    public int QueueLength { get; set; }
    public string State { get; set; } = "Started";
}

[Table("Websites")]
public class WebsiteEntity
{
    [Key]
    public long SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string AppPoolName { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public string State { get; set; } = "Started";
    public string BindingsJson { get; set; } = "[]";
    public string ApplicationsJson { get; set; } = "[]";
    public DateTime LastDiscoveredUtc { get; set; } = DateTime.UtcNow;
}

[Table("WebsiteMetrics")]
public class WebsiteMetricEntity
{
    [Key]
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IncidentId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public double RequestsPerSec { get; set; }
    public int ActiveRequests { get; set; }
    public int QueueLength { get; set; }
    public int Http500Count { get; set; }
    public int Http503Count { get; set; }
    public int Http404Count { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public double MaxResponseTimeMs { get; set; }
}

[Table("IisRequestMetrics")]
public class IisRequestMetricEntity
{
    [Key]
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IncidentId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public double MaxResponseTimeMs { get; set; }
    public int StatusCode { get; set; }
}

[Table("DotNetRuntimeMetrics")]
public class DotNetRuntimeMetricEntity
{
    [Key]
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IncidentId { get; set; }
    public int ProcessId { get; set; }
    public string AppPoolName { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public double MetricValue { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;
}

[Table("WindowsEvents")]
public class WindowsEventEntity
{
    [Key]
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IncidentId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public int EventId { get; set; }
    public string Level { get; set; } = "Information";
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

[Table("ScheduledTasks")]
public class ScheduledTaskEntity
{
    [Key]
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? IncidentId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string TaskPath { get; set; } = string.Empty;
    public DateTime? LastRunTime { get; set; }
    public DateTime? NextRunTime { get; set; }
    public int LastResult { get; set; }
    public string State { get; set; } = "Ready";
    public bool IsRunning { get; set; }
}

[Table("DiagnosticArtifacts")]
public class DiagnosticArtifactEntity
{
    [Key]
    public long Id { get; set; }
    public string IncidentId { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = "Dump"; // Dump, Log, Snapshot
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
}

[Table("ProcessMappings")]
public class ProcessMappingEntity
{
    [Key]
    public long Id { get; set; }
    public int ProcessId { get; set; }
    public string AppPoolName { get; set; } = string.Empty;
    public DateTime StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }
    public string WebsitesJson { get; set; } = "[]";
}

[Table("ConfigurationSnapshots")]
public class ConfigurationSnapshotEntity
{
    [Key]
    public long Id { get; set; }
    public string IncidentId { get; set; } = string.Empty;
    public DateTime SnapshotTimeUtc { get; set; } = DateTime.UtcNow;
    public string ConfigurationJson { get; set; } = string.Empty;
}

[Table("RecoveryActions")]
public class RecoveryActionEntity
{
    [Key]
    public long Id { get; set; }
    public string IncidentId { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string ServerName { get; set; } = string.Empty;
    public string AppPoolName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public double CpuBeforeRestart { get; set; }
    public double ServerCpuBeforeRestart { get; set; }
    public double CriticalDurationSeconds { get; set; }
    public RecoveryActionType Action { get; set; } = RecoveryActionType.AppPoolRecycle;
    public string Result { get; set; } = "Successful";
    public string? ErrorMessage { get; set; }
    public double? CpuAfter30s { get; set; }
    public double? CpuAfter60s { get; set; }
    public double? CpuAfter120s { get; set; }
    public double? CpuAfter300s { get; set; }
}

[Table("IncidentFingerprints")]
public class IncidentFingerprintEntity
{
    [Key]
    public long Id { get; set; }
    public string IncidentId { get; set; } = string.Empty;
    public string AppPoolName { get; set; } = string.Empty;
    public double PeakCpu { get; set; }
    public string CpuProfileBucketsJson { get; set; } = "[]";
    public double MemoryDeltaMb { get; set; }
    public double GcActivityScore { get; set; }
    public double RequestRatePerSec { get; set; }
    public string FingerprintHash { get; set; } = string.Empty;
}
