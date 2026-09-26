using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace TraceFold.Media.Desktop.Diagnostics;

/// <summary>
/// Probe-only diagnostic channel. Nothing in production writes to it and it is inert
/// unless a test explicitly enables it with <see cref="EnableCapture"/>, so probe error
/// codes stay stable in the persisted result while the detailed state, the original
/// exception, its SQLite codes and the observed database file state are reported to the
/// collecting test only. Every observation is best effort: it can never change a probe
/// result.
/// </summary>
public static class PackageProbeTrace
{
    public const string Category = "TraceFold.PackageProbe";

    private static readonly AsyncLocal<int> CaptureDepth = new();

    public static bool CaptureEnabled => CaptureDepth.Value > 0;

    public static IDisposable EnableCapture()
    {
        CaptureDepth.Value += 1;
        return new CaptureScope();
    }

    public static void Write(string fact)
    {
        if (!CaptureEnabled)
        {
            return;
        }

        Trace.WriteLine(fact, Category);
    }

    /// <summary>
    /// Writes the original exception chain, including the numeric SQLite codes, which
    /// the coded probe outcome otherwise discards.
    /// </summary>
    public static void WriteException(string label, Exception? error)
    {
        try
        {
            for (Exception? current = error; current is not null; current = current.InnerException)
            {
                string sqlite = current is SqliteException database
                    ? "; sqlite=" + database.SqliteErrorCode + "/" + database.SqliteExtendedErrorCode
                    : string.Empty;
                string native = current is System.ComponentModel.Win32Exception win32
                    ? "; native=" + win32.NativeErrorCode
                    : string.Empty;
                Write("exception " + label
                    + "; type=" + current.GetType().FullName
                    + "; hresult=" + current.HResult
                    + sqlite
                    + native
                    + "; message=" + current.Message);
            }
        }
        catch (Exception error2)
        {
            Write("exception.unavailable type=" + error2.GetType().FullName);
        }
    }

    /// <summary>
    /// Records the state of one database file. It reads metadata only and never opens a
    /// connection, so it cannot move the WAL state it observes.
    /// </summary>
    public static void WriteFileState(string databasePath, string phase, string role)
    {
        try
        {
            string path = role switch
            {
                "wal" => databasePath + "-wal",
                "shm" => databasePath + "-shm",
                _ => databasePath
            };
            if (!File.Exists(path))
            {
                Write("dbstate " + phase + "; file=" + role + "; db=" + databasePath
                    + "; exists=false");
                return;
            }

            var info = new FileInfo(path);
            string attributes;
            try
            {
                attributes = File.GetAttributes(path).ToString();
            }
            catch (Exception error)
            {
                attributes = "unreadable:" + error.GetType().Name;
            }

            Write("dbstate " + phase + "; file=" + role + "; db=" + databasePath
                + "; exists=true"
                + "; length=" + info.Length
                + "; createdUtc=" + info.CreationTimeUtc.ToString("O")
                + "; writtenUtc=" + info.LastWriteTimeUtc.ToString("O")
                + "; attributes=" + attributes);
        }
        catch (Exception error)
        {
            Write("dbstate " + phase + "; file=" + role + "; unavailable="
                + error.GetType().Name);
        }
    }

    /// <summary>
    /// Describes a live SQLite connection: requested mode, pooling, native read-only
    /// state and the state of its database files. The native check is a read-only query.
    /// </summary>
    public static void WriteConnectionState(string label, SqliteConnection connection)
    {
        try
        {
            var options = new SqliteConnectionStringBuilder(connection.ConnectionString);
            int nativeReadOnly = -1;
            try
            {
                nativeReadOnly = connection.Handle is { } handle
                    ? SQLitePCL.raw.sqlite3_db_readonly(handle, "main")
                    : -1;
            }
            catch (Exception error)
            {
                Write("connection " + label + "; nativeReadOnly=unavailable:"
                    + error.GetType().Name);
            }

            Write("connection " + label
                + "; db=" + options.DataSource
                + "; requested=" + options.Mode
                + "; pooling=" + options.Pooling
                + "; state=" + connection.State
                + "; nativeReadOnly=" + nativeReadOnly);
            foreach (string role in new[] { "main", "wal", "shm" })
            {
                WriteFileState(options.DataSource, label, role);
            }
        }
        catch (Exception error)
        {
            Write("connection " + label + "; unavailable=" + error.GetType().Name);
        }
    }

    private sealed class CaptureScope : IDisposable
    {
        public void Dispose() => CaptureDepth.Value = Math.Max(0, CaptureDepth.Value - 1);
    }
}
