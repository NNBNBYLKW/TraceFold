using TraceFold.Media.Application.Library;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public sealed class MetadataEditorService(
    IMetadataStore store,
    ITaxonomyLookup? taxonomyLookup = null,
    ILibraryActivityNotifier? libraryActivityNotifier = null)
    : IMetadataEditorService
{
    public Task<MetadataDocument?> LoadAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken) =>
        store.LoadAsync(owner, cancellationToken);

    public async Task<MetadataSaveResult> SaveAsync(
        MetadataOwner owner,
        MetadataUpdate update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(update);
        MetadataSelection selection = MetadataSelection.Create(
            update.DisplayName,
            update.AuthorIds,
            update.SeriesId,
            update.TagIds);
        if (taxonomyLookup is not null)
        {
            TaxonomyValidationResult validation =
                await taxonomyLookup.ValidateAsync(selection, cancellationToken);
            if (!validation.Succeeded)
            {
                return new MetadataSaveResult(
                    false,
                    update.ExpectedVersion,
                    validation.ErrorCode);
            }
        }

        MetadataSaveResult result = await store.TrySaveAsync(
            owner,
            selection,
            update.ExpectedVersion,
            cancellationToken);
        if (result.Succeeded
            && owner.Kind == MetadataOwnerKind.MediaAsset)
        {
            libraryActivityNotifier?.Publish(
                LibraryActivityKind.SingleMetadataChanged,
                [owner.Id]);
        }

        return result;
    }
}
