# TraceFold UI acceptance - reusable workflow helpers.
#
# These helpers encode the interaction rules every scenario needs, so a scenario states the
# workflow and not the driver mechanics:
#
#   * A dynamic element has no stable AutomationId. Its address is the semantic selector the
#     driver returned in the very inspection that observed it, and that selector is used
#     immediately - never cached across observations, never derived from a GUID.
#   * A composite control (a stable outer id whose real value lives on a descendant) is addressed
#     through that descendant, resolved from the same fresh tree.
#   * A UI mutation is dispatched once. Repeating a dispatch is never a retry strategy: the only
#     tolerated reattempt is one where the driver reported a lookup-stage miss, because the action
#     was not executed. A timeout, a Blocked result or any other failure is reported, not resent.
#
# All helpers are read-only apart from the two that call the driver's mutation entry points.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Contracts.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Driver.psm1') -DisableNameChecking

# A lookup-stage miss means the driver never dispatched the action, so a re-resolve is safe.
$script:uiAcceptanceLookupMissCode = 'element_not_found'
# Some lookup paths answer with a generic code whose message names the missing slug instead. That is
# still a lookup-stage miss, and matching it keeps the retry rule precise to lookup failures.
$script:uiAcceptanceLookupMissMessage = '(?i)element with slug'

<#
.SYNOPSIS
    Reads the driver property of a node without assuming it exists.

.DESCRIPTION
    The driver omits optional fields (automationId, name, children) on nodes that have none, so a
    direct property access under strict mode would fail on a legitimate response.
#>
function Get-UiAcceptanceNodeProperty {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $Node,
        [Parameter(Mandatory)][string] $Name
    )

    if ($null -eq $Node) { return '' }
    $property = $Node.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return '' }
    return [string] $property.Value
}

function Get-UiAcceptanceNodeChildren {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $Node)

    if ($null -eq $Node -or $null -eq $Node.PSObject.Properties['children']) { return @() }
    return @($Node.children)
}

<#
.SYNOPSIS
    Builds the fully observed identity of one node, for collapsing repeated reports.

.DESCRIPTION
    Identity covers every field the driver reports for an element, so it answers one question only:
    is this report indistinguishable from one already seen. A field the driver omits reads as empty,
    which is correct here because two nodes that merely both omit it are still told apart by the
    fields they do carry.
#>
function Get-UiAcceptanceNodeIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $Node)

    $fields = @('selector','type','automationId','className','name','isEnabled','isOffscreen',
        'x','y','width','height')
    $parts = foreach ($field in $fields) { Get-UiAcceptanceNodeProperty -Node $Node -Name $field }
    return ($parts -join [string] [char] 31)
}

<#
.SYNOPSIS
    Flattens one driver inspection response into a depth-first list of nodes.

.DESCRIPTION
    The pinned driver answers with windows[].elements[] carrying nested children. A response that
    is not that shape fails with the caller's schema code rather than being coerced.

    While a pop-up is open the driver reports some of the same elements more than once, always with
    an identical slug and identical bounds. A repeat is one element seen twice, so the flat list
    carries it once. The collapse is by full observed identity rather than by slug alone, so two
    elements that happen to share a slug but differ anywhere the driver reports are both kept and
    still fail the callers that require a unique match.
#>
function ConvertTo-UiAcceptanceFlatNodes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Tree,
        [Parameter(Mandatory)][string] $FailureCode
    )

    if ($null -eq $Tree -or $null -eq $Tree.PSObject.Properties['windows']) { throw $FailureCode }
    $nodes = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    foreach ($window in @($Tree.windows)) {
        if ($null -eq $window.PSObject.Properties['elements']) { throw $FailureCode }
        foreach ($element in @($window.elements)) { $pending.Push($element) }
    }
    while ($pending.Count -gt 0) {
        $node = $pending.Pop()
        if ($seen.Add((Get-UiAcceptanceNodeIdentity -Node $node))) { $nodes.Add($node) }
        foreach ($child in (Get-UiAcceptanceNodeChildren -Node $node)) { $pending.Push($child) }
    }
    return $nodes.ToArray()
}

<#
.SYNOPSIS
    Inspects the launched window and returns its flattened nodes.
#>
function Read-UiAcceptanceTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $WinAppPath,
        [Parameter(Mandatory)][string] $RunToken,
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $FailureCode,
        [int] $Depth = 20,
        [int] $TimeoutSeconds = 20
    )

    $observation = Invoke-WinAppObservation -Arguments @('ui','inspect','--depth',[string] $Depth) `
        -ProcessId $ProcessId -TimeoutSeconds $TimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed') { throw $FailureCode }
    return (ConvertTo-UiAcceptanceFlatNodes -Tree $observation.Data -FailureCode 'ui.driver_schema_incompatible')
}

<#
.SYNOPSIS
    Returns every node of a native type whose accessible name is exactly the stated name.

.DESCRIPTION
    The name is compared whole rather than as a substring, and the native type is part of the match,
    so a control that merely mentions the name, or a same-named element of another type, is never
    returned. Elements the product reports off screen can be excluded, which is what a caller needs
    when a collapsed container keeps the controls it had already realised in the tree: without that
    filter a term that was deselected still reads as if it were still selected.
#>
function Get-UiAcceptanceNamedNodes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Nodes,
        [Parameter(Mandatory)][string] $NativeType,
        [Parameter(Mandatory)][AllowEmptyString()][string] $Name,
        [switch] $VisibleOnly
    )

    $matches = @($Nodes | Where-Object {
            $node = $_
            $ok = ((Get-UiAcceptanceNodeProperty -Node $node -Name 'type') -ceq $NativeType) -and
                ((Get-UiAcceptanceNodeProperty -Node $node -Name 'name') -ceq $Name)
            if ($VisibleOnly) {
                $ok = $ok -and ((Get-UiAcceptanceNodeProperty -Node $node -Name 'isOffscreen') -ceq 'False')
            }
            $ok
        })
    return ,$matches
}

<#
.SYNOPSIS
    Collects the rows one list container shows, in the order it renders them.

.DESCRIPTION
    Rows are the list items beneath the named container whose accessible name starts with the prefix
    the caller states. The container is resolved by its own stable id and the walk stays inside it, so
    a list item belonging to a header, a navigation pane or another list can never be read as a row of
    this list. Elements the product reports off screen are left out, because a container whose pop-up
    has been dismissed keeps what it had already realised in the tree. Repeats are kept: dropping a
    duplicate name would make two rows look like one, which is exactly what a count is meant to catch.
#>
function Get-UiAcceptanceListRowNames {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Nodes,
        [Parameter(Mandatory)][string] $ContainerId,
        [Parameter(Mandatory)][AllowEmptyString()][string] $NamePrefix,
        [Parameter(Mandatory)][string] $FailureCode
    )

    $container = Find-UiAcceptanceUniqueNode -Nodes $Nodes -AutomationId $ContainerId `
        -FailureCode $FailureCode
    if ($null -eq $container) { throw $FailureCode }
    $names = [System.Collections.Generic.List[string]]::new()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    # Children are pushed in reverse so that the walk visits them in the order they are rendered.
    $children = @(Get-UiAcceptanceNodeChildren -Node $container)
    for ($index = $children.Count - 1; $index -ge 0; $index--) { $pending.Push($children[$index]) }
    while ($pending.Count -gt 0) {
        $node = $pending.Pop()
        $name = Get-UiAcceptanceNodeProperty -Node $node -Name 'name'
        if ((Get-UiAcceptanceNodeProperty -Node $node -Name 'type') -ceq 'ListItem' -and
            (Get-UiAcceptanceNodeProperty -Node $node -Name 'isOffscreen') -ceq 'False' -and
            $name.StartsWith($NamePrefix, [StringComparison]::Ordinal)) {
            $names.Add($name)
        }
        $childNodes = @(Get-UiAcceptanceNodeChildren -Node $node)
        for ($index = $childNodes.Count - 1; $index -ge 0; $index--) { $pending.Push($childNodes[$index]) }
    }
    return ,@($names.ToArray())
}

<#
.SYNOPSIS
    Compares two row collections as multisets, so order does not matter but repeats do.

.DESCRIPTION
    A row set is compared without regard to the order the rows were collected in, because the same set
    may be read in a different order; it is not compared without regard to how many times a row
    appears, because two identical rows must never compare equal to one. The comparison sorts both
    sides ordinally, so its answer does not depend on the culture of the machine or on the PowerShell
    version it runs on.
#>
function Test-UiAcceptanceRowSetMatch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Observed,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Expected
    )

    $observedSorted = [System.Collections.Generic.List[string]]::new()
    foreach ($value in @($Observed)) { $observedSorted.Add([string] $value) }
    $expectedSorted = [System.Collections.Generic.List[string]]::new()
    foreach ($value in @($Expected)) { $expectedSorted.Add([string] $value) }
    $observedSorted.Sort([System.StringComparer]::Ordinal)
    $expectedSorted.Sort([System.StringComparer]::Ordinal)
    if ($observedSorted.Count -ne $expectedSorted.Count) { return $false }
    for ($index = 0; $index -lt $observedSorted.Count; $index++) {
        if ($observedSorted[$index] -cne $expectedSorted[$index]) { return $false }
    }
    return $true
}

<#
.SYNOPSIS
    Finds the one node matching every supplied criterion.

.DESCRIPTION
    Returns $null when nothing matches. Throws when more than one matches, because picking one of
    several candidates would silently assert against the wrong element. The criterion set is
    deliberately explicit: a caller states the native type and the accessible-name prefix, so a
    same-named Group, TextBlock or generated-id container can never be mistaken for the row.
#>
function Find-UiAcceptanceUniqueNode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Nodes,
        [string] $NativeType,
        [string] $AutomationId,
        [string] $NamePrefix,
        [string] $NameContains,
        [Parameter(Mandatory)][string] $FailureCode
    )

    $matches = @($Nodes | Where-Object {
        $node = $_
        $ok = $true
        if (-not [string]::IsNullOrEmpty($NativeType)) {
            $ok = $ok -and ((Get-UiAcceptanceNodeProperty -Node $node -Name 'type') -ceq $NativeType)
        }
        if (-not [string]::IsNullOrEmpty($AutomationId)) {
            $ok = $ok -and ((Get-UiAcceptanceNodeProperty -Node $node -Name 'automationId') -ceq $AutomationId)
        }
        if (-not [string]::IsNullOrEmpty($NamePrefix)) {
            $name = Get-UiAcceptanceNodeProperty -Node $node -Name 'name'
            $ok = $ok -and $name.StartsWith($NamePrefix, [StringComparison]::Ordinal)
        }
        if (-not [string]::IsNullOrEmpty($NameContains)) {
            $name = Get-UiAcceptanceNodeProperty -Node $node -Name 'name'
            $ok = $ok -and $name.Contains($NameContains)
        }
        $ok
    })

    if ($matches.Count -eq 0) { return $null }
    if ($matches.Count -gt 1) { throw ($FailureCode + '.ambiguous') }
    return $matches[0]
}

<#
.SYNOPSIS
    Returns the semantic selector the driver reported for a node in the inspection that observed it.
#>
function Get-UiAcceptanceNodeSelector {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $Node,
        [Parameter(Mandatory)][string] $FailureCode
    )

    $selector = Get-UiAcceptanceNodeProperty -Node $Node -Name 'selector'
    if ([string]::IsNullOrWhiteSpace($selector) -or $selector -match '[\{\}]') { throw $FailureCode }
    return $selector
}

<#
.SYNOPSIS
    Resolves the unique descendant of a class beneath an observed node, for a composite control.

.DESCRIPTION
    Used where the stable id belongs to an outer container whose real value lives on an inner
    control. The descendant is scoped to the exact outer node that this inspection returned, so a
    same-named control elsewhere in the window can never be picked up.
#>
function Get-UiAcceptanceDescendantSelector {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $Node,
        [Parameter(Mandatory)][string] $NativeType,
        [string] $ClassName,
        [Parameter(Mandatory)][string] $FailureCode
    )

    $candidates = @(Get-UiAcceptanceNodeChildren -Node $Node | Where-Object {
        $child = $_
        $ok = (Get-UiAcceptanceNodeProperty -Node $child -Name 'type') -ceq $NativeType
        if (-not [string]::IsNullOrEmpty($ClassName)) {
            $ok = $ok -and ((Get-UiAcceptanceNodeProperty -Node $child -Name 'className') -ceq $ClassName)
        }
        $ok
    })
    if ($candidates.Count -ne 1) { throw $FailureCode }
    return (Get-UiAcceptanceNodeSelector -Node $candidates[0] -FailureCode $FailureCode)
}

<#
.SYNOPSIS
    Dispatches a UI mutation once, re-resolving a stale selector only when nothing was dispatched.

.DESCRIPTION
    The resolver is a script block the caller supplies; it re-observes the window and returns a
    fresh selector. Each dispatch attempt carries its own step id, because the toolkit records a
    step once and a repeated id is itself a defect.

    A lookup-stage miss (the driver's element_not_found) means the action was never executed, so
    the loop may resolve again within the bounded attempt budget and deadline. Every other outcome
    - a timeout, a blocked result, or a failure with any other cause - is returned to the caller
    unchanged and never resent.
#>
function Invoke-UiAcceptanceResolvedMutation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $StepId,
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $WinAppPath,
        [Parameter(Mandatory)][string] $RunToken,
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $Verb,
        [Parameter(Mandatory)][scriptblock] $ResolveSelector,
        [Parameter(Mandatory)][string] $FailureCode,
        [string[]] $TrailingArguments = @(),
        [int] $MaxAttempts = 3,
        [int] $DeadlineSeconds = 30,
        [int] $TimeoutMs = 5000
    )

    if ($Verb -cnotin @('invoke','set-value','scroll-into-view')) { throw 'Unsupported resolved mutation verb.' }
    $deadline = [DateTime]::UtcNow.AddSeconds($DeadlineSeconds)
    $attempt = 0
    while ($attempt -lt $MaxAttempts -and [DateTime]::UtcNow -lt $deadline) {
        $attempt++
        $selector = & $ResolveSelector
        if ([string]::IsNullOrWhiteSpace($selector)) { throw $FailureCode }
        $arguments = @('ui', $Verb, $selector) + @($TrailingArguments)
        $result = Invoke-WinAppMutation -StepId ($StepId + '.dispatch' + $attempt) -Arguments $arguments `
            -ProcessId $ProcessId -WinAppPath $WinAppPath -TimeoutMs $TimeoutMs `
            -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($result.Status -ceq 'Passed') { return $result }
        if ($result.TimedOut) { return $result }
        # The driver reports a lookup-stage miss with a distinct error code and may classify it as
        # Blocked or Failed; either way nothing was dispatched, so resolving again is safe. Some
        # lookup paths surface it as a generic internal error whose message names the missing slug,
        # which is still a lookup-stage miss and not a dispatched action. The code or that message is
        # the criterion, never the status, so a genuinely failed dispatch is never mistaken for a
        # miss and never resent. A response with no parsed payload carries no code and is returned.
        $missCode = ''
        $missMessage = ''
        if ($null -ne $result.Data -and $null -ne $result.Data.PSObject.Properties['error']) {
            $missCode = Get-UiAcceptanceNodeProperty -Node $result.Data.error -Name 'code'
            $missMessage = Get-UiAcceptanceNodeProperty -Node $result.Data.error -Name 'message'
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string] $result.Error)) {
            $missMessage = [string] $result.Error
        }
        if ($missCode -ceq $script:uiAcceptanceLookupMissCode -or
            $missMessage -match $script:uiAcceptanceLookupMissMessage) {
            continue
        }
        return $result
    }
    throw $FailureCode
}

<#
.SYNOPSIS
    Verifies a fixture against the manifest and returns its verified bytes metadata.

.DESCRIPTION
    Nothing is copied here: a caller that wants the file in the owned inbox calls this first and
    only then writes. Size and SHA-256 must both match, and the source must live under the
    repository, so an altered or substituted fixture can never enter a run.
#>
function Test-UiAcceptanceFixtureAsset {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Asset,
        [Parameter(Mandatory)][string] $RepositoryRoot
    )

    Assert-UiAcceptanceFields -InputObject $Asset -Required @('fixedFilename','repositoryPath','sha256','sizeBytes','seedDirectory')
    $source = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot ([string] $Asset.repositoryPath)))
    $boundary = $RepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $source.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'ui.fixture_target' }
    if ([IO.Path]::GetFileName([string] $Asset.fixedFilename) -cne [string] $Asset.fixedFilename) { throw 'ui.fixture_target' }
    if (-not [IO.File]::Exists($source)) { throw 'ui.fixture_missing' }
    if ((Get-Item -LiteralPath $source).Length -ne [long] $Asset.sizeBytes) { throw 'ui.fixture_mismatch' }
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ($hash -cne ([string] $Asset.sha256).ToUpperInvariant()) { throw 'ui.fixture_mismatch' }
    return [pscustomobject] @{ Source = $source; Sha256 = $hash; SizeBytes = [long] $Asset.sizeBytes }
}

<#
.SYNOPSIS
    Commits one taxonomy token into a composite picker.

.DESCRIPTION
    The picker's stable id belongs to the outer AutoSuggestBox, which has no Value pattern; the
    value lives on its unique inner Edit, which does. The token is written through that inner
    editor and committed with a single Enter, each dispatch carrying its own step id. The caller
    supplies the observation-and-resolve step, because only it knows which tree is current.
#>
function Set-UiAcceptanceTaxonomyTerm {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $StepId,
        [Parameter(Mandatory)][string] $InnerSelector,
        [Parameter(Mandatory)][string] $Term,
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $WinAppPath,
        [Parameter(Mandatory)][string] $RunToken,
        [Parameter(Mandatory)][string] $OwnedRoot,
        [scriptblock] $ForegroundCheck = { param($id) Test-UiForegroundProcess -ProcessId $id },
        [scriptblock] $DesktopCheck = { Test-UiInteractiveDesktop },
        [Parameter(Mandatory)][string] $FailureCode,
        [int] $TimeoutMs = 5000
    )

    $written = Invoke-WinAppMutation -StepId ($StepId + '.value') `
        -Arguments @('ui','set-value',$InnerSelector,$Term) -ProcessId $ProcessId `
        -WinAppPath $WinAppPath -TimeoutMs $TimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($written.Status -cne 'Passed') { return $written }

    # A single Enter commits the current query; it is never re-sent.
    return (Invoke-WinAppKeyboard -StepId ($StepId + '.commit') `
            -Arguments @('ui','send-keys','enter','--target',$InnerSelector) -ProcessId $ProcessId `
            -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck -DesktopCheck $DesktopCheck `
            -TimeoutMs $TimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot)
}

Export-ModuleMember -Function @(
    'Get-UiAcceptanceNodeProperty',
    'Get-UiAcceptanceNodeChildren',
    'ConvertTo-UiAcceptanceFlatNodes',
    'Read-UiAcceptanceTree',
    'Get-UiAcceptanceNamedNodes',
    'Get-UiAcceptanceListRowNames',
    'Test-UiAcceptanceRowSetMatch',
    'Find-UiAcceptanceUniqueNode',
    'Get-UiAcceptanceNodeSelector',
    'Get-UiAcceptanceDescendantSelector',
    'Invoke-UiAcceptanceResolvedMutation',
    'Test-UiAcceptanceFixtureAsset',
    'Set-UiAcceptanceTaxonomyTerm'
)
