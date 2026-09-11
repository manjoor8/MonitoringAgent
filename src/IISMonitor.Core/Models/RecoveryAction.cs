using IISMonitor.Core.Enums;

namespace IISMonitor.Core.Models;

/// <summary>
/// Permanent audit record of an automatic or manual recovery attempt per Section 59.
/// </summary>
public record RecoveryAction
{
    public long Id { get; init; }
    public string IncidentId { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string ServerName { get; init; } = Environment.MachineName;
    public string AppPoolName { get; init; } = string.Empty;
    public int ProcessId { get; init; }

    public double CpuBeforeRestart { get; init; }
    public double ServerCpuBeforeRestart { get; init; }
    public double CriticalDurationSeconds { get; init; }

    public RecoveryActionType Action { get; init; } = RecoveryActionType.AppPoolRecycle;
    public string Result { get; init; } = "Pending"; // Successful, Failed, CooldownExceeded
    public string? ErrorMessage { get; init; }

    public double? CpuAfter30s { get; init; }
    public double? CpuAfter60s { get; init; }
    public double? CpuAfter120s { get; init; }
    public double? CpuAfter300s { get; init; }
}
