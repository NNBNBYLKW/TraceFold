using TraceFold.Media.Application.Intake;
using Xunit;

namespace TraceFold.Media.Application.Tests.Intake;

public sealed class InboxActivityNotifierTests
{
    [Fact]
    public void Throwing_listener_does_not_break_publish_or_later_listeners()
    {
        var notifier = new InboxActivityNotifier();
        int received = 0;
        notifier.Changed += (_, _) => throw new InvalidOperationException("broken UI listener");
        notifier.Changed += (_, _) => received++;

        Exception? error = Record.Exception(
            () => notifier.Publish(InboxActivityKind.ScanCompleted));

        Assert.Null(error);
        Assert.Equal(1, received);
    }
}
