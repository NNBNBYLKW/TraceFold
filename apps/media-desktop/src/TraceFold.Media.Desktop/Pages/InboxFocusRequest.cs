namespace TraceFold.Media.Desktop.Pages;

public enum InboxFocusTarget
{
    List,
    Item,
    Title
}

public sealed record InboxFocusRequest(
    InboxFocusTarget Target,
    Guid? InboxItemId = null);
