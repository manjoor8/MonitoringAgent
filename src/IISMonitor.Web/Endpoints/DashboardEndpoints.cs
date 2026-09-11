using System.Text.Json;
using IISMonitor.Core.Collections;
using IISMonitor.Core.Configuration;
using IISMonitor.Core.Enums;
using IISMonitor.Core.Interfaces;
using IISMonitor.Core.Models;
using IISMonitor.Core.StateMachine;
using IISMonitor.Data.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IISMonitor.Web.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // 1. Dashboard summary
        group.MapGet("/dashboard", (
            CircularBuffer<MonitoringSample> buffer,
            CpuMonitoringStateMachine stateMachine,
            IIncidentService incidentService,
            ServerIdentity serverIdentity) =>
        {
            var latest = buffer.TakeLatest(1).FirstOrDefault();
            var recentSamples = buffer.Snapshot();

            var currentCpu = latest?.ServerMetrics.TotalCpuPercent ?? 0;
            var currentMem = latest?.ServerMetrics.CommittedMemoryPercent ?? 0;
            var availableMb = latest?.ServerMetrics.AvailableMemoryMb ?? 0;

            var topProcesses = latest?.TopProcesses.Take(10).ToList() ?? new();
            var topPools = latest?.AppPoolMetrics.Take(10).ToList() ?? new();

            return Results.Ok(new
            {
                Server = serverIdentity.MachineName,
                Environment = serverIdentity.EnvironmentName,
                ServerId = serverIdentity.ServerId,
                CurrentCpuPercent = currentCpu,
                CommittedMemoryPercent = currentMem,
                AvailableMemoryMb = availableMb,
                State = stateMachine.CurrentState.ToString(),
                CurrentIncidentId = stateMachine.CurrentIncidentId,
                ConsecutiveRecoverySamples = stateMachine.ConsecutiveRecoverySamples,
                TopProcesses = topProcesses,
                TopAppPools = topPools,
                SampleCount = recentSamples.Length
            });
        });

        // 2. Recent metrics snapshot (rolling baseline)
        group.MapGet("/metrics/recent", (CircularBuffer<MonitoringSample> buffer) =>
        {
            var snapshot = buffer.Snapshot();
            return Results.Ok(snapshot);
        });

        // 3. Live SSE metrics stream
        group.MapGet("/metrics/live", async (
            HttpContext context,
            CircularBuffer<MonitoringSample> buffer,
            CancellationToken ct) =>
        {
            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("Connection", "keep-alive");

            while (!ct.IsCancellationRequested)
            {
                var latest = buffer.TakeLatest(1).FirstOrDefault();
                if (latest != null)
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        Timestamp = latest.TimestampUtc,
                        Cpu = latest.ServerMetrics.TotalCpuPercent,
                        UserCpu = latest.ServerMetrics.UserCpuPercent,
                        PrivCpu = latest.ServerMetrics.PrivilegedCpuPercent,
                        MemPercent = latest.ServerMetrics.CommittedMemoryPercent,
                        TopProcess = latest.TopProcesses.FirstOrDefault()?.ProcessName,
                        TopProcessCpu = latest.TopProcesses.FirstOrDefault()?.CpuPercent ?? 0,
                        TopAppPool = latest.AppPoolMetrics.FirstOrDefault()?.AppPoolName,
                        TopAppPoolCpu = latest.AppPoolMetrics.FirstOrDefault()?.CpuPercent ?? 0
                    });

                    await context.Response.WriteAsync($"data: {json}\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                }

                await Task.Delay(2000, ct);
            }
        });
    }
}
