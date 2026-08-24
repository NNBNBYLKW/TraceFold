using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Desktop.Dialogs;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Dialogs;

public sealed class ConflictDialogViewModelTests
{
    private static readonly Guid NameMatchId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContentMatchId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Same_name_match_is_selected_by_default()
    {
        var viewModel = new ConflictDialogViewModel(CaseWithNamedAndContentMatches());

        Assert.Equal(viewModel.Case.DefaultReplaceMatchId, viewModel.SelectedMatch?.Id);
        Assert.True(viewModel.CanReplace);
    }

    [Fact]
    public void Content_only_case_requires_explicit_selection_for_replace()
    {
        var viewModel = new ConflictDialogViewModel(ContentOnlyCase());

        Assert.Null(viewModel.SelectedMatch);
        Assert.False(viewModel.CanReplace);

        viewModel.SelectedMatch = viewModel.Matches[1];

        Assert.True(viewModel.CanReplace);
    }

    [Fact]
    public void Persisted_choice_wins_after_interrupted_confirmation()
    {
        ConflictCaseDto restored = ContentOnlyCase() with
        {
            PersistedResolution = ConflictResolution.Replace,
            PersistedSelectedMatchId = ContentMatchId
        };

        var viewModel = new ConflictDialogViewModel(restored);

        Assert.Equal(ContentMatchId, viewModel.SelectedMatch?.Id);
    }

    private static ConflictCaseDto CaseWithNamedAndContentMatches() =>
        CreateCase(
            [
                Match(NameMatchId, ConflictReason.Name),
                Match(ContentMatchId, ConflictReason.Content)
            ],
            NameMatchId);

    private static ConflictCaseDto ContentOnlyCase() =>
        CreateCase(
            [
                Match(Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    ConflictReason.Content),
                Match(ContentMatchId, ConflictReason.Content)
            ],
            null);

    private static ConflictCaseDto CreateCase(
        IReadOnlyList<ConflictMediaDto> matches,
        Guid? defaultMatchId) =>
        new(
            Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"),
            Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"),
            Media(
                Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"),
                "incoming.jpg",
                isIncoming: true,
                ConflictReason.None),
            matches,
            null,
            null,
            defaultMatchId,
            true);

    private static ConflictMediaDto Match(Guid id, ConflictReason reason) =>
        Media(id, $"{id:N}.jpg", isIncoming: false, reason);

    private static ConflictMediaDto Media(
        Guid id,
        string filename,
        bool isIncoming,
        ConflictReason reason) =>
        new(
            id,
            isIncoming,
            !isIncoming,
            isIncoming ? null : Guid.NewGuid(),
            filename,
            filename,
            MediaKind.Image,
            1024,
            DateTimeOffset.Parse("2026-08-24T10:00:00Z"),
            new string(isIncoming ? 'A' : 'B', 64),
            reason,
            new ConflictMetadataSnapshot(null, [], null, []),
            new ConflictTechnicalMetadata("jpg", 1920, 1080, null),
            null);
}
