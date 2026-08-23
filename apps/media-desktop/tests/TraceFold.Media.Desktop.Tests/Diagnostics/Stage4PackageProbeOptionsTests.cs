using System.Text.Json;
using TraceFold.Media.Desktop.Diagnostics;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

public sealed class Stage4PackageProbeOptionsTests
{
    [Fact]
    public void Parse_without_probe_arguments_keeps_normal_desktop_mode()
    {
        Stage4PackageProbeOptionResult result = Stage4PackageProbeOptions.Parse([]);

        Assert.Equal(Stage4PackageProbeOptionState.NotRequested, result.State);
        Assert.Null(result.Options);
    }

    [Fact]
    public void Parse_accepts_six_ordered_or_paired_arguments()
    {
        using ProbeRoot root = ProbeRoot.Create();

        Stage4PackageProbeOptionResult ordered = Parse(root.Path, root.TokenText);
        Stage4PackageProbeOptionResult reordered = Stage4PackageProbeOptions.Parse(
        [
            Stage4PackageProbeOptions.ModeArgument,
            Stage4PackageProbeOptions.ConflictMode,
            Stage4PackageProbeOptions.TokenArgument,
            root.TokenText,
            Stage4PackageProbeOptions.RootArgument,
            root.Path
        ]);

        Assert.Equal(Stage4PackageProbeOptionState.Ready, ordered.State);
        Assert.Equal(Stage4PackageProbeOptionState.Ready, reordered.State);
        Assert.Equal(root.Path, ordered.Options!.Root);
        Assert.Equal(root.Token, ordered.Options.Token);
        Assert.Equal(Stage4PackageProbeOptions.ConflictMode, ordered.Options.Mode);
    }

    [Fact]
    public void Parse_rejects_nested_root_before_creating_subdirectories()
    {
        using ProbeRoot owner = ProbeRoot.Create();
        string nested = Directory.CreateDirectory(Path.Combine(
            owner.Path,
            $"tracefold-media-desktop-stage4-probe-{Guid.NewGuid():N}")).FullName;
        File.WriteAllText(Path.Combine(nested, "probe-owner.txt"), owner.TokenText);
        Directory.CreateDirectory(Path.Combine(nested, "fixtures"));

        Stage4PackageProbeOptionResult result = Parse(nested, owner.TokenText);

        Assert.Equal(Stage4PackageProbeOptionState.Invalid, result.State);
        Assert.False(Directory.Exists(Path.Combine(nested, "data")));
        Assert.False(Directory.Exists(Path.Combine(nested, "inbox")));
    }

    [Fact]
    public void Parse_rejects_missing_or_changed_owner_marker()
    {
        using ProbeRoot missing = ProbeRoot.Create();
        File.Delete(Path.Combine(missing.Path, "probe-owner.txt"));
        using ProbeRoot changed = ProbeRoot.Create();

        Stage4PackageProbeOptionResult missingResult = Parse(
            missing.Path,
            missing.TokenText);
        Stage4PackageProbeOptionResult changedResult = Parse(
            changed.Path,
            Guid.NewGuid().ToString("D"));

        Assert.Equal(Stage4PackageProbeOptionState.Invalid, missingResult.State);
        Assert.Equal("probe.owner_mismatch", missingResult.ErrorCode);
        Assert.Equal(Stage4PackageProbeOptionState.Invalid, changedResult.State);
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

        Stage4PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage4PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.root_not_empty", result.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_rejects_preexisting_probe_result_entry(bool directory)
    {
        using ProbeRoot root = ProbeRoot.Create();
        string resultPath = Path.Combine(
            root.Path,
            Stage4PackageProbeOptions.ResultFilename);
        if (directory)
        {
            Directory.CreateDirectory(resultPath);
        }
        else
        {
            File.WriteAllText(resultPath, "do-not-overwrite");
        }

        Stage4PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage4PackageProbeOptionState.Invalid, result.State);
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

        Stage4PackageProbeOptionResult rootResult = Parse(
            rootExtra.Path,
            rootExtra.TokenText);
        Stage4PackageProbeOptionResult fixtureResult = Parse(
            fixtureExtra.Path,
            fixtureExtra.TokenText);

        Assert.Equal(Stage4PackageProbeOptionState.Invalid, rootResult.State);
        Assert.Equal("probe.root_not_empty", rootResult.ErrorCode);
        Assert.Equal(Stage4PackageProbeOptionState.Invalid, fixtureResult.State);
        Assert.Equal("probe.fixtures_invalid", fixtureResult.ErrorCode);
    }

    [Theory]
    [InlineData("paired.mp4")]
    [InlineData("paired.m4a")]
    [InlineData("photo.jpg")]
    public void Parse_rejects_changed_fixture_hash(string filename)
    {
        using ProbeRoot root = ProbeRoot.Create();
        File.AppendAllText(Path.Combine(root.Path, "fixtures", filename), "changed");

        Stage4PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage4PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.fixtures_invalid", result.ErrorCode);
    }

    [Fact]
    public void Parse_rejects_partial_extra_duplicate_or_unknown_mode_arguments()
    {
        Stage4PackageProbeOptionResult partial = Stage4PackageProbeOptions.Parse(
            [Stage4PackageProbeOptions.RootArgument, Path.GetTempPath()]);
        Stage4PackageProbeOptionResult extra = Stage4PackageProbeOptions.Parse(
        [
            Stage4PackageProbeOptions.RootArgument,
            Path.GetTempPath(),
            Stage4PackageProbeOptions.TokenArgument,
            Guid.NewGuid().ToString("D"),
            Stage4PackageProbeOptions.ModeArgument,
            Stage4PackageProbeOptions.ConflictMode,
            "unexpected"
        ]);
        Stage4PackageProbeOptionResult duplicate = Stage4PackageProbeOptions.Parse(
        [
            Stage4PackageProbeOptions.RootArgument,
            Path.GetTempPath(),
            Stage4PackageProbeOptions.RootArgument,
            Path.GetTempPath(),
            Stage4PackageProbeOptions.ModeArgument,
            Stage4PackageProbeOptions.ConflictMode
        ]);
        Stage4PackageProbeOptionResult unknownMode = Stage4PackageProbeOptions.Parse(
        [
            Stage4PackageProbeOptions.RootArgument,
            Path.GetTempPath(),
            Stage4PackageProbeOptions.TokenArgument,
            Guid.NewGuid().ToString("D"),
            Stage4PackageProbeOptions.ModeArgument,
            "unknown"
        ]);

        Assert.Equal(Stage4PackageProbeOptionState.Invalid, partial.State);
        Assert.Equal(Stage4PackageProbeOptionState.Invalid, extra.State);
        Assert.Equal(Stage4PackageProbeOptionState.Invalid, duplicate.State);
        Assert.Equal(Stage4PackageProbeOptionState.Invalid, unknownMode.State);
    }

    [Fact]
    public async Task RunAsync_completes_all_five_real_conflict_probe_cases()
    {
        using ProbeRoot root = ProbeRoot.Create();
        Stage4PackageProbeOptions options = Parse(
            root.Path,
            root.TokenText).Options!;

        int exitCode = await Stage4PackageProbe.RunAsync(
            options,
            "C:\\Program Files\\ffmpeg\\bin",
            TestContext.Current.CancellationToken);

        string resultPath = Path.Combine(
            root.Path,
            Stage4PackageProbeOptions.ResultFilename);
        Assert.True(File.Exists(resultPath));
        using JsonDocument result = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                resultPath,
                TestContext.Current.CancellationToken));
        string? errorCode = result.RootElement.GetProperty("ErrorCode").GetString();
        Assert.True(exitCode == 0, $"Probe failed with {errorCode}.");
        Assert.True(result.RootElement.GetProperty("Succeeded").GetBoolean());
        foreach (string property in new[]
        {
            "RegisteredReplacePassed",
            "ContentKeepBothPassed",
            "UnmanagedReplacePassed",
            "ReopenPassed",
            "ProcessedMediaPassed"
        })
        {
            Assert.True(result.RootElement.GetProperty(property).GetBoolean());
        }

        Assert.Equal(3, result.RootElement.GetProperty("ImageAssetCount").GetInt32());
        Assert.Equal(1, result.RootElement.GetProperty("VideoAssetCount").GetInt32());
        Assert.Equal(4, result.RootElement.GetProperty("TotalAssetCount").GetInt32());
        Assert.False(File.Exists(Path.Combine(root.Path, "inbox", "paired.mp4")));
        Assert.False(File.Exists(Path.Combine(root.Path, "inbox", "paired.m4a")));
        Assert.True(File.Exists(Path.Combine(root.Path, "library", "paired.mp4")));
        AssertInternalStorageClean(Path.Combine(root.Path, "library"));
    }

    private static Stage4PackageProbeOptionResult Parse(
        string root,
        string token) =>
        Stage4PackageProbeOptions.Parse(
        [
            Stage4PackageProbeOptions.RootArgument,
            root,
            Stage4PackageProbeOptions.TokenArgument,
            token,
            Stage4PackageProbeOptions.ModeArgument,
            Stage4PackageProbeOptions.ConflictMode
        ]);

    private static void AssertInternalStorageClean(string library)
    {
        foreach (string category in new[] { "commit", "recovery" })
        {
            string path = Path.Combine(library, ".tracefold", category);
            Assert.True(!Directory.Exists(path)
                || !Directory.EnumerateFileSystemEntries(path).Any());
        }
    }

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
                $"tracefold-media-desktop-stage4-probe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            File.WriteAllText(
                System.IO.Path.Combine(path, "probe-owner.txt"),
                tokenText);
            string fixtures = Directory.CreateDirectory(
                System.IO.Path.Combine(path, "fixtures")).FullName;
            foreach (string filename in new[]
            {
                "paired.mp4",
                "paired.m4a",
                "photo.jpg"
            })
            {
                File.Copy(
                    Fixture(filename),
                    System.IO.Path.Combine(fixtures, filename));
            }

            return new ProbeRoot(
                System.IO.Path.GetFullPath(path),
                token,
                tokenText);
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
