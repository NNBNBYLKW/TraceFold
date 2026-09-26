using System.Diagnostics;

namespace TraceFold.Media.Desktop.Diagnostics;

/// <summary>
/// Probe-only diagnostic channel. Nothing in production writes to it, only the
/// package-probe tests listen to it, and it never reaches the persisted probe result
/// contract: probe error codes stay stable while the detailed state that explains a
/// failure is reported here.
/// </summary>
public static class PackageProbeTrace
{
    public const string Category = "TraceFold.PackageProbe";

    public static void Write(string fact) => Trace.WriteLine(fact, Category);

    /// <summary>
    /// Writes the original exception chain, which is otherwise swallowed by the probe
    /// result contract: the coded outcome only keeps the error code.
    /// </summary>
    public static void WriteException(string label, Exception? error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            string native = current is System.ComponentModel.Win32Exception win32
                ? "; native=" + win32.NativeErrorCode
                : string.Empty;
            Write("exception " + label
                + "; type=" + current.GetType().FullName
                + "; hresult=" + current.HResult
                + native
                + "; message=" + current.Message);
        }
    }
}
