namespace IISMonitor.Core.Configuration;

/// <summary>
/// Configuration for CPU monitoring thresholds and intervals per Sections 4, 21, 22.
/// </summary>
public class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public int NormalIntervalSeconds { get; set; } = 30;
    public int IncidentIntervalSeconds { get; set; } = 5;

    public double CpuThresholdPercent { get; set; } = 70.0;
    public double CriticalCpuThresholdPercent { get; set; } = 90.0;
    public double RecoveryThresholdPercent { get; set; } = 60.0;
    public int RecoveryConsecutiveSamples { get; set; } = 3;

    public int BaselineRetentionMinutes { get; set; } = 60;
    public int HighDetailRetentionDays { get; set; } = 30;
}
