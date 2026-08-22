namespace TraceFold.Media.Application.Intake;

public sealed class InboxQueryService(IInboxQueryStore store) : IInboxQueryService
{
    public Task<IReadOnlyList<InboxCardDto>> SearchAsync(
        InboxQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return store.SearchAsync(query, cancellationToken);
    }

    public Task<InboxDetailsDto?> GetDetailsAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken) =>
        store.GetDetailsAsync(inboxItemId, cancellationToken);

    public Task ClearMissingAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken) =>
        store.ClearMissingAsync(inboxItemId, cancellationToken);
}
