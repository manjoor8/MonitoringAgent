using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using IISMonitor.Infrastructure.AutoRecovery;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IISMonitor.UnitTests;

public class AutoRecoveryTests
{
    private readonly Mock<IIisMonitor> _mockIis = new();
    private readonly Mock<IProcDumpService> _mockProcDump = new();
    private readonly Mock<IRecoveryAuditSink> _mockAudit = new();
    private readonly Mock<IAlertService> _mockAlert = new();

    private AutoRecoveryService CreateService(AutomaticRecoveryOptions options)
    {
        return new AutoRecoveryService(
            options,
            _mockIis.Object,
            _mockProcDumpService.Object,
            _mockAudit.Object,
            _mockAlert.Object,
            NullLogger<AutoRecoveryService>.Instance);
    }

    private readonly Mock<IProcDumpService> _mockProcDumpService = new();

    [Fact]
    public void DisabledByDefault_ReturnsIneligible()
    {
        var options = new AutomaticRecoveryOptions { Enabled = false };
        var service = CreateService(options);

        var processes = new List<ProcessMetrics>
        {
            new() { ProcessId = 100, ProcessName = "w3wp", CpuPercent = 95.0 }
        };
        var pools = new List<AppPoolMetrics>
        {
            new() { ProcessId = 100, AppPoolName = "PatientPortalPool", CpuPercent = 95.0 }
        };

        bool eligible = service.IsRecoveryEligible(98.0, processes, pools, out _, out _, out _);
        Assert.False(eligible);
    }

    [Fact]
    public void NonIisProcess_MsMpEngHighCpu_ReturnsIneligible()
    {
        var options = new AutomaticRecoveryOptions
        {
            Enabled = true,
            MinimumCriticalDurationMinutes = 0 // for instant testing
        };
        var service = CreateService(options);

        var processes = new List<ProcessMetrics>
        {
            new() { ProcessId = 200, ProcessName = "MsMpEng.exe", CpuPercent = 85.0 },
            new() { ProcessId = 100, ProcessName = "w3wp", CpuPercent = 10.0 }
        };
        var pools = new List<AppPoolMetrics>
        {
            new() { ProcessId = 100, AppPoolName = "PatientPortalPool", CpuPercent = 10.0 }
        };

        bool eligible = service.IsRecoveryEligible(96.0, processes, pools, out _, out _, out _);
        Assert.False(eligible);
    }

    [Fact]
    public void MultipleHighConsumers_ReturnsIneligible()
    {
        var options = new AutomaticRecoveryOptions
        {
            Enabled = true,
            MinimumCriticalDurationMinutes = 0
        };
        var service = CreateService(options);

        var processes = new List<ProcessMetrics>
        {
            new() { ProcessId = 100, ProcessName = "w3wp", CpuPercent = 50.0 },
            new() { ProcessId = 101, ProcessName = "w3wp", CpuPercent = 45.0 }
        };
        var pools = new List<AppPoolMetrics>
        {
            new() { ProcessId = 100, AppPoolName = "PoolA", CpuPercent = 50.0 },
            new() { ProcessId = 101, AppPoolName = "PoolB", CpuPercent = 45.0 }
        };

        bool eligible = service.IsRecoveryEligible(96.0, processes, pools, out _, out _, out _);
        Assert.False(eligible);
    }

    [Fact]
    public void CulpritBelowThreshold_ReturnsIneligible()
    {
        var options = new AutomaticRecoveryOptions
        {
            Enabled = true,
            MinimumCriticalDurationMinutes = 0,
            MinimumCulpritCpuPercent = 70.0
        };
        var service = CreateService(options);

        var processes = new List<ProcessMetrics>
        {
            new() { ProcessId = 100, ProcessName = "w3wp", CpuPercent = 65.0 }
        };
        var pools = new List<AppPoolMetrics>
        {
            new() { ProcessId = 100, AppPoolName = "PatientPortalPool", CpuPercent = 65.0 }
        };

        bool eligible = service.IsRecoveryEligible(92.0, processes, pools, out _, out _, out _);
        Assert.False(eligible);
    }

    [Fact]
    public void CulpritSatisfiesAllCriteria_ReturnsEligible()
    {
        var options = new AutomaticRecoveryOptions
        {
            Enabled = true,
            MinimumCriticalDurationMinutes = 0,
            MinimumCulpritCpuPercent = 70.0,
            CriticalCpuThresholdPercent = 90.0
        };
        var service = CreateService(options);

        var processes = new List<ProcessMetrics>
        {
            new() { ProcessId = 100, ProcessName = "w3wp", CpuPercent = 92.0 },
            new() { ProcessId = 200, ProcessName = "MsMpEng.exe", CpuPercent = 2.0 }
        };
        var pools = new List<AppPoolMetrics>
        {
            new() { ProcessId = 100, AppPoolName = "PatientPortalPool", CpuPercent = 92.0 },
            new() { ProcessId = 300, AppPoolName = "ReportingPool", CpuPercent = 1.0 }
        };

        bool eligible = service.IsRecoveryEligible(96.0, processes, pools, out string? appPool, out int pid, out double cpu);

        Assert.True(eligible);
        Assert.Equal("PatientPortalPool", appPool);
        Assert.Equal(100, pid);
        Assert.Equal(92.0, cpu);
    }

    [Fact]
    public async Task ExecuteRecovery_CapturesDumpAndRecyclesPool()
    {
        var options = new AutomaticRecoveryOptions
        {
            Enabled = true,
            CaptureDumpBeforeRestart = true
        };
        _mockIis.Setup(x => x.RecycleAppPoolAsync("PatientPortalPool", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService(options);

        var action = await service.ExecuteRecoveryAsync("INC-01", "PatientPortalPool", 100, 92.0, 96.0);

        Assert.Equal("Successful", action.Result);
        Assert.Equal(RecoveryActionType.AppPoolRecycle, action.Action);

        // Verify procdump was triggered before recycle
        _mockProcDumpService.Verify(x => x.TriggerDumpAsync(100, "PatientPortalPool", "INC-01", It.IsAny<CancellationToken>()), Times.Once);
        // Verify only the culprit pool was recycled
        _mockIis.Verify(x => x.RecycleAppPoolAsync("PatientPortalPool", It.IsAny<CancellationToken>()), Times.Once);
        // Verify audit record was persisted
        _mockAudit.Verify(x => x.RecordRecoveryActionAsync(It.IsAny<RecoveryAction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteRecovery_AbortsIfCpuSubsidedBeforeExecution()
    {
        var options = new AutomaticRecoveryOptions { Enabled = true };
        var service = CreateService(options);

        // CPU subsided to 45% naturally
        var action = await service.ExecuteRecoveryAsync("INC-01", "PatientPortalPool", 100, 15.0, 45.0);

        Assert.Equal("AbortedNaturalRecovery", action.Result);
        Assert.Equal(RecoveryActionType.None, action.Action);

        // Should NOT recycle
        _mockIis.Verify(x => x.RecycleAppPoolAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
