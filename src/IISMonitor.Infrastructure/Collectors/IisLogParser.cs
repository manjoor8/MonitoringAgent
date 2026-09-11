using System.Globalization;
using System.Text.RegularExpressions;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Collectors;

/// <summary>
/// Incremental W3C IIS log file parser per Section 12.
/// Tracks byte offsets and only reads newly appended log records.
/// </summary>
public class IisLogParser : IIisLogParser
{
    private readonly ILogger<IisLogParser> _logger;
    private readonly string _logDirectory;

    private string? _currentLogFile;
    private long _lastBytePosition;
    private DateTime _lastFileModTimeUtc = DateTime.MinValue;

    public IisLogParser(ILogger<IisLogParser> logger, string? logDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "LogFiles", "W3SVC1");
    }

    public Task<IReadOnlyList<IisLogEntry>> ParseNewEntriesAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<IisLogEntry>();

        if (!Directory.Exists(_logDirectory))
        {
            return Task.FromResult<IReadOnlyList<IisLogEntry>>(entries);
        }

        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "*.log")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (logFiles.Count == 0) return Task.FromResult<IReadOnlyList<IisLogEntry>>(entries);

            var latestFile = logFiles.First();

            if (_currentLogFile != latestFile.FullName)
            {
                // Rotated to a new file or starting up
                _currentLogFile = latestFile.FullName;
                _lastBytePosition = Math.Max(0, latestFile.Length - 100_000); // Start near tail if new
                _lastFileModTimeUtc = latestFile.LastWriteTimeUtc;
            }

            if (latestFile.Length <= _lastBytePosition)
            {
                return Task.FromResult<IReadOnlyList<IisLogEntry>>(entries);
            }

            using var fs = new FileStream(_currentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(_lastBytePosition, SeekOrigin.Begin);

            using var reader = new StreamReader(fs);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var entry = ParseW3CLine(line);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            _lastBytePosition = fs.Position;
            _lastFileModTimeUtc = latestFile.LastWriteTimeUtc;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error incrementally parsing IIS logs from {Dir}.", _logDirectory);
        }

        return Task.FromResult<IReadOnlyList<IisLogEntry>>(entries);
    }

    public async Task<IisLogIncidentAnalysis> AnalyzeIncidentWindowAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var entries = await ParseNewEntriesAsync(cancellationToken);
        var windowEntries = entries.Where(e => e.TimestampUtc >= startUtc && e.TimestampUtc <= endUtc).ToList();

        if (windowEntries.Count == 0)
        {
            return new IisLogIncidentAnalysis
            {
                TotalRequests = 0,
                RequestsPerMinute = 0
            };
        }

        double durationMinutes = Math.Max(1.0, (endUtc - startUtc).TotalMinutes);

        var topUrls = windowEntries
            .GroupBy(e => e.UriStem)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        var topAvgTime = windowEntries
            .GroupBy(e => e.UriStem)
            .OrderByDescending(g => g.Average(e => e.TimeTakenMs))
            .Take(10)
            .ToDictionary(g => g.Key, g => Math.Round(g.Average(e => e.TimeTakenMs), 1));

        var statusDist = windowEntries
            .GroupBy(e => e.StatusCode)
            .ToDictionary(g => g.Key, g => g.Count());

        return new IisLogIncidentAnalysis
        {
            TotalRequests = windowEntries.Count,
            RequestsPerMinute = Math.Round(windowEntries.Count / durationMinutes, 1),
            TopUrlsByCount = topUrls,
            TopUrlsByAvgTimeMs = topAvgTime,
            StatusDistribution = statusDist,
            AvgResponseTimeMs = Math.Round(windowEntries.Average(e => e.TimeTakenMs), 1),
            MaxResponseTimeMs = windowEntries.Max(e => e.TimeTakenMs)
        };
    }

    private static IisLogEntry? ParseW3CLine(string line)
    {
        // Standard W3C format: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status time-taken
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 10) return null;

        try
        {
            if (DateTime.TryParseExact($"{parts[0]} {parts[1]}", "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                string method = parts.Length > 3 ? parts[3] : "GET";
                string uri = parts.Length > 4 ? parts[4] : "/";
                int status = 200;
                long timeTaken = 0;

                // Find status and time-taken from end
                if (parts.Length >= 14 && int.TryParse(parts[11], out int sc)) status = sc;
                if (parts.Length >= 15 && long.TryParse(parts[^1], out long tt)) timeTaken = tt;

                return new IisLogEntry
                {
                    TimestampUtc = dt,
                    Method = method,
                    UriStem = uri,
                    StatusCode = status,
                    TimeTakenMs = timeTaken,
                    ClientIp = parts.Length > 8 ? parts[8] : string.Empty
                };
            }
        }
        catch
        {
            // Ignore malformed line
        }

        return null;
    }
}
