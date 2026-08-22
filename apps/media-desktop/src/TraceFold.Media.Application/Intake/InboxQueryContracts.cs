using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Intake;

public sealed record InboxQuery(string? Search, InboxItemState? State);

public sealed record InboxCardDto(
    Guid Id,
    string ActualFilename,
    string DisplayName,
    MediaKind MediaKind,
    InboxItemState State,
    string StatusText,
    bool CanIngest);

public sealed record InboxSourceFileDto(
    string Filename,
    string Extension,
    long Size,
    bool IsPresent,
    bool IsPrimary);

public sealed record InboxDetailsDto(
    InboxCardDto Card,
    IReadOnlyList<InboxSourceFileDto> SourceFiles,
    PreviewDescriptor? Preview,
    MetadataDocument Metadata,
    InboxAction AllowedActions,
    string? ErrorCode,
    string? RequiredCapability,
    bool IsRetryable);

public interface IInboxQueryStore
{
    Task<IReadOnlyList<InboxCardDto>> SearchAsync(
        InboxQuery query,
        CancellationToken cancellationToken);

    Task<InboxDetailsDto?> GetDetailsAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task ClearMissingAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}

public interface IInboxQueryService
{
    Task<IReadOnlyList<InboxCardDto>> SearchAsync(
        InboxQuery query,
        CancellationToken cancellationToken);

    Task<InboxDetailsDto?> GetDetailsAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task ClearMissingAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}
