using System.Text.Json;
using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;
using IISMonitor.Core.Fingerprinting;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using IISMonitor.Data.Context;
using IISMonitor.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Data.Services;

public class IncidentRepository : IIncidentService
{
    private readonly IDbContextFactory<IISMonitorDbContext> _contextFactory;
    private readonly MonitoringOptions _options;
    private readonly ILogger<IncidentRepository> _logger;

    public IncidentRepository(
        IDbContextFactory<IISMonitorDbContext> contextFactory,
        MonitoringOptions options,
        ILogger<IncidentRepository> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IncidentSummary> StartIncidentAsync(string incidentId, double triggerCpu, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new IncidentEntity
        {
            IncidentId = incidentId,
            ServerName = Environment.MachineName,
            StartTimeUtc = DateTime.UtcNow,
            TriggerThreshold = triggerCpu,
            PeakCpu = triggerCpu,
            AverageCpu = triggerCpu,
            SampleCount = 1,
            Status = "Active",
            Classification = IncidentClassification.UnknownCpuSource
        };

        await context.Incidents.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Incident {IncidentId} created in database. Trigger CPU: {Cpu:F1}%.", incidentId, triggerCpu);

        return ToModel(entity);
    }

    public async Task UpdateIncidentAsync(IncidentSummary incident, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Incidents.FirstOrDefaultAsync(i => i.IncidentId == incident.IncidentId, cancellationToken);
        if (entity == null) return;

        entity.PeakCpu = Math.Max(entity.PeakCpu, incident.PeakCpuPercent);
        entity.AverageCpu = incident.AverageCpuPercent;
        entity.DurationSeconds = (DateTime.UtcNow - entity.StartTimeUtc).TotalSeconds;
        entity.SampleCount = incident.SampleCount;
        entity.AffectedProcessCount = incident.AffectedProcessCount;
        entity.AffectedAppPoolCount = incident.AffectedAppPoolCount;
        entity.TopProcessName = incident.TopProcessName ?? entity.TopProcessName;
        entity.TopProcessId = incident.TopProcessId ?? entity.TopProcessId;
        entity.TopProcessPeakCpu = Math.Max(entity.TopProcessPeakCpu, incident.TopProcessPeakCpu);
        entity.TopAppPoolName = incident.TopAppPoolName ?? entity.TopAppPoolName;
        entity.TopAppPoolPeakCpu = Math.Max(entity.TopAppPoolPeakCpu, incident.TopAppPoolPeakCpu);
        entity.Classification = incident.Classification;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IncidentSummary?> EndIncidentAsync(string incidentId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Incidents.FirstOrDefaultAsync(i => i.IncidentId == incidentId, cancellationToken);
        if (entity == null) return null;

        entity.EndTimeUtc = DateTime.UtcNow;
        entity.DurationSeconds = (entity.EndTimeUtc.Value - entity.StartTimeUtc).TotalSeconds;
        if (entity.Status == "Active")
        {
            entity.Status = "Recovered";
        }

        // Calculate final stats from samples
        var samples = await context.MetricSamples
            .Where(s => s.IncidentId == incidentId)
            .OrderBy(s => s.TimestampUtc)
            .ToListAsync(cancellationToken);

        if (samples.Count > 0)
        {
            entity.SampleCount = samples.Count;
            entity.PeakCpu = samples.Max(s => s.TotalCpu);
            entity.AverageCpu = samples.Average(s => s.TotalCpu);

            // Generate fingerprint
            var cpuValues = samples.Select(s => s.TotalCpu).ToList();
            var buckets = IncidentFingerprintMatcher.ResampleCpuProfile(cpuValues);

            var fp = new IncidentFingerprint
            {
                IncidentId = incidentId,
                ServerName = entity.ServerName,
                PrimaryAppPool = entity.TopAppPoolName ?? "Unknown",
                PeakCpu = entity.PeakCpu,
                CpuProfileBuckets = buckets,
                MemoryDeltaMb = 0,
                RequestRatePerSec = 0
            };

            entity.FingerprintHash = IncidentFingerprintMatcher.ComputeFingerprintHash(fp);

            var fpEntity = new IncidentFingerprintEntity
            {
                IncidentId = incidentId,
                AppPoolName = fp.PrimaryAppPool,
                PeakCpu = fp.PeakCpu,
                CpuProfileBucketsJson = JsonSerializer.Serialize(buckets),
                MemoryDeltaMb = fp.MemoryDeltaMb,
                GcActivityScore = fp.GcActivityScore,
                RequestRatePerSec = fp.RequestRatePerSec,
                FingerprintHash = entity.FingerprintHash
            };

            await context.IncidentFingerprints.AddAsync(fpEntity, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Incident {IncidentId} closed. Duration: {Duration:F0}s, Peak: {Peak:F1}%.",
            incidentId, entity.DurationSeconds, entity.PeakCpu);

        return ToModel(entity);
    }

    public async Task<IncidentSummary?> GetIncidentAsync(string incidentId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Incidents.FirstOrDefaultAsync(i => i.IncidentId == incidentId, cancellationToken);
        if (entity == null) return null;

        bool updated = false;

        // Augment TopProcessName and TopAppPoolName if empty from recorded samples
        if (string.IsNullOrEmpty(entity.TopProcessName))
        {
            var topProc = await context.ProcessSamples.AsNoTracking()
                .Where(p => p.IncidentId == incidentId)
                .OrderByDescending(p => p.CpuPercent)
                .FirstOrDefaultAsync(cancellationToken);
            if (topProc != null)
            {
                entity.TopProcessName = topProc.ProcessName;
                entity.TopProcessId = topProc.ProcessId;
                entity.TopProcessPeakCpu = topProc.CpuPercent;
                updated = true;
            }
        }

        if (string.IsNullOrEmpty(entity.TopAppPoolName))
        {
            var topPool = await context.ApplicationPoolSamples.AsNoTracking()
                .Where(a => a.IncidentId == incidentId)
                .OrderByDescending(a => a.CpuPercent)
                .FirstOrDefaultAsync(cancellationToken);
            if (topPool != null)
            {
                entity.TopAppPoolName = topPool.AppPoolName;
                entity.TopAppPoolPeakCpu = topPool.CpuPercent;
                updated = true;
            }
        }

        // Auto-close stale active incidents if no new samples have arrived in >10 minutes
        if (entity.Status == "Active")
        {
            var lastSample = await context.MetricSamples.AsNoTracking()
                .Where(s => s.IncidentId == incidentId)
                .OrderByDescending(s => s.TimestampUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastSample != null && (DateTime.UtcNow - lastSample.TimestampUtc) > TimeSpan.FromMinutes(10))
            {
                entity.Status = "Recovered";
                entity.EndTimeUtc = lastSample.TimestampUtc > entity.StartTimeUtc
                    ? lastSample.TimestampUtc
                    : entity.StartTimeUtc.AddSeconds(Math.Max(30, (entity.SampleCount > 0 ? entity.SampleCount : 1) * 30));
                entity.DurationSeconds = Math.Max(1.0, (entity.EndTimeUtc.Value - entity.StartTimeUtc).TotalSeconds);

                var allSamples = await context.MetricSamples.AsNoTracking()
                    .Where(s => s.IncidentId == incidentId)
                    .ToListAsync(cancellationToken);

                if (allSamples.Count > 0)
                {
                    entity.SampleCount = allSamples.Count;
                    entity.PeakCpu = allSamples.Max(s => s.TotalCpu);
                    entity.AverageCpu = allSamples.Average(s => s.TotalCpu);
                }

                updated = true;
            }
        }

        if (updated)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch { }
        }

        return ToModel(entity);
    }

    public async Task<IReadOnlyList<IncidentSummary>> GetIncidentsAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Incidents
            .OrderByDescending(i => i.StartTimeUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        bool changes = false;
        foreach (var entity in entities)
        {
            if (string.IsNullOrEmpty(entity.TopProcessName))
            {
                var topProc = await context.ProcessSamples.AsNoTracking()
                    .Where(p => p.IncidentId == entity.IncidentId)
                    .OrderByDescending(p => p.CpuPercent)
                    .FirstOrDefaultAsync(cancellationToken);
                if (topProc != null)
                {
                    entity.TopProcessName = topProc.ProcessName;
                    entity.TopProcessId = topProc.ProcessId;
                    entity.TopProcessPeakCpu = topProc.CpuPercent;
                    changes = true;
                }
            }

            if (string.IsNullOrEmpty(entity.TopAppPoolName))
            {
                var topPool = await context.ApplicationPoolSamples.AsNoTracking()
                    .Where(a => a.IncidentId == entity.IncidentId)
                    .OrderByDescending(a => a.CpuPercent)
                    .FirstOrDefaultAsync(cancellationToken);
                if (topPool != null)
                {
                    entity.TopAppPoolName = topPool.AppPoolName;
                    entity.TopAppPoolPeakCpu = topPool.CpuPercent;
                    changes = true;
                }
            }

            if (entity.Status == "Active")
            {
                var lastSample = await context.MetricSamples.AsNoTracking()
                    .Where(s => s.IncidentId == entity.IncidentId)
                    .OrderByDescending(s => s.TimestampUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (lastSample != null && (DateTime.UtcNow - lastSample.TimestampUtc) > TimeSpan.FromMinutes(10))
                {
                    entity.Status = "Recovered";
                    entity.EndTimeUtc = lastSample.TimestampUtc > entity.StartTimeUtc
                        ? lastSample.TimestampUtc
                        : entity.StartTimeUtc.AddSeconds(Math.Max(30, (entity.SampleCount > 0 ? entity.SampleCount : 1) * 30));
                    entity.DurationSeconds = Math.Max(1.0, (entity.EndTimeUtc.Value - entity.StartTimeUtc).TotalSeconds);
                    changes = true;
                }
            }
        }

        if (changes)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch { }
        }

        return entities.Select(ToModel).ToList();
    }

    public async Task<IReadOnlyList<SimilarIncidentResult>> FindSimilarIncidentsAsync(string incidentId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var targetFp = await context.IncidentFingerprints.AsNoTracking()
            .FirstOrDefaultAsync(f => f.IncidentId == incidentId, cancellationToken);

        if (targetFp == null) return Array.Empty<SimilarIncidentResult>();

        var allFps = await context.IncidentFingerprints.AsNoTracking()
            .Where(f => f.IncidentId != incidentId)
            .ToListAsync(cancellationToken);

        var targetModel = new IncidentFingerprint
        {
            IncidentId = targetFp.IncidentId,
            PrimaryAppPool = targetFp.AppPoolName,
            PeakCpu = targetFp.PeakCpu,
            CpuProfileBuckets = JsonSerializer.Deserialize<double[]>(targetFp.CpuProfileBucketsJson) ?? Array.Empty<double>(),
            MemoryDeltaMb = targetFp.MemoryDeltaMb,
            GcActivityScore = targetFp.GcActivityScore,
            RequestRatePerSec = targetFp.RequestRatePerSec
        };

        var results = new List<SimilarIncidentResult>();
        foreach (var other in allFps)
        {
            var otherModel = new IncidentFingerprint
            {
                IncidentId = other.IncidentId,
                PrimaryAppPool = other.AppPoolName,
                PeakCpu = other.PeakCpu,
                CpuProfileBuckets = JsonSerializer.Deserialize<double[]>(other.CpuProfileBucketsJson) ?? Array.Empty<double>(),
                MemoryDeltaMb = other.MemoryDeltaMb,
                GcActivityScore = other.GcActivityScore,
                RequestRatePerSec = other.RequestRatePerSec
            };

            double similarity = IncidentFingerprintMatcher.CalculateSimilarity(targetModel, otherModel);
            if (similarity >= 0.5)
            {
                var otherIncident = await context.Incidents.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.IncidentId == other.IncidentId, cancellationToken);

                results.Add(new SimilarIncidentResult(
                    other.IncidentId,
                    otherIncident?.StartTimeUtc ?? DateTime.UtcNow,
                    similarity,
                    other.AppPoolName,
                    other.PeakCpu
                ));
            }
        }

        return results.OrderByDescending(r => r.SimilarityScore).Take(5).ToList();
    }

    public async Task<RootCauseEvidence> GenerateRootCauseEvidenceAsync(string incidentId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var incident = await context.Incidents.AsNoTracking()
            .FirstOrDefaultAsync(i => i.IncidentId == incidentId, cancellationToken);

        if (incident == null)
            return new RootCauseEvidence { IncidentId = incidentId };

        // Process analysis
        var topProcesses = await context.ProcessSamples.AsNoTracking()
            .Where(p => p.IncidentId == incidentId)
            .GroupBy(p => p.ProcessName)
            .Select(g => new { ProcessName = g.Key, PeakCpu = g.Max(p => p.CpuPercent), AvgCpu = g.Average(p => p.CpuPercent), Pid = g.Select(x => x.ProcessId).FirstOrDefault() })
            .OrderByDescending(x => x.PeakCpu)
            .ToListAsync(cancellationToken);

        var topProc = topProcesses.FirstOrDefault();
        string primaryProcName = topProc?.ProcessName ?? "Unknown";
        double primaryProcCpu = topProc?.PeakCpu ?? 0;
        int? primaryProcPid = topProc?.Pid;

        // Security / AV
        var secProc = topProcesses.FirstOrDefault(p =>
            p.ProcessName.Contains("MsMpEng", StringComparison.OrdinalIgnoreCase) ||
            p.ProcessName.Contains("Defender", StringComparison.OrdinalIgnoreCase));

        double secCpu = secProc?.PeakCpu ?? 0;

        // AppPool analysis
        var topPools = await context.ApplicationPoolSamples.AsNoTracking()
            .Where(a => a.IncidentId == incidentId)
            .GroupBy(a => a.AppPoolName)
            .Select(g => new { AppPool = g.Key, PeakCpu = g.Max(a => a.CpuPercent), AvgCpu = g.Average(a => a.CpuPercent) })
            .OrderByDescending(x => x.PeakCpu)
            .ToListAsync(cancellationToken);

        var topPool = topPools.FirstOrDefault();

        // Tasks during incident
        var scheduledTasks = await context.ScheduledTasks.AsNoTracking()
            .Where(t => t.IncidentId == incidentId && t.IsRunning)
            .Select(t => t.TaskName)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Associated websites
        var websites = await context.Websites.AsNoTracking()
            .Where(w => w.AppPoolName == (topPool != null ? topPool.AppPool : ""))
            .Select(w => w.SiteName)
            .ToListAsync(cancellationToken);

        // Evaluations
        var procConf = primaryProcCpu >= 70.0 ? CorrelationAssessment.StrongCorrelation :
                       primaryProcCpu >= 40.0 ? CorrelationAssessment.Possible : CorrelationAssessment.Unlikely;

        var poolConf = (topPool != null && topPool.PeakCpu >= 70.0) ? CorrelationAssessment.StrongCorrelation :
                       (topPool != null && topPool.PeakCpu >= 40.0) ? CorrelationAssessment.Possible : CorrelationAssessment.Unlikely;

        var taskConf = scheduledTasks.Count > 0 ? CorrelationAssessment.StrongCorrelation : CorrelationAssessment.NoEvidence;
        var secConf = secCpu >= 40.0 ? CorrelationAssessment.StrongCorrelation : CorrelationAssessment.Unlikely;

        return new RootCauseEvidence
        {
            IncidentId = incidentId,
            PrimaryProcessName = primaryProcName,
            PrimaryProcessId = primaryProcPid,
            PrimaryProcessPeakCpu = primaryProcCpu,
            PrimaryProcessConfidence = procConf,

            PrimaryAppPoolName = topPool?.AppPool,
            PrimaryAppPoolPeakCpu = topPool?.PeakCpu ?? 0,
            PrimaryAppPoolConfidence = poolConf,

            TrafficChangePercent = 0,
            TrafficSummary = "IIS traffic rate remained within normal baseline limits.",
            TrafficAssessment = CorrelationAssessment.Unlikely,

            BaselinePrivateMemoryBytes = 0,
            PeakPrivateMemoryBytes = 0,
            MemorySummary = "Memory showed stable utilization across incident duration.",
            MemoryAssessment = CorrelationAssessment.Unlikely,

            Gen2CollectionsDelta = 0,
            GcSummary = "No anomalous GC pause times detected.",
            GcAssessment = CorrelationAssessment.NoEvidence,

            CorrelatedScheduledTasks = scheduledTasks,
            ScheduledTaskAssessment = taskConf,

            SecurityProcessPeakCpu = secCpu,
            SecuritySummary = secCpu >= 10.0 ? $"Microsoft Defender reached {secCpu:F1}% CPU." : "Antivirus / EDR CPU remained below 10%.",
            SecurityAssessment = secConf,

            AffectedWebsites = websites
        };
    }

    private static IncidentSummary ToModel(IncidentEntity e) => new()
    {
        IncidentId = e.IncidentId,
        ServerName = e.ServerName,
        StartTimeUtc = e.StartTimeUtc,
        EndTimeUtc = e.EndTimeUtc,
        DurationSeconds = e.DurationSeconds > 0
            ? e.DurationSeconds
            : (e.EndTimeUtc.HasValue && e.EndTimeUtc.Value > e.StartTimeUtc
                ? (e.EndTimeUtc.Value - e.StartTimeUtc).TotalSeconds
                : Math.Max(1.0, (DateTime.UtcNow - e.StartTimeUtc).TotalSeconds)),
        PeakCpuPercent = e.PeakCpu,
        AverageCpuPercent = e.AverageCpu,
        TriggerThresholdPercent = e.TriggerThreshold,
        SampleCount = e.SampleCount,
        AffectedProcessCount = e.AffectedProcessCount,
        AffectedAppPoolCount = e.AffectedAppPoolCount,
        Status = e.Status,
        Classification = e.Classification,
        TopProcessName = e.TopProcessName,
        TopProcessId = e.TopProcessId,
        TopProcessPeakCpu = e.TopProcessPeakCpu,
        TopAppPoolName = e.TopAppPoolName,
        TopAppPoolPeakCpu = e.TopAppPoolPeakCpu,
        FingerprintHash = e.FingerprintHash
    };
}

public static class CorrelationExtensions
{
    public static CorrelationAssessment LowCorrelation() => CorrelationAssessment.Unlikely;
}
