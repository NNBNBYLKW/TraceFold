using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

/// <summary>
/// Injected unit coverage for the temporary Windows app-theme switch. Every registry, broadcast,
/// owner and file-system dependency is a declared substitute, so no test reads or writes the real
/// registry, produces a real journal, broadcasts a setting change, or starts a product/driver/UI
/// process. The two guard tests at the end prove that the default Windows implementations were
/// neither invoked nor even referenced outside the guard itself.
/// </summary>
public sealed class WindowsAppThemeCoordinatorTests
{
    private const string Key = WindowsAppThemeCoordinator.ApprovedKeyPath;

    private const string Value = WindowsAppThemeCoordinator.ApprovedValueName;

    private const long OwnerTicks = 639276814407904666L;

    [Fact]
    public void Apply_writes_the_journal_before_it_touches_the_registry()
    {
        CallLog log = new();
        FakeThemeRegistry registry = new(log);
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new(log);
        FakeBroadcaster broadcaster = new();
        FakeInspector inspector = new();

        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, broadcaster);

        int journalWrite = log.Entries.IndexOf("journal:create-new");
        int registryWrite = log.Entries.IndexOf("registry:write");
        Assert.True(journalWrite >= 0, "the journal must be written");
        Assert.True(registryWrite >= 0, "the registry must be written");
        Assert.True(journalWrite < registryWrite,
            "the recovery journal must exist before the registry is changed");
        Assert.Equal(0, inspector.Calls);
    }

    [Theory]
    [InlineData(ThemeMode.Light, 1)]
    [InlineData(ThemeMode.Dark, 0)]
    public void Apply_writes_the_approved_dword_for_the_requested_mode(ThemeMode mode, int expected)
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        FakeBroadcaster broadcaster = new();

        ThemeApplyResult result = WindowsAppThemeCoordinator.Apply(
            Request(mode, journals, registry), registry, journals, broadcaster);

        Assert.True(result.Applied);
        Assert.Equal(ThemeRejection.None, result.Rejection);
        Assert.True(result.RegistryWritten);
        Assert.True(result.AppliedReadBackVerified);
        Assert.True(result.BroadcastSucceeded);
        Assert.True(result.JournalRetained);
        Assert.Equal(1, broadcaster.Calls);
        ThemeRegistrySnapshot applied = registry.Snapshot(Key, Value);
        Assert.True(applied.Present);
        Assert.Equal(ThemeValueKind.DWord, applied.Kind);
        Assert.Equal(BitConverter.GetBytes(expected), applied.Bytes);
    }

    [Fact]
    public void Apply_journals_a_present_original_value_with_its_kind_and_bytes()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();

        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());

        ThemeJournal journal = ReadJournal(journals);
        Assert.True(journal.Original.Present);
        Assert.Equal(ThemeValueKind.DWord, journal.Original.Kind);
        Assert.Equal(BitConverter.GetBytes(1), journal.Original.Bytes);
        Assert.Equal("Dark", journal.RequestedMode);
        Assert.Equal(Key, journal.KeyPath);
        Assert.Equal(Value, journal.ValueName);
        Assert.Equal(OwnerTicks, journal.Owner.StartUtcTicks);
        Assert.Equal(ThemeOwnerKind.HarnessRunner, journal.Owner.Kind);
        Assert.Equal("sha256", journal.Bindings.Product.HashKind);
        Assert.NotEmpty(journal.Bindings.Product.Members);
        Assert.Equal("sha256", journal.Bindings.Toolkit.HashKind);
        Assert.NotEmpty(journal.Bindings.Toolkit.Members);
    }

    [Fact]
    public void Apply_journals_an_absent_original_value_and_restores_it_by_deleting_the_value()
    {
        FakeThemeRegistry registry = new();
        FakeThemeJournalStore journals = new();

        ThemeApplyResult applied = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Light, journals, registry), registry, journals, new FakeBroadcaster());
        Assert.True(applied.Applied);
        Assert.NotNull(applied.Original);
        Assert.False(applied.Original!.Present);

        ThemeRestoreResult restored = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.True(restored.Restored);
        Assert.Equal(ThemeRestoreAction.AbsentValueRemoved, restored.Action);
        Assert.True(restored.JournalRemoved);
        Assert.False(registry.Snapshot(Key, Value).Present);
    }

    [Theory]
    [InlineData("SystemUsesLightTheme")]
    [InlineData("AppsUseLightTheme2")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Color")]
    [InlineData(@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")]
    public void Apply_and_restore_refuse_every_unapproved_target(string target)
    {
        FakeThemeRegistry registry = new();
        FakeThemeJournalStore journals = new();
        FakeBroadcaster broadcaster = new();

        string key = target.StartsWith("HKEY_", StringComparison.Ordinal)
            ? target
            : Key;
        string value = target.StartsWith("HKEY_", StringComparison.Ordinal) ? Value : target;

        ThemeApplyResult applied = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry, key: key, value: value),
            registry,
            journals,
            broadcaster);
        ThemeRestoreResult restored = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(key, value, journals.Path),
            registry,
            journals,
            broadcaster,
            FakeInspector.Dead());

        Assert.Equal(ThemeRejection.TargetNotApproved, applied.Rejection);
        Assert.False(applied.Applied);
        Assert.Equal(ThemeRejection.TargetNotApproved, restored.Rejection);
        Assert.False(restored.Restored);
        Assert.Empty(registry.Calls);
        Assert.Empty(journals.Calls);
        Assert.Equal(0, broadcaster.Calls);
        Assert.Equal("theme.target_not_approved",
            WindowsAppThemeCoordinator.ReasonCode(applied.Rejection));
    }

    [Fact]
    public void Apply_refuses_a_second_change_while_a_journal_already_exists()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();

        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        int writesBeforeSecond = registry.Calls.Count(call => call == "write");

        ThemeApplyResult second = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Light, journals, registry), registry, journals, new FakeBroadcaster());

        Assert.Equal(ThemeRejection.JournalAlreadyPresent, second.Rejection);
        Assert.False(second.Applied);
        Assert.False(second.RegistryWritten);
        Assert.Equal(writesBeforeSecond, registry.Calls.Count(call => call == "write"));
        Assert.True(journals.Exists(journals.Path), "the first journal must survive the refusal");
    }

    [Fact]
    public void Apply_failure_keeps_the_journal_and_leaves_the_original_value_readable()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        registry.FailWrite = true;
        FakeThemeJournalStore journals = new();

        ThemeApplyResult result = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());

        Assert.False(result.Applied);
        Assert.Equal(ThemeRejection.ApplyWriteFailed, result.Rejection);
        Assert.True(result.JournalWritten);
        Assert.True(result.JournalRetained);
        Assert.True(journals.Exists(journals.Path));
        ThemeJournal journal = ReadJournal(journals);
        Assert.True(journal.Original.Present);
        Assert.Equal(BitConverter.GetBytes(1), journal.Original.Bytes);
    }

    [Fact]
    public void Apply_broadcast_failure_keeps_the_journal()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();

        ThemeApplyResult result = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry),
            registry,
            journals,
            new FakeBroadcaster { Succeed = false });

        Assert.False(result.Applied);
        Assert.Equal(ThemeRejection.BroadcastFailed, result.Rejection);
        Assert.True(result.RegistryWritten);
        Assert.True(result.AppliedReadBackVerified);
        Assert.False(result.BroadcastSucceeded);
        Assert.True(result.JournalRetained);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Apply_refuses_a_support_cli_child_as_the_persistent_owner()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();

        ThemeApplyResult result = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry, kind: ThemeOwnerKind.SupportCli),
            registry,
            journals,
            new FakeBroadcaster());

        Assert.Equal(ThemeRejection.OwnerKindNotPersistentHarness, result.Rejection);
        Assert.False(result.Applied);
        Assert.Empty(registry.Calls);
        Assert.Empty(journals.Calls);
    }

    [Theory]
    [InlineData(0, 1, "harness", ThemeRejection.OwnerMissingProcessId)]
    [InlineData(1, 0, "harness", ThemeRejection.OwnerMissingStartTime)]
    [InlineData(1, 1, "", ThemeRejection.OwnerMissingContext)]
    public void Apply_refuses_an_owner_identity_that_is_not_complete(
        int processId, long ticks, string context, ThemeRejection expected)
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();

        ThemeApplyResult result = WindowsAppThemeCoordinator.Apply(
            Request(
                ThemeMode.Dark,
                journals,
                registry,
                owner: new ThemeOwnerIdentity(processId, ticks, context, ThemeOwnerKind.HarnessRunner)),
            registry,
            journals,
            new FakeBroadcaster());

        Assert.Equal(expected, result.Rejection);
        Assert.False(result.Applied);
        Assert.Empty(registry.Calls);
    }

    [Fact]
    public void Apply_refuses_bindings_that_do_not_name_their_hash_kind_and_members()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();

        ThemeApplyResult missing = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry, omitBindings: true),
            registry,
            journals,
            new FakeBroadcaster());
        ThemeApplyResult shortDigest = WindowsAppThemeCoordinator.Apply(
            Request(
                ThemeMode.Dark,
                journals,
                registry,
                bindings: new ThemeBindings(
                    new ThemeBinding("sha256", ["product.exe"], "abc"),
                    Fixtures.ToolkitBinding())),
            registry,
            journals,
            new FakeBroadcaster());
        ThemeApplyResult noMembers = WindowsAppThemeCoordinator.Apply(
            Request(
                ThemeMode.Dark,
                journals,
                registry,
                bindings: new ThemeBindings(
                    new ThemeBinding("sha256", [], Fixtures.Digest("product")),
                    Fixtures.ToolkitBinding())),
            registry,
            journals,
            new FakeBroadcaster());

        Assert.Equal(ThemeRejection.BindingsMissing, missing.Rejection);
        Assert.Equal(ThemeRejection.BindingsIncomplete, shortDigest.Rejection);
        Assert.Equal(ThemeRejection.BindingsIncomplete, noMembers.Rejection);
        Assert.Empty(registry.Calls);
        Assert.Empty(journals.Calls);
    }

    [Fact]
    public void Restore_restores_a_present_dword_byte_for_byte_and_then_removes_the_journal()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        FakeBroadcaster broadcaster = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, broadcaster);

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            broadcaster,
            FakeInspector.Dead());

        Assert.True(result.Restored);
        Assert.Equal(ThemeRejection.None, result.Rejection);
        Assert.Equal(ThemeRestoreAction.RestoredOriginal, result.Action);
        Assert.True(result.RegistryRestored);
        Assert.True(result.ReadBackVerified);
        Assert.True(result.BroadcastSucceeded);
        Assert.True(result.JournalRemoved);
        Assert.False(result.JournalRetained);
        Assert.False(journals.Exists(journals.Path));
        ThemeRegistrySnapshot restored = registry.Snapshot(Key, Value);
        Assert.True(restored.Present);
        Assert.Equal(ThemeValueKind.DWord, restored.Kind);
        Assert.Equal(BitConverter.GetBytes(1), restored.Bytes);
    }

    [Fact]
    public void Restore_preserves_a_non_dword_original_kind_and_its_exact_bytes()
    {
        byte[] original = Encoding.Unicode.GetBytes("theme-fixture");
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.String, original);
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.True(result.Restored);
        ThemeRegistrySnapshot restored = registry.Snapshot(Key, Value);
        Assert.Equal(ThemeValueKind.String, restored.Kind);
        Assert.Equal(original, restored.Bytes);
    }

    [Fact]
    public void Restore_reports_a_different_registry_type_after_the_write_as_a_read_back_mismatch()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        registry.RestoreAsKind = ThemeValueKind.String;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.ReadBackMismatch, result.Rejection);
        Assert.True(result.RegistryRestored);
        Assert.False(result.ReadBackVerified);
        Assert.False(result.JournalRemoved);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Restore_without_a_journal_is_not_the_same_fact_as_a_measured_restoration()
    {
        FakeThemeRegistry registry = new();
        FakeThemeJournalStore journals = new();

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.None, result.Rejection);
        Assert.Equal(ThemeRestoreAction.NoJournalNoAction, result.Action);
        Assert.False(result.JournalPresent);
        Assert.False(result.ReadBackVerified);
        Assert.False(result.JournalRemoved);
        Assert.Empty(registry.Calls);
    }

    [Fact]
    public void Repeated_restore_after_a_successful_one_reports_no_journal_and_no_action()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        FakeBroadcaster broadcaster = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, broadcaster);
        _ = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            broadcaster,
            FakeInspector.Dead());

        int callsBeforeRepeat = registry.Calls.Count;
        ThemeRestoreResult again = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            broadcaster,
            FakeInspector.Dead());

        Assert.False(again.Restored);
        Assert.Equal(ThemeRestoreAction.NoJournalNoAction, again.Action);
        Assert.False(again.JournalPresent);
        Assert.False(again.ReadBackVerified);
        Assert.False(again.JournalRemoved);
        Assert.Equal(callsBeforeRepeat, registry.Calls.Count);
    }

    [Fact]
    public void Restore_keeps_a_corrupt_journal_and_never_touches_the_registry()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        string good = journals.Contents(journals.Path);
        journals.Overwrite(journals.Path, good.Replace("\"checksum\":", "\"checksumX\":"));
        int callsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.JournalCorrupt, result.Rejection);
        Assert.True(result.JournalPresent);
        Assert.False(result.JournalRemoved);
        Assert.Equal(callsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Restore_checksum_covers_the_owner_and_the_bindings()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        string good = journals.Contents(journals.Path);

        journals.Overwrite(journals.Path, good.Replace("\"processId\":1000", "\"processId\":1001"));
        ThemeRestoreResult ownerTampered = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        journals.Overwrite(journals.Path, good.Replace(
            Fixtures.ToolkitBinding().Digest,
            Fixtures.Digest("other")));
        ThemeRestoreResult bindingTampered = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.Equal(ThemeRejection.JournalCorrupt, ownerTampered.Rejection);
        Assert.Equal(ThemeRejection.JournalCorrupt, bindingTampered.Rejection);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Restore_keeps_a_journal_that_cannot_be_read()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        journals.FailRead = true;
        int callsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.JournalUnreadable, result.Rejection);
        Assert.Equal(callsBefore, registry.Calls.Count);
    }

    [Fact]
    public void Restore_refuses_to_take_over_from_a_live_owner()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        int callsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Live());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.OwnerLive, result.Rejection);
        Assert.Equal(ThemeOwnerState.Live, result.OwnerProbe!.State);
        Assert.Equal(callsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Restore_recovers_after_a_simulated_dead_owner_whose_identity_is_readable()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.True(result.Restored);
        Assert.Equal(ThemeOwnerState.Dead, result.OwnerProbe!.State);
        Assert.True(result.OwnerProbe.IdentityReadable);
        Assert.True(result.JournalRemoved);
    }

    [Theory]
    [InlineData(ThemeOwnerState.PidReused, ThemeRejection.OwnerPidReused)]
    [InlineData(ThemeOwnerState.Unknown, ThemeRejection.OwnerUnknown)]
    public void Restore_refuses_a_reused_pid_or_an_unreadable_owner(
        ThemeOwnerState state, ThemeRejection expected)
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        int callsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            new FakeInspector(new ThemeOwnerProbe(state, false, "fixture")));

        Assert.False(result.Restored);
        Assert.Equal(expected, result.Rejection);
        Assert.True(result.JournalPresent);
        Assert.False(result.JournalRemoved);
        Assert.Equal(callsBefore, registry.Calls.Count);
    }

    [Fact]
    public void Restore_refuses_a_dead_owner_whose_identity_still_cannot_be_read()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        int callsBefore = registry.Calls.Count;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            new FakeInspector(new ThemeOwnerProbe(ThemeOwnerState.Dead, false, "unreadable")));

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.OwnerIdentityUnreadable, result.Rejection);
        Assert.Equal(callsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Restore_write_failure_keeps_the_journal()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        registry.FailWrite = true;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.RestoreWriteFailed, result.Rejection);
        Assert.False(result.RegistryRestored);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Restore_delete_failure_for_an_absent_original_keeps_the_journal()
    {
        FakeThemeRegistry registry = new();
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Light, journals, registry), registry, journals, new FakeBroadcaster());
        registry.FailDelete = true;

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster(),
            FakeInspector.Dead());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.RestoreDeleteFailed, result.Rejection);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Restore_broadcast_failure_keeps_the_journal_even_though_the_value_matches()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());

        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(Key, Value, journals.Path),
            registry,
            journals,
            new FakeBroadcaster { Succeed = false },
            FakeInspector.Dead());

        Assert.False(result.Restored);
        Assert.Equal(ThemeRejection.BroadcastFailed, result.Rejection);
        Assert.True(result.RegistryRestored);
        Assert.True(result.ReadBackVerified);
        Assert.Equal(BitConverter.GetBytes(1), registry.Snapshot(Key, Value).Bytes);
        Assert.True(result.JournalRetained);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void Inspect_reports_whether_a_journal_may_be_restored_without_changing_anything()
    {
        FakeThemeRegistry registry = new();
        registry.Seed(Key, Value, ThemeValueKind.DWord, BitConverter.GetBytes(1));
        FakeThemeJournalStore journals = new();
        _ = WindowsAppThemeCoordinator.Apply(
            Request(ThemeMode.Dark, journals, registry), registry, journals, new FakeBroadcaster());
        int callsBefore = registry.Calls.Count;

        ThemeJournalInspection live = WindowsAppThemeCoordinator.Inspect(
            journals.Path, journals, FakeInspector.Live());
        ThemeJournalInspection dead = WindowsAppThemeCoordinator.Inspect(
            journals.Path, journals, FakeInspector.Dead());

        Assert.True(live.JournalPresent);
        Assert.True(live.JournalChecksumValid);
        Assert.False(live.StartupRecoveryAllowed);
        Assert.False(live.HandbackAccepted);
        Assert.Equal(ThemeRejection.OwnerLive, live.Rejection);
        Assert.True(dead.StartupRecoveryAllowed);
        Assert.Equal(ThemeRejection.None, dead.Rejection);
        Assert.False(dead.HandbackAccepted,
            "recovering a dead owner's journal must not be reported as a live hand-back");
        Assert.Equal(ThemeRejection.HandbackOwnerMissing, dead.HandbackRejection);
        Assert.Equal(callsBefore, registry.Calls.Count);
        Assert.True(journals.Exists(journals.Path));
    }

    [Fact]
    public void The_default_windows_implementations_were_never_invoked()
    {
        Assert.Equal(0, SystemThemeJournalStore.InvocationCount);
        Assert.Equal(0, WindowsThemeRegistry.InvocationCount);
        Assert.Equal(0, WindowsThemeChangeBroadcaster.InvocationCount);
        Assert.Equal(0, WindowsThemeOwnerInspector.InvocationCount);
    }

    [Fact]
    public void Test_sources_reference_no_production_adapter_outside_this_guard_test()
    {
        string[] adapters =
        [
            "WindowsThemeRegistry",
            "WindowsThemeChangeBroadcaster",
            "WindowsThemeOwnerInspector",
            "SystemThemeJournalStore",
        ];

        foreach (string file in Directory.GetFiles(Fixtures.TestDirectory, "*.cs"))
        {
            if (string.Equals(
                    Path.GetFileName(file),
                    "WindowsAppThemeCoordinatorTests.cs",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (string adapter in adapters)
            {
                Assert.DoesNotContain(adapter, text, StringComparison.Ordinal);
            }
        }
    }

    private static ThemeApplyRequest Request(
        ThemeMode mode,
        FakeThemeJournalStore journals,
        FakeThemeRegistry registry,
        string key = Key,
        string value = Value,
        ThemeOwnerIdentity? owner = null,
        ThemeOwnerKind kind = ThemeOwnerKind.HarnessRunner,
        ThemeBindings? bindings = null,
        bool omitBindings = false)
    {
        _ = registry;
        return new ThemeApplyRequest(
            key,
            value,
            mode,
            journals.Path,
            owner ?? new ThemeOwnerIdentity(1000, OwnerTicks, "harness-runner", kind,
                @"C:\harness\runner.exe"),
            omitBindings
                ? null!
                : bindings ?? new ThemeBindings(
                    Fixtures.ProductBinding(), Fixtures.ToolkitBinding()));
    }

    /// <summary>One ordered history across the registry and the journal store, so ordering can be asserted between them.</summary>
    private sealed class CallLog
    {
        public List<string> Entries { get; } = [];
    }

    private static ThemeJournal ReadJournal(FakeThemeJournalStore journals)
    {
        Assert.Equal(ThemeJournalReadStatus.Read, journals.TryRead(journals.Path, out string contents));
        return WindowsAppThemeCoordinator.TryParse(contents)
            ?? throw new InvalidOperationException("The journal could not be parsed.");
    }

    private static class Fixtures
    {
        public static string TestDirectory =>
            FindDirectory("tests/TraceFold.Media.UiAcceptance.Support.Tests");

        public static string Digest(string seed) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture:" + seed)));

        public static ThemeBinding ProductBinding() =>
            new("sha256", ["TraceFold.Media.exe", "resources.pri"], Digest("product"));

        public static ThemeBinding ToolkitBinding() =>
            new("sha256", ["TraceFold.Media.UiAcceptance.Support.dll"], Digest("toolkit-members"));

        private static string FindDirectory(string relative)
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null)
            {
                string candidate = Path.Combine(
                    current.FullName,
                    "apps",
                    "media-desktop",
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Support test directory is missing: " + relative);
        }
    }

    private sealed class FakeThemeRegistry : IThemeRegistry
    {
        private readonly Dictionary<string, ThemeRegistrySnapshot> values =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly CallLog? log;

        public FakeThemeRegistry(CallLog? log = null) => this.log = log;

        public List<string> Calls { get; } = [];

        public bool FailWrite { get; set; }

        public bool FailDelete { get; set; }

        public bool FailRead { get; set; }

        public ThemeValueKind? RestoreAsKind { get; set; }

        public void Seed(string keyPath, string valueName, ThemeValueKind kind, byte[]? bytes)
        {
            values[Slot(keyPath, valueName)] = new ThemeRegistrySnapshot(true, kind, bytes);
        }

        public ThemeRegistrySnapshot Snapshot(string keyPath, string valueName) =>
            values.TryGetValue(Slot(keyPath, valueName), out ThemeRegistrySnapshot? snapshot)
                ? snapshot
                : new ThemeRegistrySnapshot(false, ThemeValueKind.Unknown, null);

        public ThemeRegistrySnapshot? Read(string keyPath, string valueName)
        {
            Calls.Add("read");
            log?.Entries.Add("registry:read");
            return FailRead ? null : Snapshot(keyPath, valueName);
        }

        public bool Write(string keyPath, string valueName, ThemeValueKind kind, byte[] bytes)
        {
            Calls.Add("write");
            log?.Entries.Add("registry:write");
            if (FailWrite)
            {
                return false;
            }

            ThemeValueKind effective = RestoreAsKind ?? kind;
            values[Slot(keyPath, valueName)] = new ThemeRegistrySnapshot(true, effective, bytes);
            return true;
        }

        public bool Delete(string keyPath, string valueName)
        {
            Calls.Add("delete");
            log?.Entries.Add("registry:delete");
            if (FailDelete)
            {
                return false;
            }

            _ = values.Remove(Slot(keyPath, valueName));
            return true;
        }

        private static string Slot(string keyPath, string valueName) => keyPath + "|" + valueName;
    }

    private sealed class FakeThemeJournalStore : IThemeJournalStore
    {
        private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

        private readonly CallLog? log;

        public FakeThemeJournalStore(CallLog? log = null) => this.log = log;

        public List<string> Calls { get; } = [];

        public bool FailCreateNew { get; set; }

        public bool FailDelete { get; set; }

        public bool FailRead { get; set; }

        public string Path { get; } = System.IO.Path.Combine("fixture", "theme-recovery.json");

        public bool Exists(string path)
        {
            Calls.Add("exists");
            log?.Entries.Add("journal:exists");
            return files.ContainsKey(path);
        }

        public ThemeJournalReadStatus TryRead(string path, out string contents)
        {
            Calls.Add("read");
            log?.Entries.Add("journal:read");
            contents = string.Empty;
            if (FailRead)
            {
                return files.ContainsKey(path)
                    ? ThemeJournalReadStatus.Unreadable
                    : ThemeJournalReadStatus.Missing;
            }

            if (!files.TryGetValue(path, out string? stored))
            {
                return ThemeJournalReadStatus.Missing;
            }

            contents = stored;
            return ThemeJournalReadStatus.Read;
        }

        public bool TryCreateNew(string path, string contents)
        {
            Calls.Add("create-new");
            log?.Entries.Add("journal:create-new");
            if (FailCreateNew || files.ContainsKey(path))
            {
                return false;
            }

            files[path] = contents;
            return true;
        }

        public bool TryDelete(string path)
        {
            Calls.Add("delete");
            log?.Entries.Add("journal:delete");
            return !FailDelete && files.Remove(path);
        }

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

        public FakeInspector()
            : this(new ThemeOwnerProbe(ThemeOwnerState.Dead, true, "fixture-dead"))
        {
        }

        public FakeInspector(ThemeOwnerProbe result) => this.result = result;

        public int Calls { get; private set; }

        public static FakeInspector Live() =>
            new(new ThemeOwnerProbe(ThemeOwnerState.Live, true, "fixture-live"));

        public static FakeInspector Dead() =>
            new(new ThemeOwnerProbe(ThemeOwnerState.Dead, true, "fixture-dead"));

        public ThemeOwnerProbe Probe(ThemeOwnerIdentity owner)
        {
            Calls++;
            return result;
        }
    }
}
