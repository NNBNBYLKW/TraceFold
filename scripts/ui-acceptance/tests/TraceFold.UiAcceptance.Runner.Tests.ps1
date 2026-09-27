# Shared builders for the startup/layout scenario contract.
#
# ASCII only, like every other toolkit file: the scenario contract under test never depends on
# the Chinese product names, which are locked separately by UiAutomationContractTests.

function New-StartupLayoutContract {
    param(
        [Parameter(Mandatory)][string] $Path
    )

    $entries = @(
        [ordered]@{ file='MainWindow.xaml'; automationId='TraceFoldMainWindow'; name='TraceFold main window'; controlType='Window'; requiredPatterns=@(); scope='window'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Shell/ShellPage.xaml'; automationId='NavInbox'; name='Inbox navigation'; controlType='NavigationViewItem'; requiredPatterns=@('SelectionItem'); scope='shell'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Pages/InboxPage.xaml'; automationId='InboxInspectorOpenButton'; name='Open inbox file panel'; controlType='Button'; requiredPatterns=@('Invoke'); scope='pages_inboxpage'; dynamicSelectorPolicy='static_id' }
        [ordered]@{ file='Pages/InboxPage.xaml'; automationId='InspectorPane'; name='Inbox file inspector'; controlType='Border'; requiredPatterns=@(); scope='page_host:inbox'; dynamicSelectorPolicy='static_id' }
    )

    $contract = [ordered]@{
        schemaVersion  = 1
        entries        = $entries
        dynamicRows    = @([ordered]@{ file='Pages/InboxPage.xaml'; controlType='Grid'; selector='accessible_name'; fixedAccessibleName='Inbox row paired.mp4'; fixedFilename='paired.mp4'; forbidGuidSelectors=$true })
        startupScopes  = @('window', 'shell', 'pages_inboxpage', 'page_host:inbox')
        layoutProbe    = [ordered]@{
            navigationItemAutomationId      = 'NavInbox'
            inlineInspectorAutomationId     = 'InspectorPane'
            inspectorOpenButtonAutomationId = 'InboxInspectorOpenButton'
            pixelTolerance                  = 2
        }
        layoutViewports = @(
            [ordered]@{ widthDip=1024; heightDip=720; navigationItemWidthDip=48; inlineInspector=$false }
            [ordered]@{ widthDip=1280; heightDip=800; navigationItemWidthDip=176; inlineInspector=$true }
            [ordered]@{ widthDip=1600; heightDip=900; navigationItemWidthDip=176; inlineInspector=$true }
        )
    }

    [IO.File]::WriteAllText($Path, ($contract | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}

function New-StartupLayoutNode {
    param(
        [Parameter(Mandatory)][string] $Id,
        [Parameter(Mandatory)][string] $ControlType,
        [Parameter(Mandatory)][string] $Name,
        [string[]] $Patterns = @(),
        [bool] $Enabled = $true,
        [bool] $Offscreen = $false,
        [int] $X = 0,
        [int] $Y = 0,
        [int] $Width = 0,
        [int] $Height = 0
    )

    return [ordered]@{
        automationId = $Id
        controlType  = $ControlType
        name         = $Name
        patterns     = $Patterns
        enabled      = $Enabled
        offscreen    = $Offscreen
        bounds       = [ordered]@{ x = $X; y = $Y; width = $Width; height = $Height }
    }
}

function New-StartupLayoutTree {
    param(
        [Parameter(Mandatory)][int] $NavigationWidth,
        [Parameter(Mandatory)][bool] $InlineInspector,
        [bool] $InspectorOpenButtonEnabled = $true,
        [int] $InspectorX = 900,
        [switch] $OmitInspectorNode
    )

    $nodes = @(
        New-StartupLayoutNode -Id 'TraceFoldMainWindow' -ControlType 'Window' -Name 'TraceFold main window' -Width 1024 -Height 720
        New-StartupLayoutNode -Id 'NavInbox' -ControlType 'NavigationViewItem' -Name 'Inbox navigation' -Patterns @('SelectionItem') -Width $NavigationWidth -Height 40
        New-StartupLayoutNode -Id 'InboxInspectorOpenButton' -ControlType 'Button' -Name 'Open inbox file panel' -Patterns @('Invoke') -Enabled $InspectorOpenButtonEnabled -X 700 -Y 60 -Width 32 -Height 32
        New-StartupLayoutNode -Id '3f2504e0-4f89-11d3-9a0c-0305e82c3301' -ControlType 'Grid' -Name 'Inbox row paired.mp4' -X 100 -Y 100 -Width 200 -Height 60
    )

    if ($OmitInspectorNode) { return $nodes }

    if ($InlineInspector) {
        $nodes += New-StartupLayoutNode -Id 'InspectorPane' -ControlType 'Border' -Name 'Inbox file inspector' -X $InspectorX -Y 0 -Width 320 -Height 720
    }
    else {
        $nodes += New-StartupLayoutNode -Id 'InspectorPane' -ControlType 'Border' -Name 'Inbox file inspector' -Offscreen $true -Width 0 -Height 0
    }

    return $nodes
}

function New-StartupLayoutTreeSequence {
    param(
        [Parameter(Mandatory)][string] $Path,
        [switch] $OmitInspectorNode
    )

    $compactClosed = New-StartupLayoutTree -NavigationWidth 48 -InlineInspector $false -InspectorOpenButtonEnabled $false -OmitInspectorNode:$OmitInspectorNode
    $compactReady = New-StartupLayoutTree -NavigationWidth 48 -InlineInspector $false -InspectorOpenButtonEnabled $true -OmitInspectorNode:$OmitInspectorNode
    $compactDrawer = New-StartupLayoutTree -NavigationWidth 48 -InlineInspector $true -InspectorOpenButtonEnabled $true -InspectorX 720 -OmitInspectorNode:$OmitInspectorNode
    $compactClosedAgain = New-StartupLayoutTree -NavigationWidth 48 -InlineInspector $false -InspectorOpenButtonEnabled $true -OmitInspectorNode:$OmitInspectorNode
    $wide1280 = New-StartupLayoutTree -NavigationWidth 176 -InlineInspector $true -OmitInspectorNode:$OmitInspectorNode
    $wide1600 = New-StartupLayoutTree -NavigationWidth 176 -InlineInspector $true -OmitInspectorNode:$OmitInspectorNode

    $payloads = @(
        ([ordered]@{ tree = $compactClosed } | ConvertTo-Json -Depth 12 -Compress)
        ([ordered]@{ tree = $wide1280 } | ConvertTo-Json -Depth 12 -Compress)
        ([ordered]@{ tree = $wide1600 } | ConvertTo-Json -Depth 12 -Compress)
        ([ordered]@{ tree = $compactReady } | ConvertTo-Json -Depth 12 -Compress)
        ([ordered]@{ tree = $compactDrawer } | ConvertTo-Json -Depth 12 -Compress)
        ([ordered]@{ tree = $compactClosedAgain } | ConvertTo-Json -Depth 12 -Compress)
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
                    Assert-Equal ([IO.Path]::GetFullPath((Join-Path $base ('TraceFold-UIA-' + $runId.ToString('D'))))) $root
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
                Assert-True ($source -notmatch "'[0-9]+\s*,\s*[0-9]+'") 'scenario must not carry coordinate literals'
                Assert-True ($source -notmatch '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}') 'scenario must not carry generated row identifiers'

                # ReadAllText decodes UTF-8 without relying on the host's default code page, so
                # the manifest's Chinese names survive on both runtimes.
                $manifestPath = Join-Path $script:root 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
                $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
                foreach ($entry in @($manifest.entries)) {
                    Assert-True ($source -notmatch [regex]::Escape([string]$entry.automationId)) ('scenario must read ' + $entry.automationId + ' from the contract')
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
                    Assert-Equal 13 $argv.Count
                    $wait = @($argv | Where-Object { $_[1] -ceq 'wait-for' })
                    Assert-Equal 1 $wait.Count
                    Assert-Equal 'TraceFoldMainWindow' $wait[0][2]
                    Assert-True ($wait[0] -contains '-a')
                    Assert-Equal '4242' $wait[0][4]

                    $resizes = @($argv | Where-Object { $_[1] -ceq 'resize' })
                    Assert-Equal 0 $resizes.Count
                    $supportActions = @(Get-Content -LiteralPath (Join-Path $base 'support.log'))
                    Assert-Equal 5 $supportActions.Count
                    Assert-Equal 'validate' $supportActions[0]
                    Assert-Equal 4 @($supportActions | Where-Object { $_ -ceq 'resize' }).Count

                    $inspections = @($argv | Where-Object { $_[1] -ceq 'inspect' })
                    Assert-Equal 6 $inspections.Count
                    $screenshots = @($argv | Where-Object { $_[1] -ceq 'screenshot' })
                    Assert-Equal 3 $screenshots.Count
                    foreach ($item in $screenshots) { Assert-True ($item -ccontains '--output') }

                    $invokes = @($argv | Where-Object { $_[1] -ceq 'invoke' })
                    Assert-Equal 2 $invokes.Count
                    Assert-Equal 'Inbox row paired.mp4' $invokes[0][2]
                    Assert-Equal 'InboxInspectorOpenButton' $invokes[1][2]
                    $keys = @($argv | Where-Object { $_[1] -ceq 'send-keys' })
                    Assert-Equal 1 $keys.Count

                    foreach ($item in $argv) {
                        foreach ($argument in $item) {
                            Assert-True ([string]$argument -notmatch '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-') 'the scenario must never select a generated row identifier'
                        }
                    }
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
        }
    )
}
