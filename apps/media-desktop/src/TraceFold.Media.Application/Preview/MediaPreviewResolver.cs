using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Application.Preview;

public sealed class MediaPreviewResolver(IPreviewPathStore store) : IMediaPreviewResolver
{
    public async Task<PreviewDescriptor> ResolveAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        PreviewPathRecord? registered = await store.FindAsync(owner, cancellationToken);
        if (registered is null)
        {
            throw new PreviewNotRegisteredException();
        }

        return new PreviewDescriptor(
            registered.MediaKind,
            registered.TrustedFullPath,
            File.Exists(registered.TrustedFullPath));
    }
}
