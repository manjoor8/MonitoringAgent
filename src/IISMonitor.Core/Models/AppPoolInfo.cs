namespace IISMonitor.Core.Models;

/// <summary>
/// Static and state configuration for an IIS Application Pool.
/// </summary>
public record AppPoolInfo
{
    public string Name { get; init; } = string.Empty;
    public string State { get; init; } = "Unknown";
    public string ManagedRuntimeVersion { get; init; } = string.Empty;
    public string ManagedPipelineMode { get; init; } = string.Empty;
    public string StartMode { get; init; } = string.Empty;
    public string IdentityType { get; init; } = string.Empty;
    public bool AutoStart { get; init; } = true;
    public List<int> WorkerProcessIds { get; init; } = new();
}
