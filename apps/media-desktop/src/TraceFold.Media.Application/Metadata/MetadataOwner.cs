namespace TraceFold.Media.Application.Metadata;

public enum MetadataOwnerKind
{
    InboxItem,
    MediaAsset
}

public sealed record MetadataOwner(MetadataOwnerKind Kind, Guid Id);
