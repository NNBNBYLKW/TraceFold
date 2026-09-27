using System.Security.Cryptography;
using System.Text.Json;
using TraceFold.Media.UiAcceptance.Support;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

public sealed class SupportProgramTests
{
    [Fact]
    public void Root_create_emits_valid_json_and_keeps_the_marker_private()
    {
        Guid runId = Guid.NewGuid();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var output = new StringWriter();
        string? root = null;
        try
        {
            int exit = Program.Run([
                "root", "create", "--parent", Path.GetTempPath(),
                "--run-id", runId.ToString("D"), "--token", token,
            ], output);

            Assert.Equal(Program.ExitSuccess, exit);
            using JsonDocument json = JsonDocument.Parse(output.ToString());
            root = json.RootElement.GetProperty("root").GetString();
            Assert.Equal(RootRejection.None,
                OwnedRootManager.Validate(root!, token, new SystemRootFileSystem(), runId));
            Assert.DoesNotContain(token, output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (root is not null && Directory.Exists(root))
            {
                OwnedRootManager.Delete(root, token, new SystemRootFileSystem(), runId);
            }
        }
    }

    [Fact]
    public void Snapshot_refuses_an_unowned_data_root()
    {
        using TempTree tree = TempTree.Create();
        using var output = new StringWriter();

        int exit = Program.Run([
            "snapshot", "--data-root", tree.Sandbox,
            "--token", tree.Token,
        ], output);

        Assert.Equal(Program.ExitUnsafeRoot, exit);
        Assert.DoesNotContain(tree.Sandbox, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_accepts_an_owned_root_without_creating_a_database()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();
        using var output = new StringWriter();

        int exit = Program.Run([
            "snapshot", "--data-root", root, "--token", tree.Token,
        ], output);

        Assert.Equal(Program.ExitSnapshotUnavailable, exit);
        Assert.False(File.Exists(Path.Combine(root,
            AcceptanceSnapshotReader.DatabaseFileName)));
    }

    [Fact]
    public void Rejects_unknown_options_before_any_file_operation()
    {
        using var output = new StringWriter();

        int exit = Program.Run([
            "root", "delete", "--root", Path.GetTempPath(),
            "--token", "irrelevant", "--unexpected", "value",
        ], output);

        Assert.Equal(Program.ExitInvalidArguments, exit);
    }

    [Fact]
    public void A_live_runner_process_allows_validation_but_blocks_deletion()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();
        File.WriteAllText(Path.Combine(root, OwnedRootManager.LiveProcessFileName),
            Environment.ProcessId + "|1|TraceFold.Media.exe");
        using var validation = new StringWriter();
        using var deletion = new StringWriter();

        Assert.Equal(Program.ExitSuccess, Program.Run([
            "root", "validate", "--root", root, "--token", tree.Token,
        ], validation));
        Assert.Equal(Program.ExitUnsafeRoot, Program.Run([
            "root", "delete", "--root", root, "--token", tree.Token,
        ], deletion));
        Assert.True(Directory.Exists(root));
    }
}
