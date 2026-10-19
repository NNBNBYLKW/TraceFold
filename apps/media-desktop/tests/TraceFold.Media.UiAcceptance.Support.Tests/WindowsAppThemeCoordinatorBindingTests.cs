using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

/// <summary>
/// The restore decides the caller's expected binding against the journal it is about to consume,
/// before the registry, the broadcast or the journal are touched. These tests use the component's
/// own fakes; they never construct a production adapter.
/// </summary>
public sealed class WindowsAppThemeCoordinatorBindingTests
{
    private const string Key = WindowsAppThemeCoordinator.ApprovedKeyPath;
    private const string Value = WindowsAppThemeCoordinator.ApprovedValueName;

    [Fact]
    public void A_wrong_binding_is_refused_before_the_write_broadcast_or_delete()
    {
        Journals journals = new();
        Calls calls = new();
        ThemeApplyResult applied = WindowsAppThemeCoordinator.Apply(
            ApplyRequest(journals, Bindings("A")), calls.Registry, journals, calls.Broadcaster);
        Assert.True(applied.Applied);
        int writesAfterApply = calls.Registry.Writes;
        int broadcastsAfterApply = calls.Broadcaster.Count;
        string? journalAfterApply = journals.Text;
        Assert.NotNull(journalAfterApply);

        ThemeRestoreResult restored = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(
                Key, Value, journals.Path, ThemeRestoreDisposition.Handback,
                Owner(), applied.HandbackToken, Bindings("B")),
            calls.Registry, journals, calls.Broadcaster, Inspector.Live());

        Assert.Equal(ThemeRejection.BindingsMismatch, restored.Rejection);
        Assert.False(restored.Restored);
        Assert.False(restored.RegistryRestored);
        Assert.False(restored.BroadcastSucceeded);
        Assert.False(restored.JournalRemoved);
        // Every side effect is exactly what the apply left behind.
        Assert.Equal(writesAfterApply, calls.Registry.Writes);
        Assert.Equal(broadcastsAfterApply, calls.Broadcaster.Count);
        Assert.Equal(journalAfterApply, journals.Text);
        Assert.NotNull(restored.Bindings);
        Assert.True(WindowsAppThemeCoordinator.BindingsEqual(Bindings("A"), restored.Bindings!));
    }

    [Fact]
    public void The_matching_binding_restores_normally()
    {
        Journals journals = new();
        Calls calls = new();
        ThemeApplyResult applied = WindowsAppThemeCoordinator.Apply(
            ApplyRequest(journals, Bindings("A")), calls.Registry, journals, calls.Broadcaster);

        ThemeRestoreResult restored = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(
                Key, Value, journals.Path, ThemeRestoreDisposition.Handback,
                Owner(), applied.HandbackToken, Bindings("A")),
            calls.Registry, journals, calls.Broadcaster, Inspector.Live());

        Assert.Equal(ThemeRejection.None, restored.Rejection);
        Assert.True(restored.Restored);
        Assert.True(restored.JournalRemoved);
        Assert.Null(journals.Text);
        Assert.Equal(1, calls.Registry.Value);
    }

    [Fact]
    public void Startup_recovery_is_not_bound_to_the_current_build_binding()
    {
        Journals journals = new();
        Calls calls = new();
        _ = WindowsAppThemeCoordinator.Apply(
            ApplyRequest(journals, Bindings("A")), calls.Registry, journals, calls.Broadcaster);

        // A dead owner's journal is recovered without adopting this build's binding: the expected
        // binding is null and the journal's own triple is what gets restored.
        ThemeRestoreResult restored = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(
                Key, Value, journals.Path, ThemeRestoreDisposition.StartupRecovery),
            calls.Registry, journals, calls.Broadcaster, Inspector.Dead());

        Assert.Equal(ThemeRejection.None, restored.Rejection);
        Assert.True(restored.Restored);
        Assert.NotNull(restored.Bindings);
        Assert.True(WindowsAppThemeCoordinator.BindingsEqual(Bindings("A"), restored.Bindings!));
    }

    private static ThemeBindings Bindings(string label) =>
        new(new ThemeBinding("sha256", ["TraceFold.Media.exe"], Digest(label)),
            new ThemeBinding("sha256", ["visual-gate", "support"], Digest(label + "T")));

    private static string Digest(string label)
    {
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(label));
        return Convert.ToHexString(bytes);
    }

    private static ThemeOwnerIdentity Owner() =>
        new(4242, 638000000000000000, "ctx-binding", ThemeOwnerKind.HarnessRunner);

    private static ThemeApplyRequest ApplyRequest(Journals journals, ThemeBindings bindings) =>
        new(Key, Value, ThemeMode.Dark, journals.Path, Owner(), bindings);

    private sealed class Calls
    {
        public FakeRegistry Registry { get; } = new();

        public FakeBroadcaster Broadcaster { get; } = new();
    }

    private sealed class FakeRegistry : IThemeRegistry
    {
        public int Writes { get; private set; }

        public int Value { get; private set; } = 1;

        public ThemeRegistrySnapshot? Read(string keyPath, string valueName) =>
            new(true, ThemeValueKind.DWord, BitConverter.GetBytes(Value));

        public bool Write(string keyPath, string valueName, ThemeValueKind kind, byte[] bytes)
        {
            Writes++;
            Value = BitConverter.ToInt32(bytes);
            return true;
        }

        public bool Delete(string keyPath, string valueName)
        {
            Writes++;
            Value = 0;
            return true;
        }
    }

    private sealed class FakeBroadcaster : IThemeChangeBroadcaster
    {
        public int Count { get; private set; }

        public bool Broadcast()
        {
            Count++;
            return true;
        }
    }

    private sealed class Journals : IThemeJournalStore
    {
        public string? Text { get; private set; }

        public string Path { get; } = @"C:\temp\theme-journal-binding-test.json";

        public bool Exists(string path) => Text is not null;

        public ThemeJournalReadStatus TryRead(string path, out string contents)
        {
            contents = Text ?? string.Empty;
            return Text is null ? ThemeJournalReadStatus.Missing : ThemeJournalReadStatus.Read;
        }

        public bool TryCreateNew(string path, string contents)
        {
            if (Text is not null)
            {
                return false;
            }

            Text = contents;
            return true;
        }

        public bool TryDelete(string path)
        {
            Text = null;
            return true;
        }
    }

    private sealed class Inspector : IThemeOwnerInspector
    {
        private readonly ThemeOwnerState state;

        private Inspector(ThemeOwnerState state) => this.state = state;

        public static Inspector Live() => new(ThemeOwnerState.Live);

        public static Inspector Dead() => new(ThemeOwnerState.Dead);

        public ThemeOwnerProbe Probe(ThemeOwnerIdentity owner) => new(state, true, "test");
    }
}
