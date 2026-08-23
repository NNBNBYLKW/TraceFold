using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using Xunit;

namespace TraceFold.Media.Application.Tests.Processing;

public sealed class ProcessingPlannerTests
{
    private static readonly DateTimeOffset SnapshotTime =
        DateTimeOffset.Parse("2026-08-23T12:00:00Z");

    [Fact]
    public void Planner_creates_a_new_job_and_uses_only_processor_owned_plan_data()
    {
        Guid itemId = Guid.NewGuid();
        ProcessingCandidate candidate = Candidate(itemId, "mp4_m4a_merge");
        var processor = new FakeProcessor("merge.v1", "3", "mp4_m4a_merge", 100);
        var planner = new ProcessingPlanner(new MediaProcessorRegistry([processor]));

        PlannedProcessing planned = planner.Create(candidate);

        Assert.Equal(itemId, planned.Plan.InboxItemId);
        Assert.Equal(planned.JobId, planned.Plan.JobId);
        Assert.Equal("merge.v1", planned.Plan.ProcessorKey);
        Assert.Equal("3", planned.Plan.ProcessorVersion);
        Assert.Equal(3, planned.Attempt);
        Assert.Equal(
            ".tracefold/processing/" + planned.JobId.ToString("N"),
            planned.Plan.WorkDirectoryRelativePath.Replace('\\', '/'));
        Assert.All(planned.Plan.Inputs, input =>
            Assert.Matches("^[0-9A-F]{64}$", input.Sha256));
    }

    [Fact]
    public void Planner_rejects_candidates_without_a_processing_state()
    {
        ProcessingCandidate candidate = Candidate(
            Guid.NewGuid(),
            "mp4_m4a_merge") with
        {
            State = InboxItemState.ReadyToTag
        };
        var planner = CreatePlanner();

        Assert.Throws<ProcessingPlanRejectedException>(() => planner.Create(candidate));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    public void Planner_rejects_missing_or_invalid_source_hashes(string sha256)
    {
        ProcessingCandidate candidate = Candidate(Guid.NewGuid(), "mp4_m4a_merge");
        candidate = candidate with
        {
            Sources = candidate.Sources
                .Select(source => source with { Sha256 = sha256 })
                .ToArray()
        };

        Assert.Throws<ProcessingPlanRejectedException>(() =>
            CreatePlanner().Create(candidate));
    }

    [Fact]
    public void Planner_rejects_a_processor_plan_for_another_item()
    {
        ProcessingCandidate candidate = Candidate(Guid.NewGuid(), "mp4_m4a_merge");
        var processor = new FakeProcessor(
            "merge.v1",
            "1",
            "mp4_m4a_merge",
            100,
            replaceItemId: Guid.NewGuid());
        var planner = new ProcessingPlanner(new MediaProcessorRegistry([processor]));

        Assert.Throws<ProcessingPlanRejectedException>(() => planner.Create(candidate));
    }

    private static ProcessingPlanner CreatePlanner() =>
        new(new MediaProcessorRegistry(
            [new FakeProcessor("merge.v1", "1", "mp4_m4a_merge", 100)]));

    private static ProcessingCandidate Candidate(Guid itemId, string capability) =>
        new(
            itemId,
            InboxItemState.RequiresProcessing,
            MediaKind.Video,
            "paired.mp4",
            capability,
            [
                new ProcessingCandidateSource(
                    Guid.NewGuid(),
                    "C:\\inbox\\paired.mp4",
                    "paired.mp4",
                    ".mp4",
                    100,
                    SnapshotTime,
                    new string('A', 64),
                    IsPrimary: true),
                new ProcessingCandidateSource(
                    Guid.NewGuid(),
                    "C:\\inbox\\paired.m4a",
                    "paired.m4a",
                    ".m4a",
                    50,
                    SnapshotTime,
                    new string('B', 64),
                    IsPrimary: false)
            ],
            PreviousAttempts: 2);

    private sealed class FakeProcessor(
        string key,
        string version,
        string capability,
        int priority,
        Guid? replaceItemId = null) : IMediaProcessor
    {
        public MediaProcessorDescriptor Descriptor { get; } =
            new(key, version, capability, priority, "Merge");

        public ProcessingPlanDefinition CreatePlan(
            Guid jobId,
            ProcessingCandidate candidate)
        {
            IReadOnlyList<ProcessingPlanInput> inputs = candidate.Sources
                .Select((source, index) => new ProcessingPlanInput(
                    source.SourceFileId,
                    index == 0 ? "video" : "audio",
                    source.FullPath,
                    source.Filename,
                    source.Size,
                    source.LastWriteTimeUtc,
                    source.Sha256!))
                .ToArray();
            return new ProcessingPlanDefinition(
                Guid.NewGuid(),
                jobId,
                replaceItemId ?? candidate.InboxItemId,
                Descriptor.Key,
                Descriptor.Version,
                Descriptor.DisplayName,
                Descriptor.Capability,
                candidate.ActualFilename,
                Path.Combine(".tracefold", "processing", jobId.ToString("N")),
                ["check_sources", "merge", "verify_output"],
                inputs);
        }

        public Task<ProcessedOutputDraft> ExecuteAsync(
            ProcessingExecutionContext context,
            IProgress<ProcessingProgressUpdate> progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
