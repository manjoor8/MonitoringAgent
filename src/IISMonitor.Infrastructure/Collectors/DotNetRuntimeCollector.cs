using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Collectors;

/// <summary>
/// .NET Runtime performance metrics collector per Section 13.
/// Captures GC collections, heap sizes, and % time in GC for worker processes running .NET.
/// </summary>
public class DotNetRuntimeCollector : IDotNetRuntimeCollector
{
    private readonly ILogger<DotNetRuntimeCollector> _logger;

    public DotNetRuntimeCollector(ILogger<DotNetRuntimeCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<DotNetRuntimeMetricEntry>> CollectMetricsAsync(int pid, string appPool, CancellationToken cancellationToken = default)
    {
        var results = new List<DotNetRuntimeMetricEntry>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult<IReadOnlyList<DotNetRuntimeMetricEntry>>(results);
        }

        try
        {
            // Find the performance counter instance name for this w3wp PID (e.g. w3wp, w3wp#1)
            string? instanceName = FindProcessInstanceNameByPid(pid, "w3wp");
            if (instanceName == null)
            {
                // Fallback to global or report not available
                results.Add(new DotNetRuntimeMetricEntry
                {
                    MetricName = "CLR Memory Counters",
                    MetricValue = 0,
                    Source = ".NET CLR Memory",
                    IsAvailable = false
                });
                return Task.FromResult<IReadOnlyList<DotNetRuntimeMetricEntry>>(results);
            }

            TryReadCounter(results, ".NET CLR Memory", "% Time in GC", instanceName);
            TryReadCounter(results, ".NET CLR Memory", "# Bytes in all Heaps", instanceName);
            TryReadCounter(results, ".NET CLR Memory", "# Gen 0 Collections", instanceName);
            TryReadCounter(results, ".NET CLR Memory", "# Gen 1 Collections", instanceName);
            TryReadCounter(results, ".NET CLR Memory", "# Gen 2 Collections", instanceName);
            TryReadCounter(results, ".NET CLR Memory", "Large Object Heap size", instanceName);
            TryReadCounter(results, ".NET CLR LocksAndThreads", "# of current physical Threads", instanceName);
            TryReadCounter(results, ".NET CLR LocksAndThreads", "Contention Rate / sec", instanceName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read .NET runtime metrics for PID {Pid}.", pid);
        }

        return Task.FromResult<IReadOnlyList<DotNetRuntimeMetricEntry>>(results);
    }

    private static void TryReadCounter(List<DotNetRuntimeMetricEntry> results, string category, string counter, string instance)
    {
        try
        {
            using var pc = new PerformanceCounter(category, counter, instance, readOnly: true);
            float val = pc.NextValue();
            results.Add(new DotNetRuntimeMetricEntry
            {
                MetricName = counter,
                MetricValue = Math.Round(val, 2),
                Source = category,
                IsAvailable = true
            });
        }
        catch
        {
            results.Add(new DotNetRuntimeMetricEntry
            {
                MetricName = counter,
                MetricValue = 0,
                Source = category,
                IsAvailable = false
            });
        }
    }

    private static string? FindProcessInstanceNameByPid(int targetPid, string processPrefix)
    {
        try
        {
            var category = new PerformanceCounterCategory("Process");
            string[] instanceNames = category.GetInstanceNames();

            foreach (var instance in instanceNames)
            {
                if (instance.StartsWith(processPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    using var pidCounter = new PerformanceCounter("Process", "ID Process", instance, readOnly: true);
                    if ((int)pidCounter.NextValue() == targetPid)
                    {
                        return instance;
                    }
                }
            }
        }
        catch
        {
            // Category or instance access failed
        }

        return null;
    }
}
