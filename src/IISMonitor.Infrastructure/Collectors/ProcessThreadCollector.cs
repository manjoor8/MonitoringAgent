using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Collectors;

public record ProcessThreadInfo(
    int ThreadId,
    double TotalProcessorTimeMs,
    string ThreadState,
    DateTime StartTimeUtc);

/// <summary>
/// Gathers thread-level execution diagnostics for high-CPU processes per Section 14.
/// </summary>
public class ProcessThreadCollector
{
    private readonly ILogger<ProcessThreadCollector> _logger;

    public ProcessThreadCollector(ILogger<ProcessThreadCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public List<ProcessThreadInfo> CollectThreads(int pid)
    {
        var results = new List<ProcessThreadInfo>();

        try
        {
            using var proc = Process.GetProcessById(pid);
            foreach (ProcessThread thread in proc.Threads)
            {
                try
                {
                    results.Add(new ProcessThreadInfo(
                        thread.Id,
                        thread.TotalProcessorTime.TotalMilliseconds,
                        thread.ThreadState.ToString(),
                        thread.StartTime.ToUniversalTime()
                    ));
                }
                catch
                {
                    // Thread may have exited
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not enumerate threads for process {Pid}.", pid);
        }

        return results.OrderByDescending(t => t.TotalProcessorTimeMs).ToList();
    }
}
