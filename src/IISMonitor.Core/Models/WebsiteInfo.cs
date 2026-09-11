namespace IISMonitor.Core.Models;

/// <summary>
/// Configuration and binding metadata for an IIS Website.
/// </summary>
public record WebsiteInfo
{
    public long SiteId { get; init; }
    public string SiteName { get; init; } = string.Empty;
    public string AppPoolName { get; init; } = string.Empty;
    public string PhysicalPath { get; init; } = string.Empty;
    public string State { get; init; } = "Started";
    public List<SiteBindingInfo> Bindings { get; init; } = new();
    public List<SiteApplicationInfo> Applications { get; init; } = new();
}

public record SiteBindingInfo
{
    public string Protocol { get; init; } = "http";
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 80;
    public string BindingInformation { get; init; } = string.Empty;
}

public record SiteApplicationInfo
{
    public string Path { get; init; } = "/";
    public string AppPoolName { get; init; } = string.Empty;
    public string PhysicalPath { get; init; } = string.Empty;
}
