using IISMonitor.Core.Configuration;
using IISMonitor.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Data.Services;

/// <summary>
/// Scheduled data cleanup service per Section 24.
/// Prevents SQLite database and dump directory from growing indefinitely.
/// </summary>
public class RetentionService
{
    private readonly IDbContextFactory<IISMonitorDbContext> _contextFactory;
    private readonly RetentionOptions _retentionOptions;
    private readonly DiagnosticsOptions _diagnosticsOptions;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(
        IDbContextFactory<IISMonitorDbContext> contextFactory,
        RetentionOptions retentionOptions,
        DiagnosticsOptions diagnosticsOptions,
        ILogger<RetentionService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _retentionOptions = retentionOptions ?? throw new ArgumentNullException(nameof(retentionOptions));
        _diagnosticsOptions = diagnosticsOptions ?? throw new ArgumentNullException(nameof(diagnosticsOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunCleanupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting scheduled retention cleanup.");

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var now = DateTime.UtcNow;

            // 1. Clean up baseline samples older than BaselineDays (default 7 days)
            var baselineCutoff = now.AddDays(-_retentionOptions.BaselineDays);
            var deletedBaseline = await context.MetricSamples
                .Where(m => m.IsBaseline && m.TimestampUtc < baselineCutoff && m.IncidentId == null)
                .ExecuteDeleteAsync(cancellationToken);

            var deletedProcessBaseline = await context.ProcessSamples
                .Where(p => p.TimestampUtc < baselineCutoff && p.IncidentId == null)
                .ExecuteDeleteAsync(cancellationToken);

            var deletedPoolBaseline = await context.ApplicationPoolSamples
                .Where(a => a.TimestampUtc < baselineCutoff && a.IncidentId == null)
                .ExecuteDeleteAsync(cancellationToken);

            _logger.LogInformation("Cleaned up baseline data: {MetricCount} metrics, {ProcCount} processes, {PoolCount} pools.",
                deletedBaseline, deletedProcessBaseline, deletedPoolBaseline);

            // 2. Clean up incident data older than IncidentDays (default 90 days)
            var incidentCutoff = now.AddDays(-_retentionOptions.IncidentDays);
            var oldIncidents = await context.Incidents
                .Where(i => i.EndTimeUtc != null && i.EndTimeUtc < incidentCutoff)
                .Select(i => i.IncidentId)
                .ToListAsync(cancellationToken);

            if (oldIncidents.Count > 0)
            {
                await context.MetricSamples.Where(m => oldIncidents.Contains(m.IncidentId!)).ExecuteDeleteAsync(cancellationToken);
                await context.ProcessSamples.Where(p => oldIncidents.Contains(p.IncidentId!)).ExecuteDeleteAsync(cancellationToken);
                await context.ApplicationPoolSamples.Where(a => oldIncidents.Contains(a.IncidentId!)).ExecuteDeleteAsync(cancellationToken);
                await context.WindowsEvents.Where(w => oldIncidents.Contains(w.IncidentId!)).ExecuteDeleteAsync(cancellationToken);
                await context.ScheduledTasks.Where(s => oldIncidents.Contains(s.IncidentId!)).ExecuteDeleteAsync(cancellationToken);
                await context.RecoveryActions.Where(r => oldIncidents.Contains(r.IncidentId)).ExecuteDeleteAsync(cancellationToken);
                await context.IncidentFingerprints.Where(f => oldIncidents.Contains(f.IncidentId)).ExecuteDeleteAsync(cancellationToken);
                await context.Incidents.Where(i => oldIncidents.Contains(i.IncidentId)).ExecuteDeleteAsync(cancellationToken);

                _logger.LogInformation("Purged {Count} expired incidents older than {Days} days.", oldIncidents.Count, _retentionOptions.IncidentDays);
            }

            // 3. Clean up dumps older than DumpDays (default 30 days) with active incident dependency check (§24)
            await CleanupDumpsAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during retention cleanup execution.");
        }
    }

    private async Task CleanupDumpsAsync(IISMonitorDbContext context, CancellationToken ct)
    {
        var dumpCutoff = DateTime.UtcNow.AddDays(-_retentionOptions.DumpDays);

        // Check diagnostic artifacts
        var expiredArtifacts = await context.DiagnosticArtifacts
            .Where(d => d.CreatedUtc < dumpCutoff)
            .ToListAsync(ct);

        foreach (var artifact in expiredArtifacts)
        {
            // Verify no active incident depends on this dump
            bool hasActiveIncident = await context.Incidents.AnyAsync(i => i.IncidentId == artifact.IncidentId && i.Status == "Active", ct);
            if (hasActiveIncident)
            {
                _logger.LogInformation("Preserving dump {Path} because incident {IncidentId} is still active.", artifact.FilePath, artifact.IncidentId);
                continue;
            }

            try
            {
                if (File.Exists(artifact.FilePath))
                {
                    File.Delete(artifact.FilePath);
                    _logger.LogInformation("Deleted expired memory dump: {Path}", artifact.FilePath);
                }
                context.DiagnosticArtifacts.Remove(artifact);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete dump file: {Path}", artifact.FilePath);
            }
        }

        await context.SaveChangesAsync(ct);
    }
}
