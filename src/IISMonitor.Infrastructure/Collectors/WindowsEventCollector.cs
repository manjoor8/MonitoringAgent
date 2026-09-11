using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Collectors;

/// <summary>
/// Windows Event Log reader using structured XPath queries per Section 16.
/// Scopes specifically to WAS, W3SVC, .NET Runtime, and Windows Error Reporting.
/// </summary>
public class WindowsEventCollector : IWindowsEventCollector
{
    private readonly ILogger<WindowsEventCollector> _logger;

    private static readonly string[] DefaultProviders = new[]
    {
        "Microsoft-Windows-WAS",
        "Microsoft-Windows-IIS-W3SVC-WP",
        ".NET Runtime",
        "Windows Error Reporting",
        "Application Error"
    };

    public WindowsEventCollector(ILogger<WindowsEventCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<WindowsEventEntry>> CollectEventsAsync(
        DateTime startUtc,
        DateTime endUtc,
        string[]? providers = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<WindowsEventEntry>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult<IReadOnlyList<WindowsEventEntry>>(results);
        }

        var targetProviders = providers ?? DefaultProviders;

        try
        {
            string startStr = startUtc.ToString("o");
            string endStr = endUtc.ToString("o");

            // Query System Log
            QueryLog("System", targetProviders, startStr, endStr, results, cancellationToken);

            // Query Application Log
            QueryLog("Application", targetProviders, startStr, endStr, results, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Windows Event Log.");
        }

        return Task.FromResult<IReadOnlyList<WindowsEventEntry>>(results.OrderBy(r => r.TimestampUtc).ToList());
    }

    private void QueryLog(
        string logName,
        string[] providers,
        string startStr,
        string endStr,
        List<WindowsEventEntry> results,
        CancellationToken ct)
    {
        try
        {
            var providerConditions = string.Join(" or ", providers.Select(p => $"@Name='{p}'"));
            string xpath = $"*[System[({providerConditions}) and TimeCreated[@SystemTime >= '{startStr}' and @SystemTime <= '{endStr}']]]";

            var query = new EventLogQuery(logName, PathType.LogName, xpath)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            for (EventRecord? record = reader.ReadEvent(); record != null; record = reader.ReadEvent())
            {
                if (ct.IsCancellationRequested) break;
                using (record)
                {
                    results.Add(new WindowsEventEntry
                    {
                        TimestampUtc = record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                        ProviderName = record.ProviderName,
                        EventId = record.Id,
                        Level = record.LevelDisplayName ?? "Information",
                        Message = record.FormatDescription() ?? string.Empty,
                        LogName = logName
                    });
                }
            }
        }
        catch (EventLogNotFoundException)
        {
            // Expected if log not available
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Event log query failed for {LogName}.", logName);
        }
    }
}
