using System.Collections.Concurrent;
using System.Diagnostics;
using IISMonitor.Core.Configuration;
using IISMonitor.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Diagnostics;

/// <summary>
/// Safe ProcDump execution service per Sections 15 and 36.
/// Guarantees argument sanitization, disk space safety checks, and per-incident dump limits.
/// </summary>
public class ProcDumpService : IProcDumpService
{
    private readonly DiagnosticsOptions _options;
    private readonly ILogger<ProcDumpService> _logger;
    private readonly ConcurrentDictionary<string, int> _dumpsPerIncident = new();

    public ProcDumpService(DiagnosticsOptions options, ILogger<ProcDumpService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int GetDumpCountForIncident(string incidentId)
    {
        return _dumpsPerIncident.TryGetValue(incidentId, out int count) ? count : 0;
    }

    public async Task<bool> TriggerDumpAsync(int pid, string appPool, string incidentId, CancellationToken cancellationToken = default)
    {
        if (!_options.ProcDumpEnabled)
        {
            _logger.LogDebug("ProcDump trigger skipped: ProcDump is disabled in configuration.");
            return false;
        }

        if (!File.Exists(_options.ProcDumpPath))
        {
            _logger.LogWarning("ProcDump executable not found at '{Path}'. Dump cannot be captured.", _options.ProcDumpPath);
            return false;
        }

        int currentCount = _dumpsPerIncident.GetOrAdd(incidentId, 0);
        if (currentCount >= _options.MaxDumpsPerIncident)
        {
            _logger.LogWarning("ProcDump limit reached for incident {IncidentId} ({Current}/{Max}).",
                incidentId, currentCount, _options.MaxDumpsPerIncident);
            return false;
        }

        // Check disk space safety
        if (!HasSufficientDiskSpace(_options.DumpDirectory, _options.MinFreeDiskSpaceBytes))
        {
            _logger.LogError("Insufficient free disk space in '{Dir}'. Aborting ProcDump to safeguard server health.", _options.DumpDirectory);
            return false;
        }

        try
        {
            Directory.CreateDirectory(_options.DumpDirectory);

            string dumpFileName = $"{incidentId}_{appPool}_pid{pid}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.dmp";
            string dumpFilePath = Path.Combine(_options.DumpDirectory, dumpFileName);

            var psi = new ProcessStartInfo
            {
                FileName = _options.ProcDumpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Safe argument list per Section 36 - prevents command injection
            psi.ArgumentList.Add("-accepteula");
            if (string.Equals(_options.DumpType, "Full", StringComparison.OrdinalIgnoreCase))
            {
                psi.ArgumentList.Add("-ma"); // Full memory dump
            }
            psi.ArgumentList.Add(pid.ToString());
            psi.ArgumentList.Add(dumpFilePath);

            _logger.LogInformation("Invoking ProcDump for PID {Pid} (AppPool: {Pool}, Incident: {Incident}). Output: {File}",
                pid, appPool, incidentId, dumpFilePath);

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

            await process.WaitForExitAsync(linkedCts.Token);

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                _dumpsPerIncident.AddOrUpdate(incidentId, 1, (_, c) => c + 1);
                _logger.LogInformation("ProcDump succeeded for PID {Pid}. File: {File}", pid, dumpFilePath);
                return true;
            }
            else
            {
                _logger.LogWarning("ProcDump returned non-zero exit code {Code}. Stderr: {Err}", process.ExitCode, stderr);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture ProcDump for PID {Pid}.", pid);
            return false;
        }
    }

    private bool HasSufficientDiskSpace(string targetDir, long minBytes)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(targetDir)) ?? "C:\\";
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace >= minBytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not query free disk space. Assuming safe.");
            return true;
        }
    }
}
