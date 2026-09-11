using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Collectors;

/// <summary>
/// Collects IIS and ASP.NET request metrics via Windows Performance Counters per Section 11.
/// </summary>
public class IisRequestMetricsCollector : IDisposable
{
    private readonly ILogger<IisRequestMetricsCollector> _logger;
    private PerformanceCounter? _requestsPerSecCounter;
    private PerformanceCounter? _totalRequestsCounter;
    private PerformanceCounter? _requestsCurrentCounter;
    private PerformanceCounter? _requestsQueuedCounter;
    private bool _initialized;

    public IisRequestMetricsCollector(ILogger<IisRequestMetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitializeCounters();
    }

    private void InitializeCounters()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            if (PerformanceCounterCategory.Exists("ASP.NET"))
            {
                _requestsCurrentCounter = new PerformanceCounter("ASP.NET", "Requests Current", readOnly: true);
                _requestsQueuedCounter = new PerformanceCounter("ASP.NET", "Requests Queued", readOnly: true);
            }

            if (PerformanceCounterCategory.Exists("ASP.NET Applications"))
            {
                _requestsPerSecCounter = new PerformanceCounter("ASP.NET Applications", "Requests/Sec", "__Total__", readOnly: true);
                _totalRequestsCounter = new PerformanceCounter("ASP.NET Applications", "Requests Total", "__Total__", readOnly: true);
                _requestsPerSecCounter.NextValue();
            }

            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize ASP.NET request performance counters.");
        }
    }

    public (double RequestsPerSec, int ActiveRequests, int QueueLength, long TotalRequests) ReadRequestMetrics()
    {
        if (!_initialized) return (0, 0, 0, 0);

        try
        {
            double rps = _requestsPerSecCounter != null ? Math.Max(0, (double)_requestsPerSecCounter.NextValue()) : 0;
            int active = _requestsCurrentCounter != null ? (int)_requestsCurrentCounter.NextValue() : 0;
            int queue = _requestsQueuedCounter != null ? (int)_requestsQueuedCounter.NextValue() : 0;
            long total = _totalRequestsCounter != null ? (long)_totalRequestsCounter.NextValue() : 0;

            return (rps, active, queue, total);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }

    public void Dispose()
    {
        _requestsPerSecCounter?.Dispose();
        _totalRequestsCounter?.Dispose();
        _requestsCurrentCounter?.Dispose();
        _requestsQueuedCounter?.Dispose();
    }
}
