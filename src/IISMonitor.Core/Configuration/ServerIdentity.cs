namespace IISMonitor.Core.Configuration;

/// <summary>
/// Server identity metadata for multi-server correlation per Section 41.
/// </summary>
public class ServerIdentity
{
    public const string SectionName = "ServerIdentity";

    public string MachineName { get; set; } = Environment.MachineName;
    public string EnvironmentName { get; set; } = "Production";
    public string ServerId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}
