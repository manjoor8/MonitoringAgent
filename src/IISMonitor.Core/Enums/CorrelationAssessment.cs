namespace IISMonitor.Core.Enums;

/// <summary>
/// Root cause correlation assessment levels per Section 33.
/// </summary>
public enum CorrelationAssessment
{
    StrongCorrelation,
    Possible,
    Unlikely,
    NoEvidence,
    Unknown
}
