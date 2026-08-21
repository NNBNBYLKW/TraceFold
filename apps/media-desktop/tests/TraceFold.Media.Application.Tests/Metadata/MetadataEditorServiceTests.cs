using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class MetadataEditorServiceTests
{
    private static readonly MetadataOwner Owner =
        new(MetadataOwnerKind.InboxItem, Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public async Task SaveAsync_rejects_stale_version_without_overwrite()
    {
        var store = new InMemoryMetadataStore(version: 4);
        var service = new MetadataEditorService(store);
        var update = new MetadataUpdate("new", [], null, [], ExpectedVersion: 3);

        MetadataSaveResult result = await service.SaveAsync(
            Owner,
            update,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("metadata.version_conflict", result.ErrorCode);
        Assert.Equal(4, store.Version);
        Assert.Null(store.LastSelection);
    }

    [Fact]
    public async Task SaveAsync_accepts_many_authors_one_series_many_tags()
    {
        var store = new InMemoryMetadataStore(version: 0);
        var service = new MetadataEditorService(store);
        Guid author1 = Guid.NewGuid();
        Guid author2 = Guid.NewGuid();
        Guid series = Guid.NewGuid();
        Guid tag1 = Guid.NewGuid();
        Guid tag2 = Guid.NewGuid();

        MetadataSaveResult result = await service.SaveAsync(
            Owner,
            new MetadataUpdate(
                null,
                [author1, author2],
                series,
                [tag1, tag2],
                ExpectedVersion: 0),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.CurrentVersion);
        Assert.Equal([author1, author2], store.LastSelection!.AuthorIds);
        Assert.Equal(series, store.LastSelection.SeriesId);
        Assert.Equal([tag1, tag2], store.LastSelection.TagIds);
    }

    private sealed class InMemoryMetadataStore(long version) : IMetadataStore
    {
        public long Version { get; private set; } = version;

        public MetadataSelection? LastSelection { get; private set; }

        public Task<MetadataDocument?> LoadAsync(
            MetadataOwner owner,
            CancellationToken cancellationToken) =>
            Task.FromResult<MetadataDocument?>(null);

        public Task<MetadataSaveResult> TrySaveAsync(
            MetadataOwner owner,
            MetadataSelection selection,
            long expectedVersion,
            CancellationToken cancellationToken)
        {
            if (expectedVersion != Version)
            {
                return Task.FromResult(new MetadataSaveResult(
                    false,
                    Version,
                    "metadata.version_conflict"));
            }

            LastSelection = selection;
            Version++;
            return Task.FromResult(new MetadataSaveResult(true, Version, null));
        }
    }
}
