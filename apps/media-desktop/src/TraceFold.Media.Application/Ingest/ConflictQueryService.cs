using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public sealed class ConflictQueryService(
    IConflictCaseStore store,
    IControlledMediaStorage storage,
    IDirectorySettingsStore settingsStore,
    IMetadataStore metadataStore,
    IMediaPreviewResolver previewResolver,
    IMediaTool? mediaTool = null) : IConflictQueryService
{
    public async Task<ConflictCaseDto?> GetOpenAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        ConflictCaseRecord? conflict = await store.LoadOpenForInboxAsync(
            inboxItemId,
            cancellationToken);
        if (conflict is null)
        {
            return null;
        }

        var incomingOwner = new MetadataOwner(
            MetadataOwnerKind.InboxItem,
            conflict.InboxItemId);
        MetadataDocument? metadata = await metadataStore.LoadAsync(
            incomingOwner,
            cancellationToken);
        PreviewDescriptor? incomingPreview = await TryResolvePreviewAsync(
            incomingOwner,
            cancellationToken);
        MediaKind incomingKind = conflict.Matches.FirstOrDefault()?.MediaKind
            ?? incomingPreview?.MediaKind
            ?? InferMediaKind(conflict.Input.TargetFilename);
        ConflictTechnicalMetadata incomingTechnical =
            await ConflictTechnicalMetadataReader.ReadAsync(
                conflict.Input.TargetFilename,
                conflict.Input.FullPath,
                incomingKind,
                mediaTool,
                cancellationToken);
        ConflictMetadataSnapshot incomingMetadata = metadata is null
            ? new ConflictMetadataSnapshot(null, [], null, [])
            : new ConflictMetadataSnapshot(
                metadata.DisplayName,
                metadata.Authors
                    .Select(term => term.DisplayName)
                    .ToArray(),
                metadata.Series?.DisplayName,
                metadata.Tags
                    .Select(term => term.DisplayName)
                    .ToArray());
        var incoming = new ConflictMediaDto(
            conflict.InboxItemId,
            true,
            false,
            null,
            conflict.Input.TargetFilename,
            metadata?.DisplayName ?? conflict.Input.TargetFilename,
            incomingKind,
            conflict.Input.Size,
            conflict.Input.LastWriteTimeUtc,
            conflict.Input.Sha256,
            ConflictReason.None,
            incomingMetadata,
            incomingTechnical,
            incomingPreview);

        LibraryFolders? folders = await settingsStore.LoadAsync(
            cancellationToken);
        var matches = new List<ConflictMediaDto>(conflict.Matches.Count);
        foreach (ConflictMatchRecord match in conflict.Matches)
        {
            PreviewDescriptor? preview = match.IsRegistered
                ? await TryResolvePreviewAsync(
                    new MetadataOwner(
                        MetadataOwnerKind.MediaAsset,
                        match.AssetId
                            ?? throw new InvalidOperationException(
                                "A registered conflict match has no asset id.")),
                    cancellationToken)
                : await TryResolveUnmanagedPreviewAsync(
                    folders,
                    match,
                    cancellationToken);
            matches.Add(new ConflictMediaDto(
                match.Id,
                false,
                match.IsRegistered,
                match.AssetId,
                match.ActualFilename,
                match.DisplayName ?? match.ActualFilename,
                match.MediaKind,
                match.Size,
                match.LastWriteTimeUtc,
                match.Sha256,
                match.Reason,
                match.Metadata,
                match.Technical,
                preview));
        }

        Guid? defaultMatchId = conflict.Matches
            .FirstOrDefault(match =>
                match.Reason.HasFlag(ConflictReason.Name))
            ?.Id;
        return new ConflictCaseDto(
            conflict.Id,
            conflict.InboxItemId,
            incoming,
            matches,
            conflict.Resolution,
            conflict.SelectedMatchId,
            defaultMatchId,
            matches.Count > 0);
    }

    private async Task<PreviewDescriptor?> TryResolvePreviewAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken)
    {
        try
        {
            return await previewResolver.ResolveAsync(owner, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (
            error is PreviewNotRegisteredException
                or IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            return null;
        }
    }

    private async Task<PreviewDescriptor?> TryResolveUnmanagedPreviewAsync(
        LibraryFolders? folders,
        ConflictMatchRecord match,
        CancellationToken cancellationToken)
    {
        if (folders is null)
        {
            return null;
        }

        try
        {
            LibraryFileSnapshot? snapshot = await storage.InspectTargetAsync(
                folders,
                match.ActualFilename,
                cancellationToken);
            return snapshot is not null
                && snapshot.Size == match.Size
                && StringComparer.OrdinalIgnoreCase.Equals(
                    snapshot.Sha256,
                    match.Sha256)
                    ? new PreviewDescriptor(
                        match.MediaKind,
                        snapshot.FullPath,
                        true)
                    : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (
            error is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            return null;
        }
    }

    private static MediaKind InferMediaKind(string filename) =>
        Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".mp4" or ".mov" or ".mkv" or ".webm" => MediaKind.Video,
            _ => MediaKind.Image
        };
}
