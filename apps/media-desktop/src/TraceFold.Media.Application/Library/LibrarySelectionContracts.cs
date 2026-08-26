namespace TraceFold.Media.Application.Library;

public sealed record LibrarySelectionEntry(
    Guid AssetId,
    long ExpectedMetadataVersion,
    int Ordinal);
