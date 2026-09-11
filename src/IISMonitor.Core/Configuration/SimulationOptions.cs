namespace IISMonitor.Core.Configuration;

/// <summary>
/// Simulation mode configuration per Section 46.
/// Enables testing without causing an actual CPU incident on IIS.
/// </summary>
public class SimulationOptions
{
    public const string SectionName = "Simulation";

    public bool Enabled { get; set; } = false;
    public bool GenerateCpuSpike { get; set; } = true;
    public int SpikeAfterSeconds { get; set; } = 30;
    public int SpikeDurationSeconds { get; set; } = 120;
    public double BaselineCpuPercent { get; set; } = 42.0;
    public double PeakCpuPercent { get; set; } = 98.0;
    public string CulpritProcessName { get; set; } = "w3wp.exe";
    public string CulpritAppPool { get; set; } = "PatientPortalPool";
    public int CulpritPid { get; set; } = 15432;
}
