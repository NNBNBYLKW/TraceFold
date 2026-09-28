using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Data.Sqlite;

namespace TraceFold.Media.UiAcceptance.Support;

public enum SnapshotStatus
{
    Available,
    Unavailable,
}

public sealed record SnapshotResult(SnapshotStatus Status, string? Json);

/// <summary>
/// Read-only observation of one product database. It opens with <c>Mode=ReadOnly</c> and a
/// private cache, never creates a database or a journal, and emits only counts, state
/// tokens, taxonomy names and formal file names. Anything that looks like an absolute path,
/// an identifier, the run token or the user profile is replaced by <c>[redacted]</c>.
/// </summary>
public static class AcceptanceSnapshotReader
{
    public const string DatabaseFileName = "media-desktop.db";

    private const string Redacted = "[redacted]";

    private static readonly string[] CountedTables =
    [
        "inbox_items",
        "media_assets",
        "source_files",
        "taxonomy_terms",
        "processing_jobs",
        "ingest_operations",
    ];

    public static SnapshotResult Read(string dataRoot, string token)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        string database = Path.Combine(dataRoot, DatabaseFileName);
        if (!File.Exists(database))
        {
            return new SnapshotResult(SnapshotStatus.Unavailable, null);
        }

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
                DefaultTimeout = 2,
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            return new SnapshotResult(SnapshotStatus.Available, Describe(connection, token));
        }
        catch (SqliteException)
        {
            return new SnapshotResult(SnapshotStatus.Unavailable, null);
        }
        catch (InvalidOperationException)
        {
            return new SnapshotResult(SnapshotStatus.Unavailable, null);
        }
    }

    private static string Describe(SqliteConnection connection, string token)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = false,
                   // Keep CJK taxonomy names readable instead of \uXXXX escapes.
                   Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
               }))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("tables");
            foreach (string table in CountedTables)
            {
                writer.WriteNumber(table, CountRows(connection, table));
            }

            writer.WriteEndObject();

            WriteArray(writer, "inboxStates", Distinct(connection, "inbox_items", ["state"]), token);
            WriteArray(writer, "processingStates", Distinct(connection, "processing_jobs", ["status"]), token);
            WriteArray(writer, "ingestStates", Distinct(connection, "ingest_operations", ["stage"]), token);
            WriteArray(writer, "formalFilenames", FormalFilenames(connection), token);
            WriteArray(writer, "displayNames", Distinct(connection, "media_assets", ["display_name"]), token);
            WriteFormalAssets(writer, connection, token);

            writer.WriteStartObject("taxonomy");
            foreach (string kind in new[] { "author", "series", "tag" })
            {
                WriteArray(writer, kind, TaxonomyNames(connection, kind), token);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteArray(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<string> values,
        string token)
    {
        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(Redact(value, token));
        }

        writer.WriteEndArray();
    }

    private static long CountRows(SqliteConnection connection, string table) =>
        TableExists(connection, table) ? Scalar(connection, "SELECT COUNT(*) FROM \"" + table + "\"") : 0;

    private static IReadOnlyList<string> Distinct(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> candidateColumns)
    {
        string? column = ResolveColumn(connection, table, candidateColumns);
        if (column is null)
        {
            return [];
        }

        var values = new List<string>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT \"" + column + "\" FROM \"" + table + "\" ORDER BY 1";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                values.Add(reader.GetString(0));
            }
        }

        return values;
    }

    private static IReadOnlyList<string> TaxonomyNames(SqliteConnection connection, string kind)
    {
        if (!TableExists(connection, "taxonomy_terms"))
        {
            return [];
        }

        var values = new List<string>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT DISTINCT \"display_name\" FROM \"taxonomy_terms\" WHERE lower(\"kind\") = $kind ORDER BY 1";
        command.Parameters.AddWithValue("$kind", kind);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                values.Add(reader.GetString(0));
            }
        }

        return values;
    }

    private static IReadOnlyList<string> FormalFilenames(SqliteConnection connection)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in Distinct(connection, "source_files", ["formal_filename"]))
        {
            values.Add(value);
        }

        foreach (string value in Distinct(connection, "media_assets", ["actual_filename"]))
        {
            values.Add(value);
        }

        return [.. values.Order(StringComparer.Ordinal)];
    }

    // Emit only public library fields. IDs are used as join keys inside the read-only connection
    // and never leave it; paths, hashes, run tokens and profile text are not serialized.
    private static void WriteFormalAssets(Utf8JsonWriter writer, SqliteConnection connection, string token)
    {
        writer.WriteStartArray("formalAssets");
        if (TableExists(connection, "media_assets"))
        {
            var assets = new List<(string Id, string Filename, string? Display, string? Kind, string? Series)>();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, actual_filename, display_name, media_kind, series_id
                FROM media_assets ORDER BY actual_filename, id
                """;
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    assets.Add((reader.GetString(0), reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4)));
                }
            }
            foreach (var asset in assets)
            {
                writer.WriteStartObject();
                writer.WriteString("actualFilename", Redact(asset.Filename, token));
                writer.WriteString("displayName", asset.Display is null ? null : Redact(asset.Display, token));
                writer.WriteString("mediaKind", asset.Kind is null ? null : Redact(asset.Kind, token));
                WriteAssetTerms(writer, connection, "authors", "media_asset_authors", "author_id", asset.Id, token);
                writer.WriteString("series", asset.Series is null ? null : ReadSeries(connection, asset.Series, token));
                WriteAssetTerms(writer, connection, "tags", "media_asset_tags", "tag_id", asset.Id, token);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    private static void WriteAssetTerms(Utf8JsonWriter writer, SqliteConnection connection,
        string outputName, string joinTable, string termColumn, string assetId, string token)
    {
        writer.WriteStartArray(outputName);
        if (TableExists(connection, joinTable))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT t.normalized_name FROM \"" + joinTable +
                "\" j JOIN taxonomy_terms t ON t.id = j.\"" + termColumn +
                "\" WHERE j.media_asset_id = $asset ORDER BY t.normalized_name";
            command.Parameters.AddWithValue("$asset", assetId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) { writer.WriteStringValue(Redact(reader.GetString(0), token)); }
        }
        writer.WriteEndArray();
    }

    private static string? ReadSeries(SqliteConnection connection, string seriesId, string token)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT normalized_name FROM taxonomy_terms WHERE id = $series";
        command.Parameters.AddWithValue("$series", seriesId);
        return command.ExecuteScalar() is string value ? Redact(value, token) : null;
    }

    private static string? ResolveColumn(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> candidates)
    {
        if (!TableExists(connection, table))
        {
            return null;
        }

        HashSet<string> columns = Columns(connection, table);
        foreach (string candidate in candidates)
        {
            if (columns.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static HashSet<string> Columns(SqliteConnection connection, string table)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(\"" + table + "\")";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string Redact(string value, string token)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if ((!string.IsNullOrEmpty(token)
                && value.Contains(token, StringComparison.OrdinalIgnoreCase))
            || value.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase)
            || Guid.TryParse(value, out _)
            || value.Contains('\\')
            || value.Contains('/')
            || value.Contains(':'))
        {
            return Redacted;
        }

        return value;
    }
}
