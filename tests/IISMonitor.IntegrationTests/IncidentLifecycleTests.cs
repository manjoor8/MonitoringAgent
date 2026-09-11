using System.Threading.Channels;
using IISMonitor.Core.Collections;
using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using IISMonitor.Core.StateMachine;
using IISMonitor.Data.Context;
using IISMonitor.Data.Services;
using IISMonitor.Infrastructure.AutoRecovery;
using IISMonitor.Infrastructure.Simulation;
using IISMonitor.Service.Workers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IISMonitor.IntegrationTests;

public class IncidentLifecycleTests
{
    private class TestDbContextFactory : IDbContextFactory<IISMonitorDbContext>
    {
        private readonly DbContextOptions<IISMonitorDbContext> _options;
        public TestDbContextFactory(DbContextOptions<IISMonitorDbContext> options) => _options = options;
        public IISMonitorDbContext CreateDbContext() => new(_options);
        public Task<IISMonitorDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new IISMonitorDbContext(_options));
    }

    [Fact]
    public async Task FullIncidentLifecycle_NormalToPeakToRecovery_PersistsAndFingerprints()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var dbOptions = new DbContextOptionsBuilder<IISMonitorDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var initCtx = new IISMonitorDbContext(dbOptions))
        {
            initCtx.Database.EnsureCreated();
        }

        var factory = new TestDbContextFactory(dbOptions);

        var monitoringOptions = new MonitoringOptions
        {
            NormalIntervalSeconds = 1,
            IncidentIntervalSeconds = 1,
            CpuThresholdPercent = 70.0,
            CriticalCpuThresholdPercent = 90.0,
            RecoveryThresholdPercent = 60.0,
            RecoveryConsecutiveSamples = 2
        };

        var stateMachine = new CpuMonitoringStateMachine(monitoringOptions);
        var incidentRepo = new IncidentRepository(factory, monitoringOptions, NullLogger<IncidentRepository>.Instance);
        var buffer = new CircularBuffer<MonitoringSample>(120);
        var writeChannel = Channel.CreateUnbounded<MetricWriteBatch>();

        var mockAlert = new Mock<IAlertService>();
        var mockRecovery = new Mock<IAutoRecoveryService>();
        var mockProcDump = new Mock<IProcDumpService>();
        var mockEvents = new Mock<IWindowsEventCollector>();
        var mockTasks = new Mock<IScheduledTaskCollector>();
        var mockDotNet = new Mock<IDotNetRuntimeCollector>();

        // Step 1: Normal state (CPU 45%)
        var r1 = stateMachine.ProcessCpuSample(45.0);
        Assert.Equal(MonitoringState.Normal, r1.CurrentState);

        // Step 2: CPU Spikes to 75% -> Incident Starts
        var r2 = stateMachine.ProcessCpuSample(75.0);
        Assert.Equal(MonitoringState.IncidentStarting, r2.CurrentState);
        string incidentId = stateMachine.CurrentIncidentId!;
        Assert.NotNull(incidentId);

        var createdIncident = await incidentRepo.StartIncidentAsync(incidentId, 75.0);
        Assert.Equal("Active", createdIncident.Status);

        // Record some samples into DB for this incident
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.MetricSamples.Add(new IISMonitor.Data.Entities.MetricSampleEntity
            {
                IncidentId = incidentId,
                TimestampUtc = DateTime.UtcNow.AddSeconds(-20),
                TotalCpu = 75.0,
                IsBaseline = false,
                IsHighDetail = true
            });
            ctx.MetricSamples.Add(new IISMonitor.Data.Entities.MetricSampleEntity
            {
                IncidentId = incidentId,
                TimestampUtc = DateTime.UtcNow.AddSeconds(-15),
                TotalCpu = 95.0,
                IsBaseline = false,
                IsHighDetail = true
            });
            ctx.MetricSamples.Add(new IISMonitor.Data.Entities.MetricSampleEntity
            {
                IncidentId = incidentId,
                TimestampUtc = DateTime.UtcNow.AddSeconds(-10),
                TotalCpu = 98.0,
                IsBaseline = false,
                IsHighDetail = true
            });
            ctx.MetricSamples.Add(new IISMonitor.Data.Entities.MetricSampleEntity
            {
                IncidentId = incidentId,
                TimestampUtc = DateTime.UtcNow.AddSeconds(-5),
                TotalCpu = 50.0,
                IsBaseline = false,
                IsHighDetail = true
            });

            ctx.ProcessSamples.Add(new IISMonitor.Data.Entities.ProcessSampleEntity
            {
                IncidentId = incidentId,
                TimestampUtc = DateTime.UtcNow.AddSeconds(-10),
                ProcessId = 15432,
                ProcessName = "w3wp.exe",
                CpuPercent = 94.0
            });

            ctx.ApplicationPoolSamples.Add(new IISMonitor.Data.Entities.ApplicationPoolSampleEntity
            {
                IncidentId = incidentId,
                TimestampUtc = DateTime.UtcNow.AddSeconds(-10),
                AppPoolName = "PatientPortalPool",
                ProcessId = 15432,
                CpuPercent = 94.0
            });

            await ctx.SaveChangesAsync();
        }

        // Step 3: CPU Escalates to 98% -> Critical
        var r3 = stateMachine.ProcessCpuSample(98.0);
        Assert.Equal(MonitoringState.Critical, r3.CurrentState);

        await incidentRepo.UpdateIncidentAsync(new IncidentSummary
        {
            IncidentId = incidentId,
            PeakCpuPercent = 98.0,
            TopProcessName = "w3wp.exe",
            TopProcessId = 15432,
            TopProcessPeakCpu = 94.0,
            TopAppPoolName = "PatientPortalPool",
            TopAppPoolPeakCpu = 94.0
        });

        // Step 4: CPU drops to 52% (below 60%) -> Recovery 1
        var r4 = stateMachine.ProcessCpuSample(52.0);
        Assert.Equal(MonitoringState.Recovery, r4.CurrentState);

        // Step 5: CPU remains at 48% -> Recovery 2 (consecutive required = 2) -> Normal!
        var r5 = stateMachine.ProcessCpuSample(48.0);
        Assert.Equal(MonitoringState.Normal, r5.CurrentState);

        // End Incident
        var ended = await incidentRepo.EndIncidentAsync(incidentId);
        Assert.NotNull(ended);
        Assert.Equal("Recovered", ended.Status);
        Assert.Equal(98.0, ended.PeakCpuPercent);
        Assert.NotEmpty(ended.FingerprintHash!);

        // Verify Root Cause Evidence Report
        var evidence = await incidentRepo.GenerateRootCauseEvidenceAsync(incidentId);
        Assert.Equal("w3wp.exe", evidence.PrimaryProcessName);
        Assert.Equal(CorrelationAssessment.StrongCorrelation, evidence.PrimaryProcessConfidence);
        Assert.Equal("PatientPortalPool", evidence.PrimaryAppPoolName);
        Assert.Equal(CorrelationAssessment.StrongCorrelation, evidence.PrimaryAppPoolConfidence);
    }
}
