using System.Threading.Channels;
using IISMonitor.Core.Collections;
using IISMonitor.Core.Configuration;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using IISMonitor.Core.StateMachine;
using IISMonitor.Data.Context;
using IISMonitor.Data.Interceptors;
using IISMonitor.Data.Services;
using IISMonitor.Infrastructure.Alerting;
using IISMonitor.Infrastructure.AutoRecovery;
using IISMonitor.Infrastructure.Collectors;
using IISMonitor.Infrastructure.Diagnostics;
using IISMonitor.Infrastructure.Simulation;
using IISMonitor.Service.Workers;
using IISMonitor.Web.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// 1. Windows Service hosting support per Section 2
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "IISMonitorAgent";
});

// 2. Configure Kestrel to bind locally per Section 36
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenLocalhost(5050);
});

// 3. Bind configuration sections
var config = builder.Configuration;

var serverIdentity = config.GetSection(ServerIdentity.SectionName).Get<ServerIdentity>() ?? new();
var monitoringOptions = config.GetSection(MonitoringOptions.SectionName).Get<MonitoringOptions>() ?? new();
var databaseOptions = config.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new();
var diagnosticsOptions = config.GetSection(DiagnosticsOptions.SectionName).Get<DiagnosticsOptions>() ?? new();
var retentionOptions = config.GetSection(RetentionOptions.SectionName).Get<RetentionOptions>() ?? new();
var alertingOptions = config.GetSection(AlertingOptions.SectionName).Get<AlertingOptions>() ?? new();
var recoveryOptions = config.GetSection(AutomaticRecoveryOptions.SectionName).Get<AutomaticRecoveryOptions>() ?? new();
var simulationOptions = config.GetSection(SimulationOptions.SectionName).Get<SimulationOptions>() ?? new();

builder.Services.AddSingleton(serverIdentity);
builder.Services.AddSingleton(monitoringOptions);
builder.Services.AddSingleton(databaseOptions);
builder.Services.AddSingleton(diagnosticsOptions);
builder.Services.AddSingleton(retentionOptions);
builder.Services.AddSingleton(alertingOptions);
builder.Services.AddSingleton(recoveryOptions);
builder.Services.AddSingleton(simulationOptions);

// 4. In-Memory Rolling Baseline Buffer (60 minutes @ 30s = 120 samples) per Sections 3 & 22
builder.Services.AddSingleton(new CircularBuffer<MonitoringSample>(120));

// 5. Producer-Consumer Channel for Batched DB writes
builder.Services.AddSingleton(Channel.CreateBounded<MetricWriteBatch>(new BoundedChannelOptions(5000)
{
    FullMode = BoundedChannelFullMode.DropOldest
}));

// 6. SQLite Database & EF Core per Section 23
builder.Services.AddSingleton<SqlitePragmaInterceptor>();

// Extract path to ensure directory exists
try
{
    var connBuilder = new SqliteConnectionStringBuilder(databaseOptions.ConnectionString);
    string? dir = Path.GetDirectoryName(connBuilder.DataSource);
    if (!string.IsNullOrWhiteSpace(dir))
    {
        Directory.CreateDirectory(dir);
    }
}
catch
{
    // Ignore if in-memory or relative
}

builder.Services.AddDbContextFactory<IISMonitorDbContext>((sp, opts) =>
{
    opts.UseSqlite(databaseOptions.ConnectionString)
        .AddInterceptors(sp.GetRequiredService<SqlitePragmaInterceptor>());
});

// 7. State Machine
builder.Services.AddSingleton<CpuMonitoringStateMachine>();

// 8. Core & Data Services
builder.Services.AddSingleton<IIncidentService, IncidentRepository>();
builder.Services.AddSingleton<RetentionService>();
builder.Services.AddSingleton<IRecoveryAuditSink, RecoveryAuditSink>();
builder.Services.AddSingleton<IAlertService, CompositeAlertService>();
builder.Services.AddSingleton<IProcDumpService, ProcDumpService>();

// 9. Collectors (Support simulation mode per Section 46)
if (simulationOptions.Enabled)
{
    builder.Services.AddSingleton<SimulatedServerMetricsCollector>();
    builder.Services.AddSingleton<IServerMetricsCollector>(sp => sp.GetRequiredService<SimulatedServerMetricsCollector>());
    builder.Services.AddSingleton<IProcessMetricsCollector, SimulatedProcessMetricsCollector>();
    builder.Services.AddSingleton<IIisMonitor, SimulatedIisMonitor>();
}
else
{
    builder.Services.AddSingleton<IServerMetricsCollector, PerformanceCounterServerMetricsCollector>();
    builder.Services.AddSingleton<IProcessMetricsCollector, ProcessMetricsCollector>();
    builder.Services.AddSingleton<IIisMonitor, IisMonitorService>();
}

builder.Services.AddSingleton<IIisLogParser, IisLogParser>();
builder.Services.AddSingleton<IWindowsEventCollector, WindowsEventCollector>();
builder.Services.AddSingleton<IScheduledTaskCollector, ScheduledTaskCollector>();
builder.Services.AddSingleton<IDotNetRuntimeCollector, DotNetRuntimeCollector>();
builder.Services.AddSingleton<ProcessThreadCollector>();
builder.Services.AddSingleton<IisRequestMetricsCollector>();

// 10. Auto Recovery
builder.Services.AddSingleton<IAutoRecoveryService, AutoRecoveryService>();

// 11. Hosted Background Workers
builder.Services.AddHostedService<MetricBatchWriter>();
builder.Services.AddHostedService<MonitoringWorker>();
builder.Services.AddHostedService<IisConfigRefreshWorker>();
builder.Services.AddHostedService<RetentionWorker>();

var app = builder.Build();

// 12. Initialize SQLite database schema
using (var scope = app.Services.CreateScope())
{
    var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IISMonitorDbContext>>();
    await using var db = await contextFactory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();

    // Register server in database
    var existingServer = await db.Servers.FirstOrDefaultAsync(s => s.ServerId == serverIdentity.ServerId);
    if (existingServer == null)
    {
        db.Servers.Add(new IISMonitor.Data.Entities.ServerEntity
        {
            ServerId = serverIdentity.ServerId,
            MachineName = serverIdentity.MachineName,
            Environment = serverIdentity.EnvironmentName,
            RegisteredUtc = DateTime.UtcNow,
            LastSeenUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}

// 13. Web Static Assets & Endpoints
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDashboardEndpoints();
app.MapIncidentEndpoints();
app.MapConfigurationEndpoints();

app.Run();
