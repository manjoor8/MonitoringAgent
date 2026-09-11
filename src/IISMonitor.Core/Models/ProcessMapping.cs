namespace IISMonitor.Core.Models;

/// <summary>
/// Historical and live mapping between PID, AppPool, and associated websites.
/// Preserves traceability across worker process recycles per Section 8.
/// </summary>
public record ProcessMapping
{
    public int ProcessId { get; init; }
    public string AppPoolName { get; init; } = string.Empty;
    public DateTime StartTimeUtc { get; init; } = DateTime.UtcNow;
    public DateTime? EndTimeUtc { get; init; }
    public List<string> AssociatedWebsites { get; init; } = new();
    public bool IsActive => EndTimeUtc == null;
}
