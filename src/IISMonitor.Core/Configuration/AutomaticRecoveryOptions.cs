namespace IISMonitor.Core.Configuration;

/// <summary>
/// Automatic recovery configuration per Sections 51-64.
/// MUST BE DISABLED BY DEFAULT.
/// </summary>
public class AutomaticRecoveryOptions
{
    public const string SectionName = "AutomaticRecovery";

    /// <summary>
    /// Master toggle for automated recovery. MUST default to false.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Critical server CPU threshold to qualify for recovery (default 90%).
    /// </summary>
    public double CriticalCpuThresholdPercent { get; set; } = 90.0;

    /// <summary>
    /// Minimum continuous duration at critical CPU before eligible for restart (default 5 minutes).
    /// </summary>
    public int MinimumCriticalDurationMinutes { get; set; } = 5;

    /// <summary>
    /// Minimum percentage consumed specifically by culprit w3wp.exe (default 70%).
    /// </summary>
    public double MinimumCulpritCpuPercent { get; set; } = 70.0;

    /// <summary>
    /// Capture forensic snapshot prior to recycling pool.
    /// </summary>
    public bool CaptureDiagnosticBeforeRestart { get; set; } = true;

    /// <summary>
    /// Capture memory dump via ProcDump prior to recycling pool.
    /// </summary>
    public bool CaptureDumpBeforeRestart { get; set; } = true;

    /// <summary>
    /// Maximum dumps captured before recycling.
    /// </summary>
    public int MaxDumpsBeforeRestart { get; set; } = 1;

    /// <summary>
    /// Only recycle the culprit App Pool; NEVER restart W3SVC or server.
    /// </summary>
    public bool RestartOnlyApplicationPool { get; set; } = true;

    /// <summary>
    /// Allow forced kill of worker process (default false: prefer graceful recycle).
    /// </summary>
    public bool AllowForcedProcessTermination { get; set; } = false;

    /// <summary>
    /// Restart limit per hour per server.
    /// </summary>
    public int MaxRestartsPerHour { get; set; } = 1;

    /// <summary>
    /// Restart limit per day per server.
    /// </summary>
    public int MaxRestartsPerDay { get; set; } = 3;

    /// <summary>
    /// Cooldown window after recovery before another restart is permitted.
    /// </summary>
    public int CooldownMinutes { get; set; } = 30;

    /// <summary>
    /// Post-restart recovery verification intervals in seconds (e.g. 30, 60, 120, 300).
    /// </summary>
    public List<int> VerificationIntervalsSeconds { get; set; } = new() { 30, 60, 120, 300 };
}
