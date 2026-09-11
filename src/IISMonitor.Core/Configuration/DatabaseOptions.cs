namespace IISMonitor.Core.Configuration;

/// <summary>
/// Database connection configuration per Section 38.
/// </summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "Data Source=C:\\ProgramData\\IISMonitor\\monitor.db";
    public int BusyTimeoutMs { get; set; } = 5000;
}
