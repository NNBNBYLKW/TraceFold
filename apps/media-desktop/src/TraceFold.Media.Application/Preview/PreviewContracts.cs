using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Application.Preview;

public sealed record PreviewPathRecord(
    MediaKind MediaKind,
    string TrustedFullPath);

public sealed record PreviewDescriptor(
    MediaKind MediaKind,
    string TrustedFullPath,
    bool Exists);

public sealed class PreviewNotRegisteredException : InvalidOperationException
{
    public PreviewNotRegisteredException()
        : base("No registered preview source exists for this media item.")
    {
    }
}

public interface IPreviewPathStore
{
    Task<PreviewPathRecord?> FindAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken);
}

public interface IMediaPreviewResolver
{
    Task<PreviewDescriptor> ResolveAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken);
}
