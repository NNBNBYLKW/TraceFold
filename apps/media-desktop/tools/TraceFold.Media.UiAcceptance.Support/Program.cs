using System.Diagnostics;
using System.Globalization;
using System.Text;
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

    /// <summary>
    /// Runs the command surface, publishing its payload as UTF-8 whatever the console code page is.
    /// </summary>
    /// <remarks>
    /// The snapshot payload carries taxonomy terms verbatim, so the bytes on the pipe have to be the
    /// UTF-8 encoding of those terms and not a transcoding into the console code page. The caller
    /// reads this pipe as UTF-8, and on a machine whose code page is a double-byte one a term whose
    /// last encoded byte is a lead byte would otherwise consume the delimiter that follows it, which
    /// turns a valid payload into an unreadable one. The console code page is a property of the
    /// machine, so it cannot be allowed to decide the encoding of a contract.
    /// </remarks>
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return Run([.. args], Console.Out);
    }

    public static int Run(IReadOnlyList<string> args, TextWriter output)
        => Run(args, output, Console.In);

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextReader input)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return args.Count == 0
                ? Fail(output, ExitInvalidArguments, "A command is required.")
                : args[0] switch
                {
                    "root" => RunRoot(args, output),
                    "snapshot" => RunSnapshot(args, output),
                    "window" => RunWindow(args, output),
                    "native" => RunNative(args, output),
                    "theme" => RunTheme(args, output, input),
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

    /// <summary>
    /// Read-only native UIA verification of one element, addressed by exact AutomationId inside
    /// the launched window's subtree. It refuses anything that is not the runner's own process,
    /// and it never mutates UI state.
    /// </summary>
    private static int RunNative(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count < 2 || args[1] != "lookup")
        {
            return Fail(output, ExitInvalidArguments, "The native action is not supported.");
        }

        if (!TryParseNamedPairs(args, 2, out List<KeyValuePair<string, string>> pairs))
        {
            return Fail(output, ExitInvalidArguments, "Every argument must be a named option with a value.");
        }

        string[] allowed =
        [
            "root", "token", "process-id", "automation-id", "pattern", "output",
            "descendant-class", "descendant-name",
        ];
        if (pairs.Any(pair => !allowed.Contains(pair.Key, StringComparer.Ordinal)))
        {
            return Fail(output, ExitInvalidArguments, "The arguments are invalid.");
        }

        string? root = Single(pairs, "root");
        string? token = Single(pairs, "token");
        string? processIdText = Single(pairs, "process-id");
        string? automationId = Single(pairs, "automation-id");
        string? descendantClass = Single(pairs, "descendant-class");
        string? descendantName = Single(pairs, "descendant-name");
        string[] patterns = [.. pairs
            .Where(pair => pair.Key == "pattern")
            .Select(pair => pair.Value)];
        if (root is null || token is null || processIdText is null || automationId is null
            || !int.TryParse(processIdText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int processId)
            || patterns.Length != patterns.Distinct(StringComparer.Ordinal).Count()
            || patterns.Any(pattern => !UiAutomationProbe.IsKnownPatternName(pattern))
            || descendantClass is not null && string.IsNullOrWhiteSpace(descendantClass))
        {
            return Fail(output, ExitInvalidArguments,
                "lookup requires root, token, process-id and automation-id, plus known pattern names.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return Fail(output, ExitWindowUnavailable, "The native command requires Windows.");
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
            UiLookupRejection rejection = UiAutomationProbe.Validate(
                automationId,
                processId,
                Path.GetFileName(executable!),
                IsRecordedProcess(root, processId, startTicks, executable!),
                process!.MainWindowHandle);
            if (rejection != UiLookupRejection.None)
            {
                return rejection is UiLookupRejection.UnownedProcess
                    or UiLookupRejection.UnexpectedExecutable
                    ? Fail(output, ExitUnsafeRoot, "The process is not owned by this run.")
                    : Fail(output, ExitWindowUnavailable, "The native probe was refused.");
            }

            UiElementFact? fact = UiAutomationProbe.Lookup(
                process.MainWindowHandle, automationId, patterns, new WindowsUiAutomationNative(),
                descendantClass, descendantName);
            if (fact is null)
            {
                // The probe could not answer: the gate must treat this as blocked, never as an
                // unsupported pattern or an absent element.
                return Fail(output, ExitWindowUnavailable, "The native probe could not answer.");
            }

            var emitValues = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in pairs)
            {
                emitValues[pair.Key] = pair.Value;
            }

            return Emit(output, emitValues, JsonSerializer.Serialize(new
            {
                automationId,
                descendantClass,
                descendantName,
                outerMatchCount = fact.OuterMatchCount,
                descendantMatchCount = fact.DescendantMatchCount,
                name = fact.Name,
                controlType = fact.ControlType,
                className = fact.ClassName,
                isEnabled = fact.IsEnabled,
                isOffscreen = fact.IsOffscreen,
                x = fact.X,
                y = fact.Y,
                width = fact.Width,
                height = fact.Height,
                patterns = fact.Patterns
                    .Select(pattern => new { name = pattern.Name, available = pattern.Available })
                    .ToArray(),
            }));
        }
    }

    /// <summary>
    /// Command surface for the Task 13 temporary app-theme switch.
    /// </summary>
    /// <remarks>
    /// One integration point: arguments become a typed request, the reviewed
    /// WindowsAppThemeCoordinator decides, and the result becomes an explicit JSON projection.
    /// No decision logic lives here and no response is produced by serialising a component record.
    /// Exit codes: 0 means the coordinator returned a verdict (applied/restored may still be
    /// false); 2 means the arguments are malformed; 10 means the environment failed.
    /// </remarks>
    private static readonly string[] ThemeOwnerRequired =
        ["owner-pid", "owner-start-utc-ticks", "owner-context"];

    private static readonly string[] ThemeBindingOptions =
    [
        "product-hash-kind", "product-members", "product-member-names", "product-sha256",
        "toolkit-hash-kind", "toolkit-members", "toolkit-member-names", "toolkit-sha256"
    ];

    private static readonly string[] ThemeCommonOptions = ["journal", "fixture-root"];

    private static readonly string[] ThemeTokenOptions = ["handback-token"];

    private static int RunTheme(IReadOnlyList<string> args, TextWriter output, TextReader input)
    {
        if (args.Count < 2)
        {
            return Fail(output, ExitInvalidArguments, "A theme action is required.");
        }

        if (!TryParsePairs(args, 2, out Dictionary<string, string> values, out string? error))
        {
            return Fail(output, ExitInvalidArguments, error!);
        }

        return args[1] switch
        {
            "inspect" => ThemeInspect(values, output, input),
            "apply" => ThemeApply(values, output),
            "restore" => ThemeRestore(values, output, input),
            _ => Fail(output, ExitInvalidArguments, "The theme action is not supported."),
        };
    }

    private static bool TryReadThemeJournal(Dictionary<string, string> values, out string journal)
    {
        journal = string.Empty;
        if (!values.TryGetValue("journal", out string? text)
            || string.IsNullOrWhiteSpace(text)
            || !Path.IsPathFullyQualified(text))
        {
            return false;
        }

        journal = text;
        return true;
    }

    /// <summary>
    /// Reads the three identity facts, plus an optional executable path. The executable is not a
    /// fourth required option: a caller that does not state it is compared on the empty value.
    /// </summary>
    private static bool TryReadThemeOwner(
        Dictionary<string, string> values,
        bool required,
        out ThemeOwnerIdentity? owner,
        out string? error)
    {
        owner = null;
        error = null;
        int present = ThemeOwnerRequired.Count(values.ContainsKey);
        bool executablePresent = values.ContainsKey("owner-exe");
        if (present == 0)
        {
            if (required || executablePresent)
            {
                error = "The theme owner is required.";
                return false;
            }

            return true;
        }

        if (present != ThemeOwnerRequired.Length)
        {
            error = "The theme owner options are incomplete.";
            return false;
        }

        if (!int.TryParse(values["owner-pid"], NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            || pid <= 0
            || !long.TryParse(values["owner-start-utc-ticks"], NumberStyles.None,
                CultureInfo.InvariantCulture, out long ticks)
            || ticks <= 0)
        {
            error = "The theme owner options are invalid.";
            return false;
        }

        string context = values["owner-context"];
        if (context.Length is 0 or > 128
            || !context.All(character => char.IsLetterOrDigit(character) || character == '-'))
        {
            error = "The theme owner options are invalid.";
            return false;
        }

        string? executable = values.TryGetValue("owner-exe", out string? text) ? text : null;
        if (executable is not null && !Path.IsPathFullyQualified(executable))
        {
            error = "The theme owner options are invalid.";
            return false;
        }

        // Kind is a shape label, not an identity proof: the coordinator probes the journalled
        // owner and compares every field. The support process never declares its own identity.
        owner = new ThemeOwnerIdentity(pid, ticks, context, ThemeOwnerKind.HarnessRunner, executable);
        return true;
    }

    private static bool TryReadThemeBindings(
        Dictionary<string, string> values,
        bool required,
        out ThemeBindings? bindings,
        out string? error)
    {
        bindings = null;
        error = null;
        int present = ThemeBindingOptions.Count(values.ContainsKey);
        if (present == 0)
        {
            if (required)
            {
                error = "The theme bindings are required.";
                return false;
            }

            return true;
        }

        if (present != ThemeBindingOptions.Length)
        {
            error = "The theme binding options are incomplete.";
            return false;
        }

        if (!TryReadThemeBinding(values, "product", out ThemeBinding? product, out error)
            || !TryReadThemeBinding(values, "toolkit", out ThemeBinding? toolkit, out error))
        {
            return false;
        }

        bindings = new ThemeBindings(product!, toolkit!);
        return true;
    }

    private static bool TryReadThemeBinding(
        Dictionary<string, string> values,
        string prefix,
        out ThemeBinding? binding,
        out string? error)
    {
        binding = null;
        error = null;
        string digest = values[prefix + "-sha256"];
        if (values[prefix + "-hash-kind"] is not "sha256"
            || !IsSha256Hex(digest)
            || !int.TryParse(values[prefix + "-members"], NumberStyles.None,
                CultureInfo.InvariantCulture, out int count)
            || count is < 1 or > 1000000)
        {
            error = "The theme binding options are invalid.";
            return false;
        }

        // The count stays a count. The names are the members the digest actually covers; the
        // count is only the cross-check that both describe the same set.
        string[] names = values[prefix + "-member-names"].Split(';', StringSplitOptions.None);
        if (names.Length != count
            || names.Any(name => string.IsNullOrWhiteSpace(name)
                || name.Any(char.IsWhiteSpace)
                || name.Contains(';', StringComparison.Ordinal)))
        {
            error = "The theme binding member names do not match the declared count.";
            return false;
        }

        binding = new ThemeBinding("sha256", names, digest);
        return true;
    }

    private static bool TryReadThemeDisposition(
        Dictionary<string, string> values,
        out ThemeRestoreDisposition disposition)
    {
        disposition = ThemeRestoreDisposition.StartupRecovery;
        if (!values.TryGetValue("disposition", out string? text))
        {
            return true;
        }

        if (text == "startup")
        {
            return true;
        }

        if (text == "handback")
        {
            disposition = ThemeRestoreDisposition.Handback;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the hand-back receipt. The only accepted value is the sentinel that means
    /// "read standard input": argv is process-visible and is the first thing a command record
    /// copies, so a receipt is never accepted there.
    /// </summary>
    private static bool TryReadThemeToken(
        Dictionary<string, string> values,
        TextReader input,
        out string token,
        out string? error)
    {
        token = string.Empty;
        error = null;
        if (!values.TryGetValue("handback-token", out string? raw))
        {
            return true;
        }

        if (!string.Equals(raw, "-", StringComparison.Ordinal))
        {
            error = "The hand-back receipt is read from standard input only.";
            return false;
        }

        token = (input.ReadLine() ?? string.Empty).Trim();
        if (token.Length > 0 && !IsSha256Hex(token))
        {
            error = "The hand-back receipt is not a receipt.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the acceptance-only adapter root. Every check happens before the fixture, the
    /// journal or any adapter is touched; the input must already be absolute, and every path is
    /// normalised before the boundary comparison so a "..\" segment cannot escape.
    /// </summary>
    private static bool TryReadThemeFixtureRoot(
        Dictionary<string, string> values,
        string journal,
        out string? root,
        out string? error)
    {
        root = null;
        error = null;
        if (!values.TryGetValue("fixture-root", out string? text))
        {
            return true;
        }

        if (!Path.IsPathFullyQualified(text))
        {
            error = "The fixture root must be an absolute path.";
            return false;
        }

        if (!ThemeFixtureAdapters.Licensed)
        {
            error = "The fixture adapters are not licensed for this process.";
            return false;
        }

        string full;
        string temp;
        string journalFull;
        try
        {
            full = Path.GetFullPath(text).TrimEnd(Path.DirectorySeparatorChar);
            temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            journalFull = Path.GetFullPath(journal);
        }
        catch (ArgumentException)
        {
            error = "The fixture root is invalid.";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "The fixture root is invalid.";
            return false;
        }

        string leaf = Path.GetFileName(full);
        string separator = Path.DirectorySeparatorChar.ToString();
        bool underTemp = full.StartsWith(temp + separator, StringComparison.OrdinalIgnoreCase);
        string journalDirectory = (Path.GetDirectoryName(journalFull) ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar);
        bool journalInside = string.Equals(journalDirectory, full, StringComparison.OrdinalIgnoreCase);

        if (!underTemp
            || !leaf.StartsWith("tracefold-theme-fixture-", StringComparison.OrdinalIgnoreCase)
            || !journalInside
            || !Directory.Exists(full)
            || HasReparsePoint(full)
            || HasReparsePointInAncestors(full, temp)
            || (File.Exists(journalFull) && HasReparsePoint(journalFull)))
        {
            error = "The fixture root is not an acceptance fixture root.";
            return false;
        }

        root = full;
        return true;
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool HasReparsePointInAncestors(string path, string stopAt)
    {
        DirectoryInfo? current = new DirectoryInfo(path).Parent;
        while (current is not null)
        {
            if (HasReparsePoint(current.FullName))
            {
                return true;
            }

            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), stopAt,
                StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = current.Parent;
        }

        return false;
    }

    private static (IThemeRegistry, IThemeChangeBroadcaster, IThemeOwnerInspector)
        CreateThemeAdapters(string? fixtureRoot) => fixtureRoot is null
            ? (new WindowsThemeRegistry(), new WindowsThemeChangeBroadcaster(),
                new WindowsThemeOwnerInspector())
            : ThemeFixtureAdapters.Create(fixtureRoot);

    private static IThemeOwnerInspector CreateThemeInspector(string? fixtureRoot) =>
        fixtureRoot is null ? new WindowsThemeOwnerInspector() : ThemeFixtureAdapters.CreateInspector(fixtureRoot);

    private static int ThemeInspect(Dictionary<string, string> values, TextWriter output, TextReader input)
    {
        string[] allowed =
        [
            .. ThemeCommonOptions, .. ThemeTokenOptions, .. ThemeOwnerRequired,
            "owner-exe", .. ThemeBindingOptions
        ];
        string? error = null;
        if (!OnlyOptions(values, allowed)
            || !TryReadThemeJournal(values, out string journal)
            || !TryReadThemeOwner(values, required: false, out ThemeOwnerIdentity? owner, out error)
            || !TryReadThemeBindings(values, required: false, out ThemeBindings? bindings, out error)
            || !TryReadThemeFixtureRoot(values, journal, out string? fixtureRoot, out error)
            || !TryReadThemeToken(values, input, out string token, out error))
        {
            return Fail(output, ExitInvalidArguments, error ?? "The theme arguments are invalid.");
        }

        ThemeJournalInspection inspection;
        try
        {
            inspection = WindowsAppThemeCoordinator.Inspect(
                journal,
                new SystemThemeJournalStore(),
                CreateThemeInspector(fixtureRoot),
                token.Length > 0 ? token : null,
                owner);
        }
        catch (ThemeFixtureException fixtureError)
        {
            return Fail(output, ExitInvalidArguments, fixtureError.Message);
        }

        var payload = new
        {
            action = "inspect",
            adapters = fixtureRoot is null ? "production" : "fixture",
            journalState = MapJournalState(inspection),
            ownerState = inspection.JournalPresent
                ? ProbeState(inspection.OwnerProbe?.State ?? ThemeOwnerState.Unknown)
                : "none",
            journalPresent = inspection.JournalPresent,
            journalReadable = inspection.JournalReadable,
            checksumValid = inspection.JournalChecksumValid,
            startupRecoveryAllowed = inspection.StartupRecoveryAllowed,
            handbackAccepted = inspection.HandbackAccepted,
            handbackRejection = inspection.HandbackRejection is null
                ? null
                : WindowsAppThemeCoordinator.ReasonCode(inspection.HandbackRejection.Value),
            rejection = WindowsAppThemeCoordinator.ReasonCode(inspection.Rejection),
            owner = inspection.Owner is null ? null : OwnerPayload(inspection.Owner),
            requestBinding = bindings is null ? null : BindingPayload(bindings),
        };
        output.WriteLine(JsonSerializer.Serialize(payload));
        return ExitSuccess;
    }

    private static int ThemeApply(Dictionary<string, string> values, TextWriter output)
    {
        string[] allowed =
        [
            .. ThemeCommonOptions, .. ThemeOwnerRequired, "owner-exe", .. ThemeBindingOptions, "mode"
        ];
        string? error = null;
        if (!OnlyOptions(values, allowed)
            || !TryReadThemeJournal(values, out string journal)
            || !values.TryGetValue("mode", out string? modeText)
            || modeText is not ("light" or "dark"))
        {
            return Fail(output, ExitInvalidArguments, "The theme arguments are invalid.");
        }

        if (!TryReadThemeOwner(values, required: true, out ThemeOwnerIdentity? owner, out error)
            || !TryReadThemeBindings(values, required: true, out ThemeBindings? bindings, out error)
            || !TryReadThemeFixtureRoot(values, journal, out string? fixtureRoot, out error))
        {
            return Fail(output, ExitInvalidArguments, error ?? "The theme arguments are invalid.");
        }

        IThemeRegistry registry;
        IThemeChangeBroadcaster broadcaster;
        try
        {
            (registry, broadcaster, _) = CreateThemeAdapters(fixtureRoot);
        }
        catch (ThemeFixtureException fixtureError)
        {
            return Fail(output, ExitInvalidArguments, fixtureError.Message);
        }

        ThemeApplyResult result = WindowsAppThemeCoordinator.Apply(
            new ThemeApplyRequest(
                WindowsAppThemeCoordinator.ApprovedKeyPath,
                WindowsAppThemeCoordinator.ApprovedValueName,
                modeText == "light" ? ThemeMode.Light : ThemeMode.Dark,
                journal,
                owner!,
                bindings!),
            registry,
            new SystemThemeJournalStore(),
            broadcaster);

        var payload = new
        {
            action = "apply",
            adapters = fixtureRoot is null ? "production" : "fixture",
            applied = result.Applied,
            mode = modeText,
            // "pending" is a claim about a journal this call wrote. Absence is never claimed
            // here: a failed call does not prove the journal was not there or was not written.
            journalState = result.JournalWritten
                ? "pending"
                : result.Rejection == ThemeRejection.JournalAlreadyPresent ? "present" : "unknown",
            journalWritten = result.JournalWritten,
            journalRetained = result.JournalRetained,
            registryWritten = result.RegistryWritten,
            readBackVerified = result.AppliedReadBackVerified,
            broadcast = new { sent = result.BroadcastSucceeded },
            original = SnapshotPayload(result.Original),
            appliedReadBack = SnapshotPayload(result.AppliedReadBack),
            owner = OwnerPayload(owner!),
            binding = BindingPayload(bindings!),
            rejection = WindowsAppThemeCoordinator.ReasonCode(result.Rejection),
            rejectionCode = result.Rejection.ToString(),
            // Capability, not an identifier. Non-empty exactly when a journal was persisted.
            handbackToken = result.HandbackToken,
        };
        output.WriteLine(JsonSerializer.Serialize(payload));
        return ExitSuccess;
    }

    private static int ThemeRestore(Dictionary<string, string> values, TextWriter output, TextReader input)
    {
        string[] allowed =
        [
            .. ThemeCommonOptions, .. ThemeTokenOptions, .. ThemeOwnerRequired,
            "owner-exe", .. ThemeBindingOptions, "disposition"
        ];
        string? error = null;
        if (!OnlyOptions(values, allowed)
            || !TryReadThemeJournal(values, out string journal)
            || !TryReadThemeDisposition(values, out ThemeRestoreDisposition disposition))
        {
            return Fail(output, ExitInvalidArguments, "The theme arguments are invalid.");
        }

        bool handback = disposition == ThemeRestoreDisposition.Handback;
        if (!TryReadThemeOwner(values, required: handback, out ThemeOwnerIdentity? owner, out error)
            // A hand-back states the binding it expects. The component decides it against the
            // journal it is about to consume and refuses before touching the registry; omitting
            // it here would leave that decision to a post-hoc comparison.
            || !TryReadThemeBindings(values, required: handback, out ThemeBindings? bindings, out error)
            || !TryReadThemeFixtureRoot(values, journal, out string? fixtureRoot, out error)
            || !TryReadThemeToken(values, input, out string token, out error))
        {
            return Fail(output, ExitInvalidArguments, error ?? "The theme arguments are invalid.");
        }

        ThemeBindings? expectedBindings = handback ? bindings : null;
        IThemeRegistry registry;
        IThemeChangeBroadcaster broadcaster;
        IThemeOwnerInspector inspector;
        try
        {
            (registry, broadcaster, inspector) = CreateThemeAdapters(fixtureRoot);
        }
        catch (ThemeFixtureException fixtureError)
        {
            return Fail(output, ExitInvalidArguments, fixtureError.Message);
        }

        var journals = new SystemThemeJournalStore();
        ThemeRestoreResult result = WindowsAppThemeCoordinator.Restore(
            new ThemeRestoreRequest(
                WindowsAppThemeCoordinator.ApprovedKeyPath,
                WindowsAppThemeCoordinator.ApprovedValueName,
                journal,
                disposition,
                handback ? owner : null,
                handback && token.Length > 0 ? token : null,
                expectedBindings),
            registry,
            journals,
            broadcaster,
            inspector);

        bool noAction = result.Action == ThemeRestoreAction.NoJournalNoAction;
        bool? bindingMatches = expectedBindings is null || result.Bindings is null
            ? null
            : WindowsAppThemeCoordinator.BindingsEqual(expectedBindings, result.Bindings);
        bool readBackMatches = result.Original is not null
            && WindowsAppThemeCoordinator.SnapshotMatches(result.ReadBack, result.Original);
        var payload = new
        {
            action = "restore",
            adapters = fixtureRoot is null ? "production" : "fixture",
            disposition = handback ? "handback" : "startup",
            restored = result.Restored,
            noAction,
            journalState = noAction || result.JournalRemoved
                ? "absent"
                : result.JournalPresent ? "present" : "unknown",
            journalPresent = result.JournalPresent,
            journalRemoved = result.JournalRemoved,
            journalChecksumValid = result.JournalChecksumValid,
            owner = result.Owner is null ? null : OwnerPayload(result.Owner),
            ownerState = noAction
                ? "none"
                : ProbeState(result.OwnerProbe?.State ?? ThemeOwnerState.Unknown),
            ownerProbe = result.OwnerProbe is null
                ? null
                : new
                {
                    state = ProbeState(result.OwnerProbe.State),
                    identityReadable = result.OwnerProbe.IdentityReadable,
                    detail = result.OwnerProbe.Detail,
                },
            registryRestored = result.RegistryRestored,
            readBackVerified = result.ReadBackVerified,
            broadcast = new { sent = result.BroadcastSucceeded },
            // Both triples come from the journal the restore actually consumed.
            original = SnapshotPayload(result.Original),
            readback = result.ReadBack is null
                ? null
                : new
                {
                    present = result.ReadBack.Present,
                    valueKind = result.ReadBack.Present ? result.ReadBack.Kind.ToString() : null,
                    bytes = result.ReadBack.Present && result.ReadBack.Bytes is not null
                        ? Convert.ToHexString(result.ReadBack.Bytes)
                        : null,
                    matchesOriginal = readBackMatches,
                },
            binding = result.Bindings is null ? null : BindingPayload(result.Bindings),
            requestBinding = expectedBindings is null ? null : BindingPayload(expectedBindings),
            bindingMatchesJournal = bindingMatches,
            rejection = WindowsAppThemeCoordinator.ReasonCode(result.Rejection),
            rejectionCode = result.Rejection.ToString(),
        };
        output.WriteLine(JsonSerializer.Serialize(payload));
        return ExitSuccess;
    }

    private static string MapJournalState(ThemeJournalInspection inspection)
    {
        if (!inspection.JournalPresent)
        {
            return "absent";
        }

        if (!inspection.JournalReadable || !inspection.JournalChecksumValid)
        {
            return "unknown";
        }

        if (inspection.StartupRecoveryAllowed)
        {
            return "restore-required";
        }

        return inspection.Rejection == ThemeRejection.OwnerLive ? "foreign-live" : "unknown";
    }

    private static string ProbeState(ThemeOwnerState state) => state switch
    {
        ThemeOwnerState.Live => "live",
        ThemeOwnerState.Dead => "dead",
        ThemeOwnerState.PidReused => "pid-reused",
        _ => "unknown",
    };

    private static object OwnerPayload(ThemeOwnerIdentity owner) => new
    {
        pid = owner.ProcessId,
        startUtcTicks = owner.StartUtcTicks,
        context = owner.Context,
        kind = owner.Kind.ToString(),
        // executablePath is deliberately not echoed: the journal keeps it for the probe, the
        // response and the report do not need a machine path.
    };

    private static object? SnapshotPayload(ThemeRegistrySnapshot? snapshot) => snapshot is null
        ? null
        : new
        {
            present = snapshot.Present,
            valueKind = snapshot.Present ? snapshot.Kind.ToString() : null,
            bytes = snapshot.Present && snapshot.Bytes is not null
                ? Convert.ToHexString(snapshot.Bytes)
                : null,
        };

    private static object BindingPayload(ThemeBindings bindings) => new
    {
        // The flat shape is the consumer contract VisualGate already validates; the member-name
        // arrays are additive and say which members the digest actually covers.
        productHashKind = bindings.Product.HashKind,
        productMembers = bindings.Product.Members.Count,
        productMemberNames = bindings.Product.Members.ToArray(),
        productSha256 = bindings.Product.Digest,
        toolkitHashKind = bindings.Toolkit.HashKind,
        toolkitMembers = bindings.Toolkit.Members.Count,
        toolkitMemberNames = bindings.Toolkit.Members.ToArray(),
        toolkitSha256 = bindings.Toolkit.Digest,
    };

    private static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string? Single(List<KeyValuePair<string, string>> pairs, string name)
    {
        string[] values = [.. pairs.Where(pair => pair.Key == name).Select(pair => pair.Value)];
        return values.Length == 1 ? values[0] : null;
    }

    /// <summary>
    /// Named-option parsing that permits a repeated option, which the pattern list needs.
    /// </summary>
    private static bool TryParseNamedPairs(
        IReadOnlyList<string> args,
        int start,
        out List<KeyValuePair<string, string>> pairs)
    {
        pairs = [];
        for (int index = start; index < args.Count; index++)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= args.Count
                || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            pairs.Add(new KeyValuePair<string, string>(token[2..], args[index + 1]));
            index++;
        }

        return true;
    }

    private static bool TryReadProcess(int processId, out Process? process, out string? executable,
        out long startTicks)
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
