using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public sealed class ConflictResolutionService(
    IConflictCaseStore conflictStore,
    IIngestStore ingestStore,
    IDirectorySettingsStore settingsStore,
    IIngestInputResolver inputResolver,
    IConflictDetectionService detector,
    IControlledMediaStorage storage,
    IIngestExecutionService executor,
    IInboxActivityNotifier activity,
    IMediaOperationCoordinator coordinator) : IConflictResolutionService
{
    public async Task<IngestResult> ResolveAsync(
        Guid conflictCaseId,
        ConflictResolution resolution,
        Guid? selectedMatchId,
        CancellationToken cancellationToken)
    {
        IMediaOperationLease lease;
        try
        {
            lease = coordinator.Begin(cancellationToken);
        }
        catch (MediaOperationRejectedException)
        {
            return Result(
                IngestOutcome.ShuttingDown,
                "ingest.shutting_down",
                conflictCaseId);
        }

        await using (lease)
        {
            CancellationToken operationToken = lease.CancellationToken;
            ConflictCaseRecord conflict = await RequireActiveAsync(
                conflictCaseId,
                operationToken);
            if (resolution == ConflictResolution.Skip)
            {
                if (selectedMatchId is not null)
                {
                    throw new InvalidOperationException(
                        "Skip cannot select a conflict match.");
                }

                await conflictStore.SkipAsync(
                    conflictCaseId,
                    operationToken);
                activity.Publish(InboxActivityKind.ItemChanged);
                return Result(
                    IngestOutcome.Skipped,
                    "ingest.skipped",
                    conflictCaseId);
            }

            IngestCandidate candidate = await RequireCandidateAsync(
                conflict.InboxItemId,
                operationToken);
            LibraryFolders? folders = await settingsStore.LoadAsync(operationToken);
            if (folders is null)
            {
                return Result(
                    IngestOutcome.NotReady,
                    "ingest.not_ready",
                    conflictCaseId);
            }

            PreparedIngestInput input;
            try
            {
                input = await inputResolver.ResolveAsync(
                    candidate,
                    operationToken);
            }
            catch (IngestInputException error)
            {
                await conflictStore.InvalidateAsync(
                    conflictCaseId,
                    $"conflict.{error.ErrorCode.Replace("ingest.", string.Empty)}",
                    operationToken);
                activity.Publish(InboxActivityKind.ItemChanged);
                return Result(
                    IngestOutcome.ConflictChanged,
                    error.ErrorCode,
                    conflictCaseId: null);
            }

            IngestPreparation current = IngestPreparation.From(candidate, input);
            ConflictRevalidationResult validation = await detector.RevalidateAsync(
                conflict,
                current,
                folders,
                operationToken);
            if (!validation.IsValid)
            {
                return await InvalidateAndRedetectAsync(
                    conflict,
                    current,
                    folders,
                    validation.ErrorCode
                        ?? "conflict.identity_changed",
                    operationToken);
            }

            IngestExecutionPlan plan;
            try
            {
                plan = await BuildPlanAsync(
                    conflict,
                    validation.CurrentMatches,
                    resolution,
                    selectedMatchId,
                    folders,
                    operationToken);
            }
            catch (ConflictTargetChangedException)
            {
                return await InvalidateAndRedetectAsync(
                    conflict,
                    current,
                    folders,
                    "conflict.target_hash_changed",
                    operationToken);
            }

            await conflictStore.ChooseAsync(
                conflictCaseId,
                resolution,
                selectedMatchId,
                operationToken);
            IngestResult result = await executor.ExecuteAsync(
                current,
                plan,
                folders,
                operationToken);
            activity.Publish(InboxActivityKind.ItemChanged);
            return result;
        }
    }

    private async Task<IngestResult> InvalidateAndRedetectAsync(
        ConflictCaseRecord conflict,
        IngestPreparation current,
        LibraryFolders folders,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await conflictStore.InvalidateAsync(
            conflict.Id,
            errorCode,
            cancellationToken);
        ConflictCaseRecord? replacement = await detector.DetectAsync(
            current,
            folders,
            cancellationToken);
        activity.Publish(InboxActivityKind.ItemChanged);
        return replacement is null
            ? Result(
                IngestOutcome.ConflictChanged,
                errorCode,
                conflictCaseId: null)
            : Result(
                IngestOutcome.ConflictBlocked,
                "ingest.conflict",
                replacement.Id);
    }

    private async Task<IngestExecutionPlan> BuildPlanAsync(
        ConflictCaseRecord conflict,
        IReadOnlyList<ConflictMatchRecord> currentMatches,
        ConflictResolution resolution,
        Guid? selectedMatchId,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        if (resolution == ConflictResolution.KeepBoth)
        {
            if (selectedMatchId is not null)
            {
                throw new InvalidOperationException(
                    "Keep Both cannot select a replacement target.");
            }

            return IngestExecutionPlan.KeepBoth(
                conflict.Id,
                conflict.Input.TargetFilename);
        }

        if (resolution != ConflictResolution.Replace
            || selectedMatchId is not Guid matchId)
        {
            throw new InvalidOperationException(
                "Replace requires one persisted conflict match.");
        }

        ConflictMatchRecord selected = currentMatches.SingleOrDefault(
                match => match.Id == matchId)
            ?? throw new InvalidOperationException(
                "The selected conflict match is not active.");
        LibraryFileSnapshot? target = await storage.InspectTargetAsync(
            folders,
            selected.ActualFilename,
            cancellationToken);
        if (target is null || !SameIdentity(selected, target))
        {
            throw new ConflictTargetChangedException();
        }

        if (!selected.IsRegistered)
        {
            return IngestExecutionPlan.ReplaceUnregistered(
                conflict.Id,
                target);
        }

        return IngestExecutionPlan.ReplaceRegistered(
            conflict.Id,
            selected.AssetId
                ?? throw new InvalidOperationException(
                    "A registered match has no asset id."),
            selected.AssetMetadataVersion
                ?? throw new InvalidOperationException(
                    "A registered match has no metadata version."),
            target);
    }

    private async Task<ConflictCaseRecord> RequireActiveAsync(
        Guid conflictCaseId,
        CancellationToken cancellationToken)
    {
        ConflictCaseRecord? conflict = await conflictStore.LoadAsync(
            conflictCaseId,
            cancellationToken);
        if (conflict is null
            || conflict.Status is not
                (ConflictCaseStatus.Open or ConflictCaseStatus.Chosen))
        {
            throw new InvalidOperationException(
                "The conflict case is not active.");
        }

        return conflict;
    }

    private async Task<IngestCandidate> RequireCandidateAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        IngestCandidate? candidate = await ingestStore.LoadCandidateAsync(
            inboxItemId,
            cancellationToken);
        if (candidate is null || candidate.State != InboxItemState.ConflictBlocked)
        {
            throw new InvalidOperationException(
                "The conflict inbox item is not available for resolution.");
        }

        return candidate;
    }

    private static bool SameIdentity(
        ConflictMatchRecord match,
        LibraryFileSnapshot target) =>
        StringComparer.OrdinalIgnoreCase.Equals(
            match.RelativePath,
            target.RelativePath)
        && match.Size == target.Size
        && match.LastWriteTimeUtc == target.LastWriteTimeUtc
        && StringComparer.OrdinalIgnoreCase.Equals(
            match.Sha256,
            target.Sha256);

    private static IngestResult Result(
        IngestOutcome outcome,
        string message,
        Guid? conflictCaseId) =>
        new(outcome, null, null, conflictCaseId, message);

    private sealed class ConflictTargetChangedException : Exception;
}
