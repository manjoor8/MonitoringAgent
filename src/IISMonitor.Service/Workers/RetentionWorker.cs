using IISMonitor.Data.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Service.Workers;

/// <summary>
/// Daily retention cleanup background service per Section 24.
/// </summary>
public class RetentionWorker : BackgroundService
{
    private readonly RetentionService _retentionService;
    private readonly ILogger<RetentionWorker> _logger;
    private static readonly TimeSpan DailyInterval = TimeSpan.FromHours(24);

    public RetentionWorker(RetentionService retentionService, ILogger<RetentionWorker> logger)
    {
        _retentionService = retentionService ?? throw new ArgumentNullException(nameof(retentionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial brief delay on startup
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _retentionService.RunCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during daily retention worker execution.");
            }

            try
            {
                await Task.Delay(DailyInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
