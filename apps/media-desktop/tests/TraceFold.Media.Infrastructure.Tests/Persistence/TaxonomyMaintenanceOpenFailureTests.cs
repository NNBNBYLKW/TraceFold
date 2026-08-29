using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class TaxonomyMaintenanceOpenFailureTests
{
    [Fact]
    public async Task Transaction_open_failures_return_stable_errors_for_every_write()
    {
        string missingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-missing-{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<MediaDesktopDbContext>()
            .UseSqlite($"Data Source={Path.Combine(missingDirectory, "media.db")}")
            .Options;
        var store = new EfTaxonomyMaintenanceStore(
            new TestContextFactory(options),
            TimeProvider.System);

        TaxonomyMaintenanceStoreResult merge = await store.ApplyMergeAsync(
            MergePreparation(),
            finalName: null,
            TestContext.Current.CancellationToken);
        TaxonomyMaintenanceStoreResult delete = await store.DeleteUnusedAsync(
            DeletePreparation(),
            TestContext.Current.CancellationToken);
        TaxonomyMaintenanceUndoResult undo = await store.UndoAsync(
            new TaxonomyDeleteUndoReceipt(
                Guid.NewGuid(),
                DeletePreparation().Terms),
            TestContext.Current.CancellationToken);

        Assert.Equal(TaxonomyMaintenanceErrorCodes.PersistenceFailed, merge.ErrorCode);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.PersistenceFailed, delete.ErrorCode);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.PersistenceFailed, undo.ErrorCode);
        Assert.True(undo.ReceiptIsRetryable);
        Assert.False(Directory.Exists(missingDirectory));
    }

    private static TaxonomyMergePreparation MergePreparation()
    {
        TaxonomyTermSnapshot target = Term(TaxonomyKind.Author, "Target");
        TaxonomyTermSnapshot source = Term(TaxonomyKind.Author, "Source");
        return new TaxonomyMergePreparation(
            Guid.NewGuid(),
            TaxonomyMergeSelection.Create(
                TaxonomyKind.Author,
                [source.Id],
                target.Id),
            target,
            [source],
            [
                new TaxonomyNameOwnerSnapshot(target.NormalizedName, target.Id),
                new TaxonomyNameOwnerSnapshot(source.NormalizedName, source.Id),
            ],
            []);
    }

    private static TaxonomyDeletePreparation DeletePreparation()
    {
        TaxonomyTermSnapshot term = Term(TaxonomyKind.Tag, "Unused");
        return new TaxonomyDeletePreparation(
            Guid.NewGuid(),
            TaxonomyDeleteSelection.Create(TaxonomyKind.Tag, [term.Id]),
            [term],
            []);
    }

    private static TaxonomyTermSnapshot Term(TaxonomyKind kind, string displayName)
    {
        TaxonomyName name = TaxonomyName.Create(displayName);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-29T00:00:00Z");
        return new TaxonomyTermSnapshot(
            Guid.NewGuid(),
            kind,
            name.Display,
            name.Normalized,
            now,
            now);
    }

    private sealed class TestContextFactory(
        DbContextOptions<MediaDesktopDbContext> options)
        : IDbContextFactory<MediaDesktopDbContext>
    {
        public MediaDesktopDbContext CreateDbContext() => new(options);

        public ValueTask<MediaDesktopDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MediaDesktopDbContext(options));
    }
}
