using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Ingest;

public sealed class IngestInputResolver(
    IProcessingStore processingStore,
    IFileHasher hasher,
    IProcessingInvalidationService invalidation) : IIngestInputResolver
{
    public async Task<PreparedIngestInput> ResolveAsync(
        IngestCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.State != InboxItemState.ReadyToTag
            || candidate.Sources.Count == 0
            || candidate.Sources.Any(source => !source.IsPresent))
        {
            throw new IngestInputException("ingest.not_ready");
        }

        ValidatedProcessingOutput? processed =
            await processingStore.LoadValidatedOutputAsync(
                candidate.InboxItemId,
                cancellationToken);
        if (processed is null)
        {
            if (candidate.Sources.Count != 1 || !candidate.Sources[0].IsPrimary)
            {
                throw new IngestInputException("ingest.not_ready");
            }

            return await ResolveOrdinaryAsync(candidate, cancellationToken);
        }

        return await ResolveProcessedAsync(
            candidate,
            processed,
            cancellationToken);
    }

    private async Task<PreparedIngestInput> ResolveOrdinaryAsync(
        IngestCandidate candidate,
        CancellationToken cancellationToken)
    {
        IngestSourceRecord source = candidate.Sources[0];
        if (!Matches(source))
        {
            await RejectChangedAsync(candidate.InboxItemId, cancellationToken);
        }

        string sha256;
        try
        {
            sha256 = await hasher.ComputeSha256Async(
                source.FullPath,
                cancellationToken);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException)
        {
            await RejectChangedAsync(candidate.InboxItemId, cancellationToken);
            throw;
        }

        if (!Matches(source))
        {
            await RejectChangedAsync(candidate.InboxItemId, cancellationToken);
        }

        var cleanup = new IngestCleanupFile(
            IngestCleanupKind.InboxSource,
            source.FullPath,
            source.Size,
            sha256.ToUpperInvariant());
        return new PreparedIngestInput(
            IngestCleanupKind.InboxSource,
            source.FullPath,
            source.Size,
            source.LastWriteTimeUtc,
            cleanup.Sha256,
            [cleanup]);
    }

    private async Task<PreparedIngestInput> ResolveProcessedAsync(
        IngestCandidate candidate,
        ValidatedProcessingOutput processed,
        CancellationToken cancellationToken)
    {
        ProcessedOutputRecord output = processed.Output;
        if (output.InboxItemId != candidate.InboxItemId
            || !output.IsValid
            || candidate.MediaKind == MediaKind.Video && !output.HasVideo
            || processed.Inputs.Any(input => input.Role == "audio")
                && !output.HasAudio
            || !Matches(output))
        {
            await RejectChangedAsync(candidate.InboxItemId, cancellationToken);
        }

        string outputHash = await HashOrRejectAsync(
            candidate.InboxItemId,
            output.FullPath,
            cancellationToken);
        if (!StringComparer.OrdinalIgnoreCase.Equals(outputHash, output.Sha256)
            || !Matches(output))
        {
            await RejectChangedAsync(candidate.InboxItemId, cancellationToken);
        }

        var cleanupFiles = new List<IngestCleanupFile>(
            candidate.Sources.Count + 1);
        var matchedInputs = new HashSet<Guid>();
        foreach (IngestSourceRecord source in candidate.Sources)
        {
            ProcessingPlanInput? input = processed.Inputs.SingleOrDefault(
                value => StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetFullPath(value.FullPath),
                    Path.GetFullPath(source.FullPath)));
            if (input is null
                || !matchedInputs.Add(input.SourceFileId)
                || input.Size != source.Size
                || input.LastWriteTimeUtc != source.LastWriteTimeUtc
                || !Matches(source))
            {
                await RejectChangedAsync(candidate.InboxItemId, cancellationToken);
            }

            ProcessingPlanInput matchedInput = input!;

            string actualHash = await HashOrRejectAsync(
                candidate.InboxItemId,
                source.FullPath,
                cancellationToken);
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    actualHash,
                    matchedInput.Sha256)
                || !Matches(source))
            {
                await RejectChangedAsync(candidate.InboxItemId, cancellationToken);
            }

            cleanupFiles.Add(new IngestCleanupFile(
                IngestCleanupKind.InboxSource,
                source.FullPath,
                source.Size,
                matchedInput.Sha256.ToUpperInvariant()));
        }

        if (matchedInputs.Count != processed.Inputs.Count)
        {
            await RejectChangedAsync(candidate.InboxItemId, cancellationToken);
        }

        cleanupFiles.Add(new IngestCleanupFile(
            IngestCleanupKind.ProcessingOutput,
            output.FullPath,
            output.Size,
            output.Sha256.ToUpperInvariant()));
        return new PreparedIngestInput(
            IngestCleanupKind.ProcessingOutput,
            output.FullPath,
            output.Size,
            output.LastWriteTimeUtc,
            output.Sha256.ToUpperInvariant(),
            cleanupFiles);
    }

    private async Task<string> HashOrRejectAsync(
        Guid inboxItemId,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await hasher.ComputeSha256Async(path, cancellationToken);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException)
        {
            await RejectChangedAsync(inboxItemId, cancellationToken);
            throw;
        }
    }

    private async Task RejectChangedAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await invalidation.InvalidateAsync(inboxItemId, cancellationToken);
        throw new IngestInputException("ingest.source_changed");
    }

    private static bool Matches(IngestSourceRecord source)
    {
        var file = new FileInfo(source.FullPath);
        return file.Exists
            && file.Length == source.Size
            && new DateTimeOffset(file.LastWriteTimeUtc) == source.LastWriteTimeUtc;
    }

    private static bool Matches(ProcessedOutputRecord output)
    {
        var file = new FileInfo(output.FullPath);
        return file.Exists
            && file.Length == output.Size
            && new DateTimeOffset(file.LastWriteTimeUtc) == output.LastWriteTimeUtc;
    }
}
