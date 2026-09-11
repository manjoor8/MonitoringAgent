using IISMonitor.Core.Configuration;
using IISMonitor.Data.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace IISMonitor.Web.Endpoints;

public record UpdateMonitoringRequest(
    int NormalIntervalSeconds,
    int IncidentIntervalSeconds,
    double CpuThresholdPercent,
    double CriticalCpuThresholdPercent,
    double RecoveryThresholdPercent,
    int RecoveryConsecutiveSamples);

public record UpdateRecoveryRequest(
    bool Enabled,
    double CriticalCpuThresholdPercent,
    int MinimumCriticalDurationMinutes,
    double MinimumCulpritCpuPercent,
    int MaxRestartsPerHour,
    int MaxRestartsPerDay,
    int CooldownMinutes,
    bool Confirmed);

public static class ConfigurationEndpoints
{
    public static void MapConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // Get all configurations
        group.MapGet("/config", (
            MonitoringOptions monitoring,
            AutomaticRecoveryOptions recovery,
            RetentionOptions retention,
            DiagnosticsOptions diagnostics,
            ServerIdentity server) =>
        {
            return Results.Ok(new
            {
                Server = server,
                Monitoring = monitoring,
                AutomaticRecovery = recovery,
                Retention = retention,
                Diagnostics = diagnostics
            });
        });

        // Update monitoring settings
        group.MapPut("/config/monitoring", (
            UpdateMonitoringRequest request,
            MonitoringOptions monitoring) =>
        {
            monitoring.NormalIntervalSeconds = Math.Clamp(request.NormalIntervalSeconds, 5, 300);
            monitoring.IncidentIntervalSeconds = Math.Clamp(request.IncidentIntervalSeconds, 1, 60);
            monitoring.CpuThresholdPercent = Math.Clamp(request.CpuThresholdPercent, 50.0, 95.0);
            monitoring.CriticalCpuThresholdPercent = Math.Clamp(request.CriticalCpuThresholdPercent, 80.0, 100.0);
            monitoring.RecoveryThresholdPercent = Math.Clamp(request.RecoveryThresholdPercent, 30.0, 80.0);
            monitoring.RecoveryConsecutiveSamples = Math.Clamp(request.RecoveryConsecutiveSamples, 1, 10);

            return Results.Ok(new { Success = true, Monitoring = monitoring });
        });

        // Update auto recovery settings (requires explicit confirmation flag per Section 64)
        group.MapPut("/config/recovery", (
            UpdateRecoveryRequest request,
            AutomaticRecoveryOptions recovery) =>
        {
            if (request.Enabled && !request.Confirmed)
            {
                return Results.BadRequest(new
                {
                    Success = false,
                    Message = "Enabling Automatic Recovery requires explicit confirmation of operational risks per Section 64."
                });
            }

            recovery.Enabled = request.Enabled;
            recovery.CriticalCpuThresholdPercent = Math.Clamp(request.CriticalCpuThresholdPercent, 80.0, 100.0);
            recovery.MinimumCriticalDurationMinutes = Math.Clamp(request.MinimumCriticalDurationMinutes, 1, 30);
            recovery.MinimumCulpritCpuPercent = Math.Clamp(request.MinimumCulpritCpuPercent, 50.0, 100.0);
            recovery.MaxRestartsPerHour = Math.Clamp(request.MaxRestartsPerHour, 1, 5);
            recovery.MaxRestartsPerDay = Math.Clamp(request.MaxRestartsPerDay, 1, 10);
            recovery.CooldownMinutes = Math.Clamp(request.CooldownMinutes, 5, 120);

            return Results.Ok(new { Success = true, AutomaticRecovery = recovery });
        });

        // Multi-server historical summary per Section 41
        group.MapGet("/history/summary", async (
            IDbContextFactory<IISMonitorDbContext> contextFactory,
            CancellationToken ct) =>
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);

            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            var serverCounts = await context.Incidents.AsNoTracking()
                .Where(i => i.StartTimeUtc >= thirtyDaysAgo)
                .GroupBy(i => i.ServerName)
                .Select(g => new { Server = g.Key, IncidentCount = g.Count() })
                .ToListAsync(ct);

            return Results.Ok(serverCounts);
        });
    }
}
