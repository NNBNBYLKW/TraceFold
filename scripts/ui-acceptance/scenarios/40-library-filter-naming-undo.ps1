# TraceFold UI acceptance - library combined filters, sorting and batch naming (Task 12).
#
# One owned root, created for this run only. Three fixed assets are ingested through the product's
# own UI and given distinguishable metadata through the single-item library inspector, so the
# combined filter intersection is meaningful.
#
# Scope, by design: this scenario drives all five of the library's filtering surfaces - the search
# box, the media-kind picker, and the author, series and content-tag filter pickers - plus both sort
# controls and the batch editor's NAMING tab (rule preview, apply, undo). The batch editor's METADATA
# tab belongs to the batch-metadata scenario, so it is not used here even for setup: the single-item
# inspector is the surface that establishes the metadata.
#
# What is proven:
#   * each of the five conditions is shown to change the visible list on its own, and then all five
#     are applied together and the list is exactly the one asset that satisfies them all, with the
#     record identity of every asset unchanged and the sort order stable across repeated reads;
#   * media kind is proven by asking for videos, which empties the list, and then for images, which
#     brings the three rows back, because every fixed asset is an image;
#   * the search is proven by a query that matches no row, which empties the list, before the final
#     query is left in place and read back from the product's own search box;
#   * the series and the term conditions are proven from their own stages, where adding the condition
#     narrows the list, and each applied condition is confirmed by the chip the product renders;
#   * a rule row can be disabled, which removes its effect from the preview;
#   * an invalid draft is refused by the product and never applied;
#   * one naming apply changes display names only - record identity, physical file names and file
#     bytes are unchanged - and one undo restores the exact prior names, metadata and presets.
#
# Measured product behaviour this scenario depends on:
#   * a closed combo box exposes no list items at all, so an option exists only after the box has
#     been expanded, and the option label is a substring of other accessible names in the window,
#     so an option is matched on the option's own name and never on a descendant;
#   * the filter pickers commit a term only through a chosen suggestion - they have no query handler
#     - so a typed term that is merely confirmed changes nothing while the driver still reports
#     success, and the product's own chip is what confirms the term landed in the draft;
#   * the search box applies its query as the text changes, so it takes effect while it is typed and
#     has no query handler to confirm it with;
#   * an emptied text box reports its accessible name instead of a value, so the search box is
#     asserted to no longer hold the query and by the visible rows the emptied query restores;
#   * the search is not one of the applied-filter chips, and clearing the filters keeps it, so it is
#     asserted separately and emptied explicitly;
#   * the naming rule rows are rendered in the reverse order of the rule set, so a rule is addressed
#     by the label its row renders, never by its index or by its position in the tree.
#
# Read-only guarantees: every conclusion is paired with a read-only snapshot or a file hash, and
# every mutation carries its own step id. A timeout or an ambiguous outcome is never re-sent.
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
    [int] $LibraryWaitSeconds = 60,
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

function New-LibraryText {
    param([Parameter(Mandatory)][int[]] $CodePoints)

    $builder = [System.Text.StringBuilder]::new()
    foreach ($codePoint in $CodePoints) { [void] $builder.Append([char] $codePoint) }
    return $builder.ToString()
}

# Taxonomy names and option labels, composed from code points so this file stays pure ASCII.
$authorOne = New-LibraryText -CodePoints @(0x4F5C, 0x8005, 0x7532)
$authorTwo = New-LibraryText -CodePoints @(0x4F5C, 0x8005, 0x4E59)
$tagOne = New-LibraryText -CodePoints @(0x98CE, 0x666F)
$tagTwo = New-LibraryText -CodePoints @(0x591C, 0x666F)
$seriesOne = New-LibraryText -CodePoints @(0x7CFB, 0x5217, 0x7532)
# Option labels of the product's own selectors.
$imageOption = New-LibraryText -CodePoints @(0x56FE, 0x7247)
$videoOption = New-LibraryText -CodePoints @(0x89C6, 0x9891)
$displayNameField = New-LibraryText -CodePoints @(0x663E, 0x793A, 0x540D, 0x79F0)
$ascendingOption = New-LibraryText -CodePoints @(0x5347, 0x5E8F)
$replaceRuleOption = New-LibraryText -CodePoints @(0x66FF, 0x6362, 0x6587, 0x672C)
$prefixRuleOption = New-LibraryText -CodePoints @(0x6DFB, 0x52A0, 0x524D, 0x7F00)
$sequenceRuleOption = New-LibraryText -CodePoints @(0x6DFB, 0x52A0, 0x5E8F, 0x53F7)

$prefixText = 'ACC-'
$fixtures = @('photo.jpg', 'photo-replacement.jpg', 'photo-third.jpg')
# Accessible names of the product's own filter chips. The draft chips live in the filter flyout and
# name the condition they remove; the applied chips live on the page and all share one name.
$authorDraftChip = New-LibraryText -CodePoints @(
    0x79FB, 0x9664, 0x4F5C, 0x8005, 0x7B5B, 0x9009, 0x6761, 0x4EF6)
$tagDraftChip = New-LibraryText -CodePoints @(
    0x79FB, 0x9664, 0x5185, 0x5BB9, 0x6807, 0x7B7E, 0x7B5B, 0x9009, 0x6761, 0x4EF6)
$seriesDraftChip = New-LibraryText -CodePoints @(
    0x79FB, 0x9664, 0x7CFB, 0x5217, 0x7B5B, 0x9009, 0x6761, 0x4EF6)
$appliedChip = New-LibraryText -CodePoints @(
    0x79FB, 0x9664, 0x6B64, 0x7B5B, 0x9009, 0x6761, 0x4EF6)
# The search is matched against the display name or the actual file name, so a query that keeps the
# hyphen excludes the first asset while the final query keeps two of the three.
$searchQuery = 'photo-'
$noMatchQuery = 'zz-no-such-asset'

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
    ours never reached the product, so it is reported as the blocked environment condition it is, and
    not as the product failing. A driver that timed out is not that case and keeps its own code.
#>
function New-LibraryFailureCode {
    param(
        [Parameter(Mandatory)][AllowNull()] $Result,
        [Parameter(Mandatory)][string] $Step,
        [Parameter(Mandatory)][string] $Code
    )

    if ([string] $Result.Status -ceq 'Blocked' -and
        [string] $Result.Error -ceq 'foreground desktop ownership unavailable') {
        return 'library.desktop_unavailable'
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

function Invoke-LibraryAction {
    param([string] $Step, [string[]] $Arguments, [string] $Code)
    $result = Invoke-WinAppMutation -StepId ('library.' + $Step) -Arguments $Arguments `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -TimeoutMs $MutationTimeoutMs `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($result.Status -cne 'Passed') {
        throw (New-LibraryFailureCode -Result $result -Step $Step -Code $Code)
    }
    return $result
}

function Read-LibraryNodes {
    return @(Read-UiAcceptanceTree -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -FailureCode 'library.inspect_unavailable' `
        -Depth $InspectDepth -TimeoutSeconds $ObservationTimeoutSeconds)
}

function Test-LibraryWait {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'library.observation_unavailable' }
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['found']) {
        throw 'library.driver_schema_incompatible'
    }
    return [bool] $observation.Data.found
}

function Wait-LibraryGone {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'library.observation_unavailable' }
    return $true
}

function Read-LibraryValue {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][string] $Code)

    $observation = Invoke-WinAppObservation -Arguments @('ui','get-value',$AutomationId) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed' -or $null -eq $observation.Data) { throw $Code }
    if ($null -eq $observation.Data.PSObject.Properties['text']) { throw 'library.driver_schema_incompatible' }
    return [string] $observation.Data.text
}

function Test-LibraryEnabled {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][int] $Seconds)

    return (Test-LibraryWait -Seconds $Seconds -Arguments @(
            'ui','wait-for',$AutomationId,'--property','IsEnabled','--value','True'))
}

<#
.SYNOPSIS
    Reads one element's current value through a selector observed in the live tree.

.DESCRIPTION
    The composite pickers keep their stable id on the outer control, so the value belongs to an inner
    element that has no id of its own and can only be read through the selector this run observed.
#>
function Read-LibraryValueBySelector {
    param([Parameter(Mandatory)][string] $Selector, [Parameter(Mandatory)][string] $Code)

    $observation = Invoke-WinAppObservation -Arguments @('ui','get-value',$Selector) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed' -or $null -eq $observation.Data) { throw $Code }
    if ($null -eq $observation.Data.PSObject.Properties['text']) { throw 'library.driver_schema_incompatible' }
    return [string] $observation.Data.text
}

<#
.SYNOPSIS
    Waits, by repeated observation, until the library shows an expected number of rows.

.DESCRIPTION
    The product reloads the list in response to a condition change, so the count is read until it
    reaches the expected value. Reads are the only pacing, because a fixed wait would encode one
    machine's speed, and the count that was actually seen is reported when the wait runs out.
#>
function Wait-LibraryRowCount {
    param([Parameter(Mandatory)][int] $Expected, [Parameter(Mandatory)][string] $Code)

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $count = -1
    while ($deadline.Elapsed.TotalSeconds -lt $LibraryWaitSeconds) {
        $count = (Get-VisibleLibraryRows).Count
        if ($count -eq $Expected) { return }
    }
    throw ($Code + '.' + [string] $count)
}

<#
.SYNOPSIS
    Opens the library's filter flyout and waits until it is on screen.

.DESCRIPTION
    Every condition of the flyout is edited in a draft that the product applies only on request, so
    each stage opens the flyout and then edits that draft.
#>
function Open-LibraryFilterFlyout {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][string] $Code)

    $null = Invoke-LibraryAction -Step $Step -Arguments @('ui','invoke','LibraryFilterButton') -Code $Code
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryFilterFlyout') -Seconds $LibraryWaitSeconds)) {
        throw $Code
    }
}

<#
.SYNOPSIS
    Applies the filter draft through the flyout's own apply button.
#>
function Invoke-LibraryFilterApply {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][string] $Code)

    $null = Invoke-LibraryAction -Step $Step -Arguments @('ui','invoke','LibraryFilterApplyButton') -Code $Code
}

<#
.SYNOPSIS
    Reads the library search box's current text back from the product.
#>
function Get-LibrarySearchValue {
    param([Parameter(Mandatory)][string] $OuterId, [Parameter(Mandatory)][string] $Code)

    $nodes = Read-LibraryNodes
    $outer = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $OuterId -FailureCode $Code
    if ($null -eq $outer) { throw $Code }
    $inner = Get-LibraryPickerTextBox -Node $outer -Code $Code
    return (Read-LibraryValueBySelector -Selector $inner -Code $Code)
}

<#
.SYNOPSIS
    Types a query into the library search box.

.DESCRIPTION
    The search box is an auto-suggest box whose text is bound to the view model, and the view model
    applies the query whenever that text changes, so the query takes effect as it is typed: there is
    no query handler to confirm it with. The box is therefore typed into rather than assigned,
    because only a real per-character input raises the change the product listens for. The visible
    rows, which the caller asserts, are what prove the query was applied.
#>
function Set-LibrarySearchText {
    param(
        [Parameter(Mandatory)][string] $OuterId,
        [Parameter(Mandatory)][string] $Query,
        [Parameter(Mandatory)][string] $Step
    )

    $nodes = Read-LibraryNodes
    $outer = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $OuterId `
        -FailureCode 'library.search_input'
    if ($null -eq $outer) { throw 'library.search_input' }
    $inner = Get-LibraryPickerTextBox -Node $outer -Code 'library.search_input'
    $null = Invoke-LibraryKeyboard -Step ('search-' + $Step) -Code 'library.search_input' `
        -Arguments @('ui','send-keys',$Query,'--verbatim','--target',$inner)
}

<#
.SYNOPSIS
    Asserts that the library search box reads back the query that is in effect.

.DESCRIPTION
    The value is read from the product's own search box, and the failure names the shape of what was
    actually read - its length and how it splits between ASCII and other characters, for the inner
    text box and for the outer control - so a box that kept the query, a box that never received it
    and a control whose value cannot be read can be told apart without either of them being guessed.
#>
function Assert-LibrarySearchValue {
    param(
        [Parameter(Mandatory)][string] $OuterId,
        # An empty query is a legitimate expectation here, and a mandatory string parameter refuses an
        # empty string unless it says so.
        [Parameter(Mandatory)][AllowEmptyString()][string] $Query,
        [Parameter(Mandatory)][string] $Step
    )

    $observed = Get-LibrarySearchValue -OuterId $OuterId -Code 'library.search_value'
    if ($observed -ceq $Query) { return }
    $artifacts.Add((Save-LibraryEvidence -EvidenceId ('library-search-' + $Step) -Nodes (Read-LibraryNodes)))
    $ascii = 0
    foreach ($character in $observed.ToCharArray()) {
        if ([int] $character -lt 128) { $ascii++ }
    }
    $outerLength = 0
    try { $outerLength = (Read-LibraryValue -AutomationId $OuterId -Code 'library.search_value').Length }
    catch { $outerLength = -1 }
    throw ('library.search_value.in' + [string] $observed.Length +
        '.ascii' + [string] $ascii +
        '.other' + [string] ($observed.Length - $ascii) +
        '.outer' + [string] $outerLength)
}

<#
.SYNOPSIS
    Empties the library search box and asserts that the query is gone.

.DESCRIPTION
    Clearing cannot be expressed by typing, and the value cannot be assigned an empty string either,
    because the harness refuses to bind an argument list whose last element is empty. The box is
    therefore emptied with the backspace key, pressed more times than the longest query this scenario
    types. That is idempotent rather than timed: with the caret already at the start, the extra
    presses delete nothing, so no assumption about where the caret is or how fast the box reacts is
    involved.

    Emptiness itself is not readable: an emptied text box reports its accessible name instead of a
    value, so the check is that the query that was in the box is no longer there, and the caller
    asserts the visible rows that the emptied query restores. That is the product's own result rather
    than a value the driver cannot report.
#>
function Clear-LibrarySearchText {
    param(
        [Parameter(Mandatory)][string] $OuterId,
        [Parameter(Mandatory)][AllowEmptyString()][string] $PreviousQuery,
        [Parameter(Mandatory)][string] $Step
    )

    $nodes = Read-LibraryNodes
    $outer = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $OuterId `
        -FailureCode 'library.search_input'
    if ($null -eq $outer) { throw 'library.search_input' }
    $inner = Get-LibraryPickerTextBox -Node $outer -Code 'library.search_input'
    # The driver takes the keys as one argument of space-separated tokens, so the presses are joined
    # into a single token list rather than passed as separate arguments.
    $presses = @()
    for ($index = 0; $index -lt 24; $index++) { $presses += 'vk=0x08' }
    $null = Invoke-LibraryKeyboard -Step ('search-' + $Step) -Code 'library.search_input' `
        -Arguments @('ui','send-keys',($presses -join ' '),'--target',$inner)
    if ((Get-LibrarySearchValue -OuterId $OuterId -Code 'library.search_value') -ceq $PreviousQuery) {
        $artifacts.Add((Save-LibraryEvidence -EvidenceId ('library-search-' + $Step) -Nodes (Read-LibraryNodes)))
        throw 'library.search_clear'
    }
}

function Read-LibraryState {
    param([Parameter(Mandatory)][string] $EvidenceId)

    $raw = & $SupportPath snapshot --data-root $OwnedRoot --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'library.snapshot_unavailable' }
    $data = ConvertFrom-UiAcceptanceJson -Text ($raw | Out-String) -Source 'snapshot'
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

function Get-AssetFacts {
    param([Parameter(Mandatory)] $State, [Parameter(Mandatory)][string] $Filename,
        [Parameter(Mandatory)][string] $Code)

    $assets = @($State.formalAssets | Where-Object { [string] $_.actualFilename -ceq $Filename })
    if ($assets.Count -ne 1) { throw $Code }
    $series = ''
    if ($null -ne $assets[0].PSObject.Properties['series'] -and $null -ne $assets[0].series) {
        $series = [string] $assets[0].series
    }
    $display = ''
    if ($null -ne $assets[0].PSObject.Properties['displayName'] -and $null -ne $assets[0].displayName) {
        $display = [string] $assets[0].displayName
    }
    return [pscustomobject] @{
        Filename    = $Filename
        AssetKey    = [string] $assets[0].assetKey
        DisplayName = $display
        Authors     = @($assets[0].authors | ForEach-Object { [string] $_ } | Sort-Object)
        Series      = $series
        Tags        = @($assets[0].tags | ForEach-Object { [string] $_ } | Sort-Object)
    }
}

<#
.SYNOPSIS
    The library rows currently shown, in the order the product renders them.
#>
function Get-LibraryRowPrefix {
    param([Parameter(Mandatory)][string] $Filename)
    return $libraryRowDeclaredName.Replace($libraryRowDeclaredFilename, $Filename)
}

function Get-VisibleLibraryRows {
    $nodes = Read-LibraryNodes
    $rows = @()
    foreach ($node in $nodes) {
        $name = Get-UiAcceptanceNodeProperty -Node $node -Name 'name'
        if ((Get-UiAcceptanceNodeProperty -Node $node -Name 'type') -ceq 'ListItem' -and
            $name.StartsWith($libraryRowGroupPrefix, [StringComparison]::Ordinal)) {
            $rows += $name
        }
    }
    # Returned as an array even when it holds one row, so a caller's count never collapses into a
    # scalar and an empty grid is reported as zero rows rather than as a missing property.
    return ,@($rows | Select-Object -Unique)
}

<#
.SYNOPSIS
    Selects one option of a product combo box by its exact visible label.

.DESCRIPTION
    A closed combo box renders only its selection box, so its options are not present as list items
    until the drop-down is open, and the label is a substring of other accessible names in the same
    window, so a text selector is ambiguous. The box is therefore expanded first and the option is
    taken from the opened list as the unique list item whose own accessible name is the label.
    The option is matched on its own name rather than on any descendant, because a grid row also
    contains a text element with the media kind and must never be mistaken for the option.
    Reading the tree is an observation, so the wait for the list repeats reads rather than actions.
#>
function Select-LibraryOption {
    param(
        [Parameter(Mandatory)][string] $ComboBoxId,
        [Parameter(Mandatory)][string] $OptionName,
        [Parameter(Mandatory)][string] $Step,
        [Parameter(Mandatory)][string] $Code
    )

    $null = Invoke-LibraryAction -Step ($Step + '-open') -Arguments @('ui','invoke',$ComboBoxId) `
        -Code ($Code + '.open')

    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        $nodes = Read-LibraryNodes
        $items = @($nodes | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq $OptionName })
        if ($items.Count -eq 1) {
            $selector = Get-UiAcceptanceNodeSelector -Node $items[0] -FailureCode $Code
            $null = Invoke-LibraryAction -Step $Step -Arguments @('ui','invoke',$selector) -Code $Code
            return
        }
        if ($items.Count -gt 1) {
            Save-LibraryOptionEvidence -EvidenceId ($Step + '-option-ambiguous')
            throw ($Code + '.options' + [string] $items.Count)
        }
    }
    Save-LibraryOptionEvidence -EvidenceId ($Step + '-option-missing')
    throw ($Code + '.option_missing')
}

<#
.SYNOPSIS
    Records the observed tree for a combo box that did not offer exactly one option.

.DESCRIPTION
    The tree is written as redacted evidence rather than as a raw dump, so a failure can be explained
    from the run's own artifacts without any generated identifier reaching disk.
#>
function Save-LibraryOptionEvidence {
    param([Parameter(Mandatory)][string] $EvidenceId)

    $artifacts.Add((Save-LibraryEvidence -EvidenceId $EvidenceId -Nodes (Read-LibraryNodes)))
}

function Read-LibraryFormalFiles {
    param([Parameter(Mandatory)][string] $LibraryDirectory)
    $files = @()
    foreach ($path in @([IO.Directory]::EnumerateFiles($LibraryDirectory, '*', [IO.SearchOption]::AllDirectories))) {
        $files += [pscustomobject] @{
            Name = [IO.Path]::GetFileName($path)
            Hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }
    return $files
}

function ConvertTo-LibraryEvidenceNodes {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Nodes)

    $normalized = [System.Collections.Generic.List[object]]::new()
    foreach ($node in @($Nodes)) {
        $normalized.Add([pscustomobject] @{
            automationId = Get-UiAcceptanceNodeProperty -Node $node -Name 'automationId'
            controlType  = Get-UiAcceptanceNodeProperty -Node $node -Name 'type'
            name         = Get-UiAcceptanceNodeProperty -Node $node -Name 'name'
            enabled      = ((Get-UiAcceptanceNodeProperty -Node $node -Name 'isEnabled') -ceq 'True')
            offscreen    = ((Get-UiAcceptanceNodeProperty -Node $node -Name 'isOffscreen') -ceq 'True')
            bounds       = [pscustomobject] @{
                x      = [int] (Get-UiAcceptanceNodeProperty -Node $node -Name 'x')
                y      = [int] (Get-UiAcceptanceNodeProperty -Node $node -Name 'y')
                width  = [int] (Get-UiAcceptanceNodeProperty -Node $node -Name 'width')
                height = [int] (Get-UiAcceptanceNodeProperty -Node $node -Name 'height')
            }
        })
    }
    return $normalized.ToArray()
}

function Save-LibraryEvidence {
    param([Parameter(Mandatory)][string] $EvidenceId, [Parameter(Mandatory)][object[]] $Nodes)
    return (Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory -EvidenceId $EvidenceId `
        -Nodes (ConvertTo-LibraryEvidenceNodes -Nodes $Nodes) -RunToken $RunToken -OwnedRoot $OwnedRoot)
}

function Save-LibraryScreenshot {
    param([Parameter(Mandatory)][string] $Selector, [Parameter(Mandatory)][string] $Name)

    $source = Join-Path $OwnedRoot ('screenshots/' + $Name + '.png')
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($source))
    $captured = Invoke-WinAppObservation -Arguments @('ui','screenshot',$Selector,'--output',$source) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($captured.Status -cne 'Passed' -or -not [IO.File]::Exists($source)) {
        throw 'library.screenshot_unavailable'
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
    Waits, by repeated observation, until one visible node of a native type carries an exact name.

.DESCRIPTION
    The product renders a suggestion or a chip asynchronously, so an observation is repeated until
    the element appears. Only elements that are on screen count: a collapsed flyout keeps its
    previously realised controls in the tree off screen, so a term that was deselected would
    otherwise still be reported as if it were still selected. This is a readiness check, not a
    selection: what is acted on is chosen by the product's own keyboard navigation, and the number of
    conditions that end up applied is asserted separately from the product's chip container. Reads
    are the only pacing, because a fixed sleep would encode one machine's speed.
#>
function Wait-LibraryNamedNode {
    param(
        [Parameter(Mandatory)][string] $NativeType,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][int] $Seconds
    )

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt $Seconds) {
        $nodes = Read-LibraryNodes
        $found = @($nodes | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq $NativeType -and
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq $Name -and
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'isOffscreen') -ceq 'False' })
        if ($found.Count -ge 1) { return $true }
    }
    return $false
}

<#
.SYNOPSIS
    Counts the filter conditions the page currently reports as applied.

.DESCRIPTION
    The applied conditions are the chips inside the page's own active-filter container, so the count
    is taken from that container's subtree rather than from anywhere in the window. The container is
    hidden while nothing is applied, which is reported as zero conditions.
#>
function Get-LibraryAppliedFilterCount {
    $nodes = Read-LibraryNodes
    $container = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId 'LibraryActiveFilters' `
        -FailureCode 'library.filter_chips'
    if ($null -eq $container) { return 0 }
    $descendants = [System.Collections.Generic.List[object]]::new()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $container)) { $pending.Push($child) }
    while ($pending.Count -gt 0) {
        $node = $pending.Pop()
        $descendants.Add($node)
        foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $node)) { $pending.Push($child) }
    }
    return @($descendants | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'Button' -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq $appliedChip }).Count
}

<#
.SYNOPSIS
    Resolves one element of the naming rule row that renders a given label.

.DESCRIPTION
    The rule rows are rendered in the reverse order of the rule set, so neither the row index the
    product assigns nor the position in the tree identifies a row: taking the last row seen, or the
    highest index, silently addresses a different rule. The list is walked from its own container
    instead, the row is identified by the label it renders, and the requested element is taken from
    that row's own subtree, so a rule added later can never be confused with an older one.
#>
function Get-LibraryRuleElementId {
    param(
        [Parameter(Mandatory)][string] $RuleLabel,
        [Parameter(Mandatory)][string] $Suffix
    )

    $nodes = Read-LibraryNodes
    $list = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId 'LibraryNamingRuleList' `
        -FailureCode 'library.rule_rows'
    if ($null -eq $list) { throw 'library.rule_rows' }
    $found = @()
    foreach ($row in @(Get-UiAcceptanceNodeChildren -Node $list)) {
        $elementId = ''
        $labels = [System.Collections.Generic.List[string]]::new()
        $pending = [System.Collections.Generic.Stack[object]]::new()
        foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $row)) { $pending.Push($child) }
        while ($pending.Count -gt 0) {
            $node = $pending.Pop()
            $id = Get-UiAcceptanceNodeProperty -Node $node -Name 'automationId'
            if ($id -like ('*-' + $Suffix)) { $elementId = $id }
            if ((Get-UiAcceptanceNodeProperty -Node $node -Name 'type') -ceq 'Text') {
                $labels.Add((Get-UiAcceptanceNodeProperty -Node $node -Name 'name'))
            }
            foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $node)) { $pending.Push($child) }
        }
        if ($labels -ccontains $RuleLabel) {
            if ([string]::IsNullOrEmpty($elementId)) { throw 'library.rule_rows' }
            $found += $elementId
        }
    }
    if ($found.Count -ne 1) { throw ('library.rule_rows.' + [string] $found.Count) }
    return $found[0]
}

<#
.SYNOPSIS
    Waits until the naming preview reaches the shape the enabled rules imply.

.DESCRIPTION
    The preview is the product's own projection of the rule set onto the selection and is recomputed
    as the rules change, so it is read until it shows exactly the three prefixed names, with a
    sequence number or without one. Reads are the only pacing, because a fixed sleep would encode
    one machine's speed. The names come back as an array even when the wait timed out empty, so an
    absent preview is reported as zero names rather than as a missing property.
#>
function Wait-LibraryPreview {
    param(
        [Parameter(Mandatory)][string] $Prefix,
        [Parameter(Mandatory)][bool] $WithDigit,
        [Parameter(Mandatory)][int] $Seconds
    )

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($deadline.Elapsed.TotalSeconds -lt $Seconds) {
        $names = @(Read-LibraryNodes | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'Text' -and
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').StartsWith($Prefix, [StringComparison]::Ordinal)
        } | ForEach-Object { Get-UiAcceptanceNodeProperty -Node $_ -Name 'name' } | Select-Object -Unique)
        if ($names.Count -eq 3) {
            $withDigits = @($names | Where-Object { $_ -match '\d' }).Count
            if ($WithDigit -and $withDigits -eq 3) { return ,$names }
            if (-not $WithDigit -and $withDigits -eq 0) { return ,$names }
        }
    }
    return ,@()
}

<#
.SYNOPSIS
    Sends one keyboard mutation and reports the driver's own reason when it fails.

.DESCRIPTION
    A keyboard dispatch is a mutation, so it is never retried; the failure reason is redacted to a
    slug and kept, because a refused transport and a missing element are different product facts.
#>
function Invoke-LibraryKeyboard {
    param(
        [Parameter(Mandatory)][string] $Step,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $Code
    )

    $result = Invoke-WinAppKeyboard -StepId ('library.' + $Step) -Arguments $Arguments `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck `
        -DesktopCheck $DesktopCheck -RunToken $RunToken -OwnedRoot $OwnedRoot -TimeoutMs $MutationTimeoutMs
    if ($result.Status -cne 'Passed') {
        throw (New-LibraryFailureCode -Result $result -Step $Step -Code $Code)
    }
    return $result
}

<#
.SYNOPSIS
    Resolves the unique text box inside an observed picker.

.DESCRIPTION
    The stable id belongs to the outer picker while the editable value lives on the inner text box,
    which the framework may place at any depth inside the picker's own template. The search stays
    inside the exact node this inspection returned, so a same-named control elsewhere in the window
    can never be picked up, and it still demands exactly one match.
#>
function Get-LibraryPickerTextBox {
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
    Commits one taxonomy term into one filter picker.

.DESCRIPTION
    The filter pickers are auto-suggest boxes that commit a term only when a suggestion is chosen:
    unlike the single-item inspector, they have no query handler, so typing a term and confirming it
    would leave the draft unchanged while the driver still reported success. The box is therefore
    typed into with the harness's send-input transport, which raises a real per-character KeyDown and
    is what produces the suggestion list, and the suggestion is then chosen with the arrow key the
    control listens for. The committed draft is confirmed by the chip the product renders for it, so
    a box that silently ignored the term can never be read as a filter that was set.
#>
function Set-LibraryFilterTerm {
    param(
        [Parameter(Mandatory)][string] $OuterId,
        [Parameter(Mandatory)][string] $Term,
        [Parameter(Mandatory)][string] $DraftChipName,
        [Parameter(Mandatory)][string] $Step
    )

    $nodes = Read-LibraryNodes
    $outer = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $OuterId `
        -FailureCode 'library.filter_term_box'
    if ($null -eq $outer) { throw 'library.filter_term_box' }
    $inner = Get-LibraryPickerTextBox -Node $outer -Code 'library.filter_term_input'

    $null = Invoke-LibraryKeyboard -Step ($Step + '-type') -Code 'library.filter_term' `
        -Arguments @('ui','send-keys',$Term,'--verbatim','--target',$inner)
    if (-not (Wait-LibraryNamedNode -NativeType 'ListItem' -Name $Term -Seconds $LibraryWaitSeconds)) {
        throw 'library.filter_suggestion'
    }

    $null = Invoke-LibraryKeyboard -Step ($Step + '-choose') -Code 'library.filter_term' `
        -Arguments @('ui','send-keys','down','--target',$inner)
    if (-not (Wait-LibraryNamedNode -NativeType 'Button' -Name $DraftChipName -Seconds $LibraryWaitSeconds)) {
        throw 'library.filter_chip'
    }
}

<#
.SYNOPSIS
    Commits one taxonomy token into one composite picker, addressed by its own identifiers.
#>
function Set-LibraryTerm {
    param(
        [Parameter(Mandatory)][string] $OuterId,
        [Parameter(Mandatory)][string] $Term,
        [Parameter(Mandatory)][string] $Step
    )

    $nodes = Read-LibraryNodes
    $outer = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $OuterId `
        -FailureCode 'library.draft_input_box'
    if ($null -eq $outer) { throw 'library.draft_input_box' }
    $inner = Get-UiAcceptanceDescendantSelector -Node $outer -NativeType 'Edit' -ClassName 'TextBox' `
        -FailureCode 'library.draft_input_inner'
    $result = Set-UiAcceptanceTaxonomyTerm -StepId ('library.' + $Step) `
        -InnerSelector $inner -Term $Term -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -ForegroundCheck $ForegroundCheck `
        -DesktopCheck $DesktopCheck -TimeoutMs $MutationTimeoutMs -FailureCode 'library.draft_input'
    if ($result.Status -cne 'Passed') {
        throw (New-LibraryFailureCode -Result $result -Step $Step -Code 'library.draft_input')
    }
}

<#
.SYNOPSIS
    Resolves one row from a fresh inspection of the live grid.
#>
function Resolve-LibraryRowSelector {
    param([Parameter(Mandatory)][string] $NamePrefix, [Parameter(Mandatory)][string] $NameContains)

    $nodes = Read-LibraryNodes
    $found = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
        -NamePrefix $NamePrefix -NameContains $NameContains -FailureCode 'library.row_missing'
    if ($null -eq $found) {
        # A miss reports the shape of what was actually in the tree, so a wrong prefix or an empty
        # grid can be told apart from a genuine absence.
        $listItems = @($nodes | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' }).Count
        $contains = @($nodes | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').Contains($NameContains) }).Count
        throw ('library.row_missing.items' + [string] $listItems + '.contains' + [string] $contains)
    }
    return (Get-UiAcceptanceNodeSelector -Node $found -FailureCode 'library.row_missing')
}

<#
.SYNOPSIS
    Invokes one row through its freshly observed selector.

.DESCRIPTION
    The grid re-renders whenever a row's state or metadata changes, so a selector observed earlier
    can go stale. The shared resolver re-observes only when the driver reports a lookup-stage miss,
    which means nothing was dispatched, and each dispatch carries its own step id.
#>
function Invoke-LibraryRow {
    param(
        [Parameter(Mandatory)][string] $Step,
        [Parameter(Mandatory)][string] $NamePrefix,
        [Parameter(Mandatory)][string] $NameContains,
        [Parameter(Mandatory)][string] $Code
    )

    $result = Invoke-UiAcceptanceResolvedMutation -StepId ('library.' + $Step) `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot `
        -Verb 'invoke' -TimeoutMs $MutationTimeoutMs -FailureCode $Code `
        -ResolveSelector { Resolve-LibraryRowSelector -NamePrefix $NamePrefix -NameContains $NameContains }
    if ($result.Status -cne 'Passed') {
        # Keep the driver's own reason, redacted to a slug, so a failure names its cause.
        throw (New-LibraryFailureCode -Result $result -Step $Step -Code $Code)
    }
}

function Select-AllLibraryRows {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][string] $Expected)

    $null = Invoke-LibraryKeyboard -Step $Step -Code 'library.selection' `
        -Arguments @('ui','send-keys','ctrl+a','--target','LibraryGrid')
    if (-not (Test-LibraryEnabled -AutomationId 'LibraryBatchEditButton' -Seconds $LibraryWaitSeconds)) {
        throw 'library.selection'
    }
    $count = Read-LibraryValue -AutomationId 'LibrarySelectedCount' -Code 'library.selection'
    if ($count -cne $Expected) { throw 'library.selection' }
}

$failureCode = 'library.unexpected'
$blockedFailureCodes = @(
    'library.desktop_unavailable',
    'library.fixture_missing',
    'library.fixture_mismatch',
    'library.fixture_target',
    'library.snapshot_unavailable'
)

try {
    foreach ($requiredInput in @($ExecutablePath, $ContractPath, $OwnedRoot, $OutputDirectory, $SupportPath, $WinAppPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredInput)) { throw 'library.invalid_input' }
    }
    if ([IO.Path]::GetFileName($ExecutablePath) -ine 'TraceFold.Media.exe') { throw 'library.invalid_product' }
    if (-not [IO.File]::Exists($ContractPath)) { throw 'library.contract_missing' }
    if (-not [IO.File]::Exists($SupportPath) -or -not [IO.File]::Exists($WinAppPath)) { throw 'library.tooling_missing' }

    $contract = ConvertFrom-UiAcceptanceJson -Text ([IO.File]::ReadAllText($ContractPath)) -Source 'uia contract'
    Assert-UiAcceptanceFields -InputObject $contract -Required @('entries','dynamicRows','fixtureAssets','sessionPreparation')

    $entryIndex = @{}
    foreach ($entry in @($contract.entries)) {
        Assert-UiAcceptanceFields -InputObject $entry -Required @('automationId','name','requiredPatterns')
        $entryIndex[[string] $entry.automationId] = $entry
    }
    foreach ($requiredId in @('NavLibrary','LibraryGrid','LibrarySelectedCount','LibrarySearchBox',
            'LibraryFilterButton','LibraryMediaFilter','LibraryAuthorFilter','LibrarySeriesFilter',
            'LibraryTagFilter','LibraryFilterApplyButton','LibraryClearFiltersButton','LibraryActiveFilters',
            'LibrarySortField','LibrarySortDirection','LibraryInspectorOpenButton','LibraryAuthorInput',
            'LibrarySeriesInput','LibraryTagInput','LibraryBatchEditButton','LibraryBatchEditDialog',
            'LibraryBatchNamingTab','LibraryNamingAddRuleButton','LibraryNamingRuleKindSelector',
            'LibraryNamingTextParameter','LibraryNamingSequenceStartParameter','LibraryNamingPreviewList',
            'LibraryNamingPresetNameInput','LibraryNamingSavePresetButton','LibraryBatchApplyButton',
            'LibraryBatchUndoButton','IngestButton')) {
        if (-not $entryIndex.ContainsKey($requiredId)) { throw ('library.contract_missing_entry.' + $requiredId) }
    }

    $preparation = $contract.sessionPreparation
    Assert-UiAcceptanceFields -InputObject $preparation -Required @(
        'seedFilenames','savedStatusText','savedStatusAutomationId','settingsNavigationAutomationId',
        'inboxNavigationAutomationId','saveFolderSettingsAutomationId','scanTriggerAutomationId','folderInputs')

    $inboxRows = @($contract.dynamicRows | Where-Object { [string] $_.phase -ceq 'inbox-seeded' })
    if ($inboxRows.Count -ne 1) { throw 'library.contract_empty' }
    $inboxRow = $inboxRows[0]
    Assert-UiAcceptanceFields -InputObject $inboxRow -Required @('fixedAccessibleName','fixedFilename','nativeControlType')
    $libraryRows = @($contract.dynamicRows | Where-Object { [string] $_.phase -ceq 'post-ingest' })
    if ($libraryRows.Count -ne 1) { throw 'library.contract_empty' }
    $libraryRowDeclaredName = [string] $libraryRows[0].fixedAccessibleName
    $libraryRowDeclaredFilename = [string] $libraryRows[0].fixedFilename
    if (-not $libraryRowDeclaredName.Contains($libraryRowDeclaredFilename)) {
        throw 'library.contract_invalid'
    }
    # Every library row shares the leading token that precedes the declared filename; a concrete
    # row name is the declared name with the declared filename replaced.
    $libraryRowGroupPrefix = $libraryRowDeclaredName.Substring(
        0, $libraryRowDeclaredName.IndexOf($libraryRowDeclaredFilename, [StringComparison]::Ordinal))

    $assets = @($contract.fixtureAssets)
    $verified = @{}
    foreach ($name in $fixtures) {
        $matches = @($assets | Where-Object { [string] $_.fixedFilename -ceq $name })
        if ($matches.Count -ne 1) { throw 'library.fixture_missing' }
        $verified[$name] = Test-UiAcceptanceFixtureAsset -Asset $matches[0] -RepositoryRoot $repositoryRoot
    }

    $folderInputs = @($preparation.folderInputs)
    if ($folderInputs.Count -eq 0) { throw 'library.contract_empty' }

    [void][IO.Directory]::CreateDirectory($OutputDirectory)

    Reset-UiAcceptanceMutationSteps
    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId

    $waited = Invoke-WinAppObservation -Arguments @('ui','wait-for','TraceFoldMainWindow') `
        -ProcessId $productProcessId -TimeoutSeconds $WaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($waited.Status -cne 'Passed' -or $null -eq $waited.Data -or $waited.Data.found -ne $true) {
        throw 'library.window_unavailable'
    }

    # --- owned preparation: three directories through the product's own Settings UI ---
    $folders = @{}
    foreach ($folderInput in $folderInputs) {
        Assert-UiAcceptanceFields -InputObject $folderInput -Required @('key','automationId','relativeDirectory')
        $relative = [string] $folderInput.relativeDirectory
        if ($relative -cnotmatch '^[a-z0-9][a-z0-9-]*$') { throw 'library.fixture_target' }
        $directory = [IO.Path]::GetFullPath((Join-Path $OwnedRoot $relative))
        $rootBoundary = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $directory.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'library.fixture_target' }
        [void][IO.Directory]::CreateDirectory($directory)
        $folders[[string] $folderInput.key] = $directory
    }

    foreach ($folderInput in $folderInputs) {
        $path = $folders[[string] $folderInput.key]
        $null = Invoke-LibraryAction -Step ('set-' + [string] $folderInput.key + '-folder') `
            -Arguments @('ui','set-value',[string] $folderInput.automationId,$path) -Code 'library.folder_settings'
        $readBack = Read-LibraryValue -AutomationId ([string] $folderInput.automationId) -Code 'library.folder_settings'
        if ($readBack -cne $path) { throw 'library.folder_settings' }
    }
    $null = Invoke-LibraryAction -Step 'save-folder-settings' `
        -Arguments @('ui','invoke',[string] $preparation.saveFolderSettingsAutomationId) -Code 'library.folder_settings'
    $status = Read-LibraryValue -AutomationId ([string] $preparation.savedStatusAutomationId) -Code 'library.folder_settings'
    if ($status -cne [string] $preparation.savedStatusText) { throw 'library.folder_settings' }

    # --- three fixed assets are ingested through the product's own UI ---
    foreach ($name in $fixtures) {
        [IO.File]::Copy($verified[$name].Source, (Join-Path $folders['inbox'] $name), $false)
    }
    $null = Invoke-LibraryAction -Step 'open-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) -Code 'library.inbox_navigation'
    $null = Invoke-LibraryAction -Step 'scan' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) -Code 'library.scan_trigger'

    $index = 0
    foreach ($name in $fixtures) {
        $index++
        $pendingPrefix = ([string] $inboxRow.fixedAccessibleName).Replace([string] $inboxRow.fixedFilename, $name)
        if (-not (Test-LibraryWait -Arguments @('ui','wait-for',$pendingPrefix) -Seconds $RowWaitTimeoutSeconds)) {
            throw 'library.row_missing'
        }
        Invoke-LibraryRow -Step ('select-ingest-' + $index) -NamePrefix $pendingPrefix -NameContains $name `
            -Code 'library.row_selection'
        if (-not (Test-LibraryEnabled -AutomationId 'IngestButton' -Seconds $LibraryWaitSeconds)) {
            throw 'library.ingest_not_ready'
        }
        $null = Invoke-LibraryAction -Step ('ingest-' + $index) -Arguments @('ui','invoke','IngestButton') -Code 'library.ingest'
        $prefix = ([string] $inboxRow.fixedAccessibleName).Replace([string] $inboxRow.fixedFilename, $name)
        if (-not (Wait-LibraryGone -Arguments @('ui','wait-for',$prefix,'--gone') -Seconds $RowWaitTimeoutSeconds)) {
            throw 'library.ingest_incomplete'
        }
    }

    $formalBefore = Read-LibraryFormalFiles -LibraryDirectory $folders['library']
    if ($formalBefore.Count -ne 3) { throw 'library.ingest_result' }

    $null = Invoke-LibraryAction -Step 'open-library' -Arguments @('ui','invoke','NavLibrary') -Code 'library.library_navigation'
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryGrid') -Seconds $WaitTimeoutSeconds)) {
        throw 'library.library_navigation'
    }

    $beforeState = Read-LibraryState -EvidenceId 'library-before'
    $beforeFacts = @{}
    foreach ($name in $fixtures) {
        $beforeFacts[$name] = Get-AssetFacts -State $beforeState -Filename $name -Code 'library.asset_missing'
    }
    $hashesBefore = @{}
    foreach ($file in $formalBefore) { $hashesBefore[$file.Name] = $file.Hash }

    # --- distinguishable metadata through the single-item inspector ---
    # The series belongs to the first and the third asset, so the series condition excludes exactly
    # one row, and the author and the tag together exclude a different one.
    $metadataPlan = @(
        @{ file = 'photo.jpg'; authors = @($authorOne); series = $seriesOne; tags = @($tagOne) }
        @{ file = 'photo-replacement.jpg'; authors = @($authorTwo); series = ''; tags = @($tagTwo) }
        @{ file = 'photo-third.jpg'; authors = @($authorOne); series = $seriesOne; tags = @($tagOne, $tagTwo) }
    )
    $planIndex = 0
    foreach ($plan in $metadataPlan) {
        $planIndex++
        Invoke-LibraryRow -Step ('metadata-select-' + $planIndex) `
            -NamePrefix (Get-LibraryRowPrefix -Filename ([string] $plan.file)) `
            -NameContains ([string] $plan.file) -Code 'library.row_selection'
        if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryAuthorInput') -Seconds $LibraryWaitSeconds)) {
            throw 'library.inspector_missing'
        }
        $termIndex = 0
        foreach ($author in @($plan.authors)) {
            $termIndex++
            Set-LibraryTerm -OuterId 'LibraryAuthorInput' -Term $author -Step ('meta-' + $planIndex + '-author-' + $termIndex)
        }
        if (-not [string]::IsNullOrEmpty([string] $plan.series)) {
            Set-LibraryTerm -OuterId 'LibrarySeriesInput' -Term ([string] $plan.series) -Step ('meta-' + $planIndex + '-series')
        }
        $termIndex = 0
        foreach ($tag in @($plan.tags)) {
            $termIndex++
            Set-LibraryTerm -OuterId 'LibraryTagInput' -Term $tag -Step ('meta-' + $planIndex + '-tag-' + $termIndex)
        }
        # Selecting another row flushes the inspector's metadata, which is how the product persists.
        Invoke-LibraryRow -Step ('metadata-flush-' + $planIndex) `
            -NamePrefix (Get-LibraryRowPrefix -Filename ([string] $fixtures[0])) `
            -NameContains ([string] $fixtures[0]) -Code 'library.row_selection'
    }

    $metadataState = Read-LibraryState -EvidenceId 'library-metadata'
    foreach ($plan in $metadataPlan) {
        $facts = Get-AssetFacts -State $metadataState -Filename ([string] $plan.file) -Code 'library.asset_missing'
        if ((@($facts.Authors) -join '|') -cne (@(@($plan.authors) | Sort-Object) -join '|')) {
            throw 'library.metadata_result'
        }
        if ([string] $facts.Series -cne [string] $plan.series) { throw 'library.metadata_result' }
        if ((@($facts.Tags) -join '|') -cne (@(@($plan.tags) | Sort-Object) -join '|')) {
            throw 'library.metadata_result'
        }
    }
    $metadataFacts = @{}
    foreach ($name in $fixtures) {
        $metadataFacts[$name] = Get-AssetFacts -State $metadataState -Filename $name -Code 'library.asset_missing'
    }

    # --- sorting stays observable and stable before any condition narrows the list ---
    Select-LibraryOption -ComboBoxId 'LibrarySortField' -OptionName $displayNameField `
        -Step 'sort-field' -Code 'library.sort'
    Select-LibraryOption -ComboBoxId 'LibrarySortDirection' -OptionName $ascendingOption `
        -Step 'sort-direction' -Code 'library.sort'
    $sortedOnce = Get-VisibleLibraryRows
    if ($sortedOnce.Count -ne 3) {
        $artifacts.Add((Save-LibraryEvidence -EvidenceId 'library-sort-rows' -Nodes (Read-LibraryNodes)))
        throw ('library.sort_count.' + [string] $sortedOnce.Count)
    }
    $sortedTwice = Get-VisibleLibraryRows
    if ((@($sortedOnce) -join '|') -cne (@($sortedTwice) -join '|')) { throw 'library.sort_stable' }

    # --- 1) media kind. All three assets are images, so this condition cannot narrow this set; what
    #        proves it is applied and effective is that asking for videos empties the list and asking
    #        for images brings the three rows back. ---
    Open-LibraryFilterFlyout -Step 'kind-video-flyout' -Code 'library.kind_rows'
    Select-LibraryOption -ComboBoxId 'LibraryMediaFilter' -OptionName $videoOption `
        -Step 'kind-video' -Code 'library.kind_rows'
    Invoke-LibraryFilterApply -Step 'kind-video-apply' -Code 'library.kind_rows'
    Wait-LibraryRowCount -Expected 0 -Code 'library.kind_rows'
    Open-LibraryFilterFlyout -Step 'kind-image-flyout' -Code 'library.kind_rows'
    Select-LibraryOption -ComboBoxId 'LibraryMediaFilter' -OptionName $imageOption `
        -Step 'kind-image' -Code 'library.kind_rows'
    Invoke-LibraryFilterApply -Step 'kind-image-apply' -Code 'library.kind_rows'
    Wait-LibraryRowCount -Expected 3 -Code 'library.kind_rows'

    # --- 2) author and content tag narrow the three rows in turn: 3 -> 2 -> 1. ---
    Open-LibraryFilterFlyout -Step 'author-flyout' -Code 'library.intersection_author'
    Set-LibraryFilterTerm -OuterId 'LibraryAuthorFilter' -Term $authorOne `
        -DraftChipName $authorDraftChip -Step 'author'
    Invoke-LibraryFilterApply -Step 'author-apply' -Code 'library.intersection_author'
    $authorChips = Get-LibraryAppliedFilterCount
    if ($authorChips -ne 2) { throw ('library.filter_chips.' + [string] $authorChips) }
    Wait-LibraryRowCount -Expected 2 -Code 'library.intersection_author'

    Open-LibraryFilterFlyout -Step 'tag-flyout' -Code 'library.intersection_tag'
    Set-LibraryFilterTerm -OuterId 'LibraryTagFilter' -Term $tagTwo `
        -DraftChipName $tagDraftChip -Step 'tag'
    Invoke-LibraryFilterApply -Step 'tag-apply' -Code 'library.intersection_tag'
    $tagChips = Get-LibraryAppliedFilterCount
    if ($tagChips -ne 3) { throw ('library.filter_chips.' + [string] $tagChips) }
    Wait-LibraryRowCount -Expected 1 -Code 'library.intersection_tag'

    # --- 3) the series condition is proven on its own, from a fresh filter set: adding the real
    #        series term narrows the three rows to the two that carry that series. ---
    $null = Invoke-LibraryAction -Step 'clear-before-series' `
        -Arguments @('ui','invoke','LibraryClearFiltersButton') -Code 'library.series_rows'
    Wait-LibraryRowCount -Expected 3 -Code 'library.filter_clear'
    Open-LibraryFilterFlyout -Step 'series-flyout' -Code 'library.series_rows'
    Set-LibraryFilterTerm -OuterId 'LibrarySeriesFilter' -Term $seriesOne `
        -DraftChipName $seriesDraftChip -Step 'series'
    Invoke-LibraryFilterApply -Step 'series-apply' -Code 'library.series_rows'
    Wait-LibraryRowCount -Expected 2 -Code 'library.series_rows'

    # --- 4) the search condition is proven on its own. The box is typed into, so a second query would
    #        be appended to the first: the proving query is emptied again and the rows are asserted
    #        back to what the series alone shows before the final query is typed. The row change is
    #        what proves a query was applied; the box's own value is asserted on top of it. ---
    Set-LibrarySearchText -OuterId 'LibrarySearchBox' -Query $noMatchQuery -Step 'none'
    Wait-LibraryRowCount -Expected 0 -Code 'library.search_rows'
    Assert-LibrarySearchValue -OuterId 'LibrarySearchBox' -Query $noMatchQuery -Step 'none'
    Clear-LibrarySearchText -OuterId 'LibrarySearchBox' -PreviousQuery $noMatchQuery -Step 'between'
    Wait-LibraryRowCount -Expected 2 -Code 'library.search_rows'
    Set-LibrarySearchText -OuterId 'LibrarySearchBox' -Query $searchQuery -Step 'final'
    Wait-LibraryRowCount -Expected 1 -Code 'library.search_rows'
    Assert-LibrarySearchValue -OuterId 'LibrarySearchBox' -Query $searchQuery -Step 'final'

    # --- 5) all five conditions at once. The draft already carries the applied series and search, so
    #        this stage adds media kind, author and tag: the list must end up as exactly the one
    #        asset that satisfies all five. ---
    Open-LibraryFilterFlyout -Step 'final-flyout' -Code 'library.intersection_rows'
    Select-LibraryOption -ComboBoxId 'LibraryMediaFilter' -OptionName $imageOption `
        -Step 'final-kind' -Code 'library.intersection_rows'
    Set-LibraryFilterTerm -OuterId 'LibraryAuthorFilter' -Term $authorOne `
        -DraftChipName $authorDraftChip -Step 'final-author'
    Set-LibraryFilterTerm -OuterId 'LibraryTagFilter' -Term $tagTwo `
        -DraftChipName $tagDraftChip -Step 'final-tag'
    Invoke-LibraryFilterApply -Step 'final-apply' -Code 'library.intersection_rows'
    Wait-LibraryRowCount -Expected 1 -Code 'library.intersection_rows'
    $intersection = Get-VisibleLibraryRows
    if ($intersection[0] -notlike ('*' + $fixtures[2] + '*')) {
        $artifacts.Add((Save-LibraryEvidence -EvidenceId 'library-intersection-asset' -Nodes (Read-LibraryNodes)))
        throw 'library.intersection_asset'
    }
    # The search is not one of the applied-filter chips, so the four flyout conditions are asserted as
    # four chips and the search is asserted separately, from the product's own search box.
    $finalChips = Get-LibraryAppliedFilterCount
    if ($finalChips -ne 4) {
        $artifacts.Add((Save-LibraryEvidence -EvidenceId 'library-intersection-chips' -Nodes (Read-LibraryNodes)))
        throw ('library.intersection_filters.' + [string] $finalChips)
    }
    $finalSearch = Get-LibrarySearchValue -OuterId 'LibrarySearchBox' -Code 'library.intersection_search'
    if ($finalSearch -cne $searchQuery) { throw 'library.intersection_search' }
    # A single row is also what a smaller condition set would leave, so each flyout condition is read
    # back from the product's own control: the picker renders the term it has applied, and the media
    # picker shows the applied kind. The draft is cancelled afterwards, which changes nothing.
    Open-LibraryFilterFlyout -Step 'final-verify-flyout' -Code 'library.intersection_filters'
    foreach ($conditions in @(
            @{ Name = $authorDraftChip; Code = 'library.intersection_author' },
            @{ Name = $seriesDraftChip; Code = 'library.intersection_series' },
            @{ Name = $tagDraftChip; Code = 'library.intersection_tag' })) {
        if (-not (Wait-LibraryNamedNode -NativeType 'Button' -Name $conditions.Name `
                    -Seconds $LibraryWaitSeconds)) {
            $artifacts.Add((Save-LibraryEvidence -EvidenceId 'library-intersection-terms' -Nodes (Read-LibraryNodes)))
            throw $conditions.Code
        }
    }
    $appliedKind = Read-LibraryValue -AutomationId 'LibraryMediaFilter' -Code 'library.intersection_kind'
    if ($appliedKind -cne $imageOption) {
        $artifacts.Add((Save-LibraryEvidence -EvidenceId 'library-intersection-kind' -Nodes (Read-LibraryNodes)))
        throw ('library.intersection_kind.' + [string] $appliedKind.Length)
    }
    # Narrowing the view must not touch the records behind it: all three assets are still present and
    # still carry the identity they had before any condition was applied.
    $filteredState = Read-LibraryState -EvidenceId 'library-intersection-state'
    foreach ($name in $fixtures) {
        $facts = Get-AssetFacts -State $filteredState -Filename $name -Code 'library.asset_missing'
        if ($facts.AssetKey -cne $metadataFacts[$name].AssetKey) { throw 'library.record_identity' }
    }
    $intersectionTree = Save-LibraryEvidence -EvidenceId 'library-intersection' -Nodes (Read-LibraryNodes)
    $artifacts.Add($intersectionTree)
    $null = Save-LibraryScreenshot -Selector 'LibraryGrid' -Name 'library-intersection'
    $null = Invoke-LibraryAction -Step 'final-verify-cancel' `
        -Arguments @('ui','invoke','LibraryFilterCancelButton') -Code 'library.intersection_filters'

    # The search survives a filter clear, so it is emptied on its own before the naming stages, which
    # operate on all three assets.
    Clear-LibrarySearchText -OuterId 'LibrarySearchBox' -PreviousQuery $searchQuery -Step 'clear'
    $null = Invoke-LibraryAction -Step 'clear-filters' `
        -Arguments @('ui','invoke','LibraryClearFiltersButton') -Code 'library.filter'
    Wait-LibraryRowCount -Expected 3 -Code 'library.filter_clear'

    # --- batch naming: prefix plus sequence, preview first ---
    Select-AllLibraryRows -Step 'select-all' -Expected '3'
    $null = Invoke-LibraryAction -Step 'batch-open' -Arguments @('ui','invoke','LibraryBatchEditButton') -Code 'library.batch_open'
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryBatchEditDialog') -Seconds $LibraryWaitSeconds)) {
        throw 'library.batch_open'
    }
    $null = Invoke-LibraryAction -Step 'naming-tab' -Arguments @('ui','invoke','LibraryBatchNamingTab') -Code 'library.batch_open'
    Select-LibraryOption -ComboBoxId 'LibraryNamingRuleKindSelector' -OptionName $prefixRuleOption `
        -Step 'naming-kind-prefix' -Code 'library.naming_rule'
    $null = Invoke-LibraryAction -Step 'naming-add-prefix' -Arguments @('ui','invoke','LibraryNamingAddRuleButton') -Code 'library.naming_rule'
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryNamingTextParameter') -Seconds $LibraryWaitSeconds)) {
        throw 'library.naming_rule'
    }
    $null = Invoke-LibraryAction -Step 'naming-prefix-text' `
        -Arguments @('ui','set-value','LibraryNamingTextParameter',$prefixText) -Code 'library.naming_rule'
    $prefixReadBack = Read-LibraryValue -AutomationId 'LibraryNamingTextParameter' -Code 'library.naming_rule'
    if ($prefixReadBack -cne $prefixText) { throw 'library.naming_rule' }

    # A preset is saved so that the undo check can show presets are unaffected by an undo.
    $presetBefore = @(Read-LibraryNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'LibraryNamingPresetList' }).Count
    if ($presetBefore -lt 1) { throw 'library.preset_missing' }
    $null = Invoke-LibraryAction -Step 'naming-preset-name' `
        -Arguments @('ui','set-value','LibraryNamingPresetNameInput','acc') -Code 'library.naming_rule'
    $null = Invoke-LibraryAction -Step 'naming-preset-save' `
        -Arguments @('ui','invoke','LibraryNamingSavePresetButton') -Code 'library.naming_rule'

    Select-LibraryOption -ComboBoxId 'LibraryNamingRuleKindSelector' -OptionName $sequenceRuleOption `
        -Step 'naming-kind-sequence' -Code 'library.naming_rule'
    $null = Invoke-LibraryAction -Step 'naming-add-sequence' -Arguments @('ui','invoke','LibraryNamingAddRuleButton') -Code 'library.naming_rule'
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryNamingSequenceStartParameter') -Seconds $LibraryWaitSeconds)) {
        throw 'library.naming_rule'
    }

    # The preview is the product's own projection of the rule set onto the selection.
    $previewNames = Wait-LibraryPreview -Prefix $prefixText -WithDigit $true -Seconds $LibraryWaitSeconds
    if ($previewNames.Count -ne 3) {
        $artifacts.Add((Save-LibraryEvidence -EvidenceId 'library-naming-preview-rows' -Nodes (Read-LibraryNodes)))
        throw 'library.preview'
    }
    $previewTree = Save-LibraryEvidence -EvidenceId 'library-naming-preview' -Nodes (Read-LibraryNodes)
    $artifacts.Add($previewTree)
    $null = Save-LibraryScreenshot -Selector 'LibraryBatchEditDialog' -Name 'library-naming-preview'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'library-preview-names.json'),
        (ConvertTo-Json -InputObject @($previewNames) -Depth 3), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('library-preview-names.json')

    # --- a disabled rule row must not contribute to the preview ---
    # The sequence rule is addressed by the label its row renders, never by its position.
    $sequenceEnabledId = Get-LibraryRuleElementId -RuleLabel $sequenceRuleOption -Suffix 'Enabled'
    $null = Invoke-LibraryAction -Step 'naming-disable-sequence' -Arguments @('ui','invoke',$sequenceEnabledId) -Code 'library.rule_disabled'
    $disabledPreview = Wait-LibraryPreview -Prefix $prefixText -WithDigit $false -Seconds $LibraryWaitSeconds
    if ($disabledPreview.Count -ne 3) {
        $artifacts.Add((Save-LibraryEvidence -EvidenceId 'library-rule-disabled' -Nodes (Read-LibraryNodes)))
        throw ('library.rule_preview.' + [string] $disabledPreview.Count)
    }
    $null = Invoke-LibraryAction -Step 'naming-enable-sequence' -Arguments @('ui','invoke',$sequenceEnabledId) -Code 'library.rule_disabled'
    # The sequence must come back, so the disabled-rule check cannot be read as a lost rule.
    if ((Wait-LibraryPreview -Prefix $prefixText -WithDigit $true -Seconds $LibraryWaitSeconds).Count -ne 3) {
        $artifacts.Add((Save-LibraryEvidence -EvidenceId 'library-rule-restored' -Nodes (Read-LibraryNodes)))
        throw 'library.rule_restore'
    }

    # --- an invalid draft must be refused, and never applied ---
    Select-LibraryOption -ComboBoxId 'LibraryNamingRuleKindSelector' -OptionName $replaceRuleOption `
        -Step 'naming-kind-replace' -Code 'library.invalid_draft'
    $null = Invoke-LibraryAction -Step 'naming-add-replace' -Arguments @('ui','invoke','LibraryNamingAddRuleButton') -Code 'library.invalid_draft'
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryNamingSearchParameter') -Seconds $LibraryWaitSeconds)) {
        throw 'library.invalid_draft'
    }
    $invalidNodes = Read-LibraryNodes
    $invalidApply = Find-UiAcceptanceUniqueNode -Nodes $invalidNodes -AutomationId 'LibraryBatchApplyButton' -FailureCode 'library.invalid_draft'
    if ($null -eq $invalidApply) { throw 'library.invalid_draft' }
    if ((Get-UiAcceptanceNodeProperty -Node $invalidApply -Name 'isEnabled') -ceq 'True') {
        throw 'batch.invalid-draft'
    }
    $invalidStatus = Read-LibraryValue -AutomationId 'LibraryBatchDialogStatus' -Code 'library.invalid_draft'
    $invalidTree = Save-LibraryEvidence -EvidenceId 'library-invalid-draft' -Nodes $invalidNodes
    $artifacts.Add($invalidTree)
    $invalidState = Read-LibraryState -EvidenceId 'library-invalid-draft'
    foreach ($name in $fixtures) {
        $facts = Get-AssetFacts -State $invalidState -Filename $name -Code 'library.asset_missing'
        if ([string] $facts.DisplayName -cne [string] $metadataFacts[$name].DisplayName) {
            throw 'library.invalid_draft'
        }
    }
    # The offending rule is removed so the valid draft can be applied. It is identified by the label
    # it renders, because the rows are rendered in the reverse order of the rule set.
    $replaceDeleteId = Get-LibraryRuleElementId -RuleLabel $replaceRuleOption -Suffix 'Delete'
    $null = Invoke-LibraryAction -Step 'naming-drop-replace' `
        -Arguments @('ui','invoke',$replaceDeleteId) -Code 'library.invalid_draft'

    if (-not (Test-LibraryEnabled -AutomationId 'LibraryBatchApplyButton' -Seconds $LibraryWaitSeconds)) {
        throw 'library.naming_apply_ready'
    }
    $null = Invoke-LibraryAction -Step 'naming-apply' -Arguments @('ui','invoke','LibraryBatchApplyButton') -Code 'library.naming_apply'
    if (-not (Wait-LibraryGone -Arguments @('ui','wait-for','LibraryBatchApplyButton','--gone') -Seconds $LibraryWaitSeconds)) {
        throw 'library.naming_completion'
    }

    $namedState = Read-LibraryState -EvidenceId 'library-named'
    foreach ($name in $fixtures) {
        $facts = Get-AssetFacts -State $namedState -Filename $name -Code 'library.asset_missing'
        if ($facts.AssetKey -cne $metadataFacts[$name].AssetKey) { throw 'library.record_identity' }
        if ([string]::IsNullOrEmpty($facts.DisplayName)) { throw 'library.display_name' }
        if ($facts.DisplayName -notlike ($prefixText + '*')) { throw 'library.display_name' }
    }
    $namedDisplayNames = @{}
    foreach ($name in $fixtures) {
        $namedDisplayNames[$name] = (Get-AssetFacts -State $namedState -Filename $name -Code 'library.asset_missing').DisplayName
    }
    # The physical files must be untouched: same names, same bytes.
    foreach ($file in (Read-LibraryFormalFiles -LibraryDirectory $folders['library'])) {
        $expected = $verified[$file.Name]
        if ($null -eq $expected -or $file.Hash -cne $expected.Sha256) { throw 'library.file_changed' }
    }

    # --- one undo restores the exact prior names, metadata and presets ---
    if (-not (Test-LibraryEnabled -AutomationId 'LibraryBatchUndoButton' -Seconds $LibraryWaitSeconds)) {
        throw 'library.undo_unavailable'
    }
    $null = Invoke-LibraryAction -Step 'undo' -Arguments @('ui','invoke','LibraryBatchUndoButton') -Code 'library.undo'
    if (-not (Wait-LibraryGone -Arguments @('ui','wait-for','LibraryBatchUndoButton','--gone') -Seconds $LibraryWaitSeconds)) {
        throw 'library.undo_incomplete'
    }

    $undoneState = Read-LibraryState -EvidenceId 'library-undone'
    foreach ($name in $fixtures) {
        $facts = Get-AssetFacts -State $undoneState -Filename $name -Code 'library.asset_missing'
        if ([string] $facts.DisplayName -cne [string] $metadataFacts[$name].DisplayName) { throw 'library.display_name' }
        if ((@($facts.Authors) -join '|') -cne (@($metadataFacts[$name].Authors) -join '|')) { throw 'library.prior_values' }
        if ((@($facts.Tags) -join '|') -cne (@($metadataFacts[$name].Tags) -join '|')) { throw 'library.prior_values' }
        if ([string] $facts.Series -cne [string] $metadataFacts[$name].Series) { throw 'library.prior_values' }
        if ($facts.AssetKey -cne $metadataFacts[$name].AssetKey) { throw 'library.record_identity' }
    }
    foreach ($file in (Read-LibraryFormalFiles -LibraryDirectory $folders['library'])) {
        $expected = $verified[$file.Name]
        if ($null -eq $expected -or $file.Hash -cne $expected.Sha256) { throw 'library.file_changed' }
    }

    # Presets belong to the editor, not to the assets, so an undo must leave them alone.
    $null = Invoke-LibraryAction -Step 'verify-reopen' -Arguments @('ui','invoke','NavLibrary') -Code 'library.preset'
    Select-AllLibraryRows -Step 'select-all-again' -Expected '3'
    $null = Invoke-LibraryAction -Step 'verify-open' -Arguments @('ui','invoke','LibraryBatchEditButton') -Code 'library.preset'
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryBatchEditDialog') -Seconds $LibraryWaitSeconds)) {
        throw 'library.preset'
    }
    $null = Invoke-LibraryAction -Step 'verify-tab' -Arguments @('ui','invoke','LibraryBatchNamingTab') -Code 'library.preset'
    # The preset saved before the apply must still be listed after the undo: a preset belongs to
    # the editor, so restoring asset state must not remove it.
    $presetNames = @(Read-LibraryNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq 'acc' } |
        ForEach-Object { Get-UiAcceptanceNodeProperty -Node $_ -Name 'name' } | Select-Object -Unique)
    if ($presetNames.Count -lt 1) { throw 'library.preset_changed' }
    $cancelNodes = Read-LibraryNodes
    $cancel = Find-UiAcceptanceUniqueNode -Nodes $cancelNodes -AutomationId 'LibraryBatchCancelButton' -FailureCode 'library.preset'
    if ($null -eq $cancel) { throw 'library.preset' }
    $null = Invoke-LibraryAction -Step 'verify-cancel' -Arguments @('ui','invoke','LibraryBatchCancelButton') -Code 'library.preset'

    return [pscustomobject] @{
        ScenarioId       = $ScenarioId
        Status           = 'Passed'
        Reason           = ''
        IntersectionRow  = [string] $intersection[0]
        ActiveFilters    = [string] $finalChips
        SearchValue      = $finalSearch
        # The conditions the single visible row is the intersection of, with the search named
        # separately because the product does not render it as an applied-filter chip.
        FinalConditions  = @('search', 'kind', 'author', 'series', 'tag')
        PreviewNames     = @($previewNames)
        NamedDisplayNames = @($namedDisplayNames.Values)
        UndoneDisplayNames = @($fixtures | ForEach-Object {
                (Get-AssetFacts -State $undoneState -Filename $_ -Code 'library.asset_missing').DisplayName })
        InvalidStatus    = $invalidStatus
        Artifacts        = @($artifacts)
    }
}
catch {
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or
        ($code -notlike 'library.*' -and $code -notlike 'batch.*')) {
        $cause = ConvertTo-UiAcceptanceSlug -Value ([string] $code)
        if ($cause.Length -gt 120) { $cause = $cause.Substring(0, 120) }
        $code = 'library.unexpected'
        if (-not [string]::IsNullOrWhiteSpace($cause)) { $code = $code + '.' + $cause }
    }
    $status = if ($blockedFailureCodes -ccontains $code) { 'Blocked' } else { 'Failed' }
    return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = $status; Reason = $code; Artifacts = @($artifacts) }
}
