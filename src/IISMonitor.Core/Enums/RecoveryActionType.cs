namespace IISMonitor.Core.Enums;

/// <summary>
/// Types of automated recovery actions.
/// </summary>
public enum RecoveryActionType
{
    None,
    AppPoolRecycle,
    ForcedTermination
}
