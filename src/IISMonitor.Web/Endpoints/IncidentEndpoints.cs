using IISMonitor.Core.Interfaces;
using IISMonitor.Data.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace IISMonitor.Web.Endpoints;

public static class IncidentEndpoints
{
    public static void MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents");

        // 1. List incidents
        group.MapGet("/", async (
            IIncidentService incidentService,
            int take = 50,
            CancellationToken ct = default) =>
        {
            var incidents = await incidentService.GetIncidentsAsync(take, ct);
            return Results.Ok(incidents);
        });

        // 2. Incident detail
        group.MapGet("/{id}", async (
            string id,
            IIncidentService incidentService,
            CancellationToken ct) =>
        {
            var incident = await incidentService.GetIncidentAsync(id, ct);
            return incident != null ? Results.Ok(incident) : Results.NotFound();
        });

        // 3. CPU Timeline samples (Baseline + Incident + Recovery)
        group.MapGet("/{id}/timeline", async (
            string id,
            IDbContextFactory<IISMonitorDbContext> contextFactory,
            CancellationToken ct) =>
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);

            var samples = await context.MetricSamples.AsNoTracking()
                .Where(m => m.IncidentId == id)
                .OrderBy(m => m.TimestampUtc)
                .Select(m => new
                {
                    m.TimestampUtc,
                    m.TotalCpu,
                    m.UserCpu,
                    m.PrivilegedCpu,
                    m.ProcessorQueueLength,
                    m.CommittedMemoryPercent,
                    m.IsBaseline,
                    m.IsHighDetail
                })
                .ToListAsync(ct);

            return Results.Ok(samples);
        });

        // 4. Process Timeline samples
        group.MapGet("/{id}/processes", async (
            string id,
            IDbContextFactory<IISMonitorDbContext> contextFactory,
            CancellationToken ct) =>
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);

            var processes = await context.ProcessSamples.AsNoTracking()
                .Where(p => p.IncidentId == id)
                .OrderBy(p => p.TimestampUtc)
                .Select(p => new
                {
                    p.TimestampUtc,
                    p.ProcessId,
                    p.ProcessName,
                    p.CpuPercent,
                    p.PrivateBytes,
                    p.WorkingSet,
                    p.ThreadCount
                })
                .ToListAsync(ct);

            return Results.Ok(processes);
        });

        // 5. App Pool Ranking
        group.MapGet("/{id}/apppools", async (
            string id,
            IDbContextFactory<IISMonitorDbContext> contextFactory,
            CancellationToken ct) =>
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);

            var poolSummary = await context.ApplicationPoolSamples.AsNoTracking()
                .Where(a => a.IncidentId == id)
                .GroupBy(a => a.AppPoolName)
                .Select(g => new
                {
                    AppPoolName = g.Key,
                    PeakCpu = g.Max(x => x.CpuPercent),
                    AvgCpu = g.Average(x => x.CpuPercent),
                    ProcessId = g.Select(x => x.ProcessId).FirstOrDefault(),
                    MaxPrivateMemoryBytes = g.Max(x => x.PrivateMemory),
                    MaxWorkingSetBytes = g.Max(x => x.WorkingSet),
                    MaxThreadCount = g.Max(x => x.ThreadCount),
                    MaxQueueLength = g.Max(x => x.QueueLength),
                    SampleCount = g.Count()
                })
                .OrderByDescending(p => p.PeakCpu)
                .ToListAsync(ct);

            return Results.Ok(poolSummary);
        });

        // 6. Root Cause Evidence Report
        group.MapGet("/{id}/rootcause", async (
            string id,
            IIncidentService incidentService,
            CancellationToken ct) =>
        {
            var evidence = await incidentService.GenerateRootCauseEvidenceAsync(id, ct);
            return Results.Ok(evidence);
        });

        // 7. Similar Incidents
        group.MapGet("/{id}/similar", async (
            string id,
            IIncidentService incidentService,
            CancellationToken ct) =>
        {
            var similar = await incidentService.FindSimilarIncidentsAsync(id, ct);
            return Results.Ok(similar);
        });

        // 8. Correlated Windows Events
        group.MapGet("/{id}/events", async (
            string id,
            IDbContextFactory<IISMonitorDbContext> contextFactory,
            CancellationToken ct) =>
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);
            var events = await context.WindowsEvents.AsNoTracking()
                .Where(w => w.IncidentId == id)
                .OrderBy(w => w.TimestampUtc)
                .ToListAsync(ct);

            return Results.Ok(events);
        });

        // 9. Correlated Scheduled Tasks
        group.MapGet("/{id}/tasks", async (
            string id,
            IDbContextFactory<IISMonitorDbContext> contextFactory,
            CancellationToken ct) =>
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);
            var tasks = await context.ScheduledTasks.AsNoTracking()
                .Where(s => s.IncidentId == id)
                .ToListAsync(ct);

            return Results.Ok(tasks);
        });

        // 10. Recovery action audit
        group.MapGet("/{id}/recovery", async (
            string id,
            IDbContextFactory<IISMonitorDbContext> contextFactory,
            CancellationToken ct) =>
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);
            var action = await context.RecoveryActions.AsNoTracking()
                .FirstOrDefaultAsync(r => r.IncidentId == id, ct);

            return action != null ? Results.Ok(action) : Results.Ok(new { Performed = false });
        });
    }
}
