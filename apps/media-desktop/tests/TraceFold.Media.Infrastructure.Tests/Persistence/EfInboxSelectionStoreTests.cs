using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class EfInboxSelectionStoreTests
{
    [Fact]
    public async Task Store_returns_current_facts_in_requested_ordinal_order()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid ready = await database.SeedInboxItemAsync(
            InboxItemState.ReadyToTag,
            metadataVersion: 3,
            mediaKind: MediaKind.Image,
            displayName: "图像");
        Guid processing = await database.SeedInboxItemAsync(
            InboxItemState.RequiresProcessing,
            metadataVersion: 8,
            mediaKind: MediaKind.Video,
            displayName: "视频");
        var store = new EfInboxSelectionStore(database.CreateContextFactory());

        IReadOnlyList<InboxSelectionItemDto> result = await store.LoadAsync(
            [new(processing, 7, 0), new(ready, 3, 1)],
            TestContext.Current.CancellationToken);

        Assert.Equal([processing, ready], result.Select(item => item.InboxItemId));
        Assert.Equal([8L, 3L], result.Select(item => item.MetadataVersion));
        Assert.Equal(["视频", "图像"], result.Select(item => item.SafeName));
        Assert.True(result[0].AllowedActions.HasFlag(InboxAction.StartProcessing));
    }

    [Fact]
    public async Task Store_chunks_large_selections_and_omits_missing_or_completed_rows()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] ids = await database.SeedEditableInboxItemsAsync(401);
        Guid completed = await database.SeedInboxItemAsync(
            InboxItemState.Completed, metadataVersion: 0);
        var selection = ids
            .Reverse()
            .Select((id, ordinal) => new InboxSelectionEntry(id, 0, ordinal))
            .Append(new InboxSelectionEntry(completed, 0, 401))
            .Append(new InboxSelectionEntry(Guid.NewGuid(), 0, 402))
            .ToArray();
        var store = new EfInboxSelectionStore(database.CreateContextFactory());

        IReadOnlyList<InboxSelectionItemDto> result = await store.LoadAsync(
            selection, TestContext.Current.CancellationToken);

        Assert.Equal(401, result.Count);
        Assert.Equal(ids[^1], result[0].InboxItemId);
        Assert.Equal(ids[0], result[^1].InboxItemId);
        Assert.Equal(Enumerable.Range(0, 401), result.Select(item => item.Ordinal));
    }
}
