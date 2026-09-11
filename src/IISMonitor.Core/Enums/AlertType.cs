namespace IISMonitor.Core.Enums;

/// <summary>
/// Types of notifications/alerts sent by the monitoring agent per Section 35.
/// </summary>
public enum AlertType
{
    IncidentStarted,
    CriticalCpu,
    IncidentRecovered,
    AutoRecoveryPerformed,
    AutoRecoveryDisabled
}
