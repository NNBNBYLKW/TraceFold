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
}
