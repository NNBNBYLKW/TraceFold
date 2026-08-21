using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class MediaDesktopDbContext(
    DbContextOptions<MediaDesktopDbContext> options) : DbContext(options)
{
    internal DbSet<AppSettingsRow> AppSettings => Set<AppSettingsRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<AppSettingsRow> row =
            modelBuilder.Entity<AppSettingsRow>();
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
    }
}
