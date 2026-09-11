namespace IISMonitor.Core.Configuration;

/// <summary>
/// Alerting settings for Windows Event Log and Email per Section 35.
/// </summary>
public class AlertingOptions
{
    public const string SectionName = "Alerting";

    public bool WindowsEventLogEnabled { get; set; } = true;
    public string EventLogSource { get; set; } = "IISMonitorAgent";

    public bool EmailEnabled { get; set; } = false;
    public string SmtpServer { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 25;
    public bool EnableSsl { get; set; } = false;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string FromAddress { get; set; } = "iismonitor@example.com";
    public List<string> ToAddresses { get; set; } = new();
    public string DashboardBaseUrl { get; set; } = "http://localhost:5050";
}
