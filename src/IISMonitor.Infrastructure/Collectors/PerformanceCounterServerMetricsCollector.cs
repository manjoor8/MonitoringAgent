using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Collectors;

/// <summary>
/// Server performance metrics collector using Windows Performance Counters per Section 6.
/// Reads % Processor Time, % User Time, % Privileged Time, Processor Queue Length, and Memory.
/// </summary>
[SupportedOSPlatform("windows")]
public class PerformanceCounterServerMetricsCollector : IServerMetricsCollector, IDisposable
{
    private readonly ILogger<PerformanceCounterServerMetricsCollector> _logger;
    private PerformanceCounter? _totalCpuCounter;
    private PerformanceCounter? _userCpuCounter;
    private PerformanceCounter? _privCpuCounter;
    private PerformanceCounter? _queueLengthCounter;
    private PerformanceCounter? _availableMemoryCounter;
    private PerformanceCounter? _committedBytesCounter;

    private bool _initialized;
    private bool _disposed;

    public PerformanceCounterServerMetricsCollector(ILogger<PerformanceCounterServerMetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitializeCounters();
    }

    private void InitializeCounters()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("Current OS is not Windows. Performance counters are disabled.");
            return;
        }

        try
        {
            _totalCpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _userCpuCounter = new PerformanceCounter("Processor", "% User Time", "_Total", readOnly: true);
            _privCpuCounter = new PerformanceCounter("Processor", "% Privileged Time", "_Total", readOnly: true);
            _queueLengthCounter = new PerformanceCounter("System", "Processor Queue Length", readOnly: true);

            // Memory counters
            try
            {
                _availableMemoryCounter = new PerformanceCounter("Memory", "Available MBytes", readOnly: true);
                _committedBytesCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use", readOnly: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not initialize memory performance counters.");
            }

            // Prime the rate counters (first read is always 0.0)
            _totalCpuCounter.NextValue();
            _userCpuCounter.NextValue();
            _privCpuCounter.NextValue();

            _initialized = true;
            _logger.LogInformation("Windows Performance Counters initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Windows Performance Counters. Fallback mode will be used.");
            _initialized = false;
        }
    }

    public Task<ServerMetrics> CollectAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            // Fallback estimation using GC and Process
            return Task.FromResult(new ServerMetrics
            {
                TimestampUtc = DateTime.UtcNow,
                TotalCpuPercent = 0,
                UserCpuPercent = 0,
                PrivilegedCpuPercent = 0,
                ProcessorQueueLength = 0,
                AvailableMemoryMb = 0,
                CommittedMemoryPercent = 0
            });
        }

        try
        {
            double totalCpu = _totalCpuCounter != null ? Math.Clamp((double)_totalCpuCounter.NextValue(), 0.0, 100.0) : 0.0;
            double userCpu = _userCpuCounter != null ? Math.Clamp((double)_userCpuCounter.NextValue(), 0.0, 100.0) : 0.0;
            double privCpu = _privCpuCounter != null ? Math.Clamp((double)_privCpuCounter.NextValue(), 0.0, 100.0) : 0.0;
            double queueLength = _queueLengthCounter != null ? (double)_queueLengthCounter.NextValue() : 0.0;
            double availableMb = _availableMemoryCounter != null ? (double)_availableMemoryCounter.NextValue() : 0.0;
            double committedPercent = _committedBytesCounter != null ? (double)_committedBytesCounter.NextValue() : 0.0;

            var metrics = new ServerMetrics
            {
                TimestampUtc = DateTime.UtcNow,
                TotalCpuPercent = totalCpu,
                UserCpuPercent = userCpu,
                PrivilegedCpuPercent = privCpu,
                ProcessorQueueLength = queueLength,
                AvailableMemoryMb = availableMb,
                CommittedMemoryPercent = committedPercent,
                LogicalProcessorCount = Environment.ProcessorCount
            };

            return Task.FromResult(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read server performance counters.");
            return Task.FromResult(new ServerMetrics
            {
                TimestampUtc = DateTime.UtcNow,
                TotalCpuPercent = 0
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _totalCpuCounter?.Dispose();
        _userCpuCounter?.Dispose();
        _privCpuCounter?.Dispose();
        _queueLengthCounter?.Dispose();
        _availableMemoryCounter?.Dispose();
        _committedBytesCounter?.Dispose();
    }
}
