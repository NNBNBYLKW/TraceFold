using System.Runtime.CompilerServices;
using System.Text.Json;
using TraceFold.Media.Desktop.Diagnostics;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

public sealed class PackageProbeRefactorTests
{
    [Fact]
    public void Offline_publish_uses_the_verified_cache_as_restore_target()
    {
        string script = File.ReadAllText(RepositoryFile(
            "scripts",
            "publish-media-desktop.ps1"));

        Assert.Equal(
            3,
            Count(script, "\"-p:RestorePackagesPath=$localPackageSource\""));
    }

    [Fact]
    public void Stage5CB_smoke_cross_checks_manifest_commit_and_version_identity()
    {
        string script = File.ReadAllText(RepositoryFile(
            "scripts",
            "smoke-media-desktop-package.ps1"));

        Assert.Contains(
            "$expectedStage5CBVersion = \"4.0.0-stage5cb+$($packageVersion.SourceCommit)\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "[string]$packageVersion.InformationalVersion -cne $expectedStage5CBVersion",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_keeps_the_exact_informational_version_without_sdk_suffixes()
    {
        string script = File.ReadAllText(RepositoryFile(
            "scripts",
            "publish-media-desktop.ps1"));

        Assert.Contains(
            "-p:IncludeSourceRevisionInInformationalVersion=false",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Smoke_cleanup_waits_for_every_owned_process_before_removing_logs()
    {
        string script = File.ReadAllText(RepositoryFile(
            "scripts",
            "smoke-media-desktop-package.ps1"));

        Assert.Contains(
            "function Stop-OwnedProcessAndWait",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$Process.WaitForExit(5000)",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Stop-OwnedProcessAndWait -Process $_ -AllowedPaths @($packageFfmpegPath, $packageFfprobePath)",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Stop-OwnedProcessAndWait -Process $_ -AllowedPaths @($exePath)",
            script,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stage5a-batch-extra")]
    [InlineData("stage5b-library-extra")]
    [InlineData("Stage5B-Library")]
    public void Generic_probe_rejects_every_non_exact_scenario(string scenario)
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(
            "tracefold-media-desktop-stage5b-probe-");

        PackageProbeOptionResult result = PackageProbeOptions.Parse([
            "--package-probe-root", root.Path,
            "--package-probe-token", root.TokenText,
            "--package-probe-scenario", scenario]);

        Assert.Equal(PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.invalid_arguments", result.ErrorCode);
    }

    [Fact]
    public void Legacy_stage4_arguments_map_to_the_common_request_without_value_changes()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures();

        PackageProbeOptionResult common = PackageProbeOptions.Parse(
        [
            Stage4PackageProbeOptions.RootArgument, root.Path,
            Stage4PackageProbeOptions.TokenArgument, root.TokenText,
            Stage4PackageProbeOptions.ModeArgument,
            Stage4PackageProbeOptions.ConflictMode
        ]);

        Assert.Equal(PackageProbeOptionState.Ready, common.State);
        Assert.Equal(PackageProbeScenarioKind.Stage4Conflict,
            common.Request!.Scenario);
        Assert.Equal(root.Path, common.Request.Root);
        Assert.Equal(root.TokenText, common.Request.OwnerToken);
    }

    [Fact]
    public async Task Common_runner_preserves_the_stage4_conflict_result_schema()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures();
        PackageProbeRequest request = PackageProbeRequests.Stage4Conflict(root);

        int exitCode = await PackageProbeRunner.RunAsync(
            request,
            "C:\\Program Files\\ffmpeg\\bin",
            TestContext.Current.CancellationToken);
        using JsonDocument result = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root.Path, Stage4PackageProbeOptions.ResultFilename),
            TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        foreach (string property in new[]
        {
            "Succeeded", "RegisteredReplacePassed", "ContentKeepBothPassed",
            "UnmanagedReplacePassed", "ReopenPassed", "ProcessedMediaPassed",
            "ImageAssetCount", "VideoAssetCount", "TotalAssetCount", "ErrorCode"
        })
        {
            Assert.True(result.RootElement.TryGetProperty(property, out _), property);
        }
    }

    private static string RepositoryFile(
        string folder,
        string filename,
        [CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testFile)!,
            "..",
            "..",
            "..",
            "..",
            "..",
            folder,
            filename));

    private static int Count(string value, string search)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
