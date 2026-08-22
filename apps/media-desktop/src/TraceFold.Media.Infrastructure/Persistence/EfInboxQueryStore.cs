using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfInboxQueryStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : IInboxQueryStore
{
    public async Task<IReadOnlyList<InboxCardDto>> SearchAsync(
        InboxQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<InboxItemRow> rows = context.InboxItems
            .AsNoTracking()
            .Include(row => row.MetadataDraft)
            .Where(row => row.State != InboxItemState.Completed);
        if (query.State.HasValue)
        {
            rows = rows.Where(row => row.State == query.State.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{EscapeLike(query.Search.Trim())}%";
            rows = rows.Where(row =>
                EF.Functions.Like(row.ActualFilename, pattern, "\\")
                || row.MetadataDraft != null
                    && row.MetadataDraft.DisplayName != null
                    && EF.Functions.Like(
                        row.MetadataDraft.DisplayName,
                        pattern,
                        "\\"));
        }

        InboxItemRow[] materialized = await rows.ToArrayAsync(cancellationToken);
        return materialized
            .OrderBy(row => StatePriority(row.State))
            .ThenBy(row => row.ActualFilename, StringComparer.OrdinalIgnoreCase)
            .Select(ToCard)
            .ToArray();
    }

    public async Task<InboxDetailsDto?> GetDetailsAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        InboxItemRow? item = await context.InboxItems
            .AsNoTracking()
            .Include(row => row.MetadataDraft)
            .Include(row => row.SourceFiles)
            .SingleOrDefaultAsync(row => row.Id == inboxItemId, cancellationToken);
        if (item?.MetadataDraft is null || item.State == InboxItemState.Completed)
        {
            return null;
        }

        var owner = new MetadataOwner(MetadataOwnerKind.InboxItem, inboxItemId);
        MetadataDocument? metadata = await new EfMetadataStore(contextFactory)
            .LoadAsync(owner, cancellationToken);
        if (metadata is null)
        {
            return null;
        }

        PreviewDescriptor? preview = null;
        PreviewPathRecord? path = await new EfPreviewPathStore(contextFactory)
            .FindAsync(owner, cancellationToken);
        if (path is not null)
        {
            preview = new PreviewDescriptor(
                path.MediaKind,
                path.TrustedFullPath,
                File.Exists(path.TrustedFullPath));
        }

        return new InboxDetailsDto(
            ToCard(item),
            item.SourceFiles
                .OrderByDescending(row => row.IsPrimary)
                .ThenBy(row => row.Filename, StringComparer.OrdinalIgnoreCase)
                .Select(row => new InboxSourceFileDto(
                    row.Filename,
                    row.Extension,
                    row.Size,
                    row.IsPresent,
                    row.IsPrimary))
                .ToArray(),
            preview,
            metadata,
            AllowedActions(item.State, item.IsRetryable),
            item.ErrorCode,
            item.RequiredCapability,
            item.IsRetryable);
    }

    public Task ClearMissingAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken) =>
        new EfInboxScanStore(contextFactory).ClearMissingAsync(
            inboxItemId,
            cancellationToken);

    private static InboxCardDto ToCard(InboxItemRow row) =>
        new(
            row.Id,
            row.ActualFilename,
            row.MetadataDraft?.DisplayName ?? row.ActualFilename,
            row.MediaKind,
            row.State,
            StatusText(row.State),
            row.State == InboxItemState.ReadyToTag);

    private static InboxAction AllowedActions(
        InboxItemState state,
        bool isRetryable) =>
        state switch
        {
            InboxItemState.WaitingForStability => InboxAction.Preview,
            InboxItemState.ReadyToTag => InboxAction.Preview
                | InboxAction.EditMetadata
                | InboxAction.Ingest
                | InboxAction.OpenFolder,
            InboxItemState.RequiresProcessing => InboxAction.Preview
                | InboxAction.EditMetadata
                | InboxAction.OpenFolder,
            InboxItemState.ConflictBlocked => InboxAction.Preview
                | InboxAction.EditMetadata
                | InboxAction.OpenFolder,
            InboxItemState.CleanupPending => InboxAction.Preview
                | InboxAction.RetryCleanup
                | InboxAction.OpenFolder,
            InboxItemState.Failed when isRetryable => InboxAction.Preview
                | InboxAction.Retry
                | InboxAction.OpenFolder,
            InboxItemState.SourceMissing => InboxAction.ClearMissing,
            _ => InboxAction.None
        };

    private static int StatePriority(InboxItemState state) =>
        state switch
        {
            InboxItemState.WaitingForStability => 0,
            InboxItemState.Failed => 1,
            InboxItemState.ConflictBlocked => 2,
            InboxItemState.SourceMissing => 3,
            InboxItemState.CleanupPending => 4,
            InboxItemState.RequiresProcessing => 5,
            InboxItemState.Analyzing => 6,
            InboxItemState.Ingesting => 7,
            InboxItemState.ReadyToTag => 8,
            _ => 9
        };

    private static string StatusText(InboxItemState state) =>
        state switch
        {
            InboxItemState.WaitingForStability => "等待文件稳定",
            InboxItemState.Analyzing => "正在分析",
            InboxItemState.ReadyToTag => "待入库",
            InboxItemState.RequiresProcessing => "需要前置处理",
            InboxItemState.Ingesting => "正在入库",
            InboxItemState.ConflictBlocked => "存在冲突",
            InboxItemState.CleanupPending => "等待安全清理",
            InboxItemState.Failed => "处理失败",
            InboxItemState.SourceMissing => "源文件缺失",
            InboxItemState.Completed => "已完成",
            _ => state.ToString()
        };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
