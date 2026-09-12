using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;
using IISMonitor.Core.Models;
using IISMonitor.Data.Context;
using IISMonitor.Data.Entities;
using IISMonitor.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using Microsoft.Data.Sqlite;

namespace IISMonitor.UnitTests;

public class DatabaseTests
{
    private class TestDbContextFactory : IDbContextFactory<IISMonitorDbContext>
    {
        private readonly DbContextOptions<IISMonitorDbContext> _options;

        public TestDbContextFactory(DbContextOptions<IISMonitorDbContext> options)
        {
            _options = options;
        }

        public IISMonitorDbContext CreateDbContext() => new(_options);
    }

    private static (IDbContextFactory<IISMonitorDbContext> Factory, SqliteConnection Connection) CreateSqliteInMemoryFactory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IISMonitorDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var ctx = new IISMonitorDbContext(options))
        {
            ctx.Database.EnsureCreated();
        }

        return (new TestDbContextFactory(options), connection);
    }

    [Fact]
    public async Task TestIncidentDetailQueries()
    {
        var dbPath = @"d:\Manjoor\Code\ServerAgent\db\monitor.db";
        if (!System.IO.File.Exists(dbPath)) return;

        var options = new DbContextOptionsBuilder<IISMonitorDbContext>()
            .UseSqlite($"Data Source={dbPath};Mode=ReadOnly")
            .Options;

        var contextFactory = new TestDbContextFactory(options);
        var repo = new IncidentRepository(contextFactory, new MonitoringOptions(), NullLogger<IncidentRepository>.Instance);
        var incidentId = "INC-20260911-7755AD";

        var inc = await repo.GetIncidentAsync(incidentId);
        Assert.NotNull(inc);
        Console.WriteLine($"TEST DURATION: Start={inc.StartTimeUtc:o}, End={inc.EndTimeUtc:o}, DurationSec={inc.DurationSeconds}, Status={inc.Status}");
        Assert.Equal("CAR-WEB-14", inc.ServerName);
        Assert.Equal("w3wp", inc.TopProcessName);
        Assert.Equal("returnlonskyscanner.carltonleisure.com", inc.TopAppPoolName);
        Assert.True(inc.PeakCpuPercent > 70);
        Assert.True(inc.DurationSeconds > 0);

        Console.WriteLine("2. Testing timeline query...");
        try
        {
            using var ctx = contextFactory.CreateDbContext();
            var samples = await ctx.MetricSamples.AsNoTracking()
                .Where(m => m.IncidentId == incidentId)
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
                .ToListAsync();
            Console.WriteLine($"SAMPLES FOR INCIDENT: Count={samples.Count}, First={samples.FirstOrDefault()?.TimestampUtc:o}, Last={samples.LastOrDefault()?.TimestampUtc:o}");
            var incidentSamples = await ctx.MetricSamples
                .Where(s => s.TimestampUtc >= DateTime.Parse("2026-09-11T22:50:00Z") && s.TimestampUtc <= DateTime.Parse("2026-09-11T23:05:00Z"))
                .OrderBy(s => s.TimestampUtc)
                .ToListAsync();
            Console.WriteLine($"SAMPLES AROUND INCIDENT ({incidentSamples.Count}):");
            foreach (var s in incidentSamples.Take(20))
            {
                Console.WriteLine($"SAMPLE: Time={s.TimestampUtc:o}, TotalCpu={s.TotalCpu:F1}%, IncId='{s.IncidentId}', Baseline={s.IsBaseline}, HighDetail={s.IsHighDetail}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Timeline query FAILED: {ex}");
        }

        Console.WriteLine("3. Testing apppools query...");
        try
        {
            using var ctx = contextFactory.CreateDbContext();
            var poolSummary = await ctx.ApplicationPoolSamples.AsNoTracking()
                .Where(a => a.IncidentId == incidentId)
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
                .ToListAsync();
            Console.WriteLine($"AppPool summary: {poolSummary.Count}");
            var json = System.Text.Json.JsonSerializer.Serialize(poolSummary.Take(2));
            Console.WriteLine($"AppPool JSON sample: {json}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AppPool query FAILED: {ex}");
        }

        Console.WriteLine("4. Testing GenerateRootCauseEvidenceAsync...");
        try
        {
            var evidence = await repo.GenerateRootCauseEvidenceAsync(incidentId);
            Console.WriteLine($"RootCause: PrimProc={evidence.PrimaryProcessName} ({evidence.PrimaryProcessPeakCpu}%), PrimPool={evidence.PrimaryAppPoolName} ({evidence.PrimaryAppPoolPeakCpu}%)");
            var json = System.Text.Json.JsonSerializer.Serialize(evidence);
            Console.WriteLine($"RootCause JSON: {json}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GenerateRootCauseEvidenceAsync FAILED: {ex}");
        }

        Console.WriteLine("5. Testing FindSimilarIncidentsAsync...");
        try
        {
            var similar = await repo.FindSimilarIncidentsAsync(incidentId);
            Console.WriteLine($"Similar incidents: {similar.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FindSimilarIncidentsAsync FAILED: {ex}");
        }

        Console.WriteLine("6. Testing recovery query...");
        try
        {
            using var ctx = contextFactory.CreateDbContext();
            var action = await ctx.RecoveryActions.AsNoTracking()
                .FirstOrDefaultAsync(r => r.IncidentId == incidentId);
            Console.WriteLine($"Recovery action: Found={action != null}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Recovery query FAILED: {ex}");
        }
    }

    [Fact]
    public async Task IncidentRepository_StartUpdateEndLifecycle()
    {
        var (factory, connection) = CreateSqliteInMemoryFactory();
        using (connection)
        {
            var options = new MonitoringOptions();
            var repo = new IncidentRepository(factory, options, NullLogger<IncidentRepository>.Instance);

            string incidentId = "INC-TEST-001";

            // 1. Start
            var created = await repo.StartIncidentAsync(incidentId, 75.0);
            Assert.Equal(incidentId, created.IncidentId);
            Assert.Equal("Active", created.Status);
            Assert.Equal(75.0, created.PeakCpuPercent);

            // 2. Update with peak
            var update = new IncidentSummary
            {
                IncidentId = incidentId,
                PeakCpuPercent = 98.0,
                AverageCpuPercent = 85.0,
                SampleCount = 10,
                TopProcessName = "w3wp.exe",
                TopProcessId = 1234,
                TopProcessPeakCpu = 92.0,
                TopAppPoolName = "PatientPortalPool",
                TopAppPoolPeakCpu = 89.0,
                Classification = IncidentClassification.NormalRecovery
            };
            await repo.UpdateIncidentAsync(update);

            var retrieved = await repo.GetIncidentAsync(incidentId);
            Assert.NotNull(retrieved);
            Assert.Equal(98.0, retrieved.PeakCpuPercent);
            Assert.Equal("w3wp.exe", retrieved.TopProcessName);
            Assert.Equal("PatientPortalPool", retrieved.TopAppPoolName);

            // 3. End
            var ended = await repo.EndIncidentAsync(incidentId);
            Assert.NotNull(ended);
            Assert.Equal("Recovered", ended.Status);
            Assert.NotNull(ended.EndTimeUtc);
        }
    }

    [Fact]
    public async Task RetentionService_CleansOldBaselineRecords()
    {
        var (factory, connection) = CreateSqliteInMemoryFactory();
        using (connection)
        {
            var retentionOpts = new RetentionOptions { BaselineDays = 7, IncidentDays = 30, DumpDays = 10 };
            var diagOpts = new DiagnosticsOptions();
            var retention = new RetentionService(factory, retentionOpts, diagOpts, NullLogger<RetentionService>.Instance);

            // Insert test records
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                // Old baseline sample (8 days ago, no incident)
                ctx.MetricSamples.Add(new MetricSampleEntity
                {
                    TimestampUtc = DateTime.UtcNow.AddDays(-8),
                    TotalCpu = 40.0,
                    IsBaseline = true,
                    IncidentId = null
                });

                // Fresh baseline sample (1 day ago)
                ctx.MetricSamples.Add(new MetricSampleEntity
                {
                    TimestampUtc = DateTime.UtcNow.AddDays(-1),
                    TotalCpu = 45.0,
                    IsBaseline = true,
                    IncidentId = null
                });

                // Incident sample from 8 days ago (should NOT be deleted by baseline cleaner)
                ctx.MetricSamples.Add(new MetricSampleEntity
                {
                    TimestampUtc = DateTime.UtcNow.AddDays(-8),
                    TotalCpu = 95.0,
                    IsBaseline = false,
                    IncidentId = "INC-OLD-01"
                });

                await ctx.SaveChangesAsync();
            }

            // Run cleanup
            await retention.RunCleanupAsync();

            await using (var ctx = await factory.CreateDbContextAsync())
            {
                var remaining = await ctx.MetricSamples.ToListAsync();
                Assert.Equal(2, remaining.Count);
                Assert.Contains(remaining, m => m.IncidentId == "INC-OLD-01");
                Assert.Contains(remaining, m => m.IncidentId == null && m.TotalCpu == 45.0);
            }
        }
    }
}
