using TraceFold.Media.Application.Processing;
using Xunit;

namespace TraceFold.Media.Application.Tests.Processing;

public sealed class MediaProcessorRegistryTests
{
    [Fact]
    public void Registry_resolves_the_only_processor_for_a_capability()
    {
        var expected = new FakeProcessor("merge.v1", "1", "mp4_m4a_merge", 100);
        var registry = new MediaProcessorRegistry([expected]);

        Assert.Same(expected, registry.Resolve("mp4_m4a_merge"));
    }

    [Fact]
    public void Registry_selects_the_highest_priority_without_a_tie()
    {
        var lower = new FakeProcessor("merge.old", "1", "mp4_m4a_merge", 50);
        var higher = new FakeProcessor("merge.new", "2", "mp4_m4a_merge", 100);
        var registry = new MediaProcessorRegistry([lower, higher]);

        Assert.Same(higher, registry.Resolve("mp4_m4a_merge"));
    }

    [Fact]
    public void Registry_rejects_duplicate_keys_and_tied_capabilities()
    {
        Assert.Throws<InvalidOperationException>(() => new MediaProcessorRegistry(
        [
            new FakeProcessor("same", "1", "a", 100),
            new FakeProcessor("same", "2", "b", 100)
        ]));
        Assert.Throws<InvalidOperationException>(() => new MediaProcessorRegistry(
        [
            new FakeProcessor("one", "1", "shared", 100),
            new FakeProcessor("two", "1", "shared", 100)
        ]));
    }

    [Fact]
    public void Registry_reports_an_unavailable_capability_with_a_stable_error()
    {
        var registry = new MediaProcessorRegistry(
            [new FakeProcessor("merge", "1", "known", 100)]);

        ProcessingCapabilityUnavailableException error = Assert.Throws<
            ProcessingCapabilityUnavailableException>(() => registry.Resolve("missing"));

        Assert.Equal("processing.capability_unavailable", error.ErrorCode);
        Assert.False(error.IsRetryable);
    }

    private sealed class FakeProcessor(
        string key,
        string version,
        string capability,
        int priority) : IMediaProcessor
    {
        public MediaProcessorDescriptor Descriptor { get; } =
            new(key, version, capability, priority, key);

        public ProcessingPlanDefinition CreatePlan(
            Guid jobId,
            ProcessingCandidate candidate) => throw new NotSupportedException();

        public Task<ProcessedOutputDraft> ExecuteAsync(
            ProcessingExecutionContext context,
            IProgress<ProcessingProgressUpdate> progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
