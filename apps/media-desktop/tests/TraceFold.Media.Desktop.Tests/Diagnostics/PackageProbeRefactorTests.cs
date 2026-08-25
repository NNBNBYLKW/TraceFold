using System.Text.Json;
using TraceFold.Media.Desktop.Diagnostics;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

public sealed class PackageProbeRefactorTests
{
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
        PackageProbeRequest request = Request.Stage4Conflict(root);

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

    private static class Request
    {
        public static PackageProbeRequest Stage4Conflict(OwnedProbeRoot root) =>
            new(
                root.Path,
                root.TokenText,
                PackageProbeScenarioKind.Stage4Conflict,
                null,
                null);
    }
}
