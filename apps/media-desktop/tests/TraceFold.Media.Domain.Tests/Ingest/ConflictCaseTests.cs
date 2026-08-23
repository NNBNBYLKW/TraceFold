using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Domain.Tests.Ingest;

public sealed class ConflictCaseTests
{
    [Fact]
    public void Replace_requires_one_match_and_can_resolve_once()
    {
        Guid matchId = Guid.NewGuid();
        ConflictCase conflict = OpenConflict(matchId);

        Assert.Throws<InvalidOperationException>(() =>
            conflict.Choose(ConflictResolution.Replace, null));

        conflict.Choose(ConflictResolution.Replace, matchId);
        conflict.Resolve();

        Assert.Equal(ConflictCaseStatus.Resolved, conflict.Status);
        Assert.Equal(matchId, conflict.SelectedMatchId);
        Assert.Throws<InvalidOperationException>(conflict.Skip);
    }

    [Fact]
    public void Keep_both_and_skip_never_carry_a_replace_target()
    {
        Guid matchId = Guid.NewGuid();
        ConflictCase conflict = OpenConflict(matchId);

        Assert.Throws<InvalidOperationException>(() =>
            conflict.Choose(ConflictResolution.KeepBoth, matchId));

        conflict.Skip();

        Assert.Equal(ConflictCaseStatus.Skipped, conflict.Status);
        Assert.Equal(ConflictResolution.Skip, conflict.Resolution);
        Assert.Null(conflict.SelectedMatchId);
    }

    [Fact]
    public void Replace_rejects_a_match_outside_the_persisted_match_set()
    {
        ConflictCase conflict = OpenConflict(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
            conflict.Choose(ConflictResolution.Replace, Guid.NewGuid()));

        Assert.Equal(ConflictCaseStatus.Open, conflict.Status);
    }

    [Fact]
    public void Repeating_the_same_persisted_choice_is_idempotent()
    {
        Guid matchId = Guid.NewGuid();
        ConflictCase conflict = OpenConflict(matchId);

        conflict.Choose(ConflictResolution.Replace, matchId);
        conflict.Choose(ConflictResolution.Replace, matchId);

        Assert.Equal(ConflictCaseStatus.Chosen, conflict.Status);
        Assert.Equal(matchId, conflict.SelectedMatchId);
    }

    [Fact]
    public void Changing_a_persisted_choice_is_rejected()
    {
        Guid firstMatchId = Guid.NewGuid();
        Guid secondMatchId = Guid.NewGuid();
        ConflictCase conflict = OpenConflict(firstMatchId, secondMatchId);
        conflict.Choose(ConflictResolution.Replace, firstMatchId);

        Assert.Throws<InvalidOperationException>(() =>
            conflict.Choose(ConflictResolution.Replace, secondMatchId));
        Assert.Throws<InvalidOperationException>(() =>
            conflict.Choose(ConflictResolution.KeepBoth, null));
    }

    [Fact]
    public void Invalidating_an_open_case_records_the_reason_and_is_terminal()
    {
        ConflictCase conflict = OpenConflict(Guid.NewGuid());

        conflict.Invalidate("conflict.target_hash_changed");

        Assert.Equal(ConflictCaseStatus.Invalidated, conflict.Status);
        Assert.Equal("conflict.target_hash_changed", conflict.InvalidationReason);
        Assert.Throws<InvalidOperationException>(() =>
            conflict.Choose(ConflictResolution.KeepBoth, null));
    }

    private static ConflictCase OpenConflict(params Guid[] matchIds) =>
        ConflictCase.Open(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('A', 64),
            "photo.jpg",
            matchIds);
}
