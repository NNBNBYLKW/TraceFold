# TraceFold UI acceptance - startup/layout scenario.
#
# Proves the pinned driver against the published window at the three accepted client sizes.
# Every selector, expected control type, expected pattern and expected layout width is read
# from the shared UIA contract at runtime: this file must not carry product literals, must not
# carry coordinates, must not sleep, and must never end a process by name.
#
# Host seams (declared so a dispatcher can hand them over, and so the contract tests can drive
# the whole scenario against the fake driver):
#   -LaunchProcess    replaces the real product launch, returning a process identity record
#   -DesktopCheck     replaces the interactive-desktop probe
#   -ForegroundCheck  replaces the foreground-ownership probe
#
# The runner appends '-a <pid>' and '--json' to every driver call, and '--timeout' to
# 'wait-for', so no argument list below repeats those.

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ScenarioId,
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $ContractPath,
    [Parameter(Mandatory)][string] $OwnedRoot,
    [Parameter(Mandatory)][string] $RunToken,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][string] $SupportPath,
    [Parameter(Mandatory)][string] $WinAppPath,
    [int] $WaitTimeoutSeconds = 30,
    [int] $ObservationTimeoutSeconds = 20,
    [int] $InspectDepth = 8,
    [int] $MutationTimeoutMs = 5000,
    [scriptblock] $LaunchProcess,
    [scriptblock] $DesktopCheck = { Test-UiInteractiveDesktop },
    [scriptblock] $ForegroundCheck = { param($id) Test-UiForegroundProcess -ProcessId $id }
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Contracts.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Processes.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Driver.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Evidence.psm1') -DisableNameChecking

function Get-StartupLayoutIndex {
    param([object[]] $Nodes)

    $index = @{}
    foreach ($node in @($Nodes)) {
        $id = [string] $node.automationId
        if ([string]::IsNullOrWhiteSpace($id)) { continue }
        if ($index.ContainsKey($id)) { $index[$id] = @($index[$id]) + @($node) }
        else { $index[$id] = @($node) }
    }
    return ,$index
}

function Read-StartupLayoutTree {
    param(
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $FailureCode
    )

    $observation = Invoke-WinAppObservation -Arguments @('ui','inspect','--depth',[string] $InspectDepth) `
        -ProcessId $ProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed') { throw $FailureCode }
    Assert-UiAcceptanceFields -InputObject $observation.Data -Required @('tree')
    return $observation.Data
}

function Resize-StartupLayoutWindow {
    param(
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][int] $WidthDip,
        [Parameter(Mandatory)][int] $HeightDip
    )

    $raw = & $SupportPath window resize --process-id $ProcessId --width-dip $WidthDip `
        --height-dip $HeightDip --root $OwnedRoot --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'startup_layout.resize_refused' }
    $fact = ($raw | Out-String | ConvertFrom-Json)
    Assert-UiAcceptanceFields -InputObject $fact -Required @('widthPixels','heightPixels','clientWidthPixels','clientHeightPixels')
    return $fact
}

function Assert-StartupLayoutClientSize {
    param(
        [Parameter(Mandatory)] $Fact,
        [Parameter(Mandatory)][int] $WidthDip,
        [Parameter(Mandatory)][int] $HeightDip,
        [Parameter(Mandatory)][int] $Tolerance
    )

    $widthPixels = [int] $Fact.widthPixels
    $heightPixels = [int] $Fact.heightPixels
    if ($widthPixels -le 0 -or $heightPixels -le 0) { throw 'startup_layout.client_viewport' }
    # The requested DIP size and the reported physical size share one rasterization scale.
    if ([Math]::Abs(($widthPixels / $WidthDip) - ($heightPixels / $HeightDip)) -gt 0.02) {
        throw 'startup_layout.client_viewport'
    }
    $expectedWidth = [int][Math]::Round($WidthDip * ($widthPixels / [double] $WidthDip), [MidpointRounding]::AwayFromZero)
    $expectedHeight = [int][Math]::Round($HeightDip * ($heightPixels / [double] $HeightDip), [MidpointRounding]::AwayFromZero)
    if ([Math]::Abs([int] $Fact.clientWidthPixels - $expectedWidth) -gt $Tolerance -or
        [Math]::Abs([int] $Fact.clientHeightPixels - $expectedHeight) -gt $Tolerance) {
        throw 'startup_layout.client_viewport'
    }
    return $widthPixels / [double] $WidthDip
}

function Assert-StartupLayoutEntry {
    param(
        [Parameter(Mandatory)][hashtable] $Index,
        [Parameter(Mandatory)][object[]] $Required,
        [string[]] $OffscreenAllowed = @()
    )

    foreach ($entry in $Required) {
        $id = [string] $entry.automationId
        if (-not $Index.ContainsKey($id)) { throw 'startup_layout.entry_missing' }
        $matches = @($Index[$id])
        if ($matches.Count -ne 1) { throw 'startup_layout.entry_ambiguous' }
        $node = $matches[0]
        if ([string] $node.controlType -cne [string] $entry.controlType) { throw 'startup_layout.control_type' }
        if ([string] $node.name -cne [string] $entry.name) { throw 'startup_layout.accessible_name' }
        if ($node.enabled -isnot [bool] -or $node.offscreen -isnot [bool]) { throw 'startup_layout.state_missing' }
        $patterns = @($node.patterns)
        foreach ($pattern in @($entry.requiredPatterns)) {
            if ($patterns -cnotcontains [string] $pattern) { throw 'startup_layout.pattern_missing' }
        }
        # The inline inspector is legitimately collapsed at the narrow size; its visibility is
        # owned by the layout policy below, not by this reachability rule.
        if ([bool] $node.offscreen -and $OffscreenAllowed -cnotcontains $id) {
            throw 'startup_layout.entry_offscreen'
        }
    }
}

function Assert-StartupLayoutDynamicRow {
    param(
        [Parameter(Mandatory)][object[]] $Nodes,
        [Parameter(Mandatory)][object[]] $Rows
    )

    foreach ($row in $Rows) {
        Assert-UiAcceptanceFields -InputObject $row -Required @('controlType','selector','fixedAccessibleName','forbidGuidSelectors')
        # Rows are addressed by their deterministic accessible name and never by a generated id.
        if ([string] $row.selector -cne 'accessible_name') { throw 'startup_layout.dynamic_selector' }
        if ([string] $row.fixedAccessibleName -match '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-') {
            throw 'startup_layout.dynamic_selector'
        }
        $matches = @($Nodes | Where-Object {
            [string] $_.controlType -ceq [string] $row.controlType -and
            [string] $_.name -ceq [string] $row.fixedAccessibleName })
        if ($matches.Count -ne 1) { throw 'startup_layout.dynamic_row' }
        foreach ($match in $matches) {
            if ([string] $match.automationId -match '^\{') { throw 'startup_layout.dynamic_selector' }
        }
    }
}

function Get-StartupLayoutBounds {
    param(
        [Parameter(Mandatory)][hashtable] $Index,
        [Parameter(Mandatory)][string] $AutomationId,
        [Parameter(Mandatory)][string] $FailureCode
    )

    if (-not $Index.ContainsKey($AutomationId)) { throw $FailureCode }
    $matches = @($Index[$AutomationId])
    if ($matches.Count -ne 1) { throw $FailureCode }
    Assert-UiAcceptanceFields -InputObject $matches[0] -Required @('bounds')
    Assert-UiAcceptanceFields -InputObject $matches[0].bounds -Required @('x','y','width','height')
    return $matches[0]
}

$failureCode = 'startup_layout.unexpected'
$viewports = @()
$artifacts = @()

try {
    foreach ($requiredInput in @($ExecutablePath, $ContractPath, $OwnedRoot, $OutputDirectory, $SupportPath, $WinAppPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredInput)) { throw 'startup_layout.invalid_input' }
    }
    if ([IO.Path]::GetFileName($ExecutablePath) -ine 'TraceFold.Media.exe') { throw 'startup_layout.invalid_product' }
    if (-not [IO.File]::Exists($ContractPath)) { throw 'startup_layout.contract_missing' }
    if (-not [IO.File]::Exists($SupportPath) -or -not [IO.File]::Exists($WinAppPath)) { throw 'startup_layout.tooling_missing' }

    $contract = ConvertFrom-UiAcceptanceJson -Text ([IO.File]::ReadAllText($ContractPath)) -Source 'uia contract'
    Assert-UiAcceptanceFields -InputObject $contract -Required @('entries','dynamicRows','startupScopes','layoutProbe','layoutViewports')

    $startupScopes = @($contract.startupScopes)
    $required = @()
    foreach ($entry in @($contract.entries)) {
        Assert-UiAcceptanceFields -InputObject $entry -Required @('automationId','name','controlType','requiredPatterns','scope')
        if ($startupScopes -ccontains [string] $entry.scope) { $required += $entry }
    }
    if ($required.Count -eq 0) { throw 'startup_layout.contract_empty' }

    # The window handle is identified by exactly one contract entry; never by a literal here.
    $windowEntries = @($required | Where-Object { [string] $_.scope -ceq 'window' })
    if ($windowEntries.Count -ne 1) { throw 'startup_layout.window_selector' }
    $windowId = [string] $windowEntries[0].automationId

    $probe = $contract.layoutProbe
    Assert-UiAcceptanceFields -InputObject $probe -Required @('navigationItemAutomationId','inlineInspectorAutomationId','inspectorOpenButtonAutomationId','pixelTolerance')
    $tolerance = [int] $probe.pixelTolerance

    $declared = @($contract.layoutViewports)
    $accepted = Get-UiAcceptanceRequiredViewport
    if ($declared.Count -ne $accepted.Count) { throw 'startup_layout.viewport_contract' }
    for ($position = 0; $position -lt $accepted.Count; $position++) {
        if ([int] $declared[$position].widthDip -ne [int] $accepted[$position].WidthDip -or
            [int] $declared[$position].heightDip -ne [int] $accepted[$position].HeightDip) {
            throw 'startup_layout.viewport_contract'
        }
    }

    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    $screenshotDirectory = Join-Path $OwnedRoot 'screenshots'
    [void][IO.Directory]::CreateDirectory($screenshotDirectory)

    Reset-UiAcceptanceMutationSteps
    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId

    $waited = Invoke-WinAppObservation -Arguments @('ui','wait-for',$windowId) `
        -ProcessId $productProcessId -TimeoutSeconds $WaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($waited.Status -cne 'Passed') { throw 'startup_layout.window_unavailable' }
    if ($null -eq $waited.Data -or $waited.Data.found -ne $true) { throw 'startup_layout.window_unavailable' }

    foreach ($position in 0..($declared.Count - 1)) {
        $spec = $declared[$position]
        $widthDip = [int] $spec.widthDip
        $heightDip = [int] $spec.heightDip
        $inlineInspector = [bool] $spec.inlineInspector

        $resized = Resize-StartupLayoutWindow -ProcessId $productProcessId -WidthDip $widthDip -HeightDip $heightDip
        $scale = Assert-StartupLayoutClientSize -Fact $resized -WidthDip $widthDip -HeightDip $heightDip -Tolerance $tolerance

        $tree = Read-StartupLayoutTree -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
        $nodes = @($tree.tree)
        $index = Get-StartupLayoutIndex -Nodes $nodes
        $indent = 'startup_layout.' + $widthDip

        if ($index.Count -eq 0) { $failureCode = $indent + '_empty_tree'; throw $failureCode }
        try { Assert-StartupLayoutEntry -Index $index -Required $required -OffscreenAllowed @([string] $probe.inlineInspectorAutomationId) }
        catch { $failureCode = $_.Exception.Message; throw $failureCode }
        try { Assert-StartupLayoutDynamicRow -Nodes $nodes -Rows @($contract.dynamicRows) }
        catch { $failureCode = $_.Exception.Message; throw $failureCode }

        $navigationId = [string] $probe.navigationItemAutomationId
        $navigation = Get-StartupLayoutBounds -Index $index -AutomationId $navigationId -FailureCode 'startup_layout.navigation_missing'
        $expectedNavigationWidth = [int][Math]::Round([int] $spec.navigationItemWidthDip * $scale, [MidpointRounding]::AwayFromZero)
        if ([Math]::Abs([int] $navigation.bounds.width - $expectedNavigationWidth) -gt $tolerance) {
            throw 'startup_layout.navigation_width'
        }

        $inspectorId = [string] $probe.inlineInspectorAutomationId
        $inspector = Get-StartupLayoutBounds -Index $index -AutomationId $inspectorId -FailureCode 'startup_layout.inspector_missing'
        if ($inlineInspector) {
            if ([bool] $inspector.offscreen -or [int] $inspector.bounds.width -le 0) {
                throw 'startup_layout.inline_inspector'
            }
        }
        elseif (-not [bool] $inspector.offscreen -and [int] $inspector.bounds.width -gt 0) {
            throw 'startup_layout.inline_inspector'
        }

        $name = 'viewport-' + $widthDip + 'x' + $heightDip
        $treeRelative = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory `
            -EvidenceId $name -Tree $tree -RunToken $RunToken -OwnedRoot $OwnedRoot

        $screenshotSource = Join-Path $screenshotDirectory ($name + '.png')
        $captured = Invoke-WinAppObservation -Arguments @('ui','screenshot',$windowId,'--output',$screenshotSource) `
            -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
            -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($captured.Status -cne 'Passed' -or -not [IO.File]::Exists($screenshotSource)) {
            throw 'startup_layout.screenshot_unavailable'
        }
        Assert-UiOwnedEvidenceFile -Path $screenshotSource -OwnedRoot $OwnedRoot
        $bytes = [IO.File]::ReadAllBytes($screenshotSource)
        Assert-UiPngBytes -Bytes $bytes
        $screenshotRelative = $name + '-screenshot.png'
        [IO.File]::WriteAllBytes((Join-Path $OutputDirectory $screenshotRelative), $bytes)

        $artifacts += $treeRelative
        $artifacts += $screenshotRelative
        $viewports += [pscustomobject] @{
            widthDip = $widthDip
            heightDip = $heightDip
            clientWidthPixels = [int] $resized.clientWidthPixels
            clientHeightPixels = [int] $resized.clientHeightPixels
            navigationWidthDip = [int] $spec.navigationItemWidthDip
            inlineInspector = $inlineInspector
            tree = $treeRelative
            screenshot = $screenshotRelative
        }
    }

    # The compact policy needs a live interactive desktop: it selects a row, opens the drawer
    # and closes it with Escape. Without one the run is Blocked, never silently Passed.
    $compactSpecs = @($declared | Where-Object { -not [bool] $_.inlineInspector })
    if ($compactSpecs.Count -gt 0) {
        if (-not (& $DesktopCheck)) {
            $failureCode = 'startup_layout.desktop_unavailable'
            throw $failureCode
        }
        $compact = $compactSpecs[0]
        $compactWidth = [int] $compact.widthDip
        $compactHeight = [int] $compact.heightDip
        $resized = Resize-StartupLayoutWindow -ProcessId $productProcessId -WidthDip $compactWidth -HeightDip $compactHeight
        $clientWidth = [int] $resized.clientWidthPixels

        $row = @($contract.dynamicRows)[0]
        $selected = Invoke-WinAppMutation -StepId ('startup.select-row.' + $compactWidth) `
            -Arguments @('ui','invoke',[string] $row.fixedAccessibleName) -ProcessId $productProcessId `
            -WinAppPath $WinAppPath -TimeoutMs $MutationTimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($selected.Status -cne 'Passed') { throw 'startup_layout.row_selection' }

        $readyTree = Read-StartupLayoutTree -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
        $readyIndex = Get-StartupLayoutIndex -Nodes @($readyTree.tree)
        $openButtonId = [string] $probe.inspectorOpenButtonAutomationId
        $openButton = Get-StartupLayoutBounds -Index $readyIndex -AutomationId $openButtonId -FailureCode 'startup_layout.inspector_open_button'
        if (-not [bool] $openButton.enabled) { throw 'startup_layout.inspector_open_button' }

        $opened = Invoke-WinAppMutation -StepId ('startup.open-inspector.' + $compactWidth) `
            -Arguments @('ui','invoke',$openButtonId) -ProcessId $productProcessId `
            -WinAppPath $WinAppPath -TimeoutMs $MutationTimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($opened.Status -cne 'Passed') { throw 'startup_layout.inspector_open' }

        $drawerTree = Read-StartupLayoutTree -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
        $drawerIndex = Get-StartupLayoutIndex -Nodes @($drawerTree.tree)
        $drawer = Get-StartupLayoutBounds -Index $drawerIndex -AutomationId ([string] $probe.inlineInspectorAutomationId) -FailureCode 'startup_layout.drawer_missing'
        if ([bool] $drawer.offscreen -or [int] $drawer.bounds.width -le 0) { throw 'startup_layout.drawer_missing' }
        # A compact drawer is anchored to the right half of the client area.
        if ([int] $drawer.bounds.x -lt ([int][Math]::Floor($clientWidth / 2))) { throw 'startup_layout.drawer_position' }

        $closed = Invoke-WinAppKeyboard -StepId ('startup.close-drawer.' + $compactWidth) `
            -Arguments @('ui','send-keys','Escape') -ProcessId $productProcessId `
            -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck -DesktopCheck $DesktopCheck `
            -TimeoutMs $MutationTimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($closed.Status -cne 'Passed') { throw 'startup_layout.drawer_close' }

        $closedTree = Read-StartupLayoutTree -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
        $closedIndex = Get-StartupLayoutIndex -Nodes @($closedTree.tree)
        $afterEscape = Get-StartupLayoutBounds -Index $closedIndex -AutomationId ([string] $probe.inlineInspectorAutomationId) -FailureCode 'startup_layout.drawer_missing'
        if (-not [bool] $afterEscape.offscreen) { throw 'startup_layout.drawer_close' }
    }

    return [pscustomobject] @{
        ScenarioId = $ScenarioId
        Status     = 'Passed'
        Reason     = ''
        Viewports  = $viewports
        Artifacts  = $artifacts
    }
}
catch {
    # The failure detail stays out of the durable record: only the stable code is reported.
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or $code -notlike 'startup_layout.*') {
        $code = 'startup_layout.unexpected'
    }
    if ($code -ceq 'startup_layout.desktop_unavailable') {
        return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = 'Blocked'; Reason = $code; Viewports = @(); Artifacts = @() }
    }
    return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = 'Failed'; Reason = $code; Viewports = @(); Artifacts = @() }
}
