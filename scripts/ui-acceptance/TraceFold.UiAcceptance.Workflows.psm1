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
    Flattens one driver inspection response into a depth-first list of nodes.

.DESCRIPTION
    The pinned driver answers with windows[].elements[] carrying nested children. A response that
    is not that shape fails with the caller's schema code rather than being coerced.
#>
function ConvertTo-UiAcceptanceFlatNodes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Tree,
        [Parameter(Mandatory)][string] $FailureCode
    )

    if ($null -eq $Tree -or $null -eq $Tree.PSObject.Properties['windows']) { throw $FailureCode }
    $nodes = [System.Collections.Generic.List[object]]::new()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    foreach ($window in @($Tree.windows)) {
        if ($null -eq $window.PSObject.Properties['elements']) { throw $FailureCode }
        foreach ($element in @($window.elements)) { $pending.Push($element) }
    }
    while ($pending.Count -gt 0) {
        $node = $pending.Pop()
        $nodes.Add($node)
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
        # Blocked or Failed; either way nothing was dispatched, so resolving again is safe. The code
        # is the criterion, never the status, so a genuinely failed dispatch is never mistaken for a
        # miss and never resent. A response with no parsed payload at all carries no code and is
        # therefore returned to the caller.
        $missCode = ''
        if ($null -ne $result.Data -and $null -ne $result.Data.PSObject.Properties['error']) {
            $missCode = Get-UiAcceptanceNodeProperty -Node $result.Data.error -Name 'code'
        }
        if ($missCode -ceq $script:uiAcceptanceLookupMissCode) {
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
        [Parameter(Mandatory)][string] $OuterSelector,
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
            -Arguments @('ui','send-keys','enter','--target',$OuterSelector) -ProcessId $ProcessId `
            -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck -DesktopCheck $DesktopCheck `
            -TimeoutMs $TimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot)
}

Export-ModuleMember -Function @(
    'Get-UiAcceptanceNodeProperty',
    'Get-UiAcceptanceNodeChildren',
    'ConvertTo-UiAcceptanceFlatNodes',
    'Read-UiAcceptanceTree',
    'Find-UiAcceptanceUniqueNode',
    'Get-UiAcceptanceNodeSelector',
    'Get-UiAcceptanceDescendantSelector',
    'Invoke-UiAcceptanceResolvedMutation',
    'Test-UiAcceptanceFixtureAsset',
    'Set-UiAcceptanceTaxonomyTerm'
)
