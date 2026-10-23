using System.Text;

namespace TraceFold.Media.UiAcceptance.Fixtures.LibrarySnapshotUtf8;

/// <summary>
/// Stands in for the support CLI's snapshot command as a real child process.
/// </summary>
/// <remarks>
/// The contract the caller depends on is the bytes on the standard output stream, so the payload is
/// encoded to UTF-8 here and written straight to that stream, exactly as the support CLI does after
/// it pins its own console encoding.
///
/// The terms are composed from code points instead of being written as literals. That keeps this
/// file ASCII while the payload still carries the real characters, which is the point of the
/// fixture: a JSON "\uXXXX" escape would survive a mistranslation of the pipe and would therefore
/// hide the very defect the fixture exists to expose. Terms ending in U+7532 are used because that
/// character's last UTF-8 byte (0xB2) is a lead byte in the double-byte code pages, so a caller that
/// reads this pipe through such a code page consumes the delimiter that follows the term.
///
/// A non-zero exit is requested through an environment variable so that the caller's exit-status
/// handling can be exercised without changing the argument list the production code passes.
/// </remarks>
internal static class Program
{
    private const string ExitCodeVariable = "TRACEFOLD_LIBRARY_SNAPSHOT_FIXTURE_EXIT";

    /// <summary>The snapshot payload, with the terms standing in as markers until it is built.</summary>
    private const string Template =
        "{\"tables\":{\"inbox_items\":3,\"media_assets\":3,\"source_files\":3," +
        "\"taxonomy_terms\":4,\"processing_jobs\":0,\"ingest_operations\":3}," +
        "\"taxonomy\":{\"author\":[\"{A1}\",\"{A2}\"],\"series\":[\"{S1}\"],\"tag\":[\"{G1}\",\"{G2}\"]}," +
        "\"formalAssets\":[" +
        "{\"assetKey\":\"key-photo\",\"actualFilename\":\"photo.jpg\",\"displayName\":\"ACC-photo-3\"," +
        "\"mediaKind\":\"image\",\"authors\":[\"{A1}\"],\"series\":\"{S1}\",\"tags\":[\"{G1}\"]}," +
        "{\"assetKey\":\"key-photo-replacement\",\"actualFilename\":\"photo-replacement.jpg\"," +
        "\"displayName\":\"ACC-photo-replacement-1\",\"mediaKind\":\"image\",\"authors\":[\"{A2}\"]," +
        "\"series\":\"\",\"tags\":[\"{G1}\"]}," +
        "{\"assetKey\":\"key-photo-third\",\"actualFilename\":\"photo-third.jpg\",\"displayName\":\"{TOKEN}\"," +
        "\"mediaKind\":\"image\",\"authors\":[\"{A1}\"],\"series\":\"{S1}\",\"tags\":[\"{G1}\",\"{G2}\"]}" +
        "]}";

    private static int Main(string[] args)
    {
        if (!TryReadSnapshotArguments(args, out string dataRoot, out string token))
        {
            Console.Error.WriteLine("snapshot requires data-root and token.");
            return 2;
        }

        string requested = Environment.GetEnvironmentVariable(ExitCodeVariable) ?? string.Empty;
        if (requested.Length > 0 && int.TryParse(requested, out int exitCode) && exitCode != 0)
        {
            Console.Error.WriteLine("The snapshot is unavailable for " + dataRoot + ".");
            return exitCode;
        }

        byte[] payload = Encoding.UTF8.GetBytes(BuildPayload(token));
        using Stream stdout = Console.OpenStandardOutput();
        stdout.Write(payload, 0, payload.Length);
        stdout.Flush();
        return 0;
    }

    /// <summary>Reads the option pairs the snapshot command accepts, refusing anything else.</summary>
    private static bool TryReadSnapshotArguments(string[] args, out string dataRoot, out string token)
    {
        dataRoot = string.Empty;
        token = string.Empty;
        if (args.Length < 5 || !string.Equals(args[0], "snapshot", StringComparison.Ordinal)) { return false; }

        for (int index = 1; index + 1 < args.Length; index += 2)
        {
            string name = args[index];
            string value = args[index + 1];
            if (string.Equals(name, "--data-root", StringComparison.Ordinal)) { dataRoot = value; }
            else if (string.Equals(name, "--token", StringComparison.Ordinal)) { token = value; }
            else { return false; }
        }

        return dataRoot.Length > 0 && token.Length > 0;
    }

    /// <summary>Builds the payload, substituting the terms and the run token into the template.</summary>
    private static string BuildPayload(string token)
    {
        return Template
            .Replace("{A1}", Term(0x4F5C, 0x8005, 0x7532), StringComparison.Ordinal)
            .Replace("{A2}", Term(0x4F5C, 0x8005, 0x4E59), StringComparison.Ordinal)
            .Replace("{S1}", Term(0x7CFB, 0x5217, 0x7532), StringComparison.Ordinal)
            .Replace("{G1}", Term(0x591C, 0x666F), StringComparison.Ordinal)
            .Replace("{G2}", Term(0x98CE, 0x666F), StringComparison.Ordinal)
            .Replace("{TOKEN}", token, StringComparison.Ordinal);
    }

    /// <summary>Composes one term from its code points so this file stays ASCII.</summary>
    private static string Term(params int[] codePoints)
    {
        StringBuilder builder = new(codePoints.Length);
        foreach (int codePoint in codePoints) { builder.Append(char.ConvertFromUtf32(codePoint)); }
        return builder.ToString();
    }
}
