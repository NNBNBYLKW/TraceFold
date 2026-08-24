using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Intake;

public sealed record InboxSelectionEntry(
    Guid InboxItemId,
    long ExpectedMetadataVersion,
    int Ordinal);

public sealed record InboxSelectionItemDto(
    Guid InboxItemId,
    long MetadataVersion,
    int Ordinal,
    MediaKind MediaKind,
    InboxItemState State,
    InboxAction AllowedActions,
    string SafeName);

public interface IInboxSelectionStore
{
    Task<IReadOnlyList<InboxSelectionItemDto>> LoadAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);
}
