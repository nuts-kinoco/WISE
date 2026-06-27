using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.SeedWork;
using WISE.Infrastructure.Data.Models;

namespace WISE.Infrastructure.Data;

public class WiseDbContext : DbContext, IUnitOfWork
{
    public DbSet<Work> Works { get; set; } = null!;
    public DbSet<Asset> Assets { get; set; } = null!;
    public DbSet<MetadataField> MetadataFields { get; set; } = null!;
    public DbSet<HistoryRecord> HistoryRecords { get; set; } = null!;
    public DbSet<Job> Jobs { get; set; } = null!;
    public DbSet<JobLogRecord> JobLogRecords { get; set; } = null!;
    public DbSet<EventLog> EventLogs => Set<EventLog>();
    public DbSet<WatchFolder> WatchFolders { get; set; } = null!;

    public WiseDbContext(DbContextOptions<WiseDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Work>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PrimaryIdentifier);

            // Backing fields configuration for encapsulating collections
            entity.Metadata.FindNavigation(nameof(Work.Assets))
                ?.SetPropertyAccessMode(PropertyAccessMode.Field);
            
            entity.Metadata.FindNavigation(nameof(Work.MetadataFields))
                ?.SetPropertyAccessMode(PropertyAccessMode.Field);
            
            entity.Metadata.FindNavigation(nameof(Work.EventLogs))
                ?.SetPropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(e => e.Assets)
                  .WithOne(a => a.Work)
                  .HasForeignKey(a => a.WorkId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.MetadataFields)
                  .WithOne(m => m.Work)
                  .HasForeignKey(m => m.WorkId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.EventLogs)
                  .WithOne(el => el.TargetWork)
                  .HasForeignKey(el => el.TargetId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Sha256);
        });

        modelBuilder.Entity<MetadataField>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<EventLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TargetId);
        });

        modelBuilder.Entity<WatchFolder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Path).IsRequired();
            entity.HasIndex(e => e.Path).IsUnique();
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<HistoryRecord>(entity =>
        {
            entity.ToTable("HISTORY_RECORD");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EventId).HasColumnName("event_id").IsRequired();
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
            entity.Property(e => e.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(100);
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
            entity.Property(e => e.WorkId).HasColumnName("work_id");
            entity.Property(e => e.AssetId).HasColumnName("asset_id");
            entity.Property(e => e.SchemaVersion).HasColumnName("schema_version").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();

            entity.HasIndex(e => e.WorkId);
            entity.HasIndex(e => e.AssetId);
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<JobDefinition>(entity =>
        {
            entity.ToTable("JOB_DEFINITION");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.JobType);
        });

        modelBuilder.Entity<JobExecution>(entity =>
        {
            entity.ToTable("JOB_EXECUTION");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.JobDefinition)
                  .WithMany()
                  .HasForeignKey(e => e.JobDefinitionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobLogRecord>(entity =>
        {
            entity.ToTable("JOB_LOG");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.JobExecutionId);

            entity.HasOne(e => e.JobExecution)
                  .WithMany()
                  .HasForeignKey(e => e.JobExecutionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
    {
        // Dispatch Domain Events here in the future
        _ = await base.SaveChangesAsync(cancellationToken);
        return true;
    }
}
