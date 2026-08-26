using System.Text.Json;
using TraceFold.Media.Desktop.Diagnostics;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

public sealed class Stage5BPackageProbeTests
{
    private const string RootPrefix =
        "tracefold-media-desktop-stage5b-probe-";

    [Fact]
    public void Stage5B_scenario_requires_owned_direct_temp_child_and_token()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(
            RootPrefix);

        PackageProbeOptionResult result = PackageProbeOptions.Parse([
            "--package-probe-root", root.Path,
            "--package-probe-token", Guid.NewGuid().ToString("D"),
            "--package-probe-scenario", "stage5b-library"]);

        Assert.Equal(PackageProbeOptionState.Invalid, result.State);
        Assert.Equal("probe.owner_mismatch", result.ErrorCode);
    }

    [Fact]
    public void Stage5B_exact_scenario_maps_to_the_guarded_request()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(
            RootPrefix);

        PackageProbeOptionResult result = PackageProbeOptions.Parse([
            "--package-probe-scenario", "stage5b-library",
            "--package-probe-token", root.TokenText,
            "--package-probe-root", root.Path]);

        Assert.Equal(PackageProbeOptionState.Ready, result.State);
        Assert.Equal(
            PackageProbeScenarioKind.Stage5BLibrary,
            result.Request!.Scenario);
    }

    [Fact]
    public async Task Stage5B_probe_filters_applies_undoes_and_keeps_files_identical()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(
            RootPrefix);

        int exitCode = await PackageProbeRunner.RunAsync(
            PackageProbeRequests.Stage5BLibrary(root),
            "C:\\Program Files\\ffmpeg\\bin",
            TestContext.Current.CancellationToken);
        string json = await File.ReadAllTextAsync(
            Path.Combine(root.Path, "stage5b-probe-result.json"),
            TestContext.Current.CancellationToken);
        Stage5BPackageProbeResult? parsed =
            JsonSerializer.Deserialize<Stage5BPackageProbeResult>(json);
        Assert.NotNull(parsed);
        Stage5BPackageProbeResult result = parsed!;

        Assert.True(exitCode == 0, result.ErrorCode);
        Assert.True(result.Succeeded);
        Assert.True(result.StrictIntersectionPassed);
        Assert.True(result.AtomicBatchPassed);
        Assert.True(result.NamingPreviewMatchedApply);
        Assert.True(result.UndoPassed);
        Assert.True(result.PhysicalFilesUnchanged);
        Assert.True(result.InternalStorageClean);
        Assert.Null(result.ErrorCode);
        Assert.DoesNotContain("Stage 5B", json, StringComparison.Ordinal);
        Assert.DoesNotContain(".jpg", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(root.Path, json, StringComparison.OrdinalIgnoreCase);
    }
}
