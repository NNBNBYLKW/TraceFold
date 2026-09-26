using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace TraceFold.Media.Desktop.Diagnostics;

/// <summary>
/// Probe-only diagnostic channel. Nothing in production writes to it and it is inert
/// unless a test explicitly enables it with <see cref="EnableCapture"/>, so probe error
/// codes stay stable in the persisted result while the detailed state, the original
/// exception and its SQLite codes are reported to the collecting test only.
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

    private sealed class CaptureScope : IDisposable
    {
        public void Dispose() => CaptureDepth.Value = Math.Max(0, CaptureDepth.Value - 1);
    }
}
