using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

/// <summary>
/// Injected coverage for the hand-back rule set of the temporary Windows app-theme switch: the
/// case where the harness that applied a change is still alive and gives it back in its own
/// <c>finally</c>. Every registry, broadcast, owner and file-system dependency is a declared
/// substitute, so nothing here touches the real registry, produces a real journal, broadcasts a
/// setting change, or starts a product/driver/UI process.
/// The two rule sets are exercised side by side: start-up recovery (the journalled owner is
/// provably gone) and hand-back (the same live harness presents the receipt apply issued). A live
/// owner is refused by the first and required by the second, and no caller obtains a hand-back by
/// copying the journalled owner fields or by declaring an owner kind of its own choosing.
/// </summary>
public sealed class WindowsAppThemeCoordinatorHandbackTests
{
    private const string Key = WindowsAppThemeCoordinator.ApprovedKeyPath;

    private const string Value = WindowsAppThemeCoordinator.ApprovedValueName;

    private const long OwnerTicks = 639276814407904666L;

    private const int OwnerPid = 4242;

    private static ThemeOwnerIdentity Owner(
        string context = "harness-runner",
        int processId = OwnerPid,
        long ticks = OwnerTicks,
        ThemeOwnerKind kind = ThemeOwnerKind.HarnessRunner) =>
        new(processId, ticks, context, kind, @"C:\harness\runner.exe");

    private static ThemeBindings Bindings() =>
        new(
            new ThemeBinding("sha256", ["TraceFold.Media.exe", "resources.pri"], Digest("product")),
            new ThemeBinding("sha256", ["TraceFold.Media.UiAcceptance.Support.dll"], Digest("toolkit")));

    private static ThemeApplyRequest ApplyRequest(
        FakeJournalStore journals, ThemeOwnerIdentity? owner = null, ThemeMode mode = ThemeMode.Dark) =>
        new(Key, Value, mode, journals.Path, owner ?? Owner(), Bindings());

    private static ThemeRestoreRequest Handback(
        FakeJournalStore journals, string? token, ThemeOwnerIdentity? owner = null) =>
        new(Key, Value, journals.Path, ThemeRestoreDisposition.Handback, owner ?? Owner(), token);

    private static ThemeRestoreRequest StartupRecovery(FakeJournalStore journals) =>
        new(Key, Value, journals.Path);

    private static ThemeApplyResult Apply(
        FakeJournalStore journals,
        FakeRegistry registry,
        ThemeOwnerIdentity? owner = null,
        ThemeMode mode = ThemeMode.Dark,
        FakeBroadcaster? broadcaster = null)
    {
        ThemeApplyResult result = WindowsAppThemeCoordinator.Apply(
            ApplyRequest(journals, owner, mode), registry, journals, broadcaster ?? new FakeBroadcaster());
        Assert.True(result.JournalWritten, "the fixture must journal the original state");
        return result;
    }

    [Fact]
    public void A_still_live_owner_hands_a_present_original_value_back_and_the_journal_goes_away()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        FakeBroadcaster broadcaster = new();
        FakeInspector inspector = FakeInspector.Live();
        ThemeApplyResult applied = Apply(journals, registry);

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            broadcaster,
            inspector);

        Assert.True(result.Restored);
        Assert.Equal(ThemeRejection.None, result.Rejection);
        Assert.Equal(ThemeRestoreAction.RestoredOriginal, result.Action);
        Assert.True(result.RegistryRestored);
        Assert.True(result.ReadBackVerified);
        Assert.True(result.BroadcastSucceeded);
        Assert.True(result.JournalRemoved);
        Assert.False(result.JournalRetained);
        Assert.Equal(ThemeOwnerState.Live, result.OwnerProbe!.State);
        ThemeRegistrySnapshot restored = registry.Snapshot(Key, Value);
        Assert.Equal(ThemeValueKind.DWord, restored.Kind);
        Assert.Equal(BitConverter.GetBytes(1), restored.Bytes);
        Assert.False(journals.Exists(journals.Path));
    }

    [Fact]
    public void A_still_live_owner_hands_an_absent_original_value_back_by_deleting_it()
    {
        FakeRegistry registry = new();
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry, mode: ThemeMode.Light);
        Assert.False(applied.Original!.Present);

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.True(result.Restored);
        Assert.Equal(ThemeRestoreAction.AbsentValueRemoved, result.Action);
        Assert.False(registry.Snapshot(Key, Value).Present);
        Assert.True(result.JournalRemoved);
    }

    [Fact]
    public void A_foreign_live_process_cannot_hand_back_someone_elses_journal()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);
        int registryCallsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken, Owner(processId: 9999)),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.HandbackOwnerMismatch, result.Rejection);
        Assert.Equal(registryCallsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
        Assert.Equal(BitConverter.GetBytes(0), registry.Snapshot(Key, Value).Bytes);
    }

    [Fact]
    public void A_support_cli_identity_never_obtains_the_hand_back()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);
        int registryCallsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken, Owner(kind: ThemeOwnerKind.SupportCli)),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.OwnerKindNotPersistentHarness, result.Rejection);
        Assert.Equal(registryCallsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void The_same_pid_with_a_different_start_time_is_not_the_same_owner()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken, Owner(ticks: OwnerTicks + 1)),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.HandbackOwnerMismatch, result.Rejection);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void The_same_pid_with_a_different_context_is_not_the_same_owner()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken, Owner(context: "another-run")),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.HandbackOwnerMismatch, result.Rejection);
        Assert.True(journals.Exists(journals.Path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_receipt_is_refused_even_for_the_true_owner(string? token)
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        _ = Apply(journals, registry);
        int registryCallsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, token),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.HandbackTokenMissing, result.Rejection);
        Assert.Equal(registryCallsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void A_receipt_from_a_different_apply_is_refused()
    {
        FakeRegistry registryA = new();
        registryA.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journalsA = new();
        ThemeApplyResult appliedA = Apply(journalsA, registryA);

        FakeRegistry registryB = new();
        registryB.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journalsB = new();
        ThemeApplyResult appliedB = Apply(journalsB, registryB);

        // The token of a later apply must not open an earlier journal, even for the same owner.
        ThemeRestoreResult crossed = WindowsAppThemeCoordinator.Restore(
            Handback(journalsA, appliedB.HandbackToken),
            registryA,
            journalsA,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(crossed.Restored);
        Assert.Equal(ThemeRejection.HandbackTokenMismatch, crossed.Rejection);
        Assert.True(journalsA.Exists(journalsA.Path));

        // The receipt is bound to the apply instance, not only to the token: the same token under
        // a different created instant yields a different digest.
        ThemeJournal journalA = ReadJournal(journalsA);
        Assert.NotEqual(
            journalA.HandbackTokenDigest,
            WindowsAppThemeCoordinator.ComputeHandbackDigest(
                appliedA.HandbackToken, "2026-01-01T00:00:00.0000000Z", Key, Value, Owner()));
        Assert.Equal(
            journalA.HandbackTokenDigest,
            WindowsAppThemeCoordinator.ComputeHandbackDigest(
                appliedA.HandbackToken, journalA.CreatedUtc, Key, Value, Owner()));
    }

    [Fact]
    public void The_journal_holds_only_the_digest_of_the_receipt()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);

        Assert.False(string.IsNullOrWhiteSpace(applied.HandbackToken));
        Assert.NotEmpty(applied.HandbackToken);
        string text = journals.Contents(journals.Path);
        Assert.DoesNotContain(applied.HandbackToken, text, StringComparison.Ordinal);
        ThemeJournal journal = ReadJournal(journals);
        Assert.False(string.IsNullOrWhiteSpace(journal.HandbackTokenDigest));
        Assert.Contains(journal.HandbackTokenDigest!, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dead_owner_is_refused_by_the_hand_back_and_recovered_by_the_start_up_path()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);
        int registryCallsBefore = registry.Calls.Count;

        ThemeRestoreResult handback = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());
        Assert.False(handback.Restored);
        Assert.Equal(ThemeRejection.HandbackOwnerNotLive, handback.Rejection);
        Assert.Equal(registryCallsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));

        FakeRegistry otherRegistry = new();
        otherRegistry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore otherJournals = new();
        _ = Apply(otherJournals, otherRegistry);

        ThemeRestoreResult startup = WindowsAppThemeCoordinator.Restore(
            StartupRecovery(otherJournals),
            otherRegistry,
            otherJournals,
            new FakeBroadcaster(),
            FakeInspector.Dead());
        Assert.True(startup.Restored);
        Assert.Equal(ThemeOwnerState.Dead, startup.OwnerProbe!.State);
        Assert.True(startup.JournalRemoved);
    }

    [Theory]
    [InlineData(ThemeOwnerState.PidReused, ThemeRejection.OwnerPidReused)]
    [InlineData(ThemeOwnerState.Unknown, ThemeRejection.OwnerUnknown)]
    public void Hand_back_refuses_a_reused_pid_or_an_unreadable_owner(
        ThemeOwnerState state, ThemeRejection expected)
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);
        int registryCallsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            new FakeBroadcaster(),
            new FakeInspector(new ThemeOwnerProbe(state, false, "fixture")));

        Assert.False(result.Restored);
        Assert.Equal(expected, result.Rejection);
        Assert.Equal(registryCallsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Start_up_recovery_still_refuses_to_take_over_from_a_live_owner()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        _ = Apply(journals, registry);
        int registryCallsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            StartupRecovery(journals), registry, journals, new FakeBroadcaster(), FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.OwnerLive, result.Rejection);
        Assert.Equal(registryCallsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void A_failed_restore_write_keeps_the_journal_even_with_a_valid_receipt()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);
        registry.FailWrite = true;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.RestoreWriteFailed, result.Rejection);
        Assert.True(result.JournalRetained);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void A_read_back_mismatch_keeps_the_journal_even_with_a_valid_receipt()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);
        registry.RestoreAsKind = ThemeValueKind.String;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.ReadBackMismatch, result.Rejection);
        Assert.True(result.RegistryRestored);
        Assert.True(result.JournalRetained);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void A_broadcast_failure_keeps_the_journal_even_with_a_valid_receipt()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            new FakeBroadcaster { Succeed = false },
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.BroadcastFailed, result.Rejection);
        Assert.True(result.ReadBackVerified);
        Assert.True(result.JournalRetained);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void A_second_hand_back_after_a_successful_one_reports_no_journal_and_no_action()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);
        _ = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());
        int registryCallsBefore = registry.Calls.Count;

        ThemeRestoreResult again = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(again.Restored);
        Assert.Equal(ThemeRestoreAction.NoJournalNoAction, again.Action);
        Assert.False(again.JournalPresent);
        Assert.False(again.ReadBackVerified);
        Assert.False(again.JournalRemoved);
        Assert.Equal(registryCallsBefore, registry.Calls.Count);
    }

    [Fact]
    public void Inspect_separates_start_up_recovery_from_the_hand_back_of_a_live_owner()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);
        int registryCallsBefore = registry.Calls.Count;

        ThemeJournalInspection declared = WindowsAppThemeCoordinator.Inspect(
            journals.Path, journals, FakeInspector.Live(), applied.HandbackToken, Owner());
        ThemeJournalInspection anonymous = WindowsAppThemeCoordinator.Inspect(
            journals.Path, journals, FakeInspector.Live());
        ThemeJournalInspection startup = WindowsAppThemeCoordinator.Inspect(
            journals.Path, journals, FakeInspector.Dead());

        Assert.False(declared.StartupRecoveryAllowed,
            "a live owner must never be reported as recoverable by the start-up path");
        Assert.Equal(ThemeRejection.OwnerLive, declared.Rejection);
        Assert.True(declared.HandbackAccepted);
        Assert.Equal(ThemeRejection.None, declared.HandbackRejection);

        Assert.False(anonymous.HandbackAccepted,
            "the journalled owner fields alone must not be reported as a valid hand-back");
        Assert.Equal(ThemeRejection.HandbackOwnerMissing, anonymous.HandbackRejection);

        Assert.True(startup.StartupRecoveryAllowed);
        Assert.False(startup.HandbackAccepted);
        Assert.Equal(registryCallsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void A_legacy_journal_without_a_receipt_can_be_recovered_but_never_handed_back()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        ThemeApplyResult applied = Apply(journals, registry);

        ThemeJournal modern = ReadJournal(journals);
        ThemeJournal legacy = modern with { SchemaVersion = 1, HandbackTokenDigest = null };
        legacy = legacy with { Checksum = WindowsAppThemeCoordinator.ComputeChecksum(legacy) };
        journals.Overwrite(journals.Path, WindowsAppThemeCoordinator.Serialize(legacy));

        ThemeRestoreResult handback = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());
        Assert.False(handback.Restored);
        Assert.Equal(ThemeRejection.HandbackTokenMismatch, handback.Rejection);
        Assert.True(journals.Exists(journals.Path));

        ThemeRestoreResult startup = WindowsAppThemeCoordinator.Restore(
            StartupRecovery(journals), registry, journals, new FakeBroadcaster(), FakeInspector.Dead());
        Assert.True(startup.Restored);
        Assert.Equal(ThemeRestoreAction.RestoredOriginal, startup.Action);
    }

    private static string Digest(string seed) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("handback-fixture:" + seed)));

    /// <summary>
    /// The defect this covers: the journal is already persisted (so the original state is owed
    /// back, and the write that reported failure may still have changed the value) but the caller
    /// also needs the receipt apply issued. Losing it closes both channels at once - the live
    /// owner cannot hand back and start-up recovery refuses to take over from a live owner.
    /// </summary>
    [Fact]
    public void A_failed_registry_write_keeps_the_journal_and_the_receipt_for_the_live_owner()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        registry.FailWrite = true;
        registry.PartialWriteBytes = BitConverter.GetBytes(0);
        FakeJournalStore journals = new();
        FakeBroadcaster broadcaster = new();

        ThemeApplyResult applied = WindowsAppThemeCoordinator.Apply(
            ApplyRequest(journals), registry, journals, broadcaster);

        Assert.False(applied.Applied);
        Assert.Equal(ThemeRejection.ApplyWriteFailed, applied.Rejection);
        Assert.True(applied.JournalWritten);
        Assert.True(applied.JournalRetained);
        Assert.False(applied.RegistryWritten);
        Assert.False(applied.AppliedReadBackVerified);
        Assert.True(journals.Exists(journals.Path));
        Assert.False(string.IsNullOrEmpty(applied.HandbackToken));

        ThemeRegistrySnapshot mutated = registry.Snapshot(Key, Value);
        Assert.Equal(BitConverter.GetBytes(0), mutated.Bytes);

        // The substitute modelled the apply-time write as failing; the give-back below is a later,
        // independent write and must not inherit that failure, otherwise the fixture - not the
        // component - would be the reason no restoration is possible.
        registry.FailWrite = false;
        registry.PartialWriteBytes = null;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            Handback(journals, applied.HandbackToken),
            registry,
            journals,
            broadcaster,
            FakeInspector.Live());

        Assert.True(result.Restored);
        Assert.Equal(ThemeRejection.None, result.Rejection);
        Assert.Equal(ThemeRestoreAction.RestoredOriginal, result.Action);
        Assert.True(result.JournalRemoved);
        Assert.False(result.JournalRetained);
        Assert.Equal(BitConverter.GetBytes(1), registry.Snapshot(Key, Value).Bytes);
    }

    /// <summary>
    /// The mirror of the case above: when the journal was never persisted there is nothing owed
    /// back, so no receipt may exist and a caller-supplied one must not buy eligibility.
    /// </summary>
    [Fact]
    public void A_journal_that_was_never_persisted_grants_no_hand_back_eligibility()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeJournalStore journals = new();
        journals.FailCreateNew = true;

        ThemeApplyResult applied = WindowsAppThemeCoordinator.Apply(
            ApplyRequest(journals), registry, journals, new FakeBroadcaster());

        Assert.False(applied.Applied);
        Assert.Equal(ThemeRejection.JournalWriteFailed, applied.Rejection);
        Assert.False(applied.JournalWritten);
        Assert.False(applied.JournalRetained);
        Assert.Equal(string.Empty, applied.HandbackToken);
        Assert.False(journals.Exists(journals.Path));

        ThemeRestoreResult forged = WindowsAppThemeCoordinator.Restore(
            Handback(journals, "forged-receipt-that-nobody-issued"),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(forged.Restored);
        Assert.Equal(ThemeRestoreAction.NoJournalNoAction, forged.Action);
        Assert.False(forged.JournalPresent);
        Assert.Equal(BitConverter.GetBytes(1), registry.Snapshot(Key, Value).Bytes);
    }

    /// <summary>
    /// After a failed apply the obligation stays open: start-up recovery may not take over from
    /// the still-live owner, and the journal is kept until someone entitled gives it back.
    /// </summary>
    [Fact]
    public void A_failed_apply_that_kept_the_journal_is_not_taken_over_by_start_up_recovery()
    {
        FakeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        registry.FailWrite = true;
        registry.PartialWriteBytes = BitConverter.GetBytes(0);
        FakeJournalStore journals = new();

        ThemeApplyResult applied = WindowsAppThemeCoordinator.Apply(
            ApplyRequest(journals), registry, journals, new FakeBroadcaster());
        Assert.Equal(ThemeRejection.ApplyWriteFailed, applied.Rejection);

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            StartupRecovery(journals),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.OwnerLive, result.Rejection);
        Assert.True(result.JournalPresent);
        Assert.True(result.JournalRetained);
        Assert.True(journals.Exists(journals.Path));
        Assert.Equal(BitConverter.GetBytes(0), registry.Snapshot(Key, Value).Bytes);
    }

    private static ThemeJournal ReadJournal(FakeJournalStore journals)
    {
        Assert.Equal(ThemeJournalReadStatus.Read, journals.TryRead(journals.Path, out string contents));
        return WindowsAppThemeCoordinator.TryParse(contents)
            ?? throw new InvalidOperationException("The journal could not be parsed.");
    }

    private sealed class FakeRegistry : IThemeRegistry
    {
        private readonly Dictionary<string, ThemeRegistrySnapshot> values =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> Calls { get; } = [];

        public bool FailWrite { get; set; }

        /// <summary>
        /// Models a write that reports failure after the value may already have been changed.
        /// The substitute still applies the bytes and then returns false, so a caller cannot treat
        /// "Write returned false" as "nothing happened". Left null, the substitute keeps the
        /// pre-existing behaviour (return false and change nothing).
        /// </summary>
        public byte[]? PartialWriteBytes { get; set; }

        public bool FailDelete { get; set; }

        public ThemeValueKind? RestoreAsKind { get; set; }

        public void Seed(string keyPath, string valueName, ThemeValueKind kind, byte[]? bytes) =>
            values[Slot(keyPath, valueName)] = new ThemeRegistrySnapshot(true, kind, bytes);

        public ThemeRegistrySnapshot Snapshot(string keyPath, string valueName) =>
            values.TryGetValue(Slot(keyPath, valueName), out ThemeRegistrySnapshot? snapshot)
                ? snapshot
                : new ThemeRegistrySnapshot(false, ThemeValueKind.Unknown, null);

        public ThemeRegistrySnapshot? Read(string keyPath, string valueName)
        {
            Calls.Add("read");
            return Snapshot(keyPath, valueName);
        }

        public bool Write(string keyPath, string valueName, ThemeValueKind kind, byte[] bytes)
        {
            Calls.Add("write");
            byte[]? partial = PartialWriteBytes;
            if (FailWrite)
            {
                if (partial is null)
                {
                    return false;
                }

                values[Slot(keyPath, valueName)] =
                    new ThemeRegistrySnapshot(true, RestoreAsKind ?? kind, partial);
                return false;
            }

            values[Slot(keyPath, valueName)] =
                new ThemeRegistrySnapshot(true, RestoreAsKind ?? kind, bytes);
            return true;
        }

        public bool Delete(string keyPath, string valueName)
        {
            Calls.Add("delete");
            if (FailDelete)
            {
                return false;
            }

            _ = values.Remove(Slot(keyPath, valueName));
            return true;
        }

        private static string Slot(string keyPath, string valueName) => keyPath + "|" + valueName;
    }

    private sealed class FakeJournalStore : IThemeJournalStore
    {
        private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

        public string Path { get; } = System.IO.Path.Combine("fixture", "handback-recovery.json");

        public bool Exists(string path) => files.ContainsKey(path);

        public ThemeJournalReadStatus TryRead(string path, out string contents)
        {
            contents = string.Empty;
            if (!files.TryGetValue(path, out string? stored))
            {
                return ThemeJournalReadStatus.Missing;
            }

            contents = stored;
            return ThemeJournalReadStatus.Read;
        }

        public bool FailCreateNew { get; set; }

        public bool TryCreateNew(string path, string contents)
        {
            if (FailCreateNew || files.ContainsKey(path))
            {
                return false;
            }

            files[path] = contents;
            return true;
        }

        public bool TryDelete(string path) => files.Remove(path);

        public string Contents(string path) => files[path];

        public void Overwrite(string path, string contents) => files[path] = contents;
    }

    private sealed class FakeBroadcaster : IThemeChangeBroadcaster
    {
        public bool Succeed { get; set; } = true;

        public int Calls { get; private set; }

        public bool Broadcast()
        {
            Calls++;
            return Succeed;
        }
    }

    private sealed class FakeInspector : IThemeOwnerInspector
    {
        private readonly ThemeOwnerProbe result;

        public FakeInspector(ThemeOwnerProbe result) => this.result = result;

        public int Calls { get; private set; }

        public static FakeInspector Live() =>
            new(new ThemeOwnerProbe(ThemeOwnerState.Live, true, "handback-live"));

        public static FakeInspector Dead() =>
            new(new ThemeOwnerProbe(ThemeOwnerState.Dead, true, "handback-dead"));

        public ThemeOwnerProbe Probe(ThemeOwnerIdentity owner)
        {
            Calls++;
            return result;
        }
    }
}
