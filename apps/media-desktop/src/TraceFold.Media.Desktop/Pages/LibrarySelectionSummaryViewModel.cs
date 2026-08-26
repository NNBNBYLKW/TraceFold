using TraceFold.Media.Application.Intake;

namespace TraceFold.Media.Desktop.Pages;

public sealed record LibrarySelectionSummaryViewModel(
    int TotalCount,
    int ImageCount,
    int VideoCount)
{
    public static LibrarySelectionSummaryViewModel Empty { get; } =
        new(0, 0, 0);

    public static LibrarySelectionSummaryViewModel From(
        IEnumerable<LibraryCardViewModel> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        LibraryCardViewModel[] cards = selected.ToArray();
        return new LibrarySelectionSummaryViewModel(
            cards.Length,
            cards.Count(card => card.MediaKind == MediaKind.Image),
            cards.Count(card => card.MediaKind == MediaKind.Video));
    }
}
