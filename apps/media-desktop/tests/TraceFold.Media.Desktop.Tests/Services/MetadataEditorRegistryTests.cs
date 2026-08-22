using TraceFold.Media.Desktop.Services;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Services;

public sealed class MetadataEditorRegistryTests
{
    [Fact]
    public async Task FlushAllAsync_flushes_every_registered_editor()
    {
        var registry = new MetadataEditorRegistry();
        var first = new RecordingEditor();
        var second = new RecordingEditor();
        registry.Register(first);
        registry.Register(second);

        await registry.FlushAllAsync(CancellationToken.None);

        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task FlushAllAsync_attempts_all_editors_before_reporting_failures()
    {
        var registry = new MetadataEditorRegistry();
        var failing = new RecordingEditor(throwOnFlush: true);
        var succeeding = new RecordingEditor();
        registry.Register(failing);
        registry.Register(succeeding);

        await Assert.ThrowsAsync<AggregateException>(() =>
            registry.FlushAllAsync(CancellationToken.None));

        Assert.Equal(1, failing.CallCount);
        Assert.Equal(1, succeeding.CallCount);
    }

    private sealed class RecordingEditor(bool throwOnFlush = false)
        : IMetadataEditorFlushable
    {
        public int CallCount { get; private set; }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return throwOnFlush
                ? Task.FromException(new InvalidOperationException("save failed"))
                : Task.CompletedTask;
        }
    }
}
