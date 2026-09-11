using System.Threading.Channels;
using IISMonitor.Core.Models;
using IISMonitor.Data.Context;
using IISMonitor.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Data.Services;

public record MetricWriteBatch(
    MonitoringSample Sample,
    IReadOnlyList<ProcessMetrics> Processes,
    IReadOnlyList<AppPoolMetrics> AppPools);

/// <summary>
/// Background writer service implementing the producer-consumer pattern via Channel.
/// Batches metric persistence to SQLite in single transactions per Section 23 and 39.
/// </summary>
public class MetricBatchWriter : BackgroundService
{
    private readonly Channel<MetricWriteBatch> _channel;
    private readonly IDbContextFactory<IISMonitorDbContext> _contextFactory;
    private readonly ILogger<MetricBatchWriter> _logger;

    private const int MaxBatchSize = 50;
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(3);

    public MetricBatchWriter(
        Channel<MetricWriteBatch> channel,
        IDbContextFactory<IISMonitorDbContext> contextFactory,
        ILogger<MetricBatchWriter> logger)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetricBatchWriter service started.");
        var batchBuffer = new List<MetricWriteBatch>(MaxBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(FlushTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

                while (batchBuffer.Count < MaxBatchSize)
                {
                    if (await _channel.Reader.WaitToReadAsync(linkedCts.Token))
                    {
                        while (batchBuffer.Count < MaxBatchSize && _channel.Reader.TryRead(out var item))
                        {
                            batchBuffer.Add(item);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Timeout reached, flush what we have
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from metric write channel.");
            }

            if (batchBuffer.Count > 0)
            {
                await FlushBatchesAsync(batchBuffer, stoppingToken);
                batchBuffer.Clear();
            }
        }

        // Final drain on shutdown
        while (_channel.Reader.TryRead(out var remaining))
        {
            batchBuffer.Add(remaining);
        }

        if (batchBuffer.Count > 0)
        {
            await FlushBatchesAsync(batchBuffer, CancellationToken.None);
        }

        _logger.LogInformation("MetricBatchWriter service stopped.");
    }

    private async Task FlushBatchesAsync(List<MetricWriteBatch> batches, CancellationToken ct)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var metricSamples = new List<MetricSampleEntity>();
            var processSamples = new List<ProcessSampleEntity>();
            var appPoolSamples = new List<ApplicationPoolSampleEntity>();

            foreach (var b in batches)
            {
                var s = b.Sample;
                metricSamples.Add(new MetricSampleEntity
                {
                    TimestampUtc = s.TimestampUtc,
                    IncidentId = s.IncidentId,
                    TotalCpu = s.ServerMetrics.TotalCpuPercent,
                    UserCpu = s.ServerMetrics.UserCpuPercent,
                    PrivilegedCpu = s.ServerMetrics.PrivilegedCpuPercent,
                    ProcessorQueueLength = s.ServerMetrics.ProcessorQueueLength,
                    AvailableMemoryMb = s.ServerMetrics.AvailableMemoryMb,
                    CommittedMemoryPercent = s.ServerMetrics.CommittedMemoryPercent,
                    IsBaseline = s.IsBaseline,
                    IsHighDetail = s.IsHighDetail
                });

                if (b.Processes != null)
                {
                    foreach (var p in b.Processes)
                    {
                        processSamples.Add(new ProcessSampleEntity
                        {
                            TimestampUtc = s.TimestampUtc,
                            IncidentId = s.IncidentId,
                            ProcessId = p.ProcessId,
                            ProcessName = p.ProcessName,
                            CpuPercent = p.CpuPercent,
                            CpuTimeMs = p.TotalProcessorTimeMs,
                            PrivateBytes = p.PrivateBytes,
                            WorkingSet = p.WorkingSet,
                            VirtualMemory = p.VirtualMemory,
                            ThreadCount = p.ThreadCount,
                            HandleCount = p.HandleCount,
                            StartTimeUtc = p.StartTimeUtc,
                            UptimeSeconds = p.UptimeSeconds,
                            ParentProcessId = p.ParentProcessId
                        });
                    }
                }

                if (b.AppPools != null)
                {
                    foreach (var a in b.AppPools)
                    {
                        appPoolSamples.Add(new ApplicationPoolSampleEntity
                        {
                            TimestampUtc = s.TimestampUtc,
                            IncidentId = s.IncidentId,
                            AppPoolName = a.AppPoolName,
                            ProcessId = a.ProcessId,
                            CpuPercent = a.CpuPercent,
                            CpuTimeMs = a.TotalProcessorTimeMs,
                            PrivateMemory = a.PrivateMemoryBytes,
                            WorkingSet = a.WorkingSetBytes,
                            ThreadCount = a.ThreadCount,
                            HandleCount = a.HandleCount,
                            ProcessAgeSeconds = a.ProcessAgeSeconds,
                            RequestCount = a.RequestCount,
                            QueueLength = a.QueueLength,
                            State = a.State
                        });
                    }
                }
            }

            await context.MetricSamples.AddRangeAsync(metricSamples, ct);
            await context.ProcessSamples.AddRangeAsync(processSamples, ct);
            await context.ApplicationPoolSamples.AddRangeAsync(appPoolSamples, ct);

            await context.SaveChangesAsync(ct);

            _logger.LogDebug("Persisted {MetricCount} metric, {ProcCount} process, {PoolCount} pool samples to SQLite.",
                metricSamples.Count, processSamples.Count, appPoolSamples.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush metric batch to SQLite.");
        }
    }
}
