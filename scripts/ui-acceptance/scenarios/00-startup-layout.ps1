# TraceFold UI acceptance - startup/layout scenario.
#
# Proves the pinned driver against the published window at the three accepted client sizes,
# then - still only through the product's own UI - materialises the inbox row and accepts the
# compact drawer interaction.
#
# Phase rules this file must respect:
#   * Startup phase: only the static elements the startup scopes declare are required. A brand
#     new owned root has no media rows, so no dynamic row may be required there.
#   * Row policy: the manifest's fixed-filename/accessible-name selection policy is validated
#     from the manifest itself, before the product is touched at all.
#   * Preparation: the owned inbox/library/work directories are set through the product's
#     Settings UI, the repository fixture is verified by size and SHA-256 and copied only into
#     the owned inbox, and the scan is triggered through the product's own control.
#   * The library row belongs to the post-ingest phase and is never required here.
#
# Bans: no coordinates, no sleeps, no shell folder-picker automation, no process termination
# by name, no direct database access, no generated row identifiers, and no pre-seeded fake rows.
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
    [int] $RowWaitTimeoutSeconds = 30,
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

# This file lives in scripts/ui-acceptance/scenarios, so the repository root is three levels up.
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))

# A precondition this host cannot satisfy never counts as a product failure.
$blockedFailureCodes = @(
    'startup_layout.desktop_unavailable',
    'startup_layout.fixture_missing',
    'startup_layout.fixture_mismatch',
    'startup_layout.fixture_target'
)

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
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['tree']) {
        throw 'startup_layout.driver_schema_incompatible'
    }
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

<#
.SYNOPSIS
    Validates the manifest's dynamic-row selection policy without touching the product.

.DESCRIPTION
    Runs before the product is launched: a row whose declared selection policy is unusable must
    stop the gate before any mutation, and the policy is a property of the manifest, not of the
    phase - so it is checked for every declared row, present or not.
#>
function Assert-StartupLayoutDynamicPolicy {
    param(
        [Parameter(Mandatory)][object[]] $Rows,
        [Parameter(Mandatory)][string[]] $Phases
    )

    foreach ($row in $Rows) {
        Assert-UiAcceptanceFields -InputObject $row -Required @('file','controlType','selector','fixedAccessibleName','fixedFilename','forbidGuidSelectors','phase')
        if ([string] $row.selector -cne 'accessible_name') { throw 'startup_layout.dynamic_selector' }
        if (-not [bool] $row.forbidGuidSelectors) { throw 'startup_layout.dynamic_selector' }
        if ([string] $row.fixedFilename -notmatch '\S') { throw 'startup_layout.dynamic_selector' }
        if ([string] $row.fixedAccessibleName -notlike ('*' + [string] $row.fixedFilename + '*')) {
            throw 'startup_layout.dynamic_selector'
        }
        if ([string] $row.fixedAccessibleName -match '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-') { throw 'startup_layout.dynamic_selector' }
        if ([string] $row.fixedAccessibleName -match '[\{\}]') { throw 'startup_layout.dynamic_selector' }
        if ($Phases -cnotcontains [string] $row.phase) { throw 'startup_layout.dynamic_phase' }
    }
}

<#
.SYNOPSIS
    Requires exactly one unique match for every row the current phase must have produced.
#>
function Assert-StartupLayoutPhaseRow {
    param(
        [Parameter(Mandatory)][object[]] $Nodes,
        [Parameter(Mandatory)][object[]] $Rows
    )

    foreach ($row in $Rows) {
        $matches = @($Nodes | Where-Object {
            [string] $_.controlType -ceq [string] $row.controlType -and
            [string] $_.name -ceq [string] $row.fixedAccessibleName })
        if ($matches.Count -ne 1) { throw 'startup_layout.dynamic_row' }
        foreach ($match in $matches) {
            # The runtime id must never be a binding expression, because the gate addresses the
            # row by its accessible name and an unresolved binding would mean the row is not real.
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

function Invoke-StartupLayoutMutation {
    param(
        [Parameter(Mandatory)][string] $StepId,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $FailureCode
    )

    $result = Invoke-WinAppMutation -StepId $StepId -Arguments $Arguments -ProcessId $ProcessId `
        -WinAppPath $WinAppPath -TimeoutMs $MutationTimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($result.Status -cne 'Passed') { throw $FailureCode }
}

<#
.SYNOPSIS
    Verifies one repository fixture and copies it into the owned seed directory.

.DESCRIPTION
    The gate only ever seeds from a repository fixture whose size and SHA-256 match the manifest,
    and it only writes into a directory it created under the owned root. A missing or altered
    fixture is a blocked gate, not a product failure.
#>
function Copy-StartupLayoutFixture {
    param(
        [Parameter(Mandatory)] $Asset,
        [Parameter(Mandatory)][hashtable] $Folders
    )

    $source = [IO.Path]::GetFullPath((Join-Path $repositoryRoot ([string] $Asset.repositoryPath)))
    $repositoryBoundary = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $source.StartsWith($repositoryBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'startup_layout.fixture_target' }
    if ([IO.Path]::GetFileName([string]$Asset.fixedFilename) -cne [string]$Asset.fixedFilename) { throw 'startup_layout.fixture_target' }
    if (-not [IO.File]::Exists($source)) { throw 'startup_layout.fixture_missing' }
    $item = Get-Item -LiteralPath $source
    if ($item.Length -ne [long] $Asset.sizeBytes) { throw 'startup_layout.fixture_mismatch' }
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ($hash -cne ([string] $Asset.sha256).ToUpperInvariant()) { throw 'startup_layout.fixture_mismatch' }

    $seedDirectory = [string] $Asset.seedDirectory
    if (-not $Folders.ContainsKey($seedDirectory)) { throw 'startup_layout.fixture_target' }
    $destination = Join-Path $Folders[$seedDirectory] ([string] $Asset.fixedFilename)
    [IO.File]::Copy($source, $destination, $false)
    return [pscustomobject] @{ Source = $source; Destination = $destination; Sha256 = $hash }
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
    Assert-UiAcceptanceFields -InputObject $contract -Required @('entries','dynamicRows','fixtureAssets','sessionPreparation','startupScopes','layoutProbe','layoutViewports')

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

    # --- row policy and fixture declaration, validated before the product is touched ---
    $phases = Get-UiAcceptanceDynamicRowPhase
    $rows = @($contract.dynamicRows)
    if ($rows.Count -eq 0) { throw 'startup_layout.contract_empty' }
    Assert-StartupLayoutDynamicPolicy -Rows $rows -Phases $phases

    $preparation = $contract.sessionPreparation
    Assert-UiAcceptanceFields -InputObject $preparation -Required @(
        'materializedRowPhase','seedFilenames','settingsNavigationAutomationId','inboxNavigationAutomationId',
        'saveFolderSettingsAutomationId','scanTriggerAutomationId','folderInputs')
    $materializedPhase = [string] $preparation.materializedRowPhase
    if ($phases -cnotcontains $materializedPhase) { throw 'startup_layout.dynamic_phase' }
    $materializedRows = @($rows | Where-Object { [string] $_.phase -ceq $materializedPhase })
    if ($materializedRows.Count -eq 0) { throw 'startup_layout.dynamic_phase' }

    $folderInputs = @($preparation.folderInputs)
    if ($folderInputs.Count -eq 0) { throw 'startup_layout.preparation_contract' }
    foreach ($folderInput in $folderInputs) {
        Assert-UiAcceptanceFields -InputObject $folderInput -Required @('key','automationId','relativeDirectory')
    }

    $assets = @($contract.fixtureAssets)
    $seedNames = @($preparation.seedFilenames)
    if ($seedNames.Count -eq 0 -or @($seedNames | Select-Object -Unique).Count -ne $seedNames.Count) { throw 'startup_layout.fixture_target' }
    foreach ($row in $materializedRows) {
        if ($seedNames -cnotcontains [string]$row.fixedFilename) { throw 'startup_layout.fixture_missing' }
    }
    foreach ($seedName in $seedNames) {
        $assetMatches = @($assets | Where-Object { [string] $_.fixedFilename -ceq [string] $seedName })
        if ($assetMatches.Count -ne 1) { throw 'startup_layout.fixture_missing' }
        Assert-UiAcceptanceFields -InputObject $assetMatches[0] -Required @('fixedFilename','repositoryPath','sha256','sizeBytes','seedDirectory')
        if ([string]$assetMatches[0].seedDirectory -cne 'inbox') { throw 'startup_layout.fixture_target' }
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

    # --- startup phase: static entries only; the owned root has no media rows yet ---
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

    # --- preparation: owned folders through the product UI, then the scan ---
    $folders = @{}
    foreach ($folderInput in $folderInputs) {
        $relative = [string]$folderInput.relativeDirectory
        if ($relative -cnotmatch '^[a-z0-9][a-z0-9-]*$') { throw 'startup_layout.fixture_target' }
        $directory = [IO.Path]::GetFullPath((Join-Path $OwnedRoot $relative))
        $rootBoundary = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $directory.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'startup_layout.fixture_target' }
        [void][IO.Directory]::CreateDirectory($directory)
        $folders[[string] $folderInput.key] = $directory
    }

    Invoke-StartupLayoutMutation -StepId 'startup.open-settings' `
        -Arguments @('ui','invoke',[string] $preparation.settingsNavigationAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.settings_unavailable'
    foreach ($folderInput in $folderInputs) {
        Invoke-StartupLayoutMutation -StepId ('startup.set-' + [string] $folderInput.key + '-folder') `
            -Arguments @('ui','set-value',[string] $folderInput.automationId,$folders[[string] $folderInput.key]) `
            -ProcessId $productProcessId -FailureCode 'startup_layout.folder_settings'
    }
    Invoke-StartupLayoutMutation -StepId 'startup.save-folder-settings' `
        -Arguments @('ui','invoke',[string] $preparation.saveFolderSettingsAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.folder_settings'
    Invoke-StartupLayoutMutation -StepId 'startup.open-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.inbox_navigation'

    # Only a fixture whose size and hash match the manifest is copied, and only into the owned
    # inbox: the library stays empty until a real ingest happens.
    foreach ($seedName in $seedNames) {
        $asset = @($assets | Where-Object { [string] $_.fixedFilename -ceq [string] $seedName })[0]
        $null = Copy-StartupLayoutFixture -Asset $asset -Folders $folders
    }

    Invoke-StartupLayoutMutation -StepId 'startup.scan-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.scan_trigger'

    foreach ($row in $materializedRows) {
        $appeared = Invoke-WinAppObservation -Arguments @('ui','wait-for',[string] $row.fixedAccessibleName) `
            -ProcessId $productProcessId -TimeoutSeconds $RowWaitTimeoutSeconds `
            -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($appeared.Status -cne 'Passed') { throw 'startup_layout.dynamic_row' }
        if ($null -eq $appeared.Data -or $appeared.Data.found -ne $true) { throw 'startup_layout.dynamic_row' }
    }

    $seededTree = Read-StartupLayoutTree -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
    Assert-StartupLayoutPhaseRow -Nodes @($seededTree.tree) -Rows $materializedRows

    # --- compact drawer policy, only once a real row exists ---
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

        $row = $materializedRows[0]
        Invoke-StartupLayoutMutation -StepId ('startup.select-row.' + $compactWidth) `
            -Arguments @('ui','invoke',[string] $row.fixedAccessibleName) `
            -ProcessId $productProcessId -FailureCode 'startup_layout.row_selection'

        $readyTree = Read-StartupLayoutTree -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
        $readyIndex = Get-StartupLayoutIndex -Nodes @($readyTree.tree)
        $openButtonId = [string] $probe.inspectorOpenButtonAutomationId
        $openButton = Get-StartupLayoutBounds -Index $readyIndex -AutomationId $openButtonId -FailureCode 'startup_layout.inspector_open_button'
        if (-not [bool] $openButton.enabled) { throw 'startup_layout.inspector_open_button' }

        Invoke-StartupLayoutMutation -StepId ('startup.open-inspector.' + $compactWidth) `
            -Arguments @('ui','invoke',$openButtonId) `
            -ProcessId $productProcessId -FailureCode 'startup_layout.inspector_open'

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
    $status = if ($blockedFailureCodes -ccontains $code) { 'Blocked' } else { 'Failed' }
    return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = $status; Reason = $code; Viewports = @(); Artifacts = @() }
}
