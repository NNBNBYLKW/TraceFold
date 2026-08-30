using System.Text.Json;
using System.Text.RegularExpressions;
using TraceFold.Media.Desktop.Diagnostics;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

public sealed class Stage5CBDiagnosticsPackageProbeTests
{
    [Fact]
    public void Stage5CB_exact_scenario_maps_to_the_guarded_request()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5CB();

        PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

        Assert.Equal(PackageProbeOptionState.Ready, result.State);
        Assert.Equal(
            PackageProbeScenarioKind.Stage5CBDiagnostics,
            result.Request!.Scenario);
        Assert.Equal(root.Path, result.Request.Root);
    }

    [Fact]
    public void Stage5CB_rejects_wrong_token_prefix_extra_entry_and_missing_fixture()
    {
        using OwnedProbeRoot owned = OwnedProbeRoot.CreateStage5CB();
        using OwnedProbeRoot wrongPrefix = OwnedProbeRoot.CreateStage5C();
        using OwnedProbeRoot extra = OwnedProbeRoot.CreateStage5CB();
        using OwnedProbeRoot missing = OwnedProbeRoot.CreateStage5CB();
        File.WriteAllText(Path.Combine(extra.Path, "unexpected.txt"), "extra");
        File.Delete(Path.Combine(missing.Path, "fixtures", "photo.jpg"));

        PackageProbeOptionResult wrongToken = Parse(
            owned.Path,
            Guid.NewGuid().ToString("D"));
        PackageProbeOptionResult wrongRoot = Parse(
            wrongPrefix.Path,
            wrongPrefix.TokenText);
        PackageProbeOptionResult nonEmpty = Parse(extra.Path, extra.TokenText);
        PackageProbeOptionResult missingFixture = Parse(
            missing.Path,
            missing.TokenText);

        Assert.Equal("probe.owner_mismatch", wrongToken.ErrorCode);
        Assert.Equal("probe.invalid_root", wrongRoot.ErrorCode);
        Assert.Equal("probe.root_not_empty", nonEmpty.ErrorCode);
        Assert.Equal("probe.fixtures_invalid", missingFixture.ErrorCode);
        Assert.All(
            new[] { wrongToken, wrongRoot, nonEmpty, missingFixture },
            result =>
            {
                Assert.Equal(PackageProbeOptionState.Invalid, result.State);
                Assert.Null(result.Request);
            });
    }

    [Fact]
    public void Stage5CB_rejects_a_reparse_point_below_the_owned_root()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5CB();
        string fixtures = Path.Combine(root.Path, "fixtures");
        Directory.Delete(fixtures, recursive: true);
        Directory.CreateSymbolicLink(fixtures, Path.GetTempPath());

        try
        {
            PackageProbeOptionResult result = Parse(root.Path, root.TokenText);

            Assert.Equal(PackageProbeOptionState.Invalid, result.State);
            Assert.Null(result.Request);
        }
        finally
        {
            Directory.Delete(fixtures);
        }
    }

    [Fact]
    public async Task Stage5CB_probe_honors_a_cancelled_token_before_writing()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5CB();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PackageProbeRunner.RunAsync(
                PackageProbeRequests.Stage5CBDiagnostics(root),
                cancellation.Token));
        Assert.False(File.Exists(Path.Combine(
            root.Path,
            PackageProbeOptions.Stage5CBResultFilename)));
    }

    [Fact]
    public async Task Stage5CB_probe_proves_read_only_diagnostics_and_redaction()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5CB();

        int exitCode = await PackageProbeRunner.RunAsync(
            PackageProbeRequests.Stage5CBDiagnostics(root),
            TestContext.Current.CancellationToken);
        Stage5CBDiagnosticsProbeResult result = root.ReadStage5CBResult();
        string json = await File.ReadAllTextAsync(
            Path.Combine(root.Path, PackageProbeOptions.Stage5CBResultFilename),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.True(result.Succeeded, result.ErrorCode);
        Assert.True(result.AllCategoriesObserved);
        Assert.False(result.PackagedEnvironmentHealthy);
        Assert.True(result.FailureIsolationPassed);
        Assert.True(result.RedactionPassed);
        Assert.True(result.BusinessDataUnchanged);
        Assert.True(result.SettingsUnchanged);
        Assert.True(result.BackgroundStateUnchanged);
        Assert.True(result.MediaFilesUnchanged);
        Assert.True(result.TemporaryArtifactsClean);
        Assert.True(result.ToolProcessesClean);
        Assert.Null(result.ErrorCode);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.All(document.RootElement.EnumerateObject(), property =>
            Assert.True(property.Value.ValueKind is
                JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null
                or JsonValueKind.String));
        foreach (string forbidden in new[]
        {
            root.Path,
            root.TokenText,
            ".db",
            ".log",
            ".jpg",
            "stage5cb-formal",
            "stage5cb-pending",
            "Stage5CB Metadata Sentinel",
            "SHA256",
            "Hash"
        })
        {
            Assert.DoesNotContain(forbidden, json,
                StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(
            new Regex(
                "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            json);
    }

    private static PackageProbeOptionResult Parse(string root, string token) =>
        PackageProbeOptions.Parse(
        [
            "--package-probe-scenario", "stage5cb-diagnostics",
            "--package-probe-token", token,
            "--package-probe-root", root
        ]);
}
