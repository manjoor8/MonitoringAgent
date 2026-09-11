using IISMonitor.Core.Configuration;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Simulation;

public class SimulatedServerMetricsCollector : IServerMetricsCollector
{
    private readonly SimulationOptions _options;
    private readonly DateTime _startTimeUtc = DateTime.UtcNow;

    public SimulatedServerMetricsCollector(SimulationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<ServerMetrics> CollectAsync(CancellationToken cancellationToken = default)
    {
        double elapsedSeconds = (DateTime.UtcNow - _startTimeUtc).TotalSeconds;
        double cpu = _options.BaselineCpuPercent;

        if (_options.GenerateCpuSpike)
        {
            double spikeStart = _options.SpikeAfterSeconds;
            double spikeEnd = spikeStart + _options.SpikeDurationSeconds;

            if (elapsedSeconds >= spikeStart && elapsedSeconds <= spikeEnd)
            {
                // Smooth bell curve transition to peak
                double progress = (elapsedSeconds - spikeStart) / _options.SpikeDurationSeconds;
                double factor = Math.Sin(progress * Math.PI); // 0 -> 1 -> 0
                cpu = _options.BaselineCpuPercent + (_options.PeakCpuPercent - _options.BaselineCpuPercent) * factor;
            }
        }

        // Add subtle jitter
        double jitter = (Random.Shared.NextDouble() * 4.0) - 2.0;
        cpu = Math.Clamp(cpu + jitter, 10.0, 100.0);

        return Task.FromResult(new ServerMetrics
        {
            TimestampUtc = DateTime.UtcNow,
            TotalCpuPercent = Math.Round(cpu, 1),
            UserCpuPercent = Math.Round(cpu * 0.85, 1),
            PrivilegedCpuPercent = Math.Round(cpu * 0.15, 1),
            ProcessorQueueLength = cpu > 80 ? 8 : 1,
            AvailableMemoryMb = 8192 - (cpu > 70 ? 2048 : 512),
            CommittedMemoryPercent = cpu > 70 ? 75 : 45,
            LogicalProcessorCount = Environment.ProcessorCount
        });
    }
}

public class SimulatedProcessMetricsCollector : IProcessMetricsCollector
{
    private readonly SimulationOptions _options;
    private readonly SimulatedServerMetricsCollector _serverCollector;

    public SimulatedProcessMetricsCollector(SimulationOptions options, SimulatedServerMetricsCollector serverCollector)
    {
        _options = options;
        _serverCollector = serverCollector;
    }

    public async Task<IReadOnlyList<ProcessMetrics>> CollectAsync(CancellationToken cancellationToken = default)
    {
        var serverMetrics = await _serverCollector.CollectAsync(cancellationToken);
        double serverCpu = serverMetrics.TotalCpuPercent;

        var list = new List<ProcessMetrics>();

        if (serverCpu >= 70.0)
        {
            // Culprit process consumes most CPU
            double culpritCpu = Math.Round(serverCpu * 0.90, 1);
            list.Add(new ProcessMetrics
            {
                ProcessId = _options.CulpritPid,
                ProcessName = _options.CulpritProcessName,
                CpuPercent = culpritCpu,
                PrivateBytes = 3L * 1024 * 1024 * 1024,
                WorkingSet = 2L * 1024 * 1024 * 1024,
                ThreadCount = 85,
                HandleCount = 3200,
                StartTimeUtc = DateTime.UtcNow.AddHours(-12),
                UptimeSeconds = 43200
            });
        }
        else
        {
            list.Add(new ProcessMetrics
            {
                ProcessId = _options.CulpritPid,
                ProcessName = _options.CulpritProcessName,
                CpuPercent = 4.2,
                PrivateBytes = 1L * 1024 * 1024 * 1024,
                WorkingSet = 800L * 1024 * 1024,
                ThreadCount = 32,
                HandleCount = 1400,
                StartTimeUtc = DateTime.UtcNow.AddHours(-12),
                UptimeSeconds = 43200
            });
        }

        // Microsoft Defender background process
        list.Add(new ProcessMetrics
        {
            ProcessId = 4112,
            ProcessName = "MsMpEng.exe",
            CpuPercent = 2.1,
            PrivateBytes = 300L * 1024 * 1024,
            WorkingSet = 250L * 1024 * 1024,
            ThreadCount = 28,
            HandleCount = 980
        });

        // Other background process
        list.Add(new ProcessMetrics
        {
            ProcessId = 1100,
            ProcessName = "sqlservr.exe",
            CpuPercent = 1.8,
            PrivateBytes = 500L * 1024 * 1024,
            WorkingSet = 450L * 1024 * 1024,
            ThreadCount = 40,
            HandleCount = 1200
        });

        return list;
    }
}

public class SimulatedIisMonitor : IIisMonitor
{
    private readonly SimulationOptions _options;
    private readonly ILogger<SimulatedIisMonitor> _logger;

    public SimulatedIisMonitor(SimulationOptions options, ILogger<SimulatedIisMonitor> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<IReadOnlyList<AppPoolInfo>> GetAppPoolsAsync(CancellationToken cancellationToken = default)
    {
        var pools = new List<AppPoolInfo>
        {
            new() { Name = _options.CulpritAppPool, State = "Started", ManagedRuntimeVersion = "v4.0", WorkerProcessIds = new() { _options.CulpritPid } },
            new() { Name = "ReportingServicePool", State = "Started", ManagedRuntimeVersion = "v4.0", WorkerProcessIds = new() { 15120 } },
            new() { Name = "AdminPortalPool", State = "Started", ManagedRuntimeVersion = "v4.0", WorkerProcessIds = new() { 15200 } }
        };
        return Task.FromResult<IReadOnlyList<AppPoolInfo>>(pools);
    }

    public Task<IReadOnlyList<WebsiteInfo>> GetSitesAsync(CancellationToken cancellationToken = default)
    {
        var sites = new List<WebsiteInfo>
        {
            new()
            {
                SiteId = 1,
                SiteName = "PatientPortal",
                AppPoolName = _options.CulpritAppPool,
                PhysicalPath = "D:\\Websites\\PatientPortal",
                Bindings = new() { new() { Host = "patient.example.com", Port = 443, Protocol = "https" } }
            },
            new()
            {
                SiteId = 2,
                SiteName = "ReportingPortal",
                AppPoolName = "ReportingServicePool",
                PhysicalPath = "D:\\Websites\\Reporting",
                Bindings = new() { new() { Host = "reports.example.com", Port = 443, Protocol = "https" } }
            }
        };
        return Task.FromResult<IReadOnlyList<WebsiteInfo>>(sites);
    }

    public Task<IReadOnlyList<ProcessMapping>> GetWorkerProcessMappingsAsync(CancellationToken cancellationToken = default)
    {
        var mappings = new List<ProcessMapping>
        {
            new()
            {
                ProcessId = _options.CulpritPid,
                AppPoolName = _options.CulpritAppPool,
                StartTimeUtc = DateTime.UtcNow.AddHours(-12),
                AssociatedWebsites = new() { "PatientPortal" }
            },
            new()
            {
                ProcessId = 15120,
                AppPoolName = "ReportingServicePool",
                StartTimeUtc = DateTime.UtcNow.AddHours(-24),
                AssociatedWebsites = new() { "ReportingPortal" }
            }
        };
        return Task.FromResult<IReadOnlyList<ProcessMapping>>(mappings);
    }

    public Task<bool> RecycleAppPoolAsync(string appPoolName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATION] Gracefully recycled AppPool: {Pool}", appPoolName);
        return Task.FromResult(true);
    }
}
