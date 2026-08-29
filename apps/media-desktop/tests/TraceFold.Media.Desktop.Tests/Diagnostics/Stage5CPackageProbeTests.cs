using System.Text.Json;
using TraceFold.Media.Desktop.Diagnostics;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

public sealed class Stage5CPackageProbeTests
{
    [Fact]
    public void Stage5C_exact_scenario_maps_to_the_guarded_request()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5C();

        PackageProbeOptionResult result = PackageProbeOptions.Parse([
            "--package-probe-scenario", "stage5c-taxonomy",
            "--package-probe-token", root.TokenText,
            "--package-probe-root", root.Path]);

        Assert.Equal(PackageProbeOptionState.Ready, result.State);
        Assert.Equal(
            PackageProbeScenarioKind.Stage5CTaxonomy,
            result.Request!.Scenario);
        Assert.Equal(root.Path, result.Request.Root);
    }

    [Fact]
    public void Stage5C_scenario_rejects_a_wrong_token_or_root_prefix()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5C();
        PackageProbeOptionResult wrongToken = PackageProbeOptions.Parse([
            "--package-probe-root", root.Path,
            "--package-probe-token", Guid.NewGuid().ToString("D"),
            "--package-probe-scenario", "stage5c-taxonomy"]);
        using OwnedProbeRoot wrongPrefix = OwnedProbeRoot.CreateWithFixtures();
        PackageProbeOptionResult wrongRoot = PackageProbeOptions.Parse([
            "--package-probe-root", wrongPrefix.Path,
            "--package-probe-token", wrongPrefix.TokenText,
            "--package-probe-scenario", "stage5c-taxonomy"]);

        Assert.Equal(PackageProbeOptionState.Invalid, wrongToken.State);
        Assert.Equal("probe.owner_mismatch", wrongToken.ErrorCode);
        Assert.Equal(PackageProbeOptionState.Invalid, wrongRoot.State);
        Assert.Equal("probe.invalid_root", wrongRoot.ErrorCode);
    }

    [Fact]
    public async Task Stage5C_probe_honors_a_cancelled_token()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5C();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PackageProbeRunner.RunAsync(
                PackageProbeRequests.Stage5CTaxonomy(root),
                cancellation.Token));
    }

    [Fact]
    public async Task Stage5C_probe_proves_query_merge_delete_undo_and_file_invariance()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5C();

        int exitCode = await PackageProbeRunner.RunAsync(
            PackageProbeRequests.Stage5CTaxonomy(root),
            TestContext.Current.CancellationToken);
        Stage5CTaxonomyProbeResult result = root.ReadStage5CResult();
        string json = await File.ReadAllTextAsync(
            Path.Combine(root.Path, PackageProbeOptions.Stage5CResultFilename),
            TestContext.Current.CancellationToken);

        Assert.True(exitCode == 0, result.ErrorCode);
        Assert.True(result.Succeeded);
        Assert.True(result.QueryPassed);
        Assert.True(result.MergePassed);
        Assert.True(result.MixedDeleteBlocked);
        Assert.True(result.DeleteUndoPassed);
        Assert.True(result.MergeUndoPassed);
        Assert.True(result.VersioningPassed);
        Assert.True(result.PhysicalFilesUnchanged);
        Assert.True(result.InternalStorageClean);
        Assert.Null(result.ErrorCode);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.All(document.RootElement.EnumerateObject(), property =>
            Assert.True(property.Value.ValueKind is
                JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null
                or JsonValueKind.Number or JsonValueKind.String));
        foreach (string forbidden in new[]
        {
            root.Path,
            ".jpg",
            "Probe Author",
            "SHA256",
            "Hash"
        })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
