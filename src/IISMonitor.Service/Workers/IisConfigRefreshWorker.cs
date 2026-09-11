using System.Text.Json;
using IISMonitor.Core.Interfaces;
using IISMonitor.Data.Context;
using IISMonitor.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Service.Workers;

/// <summary>
/// Background worker that periodically refreshes IIS site and application pool configurations per Section 39.
/// </summary>
public class IisConfigRefreshWorker : BackgroundService
{
    private readonly IIisMonitor _iisMonitor;
    private readonly IDbContextFactory<IISMonitorDbContext> _contextFactory;
    private readonly ILogger<IisConfigRefreshWorker> _logger;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    public IisConfigRefreshWorker(
        IIisMonitor iisMonitor,
        IDbContextFactory<IISMonitorDbContext> contextFactory,
        ILogger<IisConfigRefreshWorker> logger)
    {
        _iisMonitor = iisMonitor ?? throw new ArgumentNullException(nameof(iisMonitor));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IIS Configuration Refresh Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAndPersistConfigAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh IIS configuration.");
            }

            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshAndPersistConfigAsync(CancellationToken ct)
    {
        var pools = await _iisMonitor.GetAppPoolsAsync(ct);
        var sites = await _iisMonitor.GetSitesAsync(ct);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Update application pools
        foreach (var p in pools)
        {
            var existing = await context.ApplicationPools.FirstOrDefaultAsync(a => a.Name == p.Name, ct);
            if (existing == null)
            {
                context.ApplicationPools.Add(new ApplicationPoolEntity
                {
                    Name = p.Name,
                    State = p.State,
                    ManagedRuntimeVersion = p.ManagedRuntimeVersion,
                    ManagedPipelineMode = p.ManagedPipelineMode,
                    StartMode = p.StartMode,
                    IdentityType = p.IdentityType,
                    LastDiscoveredUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.State = p.State;
                existing.LastDiscoveredUtc = DateTime.UtcNow;
            }
        }

        // Update sites
        foreach (var s in sites)
        {
            var existing = await context.Websites.FirstOrDefaultAsync(w => w.SiteId == s.SiteId, ct);
            if (existing == null)
            {
                context.Websites.Add(new WebsiteEntity
                {
                    SiteId = s.SiteId,
                    SiteName = s.SiteName,
                    AppPoolName = s.AppPoolName,
                    PhysicalPath = s.PhysicalPath,
                    State = s.State,
                    BindingsJson = JsonSerializer.Serialize(s.Bindings),
                    ApplicationsJson = JsonSerializer.Serialize(s.Applications),
                    LastDiscoveredUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.SiteName = s.SiteName;
                existing.AppPoolName = s.AppPoolName;
                existing.PhysicalPath = s.PhysicalPath;
                existing.State = s.State;
                existing.BindingsJson = JsonSerializer.Serialize(s.Bindings);
                existing.ApplicationsJson = JsonSerializer.Serialize(s.Applications);
                existing.LastDiscoveredUtc = DateTime.UtcNow;
            }
        }

        await context.SaveChangesAsync(ct);
        _logger.LogDebug("Persisted IIS config: {PoolCount} pools, {SiteCount} sites.", pools.Count, sites.Count);
    }
}
