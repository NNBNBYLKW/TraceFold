# Native Media Desktop Stage 5C-C Visual and UI Automation Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify the TraceFold native desktop visual system around an Explorer-style responsive file workspace and add an offline, one-click Windows UI Automation toolkit that drives the real Stage 5C-C published window, verifies persisted facts, records diagnostic evidence, and cleans only its own processes and data.

**Architecture:** Desktop owns semantic WinUI resources, a reusable presentation-only responsive inspector host, page-specific focus restoration, accessible control contracts, and adaptive dialogs; existing Application, Domain, and Infrastructure business behavior remains unchanged. A separate self-contained support executable and PowerShell orchestrator own temporary-root safety, DPI-aware window sizing, read-only SQLite/file snapshots, pinned `winapp` invocation, scenarios, reports, screenshots, and cleanup. The product package never references or contains the acceptance toolkit.

**Tech Stack:** .NET 10, C# 14, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, EF Core SQLite 10.0.11, xUnit v3, PowerShell 7/Windows PowerShell 5.1-compatible scripts, Microsoft `winapp` CLI 0.5.0 x64 standalone ZIP, bundled FFmpeg/FFprobe 2025-06-23-git-e6298e0759-full_build-www.gyan.dev.

**Spec:** `docs/superpowers/specs/2026-09-02-native-media-desktop-stage5cc-visual-uia-closure-design.md`

## Global Constraints

- Implementation starts from the committed approved spec `1acd744`. At execution time, invoke `superpowers:using-git-worktrees` and create an isolated worktree for branch `codex/native-media-desktop-stage5cc-visual-uia-closure`; do not implement directly on `main`.
- Target Windows 11 x64. Keep `TraceFold.Media.Desktop` unpackaged, self-contained, single-instance, native-windowed, and fully offline at runtime.
- Keep the Explorer-style information architecture and every existing scan, metadata, preprocessing, ingest, conflict, library, naming, taxonomy, diagnostics, and recovery business rule unchanged. Do not add a database migration.
- All dimensions in this plan are WinUI effective pixels (DIP): minimum content viewport `1024×720`, wide breakpoint `1280`, compact range `1024–1279`, navigation widths `176/48`, inspector width `360`, and media row height `38`.
- `>=1280` shows the full three-pane workspace. `1024–1279` uses the 48-DIP navigation rail and a right-side inspector drawer. Layout changes preserve page, selection, filters, search, scroll context, and metadata drafts.
- Follow Windows theme automatically. Do not add a user-facing theme preference or persist an app theme.
- Key controls use unique, language-independent `AutomationId` values, Chinese accessible names, and their native Invoke, Toggle, Value, Selection, ExpandCollapse, Scroll, or Window UIA patterns. Dynamic media-row tests select by fixed synthetic filename/name, not by generated database GUID.
- Do not add a production command-line bypass, hidden test page, direct database mutation option, processing shortcut, or business-state seed endpoint.
- The acceptance runner drives only TraceFold-owned windows/dialogs. It does not automate Windows folder pickers, Explorer, Settings, clipboard UI, UAC, or other applications.
- Every scenario owns a fresh direct child of the system temporary directory, with a high-entropy marker and fixed synthetic fixtures. It never reads, writes, enumerates, or deletes the user's TraceFold data root or media folders.
- If a normal `TraceFold.Media` instance is running, preflight returns `Blocked`; the runner never kills or redirects it. Cleanup can terminate only PIDs launched and recorded by the current run after executable-path verification.
- Observation retries are allowed. Mutating UI actions—ingest, replace, keep both, merge, delete, batch apply, confirmation—execute at most once per scenario step.
- Pin `winappcli-x64.zip` version `0.5.0`, size `37881954`, SHA-256 `88735CE6C2582AC5FAC6200194BF62467FDD72B44B2D230F3A4ED059FA79EE7D`, and official URL `https://github.com/microsoft/winappCli/releases/download/v0.5.0/winappcli-x64.zip`. Packaging verifies all four fields and never resolves “latest”.
- The product package and acceptance toolkit are separate deliverables. The product package must not contain `winapp`, acceptance scripts, support binaries, fixtures, toolkit manifests, reports, or screenshot templates.
- Reuse the repository fixtures `paired.mp4`, `paired.m4a`, and `photo.jpg` with their existing hashes. Do not commit generated databases, logs, reports, screenshots, downloaded tool archives, FFmpeg binaries, `dist`, `.packages`, or ignored `.superpowers` data.
- The current 970 passing tests are the minimum regression baseline. Every task uses red-green-refactor, runs focused tests, and ends in one reviewable commit.
- Release build must report zero warnings and zero errors. Final UI acceptance passes only if every required scenario is `Passed`, report generation succeeds, and no owned TraceFold/FFmpeg/FFprobe process or temporary root remains.
- A clean Windows VM is not required for Stage 5C-C completion. Record it as not run until one is available; never imply otherwise.

## Before Task 1

- [ ] **Create and verify the isolated worktree**

Invoke `superpowers:using-git-worktrees`, select `T:\TraceFold5CC` as the isolated path, and create branch `codex/native-media-desktop-stage5cc-visual-uia-closure` from the committed plan. Then run:

```powershell
Set-Location 'T:\TraceFold5CC'
git status --short --branch
git merge-base --is-ancestor 1acd744 HEAD
git rev-parse --show-toplevel
git rev-parse --git-dir
git rev-parse --git-common-dir
Get-Item apps/media-desktop/.packages | Select-Object FullName,LinkType,Target
```

Expected: the branch and worktree match the names above, the tree is clean, `1acd744` is an ancestor, the worktree Git directory differs from the common Git directory, and `.packages` points to the repository-owned offline cache.

- [ ] **Restore offline and prove the 970-test baseline**

```powershell
$traceFoldPackageCache = 'T:\Windows\Documents\GitHub\TraceFold\apps\media-desktop\.packages'
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source $traceFoldPackageCache -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers
```

Expected: Release build has 0 warnings and 0 errors, exactly 970 tests pass, and no test fails. Stop and report any unexplained baseline difference before editing.

## File and Responsibility Map

- `Desktop/Styles/VisualTokens.xaml`: semantic dimensions, spacing, theme brushes, and state resources.
- `Desktop/Styles/MediaWorkspaceStyles.xaml`: shared 38-DIP media-row, headers, page surfaces, toolbar, inspector, and status styles.
- `Desktop/App.xaml`: merges the two resource dictionaries after `XamlControlsResources`.
- `Desktop/Controls/WorkspaceLayoutPolicy.cs`: pure DIP breakpoint decision.
- `Desktop/Controls/ResponsiveInspectorHost.xaml(.cs)`: one primary presenter, one inspector presenter, wide/compact positioning, drawer state, and layout events; no business state.
- `Desktop/MainWindow.xaml(.cs)` and `Desktop/Shell/ShellPage.xaml(.cs)`: minimum viewport, stable root IDs, and 176/48-DIP responsive navigation.
- `Desktop/Pages/InboxPage.xaml(.cs)` and `InboxCardViewModel.cs`: 38-DIP inbox rows, compact inspector controls, focus return, and row accessible name.
- `Desktop/Pages/LibraryPage.xaml(.cs)` and `LibraryCardViewModel.cs`: 38-DIP library rows, compact inspector controls, focus return, filters, and row accessible name.
- `Desktop/Pages/TaxonomyPage.xaml(.cs)`: responsive taxonomy work area and inspector focus behavior.
- `Desktop/Pages/SettingsPage.xaml(.cs)`: responsive folder forms and stable status boundaries.
- `Desktop/Controls/TaxonomyPickerControl.xaml(.cs)`: parent-supplied input IDs/names and accessible selected-term removal.
- `Desktop/Dialogs/*.xaml(.cs)`: adaptive content width/scrolling and stable accessible actions at `1024×720`.
- `Desktop.Tests/Pages/VisualResourceContractTests.cs`: resource keys, exact values, theme dictionaries, and no page-local duplicate tokens.
- `Desktop.Tests/Pages/ResponsiveWorkspaceStructureTests.cs`: host, breakpoint, navigation, row, drawer, dialog, focus, and AutomationId contracts.
- `Desktop.Tests/Controls/WorkspaceLayoutPolicyTests.cs`: exact boundary behavior.
- `Desktop.Tests/Pages/UiAutomationContractTests.cs`: required IDs, names, uniqueness, and dynamic-row selection contract.
- `tools/TraceFold.Media.UiAcceptance.Support`: self-contained toolkit-only CLI for owned roots, read-only snapshots, and DPI-aware viewport sizing.
- `tests/TraceFold.Media.UiAcceptance.Support.Tests`: support CLI ownership, read-only snapshot, redaction, and viewport-math tests.
- `tools/winapp/win-x64/manifest.json`, `LICENSE.txt`, and `SOURCE-NOTICE.txt`: pinned driver identity, unmodified v0.5.0 license, and source evidence; no binary archive in Git.
- `scripts/ui-acceptance/TraceFold.UiAcceptance.*.psm1`: preflight, driver, processes, scenarios, evidence, reporting, and cleanup.
- `scripts/ui-acceptance/scenarios/*.ps1`: independently runnable, order-independent UI flows.
- `scripts/test-stage5cc-ui-acceptance-toolkit.ps1`: offline fake-driver and safety self-tests.
- `scripts/run-stage5cc-ui-acceptance.ps1` and `scripts/Run-Stage5CC-Acceptance.cmd`: one-click orchestrator and non-flashing launcher.
- `scripts/publish-media-desktop.ps1` and `scripts/smoke-media-desktop-package.ps1`: Stage 5C-C product and toolkit packaging, separation, identity, and smoke checks.
- `docs/media-desktop-stage5cc-visual-uia-test-guide.md`: one-click and manual system-boundary acceptance guide.

---

## Phase A — Visual Primitives and Responsive Shell

### Task 0: Lock the Static UI Automation Contract Before Visual Migration

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Controls/TaxonomyPickerControl.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Controls/TaxonomyPickerControl.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchMetadataDialog.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/LibraryBatchEditDialog.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxCardViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryCardViewModel.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/UiAutomationContractTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/LibraryViewModelTests.cs`

**Interfaces:**
- Adds dependency properties to `TaxonomyPickerControl`: `InputAutomationId`, `InputAutomationName`, `SelectedTermsAutomationId`, and `SelectedTermsAutomationName`.
- Adds stable input IDs `InboxAuthorInput`, `InboxSeriesInput`, `InboxTagInput`, `LibraryAuthorInput`, `LibrarySeriesInput`, `LibraryTagInput`, and dialog-prefixed equivalents.
- Adds stable observation IDs for existing page titles, commands, filters, selected counts, inspectors, status text, diagnostics groups, and dialog actions.
- Adds localized `AccessibleName` to inbox rows plus `MediaKindText` and localized `AccessibleName` to library rows.
- Locks the expected native-control mapping: `Button`/`AppBarButton` → Invoke, `ToggleSwitch`/`CheckBox` → Toggle, `TextBox`/`AutoSuggestBox` → Value, `ListView`/`NavigationView` → Selection, expandable selectors → ExpandCollapse, scroll boundaries → Scroll, and app/dialog roots → Window.

- [ ] **Step 1: Write the failing static UIA contract tests**

Parse every product XAML file and assert that each pre-migration logical control has one language-independent static `AutomationId`, a concise Chinese `AutomationProperties.Name`, and the native WinUI control type corresponding to its expected UIA pattern. Static IDs must be unique within a XAML namescope. Icon-only actions require explicit names. Status IDs belong to the bound text element rather than a decorative parent.

```csharp
public static TheoryData<string, string, string> RequiredControls => new()
{
    { "InboxPage.xaml", "InboxAuthorInput", "AutoSuggestBox" },
    { "InboxPage.xaml", "InboxOperationStatus", "TextBlock" },
    { "LibraryPage.xaml", "LibraryAuthorInput", "AutoSuggestBox" },
    { "LibraryPage.xaml", "LibraryStatusMessage", "TextBlock" },
    { "SettingsPage.xaml", "SettingsStatusMessage", "TextBlock" },
};

[Theory]
[MemberData(nameof(RequiredControls))]
public void Required_control_has_stable_id_name_and_native_control(
    string file,
    string id,
    string expectedElement)
{
    XElement element = FindAutomationId(LoadProductXaml(file), id);
    Assert.Equal(expectedElement, element.Name.LocalName);
    Assert.False(string.IsNullOrWhiteSpace(AutomationName(element)));
}
```

Add view-model tests using fixed filenames, never random IDs:

```csharp
Assert.Equal("待入库文件 paired.mp4，状态 等待前置处理", inboxCard.AccessibleName);
Assert.Equal("正式文件 photo.jpg，实际文件 photo.jpg，图片", libraryCard.AccessibleName);
```

- [ ] **Step 2: Run focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~UiAutomationContractTests|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~LibraryViewModelTests"
```

Expected: FAIL on missing picker dependency properties, stable IDs, names, status boundaries, and dynamic row projections.

- [ ] **Step 3: Implement parent-owned taxonomy input semantics**

Register the four dependency properties with empty-string defaults. Bind the inner `AutoSuggestBox` and selected-term list `AutomationProperties.AutomationId`/`Name` to those properties through the control root. Assign distinct values at every use site. Keep suggestions, Enter handling, removal, validation, multiple-author/tag behavior, and single-series behavior unchanged.

- [ ] **Step 4: Add static and dynamic accessible semantics**

Add the required IDs/names without changing layout. Keep existing GUID-backed dynamic row IDs for uniqueness, but bind row `AutomationProperties.Name` to the deterministic localized `AccessibleName`. Raise property-change notifications when display name, status, actual filename, or media kind changes. Do not place database IDs or absolute paths in names.

- [ ] **Step 5: Run the contract tests and commit**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~UiAutomationContractTests|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~LibraryViewModelTests"
git add apps/media-desktop/src/TraceFold.Media.Desktop/Controls/TaxonomyPickerControl.xaml apps/media-desktop/src/TraceFold.Media.Desktop/Controls/TaxonomyPickerControl.xaml.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchMetadataDialog.xaml apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/LibraryBatchEditDialog.xaml apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages
git commit -m "test(media-desktop): lock static ui automation contract"
```

Expected: focused tests pass. Visual migration may move the same logical controls in later tasks but may not rename their IDs, change their purpose names, or replace them with a control that loses the expected native pattern.

### Task 1: Establish Semantic Visual Resources

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Styles/VisualTokens.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Styles/MediaWorkspaceStyles.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/App.xaml`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/VisualResourceContractTests.cs`

**Interfaces:**
- Produces: resource keys `TraceFoldMinViewportWidth`, `TraceFoldMinViewportHeight`, `TraceFoldWideBreakpoint`, `TraceFoldNavigationOpenPaneWidth`, `TraceFoldNavigationCompactPaneWidth`, `TraceFoldInspectorWidth`, `TraceFoldFileRowHeight`, `TraceFoldPagePadding`, `TraceFoldSectionSpacing`, and `TraceFoldControlSpacing`.
- Produces: theme brush keys `TraceFoldStatusHealthyBrush`, `TraceFoldStatusAttentionBrush`, `TraceFoldStatusCriticalBrush`, `TraceFoldStatusQueuedBrush`, and `TraceFoldStatusRunningBrush` in both `Light` and `Dark` dictionaries.
- Produces: styles `TraceFoldMediaListStyle`, `TraceFoldMediaListItemStyle`, `TraceFoldMediaRowHeaderStyle`, `TraceFoldInspectorSurfaceStyle`, and `TraceFoldPageToolbarStyle`.

- [ ] **Step 1: Write failing resource-contract tests**

Create `VisualResourceContractTests.cs` with XML assertions for exact numeric values and theme keys:

```csharp
[Theory]
[InlineData("TraceFoldMinViewportWidth", "1024")]
[InlineData("TraceFoldMinViewportHeight", "720")]
[InlineData("TraceFoldWideBreakpoint", "1280")]
[InlineData("TraceFoldNavigationOpenPaneWidth", "176")]
[InlineData("TraceFoldNavigationCompactPaneWidth", "48")]
[InlineData("TraceFoldInspectorWidth", "360")]
[InlineData("TraceFoldFileRowHeight", "38")]
public void Visual_tokens_have_approved_values(string key, string expected)
{
    XDocument tokens = XDocument.Load(DesktopSource("Styles", "VisualTokens.xaml"));
    XElement resource = Assert.Single(tokens.Descendants(), element =>
        Attribute(element, "Key") == key);
    Assert.Equal(expected, resource.Value);
}

[Fact]
public void Light_and_dark_themes_define_every_status_brush()
{
    XDocument tokens = XDocument.Load(DesktopSource("Styles", "VisualTokens.xaml"));
    string[] keys = [
        "TraceFoldStatusHealthyBrush", "TraceFoldStatusAttentionBrush",
        "TraceFoldStatusCriticalBrush", "TraceFoldStatusQueuedBrush",
        "TraceFoldStatusRunningBrush"];
    foreach (string theme in new[] { "Light", "Dark" })
    {
        XElement dictionary = ThemeDictionary(tokens, theme);
        Assert.All(keys, key => Assert.Single(dictionary.Descendants(),
            element => Attribute(element, "Key") == key));
    }
}
```

Also assert that `App.xaml` merges `VisualTokens.xaml` before `MediaWorkspaceStyles.xaml` and every shared style uses a semantic key. Add the page-local legacy-literal assertions to the migration tests in Tasks 2–5, where they become green as each page is migrated; do not make Task 1 depend on future edits.

- [ ] **Step 2: Run focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~VisualResourceContractTests
```

Expected: FAIL because the style dictionaries and keys do not exist.

- [ ] **Step 3: Add the exact token dictionaries**

`VisualTokens.xaml` defines the approved values as `x:Double` resources and uses explicit theme dictionaries. Use these colors:

```xml
<ResourceDictionary.ThemeDictionaries>
  <ResourceDictionary x:Key="Light">
    <SolidColorBrush x:Key="TraceFoldStatusHealthyBrush" Color="#0F7B0F" />
    <SolidColorBrush x:Key="TraceFoldStatusAttentionBrush" Color="#8A4B00" />
    <SolidColorBrush x:Key="TraceFoldStatusCriticalBrush" Color="#C42B1C" />
    <SolidColorBrush x:Key="TraceFoldStatusQueuedBrush" Color="#5C2E91" />
    <SolidColorBrush x:Key="TraceFoldStatusRunningBrush" Color="#0067C0" />
  </ResourceDictionary>
  <ResourceDictionary x:Key="Dark">
    <SolidColorBrush x:Key="TraceFoldStatusHealthyBrush" Color="#6CCB5F" />
    <SolidColorBrush x:Key="TraceFoldStatusAttentionBrush" Color="#FCE100" />
    <SolidColorBrush x:Key="TraceFoldStatusCriticalBrush" Color="#FF99A4" />
    <SolidColorBrush x:Key="TraceFoldStatusQueuedBrush" Color="#B146C2" />
    <SolidColorBrush x:Key="TraceFoldStatusRunningBrush" Color="#60CDFF" />
  </ResourceDictionary>
</ResourceDictionary.ThemeDictionaries>
```

Use `Thickness` resource `TraceFoldPagePadding="20,18,20,16"`, doubles `TraceFoldSectionSpacing=12` and `TraceFoldControlSpacing=8`, and styles with `HorizontalContentAlignment=Stretch`. `TraceFoldMediaListItemStyle` sets `MinHeight` and `Height` to `{StaticResource TraceFoldFileRowHeight}` and removes card-width assumptions.

- [ ] **Step 4: Merge resources in application order**

Modify `App.xaml`:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <controls:XamlControlsResources />
      <ResourceDictionary Source="Styles/VisualTokens.xaml" />
      <ResourceDictionary Source="Styles/MediaWorkspaceStyles.xaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~VisualResourceContractTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/App.xaml apps/media-desktop/src/TraceFold.Media.Desktop/Styles apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/VisualResourceContractTests.cs
git commit -m "feat(media-desktop): add semantic visual resources"
```

### Task 2: Add the Responsive Inspector Host and Shell Breakpoints

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Controls/WorkspaceLayoutPolicy.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Controls/ResponsiveInspectorHost.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Controls/ResponsiveInspectorHost.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/MainWindow.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/MainWindow.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Shell/ShellPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Shell/ShellPage.xaml.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Controls/WorkspaceLayoutPolicyTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/ResponsiveWorkspaceStructureTests.cs`

**Interfaces:**
- Produces: `enum WorkspaceLayoutMode { Compact, Wide }` and `WorkspaceLayoutPolicy.GetMode(double widthDip)`.
- Produces: `ResponsiveInspectorHost.PrimaryContent`, `InspectorContent`, `HasInspectorContent`, `IsDrawerOpen`, `LayoutMode`, `OpenDrawer()`, `CloseDrawer()`, `LayoutModeChanged`, `DrawerOpened`, and `DrawerClosed`.
- Consumes: Task 1 resource keys; no Application/Domain/Infrastructure types.

- [ ] **Step 1: Write failing breakpoint and structure tests**

```csharp
[Theory]
[InlineData(1024, WorkspaceLayoutMode.Compact)]
[InlineData(1279.99, WorkspaceLayoutMode.Compact)]
[InlineData(1280, WorkspaceLayoutMode.Wide)]
[InlineData(1600, WorkspaceLayoutMode.Wide)]
public void Breakpoint_is_exact(double width, WorkspaceLayoutMode expected) =>
    Assert.Equal(expected, WorkspaceLayoutPolicy.GetMode(width));

[Fact]
public void Shell_uses_approved_navigation_widths()
{
    XDocument shell = XDocument.Load(DesktopSource("Shell", "ShellPage.xaml"));
    XElement navigation = ElementByName(shell, "Navigation");
    Assert.Equal("{StaticResource TraceFoldNavigationOpenPaneWidth}",
        Attribute(navigation, "OpenPaneLength"));
    Assert.Equal("{StaticResource TraceFoldNavigationCompactPaneWidth}",
        Attribute(navigation, "CompactPaneLength"));
}
```

Add structure assertions that the host contains exactly one primary `ContentPresenter` and one inspector `ContentPresenter`, a named inspector column and surface, no ViewModel binding, and public drawer events. Assert `MainWindow` root IDs `TraceFoldMainWindow` and `TraceFoldShell`, `MinWidth=1024`, `MinHeight=720`, and initial resize `1280×800`.

- [ ] **Step 2: Run focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~WorkspaceLayoutPolicyTests|FullyQualifiedName~ResponsiveWorkspaceStructureTests"
```

Expected: FAIL because the policy/host do not exist and Shell still uses 220 DIP.

- [ ] **Step 3: Implement the pure breakpoint policy**

```csharp
public enum WorkspaceLayoutMode { Compact, Wide }

public static class WorkspaceLayoutPolicy
{
    public const double WideBreakpoint = 1280d;

    public static WorkspaceLayoutMode GetMode(double widthDip)
    {
        if (!double.IsFinite(widthDip) || widthDip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthDip));
        }

        return widthDip >= WideBreakpoint
            ? WorkspaceLayoutMode.Wide
            : WorkspaceLayoutMode.Compact;
    }
}
```

- [ ] **Step 4: Implement the presentation-only host**

Use dependency properties for `PrimaryContent`, `InspectorContent`, `HasInspectorContent`, and `IsDrawerOpen`. On `SizeChanged`, call `WorkspaceLayoutPolicy.GetMode(ActualWidth)`. Wide mode assigns the inspector column 360 DIP, places one `InspectorSurface` in column 1, hides drawer chrome, and forces `IsDrawerOpen=false`; compact mode collapses the inspector column, spans primary content, and shows the same `InspectorSurface` right-aligned above the primary content only when `IsDrawerOpen && HasInspectorContent`.

```csharp
public void OpenDrawer()
{
    if (LayoutMode != WorkspaceLayoutMode.Compact || !HasInspectorContent)
    {
        return;
    }

    IsDrawerOpen = true;
    ApplyLayout();
    DrawerOpened?.Invoke(this, EventArgs.Empty);
}

public void CloseDrawer()
{
    if (!IsDrawerOpen)
    {
        return;
    }

    IsDrawerOpen = false;
    ApplyLayout();
    DrawerClosed?.Invoke(this, EventArgs.Empty);
}
```

The host contains no file selection, navigation, metadata, or command logic. Page-specific buttons call these methods and page code owns focus restoration.

- [ ] **Step 5: Apply minimum viewport and responsive navigation**

Set `MainWindow` initial size to `1280×800`, root minimum to `1024×720`, and root accessible IDs. In `ShellPage`, set `OpenPaneLength`/`CompactPaneLength` from resources and update mode from actual width:

```csharp
private void ApplyNavigationMode(double widthDip)
{
    bool wide = WorkspaceLayoutPolicy.GetMode(widthDip) == WorkspaceLayoutMode.Wide;
    Navigation.PaneDisplayMode = wide
        ? NavigationViewPaneDisplayMode.Left
        : NavigationViewPaneDisplayMode.LeftCompact;
    Navigation.IsPaneOpen = wide;
}
```

Call it on `Loaded` and `SizeChanged`; do not write layout state into `ShellViewModel`.

- [ ] **Step 6: Run focused and existing Shell tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~WorkspaceLayoutPolicyTests|FullyQualifiedName~ResponsiveWorkspaceStructureTests|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~ShellViewModelTests"
```

Expected: PASS, including one `Frame`, the four existing navigation destinations, and the new exact breakpoint values.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Controls apps/media-desktop/src/TraceFold.Media.Desktop/MainWindow.xaml apps/media-desktop/src/TraceFold.Media.Desktop/MainWindow.xaml.cs apps/media-desktop/src/TraceFold.Media.Desktop/Shell apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Controls apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/ResponsiveWorkspaceStructureTests.cs
git commit -m "feat(media-desktop): add responsive workspace shell"
```

## Phase B — Responsive File Workspaces and Dialogs

### Task 3: Convert Inbox Cards to Balanced Rows and Add the Compact Inspector

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxCardViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxSelectionViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxSelectionViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaCardViewModelTests.cs`

**Interfaces:**
- Consumes: `ResponsiveInspectorHost` and Task 1 list styles.
- Preserves: Task 0 `InboxCardViewModel.AccessibleName`; produces `InboxSelectionViewModel.HasSelection`.
- Produces: IDs `InboxListHeader`, `InboxInspectorOpenButton`, `InboxInspectorCloseButton`, `InboxInspectorHeading`, and preserves existing `InboxGrid`, `InspectorPane`, processing, metadata, batch, conflict, retry, and ingest IDs.

- [ ] **Step 1: Write failing row, selection, and focus contracts**

Add tests proving:

```csharp
[Fact]
public void Inbox_row_accessible_name_contains_visible_name_and_status()
{
    InboxCardViewModel row = Card(displayName: "旅行记录", statusText: "待补资料");
    Assert.Equal("待入库文件 旅行记录，状态 待补资料", row.AccessibleName);
}

[Fact]
public void Inbox_selection_reports_has_selection()
{
    InboxSelectionViewModel selection = new();
    Assert.False(selection.HasSelection);
    selection.ReplaceFromGrid([Card(id: Guid.Parse("11111111-1111-1111-1111-111111111111"))],
        [Card(id: Guid.Parse("11111111-1111-1111-1111-111111111111"))]);
    Assert.True(selection.HasSelection);
}
```

Update XAML structure assertions: `InboxGrid` is a `ListView`, its item template binds `MinHeight`/`Height` to `TraceFoldFileRowHeight`, binds `AutomationProperties.Name` to `AccessibleName`, and has no `Width="190"`, `ItemsWrapGrid`, or `RowDefinitions="120,..."`. Assert both inspector buttons exist with Chinese names and drawer close uses `KeyboardAccelerator Key="Escape"`.

- [ ] **Step 2: Run focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~InboxSelectionViewModelTests|FullyQualifiedName~MediaCardViewModelTests"
```

Expected: FAIL because rows, `HasSelection`, and drawer controls do not exist.

- [ ] **Step 3: Add selection projection without changing DTOs**

```csharp
public bool HasSelection => _selected.Count > 0;
```

Keep the Task 0 accessible-name projection unchanged. Raise `PropertyChanged(nameof(HasSelection))` in `NotifyDerivedProperties`. Do not change captured IDs, metadata versions, query ordering, or selection semantics.

- [ ] **Step 4: Replace the wrapping card grid with a 38-DIP list**

Keep `x:Name="InboxGrid"` and AutomationId `InboxGrid` for compatibility, but change the control type to `ListView`. Add a compact header row with columns `32,*,160,110`; row columns match and contain 28-DIP thumbnail/fallback, display name, actual filename, and status/progress text. Apply `TraceFoldMediaListStyle` and `TraceFoldMediaListItemStyle`; disable horizontal scrolling and stretch containers.

Wrap primary content and the existing inspector content in `ResponsiveInspectorHost`. Add `InboxInspectorOpenButton` to the toolbar, visible only in compact mode and enabled from `Selection.HasSelection`. Add `InboxInspectorCloseButton` at the inspector heading, visible only in compact mode. Selection updates content but never calls `OpenDrawer()` automatically.

- [ ] **Step 5: Restore focus after drawer close and list refresh**

Update casts from `GridViewItem` to `ListViewItem`. Add page handlers:

```csharp
private void OnInspectorOpenClick(object sender, RoutedEventArgs args) =>
    WorkspaceHost.OpenDrawer();

private void OnInspectorDrawerOpened(object? sender, EventArgs args) =>
    InboxInspectorHeading.Focus(FocusState.Programmatic);

private void OnInspectorDrawerClosed(object? sender, EventArgs args) =>
    FocusSelectedInboxItem();
```

`FocusSelectedInboxItem` scrolls `Selection.SingleSelectedItem` or the first selected ID into view, focuses its `ListViewItem`, and falls back to `InboxGrid`. Escape closes an open drawer first; when no drawer is open it preserves the existing clear-selection behavior.

- [ ] **Step 6: Run focused Inbox tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~Inbox|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~MediaCardViewModelTests|FullyQualifiedName~ResponsiveWorkspaceStructureTests"
```

Expected: PASS; existing processing completion refresh, selection preservation, batch commands, and focus requests remain green.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages/Inbox* apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/Inbox* apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaCardViewModelTests.cs
git commit -m "feat(media-desktop): add responsive inbox list"
```

### Task 4: Convert the Library to Balanced Rows and Add the Compact Inspector

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryCardViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibrarySelectionViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/LibrarySelectionViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaCardViewModelTests.cs`

**Interfaces:**
- Consumes: `ResponsiveInspectorHost` and shared list styles.
- Preserves: Task 0 `LibraryCardViewModel.AccessibleName` and `MediaKindText`; produces `LibrarySelectionViewModel.HasSelection`.
- Produces: IDs `LibraryListHeader`, `LibraryInspectorOpenButton`, `LibraryInspectorCloseButton`, `LibraryInspectorHeading`, `LibraryDisplayNameBox`, `LibraryAuthorInput`, `LibrarySeriesInput`, and `LibraryTagInput`; preserves all Stage 5B filter, batch, undo, and naming IDs.

- [ ] **Step 1: Write failing library-row and drawer tests**

```csharp
[Fact]
public void Library_row_accessible_name_contains_display_actual_and_kind()
{
    LibraryCardViewModel row = Card(
        displayName: "海边", actualFilename: "photo.jpg", mediaKind: MediaKind.Image);
    Assert.Equal("正式文件 海边，实际文件 photo.jpg，图片", row.AccessibleName);
}
```

Add the same `HasSelection` transition test as Inbox. Extend XAML assertions so `LibraryGrid` is a 38-DIP `ListView`, contains no wrapping-card constants, preserves extended selection, and exposes open/close inspector buttons and the four inspector input IDs.

- [ ] **Step 2: Run focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~LibrarySelectionViewModelTests|FullyQualifiedName~MediaCardViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
```

Expected: FAIL on missing row and drawer contracts.

- [ ] **Step 3: Add the selection projection**

```csharp
public bool HasSelection => _selected.Count > 0;
```

Keep the Task 0 row projections unchanged. Notify `HasSelection` exactly as in Task 3. Do not change asset IDs, metadata versions, sorting, query filters, or capture order.

- [ ] **Step 4: Build the responsive library list**

Keep `x:Name="LibraryGrid"` and AutomationId `LibraryGrid`, change it to a `ListView`, and use header/row columns `32,*,180,90`. Display thumbnail, visible name, actual filename, and media kind. Wrap the existing page and inspector in `ResponsiveInspectorHost`; add compact open/close buttons and stable IDs to the display-name and taxonomy inputs. Preserve the filter flyout, active chips, batch bar, undo banner, and all existing commands.

- [ ] **Step 5: Add page-local drawer focus handling**

Use the same behavior as Inbox with `ListViewItem`: opening focuses `LibraryInspectorHeading`; closing returns to the first selected row; Escape closes the drawer before clearing selection. Layout-mode changes never call `ViewModel.LoadAsync`, `ApplyCurrentFilterAsync`, or metadata flush.

- [ ] **Step 6: Run focused Library tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~Library|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~MediaCardViewModelTests|FullyQualifiedName~ResponsiveWorkspaceStructureTests"
```

Expected: PASS, including filters, sort persistence, batch preview/apply, naming presets/rules, undo, selection reconciliation, and reveal boundary tests.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages/Library* apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/Library* apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaCardViewModelTests.cs
git commit -m "feat(media-desktop): add responsive library list"
```

### Task 5: Make Taxonomy and Settings Responsive

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomySelectionViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/TaxonomySelectionViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsDiagnosticsStructureTests.cs`

**Interfaces:**
- Consumes: `ResponsiveInspectorHost`, visual tokens, and existing taxonomy/settings ViewModels.
- Produces: `TaxonomySelectionViewModel.HasSelection` and IDs `TaxonomyInspectorOpenButton`, `TaxonomyInspectorCloseButton`, `TaxonomyInspectorHeading`, `SettingsStatusMessage`, and `SettingsFolderForm`.
- Preserves: one Settings navigation entry, two native tabs, typed diagnostics actions, all taxonomy maintenance commands, and current session diagnostics lifecycle.

- [ ] **Step 1: Write failing compact-layout tests**

Assert `TaxonomyPage` uses `ResponsiveInspectorHost`, no longer declares root columns `176,12,*,12,320`, and exposes compact inspector buttons with Chinese names. Assert `SettingsPage` references `TraceFoldPagePadding`, names `SettingsFolderForm`, gives the bound save result `SettingsStatusMessage`, and uses a `VisualStateGroup` that stacks each label/input/action row below 1280 without changing tab selection.

Add:

```csharp
[Fact]
public void Taxonomy_selection_reports_has_selection()
{
    TaxonomySelectionViewModel selection = new();
    Assert.False(selection.HasSelection);
    selection.ReplaceFromList([Term("作者甲")], [Term("作者甲")]);
    Assert.True(selection.HasSelection);
}
```

- [ ] **Step 2: Run focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~TaxonomySelectionViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~SettingsDiagnosticsStructureTests"
```

Expected: FAIL on missing responsive host and status boundaries.

- [ ] **Step 3: Move taxonomy inspector into the shared host**

Keep the 176-DIP taxonomy-kind rail and flexible term list in primary content; move the current 320-DIP inspector into `InspectorContent`. Wide mode remains kind/list/inspector. Compact mode keeps kind/list and opens the inspector only from `TaxonomyInspectorOpenButton`. Add close/Escape handling and focus return to the first selected `ListViewItem`; selection alone does not open the drawer. Add/notify `HasSelection` without changing merge/delete selection semantics.

- [ ] **Step 4: Adapt Settings folder rows without changing behavior**

Name each directory grid and add wide/compact visual states. Wide uses `140,*,Auto`; compact uses one column and three rows with label, full-width text box, then picker button. Remove `MinWidth="420"`; use `MinWidth="0"` and stretch. Keep the top-level `ScrollViewer`, the two fixed tabs, existing save command, auto-scan behavior, diagnostic activation/cancellation, and registered folder-focus mapping. Add `AutomationId="SettingsStatusMessage"` and Chinese name to the bound status `TextBlock`.

- [ ] **Step 5: Run focused page and lifecycle tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~Taxonomy|FullyQualifiedName~Settings|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~ResponsiveWorkspaceStructureTests"
```

Expected: PASS; diagnostics still auto-runs only on first activation, settings save still validates all three folders, and taxonomy operations retain guarded cancellation/focus behavior.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages/Taxonomy* apps/media-desktop/src/TraceFold.Media.Desktop/Pages/Settings* apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages
git commit -m "feat(media-desktop): adapt taxonomy and settings layouts"
```

### Task 6: Make Every Workflow Dialog Fit the Minimum Viewport

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchMetadataDialog.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchMetadataDialog.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchIngestConfirmationDialog.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchIngestConfirmationDialog.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/ConflictComparisonDialog.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/ConflictComparisonDialog.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/LibraryBatchEditDialog.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/LibraryBatchEditDialog.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/TaxonomyMergeDialog.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/TaxonomyMergeDialog.xaml.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/ResponsiveWorkspaceStructureTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/ResponsiveDialogStructureTests.cs`

**Interfaces:**
- Produces: named scroll boundaries `BatchMetadataScrollRoot`, `BatchIngestScrollRoot`, `ConflictScrollRoot`, `LibraryBatchEditScrollRoot`, and `TaxonomyMergeScrollRoot`.
- Produces: `WideDialogLayout` and `CompactDialogLayout` visual states for the three comparison/editor dialogs.
- Preserves: existing result types, validation, command invocation, dialog-service wiring, preview content, cancellation, and initial/final focus.

- [ ] **Step 1: Write failing dialog layout tests**

Parse the five XAML files and assert that each has a named vertical `ScrollViewer`, no content root has `MinWidth` greater than `900`, and no fixed content height prevents a `1024×720` window from reaching the primary and secondary buttons. Assert that conflict, library batch edit, and taxonomy merge dialogs declare both exact visual-state names. Include an assertion that every dialog button keeps its existing `x:Name` or `AutomationId`.

```csharp
[Theory]
[InlineData("ConflictComparisonDialog.xaml", "ConflictScrollRoot")]
[InlineData("LibraryBatchEditDialog.xaml", "LibraryBatchEditScrollRoot")]
[InlineData("TaxonomyMergeDialog.xaml", "TaxonomyMergeScrollRoot")]
public void Large_dialog_has_a_reachable_compact_scroll_boundary(
    string fileName,
    string scrollRoot)
{
    XDocument document = LoadDialog(fileName);
    Assert.NotNull(FindByName(document, scrollRoot));
    Assert.Contains("CompactDialogLayout", VisualStateNames(document));
    Assert.DoesNotContain(FixedMinWidths(document), width => width > 900d);
}
```

- [ ] **Step 2: Run the tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~ResponsiveDialogStructureTests|FullyQualifiedName~ResponsiveWorkspaceStructureTests"
```

Expected: FAIL because the existing `720`, `980`, `1040`, and `1080` minimum widths and compact states do not satisfy the contract.

- [ ] **Step 3: Adapt the simple dialogs**

Wrap batch metadata and batch-ingest confirmation content in their named vertical scroll roots. Bind maximum content width to the XamlRoot size minus dialog chrome, retain natural height, and keep buttons owned by `ContentDialog`. Use wrapping text and stretch the taxonomy pickers instead of clipping them. Preserve the confirmation summary, the single-series rule, multiple-author/tag behavior, and all existing validation messages.

- [ ] **Step 4: Adapt the three large dialogs**

Add `AdaptiveTrigger MinWindowWidth="1280"` visual states:

- Conflict: wide incoming/existing columns; compact incoming then existing in one vertical scroll, followed by the same Keep Both, Replace, Skip/Cancel actions.
- Library batch edit: wide metadata and OneCommander-style naming panes; compact metadata followed by naming rules and preview in the same scroll. Do not simplify or replace the existing naming-rule interaction.
- Taxonomy merge: wide source, target, and preview columns; compact source, target, then preview vertically. Keep destructive confirmation text adjacent to its action.

Move only presentation containers. Do not duplicate bound controls or construct a second view model. In code-behind, use actual-size changes only to choose layout/focus behavior; do not change commands or validation.

- [ ] **Step 5: Add and verify focus behavior**

For each dialog, test the pure focus-target decision where possible and structure-test the runtime hooks. Opening places focus on the first editable or decision control; validation failure returns focus to the failing control; closing returns focus through the existing page service. In compact conflict/merge layouts, scrolling must bring the focused action into view.

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~Dialog|FullyQualifiedName~ResponsiveWorkspaceStructureTests"
```

Expected: PASS, with all prior dialog view-model tests unchanged.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/ResponsiveWorkspaceStructureTests.cs
git commit -m "feat(media-desktop): adapt workflow dialogs to compact windows"
```

---

## Phase C — Stable UI Automation Contract

### Task 7: Re-lock the Full Contract After Responsive Migration

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/MainWindow.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Shell/ShellPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Controls/ResponsiveInspectorHost.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/*.xaml`
- Create: `apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/UiAutomationContractTests.cs`

**Interfaces:**
- Extends the Task 0 contract to layout-added open/close drawer actions, inspector hosts, scroll boundaries, compact/wide surfaces, and all dialog decisions.
- Produces a toolkit-only JSON contract whose entries contain `automationId`, Chinese `name`, `controlType`, `requiredPatterns`, `scope`, and `dynamicSelectorPolicy`.
- Preserves every Task 0 ID/name across wide and compact modes.

- [ ] **Step 1: Extend the tests before adding the final manifest**

Add required entries for `TraceFoldMainWindow`, navigation items, page roots, selected counts, `InboxInspectorOpenButton`, `InboxInspectorCloseButton`, `LibraryInspectorOpenButton`, `LibraryInspectorCloseButton`, taxonomy inspector actions, named dialog scroll roots, conflict decisions, batch apply/undo, naming preview/apply, merge/delete confirmations, diagnostic groups, and all status fields. Assert no Task 0 ID or name changed during Tasks 1–6.

Parse `stage5cc-uia-contract.json` and assert every static manifest entry resolves to exactly one XAML element with the expected name/native control. Dynamic rows declare selection by fixed accessible filename/name and explicitly forbid GUID selectors.

- [ ] **Step 2: Run the contract test and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~UiAutomationContractTests
```

Expected: FAIL because responsive controls are not fully named/identified and the toolkit contract manifest does not exist.

- [ ] **Step 3: Add semantics only to layout-created controls**

Assign IDs and Chinese purpose names to the new drawer, inspector, responsive, scroll, and dialog boundaries. Reuse the same logical content/ID when the shared `ResponsiveInspectorHost` moves it; do not render duplicate wide and compact copies. Give every icon-only action a purpose name and every disabled primary action a text explanation exposed in the UIA tree.

- [ ] **Step 4: Write the final shared contract manifest**

List only language-independent IDs and fixed synthetic row-name policies. Keep it outside product source/resources so it cannot enter the product package. Task 10 consumes this exact file instead of maintaining a second PowerShell selector list; Task 14 packages it under the toolkit's `manifests/` directory.

Do not add a custom `AutomationPeer` pre-emptively. Task 10 first verifies these expected patterns in the native published WinUI tree. If a required pattern is absent, stop and record the exact element, control type, expected/actual patterns, and driver output for design review before adding peer code.

- [ ] **Step 5: Run desktop tests and commit**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tools/ui-acceptance-contract apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/UiAutomationContractTests.cs
git commit -m "test(media-desktop): lock responsive ui automation contract"
```

Expected: all desktop tests pass; the manifest and XAML agree; static IDs are unique; Task 0 semantics remain unchanged.

---

## Phase D — Offline UI Acceptance Toolkit

### Task 8: Pin `winapp` and Build the Toolkit-Only Support Executable

**Files:**
- Create: `apps/media-desktop/tools/winapp/win-x64/manifest.json`
- Create: `apps/media-desktop/tools/winapp/win-x64/LICENSE.txt`
- Create: `apps/media-desktop/tools/winapp/win-x64/SOURCE-NOTICE.txt`
- Create: `apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/TraceFold.Media.UiAcceptance.Support.csproj`
- Create: `apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/Program.cs`
- Create: `apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/OwnedRootManager.cs`
- Create: `apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/AcceptanceSnapshotReader.cs`
- Create: `apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/WindowViewportController.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests/TraceFold.Media.UiAcceptance.Support.Tests.csproj`
- Create: `apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests/OwnedRootManagerTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests/AcceptanceSnapshotReaderTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests/WindowViewportControllerTests.cs`
- Modify: `apps/media-desktop/TraceFold.MediaDesktop.slnx`

**Interfaces:**
- `root create --parent <system-temp> --run-id <guid> --token <secret> --output <json>`
- `root validate --root <path> --token <secret>`
- `root delete --root <path> --token <secret>`
- `snapshot --data-root <path> --output <json>`
- `window resize --process-id <pid> --width-dip <width> --height-dip <height> --output <json>`
- Exit codes: `0` success, `2` invalid arguments, `3` unsafe ownership/path, `4` snapshot unavailable, `5` window unavailable, `10` unexpected error.

- [ ] **Step 1: Write failing ownership and snapshot tests**

Test that a root is accepted only when it is a direct child of the canonical system temp directory, its leaf is `TraceFold-UIA-<guid>`, its marker contains matching run ID and token hash, and every traversed component is not a reparse point. Reject the drive root, temp root itself, repository root, user profile, nested arbitrary paths, missing/mismatched markers, symlinks, junctions, and a root containing an unowned live-process marker. Verify `root delete` removes only the validated root.

Create test SQLite files with the product schema and test that `snapshot` opens them read-only, never creates a database or journal, and returns only logical fixture names, counts, processing/ingest states, authors, series, tags, and formal filenames. Assert the JSON omits absolute paths, GUIDs, marker token, and user-profile text.

- [ ] **Step 2: Write failing viewport math tests**

Extract pure calculations and assert DIP-to-pixel conversion at 96, 120, 144, and 192 DPI, including nonclient-frame expansion. Reject width below 1024, height below 720, missing PID, an executable other than the supplied `TraceFold.Media.exe`, and a process not started by the current runner token.

```csharp
[Theory]
[InlineData(1024, 96, 1024)]
[InlineData(1024, 144, 1536)]
[InlineData(1280, 192, 2560)]
public void Converts_client_dip_to_pixels(int dip, int dpi, int expectedPixels) =>
    Assert.Equal(expectedPixels, WindowViewportController.ToPixels(dip, dpi));
```

- [ ] **Step 3: Run the new tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests/TraceFold.Media.UiAcceptance.Support.Tests.csproj -c Release -p:RestorePackagesPath=$traceFoldPackageCache --source $traceFoldPackageCache --ignore-failed-sources
```

Expected: FAIL because the support project and implementations do not exist.

- [ ] **Step 4: Add the pinned driver metadata and license evidence**

Write `manifest.json` with exactly:

```json
{
  "name": "Microsoft winapp CLI",
  "version": "0.5.0",
  "architecture": "x64",
  "archiveName": "winappcli-x64.zip",
  "archiveSize": 37881954,
  "sha256": "88735CE6C2582AC5FAC6200194BF62467FDD72B44B2D230F3A4ED059FA79EE7D",
  "sourceUrl": "https://github.com/microsoft/winappCli/releases/download/v0.5.0/winappcli-x64.zip",
  "releaseCommit": "fd7cb6f235fa54dd2c6e26386e65e967a2c8797a",
  "projectSupportedOs": ["Windows 11 x64"],
  "targetArchitectures": ["x64"],
  "capabilities": ["inspect", "search", "invoke", "set-value", "get-value", "get-property", "focus", "wait-for", "scroll-into-view", "screenshot", "send-keys"]
}
```

Copy the MIT license verbatim from `https://github.com/microsoft/winappCli/blob/v0.5.0/LICENSE`. Write `SOURCE-NOTICE.txt` with the pinned release/tag, asset URL, release commit, archive size/hash, and the statement that TraceFold does not modify or install the driver. Preserve any additional license/notice files already present in the supplied official archive during toolkit staging; do not invent a third-party notice or commit the ZIP/extracted executable.

- [ ] **Step 5: Implement the support CLI**

Use `System.CommandLine` only if already present in the offline cache; otherwise implement a small strict argument parser to avoid adding a network dependency. Reference `Microsoft.Data.Sqlite` from the existing offline cache. Publish JSON through an injected output writer and keep error text path-redacted.

`OwnedRootManager` canonicalizes paths, validates every ancestor needed by the operation, creates the marker with `FileMode.CreateNew`, and deletes by explicit traversal after a second validation. `AcceptanceSnapshotReader` uses SQLite `Mode=ReadOnly;Cache=Private` and filesystem reads only; it must not reference Application mutation services. `WindowViewportController` calls `GetDpiForWindow`, `GetClientRect`, `AdjustWindowRectExForDpi`, and `SetWindowPos`, then verifies the resulting client area within two physical pixels.

- [ ] **Step 6: Add the projects and prove separation**

Add the support and support-test projects to `TraceFold.MediaDesktop.slnx`, but add no reference from any product project to the support executable. Add an architecture test that inspects project references and fails if `TraceFold.Media.Desktop`, Application, Infrastructure, or Domain references the toolkit namespace/project.

- [ ] **Step 7: Run support and full tests, then commit**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests/TraceFold.Media.UiAcceptance.Support.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache
git add apps/media-desktop/tools apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests apps/media-desktop/TraceFold.MediaDesktop.slnx
git commit -m "test(media-desktop): add isolated ui acceptance support tool"
```

Expected: all tests pass; Git contains metadata/license/source only, not `winappcli-x64.zip` or extracted driver files.

### Task 9: Implement the PowerShell Driver, Ownership, Evidence, and Report Core

**Files:**
- Create: `scripts/ui-acceptance/TraceFold.UiAcceptance.Contracts.psm1`
- Create: `scripts/ui-acceptance/TraceFold.UiAcceptance.Driver.psm1`
- Create: `scripts/ui-acceptance/TraceFold.UiAcceptance.Processes.psm1`
- Create: `scripts/ui-acceptance/TraceFold.UiAcceptance.Evidence.psm1`
- Create: `scripts/ui-acceptance/TraceFold.UiAcceptance.Runner.psm1`
- Create: `scripts/ui-acceptance/tests/fixtures/fake-winapp.ps1`
- Create: `scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Contracts.Tests.ps1`
- Create: `scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Driver.Tests.ps1`
- Create: `scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Processes.Tests.ps1`
- Create: `scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Runner.Tests.ps1`
- Create: `scripts/test-stage5cc-ui-acceptance-toolkit.ps1`

**Interfaces:**
- `Invoke-WinAppObservation -Arguments <string[]> -ProcessId <pid> -TimeoutSeconds <n>` retries only inspect/search/get-value/wait-for/screenshot.
- `Invoke-WinAppMutation -StepId <id> -Arguments <string[]> -ProcessId <pid>` rejects a second call for the same scenario step.
- `Start-TraceFoldScenario -ExecutablePath <path> -OwnedRoot <path> -RunToken <token>` returns a PID/path record and sets `TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT` only in the child environment.
- `Stop-OwnedProcessTree` terminates only recorded, path-verified current-run PIDs.
- `Write-UiAcceptanceReport` emits `report.json`, `summary.txt`, and evidence references with statuses `Passed`, `Failed`, `Blocked`, or `NotRun`.

- [ ] **Step 1: Write fake-driver and ownership tests first**

The fake driver records argv and returns fixed JSON for inspect/search/wait/screenshot. Test argument-array invocation (never string evaluation), PID targeting, timeout propagation, JSON validation, stderr capture, and redaction. Test that observation retries stop at the configured deadline and that mutation step IDs can execute exactly once even after an ambiguous timeout. Exercise success, product failure, script exception, timeout, and simulated user interruption; every path must enter the same evidence/cleanup `finally` block.

Test process cleanup with injectable process inventory/stop delegates: no PID is stopped unless it is present in the current run ledger, its start time matches, and its canonical executable path is under the current product/toolkit directory. A same-name user process and a reused PID must be left alone.

- [ ] **Step 2: Run the self-test and verify red**

```powershell
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
```

Expected: FAIL because the modules do not exist.

- [ ] **Step 3: Implement strict driver wrappers**

Invoke the pinned executable with argument arrays in this shape:

```powershell
& $WinAppPath ui wait-for $AutomationId -a $ProcessId --timeout $TimeoutMs --json
& $WinAppPath ui inspect -a $ProcessId --json
& $WinAppPath ui invoke $AutomationId -a $ProcessId --json
& $WinAppPath ui set-value $AutomationId $Value -a $ProcessId --json
```

Use `ui send-keys ... --via send-input` only for keyboard navigation checks after verifying foreground ownership and an unlocked interactive desktop. Parse JSON before returning. Classify missing desktop, driver incompatibility, an ordinary app instance, unavailable archive, and unsupported host as `Blocked`; classify product assertion failures as `Failed`.

- [ ] **Step 4: Implement process and scenario boundaries**

Preflight verifies the product executable identity, driver manifest, supplied archive hash/size or extracted driver hash manifest, interactive/unlocked desktop, no existing ordinary `TraceFold.Media` process, no stale unverified run root, and writable system temp. It creates a new empty durable run-output child with a marker, rejects existing/nonempty/reparse targets, and never recursively deletes durable reports. Launch the real published executable with a child-only `TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT`. Record PID, start time, executable path, root marker, and owned FFmpeg/FFprobe descendants.

Cleanup runs in `finally`: capture evidence first, close the app normally, then stop only still-running owned PIDs, validate/delete the scenario root, and report any cleanup failure. Never call `Stop-Process -Name`, never enumerate/delete user media roots, and never persist an environment variable.

- [ ] **Step 5: Implement evidence and report contracts**

Every failed step first captures a driver screenshot, UIA-tree JSON, last parsed command result, product log snapshot, read-only state snapshot, and redacted process ledger inside the owned scenario root. Before deleting that root, copy only schema-validated/redacted evidence into the unique durable run output (`results/<run-id>` in a packaged toolkit, an explicitly supplied output in development). Never copy the database, raw log, original fixture, ownership marker, or unredacted path. Evidence filenames use scenario/step slugs rather than user values. `report.json` records the acceptance run ID, Stage checkpoint, source commit, product hash/version, driver version/hash, fixture hashes, host OS/DPI/theme, viewport, start/end/duration, scenario/step statuses and durations, cleanup result, screenshot index, and `cleanVmStatus: "NotRun"`; it never records username, machine name, an absolute user path, ownership token, product database/entity GUID, real-user metadata, or raw database key. Reports use relative evidence paths.

- [ ] **Step 6: Run self-tests and commit**

```powershell
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
git add scripts/ui-acceptance scripts/test-stage5cc-ui-acceptance-toolkit.ps1
git commit -m "test(media-desktop): add safe ui acceptance runner core"
```

Expected: all fake-driver, once-only mutation, JSON contract, ownership, cleanup, and redaction tests pass without starting TraceFold or modifying the registry.

### Task 10: Prove the Pinned Driver Against the Real Published Window

**Files:**
- Create: `scripts/ui-acceptance/scenarios/00-startup-layout.ps1`
- Create: `scripts/run-stage5cc-ui-acceptance.ps1`
- Modify: `scripts/ui-acceptance/TraceFold.UiAcceptance.Runner.psm1`
- Modify: `scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Runner.Tests.ps1`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/UiAutomationContractTests.cs`

**Inputs:**
- A local, unmodified `winappcli-x64.zip` matching the exact Task 8 manifest. The runner must not download it.
- A local Release `win-x64` publish of `TraceFold.Media.exe`; the scenario must not run the project through a browser, `dotnet run`, or a test host.

**Compatibility gate:** Continue only if v0.5.0 can attach by the launched PID and exposes the required WinUI elements/patterns. A missing or mismatched archive is `Blocked`, not a product failure. A genuine missing native UIA pattern stops implementation for design review under Task 7; do not work around it with coordinates or an unreviewed custom peer.

- [ ] **Step 1: Write failing scenario-contract tests**

Use the fake driver to assert that `00-startup-layout.ps1`:

1. launches the passed published executable with a new owned root;
2. waits for `TraceFoldMainWindow` by PID;
3. loads `stage5cc-uia-contract.json`, inspects the full tree, and verifies one unique match for every required static entry plus the fixed-name dynamic-row policy;
4. records and compares each element's control type, supported patterns, enabled/offscreen state, and accessible name against that manifest;
5. requests the exact `1024×720`, `1280×800`, and `1600×900` client viewports through the support CLI;
6. captures a screenshot and UIA tree at each size; and
7. closes and cleans up only the launched process/root.

Assert that the scenario contains no coordinates, sleeps, Explorer/folder-picker selectors, process-name termination, or generated row GUID selectors.

- [ ] **Step 2: Run toolkit tests and verify red**

```powershell
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
```

Expected: FAIL because the startup scenario and orchestrator do not exist.

- [ ] **Step 3: Implement the startup/layout scenario**

The orchestrator accepts:

```powershell
param(
    [Parameter(Mandatory)][string] $ProductDirectory,
    [Parameter(Mandatory)][string] $WinAppCliArchivePath,
    [string] $OutputDirectory,
    [ValidateSet('All','StartupLayout','MainChain','Conflict','Library','Taxonomy','Diagnostics','Visual')]
    [string[]] $Scenario = @('All')
)
```

It verifies and extracts the driver into the current owned run root, builds/publishes the support CLI into that same root, and never installs either globally. `00-startup-layout.ps1` uses positional stable selectors exactly as documented by the pinned driver, for example:

```powershell
Invoke-WinAppObservation -Arguments @('ui','wait-for','TraceFoldMainWindow','-a',"$pid",'--timeout','30000','--json') -ProcessId $pid
Invoke-WinAppObservation -Arguments @('ui','inspect','-a',"$pid",'--depth','8','--json') -ProcessId $pid
Invoke-WinAppMutation -StepId 'navigate.settings' -Arguments @('ui','invoke','SettingsNavigationItem','-a',"$pid",'--json') -ProcessId $pid
```

At `1600×900` and `1280×800`, assert a 176-DIP open navigation pane and visible inline inspector. At `1024×720`, assert the 48-DIP rail, absent/offscreen inline inspector, enabled inspector-open button after selection, right-side drawer after a single invoke, and closed drawer after Escape. Use UIA bounds from inspect output plus the support tool's verified client bounds; allow two physical pixels for DPI rounding.

- [ ] **Step 4: Run the real compatibility gate**

```powershell
dotnet publish apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore -p:RestorePackagesPath=$traceFoldPackageCache -o .superpowers/stage5cc-product
pwsh -NoProfile -File scripts/run-stage5cc-ui-acceptance.ps1 -ProductDirectory .superpowers/stage5cc-product -WinAppCliArchivePath '<local-path>\winappcli-x64.zip' -OutputDirectory .superpowers/stage5cc-uia-gate -Scenario StartupLayout
```

Expected: `StartupLayout` is `Passed`; `report.json` identifies the published `TraceFold.Media.exe` and driver v0.5.0; all three sizes have screenshots/trees; the app and owned temp root are gone. If the archive path is unavailable, stop with `Blocked` and obtain the exact archive before claiming this task complete.

- [ ] **Step 5: Re-run contract and toolkit tests, then commit**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~UiAutomationContractTests|FullyQualifiedName~Responsive"
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
git add scripts/run-stage5cc-ui-acceptance.ps1 scripts/ui-acceptance apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/UiAutomationContractTests.cs
git commit -m "test(media-desktop): gate real window ui automation compatibility"
```

### Task 11: Automate the Complete Main Workflow Chain

**Files:**
- Create: `scripts/ui-acceptance/scenarios/10-main-chain.ps1`
- Create: `scripts/ui-acceptance/TraceFold.UiAcceptance.Workflows.psm1`
- Create: `scripts/ui-acceptance/tests/TraceFold.UiAcceptance.IntakeScenario.Tests.ps1`
- Modify: `scripts/ui-acceptance/TraceFold.UiAcceptance.Runner.psm1`
- Modify: `scripts/test-stage5cc-ui-acceptance-toolkit.ps1`

**Fixture facts:**
- `paired.mp4`: SHA-256 `B5B85F2CA95EA5EB781B42D202D1C3DB361BF37F5BF4B3107A79469FC716A1A3`.
- `paired.m4a`: SHA-256 `187F3BE0AB2C07C6A967840BEBFA0EB1646227B52179387F74AB4D8BBC64AAC1`.
- `photo.jpg`: SHA-256 `7D1BD6791F1D40FFAFE6C7704FB2E71B6FBF6CBD26B71F9A45E62460D61312A6`.
- Metadata: authors `作者甲` and `作者乙`, exactly one series `系列甲`, tags `风景` and `夜景`; display name `验收视频`, physical filename unchanged.

- [ ] **Step 1: Write the fake-driver scenario test**

Assert this exact order and once-only mutation IDs: open Settings; set owned inbox/library/workspace paths with `set-value`; save; copy verified fixtures into the configured inbox; open Inbox; refresh; select the `paired.mp4` row by deterministic accessible name; enter two authors, one series, and two tags; perform batch metadata on multiple pending rows; wait for preprocessing; confirm that the right inspector leaves `排队中`; invoke formal ingest once; wait for the row to disappear; navigate to Library; find `验收视频`; exercise combined filters, batch display-name/naming work and Undo; perform taxonomy create/edit/merge-or-delete plus Undo; finish one diagnostics run.

The test must reject a scenario that uses the folder-picker button, sleeps, retries an ingest/Enter action, copies an unverified fixture, selects a row by GUID, mutates the database outside product UI, or omits the interface-plus-fact assertion at any durable checkpoint.

- [ ] **Step 2: Run the toolkit self-test and verify red**

```powershell
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
```

Expected: FAIL because the intake scenario is absent.

- [ ] **Step 3: Implement the scenario using observations and once-only actions**

Before each copy, verify the source fixture hash. Copy only into the owned inbox. Use UIA `ValuePattern` for text fields, `InvokePattern`/`SelectionItemPattern` for buttons and rows, and `send-keys 'enter' --target <input> --via send-input` only to commit taxonomy tokens. Each token commit receives its own step ID. After the MP4/M4A pair appears as one candidate, wait on `InboxInspectorStatus` until it no longer contains `排队中` and then until it reports ready; capture a screenshot both while queued and after completion to guard the previously fixed stale-inspector defect.

Invoke each selected item's `FormalIngestButton` exactly once: first the completed video, then `photo.jpg` after verifying its preview and batch metadata. Observe completion through status change and row disappearance. At every durable checkpoint, take a concurrent read-only snapshot with a bounded busy timeout and pair it with the UI assertion. After ingest, assert:

- one formal video asset, one formal image asset, and no audio-only asset;
- display name `验收视频`, actual filename `paired.mp4`;
- exactly two authors, one series, and two tags with the expected normalized values;
- `paired.mp4` and `paired.m4a` no longer exist in the inbox;
- `photo.jpg` no longer exists in the inbox, exists in formal storage with its default physical filename, and remains previewable after reopen;
- no original M4A, intermediate output, backup, temp file, or toolkit artifact exists in formal storage;
- the formal MP4 is readable and the product log contains no unhandled exception/private absolute path.

Continue in the same application/root to complete batch metadata, strict combined library filters, display-name/naming preview and apply/Undo, taxonomy maintenance and Undo, and one diagnostics run. Put repeated public-UI setup/actions/observations in `TraceFold.UiAcceptance.Workflows.psm1`; the independent scenarios in Task 12 reuse those helpers while supplying new roots. Keep this script as one linear main chain. Pair each UI conclusion with a read-only snapshot, file hash, or process fact. Then close and reopen once for the explicit persistence check, verify the final library/taxonomy/diagnostic state through UI, and close before root cleanup.

- [ ] **Step 4: Run the real intake scenario**

```powershell
pwsh -NoProfile -File scripts/run-stage5cc-ui-acceptance.ps1 -ProductDirectory .superpowers/stage5cc-product -WinAppCliArchivePath '<local-path>\winappcli-x64.zip' -OutputDirectory .superpowers/stage5cc-main-chain -Scenario MainChain
```

Expected: `MainChain` is `Passed`; both queued/ready inspector screenshots exist; every main-workflow UI/fact pair matches; no owned process/root remains.

- [ ] **Step 5: Commit**

```powershell
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
git add scripts/ui-acceptance scripts/test-stage5cc-ui-acceptance-toolkit.ps1
git commit -m "test(media-desktop): automate intake preprocessing and ingest"
```

### Task 12: Add Independent Conflict, Library, Taxonomy, and Diagnostics Scenarios

**Files:**
- Create: `scripts/ui-acceptance/scenarios/20-conflict-keep-both.ps1`
- Create: `scripts/ui-acceptance/scenarios/21-conflict-replace.ps1`
- Create: `scripts/ui-acceptance/scenarios/22-conflict-skip.ps1`
- Create: `scripts/ui-acceptance/scenarios/23-processing-failure-retry.ps1`
- Create: `scripts/ui-acceptance/scenarios/30-batch-metadata-undo.ps1`
- Create: `scripts/ui-acceptance/scenarios/40-library-filter-naming-undo.ps1`
- Create: `scripts/ui-acceptance/scenarios/50-taxonomy-maintenance-undo.ps1`
- Create: `scripts/ui-acceptance/scenarios/60-diagnostics-read-only.ps1`
- Create: `scripts/ui-acceptance/scenarios/61-single-instance-safety.ps1`
- Create: `scripts/ui-acceptance/tests/TraceFold.UiAcceptance.WorkflowScenario.Tests.ps1`
- Modify: `scripts/ui-acceptance/TraceFold.UiAcceptance.Runner.psm1`
- Modify: `scripts/test-stage5cc-ui-acceptance-toolkit.ps1`

**Isolation rule:** Each script gets a new owned root and may run alone or in any order. Setup uses only the same public UI workflow as the assertion under test. No scenario relies on another scenario's database, filenames, or presets.

- [ ] **Step 1: Write failing scenario-shape and ordering tests**

Using the fake driver, call every script individually and in reverse order. Assert distinct owned roots, unique once-only action IDs, deterministic fixed fixtures, no direct product-database write, and no system-window selector. Assert each script records its setup, decision, UI observation, read-only snapshot, and cleanup result.

```powershell
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
```

Expected: FAIL because the scenarios are missing.

- [ ] **Step 2: Implement the three conflict decisions**

Each conflict script ingests a fixed image through the UI, copies a verified conflicting file into its owned inbox, scans, opens the comparison dialog, and executes exactly one decision:

- Keep Both: both formal records/files remain and the new file uses the next available Windows-style suffix such as `photo (2).jpg`.
- Replace: exactly one formal target remains, its bytes/record match the incoming item, the old backup is cleaned, and reopening shows the replacement.
- Skip: the existing formal asset is unchanged, the incoming file remains safely pending, and the conflict dialog closes without an ingest commit.

Capture the comparison preview before each decision. If the conflict state changes between observation and action, fail the scenario; never repeat the decision against a newly created case.

`23-processing-failure-retry.ps1` writes a deterministic, hash-checked invalid MP4/M4A pair inside its owned inbox, verifies the visible failed state and retained sources, replaces only those owned fixture bytes with the valid pair, invokes Retry once, and verifies recovery through UI plus read-only/file facts. It captures loading, queued, running, failed, recovered, and disabled-action states without modifying package tools or introducing a failure hook.

- [ ] **Step 3: Implement batch metadata and library workflows**

`30-batch-metadata-undo.ps1` selects two ingested/pending rows, applies append/replace metadata with multiple authors/tags and one series, verifies selected count, batch summary, success and deliberately invalid/partial-failure feedback, then invokes the exposed Undo action once and verifies the exact prior values through UI and snapshot.

`40-library-filter-naming-undo.ps1` ingests three fixed items, combines search/kind/author/series/tag filters, verifies strict intersection and sort stability, opens the OneCommander-style batch editor, composes a simple prefix plus sequence rule, verifies the preview, applies once, verifies display names changed while physical filenames/hashes did not, invokes Undo once, and verifies names/metadata/presets return to the prior state. It must cover a disabled rule row and preview warning without applying an invalid draft.

- [ ] **Step 4: Implement taxonomy and diagnostics workflows**

`50-taxonomy-maintenance-undo.ps1` creates source/target terms through ordinary metadata UI, filters/selects them in taxonomy management, previews and confirms a merge once, verifies every reference moved, invokes Undo once, then exercises guarded delete plus Undo on an unreferenced term. It verifies no physical filename or media hash changes.

`60-diagnostics-read-only.ps1` opens Settings > Diagnostics, waits for all categories to finish, refreshes once, verifies category status/summary/detail accessibility, result retention and cancellation behavior, captures a screenshot, closes the app, and compares before/after read-only snapshots and formal-file hashes byte-for-byte. Diagnostics may launch only package-local tools; all descendants must exit.

`61-single-instance-safety.ps1` launches a first owned instance, then launches the same published executable again with the same owned data root. It verifies one main window remains, the first instance is activated, the second process exits without creating or mutating a second database, and cleanup still owns every PID. Separately, runner preflight with an ordinary outside-run instance must return `Blocked` without terminating or redirecting that process.

- [ ] **Step 5: Run each real scenario independently**

```powershell
$scenarioNames = @('Conflict','Library','Taxonomy','Diagnostics')
foreach ($scenarioName in $scenarioNames) {
    pwsh -NoProfile -File scripts/run-stage5cc-ui-acceptance.ps1 -ProductDirectory .superpowers/stage5cc-product -WinAppCliArchivePath '<local-path>\winappcli-x64.zip' -OutputDirectory ".superpowers/stage5cc-$($scenarioName.ToLowerInvariant())" -Scenario $scenarioName
    if ($LASTEXITCODE -ne 0) { throw "Scenario failed: $scenarioName" }
}
```

Expected: every requested scenario is `Passed` in its own report and leaves no owned process/root. A scenario failing by itself is not waived because it passed in an all-scenario run.

- [ ] **Step 6: Commit**

```powershell
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
git add scripts/ui-acceptance scripts/test-stage5cc-ui-acceptance-toolkit.ps1
git commit -m "test(media-desktop): cover independent desktop workflows"
```

### Task 13: Capture the Theme, Viewport, Keyboard, Drawer, and Dialog Matrix

**Files:**
- Create: `apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/WindowsAppThemeCoordinator.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests/WindowsAppThemeCoordinatorTests.cs`
- Create: `scripts/ui-acceptance/scenarios/70-visual-keyboard-matrix.ps1`
- Create: `scripts/ui-acceptance/tests/TraceFold.UiAcceptance.VisualScenario.Tests.ps1`
- Modify: `apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/Program.cs`
- Modify: `scripts/ui-acceptance/TraceFold.UiAcceptance.Evidence.psm1`
- Modify: `scripts/ui-acceptance/TraceFold.UiAcceptance.Runner.psm1`
- Create: `scripts/ui-acceptance/report-template/index.html`
- Modify: `scripts/test-stage5cc-ui-acceptance-toolkit.ps1`

**Interfaces:**
- `theme apply --mode light|dark --journal <owned-path> --output <json>` temporarily changes only the current user's Windows app-theme value after journaling its exact original state.
- `theme restore --journal <owned-path> --output <json>` restores value existence, registry type, and bytes exactly, then removes the journal.
- `-ThemeMatrix Both` is required for a full `Passed` visual scenario; `CurrentOnly` records the missing mode as `NotRun`.

- [ ] **Step 1: Write failing theme safety tests with an injected registry adapter**

Do not touch the real registry in unit tests. Cover original value present/absent, DWORD type/value preservation, apply failure, broadcast failure, restore failure, repeated restore, corrupt journal, current live owner, and recovery after a simulated dead owner. The coordinator may address only:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme
```

Reject every other hive/key/value. Write the recovery journal with `CreateNew`, product/toolkit hashes, owner PID/start time, original presence/type/value, requested mode, and checksum before the registry write. Never change `SystemUsesLightTheme`, wallpaper, accent, high contrast, or a Windows Settings window.

- [ ] **Step 2: Write the failing visual scenario test**

Assert a matrix with both light/dark modes, all four pages, client viewports `1024×720`, `1280×800`, and `1600×900`, plus these states: empty/selected inbox, queued/ready processing, selected library, open/closed compact drawer, taxonomy selection, settings folder form, batch metadata dialog, conflict dialog, library batch edit dialog, taxonomy merge dialog, diagnostics running/complete, validation error, and disabled primary action.

Assert keyboard steps use focus plus `send-keys --via send-input` for Tab, Shift+Tab, arrows, Enter, Space, and Escape, and read the focused element from UIA after every injection. No visual pass may be inferred from screenshot creation alone; UIA bounds/state and scenario facts must also pass.

- [ ] **Step 3: Implement crash-safe temporary app-theme switching**

Place the single recovery journal under `%LOCALAPPDATA%\TraceFold.UiAcceptance\theme-recovery.json`, not under the product's data root. On every runner start, before preflight, validate any existing journal and restore only when its recorded owner is no longer the same live process. Refuse to start if ownership cannot be proven. After journaling, write DWORD `1` for light or `0` for dark, broadcast `WM_SETTINGCHANGE`, start a fresh TraceFold process, and confirm the observed theme brush/background through screenshot metadata and pixel sampling. Restore the exact original registry state in `finally` and broadcast again.

The one-click launcher must state before execution that the Windows **app theme** will change briefly and be restored. Any apply/restore uncertainty makes the visual scenario `Failed` and preserves the journal for explicit recovery; it must never silently delete recovery evidence. `CurrentOnly` performs no registry write but cannot produce a full Stage 5C-C visual pass.

- [ ] **Step 4: Implement keyboard and layout assertions**

For every page and dialog, verify visible controls stay within the client viewport, no required content is clipped behind the title bar/taskbar, the intended scroll container exposes `ScrollPattern` when needed, and primary/secondary actions are reachable. At `1024×720`, selection enables but does not auto-open the inspector; invoking the open button moves focus into the drawer; Escape closes it and restores focus to the selected row. At wide widths, the same inspector content appears inline and the compact button/drawer is absent or offscreen.

Verify logical tab order, arrow-key list navigation, Enter/Space activation, visible focus, and no focus trap. Never type free text with global keys; target the specific owned input. Capture `--capture-screen` only for product-owned flyouts/popups after foreground verification; ordinary screenshots use WGC.

- [ ] **Step 5: Build the inspectable screenshot index**

Generate a static `index.html` and `screenshots.json` inside the report directory. Group by theme, viewport, page/dialog, and state; show image, client dimensions, DPI, product hash, scenario/step status, and links to the corresponding redacted UIA JSON. Use only local relative links and no script from a CDN. The template belongs only to the toolkit package.

- [ ] **Step 6: Run tests and the real visual matrix**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests/TraceFold.Media.UiAcceptance.Support.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
pwsh -NoProfile -File scripts/run-stage5cc-ui-acceptance.ps1 -ProductDirectory .superpowers/stage5cc-product -WinAppCliArchivePath '<local-path>\winappcli-x64.zip' -OutputDirectory .superpowers/stage5cc-visual -Scenario Visual -ThemeMatrix Both
```

Expected: `Visual` is `Passed`; all matrix entries and both theme sets exist; the Windows app-theme value is byte-for-byte restored; the recovery journal, owned product processes, and owned temp roots are gone. Manually open `index.html` and inspect every image before accepting the task.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support apps/media-desktop/tests/TraceFold.Media.UiAcceptance.Support.Tests scripts/ui-acceptance scripts/test-stage5cc-ui-acceptance-toolkit.ps1
git commit -m "test(media-desktop): verify visual theme and keyboard matrix"
```

---

## Phase E — Packaging, Documentation, and Release Verification

### Task 14: Produce Separate Product and Offline Acceptance Packages

**Files:**
- Modify: `scripts/publish-media-desktop.ps1`
- Modify: `scripts/smoke-media-desktop-package.ps1`
- Create: `scripts/smoke-stage5cc-ui-acceptance-toolkit.ps1`
- Create: `scripts/Run-Stage5CC-Acceptance.cmd`
- Create: `scripts/ui-acceptance/README.txt`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage5CCPackagingContractTests.cs`
- Create: `docs/media-desktop-stage5cc-visual-uia-test-guide.md`
- Create: `docs/media-desktop-stage5cc-visual-uia-acceptance.md`
- Modify: `README.md`

**Deliverables:**
- `dist/TraceFold-MediaDesktop-Stage5C-C-win-x64/`
- `dist/TraceFold-MediaDesktop-Stage5C-C-win-x64.zip`
- `dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64/`
- `dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip`
- Product informational version `4.0.0-stage5cc+<40-character-source-commit>`.

- [ ] **Step 1: Write failing package-contract tests**

Assert that both publish/smoke `ValidateSet` declarations include exact `5C-C`; the publish script accepts `-WinAppCliArchivePath`; Stage 5C-C verifies archive filename, version, architecture, size, and SHA-256 before extraction; product and toolkit names are exact; and publishing is transactional for both artifacts.

Assert product exclusion patterns cover `winapp*`, `UiAcceptance`, scenario scripts, support binaries, toolkit manifests/licenses, fixtures, screenshot/report templates, and acceptance docs. Assert toolkit inclusion covers the extracted pinned driver, manifest/license/notices, self-contained support executable, runner/modules/scenarios/template, fixed fixtures, launcher, and guide. Assert no product `.csproj` references the support project.

```csharp
[Fact]
public void Stage5cc_product_and_toolkit_are_separate_offline_artifacts()
{
    string script = ReadRepoFile("scripts/publish-media-desktop.ps1");
    Assert.Contains("TraceFold-MediaDesktop-Stage5C-C-win-x64", script);
    Assert.Contains("TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64", script);
    Assert.Contains("88735CE6C2582AC5FAC6200194BF62467FDD72B44B2D230F3A4ED059FA79EE7D", script);
    Assert.DoesNotContain("Invoke-WebRequest", script, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run package tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~Stage5CCPackagingContractTests"
```

Expected: FAIL because `5C-C` and the separate toolkit pipeline do not exist.

- [ ] **Step 3: Extend publishing transactionally**

Add `5C-C` to both stage `ValidateSet` declarations. For Stage 5C-C, require an absolute local `-WinAppCliArchivePath`; reject a reparse-point archive, a filename other than `winappcli-x64.zip`, size/hash mismatch, duplicate/unexpected executable payload, or extraction outside the staging root. Never download or install the driver.

Publish the product exactly as the current self-contained unpackaged app with package-local FFmpeg/FFprobe and `package-version.json`. Publish the support CLI self-contained into the toolkit staging tree. Copy the extracted driver, checked-in metadata/licenses, PowerShell runner/modules/scenarios, launcher, report template, root `README.txt`, three hash-verified fixtures, and test guide into the toolkit. The packaged runner accepts the embedded driver directory instead of an archive and verifies its staged manifest/hash before use.

Build and hash both staging trees, create both ZIPs, smoke them, then atomically replace the two final directories/ZIPs together. On any error, restore both previous pairs. Validate all resolved recursive move/delete targets as direct children of `dist` and reject reparse points before mutation.

- [ ] **Step 4: Keep the product package free of acceptance code**

Have the publisher and smoke script enumerate the product tree and fail on any forbidden filename/content. The toolkit may contain test components but must not contain the user's data, generated database, reports, screenshots, `.superpowers`, source tree, NuGet cache, or the original driver ZIP. Verify `TraceFold.Media.exe` still launches as a native independent window and that launch creates no Edge/Chrome/WebView-host process.

For the Stage 5C-C product smoke, execute the existing Stage 5A, 5B, 5C, and 5C-B package probes in separate owned roots to retain batch/preprocessing, library naming/undo, taxonomy maintenance, and diagnostics coverage. Do not add a new product-only `--stage5cc-probe` switch.

- [ ] **Step 5: Implement the toolkit smoke and non-flashing launcher**

`smoke-stage5cc-ui-acceptance-toolkit.ps1` verifies structure, driver/support identities, fixture hashes, script signatures/content contracts, fake-driver self-tests, and that the sibling product path is outside the toolkit. Its optional `-RunStartupLayout` performs the real published-window compatibility scenario.

`Run-Stage5CC-Acceptance.cmd` resolves its own absolute directory without changing the user's working directory. It locates the sibling unpacked product directory or accepts a path typed by the user, starts built-in `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe` with `-NoProfile -ExecutionPolicy Bypass`, runs all scenarios with `-ThemeMatrix Both`, defaults output to `results/<run-id>` under the toolkit, prints the final report/index paths, and always pauses on success, blocked, or failure so the console cannot flash and disappear. Repository scripts must remain compatible with Windows PowerShell 5.1 as well as PowerShell 7. It never opens a browser; the screenshot index remains an optional local file the user can open manually.

- [ ] **Step 6: Write the operator and acceptance documents**

The test guide must state prerequisites, package layout, the brief current-user app-theme change and exact restoration guarantee, how to recover using a preserved journal, how `Blocked` differs from `Failed`, report/evidence locations, how to run one or selected scenarios, and how to inspect screenshots without a browser automation dependency. It must explicitly say no VM was used and a clean VM result is not claimed.

The acceptance record starts with all rows `NotRun`. Include automated rows for product smoke; toolkit smoke; startup/layout; UIA patterns; the main chain and MP4/M4A; stale-inspector refresh; metadata cardinality; source cleanup; Keep Both/Replace/Skip; processing failure/retry; batch metadata/partial failure/undo; library filter/naming/undo; taxonomy merge/delete/undo; diagnostics read-only/refresh/cleanup; single-instance safety; light/dark visual matrix; keyboard/focus/drawer/dialogs; theme restoration; and process/root cleanup. Include separate user-owned manual rows for launcher persistence/summary, screenshot density/spacing, light/dark readability, drawer intuition/focus, Windows folder picker, registered log-folder entry, clipboard redaction, icon/taskbar/title/native close behavior, product-package separation, and final process/temp-root absence. Only the automated runner may fill automated evidence references; only the user may change manual rows to `Passed`.

- [ ] **Step 7: Run focused package tests and commit**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~Stage5CCPackagingContractTests|FullyQualifiedName~PackageProbe"
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
git add scripts/publish-media-desktop.ps1 scripts/smoke-media-desktop-package.ps1 scripts/smoke-stage5cc-ui-acceptance-toolkit.ps1 scripts/Run-Stage5CC-Acceptance.cmd scripts/ui-acceptance/README.txt apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage5CCPackagingContractTests.cs docs/media-desktop-stage5cc-visual-uia-test-guide.md docs/media-desktop-stage5cc-visual-uia-acceptance.md README.md
git commit -m "build(media-desktop): package stage 5c-c acceptance toolkit"
```

- [ ] **Step 8: Run the complete offline regression gate**

```powershell
$traceFoldPackageCache = 'T:\Windows\Documents\GitHub\TraceFold\apps\media-desktop\.packages'
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source $traceFoldPackageCache -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers
pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1
```

Expected: restore uses only the supplied repository cache, build has 0 warnings/0 errors, all tests pass with a count greater than the 970-test baseline, and toolkit self-tests pass.

- [ ] **Step 9: Publish and smoke both final artifacts**

```powershell
pwsh -NoProfile -File scripts/publish-media-desktop.ps1 -StageCheckpoint '5C-C' -FfmpegSourceRoot '<local-ffmpeg-root>' -NugetPackageSource $traceFoldPackageCache -WinAppCliArchivePath '<local-path>\winappcli-x64.zip'
pwsh -NoProfile -File scripts/smoke-media-desktop-package.ps1 -StageCheckpoint '5C-C' -PackageDirectory dist/TraceFold-MediaDesktop-Stage5C-C-win-x64
pwsh -NoProfile -File scripts/smoke-stage5cc-ui-acceptance-toolkit.ps1 -ToolkitDirectory dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64 -ProductDirectory dist/TraceFold-MediaDesktop-Stage5C-C-win-x64 -RunStartupLayout
```

Expected: both smokes pass, the product opens a native window, no browser is spawned, all earlier business probes pass, and the product/toolkit separation checks pass.

- [ ] **Step 10: Run full acceptance from the packaged artifacts**

```powershell
pwsh -NoProfile -File dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64/scripts/run-stage5cc-ui-acceptance.ps1 -ProductDirectory dist/TraceFold-MediaDesktop-Stage5C-C-win-x64 -ToolkitDirectory dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64 -OutputDirectory dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64/results/final-acceptance -Scenario All -ThemeMatrix Both
```

Expected: every required automated scenario is `Passed`, no required scenario is `Blocked`/`NotRun`, the theme is exactly restored, all owned processes and temporary roots are gone, `cleanVmStatus` remains honestly `NotRun`, and the local screenshot index is complete.

- [ ] **Step 11: Inspect, review, and record immutable release evidence**

Open the local screenshot index manually and inspect every image at full size. Record the user's manual result only after they report it. Run `superpowers:requesting-code-review`, address findings through `superpowers:receiving-code-review`, then invoke `superpowers:verification-before-completion` and rerun any command whose evidence is stale.

```powershell
git diff --check
git status --short
Get-FileHash dist/TraceFold-MediaDesktop-Stage5C-C-win-x64.zip -Algorithm SHA256
Get-FileHash dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip -Algorithm SHA256
Get-Item dist/TraceFold-MediaDesktop-Stage5C-C-win-x64.zip,dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip | Select-Object Name,Length
Get-Content dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64/results/final-acceptance/report.json -Raw | ConvertFrom-Json | Select-Object overallStatus,cleanVmStatus,cleanupStatus
```

Expected: no whitespace errors or uncommitted source changes; both package sizes and hashes are recorded in the handoff together with product version/source commit; `overallStatus` and `cleanupStatus` are `Passed`; `cleanVmStatus` is `NotRun`.

- [ ] **Step 12: Commit any review-only corrections and present the branch**

If review required changes, rerun the affected focused/full gates and commit each logical correction. Then invoke `superpowers:finishing-a-development-branch` and present integration choices. Do not merge to `main`, tag, or publish externally without the user's explicit instruction.

---

## Specification Coverage Checklist

- [ ] Explorer-style three-pane architecture, narrower navigation, 38-DIP balanced rows, semantic colors, and theme following: Tasks 1–5 and 13.
- [ ] `1024×720` minimum, wide `>=1280`, compact navigation/drawer, state preservation, and focus return: Tasks 2–6, 10, and 13.
- [ ] Multiple authors/tags, one series, display-name-only rename, MP4/M4A preprocessing, source cleanup, and image parity: Tasks 3–4 and 11.
- [ ] Windows-style conflict preview and Keep Both/Replace/Skip choices: Tasks 6 and 12.
- [ ] Extensible preprocessing remains in existing Application/Infrastructure abstractions and is not hard-coded into UI/toolkit: global constraints and Tasks 3, 7, and 11.
- [ ] OneCommander-style library batch naming, filter, preview, apply, and undo: Tasks 4, 6, and 12.
- [ ] Taxonomy merge/delete guards and undo; diagnostics read-only/cancellation/cleanup: Tasks 5–7 and 12.
- [ ] Stable IDs, Chinese accessible names, native UIA patterns, keyboard/focus/scroll/dialog reachability: Tasks 0, 2–7, 10, and 13.
- [ ] Real published native window, PID targeting, no browser/folder-picker/system-window automation: Tasks 9–14.
- [ ] Fixed verified fixtures, fresh owned roots, read-only state verification, no test mutation backdoor: Tasks 8–12.
- [ ] Once-only mutating actions, observation waits, evidence on failure, safe process/root cleanup: Tasks 8–13.
- [ ] Pinned standalone Microsoft `winapp` 0.5.0, fully offline runner, no driver archive committed: Tasks 8–10 and 14.
- [ ] Separate product/toolkit packages, transactional publish, package identity, no test code in product: Task 14.
- [ ] Light/dark and viewport screenshot matrix, crash-safe app-theme restoration, inspectable index: Task 13.
- [ ] Current-host acceptance required, clean VM honestly not run, user-owned manual screenshot decision: Tasks 13–14.

## Plan Completion Review

Before implementation starts, verify this plan itself:

```powershell
$planPath = 'docs/superpowers/plans/2026-09-02-native-media-desktop-stage5cc-visual-uia-closure.md'
$placeholderTerms = @('T'+'BD','T'+'ODO','FIX'+'ME','implement '+'later','appro'+'priate','similar '+'to','and so '+'on')
foreach ($term in $placeholderTerms) { rg -n -F $term $planPath }
rg -n "TraceFold.Media.UiAcceptance.Support|ResponsiveInspectorHost|WorkspaceLayoutPolicy|Stage5C-C|5C-C|stage5cc" docs/superpowers/plans/2026-09-02-native-media-desktop-stage5cc-visual-uia-closure.md
git diff --check -- docs/superpowers/plans/2026-09-02-native-media-desktop-stage5cc-visual-uia-closure.md
```

Expected: the placeholder search has no matches; type/project/stage names are consistent; every approved spec section maps to at least one task; every task has files, a failing test, implementation, verification, and commit; no step instructs an internet download, user-data access, external-window automation, coordinate click, repeated mutation, or unverifiable completion claim.
