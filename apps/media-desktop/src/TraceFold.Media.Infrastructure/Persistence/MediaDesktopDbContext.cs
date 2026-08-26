using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Processing;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class MediaDesktopDbContext(
    DbContextOptions<MediaDesktopDbContext> options) : DbContext(options)
{
    internal DbSet<AppSettingsRow> AppSettings => Set<AppSettingsRow>();

    internal DbSet<InboxItemRow> InboxItems => Set<InboxItemRow>();

    internal DbSet<SourceFileRow> SourceFiles => Set<SourceFileRow>();

    internal DbSet<MetadataDraftRow> MetadataDrafts => Set<MetadataDraftRow>();

    internal DbSet<TaxonomyTermRow> TaxonomyTerms => Set<TaxonomyTermRow>();

    internal DbSet<InboxDraftAuthorRow> InboxDraftAuthors => Set<InboxDraftAuthorRow>();

    internal DbSet<InboxDraftTagRow> InboxDraftTags => Set<InboxDraftTagRow>();

    internal DbSet<MediaAssetRow> MediaAssets => Set<MediaAssetRow>();

    internal DbSet<MediaAssetAuthorRow> MediaAssetAuthors => Set<MediaAssetAuthorRow>();

    internal DbSet<MediaAssetTagRow> MediaAssetTags => Set<MediaAssetTagRow>();

    internal DbSet<IngestOperationRow> IngestOperations => Set<IngestOperationRow>();

    internal DbSet<ConflictCaseRow> ConflictCases => Set<ConflictCaseRow>();

    internal DbSet<ConflictMatchRow> ConflictMatches => Set<ConflictMatchRow>();

    internal DbSet<ProcessingPlanRow> ProcessingPlans => Set<ProcessingPlanRow>();

    internal DbSet<ProcessingPlanInputRow> ProcessingPlanInputs =>
        Set<ProcessingPlanInputRow>();

    internal DbSet<ProcessingJobRow> ProcessingJobs => Set<ProcessingJobRow>();

    internal DbSet<ProcessedOutputRow> ProcessedOutputs => Set<ProcessedOutputRow>();

    internal DbSet<IngestCleanupEntryRow> IngestCleanupEntries =>
        Set<IngestCleanupEntryRow>();

    internal DbSet<NamingPresetRow> NamingPresets => Set<NamingPresetRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAppSettings(modelBuilder.Entity<AppSettingsRow>());
        ConfigureInboxItem(modelBuilder.Entity<InboxItemRow>());
        ConfigureSourceFile(modelBuilder.Entity<SourceFileRow>());
        ConfigureMetadataDraft(modelBuilder.Entity<MetadataDraftRow>());
        ConfigureTaxonomyTerm(modelBuilder.Entity<TaxonomyTermRow>());
        ConfigureInboxDraftAuthor(modelBuilder.Entity<InboxDraftAuthorRow>());
        ConfigureInboxDraftTag(modelBuilder.Entity<InboxDraftTagRow>());
        ConfigureMediaAsset(modelBuilder.Entity<MediaAssetRow>());
        ConfigureMediaAssetAuthor(modelBuilder.Entity<MediaAssetAuthorRow>());
        ConfigureMediaAssetTag(modelBuilder.Entity<MediaAssetTagRow>());
        ConfigureIngestOperation(modelBuilder.Entity<IngestOperationRow>());
        ConfigureConflictCase(modelBuilder.Entity<ConflictCaseRow>());
        ConfigureConflictMatch(modelBuilder.Entity<ConflictMatchRow>());
        ConfigureProcessingPlan(modelBuilder.Entity<ProcessingPlanRow>());
        ConfigureProcessingPlanInput(modelBuilder.Entity<ProcessingPlanInputRow>());
        ConfigureProcessingJob(modelBuilder.Entity<ProcessingJobRow>());
        ConfigureProcessedOutput(modelBuilder.Entity<ProcessedOutputRow>());
        ConfigureIngestCleanupEntry(modelBuilder.Entity<IngestCleanupEntryRow>());
        ConfigureNamingPreset(modelBuilder.Entity<NamingPresetRow>());
    }

    private static void ConfigureAppSettings(EntityTypeBuilder<AppSettingsRow> row)
    {
        row.ToTable("app_settings");
        row.HasKey(item => item.Id);
        row.Property(item => item.InboxDirectory)
            .IsRequired()
            .HasMaxLength(4096);
        row.Property(item => item.LibraryDirectory)
            .IsRequired()
            .HasMaxLength(4096);
        row.Property(item => item.WorkDirectory)
            .IsRequired()
            .HasMaxLength(4096);
        row.Property(item => item.AutoScanEnabled)
            .HasDefaultValue(false);
        row.Property(item => item.LastScanSummary)
            .HasMaxLength(1024);
        row.Property(item => item.LibrarySortField)
            .HasColumnName("library_sort_field")
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue("created_at");
        row.Property(item => item.LibrarySortDescending)
            .HasColumnName("library_sort_descending")
            .HasDefaultValue(true);
    }

    private static void ConfigureInboxItem(EntityTypeBuilder<InboxItemRow> row)
    {
        row.ToTable("inbox_items");
        row.HasKey(item => item.Id);
        row.Property(item => item.State)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<InboxItemState>(value, true))
            .HasMaxLength(64);
        row.Property(item => item.MediaKind)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<MediaKind>(value, true))
            .HasMaxLength(32);
        row.Property(item => item.ActualFilename).IsRequired().HasMaxLength(1024);
        row.Property(item => item.GroupingRule).IsRequired().HasMaxLength(128);
        row.Property(item => item.RequiredCapability).HasMaxLength(128);
        row.Property(item => item.ErrorCode).HasMaxLength(128);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureSourceFile(EntityTypeBuilder<SourceFileRow> row)
    {
        row.ToTable("source_files");
        row.HasKey(item => item.Id);
        row.Property(item => item.NormalizedPath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.ActivePathKey).HasMaxLength(4096);
        row.Property(item => item.Filename).IsRequired().HasMaxLength(1024);
        row.Property(item => item.Extension).IsRequired().HasMaxLength(32);
        row.Property(item => item.Sha256).HasMaxLength(64);
        row.HasIndex(item => item.ActivePathKey).IsUnique();
        row.HasOne(item => item.InboxItem)
            .WithMany(item => item.SourceFiles)
            .HasForeignKey(item => item.InboxItemId)
            .OnDelete(DeleteBehavior.Cascade);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureMetadataDraft(EntityTypeBuilder<MetadataDraftRow> row)
    {
        row.ToTable("metadata_drafts");
        row.HasKey(item => item.InboxItemId);
        row.Property(item => item.DisplayName).HasMaxLength(1024);
        row.HasOne(item => item.InboxItem)
            .WithOne(item => item.MetadataDraft)
            .HasForeignKey<MetadataDraftRow>(item => item.InboxItemId)
            .OnDelete(DeleteBehavior.Cascade);
        row.HasOne(item => item.Series)
            .WithMany()
            .HasForeignKey(item => item.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureTaxonomyTerm(EntityTypeBuilder<TaxonomyTermRow> row)
    {
        row.ToTable("taxonomy_terms");
        row.HasKey(item => item.Id);
        row.Property(item => item.Kind).IsRequired().HasMaxLength(32);
        row.Property(item => item.DisplayName).IsRequired().HasMaxLength(512);
        row.Property(item => item.NormalizedName).IsRequired().HasMaxLength(512);
        row.HasIndex(item => new { item.Kind, item.NormalizedName }).IsUnique();
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureInboxDraftAuthor(EntityTypeBuilder<InboxDraftAuthorRow> row)
    {
        row.ToTable("inbox_draft_authors");
        row.HasKey(item => new { item.InboxItemId, item.AuthorId });
        row.HasOne(item => item.InboxItem)
            .WithMany(item => item.DraftAuthors)
            .HasForeignKey(item => item.InboxItemId)
            .OnDelete(DeleteBehavior.Cascade);
        row.HasOne(item => item.Author)
            .WithMany()
            .HasForeignKey(item => item.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureInboxDraftTag(EntityTypeBuilder<InboxDraftTagRow> row)
    {
        row.ToTable("inbox_draft_tags");
        row.HasKey(item => new { item.InboxItemId, item.TagId });
        row.HasOne(item => item.InboxItem)
            .WithMany(item => item.DraftTags)
            .HasForeignKey(item => item.InboxItemId)
            .OnDelete(DeleteBehavior.Cascade);
        row.HasOne(item => item.Tag)
            .WithMany()
            .HasForeignKey(item => item.TagId)
            .OnDelete(DeleteBehavior.Restrict);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureMediaAsset(EntityTypeBuilder<MediaAssetRow> row)
    {
        row.ToTable("media_assets");
        row.HasKey(item => item.Id);
        row.Property(item => item.MediaKind)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<MediaKind>(value, true))
            .HasMaxLength(32);
        row.Property(item => item.ActualFilename).IsRequired().HasMaxLength(1024);
        row.Property(item => item.RelativePath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.NormalizedRelativePath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.DisplayName).HasMaxLength(1024);
        row.Property(item => item.Sha256).IsRequired().HasMaxLength(64);
        row.HasIndex(item => item.NormalizedRelativePath).IsUnique();
        row.HasIndex(item => item.Sha256).IsUnique(false);
        row.HasOne(item => item.Series)
            .WithMany()
            .HasForeignKey(item => item.SeriesId)
            .OnDelete(DeleteBehavior.Restrict);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureMediaAssetAuthor(EntityTypeBuilder<MediaAssetAuthorRow> row)
    {
        row.ToTable("media_asset_authors");
        row.HasKey(item => new { item.MediaAssetId, item.AuthorId });
        row.HasOne(item => item.MediaAsset)
            .WithMany(item => item.Authors)
            .HasForeignKey(item => item.MediaAssetId)
            .OnDelete(DeleteBehavior.Cascade);
        row.HasOne(item => item.Author)
            .WithMany()
            .HasForeignKey(item => item.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureMediaAssetTag(EntityTypeBuilder<MediaAssetTagRow> row)
    {
        row.ToTable("media_asset_tags");
        row.HasKey(item => new { item.MediaAssetId, item.TagId });
        row.HasOne(item => item.MediaAsset)
            .WithMany(item => item.Tags)
            .HasForeignKey(item => item.MediaAssetId)
            .OnDelete(DeleteBehavior.Cascade);
        row.HasOne(item => item.Tag)
            .WithMany()
            .HasForeignKey(item => item.TagId)
            .OnDelete(DeleteBehavior.Restrict);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureIngestOperation(EntityTypeBuilder<IngestOperationRow> row)
    {
        row.ToTable("ingest_operations");
        row.HasKey(item => item.Id);
        row.Property(item => item.Stage)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<IngestOperationStage>(value, true))
            .HasMaxLength(64);
        row.Property(item => item.Disposition)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<IngestDisposition>(value, true))
            .HasMaxLength(64)
            .HasDefaultValue(IngestDisposition.Create);
        row.Property(item => item.SourcePath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.SourceSha256).IsRequired().HasMaxLength(64);
        row.Property(item => item.TargetFilename).IsRequired().HasMaxLength(1024);
        row.Property(item => item.StagedPath).HasMaxLength(4096);
        row.Property(item => item.ExpectedTargetRelativePath).HasMaxLength(4096);
        row.Property(item => item.ExpectedTargetSha256).HasMaxLength(64);
        row.Property(item => item.BackupState)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<IngestBackupState>(value, true))
            .HasMaxLength(64)
            .HasDefaultValue(IngestBackupState.NotRequired);
        row.Property(item => item.BackupRelativePath).HasMaxLength(4096);
        row.Property(item => item.BackupSha256).HasMaxLength(64);
        row.Property(item => item.RecoveryBlockedFromStage)
            .HasConversion(
                value => value.HasValue
                    ? value.Value.ToString().ToLowerInvariant()
                    : null,
                value => string.IsNullOrWhiteSpace(value)
                    ? null
                    : Enum.Parse<IngestOperationStage>(value, true))
            .HasMaxLength(64);
        row.Property(item => item.TargetTemporaryPath).HasMaxLength(4096);
        row.Property(item => item.MetadataSnapshotJson).IsRequired();
        row.Property(item => item.ErrorCode).HasMaxLength(128);
        row.Property(item => item.CleanupAttemptCount).HasDefaultValue(0);
        row.HasIndex(item => item.ActiveInboxItemId).IsUnique();
        row.HasIndex(item => item.ConflictCaseId).IsUnique();
        row.HasIndex(item => item.NextCleanupAttemptAtUtc);
        row.HasOne(item => item.InboxItem)
            .WithMany(item => item.IngestOperations)
            .HasForeignKey(item => item.InboxItemId)
            .OnDelete(DeleteBehavior.Restrict);
        row.HasOne<MediaAssetRow>()
            .WithMany()
            .HasForeignKey(item => item.AssetId)
            .OnDelete(DeleteBehavior.Restrict);
        row.HasOne<MediaAssetRow>()
            .WithMany()
            .HasForeignKey(item => item.ReplacementAssetId)
            .OnDelete(DeleteBehavior.Restrict);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureConflictCase(EntityTypeBuilder<ConflictCaseRow> row)
    {
        row.ToTable("conflict_cases");
        row.HasKey(item => item.Id);
        row.Property(item => item.Status)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<ConflictCaseStatus>(value, true))
            .HasMaxLength(64);
        row.Property(item => item.InputPath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.InputSha256).IsRequired().HasMaxLength(64);
        row.Property(item => item.TargetFilename).IsRequired().HasMaxLength(1024);
        row.Property(item => item.Resolution)
            .HasConversion(
                value => value.HasValue
                    ? value.Value.ToString().ToLowerInvariant()
                    : null,
                value => string.IsNullOrWhiteSpace(value)
                    ? null
                    : Enum.Parse<ConflictResolution>(value, true))
            .HasMaxLength(64);
        row.Property(item => item.InvalidationReason).HasMaxLength(128);
        row.HasIndex(item => item.ActiveInboxItemId).IsUnique();
        row.HasIndex(item => item.InputSha256);
        row.HasIndex(item => item.OperationId).IsUnique();
        row.HasOne(item => item.InboxItem)
            .WithMany()
            .HasForeignKey(item => item.InboxItemId)
            .OnDelete(DeleteBehavior.Restrict);
        row.HasMany(item => item.Matches)
            .WithOne(item => item.ConflictCase)
            .HasForeignKey(item => item.ConflictCaseId)
            .OnDelete(DeleteBehavior.Cascade);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureConflictMatch(EntityTypeBuilder<ConflictMatchRow> row)
    {
        row.ToTable("conflict_matches");
        row.HasKey(item => item.Id);
        row.Property(item => item.Reason).HasConversion<int>();
        row.Property(item => item.RelativePath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.ActualFilename).IsRequired().HasMaxLength(1024);
        row.Property(item => item.DisplayName).HasMaxLength(1024);
        row.Property(item => item.MediaKind)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<MediaKind>(value, true))
            .HasMaxLength(32);
        row.Property(item => item.Sha256).IsRequired().HasMaxLength(64);
        row.Property(item => item.MetadataSnapshotJson).IsRequired();
        row.Property(item => item.TechnicalSnapshotJson).IsRequired();
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureProcessingPlan(EntityTypeBuilder<ProcessingPlanRow> row)
    {
        row.ToTable("processing_plans");
        row.HasKey(item => item.Id);
        row.Property(item => item.ProcessorKey).IsRequired().HasMaxLength(128);
        row.Property(item => item.ProcessorVersion).IsRequired().HasMaxLength(128);
        row.Property(item => item.ProcessorDisplayName).IsRequired().HasMaxLength(1024);
        row.Property(item => item.Capability).IsRequired().HasMaxLength(128);
        row.Property(item => item.OutputFilename).IsRequired().HasMaxLength(1024);
        row.Property(item => item.WorkDirectoryRelativePath)
            .IsRequired()
            .HasMaxLength(4096);
        row.Property(item => item.StepKeysJson).IsRequired();
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureProcessingPlanInput(
        EntityTypeBuilder<ProcessingPlanInputRow> row)
    {
        row.ToTable("processing_plan_inputs");
        row.HasKey(item => item.Id);
        row.Property(item => item.Role).IsRequired().HasMaxLength(128);
        row.Property(item => item.FullPath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.Filename).IsRequired().HasMaxLength(1024);
        row.Property(item => item.Sha256).IsRequired().HasMaxLength(64);
        row.HasIndex(item => new { item.PlanId, item.Role }).IsUnique();
        row.HasOne(item => item.Plan)
            .WithMany(item => item.Inputs)
            .HasForeignKey(item => item.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureProcessingJob(EntityTypeBuilder<ProcessingJobRow> row)
    {
        row.ToTable("processing_jobs");
        row.HasKey(item => item.Id);
        row.Property(item => item.Status)
            .HasConversion(
                value => value.ToString().ToLowerInvariant(),
                value => Enum.Parse<ProcessingJobStatus>(value, true))
            .HasMaxLength(64);
        row.Property(item => item.CurrentStepKey).HasMaxLength(128);
        row.Property(item => item.ErrorCode).HasMaxLength(128);
        row.Property(item => item.CorrelationId).IsRequired().HasMaxLength(64);
        row.HasIndex(item => item.ActiveInboxItemId).IsUnique();
        row.HasOne(item => item.Plan)
            .WithOne(item => item.Job)
            .HasForeignKey<ProcessingJobRow>(item => item.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
        row.HasOne(item => item.InboxItem)
            .WithMany(item => item.ProcessingJobs)
            .HasForeignKey(item => item.InboxItemId)
            .OnDelete(DeleteBehavior.Restrict);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureProcessedOutput(EntityTypeBuilder<ProcessedOutputRow> row)
    {
        row.ToTable("processed_outputs");
        row.HasKey(item => item.JobId);
        row.Property(item => item.RelativePath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.Sha256).IsRequired().HasMaxLength(64);
        row.HasIndex(item => item.ActiveInboxItemId).IsUnique();
        row.HasOne(item => item.Job)
            .WithOne(item => item.Output)
            .HasForeignKey<ProcessedOutputRow>(item => item.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureIngestCleanupEntry(
        EntityTypeBuilder<IngestCleanupEntryRow> row)
    {
        row.ToTable("ingest_cleanup_entries");
        row.HasKey(item => item.Id);
        row.Property(item => item.CleanupKind).IsRequired().HasMaxLength(128);
        row.Property(item => item.ControlledPath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.Sha256).IsRequired().HasMaxLength(64);
        row.Property(item => item.LastErrorCode).HasMaxLength(128);
        row.HasIndex(item => new { item.OperationId, item.Ordinal }).IsUnique();
        row.HasOne(item => item.Operation)
            .WithMany(item => item.CleanupEntries)
            .HasForeignKey(item => item.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        UseSnakeCaseColumns(row);
    }

    private static void ConfigureNamingPreset(
        EntityTypeBuilder<NamingPresetRow> row)
    {
        row.ToTable("naming_presets");
        row.HasKey(item => item.Id);
        row.Property(item => item.DisplayName).IsRequired().HasMaxLength(512);
        row.Property(item => item.NormalizedName).IsRequired().HasMaxLength(512);
        row.Property(item => item.Source).IsRequired().HasMaxLength(32);
        row.Property(item => item.RulesJson).IsRequired();
        row.HasIndex(item => item.NormalizedName).IsUnique();
        UseSnakeCaseColumns(row);
    }

    private static void UseSnakeCaseColumns<TEntity>(EntityTypeBuilder<TEntity> row)
        where TEntity : class
    {
        foreach (var property in row.Metadata.GetProperties())
        {
            property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    private static string ToSnakeCase(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (index > 0 && char.IsUpper(character))
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }
}
