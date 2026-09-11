using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.TaskScheduler;

namespace IISMonitor.Infrastructure.Collectors;

/// <summary>
/// Windows Task Scheduler collector per Section 17.
/// Discovers scheduled jobs that may correlate with intermittent CPU spikes.
/// </summary>
public class ScheduledTaskCollector : IScheduledTaskCollector
{
    private readonly ILogger<ScheduledTaskCollector> _logger;

    public ScheduledTaskCollector(ILogger<ScheduledTaskCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public System.Threading.Tasks.Task<IReadOnlyList<ScheduledTaskStatus>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ScheduledTaskStatus>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScheduledTaskStatus>>(results);
        }

        try
        {
            using var ts = new TaskService();
            foreach (var task in ts.AllTasks)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    results.Add(new ScheduledTaskStatus
                    {
                        TaskName = task.Name,
                        TaskPath = task.Path,
                        LastRunTime = task.LastRunTime == DateTime.MinValue ? null : task.LastRunTime.ToUniversalTime(),
                        NextRunTime = task.NextRunTime == DateTime.MinValue ? null : task.NextRunTime.ToUniversalTime(),
                        LastTaskResult = task.LastTaskResult,
                        State = task.State.ToString()
                    });
                }
                catch
                {
                    // Task may have been modified or inaccessible
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate Windows Scheduled Tasks.");
        }

        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScheduledTaskStatus>>(results);
    }
}
