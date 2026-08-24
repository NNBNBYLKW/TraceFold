using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfInboxSelectionStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory)
    : IInboxSelectionStore
{
    private const int ReadBatchSize = 200;

    public async Task<IReadOnlyList<InboxSelectionItemDto>> LoadAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = new Dictionary<Guid, InboxItemRow>();
        foreach (Guid[] ids in selection
                     .Select(item => item.InboxItemId)
                     .Distinct()
                     .Chunk(ReadBatchSize))
        {
            InboxItemRow[] loaded = await context.InboxItems
                .AsNoTracking()
                .Include(item => item.MetadataDraft)
                .Where(item => ids.Contains(item.Id)
                    && item.State != InboxItemState.Completed)
                .ToArrayAsync(cancellationToken);
            foreach (InboxItemRow row in loaded)
            {
                rows[row.Id] = row;
            }
        }

        return selection
            .OrderBy(item => item.Ordinal)
            .Where(item => rows.ContainsKey(item.InboxItemId))
            .Select(item => ToDto(rows[item.InboxItemId], item.Ordinal))
            .ToArray();
    }

    private static InboxSelectionItemDto ToDto(InboxItemRow row, int ordinal) =>
        new(
            row.Id,
            row.MetadataDraft?.Version ?? 0,
            ordinal,
            row.MediaKind,
            row.State,
            InboxActionPolicy.For(
                row.State,
                row.IsRetryable,
                row.RequiredCapability),
            row.MetadataDraft?.DisplayName ?? row.ActualFilename);
}
