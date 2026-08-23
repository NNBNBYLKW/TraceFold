using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Domain.Processing;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class IngestInputResolverTests
{
    [Fact]
    public async Task Ordinary_ready_item_uses_its_single_primary_source()
    {
        using var fixture = InputFixture.Ordinary();

        PreparedIngestInput input = await fixture.Resolver.ResolveAsync(
            fixture.Candidate,
            TestContext.Current.CancellationToken);

        Assert.Equal(fixture.Candidate.Sources.Single().FullPath, input.FullPath);
        Assert.Single(input.CleanupFiles);
        Assert.Equal(IngestCleanupKind.InboxSource, input.CleanupFiles[0].Kind);
    }

    [Fact]
    public async Task Processed_ready_item_uses_output_and_keeps_both_originals_in_cleanup()
    {
        using var fixture = InputFixture.Processed();

        PreparedIngestInput input = await fixture.Resolver.ResolveAsync(
            fixture.Candidate,
            TestContext.Current.CancellationToken);

        Assert.Equal(fixture.Output!.Output.FullPath, input.FullPath);
        Assert.Equal(3, input.CleanupFiles.Count);
        Assert.Equal(2, input.CleanupFiles.Count(file =>
            file.Kind == IngestCleanupKind.InboxSource));
        Assert.Single(input.CleanupFiles, file =>
            file.Kind == IngestCleanupKind.ProcessingOutput);
    }

    [Fact]
    public async Task Changed_original_invalidates_processed_input_before_ingest()
    {
        using var fixture = InputFixture.Processed();
        fixture.Hasher.Override(
            fixture.Candidate.Sources[1].FullPath,
            new string('F', 64));

        IngestInputException error = await Assert.ThrowsAsync<IngestInputException>(
            () => fixture.Resolver.ResolveAsync(
                fixture.Candidate,
                TestContext.Current.CancellationToken));

        Assert.Equal("ingest.source_changed", error.ErrorCode);
        Assert.Equal([fixture.Candidate.InboxItemId], fixture.Invalidation.ItemIds);
    }

    private sealed class InputFixture : IDisposable
    {
        private readonly string root;

        private InputFixture(
            string root,
            IngestCandidate candidate,
            ValidatedProcessingOutput? output,
            MappingHasher hasher)
        {
            this.root = root;
            Candidate = candidate;
            Output = output;
            Hasher = hasher;
            Invalidation = new RecordingInvalidation();
            Resolver = new IngestInputResolver(
                new InputProcessingStore(output),
                hasher,
                Invalidation);
        }

        public IngestCandidate Candidate { get; }
        public ValidatedProcessingOutput? Output { get; }
        public MappingHasher Hasher { get; }
        public RecordingInvalidation Invalidation { get; }
        public IngestInputResolver Resolver { get; }

        public static InputFixture Ordinary()
        {
            string root = CreateRoot();
            string source = Write(root, "photo.jpg", [1, 2, 3]);
            const string hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            var hasher = new MappingHasher((source, hash));
            return new InputFixture(
                root,
                CreateCandidate([Source(source, primary: true)], MediaKind.Image),
                null,
                hasher);
        }

        public static InputFixture Processed()
        {
            string root = CreateRoot();
            string video = Write(root, "clip.mp4", [1, 2, 3]);
            string audio = Write(root, "clip.m4a", [4, 5]);
            string outputPath = Write(root, "processed.mp4", [6, 7, 8, 9]);
            string videoHash = new('A', 64);
            string audioHash = new('B', 64);
            string outputHash = new('C', 64);
            var hasher = new MappingHasher(
                (video, videoHash),
                (audio, audioHash),
                (outputPath, outputHash));
            IngestCandidate candidate = CreateCandidate(
                [Source(video, true), Source(audio, false)],
                MediaKind.Video);
            Guid jobId = Guid.NewGuid();
            var output = new ValidatedProcessingOutput(
                new ProcessedOutputRecord(
                    jobId,
                    candidate.InboxItemId,
                    outputPath,
                    Path.GetFileName(outputPath),
                    new FileInfo(outputPath).Length,
                    new FileInfo(outputPath).LastWriteTimeUtc,
                    outputHash,
                    TimeSpan.FromSeconds(1),
                    true,
                    true,
                    true),
                [
                    Input(video, "video", videoHash),
                    Input(audio, "audio", audioHash)
                ]);
            return new InputFixture(root, candidate, output, hasher);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);

        private static string CreateRoot() => Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"tracefold-input-{Guid.NewGuid():N}"))
            .FullName;

        private static string Write(string root, string filename, byte[] bytes)
        {
            string path = Path.Combine(root, filename);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static IngestSourceRecord Source(string path, bool primary)
        {
            var file = new FileInfo(path);
            return new IngestSourceRecord(
                path,
                file.Length,
                file.LastWriteTimeUtc,
                true,
                primary);
        }

        private static ProcessingPlanInput Input(
            string path,
            string role,
            string hash)
        {
            var file = new FileInfo(path);
            return new ProcessingPlanInput(
                Guid.NewGuid(),
                role,
                path,
                file.Name,
                file.Length,
                file.LastWriteTimeUtc,
                hash);
        }

        private static IngestCandidate CreateCandidate(
            IReadOnlyList<IngestSourceRecord> sources,
            MediaKind kind) =>
            new(
                Guid.NewGuid(),
                InboxItemState.ReadyToTag,
                kind,
                kind == MediaKind.Video ? "clip.mp4" : "photo.jpg",
                null,
                sources,
                MetadataSelection.Create(null, [], null, []),
                0);
    }

    private sealed class MappingHasher(params (string Path, string Hash)[] values)
        : IFileHasher
    {
        private readonly Dictionary<string, string> hashes = values.ToDictionary(
            value => Path.GetFullPath(value.Path),
            value => value.Hash,
            StringComparer.OrdinalIgnoreCase);

        public void Override(string path, string hash) =>
            hashes[Path.GetFullPath(path)] = hash;

        public Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken) =>
            Task.FromResult(hashes[Path.GetFullPath(path)]);
    }

    private sealed class RecordingInvalidation : IProcessingInvalidationService
    {
        public List<Guid> ItemIds { get; } = [];

        public Task InvalidateAsync(Guid inboxItemId, CancellationToken cancellationToken)
        {
            ItemIds.Add(inboxItemId);
            return Task.CompletedTask;
        }
    }

    private sealed class InputProcessingStore(ValidatedProcessingOutput? output)
        : IProcessingStore
    {
        public Task<ValidatedProcessingOutput?> LoadValidatedOutputAsync(Guid inboxItemId, CancellationToken cancellationToken) => Task.FromResult(output);
        public Task<ProcessingCandidate?> LoadCandidateAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProcessingJobRecord?> FindActiveAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<QueueProcessingResult> QueueAsync(PlannedProcessing planned, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProcessingJobRecord?> TryClaimNextAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid jobId, ProcessingProgressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid jobId, ProcessedOutputDraft output, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelQueuedAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InterruptAsync(Guid jobId, string errorCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid jobId, string errorCode, bool isRetryable, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessingJobRecord>> InterruptRecoverableAsync(string errorCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> InvalidateForSourceChangeAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
