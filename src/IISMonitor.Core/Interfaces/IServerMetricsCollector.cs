using IISMonitor.Core.Models;

namespace IISMonitor.Core.Interfaces;

public interface IServerMetricsCollector
{
    Task<ServerMetrics> CollectAsync(CancellationToken cancellationToken = default);
}

public interface IProcessMetricsCollector
{
    Task<IReadOnlyList<ProcessMetrics>> CollectAsync(CancellationToken cancellationToken = default);
}

public interface IIisMonitor
{
    Task<IReadOnlyList<AppPoolInfo>> GetAppPoolsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebsiteInfo>> GetSitesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessMapping>> GetWorkerProcessMappingsAsync(CancellationToken cancellationToken = default);
    Task<bool> RecycleAppPoolAsync(string appPoolName, CancellationToken cancellationToken = default);
}
