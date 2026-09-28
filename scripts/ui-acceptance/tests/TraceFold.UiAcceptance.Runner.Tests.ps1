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
            inboxReady      = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot','InboxInspectorOpenButton')
            rowReady        = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot','InboxInspectorOpenButton')
            rowSelected     = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot','InboxInspectorOpenButton')
            drawerOpen      = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot','InboxInspectorOpenButton','InspectorPane')
            drawerClosed    = @('TraceFoldMainWindow','TraceFoldShell','NavInbox','InboxPageRoot','InboxInspectorOpenButton')
        }
        layoutProbe    = [ordered]@{
            navigationItemAutomationId      = 'NavInbox'
            navigationPaneAutomationId      = 'PaneRoot'
            navigationPaneClassName         = 'SplitViewPane'
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
        [Parameter(Mandatory)][string] $Id,
        [Parameter(Mandatory)][string] $ControlType,
        [Parameter(Mandatory)][string] $Name,
        [bool] $Enabled = $true,
        [bool] $Offscreen = $false,
        [int] $X = 0,
        [int] $Y = 0,
        [int] $Width = 0,
        [int] $Height = 0,
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
        selector     = 'sel-' + $Id
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
        $row = @(New-StartupLayoutNode -Id '3f2504e0-4f89-11d3-9a0c-0305e82c3301' -ControlType 'ListItem' -Name 'Inbox row paired.mp4, state pending' -X 100 -Y 100 -Width 200 -Height 60)
    }

    # The window scope wraps the shell scope, which wraps the page scopes: the fake response has
    # real nesting so the scenario's recursive adapter is genuinely exercised.
    if ($SettingsPage) {
        $settingsChildren = @(
            New-StartupLayoutNode -Id 'InboxFolderPath' -ControlType 'Edit' -Name 'Inbox folder path' -X 825 -Y 424 -Width 633 -Height 32
            New-StartupLayoutNode -Id 'LibraryFolderPath' -ControlType 'Edit' -Name 'Library folder path' -X 825 -Y 496 -Width 633 -Height 32
            New-StartupLayoutNode -Id 'WorkFolderPath' -ControlType 'Edit' -Name 'Work folder path' -X 825 -Y 568 -Width 633 -Height 32
        )
        $shellChildren = @(
            New-StartupLayoutNode -Id 'NavInbox' -ControlType 'ListItem' -Name 'Inbox navigation' -Width $NavigationWidth -Height 40
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
        New-StartupLayoutNode -Id 'NavInbox' -ControlType 'ListItem' -Name 'Inbox navigation' -Width $NavigationWidth -Height 40
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
                    Assert-Equal 3 @($result.Viewports).Count
                    Assert-Equal 6 @($result.Artifacts).Count

                    $invocations = @(Get-Content -LiteralPath $argvLog | ForEach-Object { (ConvertFrom-Json $_) })
                    # A comma keeps each argument vector as one element instead of flattening it.
                    $argv = @()
                    foreach ($invocation in $invocations) { $argv += ,@($invocation.argv) }
                    Assert-Equal 22 $argv.Count

                    $waits = @($argv | Where-Object { $_[1] -ceq 'wait-for' })
                    Assert-Equal 2 $waits.Count
                    Assert-Equal 'TraceFoldMainWindow' $waits[0][2]
                    Assert-True ($waits[0] -contains '-a')
                    Assert-Equal '4242' $waits[0][4]
                    # The row is awaited by its deterministic accessible name, never by an id.
                    Assert-Equal 'Inbox row paired.mp4' $waits[1][2]

                    $resizes = @($argv | Where-Object { $_[1] -ceq 'resize' })
                    Assert-Equal 0 $resizes.Count
                    $supportActions = @(Get-Content -LiteralPath (Join-Path $base 'support.log'))
                    Assert-Equal 5 $supportActions.Count
                    Assert-Equal 'validate' $supportActions[0]
                    Assert-Equal 4 @($supportActions | Where-Object { $_ -ceq 'resize' }).Count

                    $inspections = @($argv | Where-Object { $_[1] -ceq 'inspect' })
                    Assert-Equal 7 $inspections.Count
                    $screenshots = @($argv | Where-Object { $_[1] -ceq 'screenshot' })
                    Assert-Equal 3 $screenshots.Count
                    foreach ($item in $screenshots) { Assert-True ($item -ccontains '--output') }

                    # The product UI is the only preparation channel: folders, save, scan.
                    $values = @($argv | Where-Object { $_[1] -ceq 'set-value' })
                    Assert-Equal 3 $values.Count
                    $valueTargets = @($values | ForEach-Object { $_[2] })
                    foreach ($target in @('InboxFolderPath','LibraryFolderPath','WorkFolderPath')) {
                        Assert-True ($valueTargets -ccontains $target) ('the scenario must set ' + $target + ' through the product UI')
                    }
                    foreach ($item in $values) {
                        Assert-True ([string] $item[3] -like (([IO.Path]::GetFullPath((Join-Path $base 'owned'))) + '*')) 'folder settings must point inside the owned root'
                    }

                    $invokes = @($argv | Where-Object { $_[1] -ceq 'invoke' })
                    Assert-Equal 6 $invokes.Count
                    $invokeTargets = @($invokes | ForEach-Object { $_[2] })
                    foreach ($target in @('NavSettings','SaveFolderSettings','NavInbox','InboxRefreshButton')) {
                        Assert-True ($invokeTargets -ccontains $target) ('the scenario must drive ' + $target)
                    }
                    Assert-True ($invokeTargets -ccontains 'Inbox row paired.mp4') 'the row must be selected by its accessible name'
                    Assert-True ($invokeTargets -ccontains 'InboxInspectorOpenButton')
                    $keys = @($argv | Where-Object { $_[1] -ceq 'send-keys' })
                    Assert-Equal 1 $keys.Count

                    foreach ($item in $argv) {
                        foreach ($argument in $item) {
                            Assert-True ([string] $argument -notmatch '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-') 'the scenario must never select a generated row identifier'
                            Assert-True ([string] $argument -notlike '*photo.jpg*') 'the post-ingest library row must not be required by this scenario'
                        }
                    }

                    # The three startup trees were inspected before any preparation: no media rows.
                    foreach ($name in @('1024x720','1280x800','1600x900')) {
                        $treeText = [IO.File]::ReadAllText((Join-Path $base ('out/viewport-' + $name + '-uia.json')))
                        Assert-True ($treeText -notlike '*Inbox row paired.mp4*') 'a brand-new owned root must not already contain the inbox row'
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

                    foreach ($viewport in @($result.Viewports)) {
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
        }
    )
}
