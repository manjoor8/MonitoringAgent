namespace IISMonitor.Core.Configuration;

/// <summary>
/// Diagnostics and ProcDump configuration per Section 15.
/// </summary>
public class DiagnosticsOptions
{
    public const string SectionName = "Diagnostics";

    public bool ProcDumpEnabled { get; set; } = false;
    public string ProcDumpPath { get; set; } = "C:\\Tools\\procdump.exe";
    public string DumpDirectory { get; set; } = "C:\\ProgramData\\IISMonitor\\Dumps";
    public double TriggerCpuPercent { get; set; } = 80.0;
    public int TriggerDurationSeconds { get; set; } = 30;
    public int MaxDumpsPerIncident { get; set; } = 3;
    public string DumpType { get; set; } = "Full"; // Mini, Full
    public long MinFreeDiskSpaceBytes { get; set; } = 5L * 1024 * 1024 * 1024; // 5 GB safety limit
}
