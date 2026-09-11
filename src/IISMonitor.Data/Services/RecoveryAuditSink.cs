using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using IISMonitor.Data.Context;
using IISMonitor.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Data.Services;

public class RecoveryAuditSink : IRecoveryAuditSink
{
    private readonly IDbContextFactory<IISMonitorDbContext> _contextFactory;
    private readonly ILogger<RecoveryAuditSink> _logger;

    public RecoveryAuditSink(IDbContextFactory<IISMonitorDbContext> contextFactory, ILogger<RecoveryAuditSink> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecordRecoveryActionAsync(RecoveryAction action, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = new RecoveryActionEntity
            {
                IncidentId = action.IncidentId,
                TimestampUtc = action.TimestampUtc,
                ServerName = action.ServerName,
                AppPoolName = action.AppPoolName,
                ProcessId = action.ProcessId,
                CpuBeforeRestart = action.CpuBeforeRestart,
                ServerCpuBeforeRestart = action.ServerCpuBeforeRestart,
                CriticalDurationSeconds = action.CriticalDurationSeconds,
                Action = action.Action,
                Result = action.Result,
                ErrorMessage = action.ErrorMessage
            };

            await context.RecoveryActions.AddAsync(entity, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Recorded recovery audit action for '{Pool}' in incident {IncidentId}.", action.AppPoolName, action.IncidentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record recovery audit action.");
        }
    }

    public async Task UpdateVerificationAsync(string incidentId, int intervalSeconds, double cpu, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await context.RecoveryActions.FirstOrDefaultAsync(r => r.IncidentId == incidentId, cancellationToken);
            if (entity != null)
            {
                if (intervalSeconds <= 30) entity.CpuAfter30s = cpu;
                else if (intervalSeconds <= 60) entity.CpuAfter60s = cpu;
                else if (intervalSeconds <= 120) entity.CpuAfter120s = cpu;
                else entity.CpuAfter300s = cpu;

                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update recovery verification for incident {IncidentId}.", incidentId);
        }
    }
}
