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
    public DbSet<ProviderDiagnostic> ProviderDiagnostics { get; set; } = null!;
    public DbSet<AppSetting> AppSettings { get; set; } = null!;
    public DbSet<ReadingHistory> ReadingHistories { get; set; } = null!;
    public DbSet<CoverCache> CoverCaches { get; set; } = null!;
    public DbSet<DisplayProfile> DisplayProfiles { get; set; } = null!;
    public DbSet<DisplayProfileField> DisplayProfileFields { get; set; } = null!;
    public DbSet<Collection> Collections { get; set; } = null!;
    public DbSet<CollectionItem> CollectionItems { get; set; } = null!;
    public DbSet<HttpCache> HttpCaches { get; set; } = null!;

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
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.MediaType);
            entity.HasIndex(e => e.Favorite);

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

        modelBuilder.Entity<ProviderDiagnostic>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProviderId).IsUnique();
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).IsRequired();
            entity.Property(e => e.Value).IsRequired().HasDefaultValue("");
        });

        modelBuilder.Entity<MetadataField>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.FieldName, e.Value });
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

        modelBuilder.Entity<ReadingHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.WorkId, e.DeviceId }).IsUnique();
            entity.HasIndex(e => e.WorkId);
            entity.HasIndex(e => new { e.DeviceId, e.LastReadAt });

            entity.HasOne(e => e.Work)
                  .WithMany()
                  .HasForeignKey(e => e.WorkId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CoverCache>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.WorkId, e.ProviderName }).IsUnique();
            entity.HasIndex(e => e.WorkId);

            entity.HasOne(e => e.Work)
                  .WithMany()
                  .HasForeignKey(e => e.WorkId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DisplayProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MediaType).IsUnique();

            entity.Metadata.FindNavigation(nameof(DisplayProfile.Fields))
                ?.SetPropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(e => e.Fields)
                  .WithOne(f => f.Profile)
                  .HasForeignKey(f => f.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DisplayProfileField>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProfileId, e.FieldName }).IsUnique();
            entity.HasIndex(e => new { e.ProfileId, e.DisplayOrder });
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.HasMany(e => e.Items)
                  .WithOne(i => i.Collection)
                  .HasForeignKey(i => i.CollectionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CollectionItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CollectionId, e.WorkId }).IsUnique();
            entity.HasIndex(e => e.CollectionId);
            entity.HasOne(e => e.Work)
                  .WithMany()
                  .HasForeignKey(e => e.WorkId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HttpCache>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Url).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.Url).IsRequired();
            entity.Property(e => e.Body).IsRequired();
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await ApplyEntityStateFixesAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyEntityStateFixesAsync(CancellationToken cancellationToken)
    {
        // Fix for EF Core incorrectly marking new child entities with generated GUIDs as Modified
        var modifiedEntries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified && (e.Entity is Work || e.Entity is Asset || e.Entity is MetadataField))
            .ToList();

        foreach (var entry in modifiedEntries)
        {
            if (entry.Entity is Work work)
            {
                bool exists = await Works.AsNoTracking().AnyAsync(w => w.Id == work.Id, cancellationToken);
                if (!exists)
                    entry.State = EntityState.Added;
            }
            else if (entry.Entity is Asset asset)
            {
                bool exists = await Assets.AsNoTracking().AnyAsync(a => a.Id == asset.Id, cancellationToken);
                if (!exists)
                {
                    entry.State = EntityState.Added;
                }
            }
            else if (entry.Entity is MetadataField field)
            {
                bool exists = await MetadataFields.AsNoTracking().AnyAsync(m => m.Id == field.Id, cancellationToken);
                if (!exists)
                {
                    entry.State = EntityState.Added;
                }
            }
        }
    }

    public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
    {
        await ApplyEntityStateFixesAsync(cancellationToken);
        _ = await base.SaveChangesAsync(cancellationToken);
        return true;
    }
}
