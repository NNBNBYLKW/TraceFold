# Shared builders for the startup/layout scenario contract.
#
# ASCII only, like every other toolkit file: the scenario contract under test never depends on
# the Chinese product names, which are locked separately by UiAutomationContractTests.

function New-StartupLayoutContract {
    param(
        [Parameter(Mandatory)][string] $Path,
        [string] $RowSelectorPolicy = 'accessible_name_prefix',
        [string] $AssetSha256 = 'B5B85F2CA95EA5EB781B42D202D1C3DB361BF37F5BF4B3107A79469FC716A1A3',
        [string] $InboxRowPhase = 'inbox-seeded'
    )

    $entries = @(
        [ordered]@{ file='MainWindow.xaml'; automationId='TraceFoldMainWindow'; name='TraceFold main window'; controlType='Grid'; nativeControlType='Group'; requiredPatterns=@(); scope='window'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Shell/ShellPage.xaml'; automationId='NavInbox'; name='Inbox navigation'; controlType='NavigationViewItem'; nativeControlType='ListItem'; requiredPatterns=@('SelectionItem'); scope='shell'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Pages/InboxPage.xaml'; automationId='InboxInspectorOpenButton'; name='Open inbox file panel'; controlType='Button'; nativeControlType='Button'; requiredPatterns=@('Invoke'); scope='pages_inboxpage'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Pages/InboxPage.xaml'; automationId='InspectorPane'; name='Inbox file inspector'; controlType='Border'; nativeControlType='Border'; requiredPatterns=@(); scope='page_host:inbox'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Shell/ShellPage.xaml'; automationId='TraceFoldShell'; name='TraceFold shell'; controlType='Grid'; nativeControlType='Group'; requiredPatterns=@(); scope='shell'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Pages/InboxPage.xaml'; automationId='InboxPageRoot'; name='Inbox page root'; controlType='Grid'; nativeControlType='Group'; requiredPatterns=@(); scope='pages_inboxpage'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Pages/SettingsPage.xaml'; automationId='SettingsStatusMessage'; name='Settings status message'; controlType='TextBlock'; nativeControlType='Text'; requiredPatterns=@(); scope='pages_settingspage'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Pages/SettingsPage.xaml'; automationId='InboxFolderPath'; name='Inbox folder path'; controlType='TextBox'; nativeControlType='Edit'; requiredPatterns=@('Value'); scope='pages_settingspage'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Pages/SettingsPage.xaml'; automationId='LibraryFolderPath'; name='Library folder path'; controlType='TextBox'; nativeControlType='Edit'; requiredPatterns=@('Value'); scope='pages_settingspage'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Pages/SettingsPage.xaml'; automationId='WorkFolderPath'; name='Work folder path'; controlType='TextBox'; nativeControlType='Edit'; requiredPatterns=@('Value'); scope='pages_settingspage'; dynamicSelectorPolicy='static_id' }
    )

    $contract = [ordered]@{
        schemaVersion  = 1
        entries        = $entries
        # The inbox row only exists after the scenario materialises the fixture; the library row
        # belongs to a later phase and must never be required by this scenario.
        dynamicRows    = @(
            [ordered]@{ file='Pages/InboxPage.xaml'; controlType='Grid'; nativeControlType='ListItem'; selector=$RowSelectorPolicy; fixedAccessibleName='Inbox row paired.mp4'; fixedFilename='paired.mp4'; stateSuffixVaries=$true; forbidGuidSelectors=$true; phase=$InboxRowPhase }
            [ordered]@{ file='Pages/LibraryPage.xaml'; controlType='Grid'; nativeControlType='ListItem'; selector='accessible_name_prefix'; fixedAccessibleName='Library row photo.jpg'; fixedFilename='photo.jpg'; forbidGuidSelectors=$true; phase='post-ingest' }
        )
        fixtureAssets  = @(
            [ordered]@{ fixedFilename='paired.mp4'; repositoryPath='apps/media-api/tests/fixtures/media/paired.mp4'; sha256=$AssetSha256; sizeBytes=2405; seedDirectory='inbox' },
            [ordered]@{ fixedFilename='paired.m4a'; repositoryPath='apps/media-api/tests/fixtures/media/paired.m4a'; sha256='187F3BE0AB2C07C6A967840BEBFA0EB1646227B52179387F74AB4D8BBC64AAC1'; sizeBytes=9988; seedDirectory='inbox' }
        )
        sessionPreparation = [ordered]@{
            materializedRowPhase           = 'inbox-seeded'
            seedFilenames                  = @('paired.mp4','paired.m4a')
            savedStatusText                = 'Folder settings saved.'
            savedStatusAutomationId        = 'SettingsStatusMessage'
            settingsNavigationAutomationId = 'NavSettings'
            inboxNavigationAutomationId    = 'NavInbox'
            saveFolderSettingsAutomationId = 'SaveFolderSettings'
            scanTriggerAutomationId        = 'InboxRefreshButton'
            folderInputs                   = @(
                [ordered]@{ key='inbox'; automationId='InboxFolderPath'; relativeDirectory='inbox' }
                [ordered]@{ key='library'; automationId='LibraryFolderPath'; relativeDirectory='library' }
                [ordered]@{ key='work'; automationId='WorkFolderPath'; relativeDirectory='work' }
            )
        }
        startupScopes  = @('window', 'shell', 'pages_inboxpage', 'page_host:inbox')
        phaseRequiredIds = [ordered]@{
            startupSettings = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','SettingsStatusMessage','InboxFolderPath','LibraryFolderPath','WorkFolderPath')
            inboxReady      = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot')
            rowReady        = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot')
            rowSelected     = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot')
            drawerOpen      = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot','InboxInspectorOpenButton','InspectorPane')
            drawerClosed    = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot','InboxInspectorOpenButton')
        }
        layoutProbe    = [ordered]@{
            navigationItemAutomationId      = 'NavInbox'
            navigationPaneAutomationId      = 'PaneRoot'
            navigationPaneClassName         = 'SplitViewPane'
            navigationPaneParentAutomationId = 'Navigation'
            inlineInspectorAutomationId     = 'InspectorPane'
            inspectorOpenButtonAutomationId = 'InboxInspectorOpenButton'
            pixelTolerance                  = 2
        }
        layoutViewports = @(
            [ordered]@{ widthDip=1024; heightDip=720; navigationPaneWidthDip=48; inlineInspector=$false }
            [ordered]@{ widthDip=1280; heightDip=800; navigationPaneWidthDip=176; inlineInspector=$true }
            [ordered]@{ widthDip=1600; heightDip=900; navigationPaneWidthDip=176; inlineInspector=$true }
        )
    }

    [IO.File]::WriteAllText($Path, ($contract | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}

function New-StartupLayoutNode {
    param(
        # Not Mandatory: a real inbox row exposes no AutomationId at all, and a Mandatory
        # parameter would block the run by prompting instead of accepting its absence.
        [string] $Id = '',
        [Parameter(Mandatory)][string] $ControlType,
        [Parameter(Mandatory)][string] $Name,
        [bool] $Enabled = $true,
        [bool] $Offscreen = $false,
        [int] $X = 0,
        [int] $Y = 0,
        [int] $Width = 0,
        [int] $Height = 0,
        [string] $GradientSelector = '',
        [object[]] $Children = @()
    )

    # The flat projection the scenario's assertions work on. The fake driver emits the pinned
    # driver's nested windows[].elements[].children shape; this helper is only used to build the
    # nested payload below, so the tests exercise the same adapter the real response goes through.
    $node = [ordered]@{
        type         = $ControlType
        name         = $Name
        automationId = $Id
        className    = 'Fake.' + $ControlType
        isEnabled    = $Enabled
        isOffscreen  = $Offscreen
        x            = $X
        y            = $Y
        width        = $Width
        height       = $Height
        selector     = $(if ([string]::IsNullOrEmpty($GradientSelector)) { 'sel-' + $Id } else { $GradientSelector })
        isInvokable  = $false
    }
    if (@($Children).Count -gt 0) { $node['children'] = @($Children) }
    return ,$node
}

<#
.SYNOPSIS
    Wraps driver nodes in the pinned driver's real inspect response envelope.

.DESCRIPTION
    The pinned driver answers 'ui inspect --json' with
    { depth, interactive, hideDisabled, hideOffscreen, windows[ { hwnd, title, elementCount, elements } ] }.
    The fake must produce that shape, otherwise the scenario's adapter would never be exercised.
#>
function New-StartupLayoutWindowPayload {
    param(
        [Parameter(Mandatory)][object[]] $Elements,
        # Not Mandatory: a Mandatory parameter prompts even when it has a default, which would
        # hang a non-interactive self-test run.
        [string] $WindowTitle = 'Fake product window'
    )

    return ,[ordered]@{
        depth          = 8
        interactive    = $false
        hideDisabled   = $false
        hideOffscreen  = $false
        windows        = @(
            [ordered]@{
                hwnd         = 4242
                title        = $WindowTitle
                elementCount = @($Elements).Count
                elements     = @($Elements)
            }
        )
    }
}

function New-StartupLayoutTree {
    param(
        [Parameter(Mandatory)][int] $NavigationWidth,
        [Parameter(Mandatory)][bool] $InlineInspector,
        [bool] $InspectorOpenButtonEnabled = $false,
        [int] $InspectorX = 900,
        [switch] $IncludeInboxRow,
        [switch] $OmitInspectorNode,
        [switch] $DuplicateNavigationId,
        [switch] $SettingsPage
    )

    $inspector = @()
    if (-not $OmitInspectorNode) {
        $inspector = @(
            if ($InlineInspector) {
                New-StartupLayoutNode -Id 'InspectorPane' -ControlType 'Border' -Name 'Inbox file inspector' -X $InspectorX -Y 0 -Width 320 -Height 720
            }
            else {
                New-StartupLayoutNode -Id 'InspectorPane' -ControlType 'Border' -Name 'Inbox file inspector' -Offscreen $true -Width 0 -Height 0
            }
        )
    }

    $row = @()
    if ($IncludeInboxRow) {
        # Its runtime id is a generated value, so any attempt to address the row by id fails. The
        # accessible name keeps the manifest's fixed prefix and gains the product's state suffix.
        $rowName = 'Inbox row paired.mp4, state pending'
        $row = @(
            New-StartupLayoutNode -ControlType 'ListItem' -Name $rowName -GradientSelector 'itm-pairedmp4-0001' -X 100 -Y 100 -Width 200 -Height 60
            New-StartupLayoutNode -Id 'InboxCard_3f2504e04f8911d39a0c0305e82c3301' -ControlType 'Group' -Name $rowName -X 100 -Y 100 -Width 200 -Height 60
        )
    }

    $navigation = New-StartupLayoutNode -Id 'Navigation' -ControlType 'Unknown(50025)' `
        -Name 'Navigation' -Width 1024 -Height 720 -Children @(
            New-StartupLayoutNode -Id 'PaneRoot' -ControlType 'Window' -Name 'Pane' `
                -Width $NavigationWidth -Height 720 -Children @(
                    New-StartupLayoutNode -Id 'NavInbox' -ControlType 'ListItem' `
                        -Name 'Inbox navigation' -Width $NavigationWidth -Height 40
                )
        )

    # The window scope wraps the shell scope, which wraps the page scopes: the fake response has
    # real nesting so the scenario's recursive adapter is genuinely exercised.
    if ($SettingsPage) {
        $settingsChildren = @(
            New-StartupLayoutNode -Id 'InboxFolderPath' -ControlType 'Edit' -Name 'Inbox folder path' -X 825 -Y 424 -Width 633 -Height 32
            New-StartupLayoutNode -Id 'LibraryFolderPath' -ControlType 'Edit' -Name 'Library folder path' -X 825 -Y 496 -Width 633 -Height 32
            New-StartupLayoutNode -Id 'WorkFolderPath' -ControlType 'Edit' -Name 'Work folder path' -X 825 -Y 568 -Width 633 -Height 32
        )
        $shellChildren = @(
            $navigation
        ) + $settingsChildren
        if ($DuplicateNavigationId) {
            $shellChildren += New-StartupLayoutNode -Id 'NavInbox' -ControlType 'ListItem' -Name 'Inbox navigation' -Width $NavigationWidth -Height 40
        }
        $root = New-StartupLayoutNode -Id 'TraceFoldMainWindow' -ControlType 'Group' -Name 'TraceFold main window' -Width 1024 -Height 720 -Children @(
            New-StartupLayoutNode -Id 'TraceFoldShell' -ControlType 'Group' -Name 'TraceFold shell' -Width 1024 -Height 720 -Children @(
                $shellChildren
                New-StartupLayoutNode -Id 'SettingsStatusMessage' -ControlType 'Text' -Name 'Settings status message' -Enabled $InspectorOpenButtonEnabled -Width 112 -Height 19
            )
        )
        return ,(New-StartupLayoutWindowPayload -Elements @($root))
    }

    $pageChildren = @(
        New-StartupLayoutNode -Id 'InboxInspectorOpenButton' -ControlType 'Button' -Name 'Open inbox file panel' -Enabled $InspectorOpenButtonEnabled -X 700 -Y 60 -Width 32 -Height 32
    ) + $row + $inspector

    $inboxChildren = @(
        $navigation
        New-StartupLayoutNode -Id 'InboxPageRoot' -ControlType 'Group' -Name 'Inbox page root' -Width $NavigationWidth -Height 40 -Children $pageChildren
    )
    if ($DuplicateNavigationId) {
        $inboxChildren += New-StartupLayoutNode -Id 'NavInbox' -ControlType 'ListItem' -Name 'Inbox navigation' -Width $NavigationWidth -Height 40
    }

    $root = New-StartupLayoutNode -Id 'TraceFoldMainWindow' -ControlType 'Group' -Name 'TraceFold main window' -Width 1024 -Height 720 -Children @(
        New-StartupLayoutNode -Id 'TraceFoldShell' -ControlType 'Group' -Name 'TraceFold shell' -Width 1024 -Height 720 -Children $inboxChildren
    )

    return ,(New-StartupLayoutWindowPayload -Elements @($root))
}

<#
.SYNOPSIS
    The seven trees the scenario must observe, in order.

.DESCRIPTION
    Three startup viewports with an empty inbox, then - after the product UI has been driven to
    point the folders at the owned root and to scan - the inbox row appears, gets selected, opens
    the drawer, and closes it again.
#>
function New-StartupLayoutTreeSequence {
    param(
        [Parameter(Mandatory)][string] $Path,
        [switch] $OmitInspectorNode,
        [switch] $OmitMaterializedRow
    )

    $settings = New-StartupLayoutTree -NavigationWidth 176 -InlineInspector $true -SettingsPage -OmitInspectorNode:$OmitInspectorNode
    $settingsConfigured = New-StartupLayoutTree -NavigationWidth 176 -InlineInspector $true -InspectorOpenButtonEnabled $true -SettingsPage -OmitInspectorNode:$OmitInspectorNode

    $rowSwitch = -not $OmitMaterializedRow
    $inboxEmpty = New-StartupLayoutTree -NavigationWidth 176 -InlineInspector $true -OmitInspectorNode:$OmitInspectorNode
    $rowPresent = New-StartupLayoutTree -NavigationWidth 48 -InlineInspector $false -IncludeInboxRow:$rowSwitch -OmitInspectorNode:$OmitInspectorNode
    $rowSelected = New-StartupLayoutTree -NavigationWidth 48 -InlineInspector $false -InspectorOpenButtonEnabled $true -IncludeInboxRow:$rowSwitch -OmitInspectorNode:$OmitInspectorNode
    $viewport1024 = New-StartupLayoutTree -NavigationWidth 48 -InlineInspector $false -InspectorOpenButtonEnabled $true -IncludeInboxRow:$rowSwitch -OmitInspectorNode:$OmitInspectorNode
    $viewport1280 = New-StartupLayoutTree -NavigationWidth 176 -InlineInspector $true -InspectorOpenButtonEnabled $true -IncludeInboxRow:$rowSwitch -InspectorX 900 -OmitInspectorNode:$OmitInspectorNode
    $viewport1600 = New-StartupLayoutTree -NavigationWidth 176 -InlineInspector $true -InspectorOpenButtonEnabled $true -IncludeInboxRow:$rowSwitch -InspectorX 1200 -OmitInspectorNode:$OmitInspectorNode
    $drawerOpen = New-StartupLayoutTree -NavigationWidth 48 -InlineInspector $true -InspectorOpenButtonEnabled $true -InspectorX 720 -IncludeInboxRow:$rowSwitch -OmitInspectorNode:$OmitInspectorNode
    $drawerClosed = New-StartupLayoutTree -NavigationWidth 48 -InlineInspector $false -InspectorOpenButtonEnabled $true -IncludeInboxRow:$rowSwitch -OmitInspectorNode:$OmitInspectorNode

    $payloads = @(
        ($settings | ConvertTo-Json -Depth 12 -Compress)
        ($settingsConfigured | ConvertTo-Json -Depth 12 -Compress)
        ($inboxEmpty | ConvertTo-Json -Depth 12 -Compress)
        ($rowPresent | ConvertTo-Json -Depth 12 -Compress)
        # The row is invoked through a selector resolved from its own fresh inspection, because the
        # accessible name carries a status that changes, so the row-ready tree is observed twice.
        ($rowPresent | ConvertTo-Json -Depth 12 -Compress)
        ($rowSelected | ConvertTo-Json -Depth 12 -Compress)
        ($viewport1024 | ConvertTo-Json -Depth 12 -Compress)
        ($viewport1280 | ConvertTo-Json -Depth 12 -Compress)
        ($viewport1600 | ConvertTo-Json -Depth 12 -Compress)
        ($drawerOpen | ConvertTo-Json -Depth 12 -Compress)
        ($drawerClosed | ConvertTo-Json -Depth 12 -Compress)
    )

    [IO.File]::WriteAllText($Path, ($payloads | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}

function New-StartupLayoutScenarioArguments {
    param(
        [Parameter(Mandatory)][string] $Base,
        [Parameter(Mandatory)][hashtable] $Overrides
    )

    $contractPath = Join-Path $Base 'contract.json'
    $treePath = Join-Path $Base 'tree-sequence.json'
    if (-not (Test-Path -LiteralPath $contractPath)) { New-StartupLayoutContract -Path $contractPath }
    if (-not (Test-Path -LiteralPath $treePath)) { New-StartupLayoutTreeSequence -Path $treePath }

    $arguments = @{
        ScenarioId        = 'startup-layout'
        ExecutablePath    = (Join-Path $Base 'TraceFold.Media.exe')
        ContractPath      = $contractPath
        OwnedRoot         = (Join-Path $Base 'owned')
        RunToken          = 'scenario-token'
        OutputDirectory   = (Join-Path $Base 'out')
        SupportPath       = (Join-Path $script:uiRoot 'tests/fixtures/fake-support.ps1')
        WinAppPath        = (Join-Path $script:uiRoot 'tests/fixtures/fake-winapp.ps1')
        LaunchProcess     = { param($path, $environment) [pscustomobject]@{ ProcessId = 4242; StartUtcTicks = 638000000000000000; ExecutablePath = $path } }
        DesktopCheck      = { $true }
        ForegroundCheck   = { param($id) $true }
    }
    foreach ($key in $Overrides.Keys) { $arguments[$key] = $Overrides[$key] }
    return $arguments
}

<#
.SYNOPSIS
    Builds an isolated repository copy whose scenario directory answers the registry.

.DESCRIPTION
    The entry script is exercised against a copy so these cases never depend on which scenario
    happens to be implemented in the working tree at that moment: another slice landing or a
    placeholder being renamed elsewhere cannot turn them green or red. Entry file names are
    taken from the registry itself, so the mapping is never restated by the fixture.
#>
function New-Stage5CCFakeRepository {
    param(
        [Parameter(Mandatory)][string] $Path,
        [string[]] $Missing = @()
    )

    $scripts = Join-Path $Path 'scripts'
    $fakeUiRoot = Join-Path $scripts 'ui-acceptance'
    $fakeScenarios = Join-Path $fakeUiRoot 'scenarios'
    [void][IO.Directory]::CreateDirectory($fakeScenarios)
    foreach ($module in @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner', 'Workflows')) {
        Copy-Item -LiteralPath (Join-Path $script:uiRoot ('TraceFold.UiAcceptance.' + $module + '.psm1')) -Destination (Join-Path $fakeUiRoot ('TraceFold.UiAcceptance.' + $module + '.psm1'))
    }
    Copy-Item -LiteralPath (Join-Path $script:root 'scripts/run-stage5cc-ui-acceptance.ps1') -Destination (Join-Path $scripts 'run-stage5cc-ui-acceptance.ps1')
    $manifestDirectory = Join-Path $Path 'apps/media-desktop/tools/winapp/win-x64'
    [void][IO.Directory]::CreateDirectory($manifestDirectory)
    Copy-Item -LiteralPath (Join-Path $script:root 'apps/media-desktop/tools/winapp/win-x64/manifest.json') -Destination (Join-Path $manifestDirectory 'manifest.json')

    foreach ($name in @(Get-UiAcceptanceScenarioName)) {
        if ($Missing -ccontains $name) { continue }
        $entryPath = Join-Path $fakeScenarios (Get-UiAcceptanceScenarioFile -Name $name)
        [IO.File]::WriteAllText($entryPath, ('# Marker for the ' + $name + ' entry.' + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    }

    return $Path
}

function Invoke-Stage5CCEntry {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string] $Scenario
    )

    # A subprocess is used because the entry answers with 'exit', which would end the self-test.
    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
    $entry = Join-Path $RepositoryRoot 'scripts/run-stage5cc-ui-acceptance.ps1'
    $null = & $hostExe -NoProfile -File $entry `
        -ProductDirectory (Join-Path $RepositoryRoot 'no-product') `
        -WinAppCliArchivePath (Join-Path $RepositoryRoot 'no-driver.zip') `
        -OutputDirectory $OutputDirectory -Scenario $Scenario 2>&1
    return $LASTEXITCODE
}

function ConvertFrom-Stage5CCPreservedJson {
    # JSON timestamps must survive as the exact lexemes the writer produced: on PowerShell 7 the
    # bare serializer would bind every ISO string to a DateTime, and the offset and the fractional
    # precision would be gone before anything reads the value. The capability guard keeps the
    # Windows PowerShell behaviour (which preserves strings natively) on its own branch.
    param([Parameter(Mandatory)][string] $Text)

    $convertCommand = Get-Command -Name ConvertFrom-Json -ErrorAction SilentlyContinue
    if ($null -ne $convertCommand -and $convertCommand.Parameters.ContainsKey('DateKind')) {
        return ($Text | ConvertFrom-Json -DateKind String)
    }
    return ($Text | ConvertFrom-Json)
}

function Get-Stage5CCCheckedInstant {
    # The ONE time semantics of every checkedUtc comparison. It reads a value that was PRESERVED
    # from JSON as text and answers with named facts; it never repairs a value, never defaults it
    # and never lets anything but an explicit-offset instant through:
    #   absent-or-null              the field is missing, JSON null, empty or blank
    #   not-a-string:<type>         a JSON value that is not a preserved string
    #   not-an-explicit-offset-...  text that is not an ISO round-trip instant with an explicit
    #                               offset - the culture shaped date a lost-offset round trip
    #                               produces ('10/14/2026 07:21:05') can never pass this gate
    #   unparsable                  the shape matches but the value does not parse
    # The instant is parsed with the invariant culture and kept as UTC ticks, so the comparison
    # cannot depend on the host's day/month order, on the current culture or on a re-formatting
    # that drops the offset or the fractional precision. The parse style below is the one the
    # development probe measured as legal on BOTH fixed hosts (every DateTimeStyles value,
    # RoundtripKind included, parses and preserves the instant there).
    param($Value, [Parameter(Mandatory)][string] $Field)

    $fact = [ordered] @{
        field = $Field; ok = $false; reason = ''; text = ''
        utcTicks = [long] 0; offset = ''; fractionalDigits = -1
    }
    if ($null -eq $Value) { $fact.reason = 'absent-or-null'; return [pscustomobject] $fact }
    if (-not ($Value -is [string])) { $fact.reason = ('not-a-string:' + $Value.GetType().Name); return [pscustomobject] $fact }
    $fact.text = [string] $Value
    if ([string]::IsNullOrWhiteSpace($fact.text)) { $fact.reason = 'absent-or-null'; return [pscustomobject] $fact }
    $pattern = '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$'
    if ($fact.text -cnotmatch $pattern) { $fact.reason = 'not-an-explicit-offset-instant'; return [pscustomobject] $fact }
    $fraction = [regex]::Match($fact.text, '\.(\d{1,7})')
    $fact.fractionalDigits = $(if ($fraction.Success) { $fraction.Groups[1].Value.Length } else { 0 })
    $suffix = [regex]::Match($fact.text, '(Z|[+-]\d{2}:\d{2})$')
    if ($suffix.Success) { $fact.offset = [string] $suffix.Groups[1].Value }
    try {
        $instant = [DateTimeOffset]::Parse($fact.text, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
    }
    catch { $fact.reason = 'unparsable'; return [pscustomobject] $fact }
    $fact.utcTicks = $instant.UtcTicks
    $fact.ok = $true
    $fact.reason = 'explicit-offset'
    return [pscustomobject] $fact
}

function Test-Stage5CCSummaryNotOlder {
    # The shared comparison of two checkedUtc values: both sides must be preserved explicit-offset
    # instants and the order is decided on UTC ticks only. Every caller receives the two facts as
    # well, so a refusal names the side and the reason instead of a bare 'not fresh'.
    param($BeforeValue, $AfterValue)

    $before = Get-Stage5CCCheckedInstant -Value $BeforeValue -Field 'before'
    $after = Get-Stage5CCCheckedInstant -Value $AfterValue -Field 'after'
    $bothOk = ([bool] $before.ok) -and ([bool] $after.ok)
    return [pscustomobject] @{
        ok       = $bothOk
        notOlder = ($bothOk -and ($after.utcTicks -ge $before.utcTicks))
        before   = $before
        after    = $after
    }
}

function Read-Stage5CCSummary {
    param([Parameter(Mandatory)][string] $OutputDirectory)

    $path = Join-Path $OutputDirectory 'summary.json'
    Assert-True ([IO.File]::Exists($path)) 'the run must persist a summary'
    $text = [IO.File]::ReadAllText($path)
    return [pscustomobject]@{
        Text    = $text
        Summary = (ConvertFrom-Stage5CCPreservedJson -Text $text)
    }
}

<#
.SYNOPSIS
    Writes a stand-in support CLI project the entry can publish in seconds.

.DESCRIPTION
    Before the entry runs anything it publishes the support tool and then asks that tool to
    create and delete the owned root. The stand-in implements exactly these two commands and
    keeps the ownership rule that matters: a root is created where it was asked for and is only
    handed back to a caller that proves it knows the run token. It agrees to nothing else, so
    whether the paths and the cleanup line up is decided by the entry's own wiring.

    Every create and delete is journalled when the caller asks for it, because a test has to be
    able to prove that nothing owned was left behind instead of assuming it.
#>
function Write-Stage5CCStubSupport {
    param([Parameter(Mandatory)][string] $ProjectRoot)

    [void][IO.Directory]::CreateDirectory($ProjectRoot)

    $source = @'
using System;
using System.IO;
using System.Text;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "root", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("unsupported support command");
            return 3;
        }

        string parent = ReadOption(args, "--parent");
        string runId = ReadOption(args, "--run-id");
        string token = ReadOption(args, "--token");
        string root = ReadOption(args, "--root");

        if (string.Equals(args[1], "create", StringComparison.Ordinal))
        {
            if (parent.Length == 0 || runId.Length == 0 || token.Length == 0)
            {
                Console.Error.WriteLine("root create needs a parent, a run id and a token");
                return 3;
            }
            string created = Path.Combine(parent, "TraceFold-UIA-" + runId.Replace("-", string.Empty));
            Directory.CreateDirectory(created);
            File.WriteAllText(Path.Combine(created, ".owned-token"), token, new UTF8Encoding(false));
            Journal("create", created);
            Console.Out.WriteLine("{\"root\":" + Quote(created) + "}");
            return 0;
        }

        if (string.Equals(args[1], "delete", StringComparison.Ordinal))
        {
            if (root.Length == 0 || token.Length == 0)
            {
                Console.Error.WriteLine("root delete needs a root and a token");
                return 3;
            }
            string marker = Path.Combine(root, ".owned-token");
            string stored = File.Exists(marker) ? File.ReadAllText(marker) : string.Empty;
            if (!string.Equals(stored, token, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("root ownership token mismatch");
                return 3;
            }
            if (Directory.Exists(root)) { Directory.Delete(root, true); }
            Journal("delete", root);
            return 0;
        }

        Console.Error.WriteLine("unsupported support command");
        return 3;
    }

    private static string ReadOption(string[] args, string name)
    {
        for (int index = 2; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal)) { return args[index + 1]; }
        }
        return string.Empty;
    }

    private static void Journal(string action, string path)
    {
        string journal = Environment.GetEnvironmentVariable("TRACEFOLD_STUB_SUPPORT_JOURNAL");
        if (string.IsNullOrEmpty(journal)) { return; }
        File.AppendAllText(journal, action + "|" + path + Environment.NewLine, new UTF8Encoding(false));
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
'@
    [IO.File]::WriteAllText((Join-Path $ProjectRoot 'Program.cs'), $source, [Text.UTF8Encoding]::new($false))

    $project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>TraceFold.Media.UiAcceptance.Support</AssemblyName>
    <RootNamespace>TraceFold.UIAcceptance.StubSupport</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
  </ItemGroup>
</Project>
'@
    $projectPath = Join-Path $ProjectRoot 'TraceFold.Media.UiAcceptance.Support.csproj'
    [IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))

    return $projectPath
}

<#
.SYNOPSIS
    Writes a scenario entry stub that records every invocation and what it was handed.

.DESCRIPTION
    The point of these cases is the wiring, so the stub writes its own arguments down before it
    answers: what survives is the proof that the entry really passed the prepared paths to the
    scenario, and how many times that happened. It deliberately does not read the paths back from
    the summary, which would only prove what the summary claims.
#>
function Write-Stage5CCScenarioStub {
    param(
        [Parameter(Mandatory)][string] $Path,
        [ValidateSet('Passed', 'Failed')][string] $Outcome = 'Passed'
    )

    $template = @'
param(
    [string] $ScenarioId,
    [string] $ExecutablePath,
    [string] $ContractPath,
    [string] $OwnedRoot,
    [string] $RunToken,
    [string] $OutputDirectory,
    [string] $SupportPath,
    [string] $WinAppPath
)

$journal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
if (-not [string]::IsNullOrWhiteSpace($journal)) {
    $record = [ordered]@{
        scenarioId      = $ScenarioId
        executablePath  = $ExecutablePath
        contractPath    = $ContractPath
        ownedRoot       = $OwnedRoot
        runTokenPresent = (-not [string]::IsNullOrWhiteSpace($RunToken))
        outputDirectory = $OutputDirectory
        supportPath     = $SupportPath
        winAppPath      = $WinAppPath
        supportExists   = [bool] [IO.File]::Exists($SupportPath)
        driverExists    = [bool] [IO.File]::Exists($WinAppPath)
        rootExists      = [bool] [IO.Directory]::Exists($OwnedRoot)
    }
    [IO.File]::AppendAllText($journal, ($record | ConvertTo-Json -Compress) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

return [pscustomobject]@{ Status = '__OUTCOME__'; Reason = '__REASON__' }
'@

    $reason = if ($Outcome -ceq 'Failed') { 'scenario_refused_on_purpose' } else { '' }
    $text = $template.Replace('__OUTCOME__', $Outcome).Replace('__REASON__', $reason)
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
    return $Path
}

function Write-Stage5CCDriverManifest {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $ArchivePath
    )

    # The manifest describes whatever archive this case built, so the entry's length and SHA-256
    # comparison is answered by the same bytes the pinned check performed, never by a copy of the
    # real driver numbers.
    $archive = Get-Item -LiteralPath $ArchivePath
    $manifest = [ordered]@{
        version     = '0.5.0'
        archiveSize = [int64] $archive.Length
        sha256      = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    }
    [IO.File]::WriteAllText($Path, ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    return $Path
}

function New-Stage5CCDriverArchive {
    param(
        [Parameter(Mandatory)][string] $Path,
        [switch] $WithoutDriverExecutable
    )

    $staging = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-driver-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($staging)
    try {
        if ($WithoutDriverExecutable) {
            [IO.File]::WriteAllText((Join-Path $staging 'readme.txt'), 'The pinned executable is missing on purpose.', [Text.UTF8Encoding]::new($false))
        }
        else {
            [IO.File]::WriteAllBytes((Join-Path $staging 'winapp.exe'), [byte[]]@(1, 2, 3))
        }
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $Path -Force
    }
    finally { Remove-UiTestTree -Path $staging }

    return $Path
}

function New-Stage5CCProductStub {
    param([Parameter(Mandatory)][string] $Path)

    [void][IO.Directory]::CreateDirectory($Path)
    [IO.File]::WriteAllBytes((Join-Path $Path 'TraceFold.Media.exe'), [byte[]]@(1, 2, 3))
    return $Path
}

<#
.SYNOPSIS
    Builds a repository copy the entry can actually walk past its precheck.
#>
function New-Stage5CCLiveRepository {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Scenario,
        [ValidateSet('Passed', 'Failed')][string] $ScenarioOutcome = 'Passed'
    )

    $repo = New-Stage5CCFakeRepository -Path $Path
    $null = Write-Stage5CCStubSupport -ProjectRoot (Join-Path $repo 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support')
    $null = Write-Stage5CCScenarioStub -Path (Join-Path $repo ('scripts/ui-acceptance/scenarios/' + (Get-UiAcceptanceScenarioFile -Name $Scenario))) -Outcome $ScenarioOutcome
    return $repo
}

function Invoke-Stage5CCLiveEntry {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $ProductDirectory,
        [Parameter(Mandatory)][string] $WinAppCliArchivePath,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string] $Scenario
    )

    # The entry answers with 'exit', so it can only be observed from a child process.
    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
    $entry = Join-Path $RepositoryRoot 'scripts/run-stage5cc-ui-acceptance.ps1'
    $null = & $hostExe -NoProfile -File $entry `
        -ProductDirectory $ProductDirectory -WinAppCliArchivePath $WinAppCliArchivePath `
        -OutputDirectory $OutputDirectory -Scenario $Scenario 2>&1
    return $LASTEXITCODE
}

function Read-Stage5CCJournal {
    param([Parameter(Mandatory)][string] $Path)

    if (-not [IO.File]::Exists($Path)) { return }
    $lines = @([IO.File]::ReadAllLines($Path) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $records = @($lines | ForEach-Object { $_ | ConvertFrom-Json })
    # No comma: every caller wraps the call in @(), so the single-element cases must also survive
    # that wrap. Wrapping an empty array here would have reported one invocation instead of none.
    return $records
}

function Get-TestCases {
    $script:metadata = @{sourceCommit='abc';productHash='hash';productVersion='1';driverVersion='0.5.0';driverHash='hash';fixtureHashes=@();hostOs='Windows';dpi=96;theme='Light';viewport=@{width=1024;height=720}}
    return @(
        @{
            Name = 'runner cleans up and reports success'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                $script:cleanupCalled = 0
                try {
                    $result = Invoke-UiAcceptanceRun -ScenarioId 'startup' -Scenario { [pscustomobject]@{Status='Passed'} } -Cleanup { $script:cleanupCalled++ } -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata
                    Assert-Equal 0 $result.ExitCode
                    Assert-Equal 1 $script:cleanupCalled
                    $report = Get-Content -LiteralPath $result.ReportPath -Raw | ConvertFrom-Json
                    Assert-Equal 'Passed' $report.scenarios[0].status
                    Assert-Equal 'NotRun' $report.cleanVmStatus
                    Assert-True ($report.acceptanceId -match '^run-[0-9a-f]{32}$')
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'runner captures failure before cleanup and reports failure'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                $png = Join-Path $root 'shot.png'
                [IO.File]::WriteAllBytes($png, (Get-UiTestPngBytes))
                $script:order = @()
                try {
                    $collect = @{
                        screenshot={ $script:order += 'capture'; $png }
                        uiaTree={ '{"tree":[]}' }
                        lastCommand={ '{"status":"Failed"}' }
                        productLog={ '{"events":[]}' }
                        stateSnapshot={ '{"tables":{},"taxonomy":{}}' }
                        processLedger={ 'owned' }
                    }
                    $result = Invoke-UiAcceptanceRun -ScenarioId 'startup' -Scenario { throw 'product failure' } -Cleanup { $script:order += 'cleanup' } -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata -EvidenceCollectors $collect
                    Assert-Equal 1 $result.ExitCode
                    Assert-Equal 'capture' $script:order[0]
                    Assert-Equal 'cleanup' $script:order[1]
                    $report = Get-Content -LiteralPath $result.ReportPath -Raw | ConvertFrom-Json
                    Assert-Equal 6 @($report.scenarios[0].steps[0].evidencePaths).Count
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'runner cleanup executes after simulated interruption and timeout'
            Body = {
                foreach ($label in @('interrupt','timeout')) {
                    $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                    $root = Join-Path $base 'owned'
                    [void][IO.Directory]::CreateDirectory($root)
                    $script:cleanupCalled = 0
                    try {
                        $result = Invoke-UiAcceptanceRun -ScenarioId $label -Scenario { throw $label } -Cleanup { $script:cleanupCalled++ } -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata
                        Assert-Equal 1 $result.ExitCode
                        Assert-Equal 1 $script:cleanupCalled
                    }
                    finally { Remove-UiTestTree -Path $base }
                }
            }
        },
        @{
            Name = 'runner closes then stops verified process then requests owned root deletion'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                $log = Join-Path $base 'order.txt'
                $exe = Join-Path $base 'TraceFold.Media.exe'
                [IO.File]::WriteAllBytes($exe, [byte[]]@(1))
                $ledger = @([pscustomobject]@{ProcessId=77;StartUtcTicks=123;ExecutablePath=$exe})
                $inventory = { param($id) [pscustomobject]@{ProcessId=77;StartUtcTicks=123;ExecutablePath=$exe} }
                $stop = { param($id) Add-Content -LiteralPath $log -Value 'stop' }
                $close = { Add-Content -LiteralPath $log -Value 'close' }
                try {
                    $env:TRACEFOLD_FAKE_SUPPORT_LOG = $log
                    $support = Join-Path $script:uiRoot 'tests/fixtures/fake-support.ps1'
                    $result = Invoke-UiAcceptanceRun -ScenarioId 'cleanup' -Scenario { [pscustomobject]@{Status='Passed'} } -Cleanup $close -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata -RunToken 'test-token' -OwnedProcessLedger $ledger -ApprovedDirectories @($base) -SupportPath $support -ProcessInventory $inventory -ProcessStop $stop
                    Assert-Equal 0 $result.ExitCode
                    $lines = @(Get-Content -LiteralPath $log)
                    Assert-Equal 'close' $lines[0]
                    Assert-Equal 'stop' $lines[1]
                    Assert-Equal 'delete' $lines[2]
                }
                finally {
                    Remove-Item Env:TRACEFOLD_FAKE_SUPPORT_LOG -ErrorAction SilentlyContinue
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'cleanup failure prevents an overall pass'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                try {
                    $result = Invoke-UiAcceptanceRun -ScenarioId 'cleanup-fails' -Scenario { [pscustomobject]@{Status='Passed'} } -Cleanup { throw 'cleanup error' } -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata
                    Assert-Equal 1 $result.ExitCode
                    Assert-Equal 'Failed' $result.Status
                    $report = Get-Content -LiteralPath $result.ReportPath -Raw | ConvertFrom-Json
                    Assert-Equal 'Failed' $report.cleanup.status
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'entry dispatcher passes only declared host seams'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                $entry = Join-Path $base 'entry.ps1'
                [IO.File]::WriteAllText($entry, "param([Parameter(Mandatory)][string] `$ScenarioId,[string] `$Sink)`n`$ScenarioId", [Text.UTF8Encoding]::new($false))
                try {
                    $declared = @(Get-UiAcceptanceEntryParameter -Path $entry)
                    Assert-Equal 2 $declared.Count
                    Assert-True ($declared -ccontains 'ScenarioId')
                    Assert-True ($declared -ccontains 'Sink')
                    $filtered = New-UiAcceptanceEntryArguments -Path $entry -Candidates @{ ScenarioId='startup'; Sink='sink'; DesktopProbe='probe' }
                    Assert-Equal 2 $filtered.Keys.Count
                    Assert-True ($filtered.ContainsKey('ScenarioId'))
                    Assert-True (-not $filtered.ContainsKey('DesktopProbe'))
                    Assert-Throws { New-UiAcceptanceEntryArguments -Path $entry -Candidates @{ Sink='sink' } }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'scenario registry resolves unique names and refuses unknown ones'
            Body = {
                $names = @(Get-UiAcceptanceScenarioName)
                Assert-True ($names.Count -ge 7)
                Assert-Equal 'StartupLayout' $names[0]
                Assert-Equal '00-startup-layout.ps1' (Get-UiAcceptanceScenarioFile -Name 'StartupLayout')
                # The main-chain entry is the intake chain, numbered after the startup gate; it was
                # previously registered as 01-main-chain.ps1, which the plan never creates.
                Assert-Equal '10-main-chain.ps1' (Get-UiAcceptanceScenarioFile -Name 'MainChain')
                foreach ($name in $names) {
                    $file = Get-UiAcceptanceScenarioFile -Name $name
                    Assert-True ($file -cmatch '^[0-9]{2}-[a-z0-9-]+\.ps1$')
                }
                $resolved = @(Resolve-UiAcceptanceScenarioRequest -Requested @('All'))
                Assert-Equal $names.Count $resolved.Count
                $deduped = @(Resolve-UiAcceptanceScenarioRequest -Requested @('StartupLayout','StartupLayout','MainChain'))
                Assert-Equal 2 $deduped.Count
                Assert-Throws { Resolve-UiAcceptanceScenarioRequest -Requested @('startup-layout') }
                Assert-Throws { Resolve-UiAcceptanceScenarioRequest -Requested @() }
            }
        },
        @{
            Name = 'run root is created by the support tool and verified in place'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN = '1'
                    $support = Join-Path $script:uiRoot 'tests/fixtures/fake-support.ps1'
                    $runId = [guid]::NewGuid()
                    $root = New-UiAcceptanceRunRoot -SupportPath $support -ParentDirectory $base -RunId $runId -RunToken 'test-token'
                    Assert-Equal ([IO.Path]::GetFullPath((Join-Path $base ('TraceFold-UIA-' + $runId.ToString('N'))))) $root
                    Assert-True ([IO.Directory]::Exists($root))
                    Assert-Throws { New-UiAcceptanceRunRoot -SupportPath $support -ParentDirectory $base -RunId ([guid]::Empty) -RunToken 'test-token' }
                    Assert-Throws { New-UiAcceptanceRunRoot -SupportPath $support -ParentDirectory $base -RunId $runId -RunToken '' }
                }
                finally {
                    Remove-Item Env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN -ErrorAction SilentlyContinue
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'startup layout scenario is self-contained and selector-free'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/00-startup-layout.ps1'
                Assert-True ([IO.File]::Exists($path)) 'startup layout scenario is missing'
                $source = [IO.File]::ReadAllText($path)
                foreach ($module in @('Contracts','Driver','Processes','Evidence')) {
                    Assert-Contains $source ('TraceFold.UiAcceptance.' + $module + '.psm1')
                }
                foreach ($seam in @('ScenarioId','ExecutablePath','ContractPath','OwnedRoot','RunToken','OutputDirectory','SupportPath','WinAppPath','LaunchProcess','DesktopCheck','ForegroundCheck')) {
                    $declared = @(Get-UiAcceptanceEntryParameter -Path $path)
                    Assert-True ($declared -ccontains $seam) ('scenario must declare ' + $seam)
                }
                Assert-NotContains $source 'Start-Sleep'
                Assert-NotContains $source 'Stop-Process'
                Assert-NotContains $source "Get-Process -Name"
                Assert-NotContains $source 'explorer'
                Assert-NotContains $source 'FolderPicker'
                Assert-NotContains $source 'SetCursorPos'
                Assert-NotContains $source 'SendInput'
                Assert-NotContains $source '{x:Bind'
                Assert-NotContains $source 'photo.jpg'
                Assert-NotContains $source 'paired.mp4'
                Assert-True ($source -notmatch "'[0-9]+\s*,\s*[0-9]+'") 'scenario must not carry coordinate literals'
                Assert-True ($source -notmatch '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}') 'scenario must not carry generated row identifiers'

                $manifestPath = Join-Path $script:root 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
                $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
                # A selector may only reach the driver through the manifest, so no contract id may
                # appear as a string literal here. Unquoted mentions are fine: the manifest is read
                # through property names, and a property name is not a selector.
                foreach ($entry in @($manifest.entries)) {
                    $literal = "['\`"]" + [regex]::Escape([string]$entry.automationId) + "['\`"]"
                    Assert-True ($source -notmatch $literal) ('scenario must read ' + $entry.automationId + ' from the contract')
                }
            }
        },
        @{
            Name = 'startup layout scenario proves the contract across all three viewports'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory((Join-Path $base 'owned'))
                [IO.File]::WriteAllBytes((Join-Path $base 'TraceFold.Media.exe'), [byte[]]@(1))
                $argvLog = Join-Path $base 'argv.jsonl'
                $state = Join-Path $base 'sequence-state.txt'
                try {
                    $env:TRACEFOLD_FAKE_ARGV_LOG = $argvLog
                    $env:TRACEFOLD_FAKE_SUPPORT_LOG = (Join-Path $base 'support.log')
                    $env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN = '1'
                    $env:TRACEFOLD_FAKE_UIA_TREE_SEQ = (Join-Path $base 'tree-sequence.json')
                    $env:TRACEFOLD_FAKE_UIA_STATE = $state
                    $arguments = New-StartupLayoutScenarioArguments -Base $base -Overrides @{}
                    $result = Invoke-UiAcceptanceScenario -Path (Join-Path $script:uiRoot 'scenarios/00-startup-layout.ps1') -Arguments $arguments
                    Assert-Equal 'Passed' $result.Status $result.Reason
                    Assert-Equal 3 @($result.Viewports | Where-Object { $null -ne $_.PSObject.Properties['widthDip'] }).Count
                    Assert-Equal 1 @($result.Viewports | Where-Object {
                        $null -ne $_.PSObject.Properties['phase'] -and $_.phase -ceq 'rowReady'
                    }).Count
                    Assert-Equal 12 @($result.Artifacts).Count

                    $invocations = @(Get-Content -LiteralPath $argvLog | ForEach-Object { (ConvertFrom-Json $_) })
                    # A comma keeps each argument vector as one element instead of flattening it.
                    $argv = @()
                    foreach ($invocation in $invocations) { $argv += ,@($invocation.argv) }
                    $waits = @($argv | Where-Object { $_[1] -ceq 'wait-for' })
                    Assert-Equal 3 $waits.Count
                    Assert-Equal 'TraceFoldMainWindow' $waits[0][2]
                    Assert-True ($waits[0] -contains '-a')
                    Assert-Equal '4242' $waits[0][4]
                    Assert-Equal 'TraceFoldMainWindow' $waits[1][2]
                    # The row is awaited by its stable name prefix, never by a generated ID.
                    Assert-Equal 'Inbox row paired.mp4' $waits[2][2]

                    $resizes = @($argv | Where-Object { $_[1] -ceq 'resize' })
                    Assert-Equal 0 $resizes.Count
                    $supportActions = @(Get-Content -LiteralPath (Join-Path $base 'support.log'))
                    Assert-Equal 'validate' $supportActions[0]
                    Assert-Equal 4 @($supportActions | Where-Object { $_ -ceq 'resize' }).Count
                    Assert-True (@($supportActions | Where-Object { $_ -ceq 'lookup' }).Count -ge 3)

                    $inspections = @($argv | Where-Object { $_[1] -ceq 'inspect' })
                    # One per phase tree plus the fresh observation the row selector is resolved
                    # from, so a stale accessible name can never be invoked.
                    Assert-Equal 11 $inspections.Count
                    $screenshots = @($argv | Where-Object { $_[1] -ceq 'screenshot' })
                    Assert-Equal 3 $screenshots.Count
                    foreach ($item in $screenshots) { Assert-True ($item -ccontains '--output') }

                    # The product UI is the only preparation channel: folders, save, scan.
                    $values = @($argv | Where-Object { $_[1] -ceq 'set-value' })
                    Assert-Equal 3 $values.Count
                    Assert-Equal 7 @($argv | Where-Object { $_[1] -ceq 'get-value' }).Count
                    $valueTargets = @($values | ForEach-Object { $_[2] })
                    foreach ($target in @('InboxFolderPath','LibraryFolderPath','WorkFolderPath')) {
                        Assert-True ($valueTargets -ccontains $target) ('the scenario must set ' + $target + ' through the product UI')
                    }
                    foreach ($item in $values) {
                        Assert-True ([string] $item[3] -like (([IO.Path]::GetFullPath((Join-Path $base 'owned'))) + '*')) 'folder settings must point inside the owned root'
                    }

                    $invokes = @($argv | Where-Object { $_[1] -ceq 'invoke' })
                    Assert-Equal 7 $invokes.Count
                    $invokeTargets = @($invokes | ForEach-Object { $_[2] })
                    foreach ($target in @('NavSettings','SaveFolderSettings','NavInbox','InboxRefreshButton')) {
                        Assert-True ($invokeTargets -ccontains $target) ('the scenario must drive ' + $target)
                    }
                    # The row is addressed through the semantic selector the driver returned in the
                    # inspection that observed it, resolved immediately before the invocation. Its
                    # accessible name embeds a status that changes, so the name observed earlier is
                    # not a safe selector, and the generated row id must never be used.
                    Assert-Equal 1 @($invokeTargets | Where-Object {
                        [string] $_ -ceq 'itm-pairedmp4-0001'
                    }).Count 'the row must be selected by the selector from its own inspection'
                    Assert-Equal 0 @($invokeTargets | Where-Object {
                        [string] $_ -clike 'Inbox row paired.mp4*'
                    }).Count 'a stale accessible name must never be used as the row selector'
                    Assert-Equal 0 @($invokeTargets | Where-Object {
                        [string] $_ -clike 'InboxCard_*'
                    }).Count 'the generated row container id must never be used as a selector'
                    Assert-True ($invokeTargets -ccontains 'InboxInspectorOpenButton')
                    $keys = @($argv | Where-Object { $_[1] -ceq 'send-keys' })
                    Assert-Equal 1 $keys.Count

                    foreach ($item in $argv) {
                        if ($item[1] -cin @('wait-for','invoke','search','get-value','set-value')) {
                            Assert-True ([string] $item[2] -notmatch '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-') 'the scenario must never select a generated row identifier'
                        }
                        Assert-True (-not (@($item) -join ' ').Contains('photo.jpg')) 'the post-ingest library row must not be required by this scenario'
                    }

                    # Each measured viewport follows the verified fixture scan and selected row.
                    foreach ($name in @('1024x720','1280x800','1600x900')) {
                        $treeText = [IO.File]::ReadAllText((Join-Path $base ('out/viewport-' + $name + '-uia.json')))
                        Assert-True ($treeText -like '*Inbox row paired.mp4*') 'each viewport must show the prepared row'
                    }

                    # Only the owned inbox received the verified fixture.
                    $copied = Join-Path $base 'owned/inbox/paired.mp4'
                    $companion = Join-Path $base 'owned/inbox/paired.m4a'
                    Assert-True ([IO.File]::Exists($copied)) 'the verified fixture must be copied into the owned inbox'
                    Assert-Equal 2405 ([IO.File]::ReadAllBytes($copied).Length)
                    Assert-True ([IO.File]::Exists($companion)) 'the companion must be copied into the owned inbox'
                    Assert-Equal 9988 ([IO.File]::ReadAllBytes($companion).Length)
                    Assert-True (-not [IO.File]::Exists((Join-Path $base 'owned/library/paired.mp4')))
                    Assert-True (-not [IO.File]::Exists((Join-Path $base 'owned/work/paired.mp4')))
                    Assert-Equal 'B5B85F2CA95EA5EB781B42D202D1C3DB361BF37F5BF4B3107A79469FC716A1A3' (Get-FileHash -LiteralPath (Join-Path $script:root 'apps/media-api/tests/fixtures/media/paired.mp4') -Algorithm SHA256).Hash

                    foreach ($viewport in @($result.Viewports | Where-Object {
                        $null -ne $_.PSObject.Properties['widthDip']
                    })) {
                        Assert-True ([IO.File]::Exists((Join-Path $base ('out/' + $viewport.tree))))
                        Assert-True ([IO.File]::Exists((Join-Path $base ('out/' + $viewport.screenshot))))
                    }
                }
                finally {
                    foreach ($name in @('TRACEFOLD_FAKE_ARGV_LOG','TRACEFOLD_FAKE_UIA_TREE_SEQ','TRACEFOLD_FAKE_UIA_STATE','TRACEFOLD_FAKE_SUPPORT_LOG','TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN')) {
                        Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue
                    }
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'startup layout reports the pinned driver inspect schema mismatch'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory((Join-Path $base 'owned'))
                [IO.File]::WriteAllBytes((Join-Path $base 'TraceFold.Media.exe'), [byte[]]@(1))
                $sequence = Join-Path $base 'tree-sequence.json'
                $argvLog = Join-Path $base 'argv.jsonl'
                try {
                    $nativeInspect = '{"depth":8,"windows":[{"elements":[{"type":"Group","automationId":"TraceFoldMainWindow","isEnabled":true}]}]}'
                    [IO.File]::WriteAllText($sequence, (ConvertTo-Json -InputObject @($nativeInspect) -Compress), [Text.UTF8Encoding]::new($false))
                    $env:TRACEFOLD_FAKE_ARGV_LOG = $argvLog
                    $env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN = '1'
                    $env:TRACEFOLD_FAKE_UIA_TREE_SEQ = $sequence
                    $env:TRACEFOLD_FAKE_UIA_STATE = (Join-Path $base 'sequence-state.txt')
                    $arguments = New-StartupLayoutScenarioArguments -Base $base -Overrides @{}
                    $result = Invoke-UiAcceptanceScenario -Path (Join-Path $script:uiRoot 'scenarios/00-startup-layout.ps1') -Arguments $arguments
                    Assert-Equal 'Failed' $result.Status
                    Assert-Equal 'startup_layout.driver_schema_incompatible' $result.Reason
                    $invocations = @(Get-Content -LiteralPath $argvLog | ForEach-Object { ConvertFrom-Json $_ })
                    Assert-Equal 0 @($invocations | Where-Object { $_.argv[1] -ceq 'invoke' }).Count
                }
                finally {
                    foreach ($name in @('TRACEFOLD_FAKE_ARGV_LOG','TRACEFOLD_FAKE_UIA_TREE_SEQ','TRACEFOLD_FAKE_UIA_STATE','TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN')) {
                        Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue
                    }
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'startup layout scenario reports blocked when the interactive desktop is unavailable'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory((Join-Path $base 'owned'))
                [IO.File]::WriteAllBytes((Join-Path $base 'TraceFold.Media.exe'), [byte[]]@(1))
                try {
                    $env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN = '1'
                    $env:TRACEFOLD_FAKE_UIA_TREE_SEQ = (Join-Path $base 'tree-sequence.json')
                    $env:TRACEFOLD_FAKE_UIA_STATE = (Join-Path $base 'sequence-state.txt')
                    $arguments = New-StartupLayoutScenarioArguments -Base $base -Overrides @{ DesktopCheck = { $false } }
                    $result = Invoke-UiAcceptanceScenario -Path (Join-Path $script:uiRoot 'scenarios/00-startup-layout.ps1') -Arguments $arguments
                    Assert-Equal 'Blocked' $result.Status
                    Assert-Contains $result.Reason 'desktop'
                }
                finally {
                    foreach ($name in @('TRACEFOLD_FAKE_UIA_TREE_SEQ','TRACEFOLD_FAKE_UIA_STATE','TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN')) {
                        Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue
                    }
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'startup layout scenario fails when a contract entry has no unique match'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory((Join-Path $base 'owned'))
                [IO.File]::WriteAllBytes((Join-Path $base 'TraceFold.Media.exe'), [byte[]]@(1))
                try {
                    # The contract requires the inspector; this tree never renders it.
                    New-StartupLayoutTreeSequence -Path (Join-Path $base 'tree-sequence.json') -OmitInspectorNode
                    $env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN = '1'
                    $env:TRACEFOLD_FAKE_UIA_TREE_SEQ = (Join-Path $base 'tree-sequence.json')
                    $env:TRACEFOLD_FAKE_UIA_STATE = (Join-Path $base 'sequence-state.txt')
                    $arguments = New-StartupLayoutScenarioArguments -Base $base -Overrides @{}
                    $result = Invoke-UiAcceptanceScenario -Path (Join-Path $script:uiRoot 'scenarios/00-startup-layout.ps1') -Arguments $arguments
                    Assert-Equal 'Failed' $result.Status
                    Assert-Contains $result.Reason 'startup_layout'
                }
                finally {
                    foreach ($name in @('TRACEFOLD_FAKE_UIA_TREE_SEQ','TRACEFOLD_FAKE_UIA_STATE','TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN')) {
                        Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue
                    }
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'startup layout scenario enforces the fixed-name row policy before any preparation'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory((Join-Path $base 'owned'))
                [IO.File]::WriteAllBytes((Join-Path $base 'TraceFold.Media.exe'), [byte[]]@(1))
                $argvLog = Join-Path $base 'argv.jsonl'
                try {
                    New-StartupLayoutContract -Path (Join-Path $base 'contract.json') -RowSelectorPolicy 'automation_id'
                    New-StartupLayoutTreeSequence -Path (Join-Path $base 'tree-sequence.json')
                    $env:TRACEFOLD_FAKE_ARGV_LOG = $argvLog
                    $env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN = '1'
                    $env:TRACEFOLD_FAKE_UIA_TREE_SEQ = (Join-Path $base 'tree-sequence.json')
                    $env:TRACEFOLD_FAKE_UIA_STATE = (Join-Path $base 'sequence-state.txt')
                    $arguments = New-StartupLayoutScenarioArguments -Base $base -Overrides @{}
                    $result = Invoke-UiAcceptanceScenario -Path (Join-Path $script:uiRoot 'scenarios/00-startup-layout.ps1') -Arguments $arguments
                    Assert-Equal 'Failed' $result.Status
                    Assert-Contains $result.Reason 'dynamic_selector'
                    # The row policy is validated from the manifest before the product is touched.
                    Assert-True (-not [IO.File]::Exists($argvLog)) 'no driver call may happen before the row policy is accepted'
                }
                finally {
                    foreach ($name in @('TRACEFOLD_FAKE_ARGV_LOG','TRACEFOLD_FAKE_UIA_TREE_SEQ','TRACEFOLD_FAKE_UIA_STATE','TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN')) {
                        Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue
                    }
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'startup layout scenario blocks instead of failing when the fixture asset does not match'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory((Join-Path $base 'owned'))
                [IO.File]::WriteAllBytes((Join-Path $base 'TraceFold.Media.exe'), [byte[]]@(1))
                $argvLog = Join-Path $base 'argv.jsonl'
                try {
                    # A missing or altered repository fixture is a blocked gate, never a product failure.
                    New-StartupLayoutContract -Path (Join-Path $base 'contract.json') -AssetSha256 ('0' * 64)
                    New-StartupLayoutTreeSequence -Path (Join-Path $base 'tree-sequence.json')
                    $env:TRACEFOLD_FAKE_ARGV_LOG = $argvLog
                    $env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN = '1'
                    $env:TRACEFOLD_FAKE_UIA_TREE_SEQ = (Join-Path $base 'tree-sequence.json')
                    $env:TRACEFOLD_FAKE_UIA_STATE = (Join-Path $base 'sequence-state.txt')
                    $arguments = New-StartupLayoutScenarioArguments -Base $base -Overrides @{}
                    $result = Invoke-UiAcceptanceScenario -Path (Join-Path $script:uiRoot 'scenarios/00-startup-layout.ps1') -Arguments $arguments
                    Assert-Equal 'Blocked' $result.Status
                    Assert-Contains $result.Reason 'fixture'
                    Assert-True (-not [IO.File]::Exists((Join-Path $base 'owned/inbox/paired.mp4'))) 'an unverified fixture must never be copied'
                    Assert-True ([IO.File]::ReadAllText($argvLog) -notlike '*send-keys*') 'the compact drawer phase must not start without a materialised row'
                }
                finally {
                    foreach ($name in @('TRACEFOLD_FAKE_ARGV_LOG','TRACEFOLD_FAKE_UIA_TREE_SEQ','TRACEFOLD_FAKE_UIA_STATE','TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN')) {
                        Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue
                    }
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'entry blocked output contains no absolute owned paths'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                [IO.File]::WriteAllBytes((Join-Path $base 'TraceFold.Media.exe'), [byte[]]@(1))
                try {
                    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
                    $entry = Join-Path $script:root 'scripts/run-stage5cc-ui-acceptance.ps1'
                    $out = Join-Path $base 'out'
                    $null = & $hostExe -NoProfile -File $entry -ProductDirectory $base -WinAppCliArchivePath (Join-Path $base 'missing.zip') -OutputDirectory $out -Scenario StartupLayout
                    Assert-Equal 1 $LASTEXITCODE
                    $json = Get-Content -LiteralPath (Join-Path $out 'summary.json') -Raw
                    $summary = $json | ConvertFrom-Json
                    Assert-Equal 'Blocked' $summary.status
                    Assert-NotContains $json $base
                    Assert-NotContains $json 'ownedRoot'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'request completeness asks only for the entries a named request owes'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                $scenarios = Join-Path $base 'scenarios'
                [void][IO.Directory]::CreateDirectory($scenarios)
                try {
                    # Only one entry exists on disk, so every other name is unimplemented here.
                    [IO.File]::WriteAllText((Join-Path $scenarios (Get-UiAcceptanceScenarioFile -Name 'StartupLayout')), '# marker', [Text.UTF8Encoding]::new($false))
                    $single = Test-UiAcceptanceRequestCompleteness -ScenariosRoot $scenarios -Requested @('StartupLayout')
                    Assert-Equal $false $single.IncludesAll
                    Assert-Equal 'request' $single.Scope
                    Assert-True $single.Complete
                    Assert-Equal 1 $single.CheckedCount
                    Assert-Equal 'StartupLayout' @($single.Checked)[0]

                    $pair = Test-UiAcceptanceRequestCompleteness -ScenariosRoot $scenarios -Requested @('StartupLayout', 'Visual')
                    Assert-True (-not $pair.Complete)
                    Assert-Equal 1 $pair.MissingCount
                    Assert-Equal 'Visual' @($pair.Missing)[0].id
                    Assert-Equal (Get-UiAcceptanceScenarioFile -Name 'Visual') @($pair.Missing)[0].entryFile
                    Assert-True (@($pair.Available) -ccontains 'StartupLayout')

                    # 'All' owes the whole registry, not just what the caller happened to name.
                    $every = Test-UiAcceptanceRequestCompleteness -ScenariosRoot $scenarios -Requested @('All')
                    Assert-True $every.IncludesAll
                    Assert-Equal 'all' $every.Scope
                    Assert-Equal @(Get-UiAcceptanceScenarioName).Count $every.CheckedCount
                    Assert-Equal (@(Get-UiAcceptanceScenarioName).Count - 1) $every.MissingCount
                    Assert-True (-not $every.Complete)

                    $rows = @(Get-UiAcceptanceScenarioAvailability -ScenariosRoot $scenarios -Requested @('StartupLayout', 'Visual'))
                    Assert-Equal 2 $rows.Count
                    Assert-Equal $true $rows[0].Available
                    Assert-Equal $false $rows[1].Available
                    Assert-Equal (Get-UiAcceptanceScenarioFile -Name 'Visual') $rows[1].EntryFile

                    # An unknown name is an argument error here too, never a silently empty answer.
                    Assert-Throws { Test-UiAcceptanceRequestCompleteness -ScenariosRoot $scenarios -Requested @('startup-layout') }
                    Assert-Throws { Test-UiAcceptanceRequestCompleteness -ScenariosRoot $scenarios -Requested @() }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an All request records every unimplemented entry and dispatches nothing'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                try {
                    $repo = New-Stage5CCFakeRepository -Path (Join-Path $base 'repo') -Missing @('Visual')
                    $out = Join-Path $base 'out-all'
                    Assert-Equal 1 (Invoke-Stage5CCEntry -RepositoryRoot $repo -OutputDirectory $out -Scenario 'All')

                    $read = Read-Stage5CCSummary -OutputDirectory $out
                    $summary = $read.Summary
                    Assert-Equal 'Blocked' $summary.status
                    Assert-Equal 'scenario_entry_missing' $summary.reason
                    Assert-Equal $false $summary.complete
                    Assert-Equal @(Get-UiAcceptanceScenarioName).Count @($summary.entries).Count
                    foreach ($entry in @($summary.entries)) {
                        Assert-Equal 'Blocked' $entry.executionStatus
                        Assert-Equal 'scenario_entry_missing' $entry.reason
                        Assert-Equal '' $entry.reportPath
                        Assert-Equal $false $entry.complete
                    }

                    $checks = $summary.checks
                    Assert-Equal 'all' $checks.scope
                    Assert-Equal @(Get-UiAcceptanceScenarioName).Count $checks.checkedCount
                    Assert-Equal 1 $checks.missingCount
                    Assert-Equal 'Visual' @($checks.missing)[0].id
                    Assert-Equal (Get-UiAcceptanceScenarioFile -Name 'Visual') @($checks.missing)[0].entryFile
                    Assert-Equal 0 $checks.dispatchedCount
                    Assert-Equal $true $checks.blockedBeforeDispatch
                    Assert-True (-not [string]::IsNullOrWhiteSpace($checks.checkedUtc))

                    # Nothing was dispatched: no per scenario directory and no support publish.
                    $children = @([IO.Directory]::GetFileSystemEntries($out) | ForEach-Object { [IO.Path]::GetFileName($_) })
                    Assert-Equal 1 $children.Count
                    Assert-Equal 'summary.json' $children[0]
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $repo 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap')))
                    Assert-NotContains $read.Text $base
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a named request is not blocked by an unrelated unimplemented entry'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                try {
                    # Visual stays unimplemented on purpose: verifying the taxonomy slice must not
                    # depend on another slice landing first.
                    $repo = New-Stage5CCFakeRepository -Path (Join-Path $base 'repo') -Missing @('Visual')
                    $out = Join-Path $base 'out-taxonomy'
                    Assert-Equal 1 (Invoke-Stage5CCEntry -RepositoryRoot $repo -OutputDirectory $out -Scenario 'Taxonomy')

                    $summary = (Read-Stage5CCSummary -OutputDirectory $out).Summary
                    Assert-Equal 1 @($summary.entries).Count
                    Assert-Equal 'Taxonomy' @($summary.entries)[0].id
                    # The walk left the precheck and reached the later phases: the answer is about
                    # the missing product, not about an unimplemented entry.
                    Assert-Equal 'published_product_unavailable' @($summary.entries)[0].reason
                    Assert-Equal 'published_product_unavailable' $summary.reason

                    $checks = $summary.checks
                    Assert-Equal 'request' $checks.scope
                    Assert-Equal 1 $checks.checkedCount
                    Assert-Equal 0 $checks.missingCount
                    Assert-Equal 0 @($checks.missing).Count
                    Assert-Equal $false $checks.blockedBeforeDispatch
                    Assert-True (@($checks.checkedIds) -ccontains 'Taxonomy')
                    Assert-Equal $false (@($checks.checkedIds) -ccontains 'Visual')
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a request whose only entry is missing is blocked before dispatch with zero dispatch'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                try {
                    $repo = New-Stage5CCFakeRepository -Path (Join-Path $base 'repo') -Missing @('Visual')
                    $out = Join-Path $base 'out-visual'
                    Assert-Equal 1 (Invoke-Stage5CCEntry -RepositoryRoot $repo -OutputDirectory $out -Scenario 'Visual')

                    $summary = (Read-Stage5CCSummary -OutputDirectory $out).Summary
                    Assert-Equal 'Blocked' $summary.status
                    Assert-Equal 'scenario_entry_missing' $summary.reason
                    Assert-Equal 1 @($summary.entries).Count
                    $entry = @($summary.entries)[0]
                    Assert-Equal 'Visual' $entry.id
                    Assert-Equal 'Blocked' $entry.executionStatus
                    Assert-Equal 'scenario_entry_missing' $entry.reason
                    Assert-Equal '' $entry.reportPath
                    Assert-Equal $false $entry.complete

                    # Nothing survived the precheck, so the precheck is what stopped the batch:
                    # the flag says exactly that, and it must not be reserved for the 'All' case.
                    Assert-Equal 'request' $summary.checks.scope
                    Assert-Equal 1 $summary.checks.missingCount
                    Assert-Equal 'Visual' @($summary.checks.missing)[0].id
                    Assert-Equal 0 $summary.checks.dispatchedCount
                    Assert-Equal $true $summary.checks.blockedBeforeDispatch

                    # Refused before any product, driver or process question was asked: those are
                    # unmet here too, and the answer is still only about the missing entry.
                    $children = @([IO.Directory]::GetFileSystemEntries($out) | ForEach-Object { [IO.Path]::GetFileName($_) })
                    Assert-Equal 1 $children.Count
                    Assert-Equal 'summary.json' $children[0]
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $repo 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap')))
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an All request whose entries all exist clears the precheck and walks on'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                try {
                    $repo = New-Stage5CCFakeRepository -Path (Join-Path $base 'repo')
                    $out = Join-Path $base 'out-all-complete'
                    Assert-Equal 1 (Invoke-Stage5CCEntry -RepositoryRoot $repo -OutputDirectory $out -Scenario 'All')

                    $summary = (Read-Stage5CCSummary -OutputDirectory $out).Summary
                    Assert-Equal @(Get-UiAcceptanceScenarioName).Count @($summary.entries).Count
                    foreach ($entry in @($summary.entries)) {
                        Assert-Equal 'published_product_unavailable' $entry.reason
                        Assert-Equal 'Blocked' $entry.executionStatus
                    }
                    Assert-Equal 'all' $summary.checks.scope
                    Assert-Equal 0 $summary.checks.missingCount
                    Assert-Equal 0 @($summary.checks.missing).Count
                    Assert-Equal $false $summary.checks.blockedBeforeDispatch
                    Assert-Equal 0 $summary.checks.dispatchedCount
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the precheck is recomputed every run and never inherits an earlier clean check'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                try {
                    $repo = New-Stage5CCFakeRepository -Path (Join-Path $base 'repo')
                    $first = Join-Path $base 'out-first'
                    $second = Join-Path $base 'out-second'
                    Assert-Equal 1 (Invoke-Stage5CCEntry -RepositoryRoot $repo -OutputDirectory $first -Scenario 'All')

                    $before = (Read-Stage5CCSummary -OutputDirectory $first).Summary
                    Assert-Equal 0 $before.checks.missingCount
                    Assert-Equal $false $before.checks.blockedBeforeDispatch

                    # The earlier run recorded that every entry existed. Removing one must change
                    # the verdict of the next run instead of being answered from what the previous
                    # run left behind: a gate that remembers is not a gate that checks.
                    [IO.File]::Delete((Join-Path $repo ('scripts/ui-acceptance/scenarios/' + (Get-UiAcceptanceScenarioFile -Name 'Visual'))))
                    Assert-Equal 1 (Invoke-Stage5CCEntry -RepositoryRoot $repo -OutputDirectory $second -Scenario 'All')

                    $after = (Read-Stage5CCSummary -OutputDirectory $second).Summary
                    Assert-Equal 1 $after.checks.missingCount
                    Assert-Equal 'Visual' @($after.checks.missing)[0].id
                    Assert-Equal $true $after.checks.blockedBeforeDispatch
                    Assert-Equal 0 $after.checks.dispatchedCount
                    foreach ($entry in @($after.entries)) { Assert-Equal 'scenario_entry_missing' $entry.reason }

                    $reread = (Read-Stage5CCSummary -OutputDirectory $first).Summary
                    Assert-Equal 0 $reread.checks.missingCount
                    $order = Test-Stage5CCSummaryNotOlder -BeforeValue $before.checks.checkedUtc -AfterValue $after.checks.checkedUtc
                    Assert-True $order.ok ('both checkedUtc values must be preserved explicit-offset instants (before=' + $order.before.reason + ', after=' + $order.after.reason + ')')
                    Assert-True $order.notOlder 'the recomputed precheck must not carry an earlier instant than the first run'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the checkedUtc reader preserves explicit-offset lexemes and refuses every other shape'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                try {
                    # Controlled summary materials are written to disk exactly as summaries are and
                    # read back through Read-Stage5CCSummary; the facts below come from the real
                    # reading and comparison code, never from a second model of it.
                    $writeMaterial = {
                        param([string] $Directory, [string] $Text)
                        [void][IO.Directory]::CreateDirectory($Directory)
                        [IO.File]::WriteAllText((Join-Path $Directory 'summary.json'), $Text, [Text.UTF8Encoding]::new($false))
                    }
                    $readCheckedUtc = {
                        param([string] $Directory)
                        $summary = (Read-Stage5CCSummary -OutputDirectory $Directory).Summary
                        $prop = $summary.checks.PSObject.Properties['checkedUtc']
                        if ($null -eq $prop) { return $null }
                        return $prop.Value
                    }

                    # The real capability branch of this host: PowerShell 7 rewrites ISO strings to
                    # DateTime unless the guard asks for strings; Windows PowerShell preserves them
                    # natively. The two fixed hosts must therefore answer differently here, and the
                    # read below must preserve the lexeme on BOTH.
                    $hasDateKind = (Get-Command -Name ConvertFrom-Json).Parameters.ContainsKey('DateKind')
                    Assert-Equal ($PSVersionTable.PSEdition -eq 'Core') $hasDateKind

                    $rows = @(
                        # day <= 12 and day >= 13: the old defect changed its verdict with the day
                        # of the month, so both halves of the range are pinned.
                        @{ label = 'day-05'; text = '{"checks":{"checkedUtc":"2026-10-05T07:21:05.1234567Z"}}'
                           ok = $true; reason = 'explicit-offset'; ticks = [long] 639267816651234567
                           offset = 'Z'; fraction = 7; lexeme = '2026-10-05T07:21:05.1234567Z' },
                        @{ label = 'day-14'; text = '{"checks":{"checkedUtc":"2026-10-14T07:21:05.1234567Z"}}'
                           ok = $true; reason = 'explicit-offset'; ticks = [long] 639275592651234567
                           offset = 'Z'; fraction = 7; lexeme = '2026-10-14T07:21:05.1234567Z' },
                        # a non-zero offset and the same instant expressed in another zone
                        @{ label = 'plus-08'; text = '{"checks":{"checkedUtc":"2026-10-14T15:21:05.1234567+08:00"}}'
                           ok = $true; reason = 'explicit-offset'; ticks = [long] 639275592651234567
                           offset = '+08:00'; fraction = 7; lexeme = '2026-10-14T15:21:05.1234567+08:00' },
                        @{ label = 'minus-05'; text = '{"checks":{"checkedUtc":"2026-10-14T07:21:05.1234567-05:00"}}'
                           ok = $true; reason = 'explicit-offset'; ticks = [long] 639275772651234567
                           offset = '-05:00'; fraction = 7; lexeme = '2026-10-14T07:21:05.1234567-05:00' },
                        # the fraction is optional; seven digits keep the 100 ns resolution
                        @{ label = 'no-fraction'; text = '{"checks":{"checkedUtc":"2026-10-14T07:21:05Z"}}'
                           ok = $true; reason = 'explicit-offset'; ticks = [long] 639275592650000000
                           offset = 'Z'; fraction = 0; lexeme = '2026-10-14T07:21:05Z' },
                        @{ label = 'one-tick'; text = '{"checks":{"checkedUtc":"2026-10-14T07:21:05.1234568Z"}}'
                           ok = $true; reason = 'explicit-offset'; ticks = [long] 639275592651234568
                           offset = 'Z'; fraction = 7; lexeme = '2026-10-14T07:21:05.1234568Z' },
                        # refusals - none of these may pass as an instant
                        @{ label = 'absent'; text = '{"checks":{}}'; ok = $false; reason = 'absent-or-null' },
                        @{ label = 'null'; text = '{"checks":{"checkedUtc":null}}'; ok = $false; reason = 'absent-or-null' },
                        @{ label = 'blank'; text = '{"checks":{"checkedUtc":"   "}}'; ok = $false; reason = 'absent-or-null' },
                        @{ label = 'number'; text = '{"checks":{"checkedUtc":20261014}}'; ok = $false; reason = 'not-a-string' },
                        @{ label = 'culture-shaped'; text = '{"checks":{"checkedUtc":"10/14/2026 07:21:05"}}'
                           ok = $false; reason = 'not-an-explicit-offset-instant' },
                        @{ label = 'no-offset'; text = '{"checks":{"checkedUtc":"2026-10-14T07:21:05"}}'
                           ok = $false; reason = 'not-an-explicit-offset-instant' },
                        @{ label = 'unparsable'; text = '{"checks":{"checkedUtc":"2026-13-45T07:21:05.1234567Z"}}'
                           ok = $false; reason = 'unparsable' }
                    )

                    for ($i = 0; $i -lt $rows.Count; $i++) {
                        $row = $rows[$i]
                        $dir = Join-Path $base ('case-' + $row.label)
                        & $writeMaterial $dir $row.text
                        $value = & $readCheckedUtc $dir
                        $fact = Get-Stage5CCCheckedInstant -Value $value -Field $row.label
                        if ($row.ok) {
                            Assert-True ($value -is [string]) ($row.label + ': the checkedUtc lexeme must stay a string')
                            Assert-Equal $true $fact.ok ($row.label + ': must read as an instant (' + $fact.reason + ')')
                            Assert-Equal 'explicit-offset' $fact.reason ($row.label + ': reason')
                            Assert-Equal $row.lexeme $fact.text ($row.label + ': the preserved lexeme')
                            Assert-Equal $row.offset $fact.offset ($row.label + ': the explicit offset')
                            Assert-Equal $row.fraction $fact.fractionalDigits ($row.label + ': the fractional precision')
                            Assert-Equal $row.ticks $fact.utcTicks ($row.label + ': the fixed UTC ticks')
                        }
                        else {
                            Assert-Equal $false $fact.ok ($row.label + ': must be refused, not read as an instant')
                            if ($row.reason -ceq 'not-a-string') {
                                Assert-True ($fact.reason -like 'not-a-string:*') ($row.label + ': reason (' + $fact.reason + ')')
                            }
                            else {
                                Assert-Equal $row.reason $fact.reason ($row.label + ': reason')
                            }
                        }
                    }

                    # the two exact facts of the one-tick pair, measured against each other
                    $one = Get-Stage5CCCheckedInstant -Value '2026-10-14T07:21:05.1234567Z' -Field 'pair-before'
                    $two = Get-Stage5CCCheckedInstant -Value '2026-10-14T07:21:05.1234568Z' -Field 'pair-after'
                    Assert-Equal ([long] 1) ($two.utcTicks - $one.utcTicks) 'the pair must differ by exactly one tick'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the checkedUtc comparison holds under a culture switch and the culture is restored'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                try {
                    $writeMaterial = {
                        param([string] $Directory, [string] $Text)
                        [void][IO.Directory]::CreateDirectory($Directory)
                        [IO.File]::WriteAllText((Join-Path $Directory 'summary.json'), $Text, [Text.UTF8Encoding]::new($false))
                    }
                    $readCheckedUtc = {
                        param([string] $Directory)
                        $summary = (Read-Stage5CCSummary -OutputDirectory $Directory).Summary
                        $prop = $summary.checks.PSObject.Properties['checkedUtc']
                        if ($null -eq $prop) { return $null }
                        return $prop.Value
                    }

                    $tickA = Join-Path $base 'tick-a'
                    $tickB = Join-Path $base 'tick-b'
                    $zoneA = Join-Path $base 'zone-a'
                    $zoneB = Join-Path $base 'zone-b'
                    & $writeMaterial $tickA '{"checks":{"checkedUtc":"2026-10-05T07:21:05.1234567Z"}}'
                    & $writeMaterial $tickB '{"checks":{"checkedUtc":"2026-10-05T07:21:05.1234568Z"}}'
                    & $writeMaterial $zoneA '{"checks":{"checkedUtc":"2026-10-14T07:21:05.1234567Z"}}'
                    & $writeMaterial $zoneB '{"checks":{"checkedUtc":"2026-10-14T15:21:05.1234567+08:00"}}'

                    # The comparison must give the same answer under this process's native culture
                    # and under a deliberately contrasting one; each phase saves the culture,
                    # switches, runs the same code, and the finally restores it and reads it back.
                    foreach ($phase in @([System.Threading.Thread]::CurrentThread.CurrentCulture.Name, 'en-US')) {
                        $saved = [System.Threading.Thread]::CurrentThread.CurrentCulture
                        try {
                            [System.Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($phase)
                            Assert-Equal $phase ([System.Threading.Thread]::CurrentThread.CurrentCulture.Name) ('phase ' + $phase + ': the switch must be in effect')

                            $tickValueA = & $readCheckedUtc $tickA
                            $tickValueB = & $readCheckedUtc $tickB
                            $forward = Test-Stage5CCSummaryNotOlder -BeforeValue $tickValueA -AfterValue $tickValueB
                            Assert-True $forward.ok ('phase ' + $phase + ': both instants must read (' + $forward.before.reason + ', ' + $forward.after.reason + ')')
                            Assert-True $forward.notOlder ('phase ' + $phase + ': one tick later is not older')
                            Assert-Equal ([long] 1) ($forward.after.utcTicks - $forward.before.utcTicks) ('phase ' + $phase + ': the exact tick delta')

                            $reverse = Test-Stage5CCSummaryNotOlder -BeforeValue $tickValueB -AfterValue $tickValueA
                            Assert-True $reverse.ok ('phase ' + $phase + ': both instants must read (reverse)')
                            Assert-True (-not $reverse.notOlder) ('phase ' + $phase + ': the reverse order must not pass')

                            $zoneValueA = & $readCheckedUtc $zoneA
                            $zoneValueB = & $readCheckedUtc $zoneB
                            $sameForward = Test-Stage5CCSummaryNotOlder -BeforeValue $zoneValueA -AfterValue $zoneValueB
                            $sameReverse = Test-Stage5CCSummaryNotOlder -BeforeValue $zoneValueB -AfterValue $zoneValueA
                            Assert-True $sameForward.ok ('phase ' + $phase + ': both zone spellings must read')
                            Assert-True $sameForward.notOlder ('phase ' + $phase + ': the same instant is not older')
                            Assert-True $sameReverse.notOlder ('phase ' + $phase + ': the same instant is not older, either way')
                            Assert-Equal $sameForward.before.utcTicks $sameForward.after.utcTicks ('phase ' + $phase + ': the same instant must have identical UTC ticks')
                        }
                        finally {
                            [System.Threading.Thread]::CurrentThread.CurrentCulture = $saved
                            Assert-Equal $saved.Name ([System.Threading.Thread]::CurrentThread.CurrentCulture.Name) ('phase ' + $phase + ': the culture must be restored')
                        }
                    }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the persisted completeness check survives an independent re-read field by field'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                try {
                    $repo = New-Stage5CCFakeRepository -Path (Join-Path $base 'repo') -Missing @('Conflict', 'Visual')
                    $out = Join-Path $base 'out-persist'
                    Assert-Equal 1 (Invoke-Stage5CCEntry -RepositoryRoot $repo -OutputDirectory $out -Scenario 'All')

                    # Read twice from disk: what is asserted is what a reviewer can reread later,
                    # not what the process happens to hold in memory.
                    $first = Read-Stage5CCSummary -OutputDirectory $out
                    $second = Read-Stage5CCSummary -OutputDirectory $out
                    Assert-Equal $first.Text $second.Text

                    foreach ($summary in @($first.Summary, $second.Summary)) {
                        Assert-Equal '5CC' $summary.checkpoint
                        Assert-Equal 'Blocked' $summary.status
                        Assert-Equal 'scenario_entry_missing' $summary.reason
                        Assert-Equal $false $summary.complete
                        Assert-Equal @(Get-UiAcceptanceScenarioName).Count @($summary.entries).Count
                        Assert-Equal 'all' $summary.checks.scope
                        Assert-Equal @(Get-UiAcceptanceScenarioName).Count $summary.checks.checkedCount
                        Assert-Equal 2 $summary.checks.missingCount
                        Assert-Equal 0 $summary.checks.dispatchedCount
                        Assert-Equal $true $summary.checks.blockedBeforeDispatch
                        $ids = @($summary.checks.missing | ForEach-Object { [string]$_.id })
                        Assert-True ($ids -ccontains 'Conflict')
                        Assert-True ($ids -ccontains 'Visual')
                        foreach ($row in @($summary.checks.missing)) {
                            Assert-Equal (Get-UiAcceptanceScenarioFile -Name ([string]$row.id)) $row.entryFile
                            Assert-Equal 'scenario_entry_missing' $row.reason
                        }
                        foreach ($entry in @($summary.entries)) {
                            Assert-Equal 'Blocked' $entry.status
                            Assert-Equal 'Blocked' $entry.reportStatus
                            Assert-Equal 'Blocked' $entry.executionStatus
                            Assert-Equal 'scenario_entry_missing' $entry.reason
                            Assert-Equal '' $entry.reportPath
                            Assert-Equal 0 $entry.durationMs
                            Assert-Equal $false $entry.complete
                        }
                    }
                    Assert-NotContains $first.Text $base
                    Assert-NotContains $second.Text $base
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'preparation success hands the prepared paths to one real scenario invocation'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $repo = New-Stage5CCLiveRepository -Path (Join-Path $base 'repo') -Scenario 'Visual'
                    $product = New-Stage5CCProductStub -Path (Join-Path $base 'product')
                    $archive = New-Stage5CCDriverArchive -Path (Join-Path $base 'driver.zip')
                    $null = Write-Stage5CCDriverManifest -Path (Join-Path $base 'repo/apps/media-desktop/tools/winapp/win-x64/manifest.json') -ArchivePath $archive

                    $journal = Join-Path $base 'scenario-journal.jsonl'
                    $supportJournal = Join-Path $base 'support-journal.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $journal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    $out = Join-Path $base 'out-prepared'

                    Assert-Equal 0 (Invoke-Stage5CCLiveEntry -RepositoryRoot $repo -ProductDirectory $product -WinAppCliArchivePath $archive -OutputDirectory $out -Scenario 'Visual')

                    $summary = (Read-Stage5CCSummary -OutputDirectory $out).Summary
                    Assert-Equal 'Passed' $summary.status
                    Assert-Equal 1 @($summary.entries).Count
                    Assert-Equal 1 $summary.checks.dispatchedCount
                    Assert-Equal $false $summary.checks.blockedBeforeDispatch

                    # The stub scenario wrote down what it was handed, and those values are the
                    # prepared paths: preparation really reached the invocation intact.
                    $records = @(Read-Stage5CCJournal -Path $journal)
                    Assert-Equal 1 $records.Count
                    $record = $records[0]
                    Assert-Equal 'visual' $record.scenarioId
                    Assert-Equal ([IO.Path]::Combine($product, 'TraceFold.Media.exe')) $record.executablePath
                    Assert-Equal (Join-Path $out 'Visual') $record.outputDirectory
                    Assert-Equal ([IO.Path]::Combine($repo, 'apps', 'media-desktop', 'tools', 'ui-acceptance-contract', 'stage5cc-uia-contract.json')) $record.contractPath
                    Assert-Equal $true ([bool] $record.runTokenPresent)
                    Assert-Equal $true ([bool] $record.rootExists)
                    Assert-Equal ([IO.Path]::Combine([string] $record.ownedRoot, 'winapp', 'winapp.exe')) $record.winAppPath
                    Assert-Equal ([IO.Path]::Combine([string] $record.ownedRoot, 'support', 'TraceFold.Media.UiAcceptance.Support.exe')) $record.supportPath
                    Assert-Equal $true ([bool] $record.supportExists)
                    Assert-Equal $true ([bool] $record.driverExists)

                    # One root was owned for this run, and the run handed it back: the support
                    # journalled the create and the delete of exactly the same directory.
                    $supportLines = @([IO.File]::ReadAllLines($supportJournal) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                    Assert-Equal 2 $supportLines.Count
                    Assert-Equal ('create|' + [string] $record.ownedRoot) $supportLines[0]
                    Assert-Equal ('delete|' + [string] $record.ownedRoot) $supportLines[1]
                    Assert-True (-not [IO.Directory]::Exists([string] $record.ownedRoot))

                    $report = Get-Content -LiteralPath ([IO.Path]::Combine($out, 'Visual', 'report.json')) -Raw | ConvertFrom-Json
                    Assert-Equal 'Passed' $report.scenarios[0].status
                    Assert-Equal 'Passed' $report.cleanup.status
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a preparation failure after the owned root exists counts nothing and cleans up'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $repo = New-Stage5CCLiveRepository -Path (Join-Path $base 'repo') -Scenario 'Visual'
                    $product = New-Stage5CCProductStub -Path (Join-Path $base 'product')
                    # The archive unpacks, but without the pinned executable, so the owned root is
                    # already there when preparation refuses to continue.
                    $archive = New-Stage5CCDriverArchive -Path (Join-Path $base 'driver.zip') -WithoutDriverExecutable
                    $null = Write-Stage5CCDriverManifest -Path (Join-Path $base 'repo/apps/media-desktop/tools/winapp/win-x64/manifest.json') -ArchivePath $archive

                    $journal = Join-Path $base 'scenario-journal.jsonl'
                    $supportJournal = Join-Path $base 'support-journal.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $journal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    $out = Join-Path $base 'out-refused'

                    Assert-Equal 1 (Invoke-Stage5CCLiveEntry -RepositoryRoot $repo -ProductDirectory $product -WinAppCliArchivePath $archive -OutputDirectory $out -Scenario 'Visual')

                    $summary = (Read-Stage5CCSummary -OutputDirectory $out).Summary
                    Assert-Equal 'Blocked' $summary.status
                    $entry = @($summary.entries)[0]
                    Assert-Equal 'Blocked' $entry.executionStatus
                    Assert-Equal 'pinned_driver_executable_missing' $entry.reason
                    Assert-Equal '' $entry.reportPath
                    Assert-Equal $false $entry.complete

                    # The precheck let this request through and preparation stopped it afterwards,
                    # so the flag stays false while the count stays at zero: they are two answers.
                    Assert-Equal 0 $summary.checks.missingCount
                    Assert-Equal 0 $summary.checks.dispatchedCount
                    Assert-Equal $false $summary.checks.blockedBeforeDispatch

                    # The scenario was never reached, not even once.
                    Assert-Equal 0 @(Read-Stage5CCJournal -Path $journal).Count

                    # The walk still knew where its scaffold had been put, and gave it back rather
                    # than leaving an owned root behind for someone else to find.
                    $supportLines = @([IO.File]::ReadAllLines($supportJournal) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                    Assert-Equal 2 $supportLines.Count
                    Assert-Equal ([string] $supportLines[0]).Replace('create|', 'delete|') $supportLines[1]
                    $root = ([string] $supportLines[0]).Substring('create|'.Length)
                    Assert-True (-not [IO.Directory]::Exists($root))
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a failing scenario counts one dispatch and keeps the cleanup and failure gates'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $repo = New-Stage5CCLiveRepository -Path (Join-Path $base 'repo') -Scenario 'Visual' -ScenarioOutcome 'Failed'
                    $product = New-Stage5CCProductStub -Path (Join-Path $base 'product')
                    $archive = New-Stage5CCDriverArchive -Path (Join-Path $base 'driver.zip')
                    $null = Write-Stage5CCDriverManifest -Path (Join-Path $base 'repo/apps/media-desktop/tools/winapp/win-x64/manifest.json') -ArchivePath $archive

                    $journal = Join-Path $base 'scenario-journal.jsonl'
                    $supportJournal = Join-Path $base 'support-journal.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $journal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    $out = Join-Path $base 'out-failed'

                    Assert-Equal 1 (Invoke-Stage5CCLiveEntry -RepositoryRoot $repo -ProductDirectory $product -WinAppCliArchivePath $archive -OutputDirectory $out -Scenario 'Visual')

                    $summary = (Read-Stage5CCSummary -OutputDirectory $out).Summary
                    Assert-Equal 'Failed' $summary.status
                    Assert-Equal 'Failed' @($summary.entries)[0].status
                    Assert-Equal 'Failed' @($summary.entries)[0].executionStatus

                    # It really ran, so it really counts: the count is not a promise of success.
                    Assert-Equal 1 $summary.checks.dispatchedCount
                    Assert-Equal $false $summary.checks.blockedBeforeDispatch
                    Assert-Equal 1 @(Read-Stage5CCJournal -Path $journal).Count

                    # The gates that survive a failed scenario: a report that says so, cleanup that
                    # passed, and no owned root left behind.
                    $report = Get-Content -LiteralPath ([IO.Path]::Combine($out, 'Visual', 'report.json')) -Raw | ConvertFrom-Json
                    Assert-Equal 'Failed' $report.scenarios[0].status
                    Assert-Equal 'Passed' $report.cleanup.status
                    Assert-Contains $report.scenarios[0].steps[0].reason 'scenario_refused_on_purpose'

                    $supportLines = @([IO.File]::ReadAllLines($supportJournal) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                    Assert-Equal 2 $supportLines.Count
                    Assert-Equal ([string] $supportLines[0]).Replace('create|', 'delete|') $supportLines[1]
                    Assert-True (-not [IO.Directory]::Exists((([string] $supportLines[0]).Substring('create|'.Length))))
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'the dispatch ledger refuses to count the same scenario twice'
            Body = {
                $ledger = New-Object 'System.Collections.Generic.List[string]'
                try {
                    Register-UiAcceptanceDispatch -Name 'Visual' -Ledger $ledger
                    Assert-Equal 1 $ledger.Count
                    Assert-Equal 'Visual' $ledger[0]
                    Assert-Throws { Register-UiAcceptanceDispatch -Name 'Visual' -Ledger $ledger }
                    Assert-Equal 1 $ledger.Count
                    Assert-Throws { Register-UiAcceptanceDispatch -Name 'Visual' -Ledger $null }
                }
                finally { $ledger.Clear() }
            }
        }
    )
}
