using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Processing;

public sealed class Mp4M4aMergeProcessor(
    IMediaTool mediaTool,
    IProcessingWorkspace workspace,
    IProcessingFileVerifier fileVerifier,
    IFileHasher fileHasher) : IMediaProcessor
{
    private static readonly IReadOnlyList<string> Steps =
        ["check_sources", "merge", "verify_output"];

    public MediaProcessorDescriptor Descriptor { get; } = new(
        "mp4_m4a_merge",
        "1",
        "mp4_m4a_merge",
        100,
        "合并视频和音频");

    public ProcessingPlanDefinition CreatePlan(
        Guid jobId,
        ProcessingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ProcessingCandidateSource[] videos = candidate.Sources
            .Where(source => StringComparer.OrdinalIgnoreCase.Equals(
                source.Extension,
                ".mp4"))
            .ToArray();
        ProcessingCandidateSource[] audios = candidate.Sources
            .Where(source => StringComparer.OrdinalIgnoreCase.Equals(
                source.Extension,
                ".m4a"))
            .ToArray();
        if (candidate.State != InboxItemState.RequiresProcessing
            || candidate.MediaKind != MediaKind.Video
            || !StringComparer.Ordinal.Equals(
                candidate.RequiredCapability,
                Descriptor.Capability)
            || videos.Length != 1
            || !videos[0].IsPrimary
            || audios.Length != 1
            || candidate.Sources.Count != 2
            || !HasSha256(videos[0])
            || !HasSha256(audios[0]))
        {
            throw new ProcessingPlanRejectedException(
                "processing.plan_invalid_pair");
        }

        IReadOnlyList<ProcessingPlanInput> inputs =
        [
            ToInput(videos[0], "video"),
            ToInput(audios[0], "audio")
        ];
        return new ProcessingPlanDefinition(
            Guid.NewGuid(),
            jobId,
            candidate.InboxItemId,
            Descriptor.Key,
            Descriptor.Version,
            Descriptor.DisplayName,
            Descriptor.Capability,
            candidate.ActualFilename,
            Path.Combine(".tracefold", "processing", jobId.ToString("N")),
            Steps,
            inputs);
    }

    public async Task<ProcessedOutputDraft> ExecuteAsync(
        ProcessingExecutionContext context,
        IProgress<ProcessingProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(progress);
        Guid jobId = context.Job.Id;
        try
        {
            ProcessingPlanInput video = RequireInput(context.Job.Plan, "video");
            ProcessingPlanInput audio = RequireInput(context.Job.Plan, "audio");
            progress.Report(new ProcessingProgressUpdate("check_sources", 0));
            await RequireSourceAsync(context.Folders, video, cancellationToken);
            await RequireSourceAsync(context.Folders, audio, cancellationToken);
            MediaProbeResult videoProbe = await mediaTool.ProbeAsync(
                video.FullPath,
                cancellationToken);
            MediaProbeResult audioProbe = await mediaTool.ProbeAsync(
                audio.FullPath,
                cancellationToken);
            if (!videoProbe.HasVideo
                || !audioProbe.HasAudio
                || videoProbe.Duration <= TimeSpan.Zero
                || audioProbe.Duration <= TimeSpan.Zero)
            {
                throw new ProcessingException(
                    "processing.input_streams_invalid",
                    isRetryable: false);
            }

            progress.Report(new ProcessingProgressUpdate("check_sources", 5));
            ProcessingWorkspacePaths paths = await workspace.PrepareAsync(
                jobId,
                context.Folders,
                context.Job.Plan.OutputFilename,
                cancellationToken);
            string[] arguments =
            [
                "-nostdin", "-n",
                "-i", video.FullPath,
                "-i", audio.FullPath,
                "-map", "0:v:0",
                "-map", "1:a:0",
                "-c", "copy",
                "-movflags", "+faststart"
            ];
            TimeSpan expectedDuration = videoProbe.Duration > audioProbe.Duration
                ? videoProbe.Duration
                : audioProbe.Duration;
            var request = new FfmpegRequest(
                arguments,
                paths.PartialOutputPath,
                expectedDuration);
            FfmpegResult result = await mediaTool.RunFfmpegAsync(
                request,
                new MergeProgress(progress),
                cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new MediaToolException(
                    "processing.ffmpeg_failed",
                    result.DiagnosticTail);
            }

            MediaProbeResult outputProbe = await mediaTool.ProbeAsync(
                paths.PartialOutputPath,
                cancellationToken);
            if (!outputProbe.HasVideo
                || !outputProbe.HasAudio
                || outputProbe.Duration <= TimeSpan.Zero)
            {
                throw new ProcessingException(
                    "processing.output_streams_invalid",
                    isRetryable: true);
            }

            await RequireSourceAsync(context.Folders, video, cancellationToken);
            await RequireSourceAsync(context.Folders, audio, cancellationToken);
            var partial = new FileInfo(paths.PartialOutputPath);
            if (!partial.Exists || partial.Length <= 0)
            {
                throw new ProcessingException(
                    "processing.output_missing",
                    isRetryable: true);
            }

            long size = partial.Length;
            DateTimeOffset lastWriteTimeUtc = partial.LastWriteTimeUtc;
            string sha256 = await fileHasher.ComputeSha256Async(
                paths.PartialOutputPath,
                cancellationToken);
            partial.Refresh();
            if (!partial.Exists
                || partial.Length != size
                || new DateTimeOffset(partial.LastWriteTimeUtc) != lastWriteTimeUtc)
            {
                throw new ProcessingException(
                    "processing.output_changed",
                    isRetryable: true);
            }

            progress.Report(new ProcessingProgressUpdate("verify_output", 99));
            string promoted = await workspace.PromoteVerifiedAsync(
                jobId,
                context.Folders,
                paths,
                cancellationToken);
            var final = new FileInfo(promoted);
            return new ProcessedOutputDraft(
                promoted,
                paths.FinalOutputRelativePath,
                final.Length,
                final.LastWriteTimeUtc,
                sha256,
                outputProbe.Duration,
                outputProbe.HasVideo,
                outputProbe.HasAudio);
        }
        catch (OperationCanceledException)
        {
            await TryCleanupAsync(jobId, context.Folders);
            throw;
        }
        catch (ProcessingException)
        {
            await TryCleanupAsync(jobId, context.Folders);
            throw;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            await TryCleanupAsync(jobId, context.Folders);
            throw new ProcessingException(
                "processing.workspace_failed",
                isRetryable: true);
        }
    }

    private static ProcessingPlanInput RequireInput(
        ProcessingPlanDefinition plan,
        string role)
    {
        ProcessingPlanInput[] matches = plan.Inputs
            .Where(input => StringComparer.Ordinal.Equals(input.Role, role))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new ProcessingPlanRejectedException(
                "processing.plan_invalid_pair");
    }

    private async Task RequireSourceAsync(
        LibraryFolders folders,
        ProcessingPlanInput input,
        CancellationToken cancellationToken)
    {
        if (!await fileVerifier.MatchesAsync(folders, input, cancellationToken))
        {
            throw new ProcessingException(
                "processing.source_changed",
                isRetryable: false);
        }
    }

    private async Task TryCleanupAsync(Guid jobId, LibraryFolders folders)
    {
        try
        {
            await workspace.CleanupUnverifiedAsync(
                jobId,
                folders,
                CancellationToken.None);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            // Preserve the original processing failure. Recovery retries guarded cleanup.
        }
    }

    private static ProcessingPlanInput ToInput(
        ProcessingCandidateSource source,
        string role) =>
        new(
            source.SourceFileId,
            role,
            source.FullPath,
            source.Filename,
            source.Size,
            source.LastWriteTimeUtc,
            source.Sha256!);

    private static bool HasSha256(ProcessingCandidateSource source) =>
        source.Sha256 is { Length: 64 } value
        && value.All(Uri.IsHexDigit);

    private sealed class MergeProgress(
        IProgress<ProcessingProgressUpdate> progress) : IProgress<int>
    {
        private int highest = 5;

        public void Report(int value)
        {
            int mapped = 5 + Math.Clamp(value, 0, 100) * 90 / 100;
            if (mapped <= highest)
            {
                return;
            }

            highest = mapped;
            progress.Report(new ProcessingProgressUpdate("merge", mapped));
        }
    }
}
