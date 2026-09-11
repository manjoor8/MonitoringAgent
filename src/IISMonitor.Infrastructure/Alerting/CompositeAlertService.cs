using System.Diagnostics;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace IISMonitor.Infrastructure.Alerting;

public class CompositeAlertService : IAlertService
{
    private readonly AlertingOptions _options;
    private readonly ILogger<CompositeAlertService> _logger;

    public CompositeAlertService(AlertingOptions options, ILogger<CompositeAlertService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendAlertAsync(AlertType type, IncidentSummary incident, string? details = null, CancellationToken cancellationToken = default)
    {
        string title = $"[IIS CPU Alert] {type} on {incident.ServerName}: Incident {incident.IncidentId}";
        string body = $@"
IIS CPU INCIDENT ALERT
----------------------------------------
Server:           {incident.ServerName}
Incident:         {incident.IncidentId}
Type:             {type}
Peak CPU:         {incident.PeakCpuPercent:F1}%
Top Process:      {incident.TopProcessName} ({incident.TopProcessPeakCpu:F1}%)
Top AppPool:      {incident.TopAppPoolName} ({incident.TopAppPoolPeakCpu:F1}%)
PID:              {incident.TopProcessId}
Duration:         {incident.Duration.TotalMinutes:F1} min
Status:           {incident.Status}
Dashboard:        {_options.DashboardBaseUrl}/incidents/{incident.IncidentId}
Details:          {details ?? "None"}
----------------------------------------";

        _logger.LogInformation("DISPATCHING ALERT: {Title}", title);

        // 1. Windows Event Log
        if (_options.WindowsEventLogEnabled && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WriteToEventLog(type, title, body);
        }

        // 2. Email
        if (_options.EmailEnabled && _options.ToAddresses.Count > 0)
        {
            await SendEmailAsync(title, body, cancellationToken);
        }
    }

    private void WriteToEventLog(AlertType type, string title, string body)
    {
        try
        {
            string source = _options.EventLogSource;
            if (!EventLog.SourceExists(source))
            {
                // Create source if elevated
                try
                {
                    EventLog.CreateEventSource(source, "Application");
                }
                catch
                {
                    source = "Application";
                }
            }

            var entryType = type switch
            {
                AlertType.CriticalCpu => EventLogEntryType.Error,
                AlertType.AutoRecoveryDisabled => EventLogEntryType.Error,
                AlertType.IncidentStarted => EventLogEntryType.Warning,
                AlertType.AutoRecoveryPerformed => EventLogEntryType.Warning,
                _ => EventLogEntryType.Information
            };

            EventLog.WriteEntry(source, $"{title}\n\n{body}", entryType, 1001);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write alert to Windows Event Log.");
        }
    }

    private async Task SendEmailAsync(string subject, string body, CancellationToken ct)
    {
        try
        {
            using var message = new MailMessage();
            message.From = new MailAddress(_options.FromAddress);
            foreach (var to in _options.ToAddresses)
            {
                message.To.Add(to);
            }
            message.Subject = subject;
            message.Body = body;

            using var client = new SmtpClient(_options.SmtpServer, _options.SmtpPort)
            {
                EnableSsl = _options.EnableSsl
            };

            if (!string.IsNullOrEmpty(_options.SmtpUsername))
            {
                client.Credentials = new System.Net.NetworkCredential(_options.SmtpUsername, _options.SmtpPassword);
            }

            await client.SendMailAsync(message, ct);
            _logger.LogInformation("Email alert dispatched to {Recipients}.", string.Join(", ", _options.ToAddresses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email alert.");
        }
    }
}
