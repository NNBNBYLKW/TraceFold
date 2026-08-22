using TraceFold.Media.Desktop.Diagnostics;
using System.Text.Json;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

public sealed class Stage2PackageProbeOptionsTests
{
    [Fact]
    public void Parse_without_probe_arguments_keeps_normal_desktop_mode()
    {
        Stage2PackageProbeOptionResult result =
            Stage2PackageProbeOptions.Parse([]);

        Assert.Equal(Stage2PackageProbeOptionState.NotRequested, result.State);
        Assert.Null(result.Options);
    }

    [Fact]
    public void Parse_accepts_verified_direct_temp_child()
    {
        using ProbeRoot root = ProbeRoot.Create();

        Stage2PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage2PackageProbeOptionState.Ready, result.State);
        Assert.Equal(root.Path, result.Options!.Root);
        Assert.Equal(root.Token, result.Options.Token);
    }

    [Fact]
    public void Parse_rejects_nested_root_before_creating_subdirectories()
    {
        using ProbeRoot owner = ProbeRoot.Create();
        string nested = Directory.CreateDirectory(Path.Combine(
            owner.Path,
            $"tracefold-media-desktop-stage2-probe-{Guid.NewGuid():N}")).FullName;
        File.WriteAllText(Path.Combine(nested, "probe-owner.txt"), owner.TokenText);

        Stage2PackageProbeOptionResult result = Parse(nested, owner.TokenText);

        Assert.Equal(Stage2PackageProbeOptionState.Invalid, result.State);
        Assert.False(Directory.Exists(Path.Combine(nested, "data")));
        Assert.False(Directory.Exists(Path.Combine(nested, "inbox")));
    }

    [Fact]
    public void Parse_rejects_owner_token_mismatch()
    {
        using ProbeRoot root = ProbeRoot.Create();

        Stage2PackageProbeOptionResult result = Parse(
            root.Path,
            Guid.NewGuid().ToString("D"));

        Assert.Equal(Stage2PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.owner_mismatch", result.ErrorCode);
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

        Stage2PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage2PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.root_not_empty", result.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_rejects_preexisting_probe_result_entry(bool directory)
    {
        using ProbeRoot root = ProbeRoot.Create();
        string resultPath = Path.Combine(root.Path, "stage2-probe-result.json");
        if (directory)
        {
            Directory.CreateDirectory(resultPath);
        }
        else
        {
            File.WriteAllText(resultPath, "do-not-overwrite");
        }

        Stage2PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(Stage2PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.root_not_empty", result.ErrorCode);
    }

    [Fact]
    public void Parse_rejects_partial_or_extra_probe_arguments()
    {
        Stage2PackageProbeOptionResult partial = Stage2PackageProbeOptions.Parse(
            ["--stage2-package-probe-root", Path.GetTempPath()]);
        Stage2PackageProbeOptionResult extra = Stage2PackageProbeOptions.Parse(
            [
                "--stage2-package-probe-root",
                Path.GetTempPath(),
                "--stage2-package-probe-token",
                Guid.NewGuid().ToString("D"),
                "unexpected"
            ]);

        Assert.Equal(Stage2PackageProbeOptionState.Invalid, partial.State);
        Assert.Equal(Stage2PackageProbeOptionState.Invalid, extra.State);
    }

    [Fact]
    public async Task RunAsync_completes_the_headless_packaged_flow()
    {
        using ProbeRoot root = ProbeRoot.Create();
        Stage2PackageProbeOptions options = Parse(root.Path, root.TokenText).Options!;

        int exitCode = await Stage2PackageProbe.RunAsync(
            options,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(Path.Combine(root.Path, "inbox", "probe.png")));
        Assert.True(File.Exists(Path.Combine(root.Path, "library", "probe.png")));
        string resultPath = Path.Combine(root.Path, "stage2-probe-result.json");
        Assert.True(File.Exists(resultPath));
        using JsonDocument result = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                resultPath,
                TestContext.Current.CancellationToken));
        Assert.True(result.RootElement.GetProperty("Succeeded").GetBoolean());
        Assert.Equal(
            "probe.png",
            result.RootElement.GetProperty("ActualFilename").GetString());
    }

    private static Stage2PackageProbeOptionResult Parse(
        string root,
        string token) =>
        Stage2PackageProbeOptions.Parse(
        [
            "--stage2-package-probe-root",
            root,
            "--stage2-package-probe-token",
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
                $"tracefold-media-desktop-stage2-probe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            File.WriteAllText(
                System.IO.Path.Combine(path, "probe-owner.txt"),
                tokenText);
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
}
