using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
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

    /// <summary>
    /// The published payload is a contract, so its bytes must not depend on the console code page.
    /// </summary>
    /// <remarks>
    /// This runs the built program as a child process and reads its standard output the way the
    /// acceptance toolkit does. A term whose last UTF-8 byte is a lead byte is the case that exposes
    /// the defect: on a double-byte console code page a transcoding would consume the delimiter that
    /// follows the term, so a caller reading UTF-8 would no longer see valid JSON. The term used here
    /// ends in U+6E90, whose UTF-8 encoding ends in the byte 0x90.
    /// </remarks>
    [Fact]
    public void The_published_stream_decodes_as_utf8_for_a_term_ending_in_a_lead_byte()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();
        string term = "\u5408\u5E76\u6E90";
        SeedTerm(root, term);

        (int exit, byte[] published) = RunPublishedSnapshot(root, tree.Token);

        Assert.Equal(0, exit);
        using JsonDocument parsed = JsonDocument.Parse(new ReadOnlyMemory<byte>(published));
        Assert.Equal(
            term,
            parsed.RootElement.GetProperty("taxonomy").GetProperty("author")[0].GetString());
    }

    private static void SeedTerm(string root, string term)
    {
        string database = Path.Combine(root, AcceptanceSnapshotReader.DatabaseFileName);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using SqliteCommand create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE inbox_items(id TEXT PRIMARY KEY, state TEXT);
            CREATE TABLE media_assets(id TEXT PRIMARY KEY, actual_filename TEXT, display_name TEXT, media_kind TEXT, series_id TEXT);
            CREATE TABLE source_files(media_asset_id TEXT, formal_filename TEXT);
            CREATE TABLE taxonomy_terms(id TEXT PRIMARY KEY, kind TEXT, display_name TEXT, normalized_name TEXT);
            CREATE TABLE media_asset_authors(media_asset_id TEXT, author_id TEXT);
            CREATE TABLE media_asset_tags(media_asset_id TEXT, tag_id TEXT);
            CREATE TABLE processing_jobs(id TEXT PRIMARY KEY, status TEXT);
            CREATE TABLE ingest_operations(id TEXT PRIMARY KEY, stage TEXT);
            """;
        create.ExecuteNonQuery();
        using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO taxonomy_terms(id, kind, display_name, normalized_name) " +
            "VALUES($id, 'Author', $name, $name);";
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        insert.Parameters.AddWithValue("$name", term);
        insert.ExecuteNonQuery();
    }

    private static (int Exit, byte[] Published) RunPublishedSnapshot(string root, string token)
    {
        string executable = Path.Combine(
            AppContext.BaseDirectory,
            "TraceFold.Media.UiAcceptance.Support.exe");
        Assert.True(File.Exists(executable), executable);
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // The reader is the toolkit: it takes the pipe as UTF-8, which is the contract.
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add("snapshot");
        start.ArgumentList.Add("--data-root");
        start.ArgumentList.Add(root);
        start.ArgumentList.Add("--token");
        start.ArgumentList.Add(token);
        using Process process = Process.Start(start)!;
        using MemoryStream captured = new();
        process.StandardOutput.BaseStream.CopyTo(captured);
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return (process.ExitCode, captured.ToArray());
    }
}
