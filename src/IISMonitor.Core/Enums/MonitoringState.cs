namespace IISMonitor.Core.Enums;

/// <summary>
/// States for the CPU monitoring state machine.
/// </summary>
public enum MonitoringState
{
    /// <summary>
    /// Normal operating condition (CPU < 70%). Sampling interval: default 30s.
    /// </summary>
    Normal,

    /// <summary>
    /// Threshold exceeded (CPU >= 70%), initiating incident tracking.
    /// </summary>
    IncidentStarting,

    /// <summary>
    /// Confirmed incident. High-frequency sampling (default 5s) active.
    /// </summary>
    HighDetail,

    /// <summary>
    /// Sustained or critical CPU (>= 90%). Maximum diagnostic capture.
    /// </summary>
    Critical,

    /// <summary>
    /// CPU dropped below recovery threshold (e.g. < 60%), verifying hysteresis.
    /// </summary>
    Recovery
}
