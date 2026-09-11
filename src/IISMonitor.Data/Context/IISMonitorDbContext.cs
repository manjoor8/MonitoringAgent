using IISMonitor.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IISMonitor.Data.Context;

public class IISMonitorDbContext : DbContext
{
    public DbSet<ServerEntity> Servers => Set<ServerEntity>();
    public DbSet<IncidentEntity> Incidents => Set<IncidentEntity>();
    public DbSet<MetricSampleEntity> MetricSamples => Set<MetricSampleEntity>();
    public DbSet<ProcessSampleEntity> ProcessSamples => Set<ProcessSampleEntity>();
    public DbSet<ApplicationPoolEntity> ApplicationPools => Set<ApplicationPoolEntity>();
    public DbSet<ApplicationPoolSampleEntity> ApplicationPoolSamples => Set<ApplicationPoolSampleEntity>();
    public DbSet<WebsiteEntity> Websites => Set<WebsiteEntity>();
    public DbSet<WebsiteMetricEntity> WebsiteMetrics => Set<WebsiteMetricEntity>();
    public DbSet<IisRequestMetricEntity> IisRequestMetrics => Set<IisRequestMetricEntity>();
    public DbSet<DotNetRuntimeMetricEntity> DotNetRuntimeMetrics => Set<DotNetRuntimeMetricEntity>();
    public DbSet<WindowsEventEntity> WindowsEvents => Set<WindowsEventEntity>();
    public DbSet<ScheduledTaskEntity> ScheduledTasks => Set<ScheduledTaskEntity>();
    public DbSet<DiagnosticArtifactEntity> DiagnosticArtifacts => Set<DiagnosticArtifactEntity>();
    public DbSet<ProcessMappingEntity> ProcessMappings => Set<ProcessMappingEntity>();
    public DbSet<ConfigurationSnapshotEntity> ConfigurationSnapshots => Set<ConfigurationSnapshotEntity>();
    public DbSet<RecoveryActionEntity> RecoveryActions => Set<RecoveryActionEntity>();
    public DbSet<IncidentFingerprintEntity> IncidentFingerprints => Set<IncidentFingerprintEntity>();

    public IISMonitorDbContext(DbContextOptions<IISMonitorDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Required indexes per Section 23
        modelBuilder.Entity<MetricSampleEntity>()
            .HasIndex(m => m.TimestampUtc);
        modelBuilder.Entity<MetricSampleEntity>()
            .HasIndex(m => m.IncidentId);

        modelBuilder.Entity<ProcessSampleEntity>()
            .HasIndex(p => new { p.TimestampUtc, p.ProcessId });
        modelBuilder.Entity<ProcessSampleEntity>()
            .HasIndex(p => p.IncidentId);
        modelBuilder.Entity<ProcessSampleEntity>()
            .HasIndex(p => p.ProcessName);

        modelBuilder.Entity<ApplicationPoolSampleEntity>()
            .HasIndex(a => new { a.TimestampUtc, a.AppPoolName });
        modelBuilder.Entity<ApplicationPoolSampleEntity>()
            .HasIndex(a => a.IncidentId);

        modelBuilder.Entity<IisRequestMetricEntity>()
            .HasIndex(r => r.TimestampUtc);
        modelBuilder.Entity<IisRequestMetricEntity>()
            .HasIndex(r => r.IncidentId);

        modelBuilder.Entity<WindowsEventEntity>()
            .HasIndex(w => w.TimestampUtc);
        modelBuilder.Entity<WindowsEventEntity>()
            .HasIndex(w => w.IncidentId);

        modelBuilder.Entity<ScheduledTaskEntity>()
            .HasIndex(s => s.TimestampUtc);
        modelBuilder.Entity<ScheduledTaskEntity>()
            .HasIndex(s => s.IncidentId);

        modelBuilder.Entity<IncidentEntity>()
            .HasIndex(i => i.StartTimeUtc);
        modelBuilder.Entity<IncidentEntity>()
            .HasIndex(i => i.Status);

        modelBuilder.Entity<ProcessMappingEntity>()
            .HasIndex(p => new { p.ProcessId, p.StartTimeUtc });

        modelBuilder.Entity<DiagnosticArtifactEntity>()
            .HasIndex(d => d.IncidentId);

        modelBuilder.Entity<RecoveryActionEntity>()
            .HasIndex(r => r.IncidentId);

        modelBuilder.Entity<IncidentFingerprintEntity>()
            .HasIndex(f => f.FingerprintHash);
    }
}
