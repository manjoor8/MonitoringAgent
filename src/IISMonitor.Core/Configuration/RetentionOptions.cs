namespace IISMonitor.Core.Configuration;

/// <summary>
/// Data retention configuration per Section 24.
/// </summary>
public class RetentionOptions
{
    public const string SectionName = "Retention";

    public int BaselineDays { get; set; } = 7;
    public int IncidentDays { get; set; } = 90;
    public int WindowsEventsDays { get; set; } = 90;
    public int DiagnosticMetadataDays { get; set; } = 90;
    public int DumpDays { get; set; } = 30;
}
