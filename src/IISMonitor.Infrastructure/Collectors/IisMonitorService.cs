using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Linq;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Web.Administration;

namespace IISMonitor.Infrastructure.Collectors;

/// <summary>
/// IIS discovery and mapping service using Microsoft.Web.Administration and appcmd fallback per Sections 8, 9, 10, 57.
/// </summary>
public class IisMonitorService : IIisMonitor
{
    private readonly ILogger<IisMonitorService> _logger;

    private readonly object _cacheLock = new();
    private DateTime _lastConfigRefreshUtc = DateTime.MinValue;
    private static readonly TimeSpan ConfigCacheDuration = TimeSpan.FromMinutes(5);

    private List<AppPoolInfo> _cachedAppPools = new();
    private List<WebsiteInfo> _cachedSites = new();
    private readonly List<ProcessMapping> _historicalMappings = new();
    private bool _hasLoggedAccessWarning;

    public IisMonitorService(ILogger<IisMonitorService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<AppPoolInfo>> GetAppPoolsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConfigRefreshedAsync(cancellationToken);
        lock (_cacheLock)
        {
            return _cachedAppPools.ToList();
        }
    }

    public async Task<IReadOnlyList<WebsiteInfo>> GetSitesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConfigRefreshedAsync(cancellationToken);
        lock (_cacheLock)
        {
            return _cachedSites.ToList();
        }
    }

    public async Task<IReadOnlyList<ProcessMapping>> GetWorkerProcessMappingsAsync(CancellationToken cancellationToken = default)
    {
        var liveMappings = new List<ProcessMapping>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Array.Empty<ProcessMapping>();
        }

        try
        {
            // Primary approach: ServerManager.WorkerProcesses
            using var serverManager = new ServerManager();
            foreach (var wp in serverManager.WorkerProcesses)
            {
                int pid = wp.ProcessId;
                string appPool = wp.AppPoolName;

                // Find associated websites
                var sites = await GetWebsitesForAppPoolAsync(appPool, cancellationToken);

                var mapping = new ProcessMapping
                {
                    ProcessId = pid,
                    AppPoolName = appPool,
                    StartTimeUtc = DateTime.UtcNow,
                    AssociatedWebsites = sites.Select(s => s.SiteName).ToList()
                };

                liveMappings.Add(mapping);
                UpdateHistoricalMapping(mapping);
            }
        }
        catch (UnauthorizedAccessException)
        {
            if (!_hasLoggedAccessWarning)
            {
                _hasLoggedAccessWarning = true;
                _logger.LogWarning("Access denied reading IIS worker processes (redirection.config). " +
                                   "IIS configuration requires administrative privileges. Run as Administrator, " +
                                   "install as a Windows Service (LocalSystem), or enable Simulation mode in appsettings.json for local testing.");
            }

            // Fallback approach: appcmd list wp /xml
            try
            {
                var appCmdMappings = await GetWorkerProcessesViaAppCmdAsync(cancellationToken);
                foreach (var (pid, appPool) in appCmdMappings)
                {
                    var sites = await GetWebsitesForAppPoolAsync(appPool, cancellationToken);
                    var mapping = new ProcessMapping
                    {
                        ProcessId = pid,
                        AppPoolName = appPool,
                        StartTimeUtc = DateTime.UtcNow,
                        AssociatedWebsites = sites.Select(s => s.SiteName).ToList()
                    };
                    liveMappings.Add(mapping);
                    UpdateHistoricalMapping(mapping);
                }
            }
            catch
            {
                // appcmd also failed (e.g. WAS stopped or unelevated)
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not enumerate IIS worker processes via ServerManager: {Message}. Attempting appcmd fallback.", ex.Message);

            // Fallback approach: appcmd list wp /xml
            try
            {
                var appCmdMappings = await GetWorkerProcessesViaAppCmdAsync(cancellationToken);
                foreach (var (pid, appPool) in appCmdMappings)
                {
                    var sites = await GetWebsitesForAppPoolAsync(appPool, cancellationToken);
                    var mapping = new ProcessMapping
                    {
                        ProcessId = pid,
                        AppPoolName = appPool,
                        StartTimeUtc = DateTime.UtcNow,
                        AssociatedWebsites = sites.Select(s => s.SiteName).ToList()
                    };
                    liveMappings.Add(mapping);
                    UpdateHistoricalMapping(mapping);
                }
            }
            catch (Exception fallbackEx)
            {
                _logger.LogDebug(fallbackEx, "Failed to read worker processes via appcmd fallback.");
            }
        }

        return liveMappings;
    }

    public Task<bool> RecycleAppPoolAsync(string appPoolName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appPoolName))
            throw new ArgumentException("Application Pool name cannot be empty.", nameof(appPoolName));

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("Recycle requested for pool {Name}, but platform is not Windows.", appPoolName);
            return Task.FromResult(false);
        }

        try
        {
            using var serverManager = new ServerManager();
            var pool = serverManager.ApplicationPools.FirstOrDefault(p =>
                string.Equals(p.Name, appPoolName, StringComparison.OrdinalIgnoreCase));

            if (pool == null)
            {
                _logger.LogWarning("Application Pool '{Name}' not found for recycle.", appPoolName);
                return Task.FromResult(false);
            }

            _logger.LogWarning("Initiating graceful recycle of Application Pool '{Name}'.", appPoolName);
            var state = pool.Recycle();
            _logger.LogInformation("Recycle command issued for '{Name}'. State: {State}", appPoolName, state);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recycle Application Pool '{Name}'.", appPoolName);
            return Task.FromResult(false);
        }
    }

    private async Task EnsureConfigRefreshedAsync(CancellationToken ct)
    {
        bool needRefresh;
        lock (_cacheLock)
        {
            needRefresh = DateTime.UtcNow - _lastConfigRefreshUtc > ConfigCacheDuration || _cachedSites.Count == 0;
        }

        if (!needRefresh) return;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        await Task.Run(() =>
        {
            try
            {
                using var serverManager = new ServerManager();

                var pools = new List<AppPoolInfo>();
                foreach (var p in serverManager.ApplicationPools)
                {
                    pools.Add(new AppPoolInfo
                    {
                        Name = p.Name,
                        State = p.State.ToString(),
                        ManagedRuntimeVersion = p.ManagedRuntimeVersion ?? string.Empty,
                        ManagedPipelineMode = p.ManagedPipelineMode.ToString(),
                        StartMode = p.StartMode.ToString(),
                        AutoStart = p.AutoStart,
                        WorkerProcessIds = p.WorkerProcesses.Select(w => w.ProcessId).ToList()
                    });
                }

                var sites = new List<WebsiteInfo>();
                foreach (var s in serverManager.Sites)
                {
                    var bindings = s.Bindings.Select(b => new SiteBindingInfo
                    {
                        Protocol = b.Protocol,
                        Host = b.Host,
                        Port = b.EndPoint?.Port ?? 80,
                        BindingInformation = b.BindingInformation
                    }).ToList();

                    var apps = s.Applications.Select(a => new SiteApplicationInfo
                    {
                        Path = a.Path,
                        AppPoolName = a.ApplicationPoolName,
                        PhysicalPath = a.VirtualDirectories.FirstOrDefault()?.PhysicalPath ?? string.Empty
                    }).ToList();

                    var rootApp = s.Applications.FirstOrDefault(a => a.Path == "/");

                    sites.Add(new WebsiteInfo
                    {
                        SiteId = s.Id,
                        SiteName = s.Name,
                        AppPoolName = rootApp?.ApplicationPoolName ?? string.Empty,
                        PhysicalPath = rootApp?.VirtualDirectories.FirstOrDefault()?.PhysicalPath ?? string.Empty,
                        State = s.State.ToString(),
                        Bindings = bindings,
                        Applications = apps
                    });
                }

                lock (_cacheLock)
                {
                    _cachedAppPools = pools;
                    _cachedSites = sites;
                    _lastConfigRefreshUtc = DateTime.UtcNow;
                }

                _logger.LogDebug("Refreshed IIS configuration: {PoolCount} pools, {SiteCount} sites.", pools.Count, sites.Count);
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException && !_hasLoggedAccessWarning)
                {
                    _hasLoggedAccessWarning = true;
                    _logger.LogWarning("Access denied reading IIS configuration files (redirection.config). " +
                                       "IIS configuration requires administrative privileges. Falling back to appcmd.");
                }
                else if (ex is not UnauthorizedAccessException)
                {
                    _logger.LogWarning("Could not refresh IIS configuration from ServerManager: {Message}. Attempting appcmd fallback.", ex.Message);
                }

                // Fallback to appcmd
                try
                {
                    var fallbackPools = GetAppPoolsViaAppCmdAsync(ct).GetAwaiter().GetResult();
                    var fallbackSites = GetSitesViaAppCmdAsync(ct).GetAwaiter().GetResult();

                    if (fallbackPools.Count > 0 || fallbackSites.Count > 0)
                    {
                        lock (_cacheLock)
                        {
                            _cachedAppPools = fallbackPools;
                            _cachedSites = fallbackSites;
                            _lastConfigRefreshUtc = DateTime.UtcNow;
                        }
                        _logger.LogInformation("Refreshed IIS configuration via appcmd fallback: {PoolCount} pools, {SiteCount} sites.",
                            fallbackPools.Count, fallbackSites.Count);
                    }
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogDebug(fallbackEx, "Failed to refresh IIS configuration via appcmd fallback.");
                }
            }
        }, ct);
    }

    private async Task<IReadOnlyList<WebsiteInfo>> GetWebsitesForAppPoolAsync(string appPoolName, CancellationToken ct)
    {
        var allSites = await GetSitesAsync(ct);
        return allSites.Where(s =>
            string.Equals(s.AppPoolName, appPoolName, StringComparison.OrdinalIgnoreCase) ||
            s.Applications.Any(a => string.Equals(a.AppPoolName, appPoolName, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    private void UpdateHistoricalMapping(ProcessMapping mapping)
    {
        lock (_historicalMappings)
        {
            // Close any older mapping for the same app pool if PID changed
            var previous = _historicalMappings.FirstOrDefault(m =>
                m.AppPoolName == mapping.AppPoolName && m.ProcessId != mapping.ProcessId && m.EndTimeUtc == null);

            if (previous != null)
            {
                int index = _historicalMappings.IndexOf(previous);
                _historicalMappings[index] = previous with { EndTimeUtc = DateTime.UtcNow };
                _logger.LogInformation("Detected AppPool recycle: '{Pool}' changed from PID {OldPid} to {NewPid}.",
                    mapping.AppPoolName, previous.ProcessId, mapping.ProcessId);
            }

            if (!_historicalMappings.Any(m => m.ProcessId == mapping.ProcessId))
            {
                _historicalMappings.Add(mapping);
            }
        }
    }

    private static string? FindAppCmdPath()
    {
        string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string path = Path.Combine(system32, "inetsrv", "appcmd.exe");
        if (File.Exists(path)) return path;

        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sysnative = Path.Combine(winDir, "sysnative", "inetsrv", "appcmd.exe");
        if (File.Exists(sysnative)) return sysnative;

        return null;
    }

    private static async Task<List<(int Pid, string AppPool)>> GetWorkerProcessesViaAppCmdAsync(CancellationToken ct)
    {
        var list = new List<(int, string)>();
        string? appCmdPath = FindAppCmdPath();
        if (appCmdPath == null) return list;

        var psi = new ProcessStartInfo
        {
            FileName = appCmdPath,
            Arguments = "list wp /xml",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return list;

        string xml = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (!string.IsNullOrWhiteSpace(xml))
        {
            var doc = XDocument.Parse(xml);
            foreach (var wp in doc.Descendants("WP"))
            {
                string? pidStr = wp.Attribute("WP.NAME")?.Value;
                string? appPool = wp.Attribute("APPPOOL.NAME")?.Value;

                if (int.TryParse(pidStr, out int pid) && !string.IsNullOrEmpty(appPool))
                {
                    list.Add((pid, appPool));
                }
            }
        }

        return list;
    }

    private static async Task<List<AppPoolInfo>> GetAppPoolsViaAppCmdAsync(CancellationToken ct)
    {
        var list = new List<AppPoolInfo>();
        string? appCmdPath = FindAppCmdPath();
        if (appCmdPath == null) return list;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = appCmdPath,
                Arguments = "list apppool /xml",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return list;
            string xml = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (!string.IsNullOrWhiteSpace(xml))
            {
                var doc = XDocument.Parse(xml);
                foreach (var el in doc.Descendants("APPPOOL"))
                {
                    string name = el.Attribute("APPPOOL.NAME")?.Value ?? string.Empty;
                    string state = el.Attribute("state")?.Value ?? el.Attribute("State")?.Value ?? "Started";
                    if (!string.IsNullOrEmpty(name))
                    {
                        list.Add(new AppPoolInfo
                        {
                            Name = name,
                            State = state
                        });
                    }
                }
            }
        }
        catch { }

        return list;
    }

    private static async Task<List<WebsiteInfo>> GetSitesViaAppCmdAsync(CancellationToken ct)
    {
        var list = new List<WebsiteInfo>();
        string? appCmdPath = FindAppCmdPath();
        if (appCmdPath == null) return list;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = appCmdPath,
                Arguments = "list site /xml",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return list;
            string xml = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (!string.IsNullOrWhiteSpace(xml))
            {
                var doc = XDocument.Parse(xml);
                foreach (var el in doc.Descendants("SITE"))
                {
                    string name = el.Attribute("SITE.NAME")?.Value ?? string.Empty;
                    string idStr = el.Attribute("SITE.ID")?.Value ?? "0";
                    long.TryParse(idStr, out long siteId);
                    if (!string.IsNullOrEmpty(name))
                    {
                        list.Add(new WebsiteInfo
                        {
                            SiteId = siteId,
                            SiteName = name,
                            State = el.Attribute("state")?.Value ?? el.Attribute("State")?.Value ?? "Started"
                        });
                    }
                }
            }
        }
        catch { }

        return list;
    }
}
