using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public sealed class MetadataEditorService(
    IMetadataStore store,
    ITaxonomyLookup? taxonomyLookup = null)
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

        return await store.TrySaveAsync(
            owner,
            selection,
            update.ExpectedVersion,
            cancellationToken);
    }
}
