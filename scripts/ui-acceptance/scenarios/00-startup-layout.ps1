# TraceFold UI acceptance - startup/layout and inbox preparation scenario.
#
# Phases, in the order the product actually reaches them, each with its own required ID set from
# the shared manifest:
#   startupSettings - a brand-new owned root opens on the folder settings page
#   inboxReady      - the Inbox page after the three owned directories have been saved
#   rowReady        - the scanned inbox row exists
#   rowSelected     - the row is selected and the inspector-open button became enabled
#   drawerOpen      - the compact drawer is open
#   drawerClosed    - Escape closed it again
#
# Rules this file must respect:
#   * Only the current phase's required IDs are demanded. A brand-new root has no media rows, so
#     no row and no Inbox page element may be required during startupSettings.
#   * The XAML element type stays the static contract; runtime verification compares the
#     separately declared, measured native UIA type. Patterns are never inferred from the
#     inspection response - the pinned driver does not report them.
#   * Pattern availability and the composite descendant rule come from the read-only native probe.
#     A pattern that is false fails the gate for design review; a query that cannot answer blocks.
#   * Directory preparation goes through the product's own Settings UI and every path is read
#     back. The fixture is verified by size and SHA-256 and copied only into the owned inbox.
#   * The library row belongs to the post-ingest phase and is never required here.
#
# Bans: no coordinates, no sleeps, no shell folder-picker automation, no process termination by
# name, no direct database access, no generated row identifiers, and no pre-seeded fake rows.
#
# Host seams:
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
    [int] $RowWaitTimeoutSeconds = 45,
    [int] $ObservationTimeoutSeconds = 20,
    [int] $InspectDepth = 20,
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
Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Workflows.psm1') -DisableNameChecking

# This file lives in scripts/ui-acceptance/scenarios, so the repository root is three levels up.
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))

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
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['windows']) {
        throw 'startup_layout.driver_schema_incompatible'
    }
    return $observation.Data
}

<#
.SYNOPSIS
    Flattens one driver inspection response into the node list every assertion works on.

.DESCRIPTION
    The pinned driver answers 'ui inspect --json' with windows[].elements[] whose nodes carry
    nested children and the fields type / isEnabled / isOffscreen / x / y / width / height. Only
    those are required; automationId, name, className and children are optional. Nothing is
    invented, and a response that is not this shape fails with one stable code.
#>
function ConvertFrom-StartupLayoutDriverTree {
    param([Parameter(Mandatory)] $Data)

    if ($null -eq $Data.PSObject.Properties['windows']) { throw 'startup_layout.driver_schema_incompatible' }
    $nodes = @()
    foreach ($window in @($Data.windows)) {
        if ($null -eq $window.PSObject.Properties['elements']) { throw 'startup_layout.driver_schema_incompatible' }
        foreach ($element in @($window.elements)) {
            $nodes += (Expand-StartupLayoutDriverNode -Node $element -ParentAutomationId '')
        }
    }
    return ,$nodes
}

function Expand-StartupLayoutDriverNode {
    param(
        [Parameter(Mandatory)] $Node,
        [AllowEmptyString()][string] $ParentAutomationId = ''
    )

    foreach ($field in @('type','isEnabled','isOffscreen','x','y','width','height')) {
        if ($null -eq $Node.PSObject.Properties[$field]) { throw 'startup_layout.driver_schema_incompatible' }
    }

    $id = ''
    if ($null -ne $Node.PSObject.Properties['automationId']) { $id = [string] $Node.automationId }
    $name = ''
    if ($null -ne $Node.PSObject.Properties['name']) { $name = [string] $Node.name }

    $flattened = @([pscustomobject] @{
        automationId = $id
        parentAutomationId = $ParentAutomationId
        controlType  = [string] $Node.type
        name         = $name
        enabled      = [bool] $Node.isEnabled
        offscreen    = [bool] $Node.isOffscreen
        bounds       = [pscustomobject] @{
            x = [int] $Node.x; y = [int] $Node.y
            width = [int] $Node.width; height = [int] $Node.height
        }
    })

    if ($null -ne $Node.PSObject.Properties['children']) {
        foreach ($child in @($Node.children)) {
            $flattened += (Expand-StartupLayoutDriverNode -Node $child -ParentAutomationId $id)
        }
    }
    return ,$flattened
}

function Read-StartupLayoutNodes {
    param(
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $FailureCode
    )

    $tree = Read-StartupLayoutTree -ProcessId $ProcessId -FailureCode $FailureCode
    $nodes = ConvertFrom-StartupLayoutDriverTree -Data $tree
    return [pscustomobject] @{ Nodes = $nodes; Index = (Get-StartupLayoutIndex -Nodes $nodes) }
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
    if ([Math]::Abs(($widthPixels / $WidthDip) - ($heightPixels / $HeightDip)) -gt 0.02) {
        throw 'startup_layout.client_viewport'
    }
    if ([Math]::Abs([int] $Fact.clientWidthPixels - $widthPixels) -gt $Tolerance -or
        [Math]::Abs([int] $Fact.clientHeightPixels - $heightPixels) -gt $Tolerance) {
        throw 'startup_layout.client_viewport'
    }
    return $widthPixels / [double] $WidthDip
}

<#
.SYNOPSIS
    Requires one unique match for every entry the current phase declares.

.DESCRIPTION
    Name and the measured native control type must match exactly, and enabled/offscreen must be
    real booleans. Whether a phase may have an element disabled or offscreen is asserted
    separately, so this rule stays about identity rather than transient state.
#>
function Assert-StartupLayoutPhase {
    param(
        [Parameter(Mandatory)][hashtable] $Index,
        [Parameter(Mandatory)][string[]] $PhaseIds,
        [Parameter(Mandatory)][hashtable] $Entries,
        [Parameter(Mandatory)][string] $Phase
    )

    $seen = @{}
    foreach ($id in @($PhaseIds)) {
        if ($seen.ContainsKey($id)) { throw 'startup_layout.phase_duplicate' }
        $seen[$id] = $true
        if (-not $Entries.ContainsKey($id)) { throw 'startup_layout.phase_unknown_entry' }
        $entry = $Entries[$id]
        if ($null -eq $entry.PSObject.Properties['nativeControlType']) { throw 'startup_layout.native_type_missing' }
        if (-not $Index.ContainsKey($id)) { throw 'startup_layout.entry_missing' }
        $matches = @($Index[$id])
        if ($matches.Count -ne 1) { throw 'startup_layout.entry_ambiguous' }
        $node = $matches[0]
        if ([string] $node.controlType -cne [string] $entry.nativeControlType) { throw 'startup_layout.native_type' }
        if ([string] $node.name -cne [string] $entry.name) { throw 'startup_layout.accessible_name' }
        if ($node.enabled -isnot [bool] -or $node.offscreen -isnot [bool]) { throw 'startup_layout.state_missing' }
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

<#
.SYNOPSIS
    Verifies the state an element must have gained or lost in this phase.
#>
function Assert-StartupLayoutState {
    param(
        [Parameter(Mandatory)][hashtable] $Index,
        [Parameter(Mandatory)][string] $AutomationId,
        [Parameter(Mandatory)][string] $FailureCode,
        [switch] $Offscreen,
        [switch] $Onscreen,
        [switch] $Enabled,
        [switch] $Disabled
    )

    $node = Get-StartupLayoutBounds -Index $Index -AutomationId $AutomationId -FailureCode $FailureCode
    if ($Offscreen -and -not [bool] $node.offscreen) { throw $FailureCode }
    if ($Onscreen -and ([bool] $node.offscreen -or [int] $node.bounds.width -le 0)) { throw $FailureCode }
    if ($Enabled -and -not [bool] $node.enabled) { throw $FailureCode }
    if ($Disabled -and [bool] $node.enabled) { throw $FailureCode }
    return $node
}

function Assert-StartupLayoutHiddenOrAbsent {
    param(
        [Parameter(Mandatory)][hashtable] $Index,
        [Parameter(Mandatory)][string] $AutomationId,
        [Parameter(Mandatory)][string] $FailureCode
    )

    # WinUI may remove the compact inspector from the UIA tree instead of retaining an
    # offscreen element. Either result means it is unavailable to the user.
    if (-not $Index.ContainsKey($AutomationId)) { return }
    $node = Get-StartupLayoutBounds -Index $Index -AutomationId $AutomationId -FailureCode $FailureCode
    if (-not [bool] $node.offscreen -and [int] $node.bounds.width -gt 0) {
        throw $FailureCode
    }
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

function Get-StartupLayoutDriverValue {
    param(
        [Parameter(Mandatory)][string] $AutomationId,
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $FailureCode
    )

    $observation = Invoke-WinAppObservation -Arguments @('ui','get-value',$AutomationId) `
        -ProcessId $ProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed') { throw $FailureCode }
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['text']) {
        throw 'startup_layout.driver_schema_incompatible'
    }
    return [string] $observation.Data.text
}

<#
.SYNOPSIS
    Read-only native UIA lookup through the owned support tool.

.DESCRIPTION
    Answers what the pinned driver cannot: the real native control type, the descendant shape of a
    composite control, and whether a required UIA pattern is genuinely available. A null answer is
    returned as null and must never be read as supported or unsupported.
#>
function Invoke-StartupLayoutNativeLookup {
    param(
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $AutomationId,
        [string[]] $Patterns = @(),
        [string] $DescendantClass,
        [string] $DescendantName,
        [Parameter(Mandatory)][string] $FailureCode
    )

    $arguments = @('native','lookup','--root',$OwnedRoot,'--token',$RunToken,'--process-id',[string] $ProcessId,
        '--automation-id',$AutomationId)
    if (-not [string]::IsNullOrEmpty($DescendantClass)) { $arguments += @('--descendant-class',$DescendantClass) }
    if (-not [string]::IsNullOrEmpty($DescendantName)) { $arguments += @('--descendant-name',$DescendantName) }
    foreach ($pattern in @($Patterns)) { $arguments += @('--pattern',[string] $pattern) }

    $raw = & $SupportPath @arguments
    if ($LASTEXITCODE -ne 0) { throw $FailureCode }
    $fact = ($raw | Out-String | ConvertFrom-Json)
    Assert-UiAcceptanceFields -InputObject $fact -Required @('automationId','outerMatchCount','descendantMatchCount','patterns')
    return $fact
}

<#
.SYNOPSIS
    Verifies the required patterns, and the composite descendant rule, for every phase entry that
    declares one.

.DESCRIPTION
    false is a product gap and fails the gate for design review; null means the query could not
    establish an answer and blocks the gate. Neither is ever a pass.
#>
function Assert-StartupLayoutNativeFacts {
    param(
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string[]] $PhaseIds,
        [Parameter(Mandatory)][hashtable] $Entries
    )

    foreach ($id in @($PhaseIds)) {
        $entry = $Entries[$id]
        $descendantClass = ''
        $descendantName = ''
        if ($null -ne $entry.PSObject.Properties['nativeDescendant']) {
            $descendant = $entry.nativeDescendant
            Assert-UiAcceptanceFields -InputObject $descendant -Required @('className','name','nativeControlType','requiredPatterns')
            $descendantClass = [string] $descendant.className
            if ($null -ne $descendant.PSObject.Properties['name']) { $descendantName = [string] $descendant.name }
            $patterns = @($descendant.requiredPatterns)
        }
        else {
            $patterns = @($entry.requiredPatterns)
        }
        if ($patterns.Count -eq 0) { continue }

        $fact = Invoke-StartupLayoutNativeLookup -ProcessId $ProcessId -AutomationId $id -Patterns $patterns `
            -DescendantClass $descendantClass -DescendantName $descendantName `
            -FailureCode 'startup_layout.native_probe_unavailable'
        if ([int] $fact.outerMatchCount -ne 1) { throw 'startup_layout.native_outer_match' }
        if (-not [string]::IsNullOrEmpty($descendantClass) -and [int] $fact.descendantMatchCount -ne 1) {
            throw 'startup_layout.native_descendant_match'
        }
        if (-not [string]::IsNullOrEmpty($descendantClass)) {
            Assert-UiAcceptanceFields -InputObject $fact -Required @('name','className','controlType')
            if ([string] $fact.name -cne $descendantName -or
                [string] $fact.className -cne $descendantClass -or
                [string] $fact.controlType -cne [string] $descendant.nativeControlType) {
                throw 'startup_layout.native_descendant_mismatch'
            }
        }
        $reportedPatterns = @($fact.patterns)
        if ($reportedPatterns.Count -ne $patterns.Count) { throw 'startup_layout.native_pattern_unknown' }
        foreach ($requiredPattern in $patterns) {
            $matches = @($reportedPatterns | Where-Object { [string] $_.name -ceq [string] $requiredPattern })
            if ($matches.Count -ne 1) { throw 'startup_layout.native_pattern_unknown' }
            $pattern = $matches[0]
            $available = $pattern.available
            if ($null -eq $available) { throw 'startup_layout.native_pattern_unknown' }
            if (-not [bool] $available) { throw 'startup_layout.native_pattern_missing' }
        }
    }
}

<#
.SYNOPSIS
    Requires exactly one row for a dynamic-row declaration, addressed only by its accessible name.
#>
function Assert-StartupLayoutDynamicRow {
    param(
        [Parameter(Mandatory)][object[]] $Nodes,
        [Parameter(Mandatory)] $Row
    )

    $prefix = [string] $Row.fixedAccessibleName
    $type = [string] $Row.nativeControlType
    $matches = @($Nodes | Where-Object {
        [string] $_.controlType -ceq $type -and ([string] $_.name) -like ($prefix + '*') })
    if ($matches.Count -ne 1) { throw 'startup_layout.dynamic_row' }
    if ([string] $matches[0].name -notlike ('*' + [string] $Row.fixedFilename + '*')) { throw 'startup_layout.dynamic_row' }
    return $matches[0]
}


<#
.SYNOPSIS
    Validates the manifest's dynamic-row selection policy without touching the product.
#>
function Assert-StartupLayoutDynamicPolicy {
    param(
        [Parameter(Mandatory)][object[]] $Rows,
        [Parameter(Mandatory)][string[]] $Phases
    )

    foreach ($row in $Rows) {
        Assert-UiAcceptanceFields -InputObject $row -Required @(
            'file','controlType','nativeControlType','selector','fixedAccessibleName','fixedFilename',
            'forbidGuidSelectors','phase')
        if ([string] $row.selector -cne 'accessible_name_prefix') { throw 'startup_layout.dynamic_selector' }
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
    Verifies one repository fixture and copies it into the owned seed directory.

.DESCRIPTION
    Only a fixture whose size and SHA-256 match the manifest is used, and only the owned inbox is
    written. A missing or altered fixture is a blocked gate, not a product failure.
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
    [IO.File]::Copy($source, $destination, $true)
    return [pscustomobject] @{ Destination = $destination; Sha256 = $hash }
}

# Preconditions this host cannot satisfy never count as product failures; a pattern the probe
# cannot establish must block rather than pass.
$blockedFailureCodes = @(
    'startup_layout.desktop_unavailable',
    'startup_layout.foreground_unavailable',
    'startup_layout.fixture_missing',
    'startup_layout.fixture_mismatch',
    'startup_layout.fixture_target',
    'startup_layout.native_probe_unavailable',
    'startup_layout.native_pattern_unknown'
)

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
    Assert-UiAcceptanceFields -InputObject $contract -Required @(
        'entries','dynamicRows','fixtureAssets','sessionPreparation','phaseRequiredIds','layoutProbe','layoutViewports')
    Assert-UiAcceptanceFields -InputObject $contract.phaseRequiredIds -Required @(
        'startupSettings','inboxReady','rowReady','rowSelected','drawerOpen','drawerClosed')

    $entryIndex = @{}
    foreach ($entry in @($contract.entries)) {
        Assert-UiAcceptanceFields -InputObject $entry -Required @('automationId','name','controlType','requiredPatterns','scope')
        $entryId = [string] $entry.automationId
        if ($entryIndex.ContainsKey($entryId)) { throw 'startup_layout.contract_duplicate_entry' }
        $entryIndex[$entryId] = $entry
    }
    if ($entryIndex.Count -eq 0) { throw 'startup_layout.contract_empty' }

    # The window handle is identified by exactly one window-scope entry; never by a literal here.
    $windowEntries = @($contract.entries | Where-Object { [string] $_.scope -ceq 'window' })
    if ($windowEntries.Count -ne 1) { throw 'startup_layout.window_selector' }
    $windowId = [string] $windowEntries[0].automationId

    $probe = $contract.layoutProbe
    Assert-UiAcceptanceFields -InputObject $probe -Required @(
        'navigationItemAutomationId','navigationPaneAutomationId','navigationPaneClassName',
        'navigationPaneParentAutomationId',
        'inlineInspectorAutomationId','inspectorOpenButtonAutomationId','pixelTolerance')
    $tolerance = [int] $probe.pixelTolerance

    $declared = @($contract.layoutViewports)
    $accepted = Get-UiAcceptanceRequiredViewport
    if ($declared.Count -ne $accepted.Count) { throw 'startup_layout.viewport_contract' }
    for ($position = 0; $position -lt $accepted.Count; $position++) {
        if ([int] $declared[$position].widthDip -ne [int] $accepted[$position].WidthDip -or
            [int] $declared[$position].heightDip -ne [int] $accepted[$position].HeightDip) {
            throw 'startup_layout.viewport_contract'
        }
        if ($null -eq $declared[$position].PSObject.Properties['navigationPaneWidthDip']) {
            throw 'startup_layout.viewport_contract'
        }
    }

    # --- row policy, phase sets and fixture declarations: validated before the product is touched ---
    $phases = Get-UiAcceptanceDynamicRowPhase
    $rows = @($contract.dynamicRows)
    if ($rows.Count -eq 0) { throw 'startup_layout.contract_empty' }
    Assert-StartupLayoutDynamicPolicy -Rows $rows -Phases $phases

    $phaseSets = @{}
    foreach ($phaseName in @('startupSettings','inboxReady','rowReady','rowSelected','drawerOpen','drawerClosed')) {
        $ids = @($contract.phaseRequiredIds.$phaseName)
        if ($ids.Count -eq 0) { throw 'startup_layout.contract_empty' }
        foreach ($id in $ids) {
            if (-not $entryIndex.ContainsKey([string] $id)) { throw 'startup_layout.phase_unknown_entry' }
        }
        $phaseSets[$phaseName] = [string[]] $ids
    }

    $preparation = $contract.sessionPreparation
    Assert-UiAcceptanceFields -InputObject $preparation -Required @(
        'materializedRowPhase','seedFilenames','savedStatusText','savedStatusAutomationId','settingsNavigationAutomationId',
        'inboxNavigationAutomationId','saveFolderSettingsAutomationId','scanTriggerAutomationId','folderInputs')
    $materializedPhase = [string] $preparation.materializedRowPhase
    if ($phases -cnotcontains $materializedPhase) { throw 'startup_layout.dynamic_phase' }
    $materializedRows = @($rows | Where-Object { [string] $_.phase -ceq $materializedPhase })
    if ($materializedRows.Count -eq 0) { throw 'startup_layout.dynamic_phase' }

    $folderInputs = @($preparation.folderInputs)
    if ($folderInputs.Count -eq 0) { throw 'startup_layout.preparation_contract' }
    foreach ($folderInput in $folderInputs) {
        Assert-UiAcceptanceFields -InputObject $folderInput -Required @('key','automationId','relativeDirectory')
        if (-not $entryIndex.ContainsKey([string] $folderInput.automationId)) { throw 'startup_layout.preparation_contract' }
    }
    if (-not $entryIndex.ContainsKey([string] $preparation.savedStatusAutomationId)) { throw 'startup_layout.preparation_contract' }

    $assets = @($contract.fixtureAssets)
    $seedNames = @($preparation.seedFilenames)
    if ($seedNames.Count -eq 0 -or @($seedNames | Select-Object -Unique).Count -ne $seedNames.Count) {
        throw 'startup_layout.fixture_target'
    }
    foreach ($seedName in $seedNames) {
        $assetMatches = @($assets | Where-Object { [string] $_.fixedFilename -ceq [string] $seedName })
        if ($assetMatches.Count -ne 1) { throw 'startup_layout.fixture_missing' }
        Assert-UiAcceptanceFields -InputObject $assetMatches[0] -Required @(
            'fixedFilename','repositoryPath','sha256','sizeBytes','seedDirectory')
        if ([string]$assetMatches[0].seedDirectory -cne 'inbox') { throw 'startup_layout.fixture_target' }
    }
    foreach ($row in $materializedRows) {
        if ($seedNames -cnotcontains [string] $row.fixedFilename) { throw 'startup_layout.fixture_missing' }
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

    # --- phase: startupSettings (a brand-new owned root has no media rows at all) ---
    $startup = Read-StartupLayoutNodes -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
    if ($startup.Index.Count -eq 0) { throw 'startup_layout.startup_empty_tree' }
    Assert-StartupLayoutPhase -Index $startup.Index -PhaseIds $phaseSets['startupSettings'] -Entries $entryIndex -Phase 'startupSettings'
    Assert-StartupLayoutNativeFacts -ProcessId $productProcessId -PhaseIds $phaseSets['startupSettings'] -Entries $entryIndex

    # --- preparation: owned folders through the product's own Settings UI ---
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

    foreach ($folderInput in $folderInputs) {
        $path = $folders[[string] $folderInput.key]
        Invoke-StartupLayoutMutation -StepId ('startup.set-' + [string] $folderInput.key + '-folder') `
            -Arguments @('ui','set-value',[string] $folderInput.automationId,$path) `
            -ProcessId $productProcessId -FailureCode 'startup_layout.folder_settings'
        $readBack = Get-StartupLayoutDriverValue -AutomationId ([string] $folderInput.automationId) `
            -ProcessId $productProcessId -FailureCode 'startup_layout.folder_settings'
        if ($readBack -cne $path) { throw 'startup_layout.folder_settings' }
    }
    Invoke-StartupLayoutMutation -StepId 'startup.save-folder-settings' `
        -Arguments @('ui','invoke',[string] $preparation.saveFolderSettingsAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.folder_settings'
    $status = Get-StartupLayoutDriverValue -AutomationId ([string] $preparation.savedStatusAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.folder_settings'
    if ($status -cne [string] $preparation.savedStatusText) { throw 'startup_layout.folder_settings' }

    # Persistence: leave the page and come back - the saved paths must still be displayed.
    Invoke-StartupLayoutMutation -StepId 'startup.leave-settings' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.folder_settings'
    Invoke-StartupLayoutMutation -StepId 'startup.return-settings' `
        -Arguments @('ui','invoke',[string] $preparation.settingsNavigationAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.folder_settings'
    foreach ($folderInput in $folderInputs) {
        $persisted = Get-StartupLayoutDriverValue -AutomationId ([string] $folderInput.automationId) `
            -ProcessId $productProcessId -FailureCode 'startup_layout.folder_settings'
        if ($persisted -cne $folders[[string] $folderInput.key]) { throw 'startup_layout.folder_settings' }
    }
    $configured = Read-StartupLayoutNodes -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
    $configuredArtifact = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory `
        -EvidenceId 'settings-configured' -Nodes $configured.Nodes -RunToken $RunToken -OwnedRoot $OwnedRoot
    $artifacts += $configuredArtifact

    # Only a fixture whose size and hash match the manifest is copied, and only into the owned
    # inbox: the library stays empty until a real ingest happens.
    foreach ($seedName in $seedNames) {
        $asset = @($assets | Where-Object { [string] $_.fixedFilename -ceq [string] $seedName })[0]
        $null = Copy-StartupLayoutFixture -Asset $asset -Folders $folders
    }

    # --- phase: inboxReady (no rows yet) ---
    Invoke-StartupLayoutMutation -StepId 'startup.open-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.inbox_navigation'
    $waitInbox = Invoke-WinAppObservation -Arguments @('ui','wait-for',[string] $phaseSets['inboxReady'][0]) `
        -ProcessId $productProcessId -TimeoutSeconds $WaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($waitInbox.Status -cne 'Passed') { throw 'startup_layout.inbox_unavailable' }
    $inbox = Read-StartupLayoutNodes -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
    $inboxArtifact = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory `
        -EvidenceId 'inbox-ready' -Nodes $inbox.Nodes -RunToken $RunToken -OwnedRoot $OwnedRoot
    $artifacts += $inboxArtifact
    Assert-StartupLayoutPhase -Index $inbox.Index -PhaseIds $phaseSets['inboxReady'] -Entries $entryIndex -Phase 'inboxReady'
    Assert-StartupLayoutNativeFacts -ProcessId $productProcessId -PhaseIds $phaseSets['inboxReady'] -Entries $entryIndex

    # --- phase: rowReady (the scan produced the real row) ---
    Invoke-StartupLayoutMutation -StepId 'startup.scan-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.scan_trigger'
    foreach ($row in $materializedRows) {
        # The row's accessible name carries a state suffix that changes over time, so the wait uses
        # the manifest's fixed prefix and the exact name is taken from the resulting tree.
        $appeared = Invoke-WinAppObservation -Arguments @('ui','wait-for',[string] $row.fixedAccessibleName) `
            -ProcessId $productProcessId -TimeoutSeconds $RowWaitTimeoutSeconds `
            -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($appeared.Status -cne 'Passed') { throw 'startup_layout.dynamic_row' }
    }
    $rowReady = Read-StartupLayoutNodes -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
    Assert-StartupLayoutPhase -Index $rowReady.Index -PhaseIds $phaseSets['rowReady'] -Entries $entryIndex -Phase 'rowReady'
    $rowNode = Assert-StartupLayoutDynamicRow -Nodes $rowReady.Nodes -Row $materializedRows[0]
    $rowArtifact = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory `
        -EvidenceId 'row-ready' -Nodes $rowReady.Nodes -RunToken $RunToken -OwnedRoot $OwnedRoot
    $artifacts += $rowArtifact
    $viewports += [pscustomobject] @{ phase = 'rowReady'; rowName = [string] $rowNode.name; tree = $rowArtifact }

    # --- phase: rowSelected ---
    # The row's accessible name embeds a status that changes as the item progresses, so the name
    # observed during the rowReady inspection can be stale by the time the row is invoked. The
    # selector is therefore resolved from a fresh inspection of its own, and a driver report that
    # the lookup failed is the only outcome that may be resolved again.
    $rowRow = $materializedRows[0]
    $rowResolver = {
        # Resolved against the driver's raw response, which is where the semantic selector and the
        # native type live; the normalized projection is only for the phase assertions.
        $current = Read-StartupLayoutTree -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
        $raw = ConvertTo-UiAcceptanceFlatNodes -Tree $current -FailureCode 'startup_layout.driver_schema_incompatible'
        $found = Find-UiAcceptanceUniqueNode -Nodes $raw `
            -NativeType ([string] $rowRow.nativeControlType) `
            -NamePrefix ([string] $rowRow.fixedAccessibleName) `
            -NameContains ([string] $rowRow.fixedFilename) `
            -FailureCode 'startup_layout.dynamic_row'
        if ($null -eq $found) { throw 'startup_layout.dynamic_row' }
        return (Get-UiAcceptanceNodeSelector -Node $found -FailureCode 'startup_layout.dynamic_row')
    }
    $selection = Invoke-UiAcceptanceResolvedMutation -StepId 'startup.select-row' `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot `
        -Verb 'invoke' -ResolveSelector $rowResolver -FailureCode 'startup_layout.row_selection' `
        -TimeoutMs $MutationTimeoutMs
    if ($selection.Status -cne 'Passed') { throw 'startup_layout.row_selection' }
    $selected = Read-StartupLayoutNodes -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
    Assert-StartupLayoutPhase -Index $selected.Index -PhaseIds $phaseSets['rowSelected'] -Entries $entryIndex -Phase 'rowSelected'
    $selectedArtifact = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory `
        -EvidenceId 'row-selected' -Nodes $selected.Nodes -RunToken $RunToken -OwnedRoot $OwnedRoot
    $artifacts += $selectedArtifact

    # --- the three viewports: pane boundary, inspector policy, screenshot and tree each ---
    foreach ($position in 0..($declared.Count - 1)) {
        $spec = $declared[$position]
        $widthDip = [int] $spec.widthDip
        $heightDip = [int] $spec.heightDip
        $inlineInspector = [bool] $spec.inlineInspector

        $resized = Resize-StartupLayoutWindow -ProcessId $productProcessId -WidthDip $widthDip -HeightDip $heightDip
        $scale = Assert-StartupLayoutClientSize -Fact $resized -WidthDip $widthDip -HeightDip $heightDip -Tolerance $tolerance

        $viewport = Read-StartupLayoutNodes -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
        $name = 'viewport-' + $widthDip + 'x' + $heightDip
        $treeRelative = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory `
            -EvidenceId $name -Nodes $viewport.Nodes -RunToken $RunToken -OwnedRoot $OwnedRoot
        $artifacts += $treeRelative
        Assert-StartupLayoutPhase -Index $viewport.Index -PhaseIds $phaseSets['rowSelected'] -Entries $entryIndex -Phase 'rowSelected'
        Assert-StartupLayoutDynamicRow -Nodes $viewport.Nodes -Row $materializedRows[0] | Out-Null

        # The navigation pane boundary is measured, never the navigation item inside it.
        $pane = Invoke-StartupLayoutNativeLookup -ProcessId $productProcessId `
            -AutomationId ([string] $probe.navigationPaneAutomationId) `
            -FailureCode 'startup_layout.native_probe_unavailable'
        if ([int] $pane.outerMatchCount -ne 1) { throw 'startup_layout.navigation_pane' }
        if ([string] $pane.className -cne [string] $probe.navigationPaneClassName) { throw 'startup_layout.navigation_pane' }
        $paneNode = Get-StartupLayoutBounds -Index $viewport.Index `
            -AutomationId ([string] $probe.navigationPaneAutomationId) `
            -FailureCode 'startup_layout.navigation_pane'
        if ([string] $paneNode.parentAutomationId -cne [string] $probe.navigationPaneParentAutomationId) {
            throw 'startup_layout.navigation_pane'
        }
        if ($null -eq $pane.width) { throw 'startup_layout.native_pattern_unknown' }
        $expectedPaneWidth = [int][Math]::Round([int] $spec.navigationPaneWidthDip * $scale, [MidpointRounding]::AwayFromZero)
        if ([Math]::Abs([int] $pane.width - $expectedPaneWidth) -gt $tolerance) { throw 'startup_layout.navigation_pane' }

        $inspectorId = [string] $probe.inlineInspectorAutomationId
        if ($inlineInspector) {
            $null = Assert-StartupLayoutState -Index $viewport.Index -AutomationId $inspectorId `
                -FailureCode 'startup_layout.inline_inspector' -Onscreen
        }
        else {
            Assert-StartupLayoutHiddenOrAbsent -Index $viewport.Index -AutomationId $inspectorId `
                -FailureCode 'startup_layout.inline_inspector'
            $null = Assert-StartupLayoutState -Index $viewport.Index `
                -AutomationId ([string] $probe.inspectorOpenButtonAutomationId) `
                -FailureCode 'startup_layout.inspector_open_button' -Onscreen -Enabled
        }

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

        $artifacts += $screenshotRelative
        $viewports += [pscustomobject] @{
            widthDip = $widthDip
            heightDip = $heightDip
            clientWidthPixels = [int] $resized.clientWidthPixels
            clientHeightPixels = [int] $resized.clientHeightPixels
            navigationPaneWidthDip = [int] $spec.navigationPaneWidthDip
            navigationPaneWidthPixels = [int] $pane.width
            inlineInspector = $inlineInspector
            tree = $treeRelative
            screenshot = $screenshotRelative
        }
    }

    # --- phase: drawerOpen, then drawerClosed, at the compact size ---
    $compactSpecs = @($declared | Where-Object { -not [bool] $_.inlineInspector })
    if ($compactSpecs.Count -eq 0) { throw 'startup_layout.viewport_contract' }
    if (-not (& $DesktopCheck)) {
        $failureCode = 'startup_layout.desktop_unavailable'
        throw $failureCode
    }
    $compact = $compactSpecs[0]
    $compactWidth = [int] $compact.widthDip
    $compactHeight = [int] $compact.heightDip
    $resized = Resize-StartupLayoutWindow -ProcessId $productProcessId -WidthDip $compactWidth -HeightDip $compactHeight
    $clientWidth = [int] $resized.clientWidthPixels

    Invoke-StartupLayoutMutation -StepId ('startup.open-inspector.' + $compactWidth) `
        -Arguments @('ui','invoke',[string] $probe.inspectorOpenButtonAutomationId) `
        -ProcessId $productProcessId -FailureCode 'startup_layout.inspector_open'
    $drawer = Read-StartupLayoutNodes -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
    Assert-StartupLayoutPhase -Index $drawer.Index -PhaseIds $phaseSets['drawerOpen'] -Entries $entryIndex -Phase 'drawerOpen'
    Assert-StartupLayoutNativeFacts -ProcessId $productProcessId -PhaseIds $phaseSets['drawerOpen'] -Entries $entryIndex
    $drawerPane = Assert-StartupLayoutState -Index $drawer.Index `
        -AutomationId ([string] $probe.inlineInspectorAutomationId) `
        -FailureCode 'startup_layout.drawer_missing' -Onscreen
    # A compact drawer is anchored to the right half of the client area.
    if ([int] $drawerPane.bounds.x -lt [int][Math]::Floor($clientWidth / 2)) { throw 'startup_layout.drawer_position' }
    $drawerArtifact = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory `
        -EvidenceId 'drawer-open' -Nodes $drawer.Nodes -RunToken $RunToken -OwnedRoot $OwnedRoot
    $artifacts += $drawerArtifact

    $closed = Invoke-WinAppKeyboard -StepId ('startup.close-drawer.' + $compactWidth) `
        -Arguments @('ui','send-keys','Escape') -ProcessId $productProcessId `
        -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck -DesktopCheck $DesktopCheck `
        -TimeoutMs $MutationTimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($closed.Status -ceq 'Blocked') { throw 'startup_layout.foreground_unavailable' }
    if ($closed.Status -cne 'Passed') { throw 'startup_layout.drawer_close' }
    $closedState = Read-StartupLayoutNodes -ProcessId $productProcessId -FailureCode 'startup_layout.inspect_unavailable'
    $closedArtifact = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory `
        -EvidenceId 'drawer-closed' -Nodes $closedState.Nodes -RunToken $RunToken -OwnedRoot $OwnedRoot
    $artifacts += $closedArtifact
    Assert-StartupLayoutPhase -Index $closedState.Index -PhaseIds $phaseSets['drawerClosed'] -Entries $entryIndex -Phase 'drawerClosed'
    Assert-StartupLayoutHiddenOrAbsent -Index $closedState.Index `
        -AutomationId ([string] $probe.inlineInspectorAutomationId) `
        -FailureCode 'startup_layout.drawer_close'

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
    return [pscustomobject] @{
        ScenarioId = $ScenarioId
        Status = $status
        Reason = $code
        Viewports = @($viewports)
        Artifacts = @($artifacts)
    }
}
