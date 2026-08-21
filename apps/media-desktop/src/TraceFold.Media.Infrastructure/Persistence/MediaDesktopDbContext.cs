using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class MediaDesktopDbContext(
    DbContextOptions<MediaDesktopDbContext> options) : DbContext(options)
{
    internal DbSet<AppSettingsRow> AppSettings => Set<AppSettingsRow>();

    internal DbSet<InboxItemRow> InboxItems => Set<InboxItemRow>();

    internal DbSet<SourceFileRow> SourceFiles => Set<SourceFileRow>();

    internal DbSet<MetadataDraftRow> MetadataDrafts => Set<MetadataDraftRow>();

    internal DbSet<TaxonomyTermRow> TaxonomyTerms => Set<TaxonomyTermRow>();

    internal DbSet<MediaAssetRow> MediaAssets => Set<MediaAssetRow>();

    internal DbSet<IngestOperationRow> IngestOperations => Set<IngestOperationRow>();

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
        row.HasIndex(item => item.Sha256).IsUnique();
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
        row.Property(item => item.SourcePath).IsRequired().HasMaxLength(4096);
        row.Property(item => item.SourceSha256).IsRequired().HasMaxLength(64);
        row.Property(item => item.TargetFilename).IsRequired().HasMaxLength(1024);
        row.Property(item => item.StagedPath).HasMaxLength(4096);
        row.Property(item => item.TargetTemporaryPath).HasMaxLength(4096);
        row.Property(item => item.MetadataSnapshotJson).IsRequired();
        row.Property(item => item.ErrorCode).HasMaxLength(128);
        row.HasIndex(item => item.ActiveInboxItemId).IsUnique();
        row.HasOne(item => item.InboxItem)
            .WithMany(item => item.IngestOperations)
            .HasForeignKey(item => item.InboxItemId)
            .OnDelete(DeleteBehavior.Restrict);
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
