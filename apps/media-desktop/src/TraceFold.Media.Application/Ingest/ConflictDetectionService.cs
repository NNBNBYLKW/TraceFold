using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public sealed class ConflictDetectionService(
    IConflictCaseStore store,
    IControlledMediaStorage storage,
    IMediaTool? mediaTool = null) : IConflictDetectionService
{
    public async Task<ConflictCaseRecord?> DetectAsync(
        IngestPreparation preparation,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(folders);
        ConflictScan scan = await ScanAsync(
            preparation,
            folders,
            existingIds: null,
            cancellationToken);
        if (scan.Matches.Count == 0)
        {
            return null;
        }

        return await store.ReplaceOpenAsync(
            preparation.InboxItemId,
            ToInputSnapshot(preparation),
            scan.Matches,
            cancellationToken);
    }

    public async Task<ConflictRevalidationResult> RevalidateAsync(
        ConflictCaseRecord conflict,
        IngestPreparation current,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(folders);
        string? inputError = CompareInput(conflict.Input, current);
        if (inputError is not null)
        {
            return ConflictRevalidationResult.Invalid(inputError);
        }

        IReadOnlyDictionary<string, Guid> existingIds = conflict.Matches
            .ToDictionary(MatchKey, match => match.Id, StringComparer.Ordinal);
        ConflictScan scan = await ScanAsync(
            current,
            folders,
            existingIds,
            cancellationToken);
        IReadOnlyDictionary<string, ConflictMatchRecord> previous =
            conflict.Matches.ToDictionary(
                MatchKey,
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, ConflictMatchRecord> observed =
            scan.Matches.ToDictionary(
                MatchKey,
                StringComparer.Ordinal);

        foreach (ConflictMatchRecord persisted in conflict.Matches)
        {
            string key = MatchKey(persisted);
            if (!observed.TryGetValue(key, out ConflictMatchRecord? candidate))
            {
                ConflictMatchRecord? samePath = scan.Matches.FirstOrDefault(
                    match => StringComparer.OrdinalIgnoreCase.Equals(
                        match.RelativePath,
                        persisted.RelativePath));
                if (samePath is not null
                    && samePath.IsRegistered != persisted.IsRegistered)
                {
                    return ConflictRevalidationResult.Invalid(
                        "conflict.target_registration_changed");
                }

                if (persisted.IsRegistered)
                {
                    RegisteredConflictCandidate? registered =
                        scan.RegisteredCandidates.FirstOrDefault(
                            item => item.AssetId == persisted.AssetId);
                    if (registered is null)
                    {
                        return ConflictRevalidationResult.Invalid(
                            "conflict.target_registration_changed");
                    }

                    if (registered.AssetMetadataVersion
                        != persisted.AssetMetadataVersion)
                    {
                        return ConflictRevalidationResult.Invalid(
                            "conflict.asset_version_changed");
                    }
                }

                return ConflictRevalidationResult.Invalid(
                    "conflict.target_hash_changed");
            }

            if (candidate.IsRegistered != persisted.IsRegistered
                || candidate.AssetId != persisted.AssetId)
            {
                return ConflictRevalidationResult.Invalid(
                    "conflict.target_registration_changed");
            }

            if (candidate.AssetMetadataVersion
                != persisted.AssetMetadataVersion)
            {
                return ConflictRevalidationResult.Invalid(
                    "conflict.asset_version_changed");
            }

            if (!SameTargetIdentity(persisted, candidate))
            {
                return ConflictRevalidationResult.Invalid(
                    "conflict.target_hash_changed");
            }
        }

        foreach (ConflictMatchRecord candidate in scan.Matches)
        {
            if (previous.ContainsKey(MatchKey(candidate)))
            {
                continue;
            }

            bool registrationChanged = candidate.IsRegistered
                || conflict.Matches.Any(match =>
                    StringComparer.OrdinalIgnoreCase.Equals(
                        match.RelativePath,
                        candidate.RelativePath)
                    && match.IsRegistered != candidate.IsRegistered);
            return ConflictRevalidationResult.Invalid(registrationChanged
                ? "conflict.target_registration_changed"
                : "conflict.target_hash_changed");
        }

        if (conflict.SelectedMatchId is Guid selectedId
            && scan.Matches.All(match => match.Id != selectedId))
        {
            return ConflictRevalidationResult.Invalid(
                "conflict.selected_match_changed");
        }

        return ConflictRevalidationResult.Valid(scan.Matches);
    }

    private async Task<ConflictScan> ScanAsync(
        IngestPreparation preparation,
        LibraryFolders folders,
        IReadOnlyDictionary<string, Guid>? existingIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RegisteredConflictCandidate> registered =
            await store.FindRegisteredCandidatesAsync(
                preparation.TargetFilename,
                preparation.Input.Sha256,
                cancellationToken);
        var snapshots = new Dictionary<string, LibraryFileSnapshot?>(
            StringComparer.OrdinalIgnoreCase);
        async Task<LibraryFileSnapshot?> InspectAsync(string filename)
        {
            if (!snapshots.TryGetValue(filename, out LibraryFileSnapshot? value))
            {
                value = await storage.InspectTargetAsync(
                    folders,
                    filename,
                    cancellationToken);
                snapshots[filename] = value;
            }

            return value;
        }

        var matches = new Dictionary<string, ConflictMatchRecord>(
            StringComparer.Ordinal);
        foreach (RegisteredConflictCandidate candidate in registered)
        {
            LibraryFileSnapshot? snapshot = await InspectAsync(
                candidate.ActualFilename);
            if (snapshot is null)
            {
                continue;
            }

            ConflictReason reason = CurrentReason(
                snapshot,
                preparation.TargetFilename,
                preparation.Input.Sha256);
            if (reason == ConflictReason.None)
            {
                continue;
            }

            string key = RegisteredKey(candidate.AssetId);
            if (matches.TryGetValue(key, out ConflictMatchRecord? existing))
            {
                matches[key] = existing with
                {
                    Reason = existing.Reason | reason
                };
                continue;
            }

            ConflictTechnicalMetadata technical =
                await ConflictTechnicalMetadataReader.ReadAsync(
                    snapshot.RelativePath,
                    snapshot.FullPath,
                    candidate.MediaKind,
                    mediaTool,
                    cancellationToken);
            matches[key] = new ConflictMatchRecord(
                ResolveMatchId(key, existingIds),
                reason,
                true,
                candidate.AssetId,
                candidate.AssetMetadataVersion,
                snapshot.RelativePath,
                Path.GetFileName(snapshot.RelativePath),
                candidate.DisplayName,
                candidate.MediaKind,
                snapshot.Size,
                snapshot.LastWriteTimeUtc,
                snapshot.Sha256,
                candidate.Metadata,
                technical);
        }

        LibraryFileSnapshot? target = await InspectAsync(
            preparation.TargetFilename);
        if (target is not null
            && !matches.Values.Any(match =>
                match.IsRegistered
                && StringComparer.OrdinalIgnoreCase.Equals(
                    match.RelativePath,
                    target.RelativePath)))
        {
            string key = UnmanagedKey(target.RelativePath);
            ConflictTechnicalMetadata technical =
                await ConflictTechnicalMetadataReader.ReadAsync(
                    target.RelativePath,
                    target.FullPath,
                    preparation.MediaKind,
                    mediaTool,
                    cancellationToken);
            matches[key] = new ConflictMatchRecord(
                ResolveMatchId(key, existingIds),
                CurrentReason(
                    target,
                    preparation.TargetFilename,
                    preparation.Input.Sha256),
                false,
                null,
                null,
                target.RelativePath,
                Path.GetFileName(target.RelativePath),
                null,
                preparation.MediaKind,
                target.Size,
                target.LastWriteTimeUtc,
                target.Sha256,
                new ConflictMetadataSnapshot(null, [], null, []),
                technical);
        }

        ConflictMatchRecord[] ordered = matches.Values
            .OrderByDescending(match =>
                match.Reason.HasFlag(ConflictReason.Name))
            .ThenBy(
                match => match.ActualFilename,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.AssetId)
            .ThenBy(match => match.Id)
            .ToArray();
        return new ConflictScan(registered, ordered);
    }

    private static ConflictInputSnapshot ToInputSnapshot(
        IngestPreparation preparation) =>
        new(
            preparation.Input.FullPath,
            preparation.Input.Size,
            preparation.Input.LastWriteTimeUtc,
            preparation.Input.Sha256,
            preparation.TargetFilename,
            preparation.MetadataVersion);

    private static string? CompareInput(
        ConflictInputSnapshot persisted,
        IngestPreparation current)
    {
        PreparedIngestInput input = current.Input;
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                persisted.FullPath,
                input.FullPath))
        {
            return "conflict.source_path_changed";
        }

        if (persisted.Size != input.Size)
        {
            return "conflict.source_size_changed";
        }

        if (persisted.LastWriteTimeUtc != input.LastWriteTimeUtc)
        {
            return "conflict.source_timestamp_changed";
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(
                persisted.Sha256,
                input.Sha256))
        {
            return "conflict.source_hash_changed";
        }

        return !StringComparer.OrdinalIgnoreCase.Equals(
            persisted.TargetFilename,
            current.TargetFilename)
            ? "conflict.target_filename_changed"
            : null;
    }

    private static ConflictReason CurrentReason(
        LibraryFileSnapshot target,
        string requestedFilename,
        string sourceSha256)
    {
        ConflictReason reason = ConflictReason.None;
        if (StringComparer.OrdinalIgnoreCase.Equals(
                target.RelativePath,
                requestedFilename))
        {
            reason |= ConflictReason.Name;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(
                target.Sha256,
                sourceSha256))
        {
            reason |= ConflictReason.Content;
        }

        return reason;
    }

    private static bool SameTargetIdentity(
        ConflictMatchRecord persisted,
        ConflictMatchRecord current) =>
        persisted.Reason == current.Reason
        && StringComparer.OrdinalIgnoreCase.Equals(
            persisted.RelativePath,
            current.RelativePath)
        && persisted.Size == current.Size
        && persisted.LastWriteTimeUtc == current.LastWriteTimeUtc
        && StringComparer.OrdinalIgnoreCase.Equals(
            persisted.Sha256,
            current.Sha256);

    private static string MatchKey(ConflictMatchRecord match) =>
        match.AssetId is Guid assetId
            ? RegisteredKey(assetId)
            : UnmanagedKey(match.RelativePath);

    private static string RegisteredKey(Guid assetId) =>
        $"asset:{assetId:D}";

    private static string UnmanagedKey(string relativePath) =>
        $"unmanaged:{relativePath.ToUpperInvariant()}";

    private static Guid ResolveMatchId(
        string key,
        IReadOnlyDictionary<string, Guid>? existingIds) =>
        existingIds is not null
        && existingIds.TryGetValue(key, out Guid existingId)
            ? existingId
            : Guid.NewGuid();

    private sealed record ConflictScan(
        IReadOnlyList<RegisteredConflictCandidate> RegisteredCandidates,
        IReadOnlyList<ConflictMatchRecord> Matches);
}

internal static class ConflictTechnicalMetadataReader
{
    public static async Task<ConflictTechnicalMetadata> ReadAsync(
        string filename,
        string trustedPath,
        MediaKind mediaKind,
        IMediaTool? mediaTool,
        CancellationToken cancellationToken)
    {
        string format = Path.GetExtension(filename)
            .TrimStart('.')
            .ToLowerInvariant();
        TimeSpan? duration = null;
        if (mediaKind == MediaKind.Video && mediaTool is not null)
        {
            try
            {
                MediaProbeResult probe = await mediaTool.ProbeAsync(
                    trustedPath,
                    cancellationToken);
                if (probe.Duration > TimeSpan.Zero)
                {
                    duration = probe.Duration;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Technical metadata is best effort and never hides a conflict.
            }
        }

        return new ConflictTechnicalMetadata(
            format,
            null,
            null,
            duration);
    }
}
