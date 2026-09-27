using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
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

    public const string OwnedProcessesEnvironmentVariable = "TRACEFOLD_UIA_OWNED_PIDS";

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
        if (!TryRead(values, "parent", out string parent)
            || !TryRead(values, "run-id", out string runIdText)
            || !TryRead(values, "token", out string token)
            || !Guid.TryParseExact(runIdText, "D", out Guid runId))
        {
            return Fail(output, ExitInvalidArguments, "create requires parent, run-id and token.");
        }

        string root = OwnedRootManager.Create(parent, runId, token, fileSystem);
        return Emit(output, values, Json(root, $"\"root\":\"{root}\""));
    }

    private static int ValidateRoot(
        Dictionary<string, string> values,
        IRootFileSystem fileSystem,
        TextWriter output)
    {
        if (!TryRead(values, "root", out string root) || !TryRead(values, "token", out string token))
        {
            return Fail(output, ExitInvalidArguments, "validate requires root and token.");
        }

        RootRejection rejection = OwnedRootManager.Validate(root, token, fileSystem);
        if (rejection != RootRejection.None)
        {
            return Fail(output, ExitUnsafeRoot, "The root failed ownership validation: " + rejection + ".");
        }

        return Emit(output, values, Json(root, "\"owned\":true"));
    }

    private static int DeleteRoot(
        Dictionary<string, string> values,
        IRootFileSystem fileSystem,
        TextWriter output)
    {
        if (!TryRead(values, "root", out string root) || !TryRead(values, "token", out string token))
        {
            return Fail(output, ExitInvalidArguments, "delete requires root and token.");
        }

        OwnedRootManager.Delete(root, token, fileSystem);
        return Emit(output, values, Json(root, "\"deleted\":true"));
    }

    private static int RunSnapshot(IReadOnlyList<string> args, TextWriter output)
    {
        if (!TryParsePairs(args, 1, out Dictionary<string, string> values, out string? error))
        {
            return Fail(output, ExitInvalidArguments, error!);
        }

        if (!TryRead(values, "data-root", out string dataRoot)
            || !TryRead(values, "token", out string token))
        {
            return Fail(output, ExitInvalidArguments, "snapshot requires data-root and token.");
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

        if (!TryRead(values, "process-id", out string processIdText)
            || !TryRead(values, "width-dip", out string widthText)
            || !TryRead(values, "height-dip", out string heightText)
            || !int.TryParse(processIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId)
            || !int.TryParse(widthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(heightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
        {
            return Fail(output, ExitInvalidArguments, "resize requires process-id, width-dip and height-dip.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Fail(output, ExitWindowUnavailable, "The window command requires Windows.");
        }

        if (!TryReadProcess(processId, out string? executable))
        {
            return Fail(output, ExitWindowUnavailable, "The window is unavailable.");
        }

        bool ownedByRunner = OwnedProcessIds().Contains(processId);
        uint windowDpi = GetDpiForWindow(Process.GetProcessById(processId).MainWindowHandle);
        int dpi = windowDpi == 0 ? 96 : (int)windowDpi;
        ViewportRequest? request = WindowViewportController.TryCreateRequest(
            width,
            height,
            processId,
            executable!,
            ownedByRunner,
            dpi <= 0 ? 96 : dpi,
            0,
            0);
        if (request is null)
        {
            return Fail(output, ExitWindowUnavailable, "The window request was refused.");
        }

        return Emit(output, values, Json(
            dataRoot: null,
            $"\"processId\":{request.ProcessId},\"widthPixels\":{request.WidthPixels},"
            + $"\"heightPixels\":{request.HeightPixels}"));
    }

    private static bool TryReadProcess(int processId, out string? executable)
    {
        executable = null;
        try
        {
            using Process process = Process.GetProcessById(processId);
            executable = process.MainModule?.FileName;
            return executable is not null && process.MainWindowHandle != IntPtr.Zero;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static HashSet<int> OwnedProcessIds()
    {
        var owned = new HashSet<int>();
        string? value = Environment.GetEnvironmentVariable(OwnedProcessesEnvironmentVariable);
        foreach (string part in (value ?? string.Empty).Split([';', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int processId))
            {
                owned.Add(processId);
            }
        }

        return owned;
    }

    private static int Emit(TextWriter output, Dictionary<string, string> values, string json)
    {
        if (TryRead(values, "output", out string path))
        {
            File.WriteAllText(path, json);
        }

        output.WriteLine(json);
        return ExitSuccess;
    }

    private static string Json(string? dataRoot, string body) =>
        (dataRoot is null ? "{" : "{\"dataRoot\":\"" + dataRoot + "\",") + body + "}";

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
                error = "The option " + token + " has no value.";
                return false;
            }

            string name = token[2..];
            if (values.ContainsKey(name))
            {
                error = "The option " + token + " is repeated.";
                return false;
            }

            values[name] = args[index + 1];
            index++;
        }

        return true;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);
}
