using System.Text.Json;
using TraceFold.Media.Desktop.Diagnostics;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

public sealed class Stage3PackageProbeOptionsTests
{
    [Fact]
    public void Parse_without_probe_arguments_keeps_normal_desktop_mode()
    {
        Stage3PackageProbeOptionResult result = Stage3PackageProbeOptions.Parse([]);

        Assert.Equal(Stage3PackageProbeOptionState.NotRequested, result.State);
        Assert.Null(result.Options);
    }

    [Fact]
    public void Parse_accepts_verified_direct_temp_child_and_fixed_fixtures()
    {
        using ProbeRoot root = ProbeRoot.Create();

        Stage3PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage3PackageProbeOptionState.Ready, result.State);
        Assert.Equal(root.Path, result.Options!.Root);
        Assert.Equal(root.Token, result.Options.Token);
    }

    [Fact]
    public void Parse_rejects_nested_root_before_creating_subdirectories()
    {
        using ProbeRoot owner = ProbeRoot.Create();
        string nested = Directory.CreateDirectory(Path.Combine(
            owner.Path,
            $"tracefold-media-desktop-stage3-probe-{Guid.NewGuid():N}")).FullName;
        File.WriteAllText(Path.Combine(nested, "probe-owner.txt"), owner.TokenText);
        Directory.CreateDirectory(Path.Combine(nested, "fixtures"));

        Stage3PackageProbeOptionResult result = Parse(nested, owner.TokenText);

        Assert.Equal(Stage3PackageProbeOptionState.Invalid, result.State);
        Assert.False(Directory.Exists(Path.Combine(nested, "data")));
        Assert.False(Directory.Exists(Path.Combine(nested, "inbox")));
    }

    [Fact]
    public void Parse_rejects_missing_or_changed_owner_marker()
    {
        using ProbeRoot missing = ProbeRoot.Create();
        File.Delete(Path.Combine(missing.Path, "probe-owner.txt"));
        using ProbeRoot changed = ProbeRoot.Create();

        Stage3PackageProbeOptionResult missingResult = Parse(
            missing.Path,
            missing.TokenText);
        Stage3PackageProbeOptionResult changedResult = Parse(
            changed.Path,
            Guid.NewGuid().ToString("D"));

        Assert.Equal(Stage3PackageProbeOptionState.Invalid, missingResult.State);
        Assert.Equal("probe.owner_mismatch", missingResult.ErrorCode);
        Assert.Equal(Stage3PackageProbeOptionState.Invalid, changedResult.State);
        Assert.Equal("probe.owner_mismatch", changedResult.ErrorCode);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("inbox")]
    [InlineData("library")]
    [InlineData("work")]
    public void Parse_rejects_preexisting_probe_subdirectory(string name)
    {
        using ProbeRoot root = ProbeRoot.Create();
        Directory.CreateDirectory(Path.Combine(root.Path, name));

        Stage3PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage3PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.root_not_empty", result.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_rejects_preexisting_probe_result_entry(bool directory)
    {
        using ProbeRoot root = ProbeRoot.Create();
        string resultPath = Path.Combine(root.Path, "stage3-probe-result.json");
        if (directory)
        {
            Directory.CreateDirectory(resultPath);
        }
        else
        {
            File.WriteAllText(resultPath, "do-not-overwrite");
        }

        Stage3PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage3PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.root_not_empty", result.ErrorCode);
    }

    [Fact]
    public void Parse_rejects_extra_root_or_fixture_entries()
    {
        using ProbeRoot rootExtra = ProbeRoot.Create();
        File.WriteAllText(Path.Combine(rootExtra.Path, "unexpected.txt"), "extra");
        using ProbeRoot fixtureExtra = ProbeRoot.Create();
        File.WriteAllText(
            Path.Combine(fixtureExtra.Path, "fixtures", "unexpected.bin"),
            "extra");

        Stage3PackageProbeOptionResult rootResult = Parse(
            rootExtra.Path,
            rootExtra.TokenText);
        Stage3PackageProbeOptionResult fixtureResult = Parse(
            fixtureExtra.Path,
            fixtureExtra.TokenText);

        Assert.Equal(Stage3PackageProbeOptionState.Invalid, rootResult.State);
        Assert.Equal("probe.root_not_empty", rootResult.ErrorCode);
        Assert.Equal(Stage3PackageProbeOptionState.Invalid, fixtureResult.State);
        Assert.Equal("probe.fixtures_invalid", fixtureResult.ErrorCode);
    }

    [Theory]
    [InlineData("paired.mp4")]
    [InlineData("paired.m4a")]
    public void Parse_rejects_changed_fixture_hash(string filename)
    {
        using ProbeRoot root = ProbeRoot.Create();
        File.AppendAllText(Path.Combine(root.Path, "fixtures", filename), "changed");

        Stage3PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage3PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.fixtures_invalid", result.ErrorCode);
    }

    [Fact]
    public void Parse_rejects_partial_or_extra_probe_arguments()
    {
        Stage3PackageProbeOptionResult partial = Stage3PackageProbeOptions.Parse(
            ["--stage3-package-probe-root", Path.GetTempPath()]);
        Stage3PackageProbeOptionResult extra = Stage3PackageProbeOptions.Parse(
            [
                "--stage3-package-probe-root",
                Path.GetTempPath(),
                "--stage3-package-probe-token",
                Guid.NewGuid().ToString("D"),
                "unexpected"
            ]);

        Assert.Equal(Stage3PackageProbeOptionState.Invalid, partial.State);
        Assert.Equal(Stage3PackageProbeOptionState.Invalid, extra.State);
    }

    [Fact]
    public async Task RunAsync_completes_the_real_processed_packaged_flow()
    {
        using ProbeRoot root = ProbeRoot.Create();
        Stage3PackageProbeOptions options = Parse(root.Path, root.TokenText).Options!;

        int exitCode = await Stage3PackageProbe.RunAsync(
            options,
            "C:\\Program Files\\ffmpeg\\bin",
            TestContext.Current.CancellationToken);

        string resultPath = Path.Combine(root.Path, "stage3-probe-result.json");
        Assert.True(File.Exists(resultPath));
        using JsonDocument result = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                resultPath,
                TestContext.Current.CancellationToken));
        string? errorCode = result.RootElement.GetProperty("ErrorCode").GetString();
        Assert.True(exitCode == 0, $"Probe failed with {errorCode}.");
        Assert.False(File.Exists(Path.Combine(root.Path, "inbox", "paired.mp4")));
        Assert.False(File.Exists(Path.Combine(root.Path, "inbox", "paired.m4a")));
        Assert.True(File.Exists(Path.Combine(root.Path, "library", "paired.mp4")));
        string processing = Path.Combine(
            root.Path,
            "work",
            ".tracefold",
            "processing");
        Assert.True(
            !Directory.Exists(processing)
            || !Directory.EnumerateFileSystemEntries(processing).Any());
        Assert.True(result.RootElement.GetProperty("Succeeded").GetBoolean());
        Assert.NotEqual(Guid.Empty, result.RootElement.GetProperty("JobId").GetGuid());
        Assert.Equal(
            "paired.mp4",
            result.RootElement.GetProperty("ActualFilename").GetString());
        Assert.True(result.RootElement.GetProperty("HasVideo").GetBoolean());
        Assert.True(result.RootElement.GetProperty("HasAudio").GetBoolean());
    }

    private static Stage3PackageProbeOptionResult Parse(
        string root,
        string token) =>
        Stage3PackageProbeOptions.Parse(
        [
            "--stage3-package-probe-root",
            root,
            "--stage3-package-probe-token",
            token
        ]);

    private sealed class ProbeRoot : IDisposable
    {
        private ProbeRoot(string path, Guid token, string tokenText)
        {
            Path = path;
            Token = token;
            TokenText = tokenText;
        }

        public string Path { get; }

        public Guid Token { get; }

        public string TokenText { get; }

        public static ProbeRoot Create()
        {
            Guid token = Guid.NewGuid();
            string tokenText = token.ToString("D");
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"tracefold-media-desktop-stage3-probe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            File.WriteAllText(
                System.IO.Path.Combine(path, "probe-owner.txt"),
                tokenText);
            string fixtures = Directory.CreateDirectory(
                System.IO.Path.Combine(path, "fixtures")).FullName;
            File.Copy(
                Fixture("paired.mp4"),
                System.IO.Path.Combine(fixtures, "paired.mp4"));
            File.Copy(
                Fixture("paired.m4a"),
                System.IO.Path.Combine(fixtures, "paired.m4a"));
            return new ProbeRoot(System.IO.Path.GetFullPath(path), token, tokenText);
        }

        public void Dispose()
        {
            string temp = System.IO.Path.TrimEndingDirectorySeparator(
                System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()));
            string? parent = System.IO.Path.GetDirectoryName(Path);
            if (parent is not null
                && StringComparer.OrdinalIgnoreCase.Equals(
                    System.IO.Path.TrimEndingDirectorySeparator(parent),
                    temp)
                && Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private static string Fixture(string filename)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(
                current.FullName,
                "apps",
                "media-api",
                "tests",
                "fixtures",
                "media",
                filename);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository fixture is missing: {filename}");
    }
}
