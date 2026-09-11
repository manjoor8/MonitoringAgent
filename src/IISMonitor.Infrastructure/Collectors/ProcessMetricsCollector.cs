using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Collectors;

/// <summary>
/// Process metrics collector per Section 7.
/// Calculates per-process CPU percentage across sample intervals and gathers memory, thread, and handle counts.
/// </summary>
public class ProcessMetricsCollector : IProcessMetricsCollector
{
    private readonly ILogger<ProcessMetricsCollector> _logger;

    private class ProcessSnapshot
    {
        public TimeSpan CpuTime { get; set; }
        public long TimestampTicks { get; set; }
    }

    private readonly ConcurrentDictionary<int, ProcessSnapshot> _snapshots = new();

    public ProcessMetricsCollector(ILogger<ProcessMetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<ProcessMetrics>> CollectAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ProcessMetrics>();
        var currentPids = new HashSet<int>();

        var processes = Process.GetProcesses();
        long nowTicks = Stopwatch.GetTimestamp();
        int processorCount = Math.Max(1, Environment.ProcessorCount);

        foreach (var p in processes)
        {
            if (cancellationToken.IsCancellationRequested) break;

            int pid = p.Id;
            currentPids.Add(pid);

            try
            {
                string processName = p.ProcessName;
                TimeSpan currentCpuTime = p.TotalProcessorTime;

                double cpuPercent = 0.0;

                if (_snapshots.TryGetValue(pid, out var previous))
                {
                    double cpuDeltaMs = (currentCpuTime - previous.CpuTime).TotalMilliseconds;
                    double wallClockDeltaMs = Stopwatch.GetElapsedTime(previous.TimestampTicks, nowTicks).TotalMilliseconds;

                    if (wallClockDeltaMs > 10.0)
                    {
                        cpuPercent = (cpuDeltaMs / (wallClockDeltaMs * processorCount)) * 100.0;
                        cpuPercent = Math.Clamp(cpuPercent, 0.0, 100.0);
                    }

                    previous.CpuTime = currentCpuTime;
                    previous.TimestampTicks = nowTicks;
                }
                else
                {
                    _snapshots[pid] = new ProcessSnapshot
                    {
                        CpuTime = currentCpuTime,
                        TimestampTicks = nowTicks
                    };
                }

                // Filter: Collect w3wp, security processes, system, or processes consuming CPU (> 0.5%)
                bool isW3wp = string.Equals(processName, "w3wp", StringComparison.OrdinalIgnoreCase);
                bool isSecurity = string.Equals(processName, "MsMpEng", StringComparison.OrdinalIgnoreCase) ||
                                  processName.Contains("Defender", StringComparison.OrdinalIgnoreCase);

                long privateBytes = 0;
                long workingSet = 0;
                long virtualMemory = 0;
                int threadCount = 0;
                int handleCount = 0;
                DateTime? startTime = null;
                double uptimeSeconds = 0;
                int? parentPid = null;

                try
                {
                    privateBytes = p.PrivateMemorySize64;
                    workingSet = p.WorkingSet64;
                    virtualMemory = p.VirtualMemorySize64;
                    threadCount = p.Threads.Count;
                    handleCount = p.HandleCount;
                    startTime = p.StartTime.ToUniversalTime();
                    uptimeSeconds = (DateTime.UtcNow - startTime.Value).TotalSeconds;

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        parentPid = GetParentProcessId(p);
                    }
                }
                catch
                {
                    // Access denied on some system processes (e.g. System, Audiodg)
                }

                results.Add(new ProcessMetrics
                {
                    ProcessId = pid,
                    ProcessName = processName,
                    CpuPercent = Math.Round(cpuPercent, 2),
                    TotalProcessorTimeMs = currentCpuTime.TotalMilliseconds,
                    PrivateBytes = privateBytes,
                    WorkingSet = workingSet,
                    VirtualMemory = virtualMemory,
                    ThreadCount = threadCount,
                    HandleCount = handleCount,
                    StartTimeUtc = startTime,
                    UptimeSeconds = Math.Max(0, uptimeSeconds),
                    ParentProcessId = parentPid
                });
            }
            catch
            {
                // Process terminated or inaccessible
            }
            finally
            {
                p.Dispose();
            }
        }

        // Evict terminated processes from snapshot cache
        foreach (var key in _snapshots.Keys)
        {
            if (!currentPids.Contains(key))
            {
                _snapshots.TryRemove(key, out _);
            }
        }

        // Return sorted by CPU descending
        var sorted = results.OrderByDescending(p => p.CpuPercent).ToList();
        return Task.FromResult<IReadOnlyList<ProcessMetrics>>(sorted);
    }

    #region Win32 P/Invoke for Parent Process ID

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    private static int? GetParentProcessId(Process process)
    {
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(process.Handle, 0, ref pbi, Marshal.SizeOf(pbi), out _);
            if (status == 0)
            {
                return pbi.InheritedFromUniqueProcessId.ToInt32();
            }
        }
        catch
        {
            // Access denied or 32/64-bit mismatch
        }

        return null;
    }

    #endregion
}
