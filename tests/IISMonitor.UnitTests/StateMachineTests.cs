using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;
using IISMonitor.Core.StateMachine;
using Xunit;

namespace IISMonitor.UnitTests;

public class StateMachineTests
{
    private readonly MonitoringOptions _options = new()
    {
        CpuThresholdPercent = 70.0,
        CriticalCpuThresholdPercent = 90.0,
        RecoveryThresholdPercent = 60.0,
        RecoveryConsecutiveSamples = 3
    };

    [Fact]
    public void InitialState_ShouldBeNormal()
    {
        var sm = new CpuMonitoringStateMachine(_options);
        Assert.Equal(MonitoringState.Normal, sm.CurrentState);
    }

    [Theory]
    [InlineData(45.0)]
    [InlineData(50.0)]
    [InlineData(69.9)]
    public void NormalCpu_RemainsInNormal(double cpu)
    {
        var sm = new CpuMonitoringStateMachine(_options);
        var result = sm.ProcessCpuSample(cpu);

        Assert.False(result.Changed);
        Assert.Equal(MonitoringState.Normal, sm.CurrentState);
        Assert.Null(sm.CurrentIncidentId);
    }

    [Fact]
    public void CpuExceedsThreshold_TransitionsToIncidentStarting()
    {
        var sm = new CpuMonitoringStateMachine(_options);
        var result = sm.ProcessCpuSample(72.0);

        Assert.True(result.Changed);
        Assert.Equal(MonitoringState.Normal, result.PreviousState);
        Assert.Equal(MonitoringState.IncidentStarting, result.CurrentState);
        Assert.NotNull(sm.CurrentIncidentId);
        Assert.StartsWith("INC-", sm.CurrentIncidentId);
    }

    [Fact]
    public void SustainedHighCpu_TransitionsToHighDetail()
    {
        var sm = new CpuMonitoringStateMachine(_options);
        sm.ProcessCpuSample(75.0); // Normal -> IncidentStarting
        var result = sm.ProcessCpuSample(78.0); // IncidentStarting -> HighDetail

        Assert.True(result.Changed);
        Assert.Equal(MonitoringState.HighDetail, sm.CurrentState);
    }

    [Fact]
    public void CriticalThreshold_TransitionsToCritical()
    {
        var sm = new CpuMonitoringStateMachine(_options);
        sm.ProcessCpuSample(75.0); // Normal -> IncidentStarting
        sm.ProcessCpuSample(80.0); // IncidentStarting -> HighDetail
        var result = sm.ProcessCpuSample(92.0); // HighDetail -> Critical

        Assert.True(result.Changed);
        Assert.Equal(MonitoringState.Critical, sm.CurrentState);
    }

    [Fact]
    public void Hysteresis_WithinBand_RemainsInIncident()
    {
        var sm = new CpuMonitoringStateMachine(_options);
        sm.ProcessCpuSample(75.0); // IncidentStarting
        sm.ProcessCpuSample(80.0); // HighDetail

        // Drop to 65% (between recovery 60% and threshold 70%)
        var result = sm.ProcessCpuSample(65.0);

        Assert.False(result.Changed);
        Assert.Equal(MonitoringState.HighDetail, sm.CurrentState);
    }

    [Fact]
    public void Recovery_RequiresConfiguredConsecutiveSamples()
    {
        var sm = new CpuMonitoringStateMachine(_options);
        sm.ProcessCpuSample(75.0); // Normal -> IncidentStarting
        sm.ProcessCpuSample(80.0); // IncidentStarting -> HighDetail

        // Sample 1 below 60%: transitions to Recovery
        var r1 = sm.ProcessCpuSample(55.0);
        Assert.True(r1.Changed);
        Assert.Equal(MonitoringState.Recovery, sm.CurrentState);
        Assert.Equal(1, sm.ConsecutiveRecoverySamples);

        // Sample 2 below 60%: remains in Recovery
        var r2 = sm.ProcessCpuSample(52.0);
        Assert.False(r2.Changed);
        Assert.Equal(MonitoringState.Recovery, sm.CurrentState);
        Assert.Equal(2, sm.ConsecutiveRecoverySamples);

        // Sample 3 below 60%: recovers to Normal!
        var r3 = sm.ProcessCpuSample(48.0);
        Assert.True(r3.Changed);
        Assert.Equal(MonitoringState.Normal, sm.CurrentState);
        Assert.Equal(3, sm.ConsecutiveRecoverySamples);
    }

    [Fact]
    public void Recovery_InterruptedBySpike_ReturnsToHighDetail()
    {
        var sm = new CpuMonitoringStateMachine(_options);
        sm.ProcessCpuSample(75.0);
        sm.ProcessCpuSample(80.0);
        sm.ProcessCpuSample(55.0); // Enters Recovery (sample 1)
        sm.ProcessCpuSample(54.0); // Recovery (sample 2)

        // Re-spike above 70% before 3rd sample
        var reSpike = sm.ProcessCpuSample(76.0);

        Assert.True(reSpike.Changed);
        Assert.Equal(MonitoringState.HighDetail, sm.CurrentState);
        Assert.Equal(0, sm.ConsecutiveRecoverySamples);
    }

    [Fact]
    public void StateChangedEvent_FiresOnTransitions()
    {
        var sm = new CpuMonitoringStateMachine(_options);
        int eventCount = 0;
        sm.StateChanged += (_, _) => eventCount++;

        sm.ProcessCpuSample(75.0); // +1 (IncidentStarting)
        sm.ProcessCpuSample(75.0); // +0 (remains IncidentStarting/HighDetail depending on logic)
        sm.ProcessCpuSample(78.0); // +1 (HighDetail)

        Assert.Equal(2, eventCount);
    }
}
