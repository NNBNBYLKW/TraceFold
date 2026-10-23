using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

/// <summary>
/// Exercises the theme command through the published executable: real argv, real standard input,
/// the real argument parser, the real coordinator and the acceptance-only fixture adapters. No
/// registry, broadcast or process probe is real: the three fixture files stand in for them, and
/// the journal is a real file under a test-owned root.
/// </summary>
/// <remarks>
/// The command runs as a child process on purpose. In-process calls would use the production
/// journal store inside the test host, which the coordinator's own guard test forbids and which
/// would also hide the argv and stdin contract this file is about.
/// </remarks>
public sealed class ThemeProgramTests
{
    private const string OwnerPid = "4242";
    private const string OwnerTicks = "638000000000000000";
    private const string OwnerContext = "ctx-theme-1";
    private const string ProductSha = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ToolkitSha = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string OtherSha = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    [Fact]
    public void Apply_without_an_owner_is_an_argument_error_with_a_json_body()
    {
        using var fixture = new FixtureRoot();
        var run = RunSupport(
            ["theme", "apply", "--journal", fixture.JournalPath, "--mode", "light",
             "--fixture-root", fixture.Path], licensed: true);

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
        using JsonDocument body = JsonDocument.Parse(run.Stdout);
        Assert.True(body.RootElement.TryGetProperty("error", out _));
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public void Owner_exe_alone_is_not_a_complete_owner()
    {
        using var fixture = new FixtureRoot();
        var run = RunSupport(
            ["theme", "apply", "--journal", fixture.JournalPath, "--mode", "light",
             "--owner-exe", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
             "--fixture-root", fixture.Path], licensed: true);

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public void Member_names_must_describe_the_declared_count()
    {
        using var fixture = new FixtureRoot();
        var run = RunSupport(
            ["theme", "apply", "--journal", fixture.JournalPath, "--mode", "light",
             "--owner-pid", OwnerPid, "--owner-start-utc-ticks", OwnerTicks,
             "--owner-context", OwnerContext,
             "--product-hash-kind", "sha256", "--product-members", "1",
             "--product-member-names", "TraceFold.Media.exe;second", "--product-sha256", ProductSha,
             "--toolkit-hash-kind", "sha256", "--toolkit-members", "2",
             "--toolkit-member-names", "visual-gate;support", "--toolkit-sha256", ToolkitSha,
             "--fixture-root", fixture.Path], licensed: true);

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public void A_literal_receipt_in_argv_is_refused()
    {
        using var fixture = new FixtureRoot();
        string token = new('C', 64);
        var run = RunSupport(
            ["theme", "restore", "--journal", fixture.JournalPath, "--disposition", "handback",
             "--owner-pid", OwnerPid, "--owner-start-utc-ticks", OwnerTicks,
             "--owner-context", OwnerContext,
             .. BindingArguments(ProductSha, ToolkitSha),
             "--handback-token", token, "--fixture-root", fixture.Path], licensed: true);

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
        Assert.DoesNotContain(token, run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(token, run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fixture_root_without_the_process_license_is_refused_before_any_journal_access()
    {
        using var fixture = new FixtureRoot();
        var run = RunSupport(
            ["theme", "inspect", "--journal", fixture.JournalPath,
             "--fixture-root", fixture.Path], licensed: false);

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
        Assert.False(File.Exists(fixture.JournalPath));
        Assert.Equal("01000000", fixture.RegistryBytes());
        Assert.Equal(0, fixture.BroadcastCount());
    }

    [Fact]
    public void A_fixture_root_that_escapes_its_name_is_refused()
    {
        using var fixture = new FixtureRoot();
        string escaped = fixture.Path + @"\..\outside";
        var run = RunSupport(
            ["theme", "inspect", "--journal", fixture.JournalPath, "--fixture-root", escaped],
            licensed: true);

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
    }

    [Fact]
    public void A_fixture_leaf_that_is_a_reparse_point_is_refused_before_the_journal_is_touched()
    {
        using var fixture = new FixtureRoot();
        string outside = Path.Combine(fixture.Path, "outside.json");
        File.WriteAllText(outside, """{"fail":false,"count":0}""");
        string registry = Path.Combine(fixture.Path, "registry.json");
        File.Delete(registry);
        try
        {
            File.CreateSymbolicLink(registry, outside);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This host does not allow creating a symbolic link: " + error.GetType().Name);
            return;
        }

        var run = RunSupport(ApplyArguments(fixture), licensed: true);

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
        Assert.False(File.Exists(fixture.JournalPath));
        Assert.Equal("""{"fail":false,"count":0}""", File.ReadAllText(outside));
    }

    [Fact]
    public void A_missing_fixture_leaf_is_refused_instead_of_being_read_as_absent()
    {
        using var fixture = new FixtureRoot();
        File.Delete(Path.Combine(fixture.Path, "probe.json"));

        var run = RunSupport(
            ["theme", "inspect", "--journal", fixture.JournalPath, "--fixture-root", fixture.Path],
            licensed: true);

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public void Theme_output_files_are_refused_so_the_receipt_cannot_be_written()
    {
        using var fixture = new FixtureRoot();
        string target = Path.Combine(fixture.Path, "theme.json");
        string[] args = [.. ApplyArguments(fixture), "--output", target];
        var run = RunSupport(args, licensed: true);

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public void Apply_then_hand_back_restores_the_exact_original()
    {
        using var fixture = new FixtureRoot();
        string token = Apply(fixture);

        Assert.True(File.Exists(fixture.JournalPath));
        Assert.Equal("00000000", fixture.RegistryBytes());

        var run = RunSupport(
            ["theme", "restore", "--journal", fixture.JournalPath, "--disposition", "handback",
             "--owner-pid", OwnerPid, "--owner-start-utc-ticks", OwnerTicks,
             "--owner-context", OwnerContext,
             .. BindingArguments(ProductSha, ToolkitSha),
             "--handback-token", "-", "--fixture-root", fixture.Path],
            licensed: true, stdin: token + "\n");

        Assert.Equal(Program.ExitSuccess, run.Exit);
        using JsonDocument body = JsonDocument.Parse(run.Stdout);
        JsonElement root = body.RootElement;
        Assert.True(root.GetProperty("restored").GetBoolean());
        Assert.True(root.GetProperty("journalRemoved").GetBoolean());
        Assert.True(root.GetProperty("journalPresent").GetBoolean());
        Assert.True(root.GetProperty("journalChecksumValid").GetBoolean());
        Assert.True(root.GetProperty("bindingMatchesJournal").GetBoolean());
        Assert.Equal("01000000", root.GetProperty("readback").GetProperty("bytes").GetString());
        Assert.False(File.Exists(fixture.JournalPath));
        Assert.Equal("01000000", fixture.RegistryBytes());
        Assert.Equal(2, fixture.BroadcastCount());
        Assert.DoesNotContain(token, run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(token, run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_back_without_expected_bindings_is_an_argument_error_and_changes_nothing()
    {
        using var fixture = new FixtureRoot();
        string token = Apply(fixture);
        string before = fixture.RegistryBytes();
        int broadcasts = fixture.BroadcastCount();

        var run = RunSupport(
            ["theme", "restore", "--journal", fixture.JournalPath, "--disposition", "handback",
             "--owner-pid", OwnerPid, "--owner-start-utc-ticks", OwnerTicks,
             "--owner-context", OwnerContext,
             "--handback-token", "-", "--fixture-root", fixture.Path],
            licensed: true, stdin: token + "\n");

        Assert.Equal(Program.ExitInvalidArguments, run.Exit);
        Assert.True(File.Exists(fixture.JournalPath));
        Assert.Equal(before, fixture.RegistryBytes());
        Assert.Equal(broadcasts, fixture.BroadcastCount());
    }

    [Fact]
    public void A_wrong_binding_is_refused_before_the_registry_broadcast_or_journal_is_touched()
    {
        using var fixture = new FixtureRoot();
        string token = Apply(fixture);
        string before = fixture.RegistryBytes();
        int broadcasts = fixture.BroadcastCount();

        var run = RunSupport(
            ["theme", "restore", "--journal", fixture.JournalPath, "--disposition", "handback",
             "--owner-pid", OwnerPid, "--owner-start-utc-ticks", OwnerTicks,
             "--owner-context", OwnerContext,
             .. BindingArguments(OtherSha, ToolkitSha),
             "--handback-token", "-", "--fixture-root", fixture.Path],
            licensed: true, stdin: token + "\n");

        Assert.Equal(Program.ExitSuccess, run.Exit);
        using JsonDocument body = JsonDocument.Parse(run.Stdout);
        JsonElement root = body.RootElement;
        Assert.False(root.GetProperty("restored").GetBoolean());
        Assert.Equal("theme.bindings_mismatch", root.GetProperty("rejection").GetString());
        Assert.False(root.GetProperty("bindingMatchesJournal").GetBoolean());
        // The refusal is decided before the write, so the applied value, the broadcast count and
        // the journal are exactly what the apply left behind.
        Assert.True(File.Exists(fixture.JournalPath));
        Assert.Equal(before, fixture.RegistryBytes());
        Assert.Equal(broadcasts, fixture.BroadcastCount());
    }

    [Fact]
    public void A_missing_receipt_is_refused_and_keeps_the_journal()
    {
        using var fixture = new FixtureRoot();
        _ = Apply(fixture);

        var run = RunSupport(
            ["theme", "restore", "--journal", fixture.JournalPath, "--disposition", "handback",
             "--owner-pid", OwnerPid, "--owner-start-utc-ticks", OwnerTicks,
             "--owner-context", OwnerContext,
             .. BindingArguments(ProductSha, ToolkitSha),
             "--handback-token", "-", "--fixture-root", fixture.Path],
            licensed: true, stdin: "\n");

        Assert.Equal(Program.ExitSuccess, run.Exit);
        using JsonDocument body = JsonDocument.Parse(run.Stdout);
        Assert.False(body.RootElement.GetProperty("restored").GetBoolean());
        Assert.Equal("theme.handback_token_missing",
            body.RootElement.GetProperty("rejection").GetString());
        Assert.True(File.Exists(fixture.JournalPath));
    }

    [Fact]
    public void A_failed_registry_write_still_returns_the_receipt_and_can_be_handed_back()
    {
        using var fixture = new FixtureRoot();
        fixture.SetFailWrite(true);

        var apply = RunSupport(ApplyArguments(fixture), licensed: true);
        Assert.Equal(Program.ExitSuccess, apply.Exit);
        using JsonDocument applyBody = JsonDocument.Parse(apply.Stdout);
        JsonElement applied = applyBody.RootElement;
        Assert.False(applied.GetProperty("applied").GetBoolean());
        Assert.Equal("theme.apply_write_failed", applied.GetProperty("rejection").GetString());
        Assert.True(applied.GetProperty("journalWritten").GetBoolean());
        string token = applied.GetProperty("handbackToken").GetString()!;
        Assert.Equal(64, token.Length);

        fixture.SetFailWrite(false);
        var restore = RunSupport(
            ["theme", "restore", "--journal", fixture.JournalPath, "--disposition", "handback",
             "--owner-pid", OwnerPid, "--owner-start-utc-ticks", OwnerTicks,
             "--owner-context", OwnerContext,
             .. BindingArguments(ProductSha, ToolkitSha),
             "--handback-token", "-",
             "--fixture-root", fixture.Path],
            licensed: true, stdin: token + "\n");
        Assert.Equal(Program.ExitSuccess, restore.Exit);
        using JsonDocument restoreBody = JsonDocument.Parse(restore.Stdout);
        Assert.True(restoreBody.RootElement.GetProperty("restored").GetBoolean());
        Assert.False(File.Exists(fixture.JournalPath));
    }

    private static string Apply(FixtureRoot fixture)
    {
        var run = RunSupport(ApplyArguments(fixture), licensed: true);
        Assert.Equal(Program.ExitSuccess, run.Exit);
        using JsonDocument body = JsonDocument.Parse(run.Stdout);
        JsonElement root = body.RootElement;
        Assert.True(root.GetProperty("applied").GetBoolean());
        Assert.True(root.GetProperty("journalWritten").GetBoolean());
        string token = root.GetProperty("handbackToken").GetString()!;
        Assert.Equal(64, token.Length);
        return token;
    }

    private static string[] ApplyArguments(FixtureRoot fixture) =>
    [
        "theme", "apply", "--journal", fixture.JournalPath, "--mode", "dark",
        "--owner-pid", OwnerPid, "--owner-start-utc-ticks", OwnerTicks,
        "--owner-context", OwnerContext,
        .. BindingArguments(ProductSha, ToolkitSha),
        "--fixture-root", fixture.Path,
    ];

    private static string[] BindingArguments(string productSha, string toolkitSha) =>
    [
        "--product-hash-kind", "sha256", "--product-members", "1",
        "--product-member-names", "TraceFold.Media.exe", "--product-sha256", productSha,
        "--toolkit-hash-kind", "sha256", "--toolkit-members", "2",
        "--toolkit-member-names", "visual-gate;support", "--toolkit-sha256", toolkitSha,
    ];

    private static (int Exit, string Stdout, string Stderr) RunSupport(
        string[] args, bool licensed, string stdin = "")
    {
        string executable = Path.Combine(
            AppContext.BaseDirectory,
            "TraceFold.Media.UiAcceptance.Support.exe");
        Assert.True(File.Exists(executable), executable);
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (string argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        if (licensed)
        {
            start.Environment["TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE"] = "1";
        }
        else
        {
            start.Environment.Remove("TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE");
        }

        using Process process = Process.Start(start)!;
        if (stdin.Length > 0)
        {
            process.StandardInput.Write(stdin);
        }

        process.StandardInput.Close();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60000), "the support CLI did not exit within 60 s");
        return (process.ExitCode, stdout, stderr);
    }

    private sealed class FixtureRoot : IDisposable
    {
        public FixtureRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tracefold-theme-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            JournalPath = System.IO.Path.Combine(Path, "theme-recovery.json");
            Write("registry.json",
                """{"present":true,"kind":"DWord","bytesHex":"01000000","failWrite":false,"failDelete":false}""");
            Write("broadcast.json", """{"fail":false,"count":0}""");
            Write("probe.json", """{"state":"live","identityReadable":true,"detail":"fixture"}""");
        }

        public string Path { get; }

        public string JournalPath { get; }

        public string RegistryBytes()
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(Path, "registry.json")));
            return document.RootElement.GetProperty("bytesHex").GetString()!;
        }

        public int BroadcastCount()
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(Path, "broadcast.json")));
            return document.RootElement.GetProperty("count").GetInt32();
        }

        public void SetFailWrite(bool fail) =>
            Write("registry.json",
                $$"""{"present":true,"kind":"DWord","bytesHex":"01000000","failWrite":{{(fail ? "true" : "false")}},"failDelete":false}""");

        private void Write(string name, string contents) =>
            File.WriteAllText(System.IO.Path.Combine(Path, name), contents, new UTF8Encoding(false));

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
