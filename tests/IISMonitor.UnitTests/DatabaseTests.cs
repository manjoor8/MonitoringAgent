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
