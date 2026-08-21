using TraceFold.Media.Application.Intake;

namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class MediaAssetRow
{
    public Guid Id { get; set; }

    public MediaKind MediaKind { get; set; }

    public required string ActualFilename { get; set; }

    public required string RelativePath { get; set; }

    public required string NormalizedRelativePath { get; set; }

    public string? DisplayName { get; set; }

    public long Size { get; set; }

    public required string Sha256 { get; set; }

    public Guid? SeriesId { get; set; }

    public TaxonomyTermRow? Series { get; set; }

    public long MetadataVersion { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<MediaAssetAuthorRow> Authors { get; } = [];

    public ICollection<MediaAssetTagRow> Tags { get; } = [];
}
