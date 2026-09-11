using IISMonitor.Core.Enums;
using IISMonitor.Core.Models;

namespace IISMonitor.Core.Interfaces;

public interface IIisLogParser
{
    Task<IReadOnlyList<IisLogEntry>> ParseNewEntriesAsync(CancellationToken cancellationToken = default);
    Task<IisLogIncidentAnalysis> AnalyzeIncidentWindowAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}

public interface IWindowsEventCollector
{
    Task<IReadOnlyList<WindowsEventEntry>> CollectEventsAsync(DateTime startUtc, DateTime endUtc, string[]? providers = null, CancellationToken cancellationToken = default);
}

public interface IScheduledTaskCollector
{
    Task<IReadOnlyList<ScheduledTaskStatus>> GetTasksAsync(CancellationToken cancellationToken = default);
}

public interface IDotNetRuntimeCollector
{
    Task<IReadOnlyList<DotNetRuntimeMetricEntry>> CollectMetricsAsync(int pid, string appPool, CancellationToken cancellationToken = default);
}

public interface IProcDumpService
{
    Task<bool> TriggerDumpAsync(int pid, string appPool, string incidentId, CancellationToken cancellationToken = default);
    int GetDumpCountForIncident(string incidentId);
}

public interface IAlertService
{
    Task SendAlertAsync(AlertType type, IncidentSummary incident, string? details = null, CancellationToken cancellationToken = default);
}

public interface IIncidentService
{
    Task<IncidentSummary> StartIncidentAsync(string incidentId, double triggerCpu, CancellationToken cancellationToken = default);
    Task UpdateIncidentAsync(IncidentSummary incident, CancellationToken cancellationToken = default);
    Task<IncidentSummary?> EndIncidentAsync(string incidentId, CancellationToken cancellationToken = default);
    Task<IncidentSummary?> GetIncidentAsync(string incidentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IncidentSummary>> GetIncidentsAsync(int take = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SimilarIncidentResult>> FindSimilarIncidentsAsync(string incidentId, CancellationToken cancellationToken = default);
    Task<RootCauseEvidence> GenerateRootCauseEvidenceAsync(string incidentId, CancellationToken cancellationToken = default);
}

public interface IAutoRecoveryService
{
    bool IsRecoveryEligible(
        double serverCpu,
        IReadOnlyList<ProcessMetrics> processes,
        IReadOnlyList<AppPoolMetrics> appPools,
        out string? culpritAppPool,
        out int culpritPid,
        out double culpritCpu);

    Task<RecoveryAction> ExecuteRecoveryAsync(
        string incidentId,
        string appPoolName,
        int pid,
        double culpritCpu,
        double serverCpu,
        CancellationToken cancellationToken = default);

    Task VerifyRecoveryAsync(
        RecoveryAction recoveryAction,
        Func<Task<double>> currentCpuProvider,
        CancellationToken cancellationToken = default);
}

public interface IRecoveryAuditSink
{
    Task RecordRecoveryActionAsync(RecoveryAction action, CancellationToken cancellationToken = default);
    Task UpdateVerificationAsync(string incidentId, int intervalSeconds, double cpu, CancellationToken cancellationToken = default);
}
