using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

internal sealed class FixtureState
{
    public string Page { get; set; } = "inbox";
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 720;
    public bool InboxRow { get; set; } = true;
    public bool LibraryRow { get; set; }
    public bool BatchSelected { get; set; }
    public bool ProcessingQueued { get; set; }
    public bool ProcessingReady { get; set; }
    public bool DiagnosticsTab { get; set; }
    public bool DiagnosticsRunning { get; set; }
    public bool ValidationError { get; set; }
    public bool DrawerOpen { get; set; }
    public bool TaxonomyInputVisible { get; set; }
    public string Focus { get; set; } = "InboxRefreshButton";
    public string PreviousFocus { get; set; } = "InboxGrid";
    public string Selected { get; set; } = "";
    public string OpenDialog { get; set; } = "";
    public string StatusText { get; set; } = "";
    public string TaxonomyInput { get; set; } = "";
    public List<string> Taxonomies { get; set; } = new List<string>();
    public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

internal sealed class UiNode
{
    public string Selector { get; set; }
    public string Type { get; set; }
    public string AutomationId { get; set; }
    public string ClassName { get; set; }
    public string Name { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsOffscreen { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Focus { get; set; }
    public List<UiNode> Children { get; set; }
}

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static int Main(string[] args)
    {
        try
        {
            string statePath = RequiredEnvironment("TRACEFOLD_VISUAL_UI_STATE");
            FixtureState state = LoadState(statePath);
            if (args.Length == 0)
            {
                Console.Error.WriteLine("missing command");
                return 2;
            }

            if (string.Equals(args[0], "ui", StringComparison.Ordinal))
            {
                return RunUi(args, state, statePath);
            }
            if (string.Equals(args[0], "window", StringComparison.Ordinal))
            {
                return RunWindow(args, state, statePath);
            }
            if (string.Equals(args[0], "native", StringComparison.Ordinal))
            {
                return RunNative(args);
            }
            if (string.Equals(args[0], "root", StringComparison.Ordinal))
            {
                return RunRoot(args);
            }
            if (string.Equals(args[0], "theme", StringComparison.Ordinal))
            {
                return RunTheme(args);
            }

            Console.Error.WriteLine("unsupported support command");
            return 3;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 3;
        }
    }

    private static int RunUi(string[] args, FixtureState state, string statePath)
    {
        if (args.Length < 2)
        {
            Log("ui", "missing-action", "", "", "");
            return 2;
        }

        string action = args[1];
        Dictionary<string, string> values = ReadOptions(args, 2);
        string target = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : "";
        Log("ui", action, target, values.TryGetValue("property", out string property) ? property : "",
            values.TryGetValue("value", out string expected) ? expected : "");

        switch (action)
        {
            case "inspect":
                Emit(new Dictionary<string, object>
                {
                    ["windows"] = new object[]
                    {
                        new Dictionary<string, object> { ["elements"] = BuildTree(state) }
                    }
                });
                return 0;

            case "search":
                UiNode match = FindNode(BuildTree(state), target);
                if (match == null)
                {
                    Console.Out.Write("{\"matchCount\":0,\"hasMore\":false,\"matches\":[]}");
                    return 0;
                }
                Emit(new Dictionary<string, object>
                {
                    ["matchCount"] = 1,
                    ["hasMore"] = false,
                    ["matches"] = new object[] { match }
                });
                return 0;

            case "get-property":
                UiNode propertyNode = FindNode(BuildTree(state), target);
                if (propertyNode == null || !values.TryGetValue("property", out string propertyName))
                {
                    Console.Error.WriteLine("element or property not found");
                    return 4;
                }
                Emit(new Dictionary<string, object>
                {
                    ["elementId"] = target,
                    ["properties"] = new Dictionary<string, object>
                    {
                        [propertyName] = PropertyValue(propertyNode, propertyName)
                    }
                });
                return 0;

            case "wait-for":
                Dictionary<string, object> wait = WaitFor(state, statePath, target, values);
                Emit(wait);
                return 0;

            case "get-value":
                string text = string.Equals(target, "SettingsStatusMessage", StringComparison.Ordinal)
                    ? state.StatusText
                    : (state.Values.TryGetValue(target, out string stored) ? stored : "");
                Emit(new Dictionary<string, object> { ["text"] = text });
                return 0;

            case "set-value":
                if (string.IsNullOrWhiteSpace(target) || args.Length < 4)
                {
                    Console.Error.WriteLine("set-value needs a target and value");
                    return 4;
                }
                state.Values[target] = args[3];
                if (string.Equals(target, "TaxonomyNameInput", StringComparison.Ordinal))
                {
                    state.TaxonomyInput = args[3];
                }
                SaveState(statePath, state);
                Emit(new Dictionary<string, object> { ["result"] = "set" });
                return 0;

            case "invoke":
                if (!InvokeTarget(state, target))
                {
                    Console.Error.WriteLine("element not found: " + target);
                    return 4;
                }
                SaveState(statePath, state);
                Emit(new Dictionary<string, object> { ["result"] = "invoked" });
                return 0;

            case "scroll-into-view":
                if (FindNode(BuildTree(state), target) == null)
                {
                    Console.Error.WriteLine("element not found: " + target);
                    return 4;
                }
                Emit(new Dictionary<string, object> { ["result"] = "scrolled" });
                return 0;

            case "send-keys":
                if (!SendKeys(state, target, values))
                {
                    Console.Error.WriteLine("key dispatch rejected: " + target);
                    return 4;
                }
                SaveState(statePath, state);
                Emit(new Dictionary<string, object> { ["result"] = "sent" });
                return 0;

            case "screenshot":
                string output = values.TryGetValue("output", out string outputValue) ? outputValue : "";
                if (string.IsNullOrWhiteSpace(output))
                {
                    Console.Error.WriteLine("screenshot needs --output");
                    return 4;
                }
                string directory = Path.GetDirectoryName(Path.GetFullPath(output));
                if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); }
                (byte red, byte green, byte blue) = ScreenshotColor();
                File.WriteAllBytes(output, CreatePng(Math.Max(1, state.Width), Math.Max(1, state.Height), red, green, blue));
                Emit(new Dictionary<string, object> { ["captured"] = true, ["path"] = output });
                return 0;

            default:
                Console.Error.WriteLine("unsupported ui command: " + action);
                return 2;
        }
    }

    private static int RunWindow(string[] args, FixtureState state, string statePath)
    {
        if (args.Length < 2 || !string.Equals(args[1], "resize", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("unsupported window command");
            return 2;
        }

        Dictionary<string, string> values = ReadOptions(args, 2);
        if (!int.TryParse(values.TryGetValue("width-dip", out string widthText) ? widthText : "", out int width) ||
            !int.TryParse(values.TryGetValue("height-dip", out string heightText) ? heightText : "", out int height) ||
            width <= 0 || height <= 0)
        {
            Console.Error.WriteLine("window resize needs positive dimensions");
            return 2;
        }

        state.Width = width;
        state.Height = height;
        SaveState(statePath, state);
        Log("window", "resize", width + "x" + height, "", "");
        Emit(new Dictionary<string, object>
        {
            ["processId"] = values.TryGetValue("process-id", out string processId) ? processId : "",
            ["widthPixels"] = width,
            ["heightPixels"] = height,
            ["clientWidthPixels"] = width,
            ["clientHeightPixels"] = height
        });
        return 0;
    }

    private static int RunNative(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[1], "lookup", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("unsupported native command");
            return 2;
        }

        List<string> patterns = new List<string>();
        Dictionary<string, string> values = ReadOptions(args, 2, patterns);
        List<object> patternFacts = new List<object>();
        foreach (string pattern in patterns)
        {
            patternFacts.Add(new Dictionary<string, object>
            {
                ["name"] = pattern,
                ["available"] = true
            });
        }
        Log("native", "lookup", values.TryGetValue("automation-id", out string automationId) ? automationId : "", "", "");
        Emit(new Dictionary<string, object>
        {
            ["automationId"] = values.TryGetValue("automation-id", out string id) ? id : "",
            ["patterns"] = patternFacts
        });
        return 0;
    }

    private static int RunRoot(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("root command is incomplete");
            return 2;
        }

        Dictionary<string, string> values = ReadOptions(args, 2);
        string action = args[1];
        Log("root", action, values.TryGetValue("root", out string loggedRoot) ? loggedRoot : "", "", "");
        if (string.Equals(action, "create", StringComparison.Ordinal))
        {
            if (!values.TryGetValue("parent", out string parent) ||
                !values.TryGetValue("run-id", out string runIdText) ||
                !values.TryGetValue("token", out string token) ||
                !Guid.TryParseExact(runIdText, "D", out Guid runId))
            {
                Console.Error.WriteLine("root create needs parent, run-id and token");
                return 3;
            }
            string root = Path.Combine(parent, "TraceFold-UIA-" + runId.ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, ".owned-token"), token, new UTF8Encoding(false));
            Emit(new Dictionary<string, object> { ["root"] = root });
            return 0;
        }
        if (string.Equals(action, "validate", StringComparison.Ordinal))
        {
            string root = values.TryGetValue("root", out string rootValue) ? rootValue : "";
            string token = values.TryGetValue("token", out string tokenValue) ? tokenValue : "";
            string marker = Path.Combine(root, ".owned-token");
            bool owned = File.Exists(marker) && string.Equals(File.ReadAllText(marker), token, StringComparison.Ordinal);
            Emit(new Dictionary<string, object> { ["owned"] = owned });
            return owned ? 0 : 3;
        }
        if (string.Equals(action, "delete", StringComparison.Ordinal))
        {
            string root = values.TryGetValue("root", out string rootValue) ? rootValue : "";
            string token = values.TryGetValue("token", out string tokenValue) ? tokenValue : "";
            string marker = Path.Combine(root, ".owned-token");
            if (!File.Exists(marker) || !string.Equals(File.ReadAllText(marker), token, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("root ownership token mismatch");
                return 3;
            }
            // The real Runner may delete the owned root even after a scenario fails. Preserve
            // its exact ledger and all runtime evidence before this non-theme terminal deletes it.
            string snapshotParent = RequiredEnvironment("TRACEFOLD_VISUAL_ROOT_SNAPSHOTS");
            string snapshot = Path.Combine(snapshotParent, Path.GetFileName(Path.GetFullPath(root)));
            if (Directory.Exists(snapshot) || File.Exists(snapshot))
            {
                Console.Error.WriteLine("owned root snapshot already exists");
                return 3;
            }
            SnapshotDirectory(root, snapshot);
            Log("root", "snapshot", snapshot, "", "");
            Directory.Delete(root, true);
            Emit(new Dictionary<string, object> { ["deleted"] = true });
            return 0;
        }

        Console.Error.WriteLine("unsupported root command");
        return 3;
    }

    private static void SnapshotDirectory(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("owned root snapshot refuses a reparse point");
        }
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("owned root snapshot refuses a reparse point");
            }
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            SnapshotDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static int RunTheme(string[] args)
    {
        string action = args.Length > 1 ? args[1] : "";
        string mode = ReadOptions(args, 2).TryGetValue("mode", out string modeValue) ? modeValue : "";
        Log("theme", action, mode, "", "");

        string realSupport = Environment.GetEnvironmentVariable("TRACEFOLD_VISUAL_REAL_SUPPORT");
        if (string.IsNullOrWhiteSpace(realSupport))
        {
            realSupport = Path.Combine(AppContext.BaseDirectory, "RealSupport.exe");
        }
        if (!File.Exists(realSupport))
        {
            Console.Error.WriteLine("real support fixture is missing");
            return 3;
        }
        if (!args.Any(item => string.Equals(item, "--fixture-root", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine("theme command reached the adapter without --fixture-root");
            return 3;
        }
        if (!string.Equals(Environment.GetEnvironmentVariable("TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE"), "1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("theme fixture process licence is missing");
            return 3;
        }

        string standardInput = Console.In.ReadToEnd();
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = realSupport,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (string argument in args) { start.ArgumentList.Add(argument); }
        using Process process = new Process { StartInfo = start };
        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(standardInput);
        process.StandardInput.Close();
        if (!process.WaitForExit(60000))
        {
            try { process.Kill(true); } catch { try { process.Kill(); } catch { } }
            Console.Error.WriteLine("real support fixture did not finish");
            return 3;
        }
        Dictionary<string, object> termination = WaitTasks(process, stdout, stderr);
        if (!(bool) termination["streamsCompleted"])
        {
            Console.Error.WriteLine("real support fixture output did not close");
            return 3;
        }
        Console.Out.Write(stdout.GetAwaiter().GetResult());
        Console.Error.Write(stderr.GetAwaiter().GetResult());
        return process.ExitCode;
    }

    private static Dictionary<string, object> WaitFor(
        FixtureState state,
        string statePath,
        string target,
        Dictionary<string, string> values)
    {
        bool gone = values.ContainsKey("gone");
        bool transitioned = false;
        if (!gone &&
            string.Equals(target, "DiagnosticsRefreshButton", StringComparison.Ordinal) &&
            values.TryGetValue("property", out string transitionProperty) &&
            string.Equals(transitionProperty, "IsEnabled", StringComparison.Ordinal) &&
            values.TryGetValue("value", out string transitionValue) &&
            string.Equals(transitionValue, "True", StringComparison.OrdinalIgnoreCase) &&
            state.DiagnosticsRunning)
        {
            state.DiagnosticsRunning = false;
            transitioned = true;
            SaveState(statePath, state);
        }

        UiNode node = FindNode(BuildTree(state), target);
        bool found;
        if (gone)
        {
            found = node == null || node.IsOffscreen;
        }
        else if (node == null)
        {
            found = false;
        }
        else if (values.TryGetValue("property", out string property))
        {
            string actual = PropertyValue(node, property);
            found = !values.TryGetValue("value", out string expected) ||
                string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            found = !node.IsOffscreen;
        }
        return new Dictionary<string, object>
        {
            ["found"] = found,
            ["automationId"] = target,
            ["transitioned"] = transitioned
        };
    }

    private static bool InvokeTarget(FixtureState state, string target)
    {
        if (target == "NavInbox" || target == "NavLibrary" || target == "NavTaxonomy" || target == "NavSettings")
        {
            state.Page = target.Substring(3).ToLowerInvariant();
            state.OpenDialog = "";
            state.DrawerOpen = false;
            state.Focus = target;
            return true;
        }
        if (target == "row-inbox-paired")
        {
            state.Selected = "inbox";
            state.BatchSelected = false;
            state.Focus = target;
            return state.InboxRow;
        }
        if (target == "row-library-paired")
        {
            state.Selected = "library";
            state.BatchSelected = false;
            state.Focus = target;
            return state.LibraryRow;
        }
        if (target.StartsWith("row-taxonomy-", StringComparison.Ordinal))
        {
            state.Selected = target.Substring("row-taxonomy-".Length);
            state.BatchSelected = false;
            state.Focus = target;
            return state.Taxonomies.Contains(state.Selected);
        }

        switch (target)
        {
            case "InboxRefreshButton":
                state.InboxRow = true;
                state.Focus = "InboxGrid";
                return true;
            case "InboxBatchMetadataButton":
                if (!state.BatchSelected || !state.InboxRow) { return false; }
                state.OpenDialog = "BatchMetadataDialog";
                state.Focus = "BatchMetadataScrollRoot";
                return true;
            case "InboxCancelSelectionButton":
                state.BatchSelected = false;
                state.Selected = "";
                state.OpenDialog = "";
                state.Focus = "InboxGrid";
                return true;
            case "InboxStartProcessingButton":
                if (state.Selected != "inbox" || !state.InboxRow) { return false; }
                state.ProcessingQueued = true;
                state.ProcessingReady = true;
                state.Focus = "InboxProcessingProgress";
                return true;
            case "IngestButton":
                if (!state.ProcessingReady || !state.InboxRow) { return false; }
                state.ProcessingQueued = false;
                state.ProcessingReady = false;
                if (state.LibraryRow)
                {
                    state.OpenDialog = "ConflictComparisonDialog";
                    state.Focus = "ConflictKeepBothButton";
                }
                else
                {
                    state.InboxRow = false;
                    state.LibraryRow = true;
                    state.Selected = "";
                    state.Focus = "InboxGrid";
                }
                return true;
            case "LibraryBatchEditButton":
                if (state.Selected != "library" || !state.LibraryRow) { return false; }
                state.OpenDialog = "LibraryBatchEditDialog";
                state.Focus = "LibraryBatchEditScrollRoot";
                return true;
            case "TaxonomyCreateButton":
                state.TaxonomyInputVisible = true;
                state.Focus = "TaxonomyNameInput";
                return true;
            case "PrimaryButton":
                if (!state.TaxonomyInputVisible || string.IsNullOrWhiteSpace(state.TaxonomyInput)) { return false; }
                if (!state.Taxonomies.Contains(state.TaxonomyInput)) { state.Taxonomies.Add(state.TaxonomyInput); }
                state.TaxonomyInputVisible = false;
                state.Focus = "TaxonomyTermList";
                return true;
            case "TaxonomyMergeButton":
                if (string.IsNullOrWhiteSpace(state.Selected) || !state.Taxonomies.Contains(state.Selected)) { return false; }
                state.OpenDialog = "TaxonomyMergeDialog";
                state.Focus = "TaxonomyMergeScrollRoot";
                return true;
            case "FolderSettingsTab":
                state.DiagnosticsTab = false;
                state.Focus = "InboxFolderPath";
                return true;
            case "SystemDiagnosticsTab":
                state.DiagnosticsTab = true;
                state.DiagnosticsRunning = false;
                state.Focus = "DiagnosticsRefreshButton";
                return true;
            case "DiagnosticsRefreshButton":
                if (state.DiagnosticsRunning) { return false; }
                state.DiagnosticsRunning = true;
                state.Focus = "DiagnosticsRefreshButton";
                return true;
            case "SaveFolderSettings":
                string inbox = state.Values.TryGetValue("InboxFolderPath", out string inboxValue) ? inboxValue : "";
                state.ValidationError = inbox.Contains("tracefold-acceptance-missing-folder", StringComparison.OrdinalIgnoreCase);
                state.StatusText = state.ValidationError ? "The selected folder does not exist." : ContractSavedStatus();
                state.Focus = state.ValidationError ? "SettingsValidationErrors" : "SettingsStatusMessage";
                return true;
            case "InboxInspectorOpenButton":
                if (state.Selected != "inbox") { return false; }
                state.DrawerOpen = true;
                state.Focus = "InboxInspectorHeading";
                return true;
            case "BatchMetadataScrollRoot":
            case "ConflictScrollRoot":
            case "LibraryBatchEditScrollRoot":
            case "TaxonomyMergeScrollRoot":
                state.Focus = target;
                return true;
            case "LibraryBatchCancelButton":
            case "TaxonomyMergeCancelButton":
                state.OpenDialog = "";
                state.Focus = state.Page == "library" ? "LibraryGrid" : "TaxonomyTermList";
                return true;
            case "ConflictKeepBothButton":
                state.OpenDialog = "";
                state.InboxRow = false;
                state.Selected = "";
                state.Focus = "InboxGrid";
                return true;
            default:
                return target.StartsWith("row-", StringComparison.Ordinal);
        }
    }

    private static bool SendKeys(FixtureState state, string key, Dictionary<string, string> values)
    {
        string target = values.TryGetValue("target", out string targetValue) ? targetValue : "";
        string normalized = key.ToLowerInvariant();
        if (normalized == "ctrl+a")
        {
            if (string.IsNullOrWhiteSpace(target)) { target = "InboxGrid"; }
            state.BatchSelected = true;
            state.Focus = target;
            return true;
        }
        if (normalized == "escape")
        {
            if (!string.IsNullOrEmpty(state.OpenDialog))
            {
                if (state.OpenDialog == "ConflictComparisonDialog")
                {
                    state.InboxRow = false;
                    state.Selected = "";
                }
                state.OpenDialog = "";
                state.Focus = state.Page + "-after-dialog";
                return true;
            }
            if (state.DrawerOpen)
            {
                state.DrawerOpen = false;
                state.Focus = "row-inbox-paired";
                return true;
            }
            state.Focus = state.Page switch
            {
                "library" => "LibraryGrid",
                "taxonomy" => "TaxonomyTermList",
                "settings" => "SettingsTabs",
                _ => "InboxGrid"
            };
            return true;
        }
        if (normalized == "tab")
        {
            state.PreviousFocus = state.Focus;
            state.Focus = target switch
            {
                "InboxGrid" => "InboxSearchBox",
                "LibraryGrid" => "LibrarySearchBox",
                "TaxonomyTermList" => "TaxonomySearchBox",
                "SettingsTabs" => "FolderSettingsTab",
                _ => "NavInbox"
            };
            return true;
        }
        if (normalized == "shift+tab")
        {
            state.Focus = string.IsNullOrWhiteSpace(state.PreviousFocus) ? "InboxGrid" : state.PreviousFocus;
            return true;
        }
        if (!string.IsNullOrWhiteSpace(target)) { state.Focus = target; }
        return true;
    }

    private static List<UiNode> BuildTree(FixtureState state)
    {
        int width = Math.Max(320, state.Width);
        int height = Math.Max(240, state.Height);
        int navWidth = 160;
        int contentX = navWidth;
        int contentWidth = width - contentX;
        List<UiNode> elements = new List<UiNode>
        {
            Node("TraceFoldMainWindow", "TraceFold", "Window", 0, 0, width, height, true, false, state.Focus),
            Node("NavInbox", "Inbox", "NavigationViewItem", 0, 0, navWidth, 40, true, false, state.Focus),
            Node("NavLibrary", "Library", "NavigationViewItem", 0, 40, navWidth, 40, true, false, state.Focus),
            Node("NavTaxonomy", "Taxonomy", "NavigationViewItem", 0, 80, navWidth, 40, true, false, state.Focus),
            Node("NavSettings", "Settings", "NavigationViewItem", 0, 120, navWidth, 40, true, false, state.Focus)
        };

        switch (state.Page)
        {
            case "library":
                elements.AddRange(LibraryPage(state, contentX, contentWidth, height));
                break;
            case "taxonomy":
                elements.AddRange(TaxonomyPage(state, contentX, contentWidth, height));
                break;
            case "settings":
                elements.AddRange(SettingsPage(state, contentX, contentWidth, height));
                break;
            default:
                elements.AddRange(InboxPage(state, contentX, contentWidth, height));
                break;
        }

        if (!string.IsNullOrEmpty(state.OpenDialog))
        {
            elements.Add(Dialog(state, state.OpenDialog, contentX, contentWidth, height));
        }
        return elements;
    }

    private static IEnumerable<UiNode> InboxPage(FixtureState state, int x, int width, int height)
    {
        List<UiNode> nodes = CommonPage("InboxPageRoot", "Inbox", x, width, height, state.Focus);
        nodes.Add(Node("InboxTitle", "Inbox", "Text", x + 20, 20, 200, 24, true, false, state.Focus));
        nodes.Add(Node("InboxSearchBox", "Search inbox", "Edit", x + 20, 56, 220, 32, true, false, state.Focus));
        nodes.Add(Node("InboxStateFilter", "State", "ComboBox", x + 250, 56, 140, 32, true, false, state.Focus));
        nodes.Add(Node("InboxRefreshButton", "Refresh", "Button", x + 400, 56, 90, 32, true, false, state.Focus));
        UiNode grid = Node("InboxGrid", "Inbox files", "ListView", x + 20, 100, Math.Max(240, width - 40), 220, true, false, state.Focus);
        grid.Children = new List<UiNode>();
        if (state.InboxRow)
        {
            grid.Children.Add(Row("row-inbox-paired", "Inbox paired.mp4", "paired.mp4", x + 20, 100, state.Focus));
        }
        nodes.Add(grid);
        nodes.Add(Node("InboxStartProcessingButton", "Process selected", "Button", x + 20, 336, 140, 32,
            state.Selected == "inbox" && state.InboxRow, false, state.Focus));
        nodes.Add(Node("IngestButton", "Ingest", "Button", x + 170, 336, 90, 32,
            state.ProcessingReady, false, state.Focus));
        nodes.Add(Node("InboxProcessingProgress", "Processing", "ProgressBar", x + 270, 336, 160, 32,
            true, !state.ProcessingQueued, state.Focus));
        nodes.Add(Node("InboxBatchMetadataButton", "Batch metadata", "Button", x + 20, 378, 140, 32,
            state.BatchSelected && state.InboxRow, false, state.Focus));
        nodes.Add(Node("InboxCancelSelectionButton", "Clear selection", "Button", x + 170, 378, 140, 32,
            state.BatchSelected, false, state.Focus));
        nodes.Add(Node("InboxInspectorOpenButton", "Open inspector", "Button", x + 320, 378, 120, 32,
            state.Selected == "inbox" && state.InboxRow, state.Width > 1024, state.Focus));
        UiNode inspector = Node("InspectorPane", "Inspector", "Group", Math.Max(x + 40, x + width - 330), 20,
            Math.Min(310, width - 60), Math.Min(420, height - 40), true,
            state.Width <= 1024 && !state.DrawerOpen, state.Focus);
        inspector.Children = new List<UiNode>
        {
            Node("InboxInspectorHeading", "Inspector", "Text", inspector.X + 10, inspector.Y + 10, 180, 24, true, false, state.Focus),
            Node("InboxInspectorCloseButton", "Close", "Button", inspector.X + 250, inspector.Y + 10, 40, 28, true, false, state.Focus),
            Node("DisplayNameBox", "Display name", "Edit", inspector.X + 10, inspector.Y + 50, 240, 32, true, false, state.Focus),
            Node("InboxAuthorInput", "Author", "Edit", inspector.X + 10, inspector.Y + 90, 240, 32, true, false, state.Focus),
            Node("InboxTagInput", "Tag", "Edit", inspector.X + 10, inspector.Y + 130, 240, 32, true, false, state.Focus)
        };
        if (state.DrawerOpen) { foreach (UiNode child in inspector.Children) { child.IsOffscreen = false; } }
        nodes.Add(inspector);
        return nodes;
    }

    private static IEnumerable<UiNode> LibraryPage(FixtureState state, int x, int width, int height)
    {
        List<UiNode> nodes = CommonPage("LibraryPageRoot", "Library", x, width, height, state.Focus);
        nodes.Add(Node("LibrarySearchBox", "Search library", "Edit", x + 20, 20, 220, 32, true, false, state.Focus));
        nodes.Add(Node("LibrarySortField", "Sort", "ComboBox", x + 250, 20, 140, 32, true, false, state.Focus));
        nodes.Add(Node("LibraryFilterButton", "Filter", "Button", x + 400, 20, 90, 32, true, false, state.Focus));
        UiNode grid = Node("LibraryGrid", "Library files", "ListView", x + 20, 70, Math.Max(240, width - 40), 300, true, false, state.Focus);
        grid.Children = new List<UiNode>();
        if (state.LibraryRow)
        {
            grid.Children.Add(Row("row-library-paired", "Library paired.mp4", "paired.mp4", x + 20, 70, state.Focus));
        }
        nodes.Add(grid);
        nodes.Add(Node("LibraryBatchEditButton", "Batch edit", "Button", x + 20, 390, 120, 32,
            state.Selected == "library" && state.LibraryRow, false, state.Focus));
        nodes.Add(Node("LibraryInspectorOpenButton", "Open inspector", "Button", x + 160, 390, 120, 32,
            state.Selected == "library", state.Width > 1024, state.Focus));
        nodes.Add(Node("LibraryInspectorPane", "Inspector", "Group", Math.Max(x + 40, x + width - 330), 20,
            Math.Min(310, width - 60), Math.Min(360, height - 40), true,
            state.Width <= 1024 || !state.Selected.Equals("library", StringComparison.Ordinal), state.Focus));
        return nodes;
    }

    private static IEnumerable<UiNode> TaxonomyPage(FixtureState state, int x, int width, int height)
    {
        List<UiNode> nodes = CommonPage("TaxonomyPageRoot", "Taxonomy", x, width, height, state.Focus);
        nodes.Add(Node("TaxonomyKindList", "Kinds", "ComboBox", x + 20, 20, 160, 32, true, false, state.Focus));
        nodes.Add(Node("TaxonomySearchBox", "Search taxonomy", "Edit", x + 190, 20, 220, 32, true, false, state.Focus));
        nodes.Add(Node("TaxonomyRefreshButton", "Refresh", "Button", x + 420, 20, 90, 32, true, false, state.Focus));
        UiNode list = Node("TaxonomyTermList", "Terms", "ListView", x + 20, 70, Math.Max(240, width - 40), 280, true, false, state.Focus);
        list.Children = new List<UiNode>();
        foreach (string term in state.Taxonomies)
        {
            list.Children.Add(Row("row-taxonomy-" + term, "Taxonomy " + term, term, x + 20, 70, state.Focus));
        }
        nodes.Add(list);
        nodes.Add(Node("TaxonomyCreateButton", "Create", "Button", x + 20, 370, 90, 32, true, false, state.Focus));
        nodes.Add(Node("TaxonomyMergeButton", "Merge", "Button", x + 120, 370, 90, 32,
            !string.IsNullOrWhiteSpace(state.Selected) && state.Taxonomies.Contains(state.Selected), false, state.Focus));
        nodes.Add(Node("TaxonomyInspectorOpenButton", "Open inspector", "Button", x + 220, 370, 120, 32,
            !string.IsNullOrWhiteSpace(state.Selected), state.Width > 1024, state.Focus));
        nodes.Add(Node("TaxonomyInspectorPane", "Inspector", "Group", Math.Max(x + 40, x + width - 330), 20,
            Math.Min(310, width - 60), Math.Min(340, height - 40), true,
            state.Width <= 1024 || string.IsNullOrWhiteSpace(state.Selected), state.Focus));
        if (state.TaxonomyInputVisible)
        {
            nodes.Add(Node("TaxonomyNameInput", "Name", "Edit", x + 20, 420, 240, 32, true, false, state.Focus));
            nodes.Add(Node("PrimaryButton", "Create", "Button", x + 270, 420, 90, 32, true, false, state.Focus));
        }
        return nodes;
    }

    private static IEnumerable<UiNode> SettingsPage(FixtureState state, int x, int width, int height)
    {
        List<UiNode> nodes = CommonPage("SettingsPageRoot", "Settings", x, width, height, state.Focus);
        nodes.Add(Node("SettingsTabs", "Settings sections", "Tab", x + 20, 20, 420, 36, true, false, state.Focus));
        nodes.Add(Node("FolderSettingsTab", "Folders", "TabItem", x + 20, 60, 140, 32, true, false, state.Focus));
        nodes.Add(Node("SystemDiagnosticsTab", "Diagnostics", "TabItem", x + 160, 60, 140, 32, true, false, state.Focus));
        foreach (string id in FolderInputIds())
        {
            string value = state.Values.TryGetValue(id, out string current) ? current : "";
            nodes.Add(Node(id, value, "Edit", x + 20, 110, Math.Max(240, Math.Min(420, width - 60)), 32, true, false, state.Focus));
        }
        nodes.Add(Node("SaveFolderSettings", "Save", "Button", x + 20, 160, 90, 32, true, false, state.Focus));
        nodes.Add(Node("SettingsValidationErrors", "Folder validation failed", "Text", x + 20, 200, 320, 24,
            true, !state.ValidationError, state.Focus));
        nodes.Add(Node("SettingsStatusMessage", state.StatusText, "Text", x + 20, 230, 320, 24,
            true, string.IsNullOrWhiteSpace(state.StatusText), state.Focus));
        nodes.Add(Node("DiagnosticsRuntimeGroup", "Runtime diagnostics", "Group", x + 20, 110, 420, 240,
            true, !state.DiagnosticsTab, state.Focus));
        nodes.Add(Node("DiagnosticsRefreshButton", "Refresh diagnostics", "Button", x + 20, 360, 150, 32,
            !state.DiagnosticsRunning, !state.DiagnosticsTab, state.Focus));
        return nodes;
    }

    private static List<UiNode> CommonPage(string id, string name, int x, int width, int height, string focus)
    {
        return new List<UiNode>
        {
            Node(id, name, "Grid", x, 0, Math.Max(160, width), height, true, false, focus)
        };
    }

    private static UiNode Dialog(FixtureState state, string id, int x, int width, int height)
    {
        int dialogWidth = Math.Max(520, Math.Min(700, width - 80));
        int dialogHeight = Math.Max(480, Math.Min(620, height - 40));
        UiNode dialog = Node(id, id, "Dialog", x + 30, 20, dialogWidth, dialogHeight, true, false, state.Focus);
        string scroll = id switch
        {
            "BatchMetadataDialog" => "BatchMetadataScrollRoot",
            "ConflictComparisonDialog" => "ConflictScrollRoot",
            "LibraryBatchEditDialog" => "LibraryBatchEditScrollRoot",
            "TaxonomyMergeDialog" => "TaxonomyMergeScrollRoot",
            _ => id + "ScrollRoot"
        };
        dialog.Children = new List<UiNode>
        {
            Node(scroll, "Details", "ScrollViewer", x + 50, 60, dialogWidth - 40, dialogHeight - 140, true, false, state.Focus)
        };
        string close = id switch
        {
            "LibraryBatchEditDialog" => "LibraryBatchCancelButton",
            "TaxonomyMergeDialog" => "TaxonomyMergeCancelButton",
            "ConflictComparisonDialog" => "ConflictKeepBothButton",
            _ => ""
        };
        if (!string.IsNullOrEmpty(close))
        {
            dialog.Children.Add(Node(close, "Close", "Button", x + 70, dialogHeight + 20, 120, 32, true, false, state.Focus));
        }
        return dialog;
    }

    private static UiNode Row(string selector, string name, string text, int x, int y, string focus)
    {
        UiNode row = Node(selector, name, "ListItem", x, y, 520, 32, true, false, focus);
        row.Children = new List<UiNode>
        {
            Node(selector + "-text", text, "Text", x + 8, y + 4, 320, 24, true, false, focus)
        };
        return row;
    }

    private static UiNode Node(
        string id,
        string name,
        string type,
        int x,
        int y,
        int width,
        int height,
        bool enabled,
        bool offscreen,
        string focus)
    {
        return new UiNode
        {
            Selector = id,
            Type = type,
            AutomationId = id,
            ClassName = type,
            Name = name,
            IsEnabled = enabled,
            IsOffscreen = offscreen,
            X = x,
            Y = y,
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            Focus = string.Equals(id, focus, StringComparison.Ordinal)
        };
    }

    private static UiNode FindNode(IEnumerable<UiNode> nodes, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) { return null; }
        Stack<UiNode> pending = new Stack<UiNode>(nodes.Reverse());
        while (pending.Count > 0)
        {
            UiNode node = pending.Pop();
            if (string.Equals(node.AutomationId, target, StringComparison.Ordinal) ||
                string.Equals(node.Selector, target, StringComparison.Ordinal) ||
                string.Equals(node.Name, target, StringComparison.Ordinal))
            {
                return node;
            }
            if (node.Children != null)
            {
                for (int index = node.Children.Count - 1; index >= 0; index--)
                {
                    pending.Push(node.Children[index]);
                }
            }
        }
        return null;
    }

    private static string PropertyValue(UiNode node, string name)
    {
        return name.ToLowerInvariant() switch
        {
            "automationid" => node.AutomationId,
            "name" => node.Name,
            "controltype" or "type" => node.Type,
            "isEnabled" => node.IsEnabled.ToString(),
            "isenabled" => node.IsEnabled.ToString(),
            "isOffscreen" => node.IsOffscreen.ToString(),
            "isoffscreen" => node.IsOffscreen.ToString(),
            "x" => node.X.ToString(CultureInfo.InvariantCulture),
            "y" => node.Y.ToString(CultureInfo.InvariantCulture),
            "width" => node.Width.ToString(CultureInfo.InvariantCulture),
            "height" => node.Height.ToString(CultureInfo.InvariantCulture),
            _ => ""
        };
    }

    private static Dictionary<string, string> ReadOptions(string[] args, int start, List<string> repeated = null)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = start; index < args.Length; index++)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { continue; }
            string name = token.Substring(2);
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[name] = "";
                continue;
            }
            string value = args[++index];
            if (repeated != null && string.Equals(name, "pattern", StringComparison.Ordinal))
            {
                repeated.Add(value);
            }
            else
            {
                values[name] = value;
            }
        }
        return values;
    }

    private static string ContractSavedStatus()
    {
        string path = Environment.GetEnvironmentVariable("TRACEFOLD_VISUAL_CONTRACT");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { return "Folder settings saved."; }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("sessionPreparation").GetProperty("savedStatusText").GetString()
                ?? "Folder settings saved.";
        }
        catch
        {
            return "Folder settings saved.";
        }
    }

    private static IEnumerable<string> FolderInputIds()
    {
        string path = Environment.GetEnvironmentVariable("TRACEFOLD_VISUAL_CONTRACT");
        List<string> ids = new List<string>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { return ids; }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement input in document.RootElement.GetProperty("sessionPreparation").GetProperty("folderInputs").EnumerateArray())
            {
                string id = input.GetProperty("automationId").GetString();
                if (!string.IsNullOrWhiteSpace(id)) { ids.Add(id); }
            }
        }
        catch
        {
            ids.Clear();
        }
        return ids;
    }

    private static (byte, byte, byte) ScreenshotColor()
    {
        string fixtureRoot = Environment.GetEnvironmentVariable("TRACEFOLD_VISUAL_FIXTURE_ROOT");
        string registry = string.IsNullOrWhiteSpace(fixtureRoot) ? "" : Path.Combine(fixtureRoot, "registry.json");
        if (string.IsNullOrWhiteSpace(registry) || !File.Exists(registry)) { return (128, 128, 128); }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(registry));
            if (!document.RootElement.GetProperty("present").GetBoolean()) { return (128, 128, 128); }
            string bytes = document.RootElement.GetProperty("bytesHex").GetString();
            return string.Equals(bytes, "01000000", StringComparison.OrdinalIgnoreCase)
                ? ((byte) 238, (byte) 238, (byte) 238)
                : ((byte) 24, (byte) 24, (byte) 24);
        }
        catch
        {
            return (128, 128, 128);
        }
    }

    private static byte[] CreatePng(int width, int height, byte red, byte green, byte blue)
    {
        using MemoryStream output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        byte[] header = new byte[13];
        WriteUInt32BigEndian(header, 0, (uint) width);
        WriteUInt32BigEndian(header, 4, (uint) height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(output, "IHDR", header);

        using MemoryStream raw = new MemoryStream();
        byte[] row = new byte[1 + (width * 3)];
        row[0] = 0;
        for (int x = 0; x < width; x++)
        {
            row[1 + x * 3] = red;
            row[2 + x * 3] = green;
            row[3 + x * 3] = blue;
        }
        for (int y = 0; y < height; y++) { raw.Write(row, 0, row.Length); }

        byte[] compressed;
        raw.Position = 0;
        using (MemoryStream zipped = new MemoryStream())
        {
            using (ZLibStream zlib = new ZLibStream(zipped, CompressionLevel.Fastest, true))
            {
                raw.CopyTo(zlib);
            }
            compressed = zipped.ToArray();
        }
        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteUInt32BigEndian(length, 0, (uint) data.Length);
        output.Write(length, 0, length.Length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes, 0, typeBytes.Length);
        output.Write(data, 0, data.Length);
        byte[] crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        byte[] crc = new byte[4];
        WriteUInt32BigEndian(crc, 0, ComputeCrc32(crcInput));
        output.Write(crc, 0, crc.Length);
    }

    private static uint ComputeCrc32(byte[] data)
    {
        uint crc = 0xffffffffu;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (0xedb88320u ^ (crc >> 1)) : (crc >> 1);
            }
        }
        return crc ^ 0xffffffffu;
    }

    private static void WriteUInt32BigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte) (value >> 24);
        target[offset + 1] = (byte) (value >> 16);
        target[offset + 2] = (byte) (value >> 8);
        target[offset + 3] = (byte) value;
    }

    private static FixtureState LoadState(string path)
    {
        if (!File.Exists(path)) { return new FixtureState(); }
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FixtureState>(json, JsonOptions) ?? new FixtureState();
    }

    private static void SaveState(string path, FixtureState state)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); }
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions), new UTF8Encoding(false));
    }

    private static void Emit(object value)
    {
        Console.Out.Write(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void Log(string kind, string action, string target, string property, string value)
    {
        string path = Environment.GetEnvironmentVariable("TRACEFOLD_VISUAL_SUPPORT_LOG");
        if (string.IsNullOrWhiteSpace(path)) { return; }
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); }
        Dictionary<string, object> fact = new Dictionary<string, object>
        {
            ["kind"] = kind,
            ["action"] = action,
            ["target"] = target,
            ["property"] = property,
            ["value"] = value,
            ["processPath"] = Environment.ProcessPath ?? "",
            ["baseDirectory"] = AppContext.BaseDirectory,
            ["workingDirectory"] = Environment.CurrentDirectory,
            ["elapsedUtc"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
        File.AppendAllText(path, JsonSerializer.Serialize(fact, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static Dictionary<string, object> WaitTasks(Process process, Task<string> stdout, Task<string> stderr)
    {
        bool completed = Task.WaitAll(new Task[] { stdout, stderr }, 3000);
        return new Dictionary<string, object>
        {
            ["streamsCompleted"] = completed,
            ["stdoutCompleted"] = stdout.IsCompleted,
            ["stderrCompleted"] = stderr.IsCompleted,
            ["exitObserved"] = process.HasExited
        };
    }

    private static string RequiredEnvironment(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) { throw new InvalidOperationException(name + " is missing"); }
        return value;
    }
}
