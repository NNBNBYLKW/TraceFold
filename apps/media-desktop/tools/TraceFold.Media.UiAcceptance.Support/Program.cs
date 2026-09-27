using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace TraceFold.Media.UiAcceptance.Support;

/// <summary>
/// Strict, dependency-free command surface for the acceptance toolkit. The task runner is the
/// only caller: every argument is validated, error text is path-redacted, and the exit codes
/// are part of the contract.
/// </summary>
public static class Program
{
    public const int ExitSuccess = 0;

    public const int ExitInvalidArguments = 2;

    public const int ExitUnsafeRoot = 3;

    public const int ExitSnapshotUnavailable = 4;

    public const int ExitWindowUnavailable = 5;

    public const int ExitUnexpectedError = 10;

    public static int Main(string[] args) => Run([.. args], Console.Out);

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        try
        {
            return args.Count == 0
                ? Fail(output, ExitInvalidArguments, "A command is required.")
                : args[0] switch
                {
                    "root" => RunRoot(args, output),
                    "snapshot" => RunSnapshot(args, output),
                    "window" => RunWindow(args, output),
                    _ => Fail(output, ExitInvalidArguments, "The command is not supported."),
                };
        }
        catch (Exception)
        {
            return Fail(output, ExitUnexpectedError, "The support command failed.");
        }
    }

    private static int RunRoot(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count < 2)
        {
            return Fail(output, ExitInvalidArguments, "A root action is required.");
        }

        if (!TryParsePairs(args, 2, out Dictionary<string, string> values, out string? error))
        {
            return Fail(output, ExitInvalidArguments, error!);
        }

        var fileSystem = new SystemRootFileSystem();
        try
        {
            return args[1] switch
            {
                "create" => CreateRoot(values, fileSystem, output),
                "validate" => ValidateRoot(values, fileSystem, output),
                "delete" => DeleteRoot(values, fileSystem, output),
                _ => Fail(output, ExitInvalidArguments, "The root action is not supported."),
            };
        }
        catch (IOException)
        {
            return Fail(output, ExitUnsafeRoot, "The root is not an owned probe root.");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(output, ExitUnsafeRoot, "The root is not an owned probe root.");
        }
        catch (ArgumentException)
        {
            return Fail(output, ExitInvalidArguments, "The arguments are invalid.");
        }
    }

    private static int CreateRoot(
        Dictionary<string, string> values,
        IRootFileSystem fileSystem,
        TextWriter output)
    {
        if (!OnlyOptions(values, "parent", "run-id", "token", "output")
            || !TryRead(values, "parent", out string parent)
            || !TryRead(values, "run-id", out string runIdText)
            || !TryRead(values, "token", out string token)
            || !Guid.TryParseExact(runIdText, "D", out Guid runId))
        {
            return Fail(output, ExitInvalidArguments, "create requires parent, run-id and token.");
        }

        string root = OwnedRootManager.Create(parent, runId, token, fileSystem);
        return Emit(output, values, JsonSerializer.Serialize(new { root }));
    }

    private static int ValidateRoot(
        Dictionary<string, string> values,
        IRootFileSystem fileSystem,
        TextWriter output)
    {
        if (!OnlyOptions(values, "root", "token", "output")
            || !TryRead(values, "root", out string root)
            || !TryRead(values, "token", out string token))
        {
            return Fail(output, ExitInvalidArguments, "validate requires root and token.");
        }

        RootRejection rejection = OwnedRootManager.Validate(root, token, fileSystem,
            allowLiveProcessForRead: true);
        if (rejection != RootRejection.None)
        {
            return Fail(output, ExitUnsafeRoot, "The root failed ownership validation: " + rejection + ".");
        }

        return Emit(output, values, JsonSerializer.Serialize(new { owned = true }));
    }

    private static int DeleteRoot(
        Dictionary<string, string> values,
        IRootFileSystem fileSystem,
        TextWriter output)
    {
        if (!OnlyOptions(values, "root", "token", "output")
            || !TryRead(values, "root", out string root)
            || !TryRead(values, "token", out string token))
        {
            return Fail(output, ExitInvalidArguments, "delete requires root and token.");
        }

        OwnedRootManager.Delete(root, token, fileSystem);
        return Emit(output, values, JsonSerializer.Serialize(new { deleted = true }));
    }

    private static int RunSnapshot(IReadOnlyList<string> args, TextWriter output)
    {
        if (!TryParsePairs(args, 1, out Dictionary<string, string> values, out string? error))
        {
            return Fail(output, ExitInvalidArguments, error!);
        }

        if (!OnlyOptions(values, "data-root", "token", "output")
            || !TryRead(values, "data-root", out string dataRoot)
            || !TryRead(values, "token", out string token))
        {
            return Fail(output, ExitInvalidArguments, "snapshot requires data-root and token.");
        }

        bool ownedDataRoot;
        try
        {
            ownedDataRoot = IsOwnedDataRoot(dataRoot, token);
        }
        catch (Exception failure) when (failure is IOException
            or UnauthorizedAccessException or ArgumentException)
        {
            ownedDataRoot = false;
        }

        if (!ownedDataRoot)
        {
            return Fail(output, ExitUnsafeRoot, "The data root is not an owned probe root.");
        }

        SnapshotResult result = AcceptanceSnapshotReader.Read(dataRoot, token);
        return result.Status == SnapshotStatus.Available
            ? Emit(output, values, result.Json!)
            : Fail(output, ExitSnapshotUnavailable, "The snapshot is unavailable.");
    }

    private static int RunWindow(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count < 2 || args[1] != "resize")
        {
            return Fail(output, ExitInvalidArguments, "The window action is not supported.");
        }

        if (!TryParsePairs(args, 2, out Dictionary<string, string> values, out string? error))
        {
            return Fail(output, ExitInvalidArguments, error!);
        }

        if (!OnlyOptions(values, "process-id", "width-dip", "height-dip",
                "root", "token", "output")
            || !TryRead(values, "process-id", out string processIdText)
            || !TryRead(values, "width-dip", out string widthText)
            || !TryRead(values, "height-dip", out string heightText)
            || !TryRead(values, "root", out string root)
            || !TryRead(values, "token", out string token)
            || !int.TryParse(processIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId)
            || !int.TryParse(widthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(heightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
        {
            return Fail(output, ExitInvalidArguments,
                "resize requires process-id, width-dip, height-dip, root and token.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Fail(output, ExitWindowUnavailable, "The window command requires Windows.");
        }

        if (!IsValidOwnedRoot(root, token))
        {
            return Fail(output, ExitUnsafeRoot, "The process root is not owned.");
        }

        if (!TryReadProcess(processId, out Process? process, out string? executable,
                out long startTicks))
        {
            return Fail(output, ExitWindowUnavailable, "The window is unavailable.");
        }

        using (process)
        {
            if (!IsRecordedProcess(root, processId, startTicks, executable!)
                || WindowViewportController.Validate(width, height, processId,
                    Path.GetFileName(executable!), ownedByRunner: true)
                    != ViewportRejection.None
                || process!.HasExited)
            {
                return Fail(output, ExitWindowUnavailable, "The window request was refused.");
            }

            ViewportResizeResult? result = WindowViewportController.Resize(
                process.MainWindowHandle, width, height, new Win32WindowViewportNative());
            if (result is null)
            {
                return Fail(output, ExitWindowUnavailable,
                    "The client area could not be set to the requested size.");
            }

            return Emit(output, values, JsonSerializer.Serialize(new
            {
                processId,
                widthPixels = result.RequestedWidthPixels,
                heightPixels = result.RequestedHeightPixels,
                clientWidthPixels = result.ActualWidthPixels,
                clientHeightPixels = result.ActualHeightPixels,
            }));
        }
    }

    private static bool TryReadProcess(int processId, out Process? process,
        out string? executable, out long startTicks)
    {
        process = null;
        executable = null;
        startTicks = 0;
        try
        {
            process = Process.GetProcessById(processId);
            process.Refresh();
            executable = process.MainModule?.FileName;
            startTicks = process.StartTime.ToUniversalTime().Ticks;
            if (executable is not null && process.MainWindowHandle != IntPtr.Zero
                && !process.HasExited)
            {
                return true;
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        process?.Dispose();
        process = null;
        return false;
    }

    private static bool IsRecordedProcess(string root, int processId,
        long startTicks, string executable)
    {
        string ledger = Path.Combine(OwnedRootManager.CanonicalDirectory(root),
            OwnedRootManager.LiveProcessFileName);
        var fileSystem = new SystemRootFileSystem();
        if (!File.Exists(ledger) || fileSystem.IsReparsePoint(ledger))
        {
            return false;
        }

        return WindowViewportController.IsRecordedProcess(
            File.ReadLines(ledger), processId, startTicks, executable);
    }

    private static int Emit(TextWriter output, Dictionary<string, string> values, string json)
    {
        if (TryRead(values, "output", out string path))
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(json);
        }

        output.WriteLine(json);
        return ExitSuccess;
    }

    private static bool IsOwnedDataRoot(string dataRoot, string token)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot))
        {
            return false;
        }

        string canonical = OwnedRootManager.CanonicalDirectory(dataRoot);
        string temp = OwnedRootManager.CanonicalDirectory(Path.GetTempPath());
        string? ownedRoot = canonical;
        while (ownedRoot is not null
            && !string.Equals(Path.GetDirectoryName(ownedRoot), temp,
                StringComparison.OrdinalIgnoreCase))
        {
            ownedRoot = Path.GetDirectoryName(ownedRoot);
        }

        if (ownedRoot is null || !IsValidOwnedRoot(ownedRoot, token))
        {
            return false;
        }

        var fileSystem = new SystemRootFileSystem();
        for (string? current = canonical;
            current is not null
                && !string.Equals(current, ownedRoot, StringComparison.OrdinalIgnoreCase);
            current = Path.GetDirectoryName(current))
        {
            if (fileSystem.IsReparsePoint(current))
            {
                return false;
            }
        }

        return !fileSystem.IsReparsePoint(Path.Combine(canonical,
            AcceptanceSnapshotReader.DatabaseFileName));
    }

    private static bool IsValidOwnedRoot(string root, string token)
    {
        try
        {
            return OwnedRootManager.Validate(root, token,
                new SystemRootFileSystem(), allowLiveProcessForRead: true)
                == RootRejection.None;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static int Fail(TextWriter output, int exitCode, string message)
    {
        output.WriteLine("{\"error\":\"" + message.Replace('"', '\'') + "\"}");
        return exitCode;
    }

    private static bool TryRead(
        Dictionary<string, string> values,
        string name,
        out string value) =>
        values.TryGetValue(name, out value!);

    private static bool OnlyOptions(
        Dictionary<string, string> values, params string[] allowed) =>
        values.Keys.All(key => allowed.Contains(key, StringComparer.Ordinal));

    private static bool TryParsePairs(
        IReadOnlyList<string> args,
        int start,
        out Dictionary<string, string> values,
        out string? error)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        error = null;
        for (int index = start; index < args.Count; index++)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                error = "Every argument must be a named option.";
                return false;
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = "An option has no value.";
                return false;
            }

            string name = token[2..];
            if (values.ContainsKey(name))
            {
                error = "An option is repeated.";
                return false;
            }

            values[name] = args[index + 1];
            index++;
        }

        return true;
    }

}
