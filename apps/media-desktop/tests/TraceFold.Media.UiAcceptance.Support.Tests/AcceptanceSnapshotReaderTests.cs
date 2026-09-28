using Microsoft.Data.Sqlite;
using System.Text.Json;
using TraceFold.Media.UiAcceptance.Support;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

public sealed class AcceptanceSnapshotReaderTests
{
    [Fact]
    public void Reads_logical_facts_without_modifying_the_database()
    {
        using TempTree tree = TempTree.Create();
        string dataRoot = Path.Combine(tree.Sandbox, "owned-data");
        Directory.CreateDirectory(dataRoot);
        string database = Path.Combine(dataRoot, "media-desktop.db");
        CreateProductSchema(database);
        byte[] before = File.ReadAllBytes(database);
        DateTime beforeWrite = File.GetLastWriteTimeUtc(database);

        SnapshotResult result = AcceptanceSnapshotReader.Read(dataRoot, tree.Token);

        Assert.Equal(SnapshotStatus.Available, result.Status);
        string json = result.Json!;
        Assert.Contains("\"inbox_items\":2", json, StringComparison.Ordinal);
        Assert.Contains("ReadyToTag", json, StringComparison.Ordinal);
        Assert.Contains("\"image.jpg\"", json, StringComparison.Ordinal);
        Assert.Contains("\"作者甲\"", json, StringComparison.Ordinal);
        Assert.Contains("Succeeded", json, StringComparison.Ordinal);
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement asset = Assert.Single(document.RootElement.GetProperty("formalAssets").EnumerateArray());
            Assert.Equal("image.jpg", asset.GetProperty("actualFilename").GetString());
            Assert.Equal("image", asset.GetProperty("mediaKind").GetString());
            Assert.Equal("作者甲", Assert.Single(asset.GetProperty("authors").EnumerateArray()).GetString());
            Assert.Equal("系列甲", asset.GetProperty("series").GetString());
            Assert.Equal("风景", Assert.Single(asset.GetProperty("tags").EnumerateArray()).GetString());
        }
        Assert.Equal(before, File.ReadAllBytes(database));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(database));
        Assert.False(File.Exists(database + "-wal"));
        Assert.False(File.Exists(database + "-shm"));
    }

    [Fact]
    public void Omits_paths_identifiers_token_and_profile_text()
    {
        using TempTree tree = TempTree.Create();
        string dataRoot = Path.Combine(tree.Sandbox, "owned-data");
        Directory.CreateDirectory(dataRoot);
        string database = Path.Combine(dataRoot, "media-desktop.db");
        CreateProductSchema(database);

        SnapshotResult result = AcceptanceSnapshotReader.Read(dataRoot, tree.Token);

        string json = result.Json!;
        Assert.DoesNotContain(dataRoot, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tree.Token, json, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Environment.UserName,
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "11111111-1111-1111-1111-111111111111",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_unavailable_without_creating_a_missing_database()
    {
        using TempTree tree = TempTree.Create();
        string dataRoot = Path.Combine(tree.Sandbox, "absent-data");
        Directory.CreateDirectory(dataRoot);

        SnapshotResult result = AcceptanceSnapshotReader.Read(dataRoot, tree.Token);

        Assert.Equal(SnapshotStatus.Unavailable, result.Status);
        Assert.Null(result.Json);
        Assert.False(File.Exists(Path.Combine(dataRoot, "media-desktop.db")));
    }

    private static void CreateProductSchema(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        Execute(connection, """
            CREATE TABLE inbox_items(id TEXT PRIMARY KEY, state TEXT);
            CREATE TABLE media_assets(id TEXT PRIMARY KEY, actual_filename TEXT, display_name TEXT, media_kind TEXT, series_id TEXT);
            CREATE TABLE source_files(media_asset_id TEXT, formal_filename TEXT);
            CREATE TABLE taxonomy_terms(id TEXT PRIMARY KEY, kind TEXT, display_name TEXT, normalized_name TEXT);
            CREATE TABLE media_asset_authors(media_asset_id TEXT, author_id TEXT);
            CREATE TABLE media_asset_tags(media_asset_id TEXT, tag_id TEXT);
            CREATE TABLE processing_jobs(id TEXT PRIMARY KEY, status TEXT);
            CREATE TABLE ingest_operations(id TEXT PRIMARY KEY, stage TEXT);
            """);
        string profile = Environment.UserName;
        Execute(connection, $"""
            INSERT INTO inbox_items VALUES('11111111-1111-1111-1111-111111111111','ReadyToTag');
            INSERT INTO inbox_items VALUES('22222222-2222-2222-2222-222222222222','RequiresProcessing');
            INSERT INTO media_assets VALUES(
                '33333333-3333-3333-3333-333333333333','image.jpg','{profile}-private','image',
                '77777777-7777-7777-7777-777777777777');
            INSERT INTO source_files VALUES(
                '33333333-3333-3333-3333-333333333333','image.jpg');
            INSERT INTO taxonomy_terms VALUES(
                '44444444-4444-4444-4444-444444444444','Author','作者甲','作者甲');
            INSERT INTO taxonomy_terms VALUES(
                '77777777-7777-7777-7777-777777777777','Series','系列甲','系列甲');
            INSERT INTO taxonomy_terms VALUES(
                '88888888-8888-8888-8888-888888888888','Tag','风景','风景');
            INSERT INTO media_asset_authors VALUES(
                '33333333-3333-3333-3333-333333333333','44444444-4444-4444-4444-444444444444');
            INSERT INTO media_asset_tags VALUES(
                '33333333-3333-3333-3333-333333333333','88888888-8888-8888-8888-888888888888');
            INSERT INTO processing_jobs VALUES(
                '55555555-5555-5555-5555-555555555555','Succeeded');
            INSERT INTO ingest_operations VALUES(
                '66666666-6666-6666-6666-666666666666','Completed');
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
