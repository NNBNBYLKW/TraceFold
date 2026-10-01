# TraceFold UI acceptance - taxonomy maintenance, merge, delete guard and undo (Task 12).
#
# One owned root, created for this run only. Three fixed assets are ingested through the product's own
# UI and given references to three author terms through the single-item library inspector, so every
# reference this scenario reasons about was established through the product and can be re-read from a
# read-only snapshot.
#
# The references are chosen so that each claim is falsifiable on its own:
#   * the source term is referenced by two assets, which is what makes "every reference moved" a claim
#     with more than one part;
#   * the target term is referenced by one of those same two assets and by no other, so the merge has
#     to keep its own reference and must not end up holding the same term twice on one asset;
#   * the third term is referenced by the remaining asset, which is what makes the delete guard a real
#     guard rather than a term nobody wants.
#
# What is proven:
#   * the source, the target and the affected file set are read from the taxonomy page and from the
#     merge preview dialog before anything is confirmed, and the preview is checked per file;
#   * one merge confirmation moves every source reference to the target, leaves the target's own
#     reference in place, and leaves no asset holding the target twice - checked per file, never as a
#     total;
#   * one undo restores the source term, the target term and every per-file reference exactly;
#   * a referenced term cannot be deleted: the product disables the delete action and names the term it
#     is blocking on, and the term is still there afterwards with its reference intact;
#   * the reference is then released through the same metadata UI, and the product's own unused filter
#     plus the term's own reference count are what prove the term is unreferenced;
#   * one delete confirmation removes it and one undo restores it;
#   * no physical file name, file byte or record identity ever changes, and the state survives closing
#     and reopening the application.
#
# Measured product behaviour this scenario depends on:
#   * the taxonomy term list renders each term from a data template, so a row carries no term name of
#     its own: a row is found by the text its own children render, and only rows that are on screen
#     count, because a collapsed control keeps what it had already realised in the tree;
#   * changing the kind, the search text or the reference filter reloads the list and clears the
#     selection, so a term is always selected after the list was last narrowed;
#   * the merge dialog opens with the selected terms as its sources, lists the same-kind candidates as
#     its targets, and shows one row per affected file with its before and after summaries - only the
#     files whose assignment actually changes appear;
#   * the undo banner is open exactly while the last maintenance operation can still be undone, so it
#     is what "exactly one undo is available" and "the undo was consumed" are read from;
#   * the delete confirmation is a dialog the product builds for that one operation, and the delete
#     action on the page is disabled while any selected term is still referenced.
#
# Read-only guarantees: every conclusion is paired with a read-only snapshot or a file hash, and every
# mutation carries its own step id. A timeout or an ambiguous outcome is never re-sent.
#
# Bans: no coordinates, no fixed delays, no termination by process name, no shell folder picker, no
# product database access, and no reuse of another scenario's root.

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
    [int] $TaxonomyWaitSeconds = 60,
    [int] $ObservationTimeoutSeconds = 20,
    [int] $InspectDepth = 20,
    [int] $MutationTimeoutMs = 60000,
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

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))

function New-TaxonomyText {
    param([Parameter(Mandatory)][int[]] $CodePoints)

    $builder = [System.Text.StringBuilder]::new()
    foreach ($codePoint in $CodePoints) { [void] $builder.Append([char] $codePoint) }
    return $builder.ToString()
}

# Term names and the product's own labels, composed from code points so this file stays pure ASCII.
$mergeSourceName = New-TaxonomyText -CodePoints @(0x5408, 0x5E76, 0x6E90)
$mergeTargetName = New-TaxonomyText -CodePoints @(0x5408, 0x5E76, 0x76EE, 0x6807)
$deleteTermName = New-TaxonomyText -CodePoints @(0x5F85, 0x5220, 0x8BCD, 0x6761)
$authorKindName = New-TaxonomyText -CodePoints @(0x4F5C, 0x8005)
$seriesKindName = New-TaxonomyText -CodePoints @(0x7CFB, 0x5217)
$unusedFilterName = New-TaxonomyText -CodePoints @(0x672A, 0x4F7F, 0x7528)
$removeTermName = New-TaxonomyText -CodePoints @(0x79FB, 0x9664, 0x6240, 0x9009, 0x8BCD, 0x6761)
$referenceUnitName = New-TaxonomyText -CodePoints @(0x9879, 0x5F15, 0x7528)
$libraryLocationLabel = New-TaxonomyText -CodePoints @(0x6B63, 0x5F0F, 0x5E93)
$inboxLocationLabel = New-TaxonomyText -CodePoints @(0x5F85, 0x5165, 0x5E93)
$totalLabel = New-TaxonomyText -CodePoints @(0x5408, 0x8BA1)
$beforeLabel = New-TaxonomyText -CodePoints @(0x5408, 0x5E76, 0x524D)
$afterLabel = New-TaxonomyText -CodePoints @(0x5408, 0x5E76, 0x540E)
$deleteConfirmName = New-TaxonomyText -CodePoints @(0x5220, 0x9664)
# The separator the product joins a file's relevant terms with.
$termSeparator = [string] [char] 0x3001

$fixtures = @('photo.jpg', 'photo-replacement.jpg', 'photo-third.jpg')
# Per-asset references this scenario establishes: the source is referenced twice, the target keeps its
# own reference on the same asset the source is leaving, and the third term holds the guard.
$referencePlan = @{
    'photo.jpg' = @($mergeSourceName)
    'photo-replacement.jpg' = @($mergeSourceName, $mergeTargetName)
    'photo-third.jpg' = @($deleteTermName)
}
# The assignment each asset must hold after the source is merged into the target: the source is replaced
# by the target wherever it was referenced, an asset that already held the target holds it once, and an
# asset that never held the source is untouched.
$expectedAfterMerge = @{
    'photo.jpg' = @($mergeTargetName)
    'photo-replacement.jpg' = @($mergeTargetName)
    'photo-third.jpg' = @($deleteTermName)
}

$productProcessId = 0
$launched = $null
$artifacts = [System.Collections.Generic.List[string]]::new()

<#
.SYNOPSIS
    Shapes one failed driver result into a redacted, self-describing failure code.

.DESCRIPTION
    The failing step and the driver's own reason are kept as slugs, so a refused transport, a missing
    element and a dispatched-but-failed action can be told apart without any raw text reaching the
    report. A dispatch the harness refused because the interactive desktop or the foreground was not
    ours never reached the product, so it is reported as the blocked environment condition it is.
#>
function New-TaxonomyFailureCode {
    param(
        [Parameter(Mandatory)][AllowNull()] $Result,
        [Parameter(Mandatory)][string] $Step,
        [Parameter(Mandatory)][string] $Code
    )

    if ([string] $Result.Status -ceq 'Blocked' -and
        [string] $Result.Error -ceq 'foreground desktop ownership unavailable') {
        return 'taxonomy.desktop_unavailable'
    }
    $detail = 'driver_unavailable'
    if ($null -ne $Result.Data -and $null -ne $Result.Data.PSObject.Properties['error'] -and
        $null -ne $Result.Data.error.PSObject.Properties['code']) {
        $detail = ConvertTo-UiAcceptanceSlug -Value ([string] $Result.Data.error.code)
    }
    $errorSlug = ''
    if (-not [string]::IsNullOrWhiteSpace([string] $Result.Error)) {
        $errorSlug = '.' + (ConvertTo-UiAcceptanceSlug -Value ([string] $Result.Error))
        if ($errorSlug.Length -gt 120) { $errorSlug = $errorSlug.Substring(0, 120) }
    }
    return ($Code + '.' + (ConvertTo-UiAcceptanceSlug -Value $Step) + '.' + $detail +
        '.' + (ConvertTo-UiAcceptanceSlug -Value ([string] $Result.Status)) +
        '.exit' + [string] $Result.ExitCode + $errorSlug)
}

function Invoke-TaxonomyAction {
    param([string] $Step, [string[]] $Arguments, [string] $Code)

    $result = Invoke-WinAppMutation -StepId ('taxonomy.' + $Step) -Arguments $Arguments `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -TimeoutMs $MutationTimeoutMs `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($result.Status -cne 'Passed') {
        throw (New-TaxonomyFailureCode -Result $result -Step $Step -Code $Code)
    }
    return $result
}

<#
.SYNOPSIS
    Sends one keyboard mutation and reports the driver's own reason when it fails.

.DESCRIPTION
    A keyboard dispatch is a mutation, so it is never retried; the failure reason is redacted to a slug
    and kept, because a refused transport and a missing element are different product facts.
#>
function Invoke-TaxonomyKeyboard {
    param([string] $Step, [string[]] $Arguments, [string] $Code)

    $result = Invoke-WinAppKeyboard -StepId ('taxonomy.' + $Step) -Arguments $Arguments `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck `
        -DesktopCheck $DesktopCheck -RunToken $RunToken -OwnedRoot $OwnedRoot -TimeoutMs $MutationTimeoutMs
    if ($result.Status -cne 'Passed') {
        throw (New-TaxonomyFailureCode -Result $result -Step $Step -Code $Code)
    }
    return $result
}

function Read-TaxonomyNodes {
    return @(Read-UiAcceptanceTree -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -FailureCode 'taxonomy.inspect_unavailable' `
        -Depth $InspectDepth -TimeoutSeconds $ObservationTimeoutSeconds)
}

<#
.SYNOPSIS
    Converts one raw driver node into the reviewed node shape the evidence writer takes.

.DESCRIPTION
    The driver answers with its own native field names, while the evidence writer takes the reviewed
    shape. The conversion is done here rather than in the writer so the writer keeps one input
    contract and the mapping stays next to the scenario that depends on it.

    The driver omits a field an element does not have, so an absent automation id or name becomes an
    empty string and an absent rectangle becomes a rectangle with zeroes in every field, which the
    writer accepts as an unmeasured element. A rectangle the driver reported only in part is passed
    on as it arrived, so the writer refuses it rather than having the missing edges completed here.
    The fields the driver always reports are required, because an element whose type, enablement or
    visibility it did not state is not an element this scenario can reason about.
#>
function ConvertTo-TaxonomyReviewedNode {
    param(
        [Parameter(Mandatory)] $Node,
        [AllowEmptyString()][string] $ParentId = ''
    )

    foreach ($field in @('type','isEnabled','isOffscreen')) {
        if ($null -eq $Node.PSObject.Properties[$field]) { throw 'taxonomy.driver_schema_incompatible' }
    }
    $id = ''
    if ($null -ne $Node.PSObject.Properties['automationId']) { $id = [string] $Node.automationId }
    $name = ''
    if ($null -ne $Node.PSObject.Properties['name']) { $name = [string] $Node.name }
    $bounds = $null
    $present = 0
    foreach ($field in @('x','y','width','height')) {
        $property = $Node.PSObject.Properties[$field]
        if ($null -eq $property -or $null -eq $property.Value) { continue }
        $present++
    }
    if ($present -eq 4) {
        $bounds = [pscustomobject] @{
            x = [int] $Node.x; y = [int] $Node.y
            width = [int] $Node.width; height = [int] $Node.height
        }
    }
    elseif ($present -ne 0) {
        # An incomplete rectangle is handed to the writer as it arrived so the writer refuses it.
        $bounds = [pscustomobject] @{ x = [int] $Node.x; y = [int] $Node.y }
    }
    return [pscustomobject] @{
        automationId = $id
        parentAutomationId = $ParentId
        controlType = [string] $Node.type
        name = $name
        enabled = [bool] $Node.isEnabled
        offscreen = [bool] $Node.isOffscreen
        bounds = $bounds
    }
}

<#
.SYNOPSIS
    Reads the inspected window in the reviewed shape the evidence writer takes.

.DESCRIPTION
    The same inspection that feeds the readers is converted once into the reviewed shape, so the
    evidence describes the tree that was actually read rather than a separate reading that might
    differ.

    Repetition is collapsed while the nodes still carry the driver's own field names, because that
    is the only shape in which two reports of an element can still be told apart. The reviewed shape
    folds the native type, both state flags and the rectangle into fields of its own, so elements
    that differ only in one of those are indistinguishable there: collapsing them afterwards keeps
    one and drops the rest, which is how same-named elements that differ in nothing but position,
    type or state were lost from the evidence. The shared converter decides which reports survive,
    and the walk that converts them is the same walk that gives a child the id of the parent it was
    read under, so collapsing first costs nothing that the evidence needs.
#>
function Read-TaxonomyEvidenceNodes {
    $observation = Invoke-WinAppObservation -Arguments @('ui','inspect','--depth',[string] $InspectDepth) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed') { throw 'taxonomy.inspect_unavailable' }
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['windows']) {
        throw 'taxonomy.driver_schema_incompatible'
    }
    $observed = New-Object 'System.Collections.Generic.List[object]'
    foreach ($node in @(ConvertTo-UiAcceptanceFlatNodes -Tree $observation.Data `
            -FailureCode 'taxonomy.driver_schema_incompatible')) {
        $observed.Add($node)
    }
    $nodes = New-Object 'System.Collections.Generic.List[object]'
    foreach ($window in @($observation.Data.windows)) {
        if ($null -eq $window.PSObject.Properties['elements']) { throw 'taxonomy.driver_schema_incompatible' }
        foreach ($element in @($window.elements)) {
            Add-TaxonomyEvidenceNodes -Node $element -ParentId '' -Observed $observed -Nodes $nodes
        }
    }
    return $nodes.ToArray()
}

<#
.SYNOPSIS
    Walks one raw driver subtree depth first, converting every node into the reviewed shape and
    keeping the ones the collapse left.

.DESCRIPTION
    A node the collapse dropped is a repeat of an element that was already converted, so it is not
    converted again. It is still walked into, because the collapse is per element: a child is its own
    element with an identity of its own, and it keeps the id of the parent it was read under whether
    or not that parent was the report that survived.
#>
function Add-TaxonomyEvidenceNodes {
    param(
        [Parameter(Mandatory)] $Node,
        [Parameter(Mandatory)][AllowEmptyString()][string] $ParentId,
        [Parameter(Mandatory)] $Observed,
        [Parameter(Mandatory)] $Nodes
    )

    # The conversion is what states the node's id, so the id a child reports as its parent is taken
    # from the conversion itself rather than from a second rule that could disagree with it.
    $converted = ConvertTo-TaxonomyReviewedNode -Node $Node -ParentId $ParentId
    if ($Observed.Contains($Node)) { $Nodes.Add($converted) }
    if ($null -ne $Node.PSObject.Properties['children']) {
        foreach ($child in @($Node.children)) {
            Add-TaxonomyEvidenceNodes -Node $child -ParentId ([string] $converted.automationId) `
                -Observed $Observed -Nodes $Nodes
        }
    }
}

function Test-TaxonomyWait {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'taxonomy.observation_unavailable' }
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['found']) {
        throw 'taxonomy.driver_schema_incompatible'
    }
    return [bool] $observation.Data.found
}

<#
.SYNOPSIS
    Waits until an element is gone.

.DESCRIPTION
    A disappearance wait answers whether the wait condition was met, so a successful observation is
    what success means here. The element's own presence flag describes the end state rather than the
    outcome of the wait, which is why it is not read as the answer.
#>
function Wait-TaxonomyGone {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'taxonomy.observation_unavailable' }
    return $true
}

function Read-TaxonomyValue {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][string] $Code)

    $observation = Invoke-WinAppObservation -Arguments @('ui','get-value',$AutomationId) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed' -or $null -eq $observation.Data) { throw $Code }
    if ($null -eq $observation.Data.PSObject.Properties['text']) { throw 'taxonomy.driver_schema_incompatible' }
    return [string] $observation.Data.text
}

function Read-TaxonomyValueBySelector {
    param([Parameter(Mandatory)][string] $Selector, [Parameter(Mandatory)][string] $Code)

    $observation = Invoke-WinAppObservation -Arguments @('ui','get-value',$Selector) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed' -or $null -eq $observation.Data) { throw $Code }
    if ($null -eq $observation.Data.PSObject.Properties['text']) { throw 'taxonomy.driver_schema_incompatible' }
    return [string] $observation.Data.text
}

function Test-TaxonomyEnabled {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][int] $Seconds)

    return (Test-TaxonomyWait -Seconds $Seconds -Arguments @(
            'ui','wait-for',$AutomationId,'--property','IsEnabled','--value','True'))
}

function Read-TaxonomyState {
    param([Parameter(Mandatory)][string] $EvidenceId)

    # The snapshot carries the terms as they are spelled, so its output is taken as UTF-8 and the
    # payload is parsed from that text. Letting the machine's code page decode it would corrupt a
    # term whose last encoded byte is a lead byte, and the JSON around it would not survive.
    $snapshot = Invoke-UiAcceptanceSupportCommand -SupportPath $SupportPath `
        -Arguments @('snapshot','--data-root',$OwnedRoot,'--token',$RunToken) -TimeoutMs $MutationTimeoutMs
    if ($snapshot.ExitCode -ne 0) { throw 'taxonomy.snapshot_unavailable' }
    $data = ConvertFrom-UiAcceptanceJson -Text $snapshot.Stdout -Source 'snapshot'
    Assert-UiAcceptanceFields -InputObject $data -Required @('tables','taxonomy','formalAssets')
    $relative = (ConvertTo-UiAcceptanceSlug -Value $EvidenceId) + '-snapshot.json'
    $text = ConvertTo-Json -InputObject ([ordered] @{
        tables       = $data.tables
        taxonomy     = $data.taxonomy
        formalAssets = @($data.formalAssets | ForEach-Object {
            $asset = $_
            $fields = [ordered] @{}
            foreach ($field in @('assetKey','actualFilename','displayName','mediaKind')) {
                $value = ''
                if ($null -ne $asset.PSObject.Properties[$field] -and $null -ne $asset.PSObject.Properties[$field].Value) {
                    $value = [string] $asset.PSObject.Properties[$field].Value
                }
                $fields[$field] = Protect-UiAcceptanceText -Value $value -RunToken $RunToken -OwnedRoot $OwnedRoot
            }
            $fields['authors'] = @($asset.authors | ForEach-Object {
                Protect-UiAcceptanceText -Value ([string] $_) -RunToken $RunToken -OwnedRoot $OwnedRoot })
            $fields['series'] = Protect-UiAcceptanceText -Value ([string] $asset.series) -RunToken $RunToken -OwnedRoot $OwnedRoot
            $fields['tags'] = @($asset.tags | ForEach-Object {
                Protect-UiAcceptanceText -Value ([string] $_) -RunToken $RunToken -OwnedRoot $OwnedRoot })
            $fields
        })
    }) -Depth 12
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $relative), $text, [Text.UTF8Encoding]::new($false))
    $artifacts.Add($relative)
    return $data
}

<#
.SYNOPSIS
    Returns one asset's recorded facts, refusing a missing or duplicated record.
#>
function Get-TaxonomyAssetFacts {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)][string] $Filename,
        [Parameter(Mandatory)][string] $Code
    )

    $matches = @($State.formalAssets | Where-Object {
        [string] $_.actualFilename -ceq $Filename })
    if ($matches.Count -ne 1) {
        throw ($Code + '.' + (ConvertTo-UiAcceptanceSlug -Value $Filename) + '.n' + [string] $matches.Count)
    }
    $asset = $matches[0]
    return [pscustomobject] @{
        AssetKey       = [string] $asset.assetKey
        ActualFilename = [string] $asset.actualFilename
        DisplayName    = [string] $asset.displayName
        Authors        = @($asset.authors | ForEach-Object { [string] $_ } | Sort-Object)
        Series         = [string] $asset.series
        Tags           = @($asset.tags | ForEach-Object { [string] $_ } | Sort-Object)
    }
}

<#
.SYNOPSIS
    Returns the authors every asset records, keyed by actual file name.

.DESCRIPTION
    Reference ownership is read per file rather than as a total, because a total cannot say which file
    holds a term, and moving a reference is exactly a claim about which files hold it.
#>
function Get-TaxonomyAuthorOwnership {
    param([Parameter(Mandatory)] $State)

    $ownership = [ordered] @{}
    foreach ($name in $fixtures) {
        $facts = Get-TaxonomyAssetFacts -State $State -Filename $name -Code 'taxonomy.asset_missing'
        $ownership[$name] = @($facts.Authors)
    }
    return $ownership
}

<#
.SYNOPSIS
    Reports the assets that reference a term, read from the snapshot's per-file facts.
#>
function Get-TaxonomyTermOwners {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)][string] $Term
    )

    $owners = @()
    foreach ($name in $fixtures) {
        $facts = Get-TaxonomyAssetFacts -State $State -Filename $name -Code 'taxonomy.asset_missing'
        if (@($facts.Authors) -ccontains $Term) { $owners += $name }
    }
    return ,@($owners)
}

function Save-TaxonomyEvidence {
    param([Parameter(Mandatory)][string] $EvidenceId, [Parameter(Mandatory)][object[]] $Nodes)
    return (Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory -EvidenceId $EvidenceId `
        -Nodes $Nodes -RunToken $RunToken -OwnedRoot $OwnedRoot)
}

<#
.SYNOPSIS
    Writes the current window as redacted UIA tree evidence.

.DESCRIPTION
    The inspection is converted into the reviewed shape before it is written, because the writer
    takes one clear input contract and the driver's native shape is not it. Reading and converting in
    one step keeps the evidence a description of the tree at the moment the observation was taken,
    rather than of a later reading taken for the sake of writing it down.
#>
function Save-TaxonomyTree {
    param([Parameter(Mandatory)][string] $EvidenceId)

    $nodes = Read-TaxonomyEvidenceNodes
    return (Save-TaxonomyEvidence -EvidenceId $EvidenceId -Nodes $nodes)
}

function Save-TaxonomyScreenshot {
    param([Parameter(Mandatory)][string] $Selector, [Parameter(Mandatory)][string] $Name)

    $source = Join-Path $OwnedRoot ('screenshots/' + $Name + '.png')
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($source))
    $captured = Invoke-WinAppObservation -Arguments @('ui','screenshot',$Selector,'--output',$source) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($captured.Status -cne 'Passed' -or -not [IO.File]::Exists($source)) {
        throw 'taxonomy.screenshot_unavailable'
    }
    Assert-UiOwnedEvidenceFile -Path $source -OwnedRoot $OwnedRoot
    $bytes = [IO.File]::ReadAllBytes($source)
    Assert-UiPngBytes -Bytes $bytes
    $relative = $Name + '.png'
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory $relative), $bytes)
    $artifacts.Add($relative)
    return $relative
}

<#
.SYNOPSIS
    Records one stage's observed state as a run artifact.
#>
function Save-TaxonomyStageState {
    param(
        [Parameter(Mandatory)][string] $Stage,
        [Parameter(Mandatory)][System.Collections.IDictionary] $Facts
    )

    $record = [ordered] @{ stage = $Stage }
    foreach ($key in $Facts.Keys) { $record[$key] = $Facts[$key] }
    $relative = 'taxonomy-stage-' + (ConvertTo-UiAcceptanceSlug -Value $Stage) + '.json'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $relative),
        (ConvertTo-Json -InputObject $record -Depth 8), [Text.UTF8Encoding]::new($false))
    $artifacts.Add($relative)
    return $relative
}

<#
.SYNOPSIS
    Returns one list container's rows together with the text each row renders.
#>
function Get-TaxonomyRows {
    param([Parameter(Mandatory)][string] $ContainerId, [Parameter(Mandatory)][string] $Code)

    return (Get-UiAcceptanceListRowTexts -Nodes (Read-TaxonomyNodes) -ContainerId $ContainerId `
        -FailureCode $Code)
}

<#
.SYNOPSIS
    Returns the one node a stated automation id addresses, visible or not.

.DESCRIPTION
    Some containers are only reported off screen once the layout collapses them, and their state is
    still a fact worth recording, so this lookup does not filter on visibility. The caller decides
    what visibility it needs. Nothing matching and more than one matching are both refused.
#>
function Read-TaxonomyNode {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][string] $Code)

    return (Find-UiAcceptanceUniqueNode -Nodes (Read-TaxonomyNodes) -AutomationId $AutomationId `
        -FailureCode $Code)
}

<#
.SYNOPSIS
    Waits, by repeated observation, until a list shows exactly the expected rows.

.DESCRIPTION
    The list is reloaded in response to a kind, search or filter change, so its rows are read until the
    set of rendered row texts matches. Reads are the only pacing, because a fixed delay would encode
    one machine's speed, and the set that was actually seen is reported when the wait runs out.
#>
function Wait-TaxonomyRowSet {
    param(
        [Parameter(Mandatory)][string] $ContainerId,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Expected,
        [Parameter(Mandatory)][string] $Code
    )

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $seen = @()
    while ($deadline.Elapsed.TotalSeconds -lt $TaxonomyWaitSeconds) {
        $rows = Get-TaxonomyRows -ContainerId $ContainerId -Code $Code
        $seen = @($rows | ForEach-Object { @($_.Texts) -join '|' })
        if (Test-UiAcceptanceRowSetMatch -Observed $seen -Expected $Expected) { return ,@($rows) }
    }
    $slug = ConvertTo-UiAcceptanceSlug -Value ((@($seen) | Sort-Object) -join ' / ')
    if ($slug.Length -gt 110) { $slug = $slug.Substring(0, 110) }
    throw ($Code + '.n' + [string] @($seen).Count + '.' + $slug)
}

<#
.SYNOPSIS
    Finds the one visible row of a list that renders the stated text.

.DESCRIPTION
    A row of these lists is identified by the text its own children render, never by a row index or a
    position, so a row that merely sits next to the wanted one can never be selected. Nothing matching
    and more than one matching are both refused, and the rows that were seen are named in the failure.
#>
function Find-TaxonomyRow {
    param(
        [Parameter(Mandatory)][string] $ContainerId,
        [Parameter(Mandatory)][string] $Text,
        [Parameter(Mandatory)][string] $Code
    )

    $rows = Get-TaxonomyRows -ContainerId $ContainerId -Code $Code
    $matches = @($rows | Where-Object { @($_.Texts) -ccontains $Text })
    if ($matches.Count -ne 1) {
        $slug = ConvertTo-UiAcceptanceSlug -Value ((@($rows | ForEach-Object { @($_.Texts) -join '|' }) | Sort-Object) -join ' / ')
        if ($slug.Length -gt 110) { $slug = $slug.Substring(0, 110) }
        throw ($Code + '.n' + [string] $matches.Count + '.' + $slug)
    }
    return $matches[0]
}

<#
.SYNOPSIS
    Invokes one list row through its freshly observed selector.

.DESCRIPTION
    The list re-renders whenever the query changes, so a selector observed earlier can go stale. The
    shared resolver re-observes only when the driver reports a lookup-stage miss, which means nothing
    was dispatched, and each dispatch carries its own step id.
#>
function Invoke-TaxonomyRow {
    param(
        [Parameter(Mandatory)][string] $Step,
        [Parameter(Mandatory)][string] $ContainerId,
        [Parameter(Mandatory)][string] $Text,
        [Parameter(Mandatory)][string] $Code
    )

    $result = Invoke-UiAcceptanceResolvedMutation -StepId ('taxonomy.' + $Step) `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot `
        -Verb 'invoke' -TimeoutMs $MutationTimeoutMs -FailureCode $Code `
        -ResolveSelector { (Find-TaxonomyRow -ContainerId $ContainerId -Text $Text -Code $Code).Selector }
    if ($result.Status -cne 'Passed') {
        throw (New-TaxonomyFailureCode -Result $result -Step $Step -Code $Code)
    }
}

<#
.SYNOPSIS
    Selects one option of a product combo box by its exact visible label.
#>
function Select-TaxonomyOption {
    param(
        [Parameter(Mandatory)][string] $ComboBoxId,
        [Parameter(Mandatory)][string] $OptionName,
        [Parameter(Mandatory)][string] $Step,
        [Parameter(Mandatory)][string] $Code
    )

    $null = Invoke-TaxonomyAction -Step ($Step + '-open') -Arguments @('ui','invoke',$ComboBoxId) -Code ($Code + '.open')
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        $nodes = Read-TaxonomyNodes
        $items = @($nodes | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq $OptionName })
        if ($items.Count -eq 1) {
            $selector = Get-UiAcceptanceNodeSelector -Node $items[0] -FailureCode $Code
            $null = Invoke-TaxonomyAction -Step $Step -Arguments @('ui','invoke',$selector) -Code $Code
            return
        }
        if ($items.Count -gt 1) {
            throw ($Code + '.options' + [string] $items.Count)
        }
    }
    throw ($Code + '.option_missing')
}

<#
.SYNOPSIS
    Resolves the unique text box inside an observed picker.
#>
function Get-TaxonomyPickerTextBox {
    param([Parameter(Mandatory)][AllowNull()] $Node, [Parameter(Mandatory)][string] $Code)

    $matches = [System.Collections.Generic.List[object]]::new()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $Node)) { $pending.Push($child) }
    while ($pending.Count -gt 0) {
        $candidate = $pending.Pop()
        if ((Get-UiAcceptanceNodeProperty -Node $candidate -Name 'type') -ceq 'Edit' -and
            (Get-UiAcceptanceNodeProperty -Node $candidate -Name 'className') -ceq 'TextBox') {
            $matches.Add($candidate)
        }
        foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $candidate)) { $pending.Push($child) }
    }
    if ($matches.Count -ne 1) { throw ($Code + '.' + [string] $matches.Count) }
    return (Get-UiAcceptanceNodeSelector -Node $matches[0] -FailureCode $Code)
}

<#
.SYNOPSIS
    Types a query into one auto-suggest search box.

.DESCRIPTION
    The product applies the query as the text changes, so the query takes effect while it is typed and
    there is no query handler to confirm it with. The box is therefore typed into rather than assigned,
    because only a real per-character input raises the change the product listens for. The rows the
    caller asserts are what prove the query was applied.
#>
function Set-TaxonomySearch {
    param(
        [Parameter(Mandatory)][string] $OuterId,
        [Parameter(Mandatory)][string] $Query,
        [Parameter(Mandatory)][string] $Step
    )

    $nodes = Read-TaxonomyNodes
    $outer = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $OuterId -FailureCode 'taxonomy.search_input'
    if ($null -eq $outer) { throw 'taxonomy.search_input' }
    $inner = Get-TaxonomyPickerTextBox -Node $outer -Code 'taxonomy.search_input'
    $null = Invoke-TaxonomyKeyboard -Step ($Step + '-type') -Code 'taxonomy.search_input' `
        -Arguments @('ui','send-keys',$Query,'--verbatim','--target',$inner)
}

<#
.SYNOPSIS
    Empties one search box through the keyboard.

.DESCRIPTION
    A second query would be appended to the first, because the box is typed into, so the box is
    emptied first: the caret is moved to the end of the text and the backspace key is pressed more
    times than the longest query this scenario types, which is idempotent rather than timed. The keys
    go as one token list, because the driver takes the keys as a single argument. The caller asserts the
    rows the emptied query restores, which is the product's own proof that the query is gone.
#>
function Clear-TaxonomySearch {
    param(
        [Parameter(Mandatory)][string] $OuterId,
        [Parameter(Mandatory)][string] $Step
    )

    $nodes = Read-TaxonomyNodes
    $outer = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $OuterId -FailureCode 'taxonomy.search_input'
    if ($null -eq $outer) { throw 'taxonomy.search_input' }
    $inner = Get-TaxonomyPickerTextBox -Node $outer -Code 'taxonomy.search_input'
    $presses = @('end')
    for ($index = 0; $index -lt 24; $index++) { $presses += 'vk=0x08' }
    $null = Invoke-TaxonomyKeyboard -Step ($Step + '-clear') -Code 'taxonomy.search_input' `
        -Arguments @('ui','send-keys',($presses -join ' '),'--target',$inner)
}

<#
.SYNOPSIS
    Commits one term into the single-item inspector's picker, addressed by its own identifiers.

.DESCRIPTION
    The inspector's pickers commit the typed name on Enter, which is how a term that does not exist yet
    is created and how an existing one is attached to the file that is being edited.
#>
function Set-TaxonomyInspectorTerm {
    param(
        [Parameter(Mandatory)][string] $OuterId,
        [Parameter(Mandatory)][string] $Term,
        [Parameter(Mandatory)][string] $Step
    )

    $nodes = Read-TaxonomyNodes
    $outer = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $OuterId `
        -FailureCode 'taxonomy.draft_input_box'
    if ($null -eq $outer) { throw 'taxonomy.draft_input_box' }
    $inner = Get-UiAcceptanceDescendantSelector -Node $outer -NativeType 'Edit' -ClassName 'TextBox' `
        -FailureCode 'taxonomy.draft_input_inner'
    $result = Set-UiAcceptanceTaxonomyTerm -StepId ('taxonomy.' + $Step) `
        -InnerSelector $inner -Term $Term -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -ForegroundCheck $ForegroundCheck `
        -DesktopCheck $DesktopCheck -TimeoutMs $MutationTimeoutMs -FailureCode 'taxonomy.draft_input'
    if ($result.Status -cne 'Passed') {
        throw (New-TaxonomyFailureCode -Result $result -Step $Step -Code 'taxonomy.draft_input')
    }
}

<#
.SYNOPSIS
    Waits, by repeated observation, until a term row shows a stated reference count.

.DESCRIPTION
    The list is reloaded in response to a kind, search or filter change, so it is read until exactly one
    visible row renders the term and that row also renders the reference count the call expects. The
    count is the product's own count of the term's references, which is why it is worth asserting on
    its own: it is what makes a term that nothing references visible as such. Nothing matching, more
    than one matching, and a row that shows a different count are all refused, and the rows that were
    actually seen are named in the failure.
#>
function Wait-TaxonomyTermRow {
    param(
        [Parameter(Mandatory)][string] $ContainerId,
        [Parameter(Mandatory)][string] $Term,
        [Parameter(Mandatory)][string] $Units,
        [Parameter(Mandatory)][string] $Code
    )

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $seen = ''
    while ($deadline.Elapsed.TotalSeconds -lt $TaxonomyWaitSeconds) {
        $rows = Get-TaxonomyRows -ContainerId $ContainerId -Code $Code
        $seen = (@($rows | ForEach-Object { @($_.Texts) -join '|' }) | Sort-Object) -join ' / '
        $matching = @($rows | Where-Object { @($_.Texts) -ccontains $Term })
        if ($matching.Count -eq 1 -and @($matching[0].Texts) -ccontains $Units) { return $matching[0] }
    }
    $slug = ConvertTo-UiAcceptanceSlug -Value $seen
    if ($slug.Length -gt 110) { $slug = $slug.Substring(0, 110) }
    throw ($Code + '.u' + $Units + '.' + $slug)
}

<#
.SYNOPSIS
    Waits, by repeated observation, until a container shows exactly one row rendering a stated text.

.DESCRIPTION
    Used for the confirmation the product builds for one delete operation, whose rows carry the term
    as their own content. Exactly one row is required, both when it first appears and after every
    re-read, so a dialog that listed a term it should not, or listed it twice, fails here.
#>
function Wait-TaxonomySingleRow {
    param(
        [Parameter(Mandatory)][string] $ContainerId,
        [Parameter(Mandatory)][string] $Text,
        [Parameter(Mandatory)][string] $Code
    )

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $seen = ''
    while ($deadline.Elapsed.TotalSeconds -lt $TaxonomyWaitSeconds) {
        $rows = Get-TaxonomyRows -ContainerId $ContainerId -Code $Code
        $seen = (@($rows | ForEach-Object {
                    if (@($_.Texts).Count -gt 0) { @($_.Texts) -join '|' } else { $_.Name }
                }) | Sort-Object) -join ' / '
        if (@($rows).Count -eq 1) {
            $single = @($rows)[0]
            $rendered = @(@($single.Texts) + @($single.Name))
            if ($rendered -ccontains $Text) { return $single }
        }
    }
    $slug = ConvertTo-UiAcceptanceSlug -Value $seen
    if ($slug.Length -gt 110) { $slug = $slug.Substring(0, 110) }
    throw ($Code + '.n' + [string] @($seen -split ' / ').Count + '.' + $slug)
}

<#
.SYNOPSIS
    Releases one term from the inspector's picker through the chip the product renders for it.

.DESCRIPTION
    Measured product behaviour: the picker's selected-terms container holds, for each selected term and
    in render order, a Text element that renders the term followed by the removal button for it. A term
    is therefore identified by the Text that precedes a removal button, and that button is what is
    pressed. Nothing matching and more than one matching are refused, so a chip belonging to a
    different term can never be pressed, and a container whose shape is not the measured one is
    refused rather than walked on.
#>
function Remove-TaxonomyInspectorTerm {
    param(
        [Parameter(Mandatory)][string] $TermsId,
        [Parameter(Mandatory)][string] $Term,
        [Parameter(Mandatory)][string] $Code
    )

    $nodes = Read-TaxonomyNodes
    $container = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $TermsId -FailureCode $Code
    if ($null -eq $container) { throw $Code }
    $children = @(Get-UiAcceptanceNodeChildren -Node $container) | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'isOffscreen') -ceq 'False' }
    $matches = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt @($children).Count; $index++) {
        $termNode = @($children)[$index]
        if ((Get-UiAcceptanceNodeProperty -Node $termNode -Name 'type') -cne 'Text') {
            throw ($Code + '.chip_shape')
        }
        if (($index + 1) -ge @($children).Count) { throw ($Code + '.chip_dangling') }
        $buttonNode = @($children)[$index + 1]
        if ((Get-UiAcceptanceNodeProperty -Node $buttonNode -Name 'type') -cne 'Button' -or
            (Get-UiAcceptanceNodeProperty -Node $buttonNode -Name 'name') -cne $removeTermName) {
            throw ($Code + '.chip_button_missing')
        }
        if ((Get-UiAcceptanceNodeProperty -Node $termNode -Name 'name') -ceq $Term) { $matches.Add($buttonNode) }
        $index++
    }
    if ($matches.Count -ne 1) { throw ($Code + '.chips' + [string] $matches.Count) }
    $selector = Get-UiAcceptanceNodeSelector -Node $matches[0] -FailureCode $Code
    $null = Invoke-TaxonomyAction -Step ($Code + '-remove') -Arguments @('ui','invoke',$selector) -Code $Code
}

<#
.SYNOPSIS
    Finds the one visible button with a stated id whose accessible name is stated.

.DESCRIPTION
    A dialog's own buttons carry generated ids; the name is what says which action they perform. Both
    the id and the name are part of the match, so a same-named button elsewhere in the window, or the
    page's own action of a similar name, can never be taken for the dialog's.
#>
function Get-TaxonomyButton {
    param(
        [Parameter(Mandatory)][string] $AutomationId,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Code
    )

    $nodes = Read-TaxonomyNodes
    $matches = @($nodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'Button' -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq $AutomationId -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq $Name -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'isOffscreen') -ceq 'False' })
    if ($matches.Count -ne 1) { throw ($Code + '.n' + [string] $matches.Count) }
    return (Get-UiAcceptanceNodeSelector -Node $matches[0] -FailureCode $Code)
}

<#
.SYNOPSIS
    Reads the library's formal files with their hashes.
#>
function Read-TaxonomyFormalFiles {
    param([Parameter(Mandatory)][string] $LibraryDirectory)

    $files = @()
    foreach ($path in @([IO.Directory]::EnumerateFiles($LibraryDirectory, '*', [IO.SearchOption]::AllDirectories))) {
        $files += [pscustomobject] @{
            Name = [IO.Path]::GetFileName($path)
            Hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }
    return ,@($files)
}

function Open-TaxonomyPage {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][string] $Code)

    $null = Invoke-TaxonomyAction -Step $Step -Arguments @('ui','invoke','NavTaxonomy') -Code $Code
    if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','TaxonomyTermList') -Seconds $TaxonomyWaitSeconds)) {
        throw $Code
    }
}

<#
.SYNOPSIS
    Makes the taxonomy inspector available and waits for its actions.

.DESCRIPTION
    The inspection pane is part of the page on a wide window and a drawer on a narrow one. The product
    itself says which it is: the button that opens the drawer is only on screen in the narrow layout,
    so it is pressed exactly when it is there, and the pane's own actions being on screen is what the
    wait is for. Nothing about the window size is assumed.
#>
function Open-TaxonomyInspector {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][string] $Code)

    $openers = @(Read-TaxonomyNodes | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'TaxonomyInspectorOpenButton' -and
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'isOffscreen') -ceq 'False' })
    if ($openers.Count -gt 1) { throw ($Code + '.openers' + [string] $openers.Count) }
    if ($openers.Count -eq 1) {
        $selector = Get-UiAcceptanceNodeSelector -Node $openers[0] -FailureCode $Code
        $null = Invoke-TaxonomyAction -Step ($Step + '-drawer') -Arguments @('ui','invoke',$selector) -Code $Code
    }
    if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','TaxonomyDeleteButton') -Seconds $TaxonomyWaitSeconds)) {
        throw $Code
    }
}

function Open-LibraryPage {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][string] $Code)

    $null = Invoke-TaxonomyAction -Step $Step -Arguments @('ui','invoke','NavLibrary') -Code $Code
    if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','LibraryGrid') -Seconds $TaxonomyWaitSeconds)) {
        throw $Code
    }
}

<#
.SYNOPSIS
    Finds the one visible grid row of the library that belongs to a stated file.

.DESCRIPTION
    The grid's rows are template content, so a row's identity is the file name its own subtree renders
    together with the row group's fixed prefix. Both are matched at once, and the match must be unique
    among the rows that are on screen, so a row for a different file - or the same file listed twice -
    can never be taken for the one that is being edited.
#>
function Find-LibraryRow {
    param(
        [Parameter(Mandatory)][string] $NamePrefix,
        [Parameter(Mandatory)][string] $FileName,
        [Parameter(Mandatory)][string] $Code
    )

    return (Find-UiAcceptanceUniqueNode -Nodes (Read-TaxonomyNodes) -NativeType 'ListItem' `
        -NamePrefix $NamePrefix -NameContains $FileName -VisibleOnly -FailureCode $Code)
}

<#
.SYNOPSIS
    Waits until the library grid renders a row for a stated file.

.DESCRIPTION
    The grid reloads when its rows are ingested or filtered, so the row is waited for by repeated
    observation rather than assumed to be there after a fixed delay.
#>
function Wait-LibraryRow {
    param(
        [Parameter(Mandatory)][string] $NamePrefix,
        [Parameter(Mandatory)][string] $FileName,
        [Parameter(Mandatory)][string] $Code
    )

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt $TaxonomyWaitSeconds) {
        $row = Find-LibraryRow -NamePrefix $NamePrefix -FileName $FileName -Code $Code
        if ($null -ne $row) { return $row }
    }
    throw ($Code + '.missing')
}

<#
.SYNOPSIS
    Reads the author terms the library inspector currently renders for the selected file.

.DESCRIPTION
    The references are read from the chips the product draws for the selected file itself, so the
    claim "this file holds exactly these authors" is about what the product shows for that row rather
    than about a database this scenario may not read.

    Measured product behaviour: inside the picker's selected-terms container the product draws, for
    each selected term and in render order, a Text element that renders the term followed by the
    removal button for that same term. The chip is that pair, so the term is read from the Text that
    precedes the button; the button's own subtree is only its glyph. A pair whose shape is not the
    measured one is refused rather than skipped, because skipping it would silently shorten the set.

    The container itself is allowed to be reported off screen: the product collapses it while the file
    holds no term, and an empty picker is exactly the answer then, so only the chips must be on screen.
#>
function Get-LibraryInspectorTerms {
    param([Parameter(Mandatory)][string] $TermsId, [Parameter(Mandatory)][string] $Code)

    $container = Find-UiAcceptanceUniqueNode -Nodes (Read-TaxonomyNodes) -AutomationId $TermsId `
        -FailureCode $Code
    if ($null -eq $container) { throw $Code }
    # The children are read in the order they are rendered, because a chip is a term followed by the
    # button that removes it.
    $children = @(Get-UiAcceptanceNodeChildren -Node $container) | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'isOffscreen') -ceq 'False' }
    $terms = @()
    for ($index = 0; $index -lt @($children).Count; $index++) {
        $termNode = @($children)[$index]
        if ((Get-UiAcceptanceNodeProperty -Node $termNode -Name 'type') -cne 'Text') {
            throw ($Code + '.chip_shape')
        }
        if (($index + 1) -ge @($children).Count) { throw ($Code + '.chip_dangling') }
        $buttonNode = @($children)[$index + 1]
        if ((Get-UiAcceptanceNodeProperty -Node $buttonNode -Name 'type') -cne 'Button' -or
            (Get-UiAcceptanceNodeProperty -Node $buttonNode -Name 'name') -cne $removeTermName) {
            throw ($Code + '.chip_button_missing')
        }
        $terms += [string] (Get-UiAcceptanceNodeProperty -Node $termNode -Name 'name')
        $index++
    }
    return ,@($terms)
}

<#
.SYNOPSIS
    Waits until the library inspector renders exactly the stated author terms for a file.

.DESCRIPTION
    Reading the chips is an observation, and the picker re-renders as a draft commits, so the wait
    re-reads until the rendered set matches. The rendered set is compared as a set with duplicates
    kept, so a file that held the same term twice would not match the expected single occurrence. The
    expectation is stated as a plain list, so "no terms" is written as an empty list on both runtimes.
#>
function Wait-LibraryInspectorTerms {
    param(
        [Parameter(Mandatory)][string] $TermsId,
        [Parameter(Mandatory)][AllowEmptyCollection()][AllowNull()] $Expected,
        [Parameter(Mandatory)][string] $Code
    )

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    # The reader hands its terms back as one array, so it is taken by direct assignment. Enclosing it
    # in an array sub-expression again would put the whole list inside a single element, and every set
    # comparison would then be made against one nested array and could never match.
    $seen = @()
    while ($deadline.Elapsed.TotalSeconds -lt $TaxonomyWaitSeconds) {
        $seen = Get-LibraryInspectorTerms -TermsId $TermsId -Code $Code
        if (Test-UiAcceptanceRowSetMatch -Observed $seen -Expected $Expected) { return $seen }
    }
    throw ($Code + '.n' + [string] @($seen).Count + '.' +
        (ConvertTo-UiAcceptanceSlug -Value ((@($seen) | Sort-Object) -join ' / ')))
}

function Select-LibraryRow {
    param(
        [Parameter(Mandatory)][string] $Step,
        [Parameter(Mandatory)][string] $NamePrefix,
        [Parameter(Mandatory)][string] $NameContains,
        [Parameter(Mandatory)][string] $Code
    )

    $result = Invoke-UiAcceptanceResolvedMutation -StepId ('taxonomy.' + $Step) `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot `
        -Verb 'invoke' -TimeoutMs $MutationTimeoutMs -FailureCode $Code `
        -ResolveSelector {
            $nodes = Read-TaxonomyNodes
            $found = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
                -NamePrefix $NamePrefix -NameContains $NameContains -FailureCode $Code
            if ($null -eq $found) {
                $listItems = @($nodes | Where-Object {
                    (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' }).Count
                $contains = @($nodes | Where-Object {
                    (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').Contains($NameContains) }).Count
                throw ($Code + '.items' + [string] $listItems + '.contains' + [string] $contains)
            }
            return (Get-UiAcceptanceNodeSelector -Node $found -FailureCode $Code)
        }
    if ($result.Status -cne 'Passed') {
        throw (New-TaxonomyFailureCode -Result $result -Step $Step -Code $Code)
    }
}

try {
    # --- inputs and contract ------------------------------------------------------------------
    foreach ($requiredInput in @($ExecutablePath, $ContractPath, $OwnedRoot, $OutputDirectory, $SupportPath, $WinAppPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredInput)) { throw 'taxonomy.invalid_input' }
    }
    if ([IO.Path]::GetFileName($ExecutablePath) -ine 'TraceFold.Media.exe') { throw 'taxonomy.invalid_product' }
    if (-not [IO.File]::Exists($ContractPath)) { throw 'taxonomy.contract_missing' }
    if (-not [IO.File]::Exists($SupportPath) -or -not [IO.File]::Exists($WinAppPath)) { throw 'taxonomy.tooling_missing' }

    $contract = ConvertFrom-UiAcceptanceJson -Text ([IO.File]::ReadAllText($ContractPath)) -Source 'uia contract'
    Assert-UiAcceptanceFields -InputObject $contract -Required @('entries','dynamicRows','fixtureAssets','sessionPreparation','runtimeControls')

    $entryIndex = @{}
    foreach ($entry in @($contract.entries)) {
        Assert-UiAcceptanceFields -InputObject $entry -Required @('automationId','name','requiredPatterns')
        $entryIndex[[string] $entry.automationId] = $entry
    }
    foreach ($requiredId in @('NavTaxonomy','TaxonomyPageRoot','TaxonomyUndoBanner','TaxonomyUndoButton',
            'TaxonomyKindList','TaxonomySearchBox','TaxonomyRefreshButton','TaxonomyCreateButton',
            'TaxonomyTermList','TaxonomyInspectorPane','TaxonomySelectedCount','TaxonomyDeleteBlockers',
            'TaxonomyMergeButton','TaxonomyDeleteButton','TaxonomyMergeDialog','TaxonomyMergeSources',
            'TaxonomyMergeTargetSearch','TaxonomyMergeTargetResults','TaxonomyMergeAffectedSummary',
            'TaxonomyMergeAffectedFiles','TaxonomyMergeApplyButton','NavLibrary','LibraryGrid',
            'LibraryAuthorInput','LibraryAuthorInputTerms','IngestButton')) {
        if (-not $entryIndex.ContainsKey($requiredId)) { throw ('taxonomy.contract_missing_entry.' + $requiredId) }
    }
    # The delete confirmation is built for that one operation at run time, so the contract declares it
    # among the run-time controls rather than among the page's entries.
    $runtimeIds = @($contract.runtimeControls | ForEach-Object { [string] $_.automationId })
    foreach ($requiredRuntimeId in @('TaxonomyDeleteConfirmDialog','TaxonomyDeleteTermList')) {
        if ($runtimeIds -cnotcontains $requiredRuntimeId) {
            throw ('taxonomy.contract_missing_runtime.' + $requiredRuntimeId)
        }
    }

    $preparation = $contract.sessionPreparation
    Assert-UiAcceptanceFields -InputObject $preparation -Required @(
        'seedFilenames','savedStatusText','savedStatusAutomationId','settingsNavigationAutomationId',
        'inboxNavigationAutomationId','saveFolderSettingsAutomationId','scanTriggerAutomationId','folderInputs')

    $inboxRows = @($contract.dynamicRows | Where-Object { [string] $_.phase -ceq 'inbox-seeded' })
    if ($inboxRows.Count -ne 1) { throw 'taxonomy.contract_empty' }
    $inboxRow = $inboxRows[0]
    Assert-UiAcceptanceFields -InputObject $inboxRow -Required @('fixedAccessibleName','fixedFilename','nativeControlType')
    $libraryRows = @($contract.dynamicRows | Where-Object { [string] $_.phase -ceq 'post-ingest' })
    if ($libraryRows.Count -ne 1) { throw 'taxonomy.contract_empty' }
    $libraryRowDeclaredName = [string] $libraryRows[0].fixedAccessibleName
    $libraryRowDeclaredFilename = [string] $libraryRows[0].fixedFilename
    if (-not $libraryRowDeclaredName.Contains($libraryRowDeclaredFilename)) {
        throw 'taxonomy.contract_invalid'
    }
    $libraryRowGroupPrefix = $libraryRowDeclaredName.Substring(
        0, $libraryRowDeclaredName.IndexOf($libraryRowDeclaredFilename, [StringComparison]::Ordinal))

    $assets = @($contract.fixtureAssets)
    $verified = @{}
    foreach ($name in $fixtures) {
        $matches = @($assets | Where-Object { [string] $_.fixedFilename -ceq $name })
        if ($matches.Count -ne 1) { throw 'taxonomy.fixture_missing' }
        $verified[$name] = Test-UiAcceptanceFixtureAsset -Asset $matches[0] -RepositoryRoot $repositoryRoot
    }

    $folderInputs = @($preparation.folderInputs)
    if ($folderInputs.Count -eq 0) { throw 'taxonomy.contract_empty' }

    [void][IO.Directory]::CreateDirectory($OutputDirectory)

    Reset-UiAcceptanceMutationSteps
    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId

    $waited = Invoke-WinAppObservation -Arguments @('ui','wait-for','TraceFoldMainWindow') `
        -ProcessId $productProcessId -TimeoutSeconds $WaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($waited.Status -cne 'Passed' -or $null -eq $waited.Data -or $waited.Data.found -ne $true) {
        throw 'taxonomy.window_unavailable'
    }

    # --- owned preparation: three directories through the product's own Settings UI --------------
    $folders = @{}
    foreach ($folderInput in $folderInputs) {
        Assert-UiAcceptanceFields -InputObject $folderInput -Required @('key','automationId','relativeDirectory')
        $relative = [string] $folderInput.relativeDirectory
        if ($relative -cnotmatch '^[a-z0-9][a-z0-9-]*$') { throw 'taxonomy.fixture_target' }
        $directory = [IO.Path]::GetFullPath((Join-Path $OwnedRoot $relative))
        $rootBoundary = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $directory.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'taxonomy.fixture_target' }
        [void][IO.Directory]::CreateDirectory($directory)
        $folders[[string] $folderInput.key] = $directory
    }
    foreach ($folderInput in $folderInputs) {
        $path = $folders[[string] $folderInput.key]
        $null = Invoke-TaxonomyAction -Step ('set-' + [string] $folderInput.key + '-folder') `
            -Arguments @('ui','set-value',[string] $folderInput.automationId,$path) -Code 'taxonomy.folder_settings'
        $readBack = Read-TaxonomyValue -AutomationId ([string] $folderInput.automationId) -Code 'taxonomy.folder_settings'
        if ($readBack -cne $path) { throw 'taxonomy.folder_settings' }
    }
    $null = Invoke-TaxonomyAction -Step 'save-folder-settings' `
        -Arguments @('ui','invoke',[string] $preparation.saveFolderSettingsAutomationId) -Code 'taxonomy.folder_settings'
    $status = Read-TaxonomyValue -AutomationId ([string] $preparation.savedStatusAutomationId) -Code 'taxonomy.folder_settings'
    if ($status -cne [string] $preparation.savedStatusText) { throw 'taxonomy.folder_settings' }

    # --- three fixed assets are ingested through the product's own UI ----------------------------
    foreach ($name in $fixtures) {
        [IO.File]::Copy($verified[$name].Source, (Join-Path $folders['inbox'] $name), $false)
    }
    $null = Invoke-TaxonomyAction -Step 'open-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) -Code 'taxonomy.inbox_navigation'
    $null = Invoke-TaxonomyAction -Step 'scan' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) -Code 'taxonomy.scan_trigger'

    $index = 0
    foreach ($name in $fixtures) {
        $index++
        $pendingPrefix = ([string] $inboxRow.fixedAccessibleName).Replace([string] $inboxRow.fixedFilename, $name)
        if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for',$pendingPrefix) -Seconds $RowWaitTimeoutSeconds)) {
            throw 'taxonomy.row_missing'
        }
        Select-LibraryRow -Step ('select-ingest-' + $index) -NamePrefix $pendingPrefix -NameContains $name `
            -Code 'taxonomy.row_selection'
        if (-not (Test-TaxonomyEnabled -AutomationId 'IngestButton' -Seconds $TaxonomyWaitSeconds)) {
            throw 'taxonomy.ingest_not_ready'
        }
        $null = Invoke-TaxonomyAction -Step ('ingest-' + $index) -Arguments @('ui','invoke','IngestButton') -Code 'taxonomy.ingest'
        if (-not (Wait-TaxonomyGone -Arguments @('ui','wait-for',$pendingPrefix,'--gone') -Seconds $RowWaitTimeoutSeconds)) {
            throw 'taxonomy.ingest_incomplete'
        }
    }

    $formalBefore = Read-TaxonomyFormalFiles -LibraryDirectory $folders['library']
    if ($formalBefore.Count -ne 3) { throw 'taxonomy.ingest_result' }
    $hashesBefore = @{}
    foreach ($file in $formalBefore) { $hashesBefore[$file.Name] = $file.Hash }

    # --- references are established through the ordinary inspector ------------------------------
    Open-LibraryPage -Step 'open-library' -Code 'taxonomy.library_navigation'
    $beforeReferences = Read-TaxonomyState -EvidenceId 'taxonomy-before'
    # Each file is selected, its planned terms are committed one by one, and the picker is read back
    # before the next file is opened. The commit is proven by the chips the product renders for that
    # file afterwards, because a term the product refused to attach would leave the chips short and
    # the next step, and the final snapshot, would disagree with the plan.
    $referenceReadBack = [ordered] @{}
    $planIndex = 0
    foreach ($name in $fixtures) {
        $planIndex++
        $null = Wait-LibraryRow -NamePrefix $libraryRowGroupPrefix -FileName $name `
            -Code 'taxonomy.row_selection'
        Select-LibraryRow -Step ('reference-select-' + $planIndex) `
            -NamePrefix $libraryRowGroupPrefix -NameContains $name -Code 'taxonomy.row_selection'
        if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','LibraryAuthorInput') -Seconds $TaxonomyWaitSeconds)) {
            throw 'taxonomy.inspector_missing'
        }
        # A fresh file starts from no draft term, which is what a commit to that file is proven against.
        $starting = Wait-LibraryInspectorTerms -TermsId 'LibraryAuthorInputTerms' -Expected @() `
            -Code 'taxonomy.reference_draft'
        # The draft is read back, and an empty result is a legitimate answer for a file with no terms.
        if ($null -ne $starting -and @($starting).Count -ne 0) { throw 'taxonomy.reference_draft.not_empty' }
        $termIndex = 0
        foreach ($term in @($referencePlan[$name])) {
            $termIndex++
            Set-TaxonomyInspectorTerm -OuterId 'LibraryAuthorInput' -Term $term `
                -Step ('reference-' + $planIndex + '-author-' + $termIndex)
        }
        $committed = Wait-LibraryInspectorTerms -TermsId 'LibraryAuthorInputTerms' `
            -Expected $referencePlan[$name] -Code 'taxonomy.reference_commit'
        $referenceReadBack[$name] = @($committed)
    }
    # Selecting another row flushes the last file's draft, which is how the product persists it.
    Select-LibraryRow -Step 'reference-flush' -NamePrefix $libraryRowGroupPrefix `
        -NameContains ([string] $fixtures[0]) -Code 'taxonomy.row_selection'

    $established = Read-TaxonomyState -EvidenceId 'taxonomy-references'
    foreach ($name in $fixtures) {
        $facts = Get-TaxonomyAssetFacts -State $established -Filename $name -Code 'taxonomy.asset_missing'
        if ((@($facts.Authors) -join '|') -cne (@(@($referencePlan[$name]) | Sort-Object) -join '|')) {
            throw 'taxonomy.reference_result'
        }
    }
    $sourceOwners = Get-TaxonomyTermOwners -State $established -Term $mergeSourceName
    $targetOwners = Get-TaxonomyTermOwners -State $established -Term $mergeTargetName
    $deleteOwners = Get-TaxonomyTermOwners -State $established -Term $deleteTermName
    if ((@($sourceOwners) -join '|') -cne (@($fixtures[0], $fixtures[1]) -join '|')) { throw 'taxonomy.reference_result' }
    if ((@($targetOwners) -join '|') -cne ([string] $fixtures[1])) { throw 'taxonomy.reference_result' }
    if ((@($deleteOwners) -join '|') -cne ([string] $fixtures[2])) { throw 'taxonomy.reference_result' }
    foreach ($term in @($mergeSourceName, $mergeTargetName, $deleteTermName)) {
        if (@($established.taxonomy.author) -cnotcontains $term) { throw 'taxonomy.term_missing' }
    }
    $null = Save-TaxonomyStageState -Stage 'references' -Facts ([ordered] @{
            source = $mergeSourceName
            sourceOwners = @($sourceOwners)
            target = $mergeTargetName
            targetOwners = @($targetOwners)
            deleteTerm = $deleteTermName
            deleteOwners = @($deleteOwners)
            authors = $established.taxonomy.author
            renderedReferences = $referenceReadBack
        })

    # --- the source is found on the taxonomy page through its own kind, search and selection -----
    Open-TaxonomyPage -Step 'open-taxonomy' -Code 'taxonomy.navigation'
    # The kind control is exercised for real, and it is also the rejection this stage needs: an author
    # term must not be reachable while the page lists series.
    # The kind control is a list whose items are always present, so the kind is chosen by its own
    # rendered text rather than by expanding a drop-down.
    Invoke-TaxonomyRow -Step 'kind-series' -ContainerId 'TaxonomyKindList' -Text $seriesKindName `
        -Code 'taxonomy.kind'
    Set-TaxonomySearch -OuterId 'TaxonomySearchBox' -Query $mergeSourceName -Step 'kind-series-search'
    $rows = Wait-TaxonomyRowSet -ContainerId 'TaxonomyTermList' -Expected @() -Code 'taxonomy.kind'
    $null = Save-TaxonomyStageState -Stage 'kind-series' -Facts ([ordered] @{
            kind = $seriesKindName
            rows = @($rows | ForEach-Object { @($_.Texts) })
            sourceVisible = $false
        })
    Invoke-TaxonomyRow -Step 'kind-author' -ContainerId 'TaxonomyKindList' -Text $authorKindName `
        -Code 'taxonomy.kind'
    Clear-TaxonomySearch -OuterId 'TaxonomySearchBox' -Step 'kind-author-clear'
    Set-TaxonomySearch -OuterId 'TaxonomySearchBox' -Query $mergeSourceName -Step 'source-search'
    $sourceRow = Wait-TaxonomyTermRow -ContainerId 'TaxonomyTermList' -Term $mergeSourceName `
        -Units '2' -Code 'taxonomy.search_rows'
    $null = Save-TaxonomyStageState -Stage 'source-found' -Facts ([ordered] @{
            kind = $authorKindName
            rowTexts = @($sourceRow.Texts)
            expectedUnits = '2'
        })

    Invoke-TaxonomyRow -Step 'source-select' -ContainerId 'TaxonomyTermList' -Text $mergeSourceName `
        -Code 'taxonomy.term_selection'
    Open-TaxonomyInspector -Step 'source-inspector' -Code 'taxonomy.term_selection'
    # The selection is proven by the product making the merge action available and by what the merge
    # dialog then lists as its source, not by a count read from a control that the narrow layout keeps
    # in a drawer.
    if (-not (Test-TaxonomyEnabled -AutomationId 'TaxonomyMergeButton' -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.merge_open'
    }

    # --- the merge preview is read before anything is confirmed ---------------------------------
    $null = Invoke-TaxonomyAction -Step 'merge-open' -Arguments @('ui','invoke','TaxonomyMergeButton') `
        -Code 'taxonomy.merge_open'
    if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','TaxonomyMergeDialog') -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.merge_open'
    }
    # The dialog opening at all is the first proof that the page carried a source: the dialog refuses
    # to prepare when its source set is empty, so an open dialog means at least one source term was
    # taken from the page's own selection. What that source *is*, and which references it holds, is
    # then established by the affected-file preview below, because the preview is built from the very
    # source term ids the dialog captured. The pane itself is read as well, so its state is part of
    # the evidence rather than an assumption.
    $sourcePane = Read-TaxonomyNode -AutomationId 'TaxonomyMergeSources' -Code 'taxonomy.merge_sources'
    if ((Get-UiAcceptanceNodeProperty -Node $sourcePane -Name 'isOffscreen') -cne 'False') {
        throw 'taxonomy.merge_sources.hidden'
    }
    # Measured behaviour: this pane is a display-only list inside an auto-sized row, so the product
    # gives it no height and it realises no rows. Its emptiness is recorded, never asserted as a set,
    # and the source term is carried instead by the affected-file preview and by the snapshot.
    $sourcePaneRows = @(Get-TaxonomyRows -ContainerId 'TaxonomyMergeSources' -Code 'taxonomy.merge_sources')
    $sourcePreview = @()
    foreach ($row in $sourcePaneRows) { $sourcePreview += ,@($row.Texts) }

    # The target is chosen by its own name among the same-kind candidates, and the choice is confirmed
    # by the product rendering the preview for it. A target row renders its display name and its
    # reference count, which is read per row rather than counted.
    Set-TaxonomySearch -OuterId 'TaxonomyMergeTargetSearch' -Query $mergeTargetName -Step 'merge-target-search'
    $targetRows = Wait-TaxonomyRowSet -ContainerId 'TaxonomyMergeTargetResults' `
        -Expected @(@($mergeTargetName, [string] @($targetOwners).Count) -join '|') -Code 'taxonomy.merge_target'
    # The narrowed list must not offer the source itself as a target, so a target that also happens to
    # be a source is refused here rather than trusted.
    foreach ($candidate in $targetRows) {
        if ((@($candidate.Texts) -ccontains $mergeSourceName)) { throw 'taxonomy.merge_target.is_source' }
    }
    Invoke-TaxonomyRow -Step 'merge-target-select' -ContainerId 'TaxonomyMergeTargetResults' `
        -Text $mergeTargetName -Code 'taxonomy.merge_target'

    # The affected files are the ones whose assignment changes, and each one's before and after are
    # read from its own preview row, never from a total.
    $expectedAffected = @($fixtures[0], $fixtures[1])
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $affected = @()
    $affectedNames = @()
    while ($deadline.Elapsed.TotalSeconds -lt $TaxonomyWaitSeconds) {
        $affected = Get-TaxonomyRows -ContainerId 'TaxonomyMergeAffectedFiles' -Code 'taxonomy.merge_preview'
        $affectedNames = @($affected | ForEach-Object { @($_.Texts)[1] })
        if (Test-UiAcceptanceRowSetMatch -Observed $affectedNames -Expected $expectedAffected) { break }
    }
    if (-not (Test-UiAcceptanceRowSetMatch -Observed $affectedNames -Expected $expectedAffected)) {
        throw ('taxonomy.merge_preview.n' + [string] @($affectedNames).Count + '.' +
            (ConvertTo-UiAcceptanceSlug -Value ((@($affectedNames) | Sort-Object) -join ' / ')))
    }
    $affectedFacts = @()
    $previewBefore = [ordered] @{}
    foreach ($row in $affected) {
        $texts = @($row.Texts)
        $beforeIndex = [Array]::IndexOf($texts, $beforeLabel)
        $afterIndex = [Array]::IndexOf($texts, $afterLabel)
        if ($beforeIndex -lt 0 -or $afterIndex -lt 0 -or $afterIndex -ge ($texts.Count - 1)) {
            throw 'taxonomy.merge_preview.shape'
        }
        $before = [string] $texts[$beforeIndex + 1]
        $after = [string] $texts[$afterIndex + 1]
        if ([string] $texts[0] -cne $libraryLocationLabel) { throw 'taxonomy.merge_preview.location' }
        if (-not $before.Contains($mergeSourceName)) { throw 'taxonomy.merge_preview.before' }
        if ($after.Contains($mergeSourceName)) { throw 'taxonomy.merge_preview.after_source' }
        if (-not $after.Contains($mergeTargetName)) { throw 'taxonomy.merge_preview.after_target' }
        # A file that already held the target must not end up holding it twice.
        if ($after.Contains($mergeTargetName + $termSeparator) -or
            $after.Contains($termSeparator + $mergeTargetName)) { throw 'taxonomy.merge_duplicate' }
        # The preview's own "before" has to agree with the reference this file was left holding, so a
        # preview that described a different starting state than the one that was established would be
        # refused rather than accepted as a plausible-looking pair.
        $fileName = [string] $texts[1]
        foreach ($term in @($referencePlan[$fileName])) {
            if (-not $before.Contains($term)) {
                throw ('taxonomy.merge_preview.stale.' + (ConvertTo-UiAcceptanceSlug -Value $fileName))
            }
        }
        $previewBefore[$fileName] = @($before -split $termSeparator)
        $affectedFacts += [ordered] @{
            file = $fileName
            location = [string] $texts[0]
            before = $before
            after = $after
        }
    }
    # Every file the plan says holds the source must appear in this preview, because a preview that
    # omitted one would make the migration look narrower than it is.
    $plannedHolders = @($fixtures | Where-Object { @($referencePlan[$_]) -ccontains $mergeSourceName })
    if (-not (Test-UiAcceptanceRowSetMatch -Observed $affectedNames -Expected $plannedHolders)) {
        throw 'taxonomy.merge_preview.coverage'
    }

    # The preview's own per-file rows are the source of the claim that exactly those files are touched,
    # so the counts the dialog shows are read as well and have to agree with them: two affected files in
    # the formal library, none pending.
    $summary = @(Read-TaxonomyNodes | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'TaxonomyMergeAffectedSummary' })
    if ($summary.Count -ne 1) { throw ('taxonomy.merge_summary.n' + [string] $summary.Count) }
    $summaryFacts = @()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $summary[0])) { $pending.Push($child) }
    while ($pending.Count -gt 0) {
        $node = $pending.Pop()
        if ((Get-UiAcceptanceNodeProperty -Node $node -Name 'type') -ceq 'Text') {
            $summaryFacts += (Get-UiAcceptanceNodeProperty -Node $node -Name 'name')
        }
        foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $node)) { $pending.Push($child) }
    }
    if (-not (Test-UiAcceptanceRowSetMatch -Observed $summaryFacts `
                -Expected @($inboxLocationLabel, '0', $libraryLocationLabel, '2', $totalLabel, '2'))) {
        throw ('taxonomy.merge_summary.' + (ConvertTo-UiAcceptanceSlug -Value ((@($summaryFacts) | Sort-Object) -join '|')))
    }
    $mergeTree = Save-TaxonomyTree -EvidenceId 'taxonomy-merge-preview'
    $artifacts.Add($mergeTree)
    $null = Save-TaxonomyScreenshot -Selector 'TaxonomyMergeDialog' -Name 'taxonomy-merge-preview'
    $null = Save-TaxonomyStageState -Stage 'merge-preview' -Facts ([ordered] @{
            selection = $mergeSourceName
            selectionOwners = @($sourceOwners)
            # Measured: the display-only source pane realises no rows, so its rendered content is
            # recorded as observed and the source identity is carried by the affected-file preview
            # and by the snapshot instead of by an assertion this pane cannot satisfy.
            sourcePaneRenderedRows = @($sourcePreview)
            sourcePaneRealisesRows = (@($sourcePreview).Count -gt 0)
            targets = @($targetRows | ForEach-Object { @($_.Texts) })
            target = $mergeTargetName
            affected = $affectedFacts
            previewBefore = $previewBefore
            summaryTexts = @($summaryFacts)
        })

    # --- one merge confirmation -----------------------------------------------------------------
    $null = Invoke-TaxonomyAction -Step 'merge-confirm' -Arguments @('ui','invoke','TaxonomyMergeApplyButton') `
        -Code 'taxonomy.merge_apply'
    if (-not (Wait-TaxonomyGone -Arguments @('ui','wait-for','TaxonomyMergeDialog','--gone') -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.merge_completion'
    }
    $merged = Read-TaxonomyState -EvidenceId 'taxonomy-merged'
    if (@($merged.taxonomy.author) -ccontains $mergeSourceName) { throw 'taxonomy.merge_source_gone' }
    if (@($merged.taxonomy.author) -cnotcontains $mergeTargetName) { throw 'taxonomy.merge_target_kept' }
    $mergedOwners = Get-TaxonomyTermOwners -State $merged -Term $mergeTargetName
    if ((@($mergedOwners) -join '|') -cne (@($fixtures[0], $fixtures[1]) -join '|')) { throw 'taxonomy.merge_references' }
    foreach ($name in $fixtures) {
        $facts = Get-TaxonomyAssetFacts -State $merged -Filename $name -Code 'taxonomy.asset_missing'
        $expected = @($expectedAfterMerge[$name])
        if ((@($facts.Authors) -join '|') -cne (@($expected | Sort-Object) -join '|')) {
            throw ('taxonomy.merge_references.' + (ConvertTo-UiAcceptanceSlug -Value $name))
        }
        $duplicates = @($facts.Authors | Group-Object | Where-Object { $_.Count -gt 1 })
        if ($duplicates.Count -gt 0) { throw ('taxonomy.merge_duplicate.' + (ConvertTo-UiAcceptanceSlug -Value $name)) }
    }
    if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','TaxonomyUndoButton') -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.undo_unavailable'
    }
    $null = Save-TaxonomyStageState -Stage 'merge-applied' -Facts ([ordered] @{
            sourceAbsent = $true
            target = $mergeTargetName
            targetOwners = @($mergedOwners)
            authors = $merged.taxonomy.author
            ownership = Get-TaxonomyAuthorOwnership -State $merged
        })

    # --- one undo restores the pre-merge state exactly ------------------------------------------
    $null = Invoke-TaxonomyAction -Step 'undo-merge' -Arguments @('ui','invoke','TaxonomyUndoButton') `
        -Code 'taxonomy.undo'
    if (-not (Wait-TaxonomyGone -Arguments @('ui','wait-for','TaxonomyUndoButton','--gone') -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.undo_incomplete'
    }
    $restored = Read-TaxonomyState -EvidenceId 'taxonomy-restored'
    foreach ($term in @($mergeSourceName, $mergeTargetName, $deleteTermName)) {
        if (@($restored.taxonomy.author) -cnotcontains $term) { throw 'taxonomy.undo_restore' }
    }
    $restoredSource = Get-TaxonomyTermOwners -State $restored -Term $mergeSourceName
    $restoredTarget = Get-TaxonomyTermOwners -State $restored -Term $mergeTargetName
    if ((@($restoredSource) -join '|') -cne (@($sourceOwners) -join '|')) { throw 'taxonomy.undo_restore.source' }
    if ((@($restoredTarget) -join '|') -cne (@($targetOwners) -join '|')) { throw 'taxonomy.undo_restore.target' }
    $restoredOwnership = Get-TaxonomyAuthorOwnership -State $restored
    foreach ($name in $fixtures) {
        # The fact is taken on its own before its author list is read, for the same reason as in the
        # release stage: a property read off an array-wrapped command is looked for on the array.
        $baseline = Get-TaxonomyAssetFacts -State $established -Filename $name -Code 'taxonomy.asset_missing'
        if ((@($restoredOwnership[$name]) -join '|') -cne (@($baseline.Authors) -join '|')) {
            throw 'taxonomy.undo_restore.ownership'
        }
    }
    $null = Save-TaxonomyStageState -Stage 'merge-undone' -Facts ([ordered] @{
            sourceOwners = @($restoredSource)
            targetOwners = @($restoredTarget)
            ownership = $restoredOwnership
        })

    # --- a referenced term cannot be deleted ----------------------------------------------------
    # The box is typed into rather than set, so whatever an earlier stage left in it would be
    # prepended to this query and the term would never be found. It is emptied first, and the clear
    # is proven by the unrelated term that must reappear in the list once the query is gone, rather
    # than by assuming the box is empty.
    Clear-TaxonomySearch -OuterId 'TaxonomySearchBox' -Step 'guard-search-clear'
    $null = Wait-TaxonomyTermRow -ContainerId 'TaxonomyTermList' -Term $mergeSourceName `
        -Units ([string] @($sourceOwners).Count) -Code 'taxonomy.search_input'
    Set-TaxonomySearch -OuterId 'TaxonomySearchBox' -Query $deleteTermName -Step 'guard-search'
    $null = Wait-TaxonomyTermRow -ContainerId 'TaxonomyTermList' -Term $deleteTermName `
        -Units '1' -Code 'taxonomy.delete_guard'
    Invoke-TaxonomyRow -Step 'guard-select' -ContainerId 'TaxonomyTermList' -Text $deleteTermName `
        -Code 'taxonomy.term_selection'
    Open-TaxonomyInspector -Step 'guard-inspector' -Code 'taxonomy.delete_guard'
    $blockerNodes = Read-TaxonomyNodes
    $blockers = @($blockerNodes | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'TaxonomyDeleteBlockers' })
    if ($blockers.Count -ne 1) { throw ('taxonomy.delete_guard.blockers' + [string] $blockers.Count) }
    if ((Get-UiAcceptanceNodeProperty -Node $blockers[0] -Name 'isOffscreen') -cne 'False') {
        throw 'taxonomy.delete_guard.blockers_hidden'
    }
    $blockedTerms = @()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $blockers[0])) { $pending.Push($child) }
    while ($pending.Count -gt 0) {
        $node = $pending.Pop()
        if ((Get-UiAcceptanceNodeProperty -Node $node -Name 'type') -ceq 'Text') {
            $blockedTerms += (Get-UiAcceptanceNodeProperty -Node $node -Name 'name')
        }
        foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $node)) { $pending.Push($child) }
    }
    if (@($blockedTerms) -cnotcontains $deleteTermName) { throw 'taxonomy.delete_guard.names' }
    # The action itself is refused by the product, which is what the guard is: it is disabled and the
    # term stays exactly as it was, so nothing is dispatched and no protected delete is claimed.
    $deleteButton = Find-UiAcceptanceUniqueNode -Nodes (Read-TaxonomyNodes) -AutomationId 'TaxonomyDeleteButton' `
        -FailureCode 'taxonomy.delete_guard'
    if ($null -eq $deleteButton) { throw 'taxonomy.delete_guard' }
    if ((Get-UiAcceptanceNodeProperty -Node $deleteButton -Name 'isEnabled') -cne 'False') {
        throw 'taxonomy.delete_guard.enabled'
    }
    $guarded = Read-TaxonomyState -EvidenceId 'taxonomy-delete-guard'
    if (@($guarded.taxonomy.author) -cnotcontains $deleteTermName) { throw 'taxonomy.delete_guard.deleted' }
    $guardedOwners = Get-TaxonomyTermOwners -State $guarded -Term $deleteTermName
    if ((@($guardedOwners) -join '|') -cne ([string] $fixtures[2])) { throw 'taxonomy.delete_guard.deleted' }
    $guardTree = Save-TaxonomyTree -EvidenceId 'taxonomy-delete-guard'
    $artifacts.Add($guardTree)
    $null = Save-TaxonomyScreenshot -Selector 'TaxonomyInspectorPane' -Name 'taxonomy-delete-guard'
    $null = Save-TaxonomyStageState -Stage 'delete-guard' -Facts ([ordered] @{
            term = $deleteTermName
            blockersVisible = $true
            blockedTerms = @($blockedTerms)
            deleteEnabled = $false
            owners = @($guardedOwners)
        })

    # --- the reference is released through the same metadata UI, which leaves the term unused -----
    Open-LibraryPage -Step 'release-open-library' -Code 'taxonomy.library_navigation'
    Select-LibraryRow -Step 'release-select-row' -NamePrefix $libraryRowGroupPrefix `
        -NameContains ([string] $fixtures[2]) -Code 'taxonomy.row_selection'
    if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','LibraryAuthorInput') -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.inspector_missing'
    }
    # The file's own chips are read first, so the release is a change to what the product renders for
    # that file and not an action taken against a term that was not there.
    $null = Wait-LibraryInspectorTerms -TermsId 'LibraryAuthorInputTerms' -Expected @($deleteTermName) `
        -Code 'taxonomy.reference_removed'
    Remove-TaxonomyInspectorTerm -TermsId 'LibraryAuthorInputTerms' -Term $deleteTermName -Code 'taxonomy.reference_removed'
    $releasedChips = Wait-LibraryInspectorTerms -TermsId 'LibraryAuthorInputTerms' -Expected @() `
        -Code 'taxonomy.reference_removed'
    Select-LibraryRow -Step 'release-flush' -NamePrefix $libraryRowGroupPrefix `
        -NameContains ([string] $fixtures[0]) -Code 'taxonomy.row_selection'
    $released = Read-TaxonomyState -EvidenceId 'taxonomy-released'
    $releasedOwners = Get-TaxonomyTermOwners -State $released -Term $deleteTermName
    if (@($releasedOwners).Count -ne 0) { throw 'taxonomy.reference_removed' }
    # The fact is taken on its own before its author list is read: reading a property off a command
    # that has been enclosed in an array sub-expression is refused under strict mode, because the
    # result is an array and the property is then looked for on the array rather than on its one
    # member. Naming the fact first expresses the same claim without depending on that behaviour.
    $releasedFacts = Get-TaxonomyAssetFacts -State $released -Filename $fixtures[2] -Code 'taxonomy.asset_missing'
    if (@($releasedFacts.Authors).Count -ne 0) { throw 'taxonomy.reference_removed' }

    # The product's own unused filter, and the term's own reference count, are what prove it is unused.
    Open-TaxonomyPage -Step 'unused-open-taxonomy' -Code 'taxonomy.navigation'
    Select-TaxonomyOption -ComboBoxId 'TaxonomyReferenceFilter' -OptionName $unusedFilterName `
        -Step 'unused-filter' -Code 'taxonomy.term_unreferenced'
    # Leaving the page and returning does not empty the search box, so the previous query is removed
    # before this one is typed; otherwise the two would be concatenated and the term would be missed.
    Clear-TaxonomySearch -OuterId 'TaxonomySearchBox' -Step 'unused-search-clear'
    Set-TaxonomySearch -OuterId 'TaxonomySearchBox' -Query $deleteTermName -Step 'unused-search'
    $unusedRow = Wait-TaxonomyTermRow -ContainerId 'TaxonomyTermList' -Term $deleteTermName `
        -Units '0' -Code 'taxonomy.term_unreferenced'
    $null = Save-TaxonomyStageState -Stage 'term-unused' -Facts ([ordered] @{
            term = $deleteTermName
            filter = $unusedFilterName
            rowTexts = @($unusedRow.Texts)
            owners = @($releasedOwners)
            chipsAfterRelease = @($releasedChips)
        })

    # --- one delete confirmation, then one undo -------------------------------------------------
    Invoke-TaxonomyRow -Step 'delete-select' -ContainerId 'TaxonomyTermList' -Text $deleteTermName `
        -Code 'taxonomy.term_selection'
    Open-TaxonomyInspector -Step 'delete-inspector' -Code 'taxonomy.delete_open'
    if (-not (Test-TaxonomyEnabled -AutomationId 'TaxonomyDeleteButton' -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.delete_open'
    }
    $null = Invoke-TaxonomyAction -Step 'delete-open' -Arguments @('ui','invoke','TaxonomyDeleteButton') `
        -Code 'taxonomy.delete_open'
    if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','TaxonomyDeleteConfirmDialog') -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.delete_open'
    }
    $confirmRow = Wait-TaxonomySingleRow -ContainerId 'TaxonomyDeleteTermList' -Text $deleteTermName `
        -Code 'taxonomy.delete_confirm'
    $null = Save-TaxonomyStageState -Stage 'delete-confirm' -Facts ([ordered] @{
            term = $deleteTermName
            rowTexts = @($confirmRow.Texts)
            rowName = [string] $confirmRow.Name
        })
    $primarySelector = Get-TaxonomyButton -AutomationId 'PrimaryButton' -Name $deleteConfirmName `
        -Code 'taxonomy.delete_confirm'
    $null = Invoke-TaxonomyAction -Step 'delete-confirm' -Arguments @('ui','invoke',$primarySelector) `
        -Code 'taxonomy.delete_confirm'
    if (-not (Wait-TaxonomyGone -Arguments @('ui','wait-for','TaxonomyDeleteConfirmDialog','--gone') -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.delete_completion'
    }
    $deleted = Read-TaxonomyState -EvidenceId 'taxonomy-deleted'
    if (@($deleted.taxonomy.author) -ccontains $deleteTermName) { throw 'taxonomy.delete_completion' }
    if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','TaxonomyUndoButton') -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.undo_unavailable'
    }
    $null = Save-TaxonomyStageState -Stage 'delete-applied' -Facts ([ordered] @{
            termAbsent = $true
            authors = $deleted.taxonomy.author
        })

    $null = Invoke-TaxonomyAction -Step 'undo-delete' -Arguments @('ui','invoke','TaxonomyUndoButton') `
        -Code 'taxonomy.undo'
    if (-not (Wait-TaxonomyGone -Arguments @('ui','wait-for','TaxonomyUndoButton','--gone') -Seconds $TaxonomyWaitSeconds)) {
        throw 'taxonomy.undo_incomplete'
    }
    $undeleted = Read-TaxonomyState -EvidenceId 'taxonomy-undeleted'
    if (@($undeleted.taxonomy.author) -cnotcontains $deleteTermName) { throw 'taxonomy.delete_restore' }
    $undeletedOwners = Get-TaxonomyTermOwners -State $undeleted -Term $deleteTermName
    if (@($undeletedOwners).Count -ne 0) { throw 'taxonomy.delete_restore.ownership' }
    foreach ($name in $fixtures) {
        $facts = Get-TaxonomyAssetFacts -State $undeleted -Filename $name -Code 'taxonomy.asset_missing'
        if ($facts.AssetKey -cne (Get-TaxonomyAssetFacts -State $released -Filename $name -Code 'taxonomy.asset_missing').AssetKey) {
            throw 'taxonomy.record_identity'
        }
    }
    $null = Save-TaxonomyStageState -Stage 'delete-undone' -Facts ([ordered] @{
            term = $deleteTermName
            owners = @($undeletedOwners)
            authors = $undeleted.taxonomy.author
        })

    # --- nothing physical changed, and the state survives a restart ------------------------------
    foreach ($file in (Read-TaxonomyFormalFiles -LibraryDirectory $folders['library'])) {
        $expected = $hashesBefore[$file.Name]
        if ($null -eq $expected -or $file.Hash -cne $expected) { throw 'taxonomy.file_changed' }
    }
    foreach ($name in $fixtures) {
        if (-not [IO.File]::Exists((Join-Path $folders['library'] $name))) { throw 'taxonomy.file_changed' }
    }

    $stopped = @(Stop-OwnedProcessTree -Ledger @($launched) `
            -ApprovedDirectories @([IO.Path]::GetDirectoryName($ExecutablePath)))
    if ($stopped -cnotcontains $productProcessId) { throw 'taxonomy.restart_stop' }
    $closed = Read-TaxonomyState -EvidenceId 'taxonomy-closed'
    if (@($closed.taxonomy.author) -cnotcontains $mergeSourceName) { throw 'taxonomy.restart_persistence' }

    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId
    if (-not (Test-TaxonomyWait -Arguments @('ui','wait-for','TraceFoldMainWindow') -Seconds $WaitTimeoutSeconds)) {
        throw 'taxonomy.restart_window'
    }
    Open-TaxonomyPage -Step 'restart-open-taxonomy' -Code 'taxonomy.restart_window'
    Select-TaxonomyOption -ComboBoxId 'TaxonomyReferenceFilter' -OptionName $unusedFilterName `
        -Step 'restart-filter' -Code 'taxonomy.restart_persistence'
    Set-TaxonomySearch -OuterId 'TaxonomySearchBox' -Query $deleteTermName -Step 'restart-search'
    $restartRow = Wait-TaxonomyTermRow -ContainerId 'TaxonomyTermList' -Term $deleteTermName `
        -Units '0' -Code 'taxonomy.restart_persistence'
    $reopened = Read-TaxonomyState -EvidenceId 'taxonomy-reopened'
    foreach ($term in @($mergeSourceName, $mergeTargetName, $deleteTermName)) {
        if (@($reopened.taxonomy.author) -cnotcontains $term) { throw 'taxonomy.restart_persistence' }
    }
    foreach ($name in $fixtures) {
        $reopenedFacts = Get-TaxonomyAssetFacts -State $reopened -Filename $name -Code 'taxonomy.asset_missing'
        $undeletedFacts = Get-TaxonomyAssetFacts -State $undeleted -Filename $name -Code 'taxonomy.asset_missing'
        if ((@($reopenedFacts.Authors) -join '|') -cne (@($undeletedFacts.Authors) -join '|')) {
            throw 'taxonomy.restart_persistence'
        }
        if ($reopenedFacts.AssetKey -cne $undeletedFacts.AssetKey) { throw 'taxonomy.record_identity' }
    }
    $null = Save-TaxonomyStageState -Stage 'reopened' -Facts ([ordered] @{
            authors = $reopened.taxonomy.author
            ownership = Get-TaxonomyAuthorOwnership -State $reopened
            unusedRowTexts = @($restartRow.Texts)
        })

    return [pscustomobject] @{
        ScenarioId        = $ScenarioId
        Status            = 'Passed'
        Reason            = ''
        MergeSource       = $mergeSourceName
        MergeTarget       = $mergeTargetName
        DeleteTerm        = $deleteTermName
        SourceOwners      = @($sourceOwners)
        TargetOwners      = @($targetOwners)
        AffectedFiles     = @($affectedNames)
        MergedTargetOwners = @($mergedOwners)
        RestoredSourceOwners = @($restoredSource)
        GuardBlocked      = $true
        UnusedOwners      = @($releasedOwners)
        RenderedReferences = $referenceReadBack
        RestoredDeleteOwners = @($undeletedOwners)
        Artifacts         = @($artifacts)
    }
}
catch {
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or
        ($code -notlike 'taxonomy.*' -and $code -notlike 'ui.*')) {
        $cause = ConvertTo-UiAcceptanceSlug -Value ([string] $code)
        if ($cause.Length -gt 120) { $cause = $cause.Substring(0, 120) }
        $code = 'taxonomy.unexpected'
        if (-not [string]::IsNullOrWhiteSpace($cause)) { $code = $code + '.' + $cause }
    }
    $blockedFailureCodes = @(
        'taxonomy.desktop_unavailable',
        'taxonomy.fixture_missing',
        'taxonomy.fixture_mismatch',
        'taxonomy.fixture_target',
        'taxonomy.snapshot_unavailable'
    )
    $status = if ($blockedFailureCodes -ccontains $code) { 'Blocked' } else { 'Failed' }
    return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = $status; Reason = $code; Artifacts = @($artifacts) }
}
