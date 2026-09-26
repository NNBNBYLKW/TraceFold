using System.Text.Json;
using TraceFold.Media.Desktop.Diagnostics;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

public sealed class Stage5APackageProbeTests
{
    private const string RootArgument = "--package-probe-root";
    private const string TokenArgument = "--package-probe-token";
    private const string ScenarioArgument = "--package-probe-scenario";
    private const string Scenario = "stage5a-batch";
    private const string RootPrefix = "tracefold-media-desktop-stage5a-probe-";

    [Fact]
    public void Stage5A_arguments_require_exact_owned_root_token_and_scenario()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(RootPrefix);

        PackageProbeOptionResult result = PackageProbeOptions.Parse(
        [
            RootArgument, root.Path,
            TokenArgument, root.TokenText,
            ScenarioArgument, Scenario
        ]);

        Assert.Equal(PackageProbeOptionState.Ready, result.State);
        Assert.Equal(PackageProbeScenarioKind.Stage5ABatch, result.Request!.Scenario);
    }

    [Fact]
    public void Stage5A_arguments_accept_the_three_pairs_in_any_order()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(RootPrefix);

        PackageProbeOptionResult result = PackageProbeOptions.Parse(
        [
            ScenarioArgument, Scenario,
            TokenArgument, root.TokenText,
            RootArgument, root.Path
        ]);

        Assert.Equal(PackageProbeOptionState.Ready, result.State);
        Assert.Equal(root.Path, result.Request!.Root);
        Assert.Equal(root.TokenText, result.Request.OwnerToken);
    }

    [Fact]
    public void Stage5A_arguments_leave_an_empty_command_line_not_requested()
    {
        PackageProbeOptionResult result = PackageProbeOptions.Parse([]);

        Assert.Equal(PackageProbeOptionState.NotRequested, result.State);
    }

    [Fact]
    public void Stage5A_arguments_reject_mixed_legacy_generic_duplicate_unknown_and_fault_pairs()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(RootPrefix);
        IReadOnlyList<string>[] arguments =
        [
            [
                RootArgument, root.Path,
                TokenArgument, root.TokenText,
                ScenarioArgument, Scenario,
                Stage4PackageProbeOptions.RootArgument, root.Path
            ],
            [
                RootArgument, root.Path,
                TokenArgument, root.TokenText,
                RootArgument, root.Path
            ],
            [
                RootArgument, root.Path,
                TokenArgument, root.TokenText,
                ScenarioArgument, "stage5a-unknown"
            ],
            [
                RootArgument, root.Path,
                TokenArgument, root.TokenText,
                ScenarioArgument, Scenario,
                Stage4PackageProbeOptions.FaultArgument, "AfterBackupCreated"
            ],
            [RootArgument, root.Path]
        ];

        foreach (IReadOnlyList<string> item in arguments)
        {
            PackageProbeOptionResult result = PackageProbeOptions.Parse(item);

            Assert.Equal(PackageProbeOptionState.Invalid, result.State);
            Assert.Null(result.Request);
        }
    }

    [Fact]
    public void Stage5A_arguments_reject_a_wrong_prefix_or_changed_owner_token()
    {
        using OwnedProbeRoot wrongPrefix = OwnedProbeRoot.CreateWithFixtures(
            "tracefold-media-desktop-stage4-probe-");
        using OwnedProbeRoot changedToken = OwnedProbeRoot.CreateWithFixtures(RootPrefix);

        PackageProbeOptionResult prefixResult = Parse(wrongPrefix.Path, wrongPrefix.TokenText);
        PackageProbeOptionResult tokenResult = Parse(
            changedToken.Path,
            Guid.NewGuid().ToString("D"));

        Assert.Equal(PackageProbeOptionState.Invalid, prefixResult.State);
        Assert.Equal(PackageProbeOptionState.Invalid, tokenResult.State);
        Assert.Equal("probe.owner_mismatch", tokenResult.ErrorCode);
    }

    [Fact]
    public void Stage5A_arguments_reject_reparse_points_extra_entries_and_changed_fixture_hashes()
    {
        using OwnedProbeRoot reparse = OwnedProbeRoot.CreateWithFixtures(RootPrefix);
        string fixtures = Path.Combine(reparse.Path, "fixtures");
        Directory.Delete(fixtures, recursive: true);
        Directory.CreateSymbolicLink(fixtures, Path.GetTempPath());
        using OwnedProbeRoot extra = OwnedProbeRoot.CreateWithFixtures(RootPrefix);
        File.WriteAllText(Path.Combine(extra.Path, "unexpected.txt"), "extra");
        using OwnedProbeRoot changed = OwnedProbeRoot.CreateWithFixtures(RootPrefix);
        File.AppendAllText(Path.Combine(changed.Path, "fixtures", "photo.jpg"), "changed");

        try
        {
            Assert.Equal(PackageProbeOptionState.Invalid,
                Parse(reparse.Path, reparse.TokenText).State);
        }
        finally
        {
            Directory.Delete(fixtures);
        }

        Assert.Equal(PackageProbeOptionState.Invalid,
            Parse(extra.Path, extra.TokenText).State);
        Assert.Equal(PackageProbeOptionState.Invalid,
            Parse(changed.Path, changed.TokenText).State);
    }

    [Fact]
    public async Task Stage5A_scenario_completes_metadata_processing_and_partial_ingest()
    {
        using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(RootPrefix);
        PackageProbeRequest request = PackageProbeRequests.Stage5ABatch(root);

        int exitCode = await PackageProbeRunner.RunAsync(
            request, "C:\\Program Files\\ffmpeg\\bin", TestContext.Current.CancellationToken);
        using JsonDocument result = await ReadResultAsync(root.Path);

        Assert.True(exitCode == 0,
            "Probe failed with " + result.RootElement.GetProperty("ErrorCode").GetString() + "."
            + Environment.NewLine + result.RootElement.GetRawText());
        Assert.True(result.RootElement.GetProperty("Succeeded").GetBoolean(),
            result.RootElement.GetRawText());
        Assert.True(result.RootElement.GetProperty("AtomicMetadata").GetBoolean());
        Assert.True(result.RootElement.GetProperty("GenericProcessingQueued").GetBoolean());
        Assert.True(result.RootElement.GetProperty("ProcessingCompleted").GetBoolean());
        Assert.Equal(3, result.RootElement.GetProperty("CompletedIngestCount").GetInt32());
        Assert.Equal(1, result.RootElement.GetProperty("ConflictCount").GetInt32());
        Assert.True(result.RootElement.GetProperty("ConflictSourceRetained").GetBoolean());
        Assert.True(result.RootElement.GetProperty("InternalStorageClean").GetBoolean());
        Assert.True(result.RootElement.TryGetProperty("ErrorCode", out _));
    }

    private static PackageProbeOptionResult Parse(string root, string token) =>
        PackageProbeOptions.Parse(
        [
            RootArgument, root,
            TokenArgument, token,
            ScenarioArgument, Scenario
        ]);

    private static async Task<JsonDocument> ReadResultAsync(string root) =>
        JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, "stage5a-probe-result.json"),
            TestContext.Current.CancellationToken));
}
