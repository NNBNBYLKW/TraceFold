using TraceFold.Media.Domain.Inbox;
using Xunit;

namespace TraceFold.Media.Domain.Tests.Inbox;

public sealed class InboxItemTests
{
    [Fact]
    public void Ready_item_allows_metadata_preview_and_ingest()
    {
        InboxItem item = InboxItem.Discover(Guid.NewGuid());
        item.MarkAnalyzing();
        item.MarkReady();

        Assert.Equal(InboxItemState.ReadyToTag, item.State);
        Assert.True(item.AllowedActions.HasFlag(InboxAction.EditMetadata));
        Assert.True(item.AllowedActions.HasFlag(InboxAction.Preview));
        Assert.True(item.AllowedActions.HasFlag(InboxAction.Ingest));
    }

    [Fact]
    public void Requires_processing_never_allows_ingest()
    {
        InboxItem item = InboxItem.Discover(Guid.NewGuid());
        item.MarkAnalyzing();
        item.MarkRequiresProcessing("mp4_m4a_merge");

        Assert.Equal(InboxItemState.RequiresProcessing, item.State);
        Assert.Equal("mp4_m4a_merge", item.RequiredCapability);
        Assert.False(item.AllowedActions.HasFlag(InboxAction.Ingest));
    }

    [Fact]
    public void Required_item_can_queue_start_and_complete_processing()
    {
        InboxItem item = InboxItem.Discover(Guid.NewGuid());
        item.MarkAnalyzing();
        item.MarkRequiresProcessing("mp4_m4a_merge");

        item.QueueProcessing();
        Assert.Equal(InboxItemState.ProcessingQueued, item.State);
        Assert.True(item.AllowedActions.HasFlag(InboxAction.CancelProcessing));

        item.BeginProcessing();
        Assert.Equal(InboxItemState.Processing, item.State);
        Assert.True(item.AllowedActions.HasFlag(InboxAction.StopProcessing));

        item.CompleteProcessing();
        Assert.Equal(InboxItemState.ReadyToTag, item.State);
        Assert.Null(item.RequiredCapability);
        Assert.True(item.AllowedActions.HasFlag(InboxAction.Ingest));
    }

    [Fact]
    public void Interrupted_processing_preserves_capability_and_requires_manual_retry()
    {
        InboxItem item = ProcessingItem();

        item.InterruptProcessing();

        Assert.Equal(InboxItemState.ProcessingInterrupted, item.State);
        Assert.Equal("mp4_m4a_merge", item.RequiredCapability);
        Assert.True(item.AllowedActions.HasFlag(InboxAction.RetryProcessing));
        Assert.False(item.AllowedActions.HasFlag(InboxAction.Ingest));
    }

    [Fact]
    public void Processing_failure_does_not_turn_into_ingest_recovery()
    {
        InboxItem item = ProcessingItem();

        item.FailProcessing("processing.ffmpeg_failed", isRetryable: true);

        Assert.Equal(InboxItemState.Failed, item.State);
        Assert.True(item.AllowedActions.HasFlag(InboxAction.RetryProcessing));
        Assert.False(item.AllowedActions.HasFlag(InboxAction.Retry));
    }

    [Fact]
    public void Cancelling_queued_processing_returns_to_required_state()
    {
        InboxItem item = InboxItem.Discover(Guid.NewGuid());
        item.MarkAnalyzing();
        item.MarkRequiresProcessing("mp4_m4a_merge");
        item.QueueProcessing();

        item.CancelQueuedProcessing();

        Assert.Equal(InboxItemState.RequiresProcessing, item.State);
        Assert.Equal("mp4_m4a_merge", item.RequiredCapability);
        Assert.True(item.AllowedActions.HasFlag(InboxAction.StartProcessing));
    }

    [Fact]
    public void Source_change_can_reset_queued_running_or_interrupted_processing()
    {
        InboxItem queued = RequiredItem();
        queued.QueueProcessing();
        InboxItem running = ProcessingItem();
        InboxItem interrupted = ProcessingItem();
        interrupted.InterruptProcessing();

        queued.ResetForSourceChange();
        running.ResetForSourceChange();
        interrupted.ResetForSourceChange();

        Assert.Equal(InboxItemState.WaitingForStability, queued.State);
        Assert.Equal(InboxItemState.WaitingForStability, running.State);
        Assert.Equal(InboxItemState.WaitingForStability, interrupted.State);
    }

    [Fact]
    public void Source_change_preserves_identity_but_returns_to_waiting()
    {
        Guid id = Guid.NewGuid();
        InboxItem item = InboxItem.Discover(id);
        item.MarkAnalyzing();
        item.MarkReady();

        item.ResetForSourceChange();

        Assert.Equal(id, item.Id);
        Assert.Equal(InboxItemState.WaitingForStability, item.State);
    }

    [Fact]
    public void Invalid_transition_is_rejected()
    {
        InboxItem item = InboxItem.Discover(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(item.MarkReady);
    }

    [Fact]
    public void Retryable_failure_exposes_error_and_retry_action()
    {
        InboxItem item = InboxItem.Discover(Guid.NewGuid());
        item.MarkAnalyzing();

        item.Fail("scan.access_denied", isRetryable: true);

        Assert.Equal(InboxItemState.Failed, item.State);
        Assert.Equal("scan.access_denied", item.ErrorCode);
        Assert.True(item.IsRetryable);
        Assert.True(item.AllowedActions.HasFlag(InboxAction.Retry));
    }

    private static InboxItem RequiredItem()
    {
        InboxItem item = InboxItem.Discover(Guid.NewGuid());
        item.MarkAnalyzing();
        item.MarkRequiresProcessing("mp4_m4a_merge");
        return item;
    }

    private static InboxItem ProcessingItem()
    {
        InboxItem item = RequiredItem();
        item.QueueProcessing();
        item.BeginProcessing();
        return item;
    }
}
