namespace IISMonitor.Core.Enums;

/// <summary>
/// Incident classification categories per Section 67.
/// </summary>
public enum IncidentClassification
{
    NormalRecovery,
    AutomaticRecovery,
    ManualRecovery,
    MultipleCpuConsumers,
    NonIisCpu,
    UnknownCpuSource
}
