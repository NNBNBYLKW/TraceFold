# TraceFold Stage 5CC - aggregate entry for the 'Library' registry item.
#
# The Library aggregate is a composition of the two library slices, each of which is its own
# scenario and its own proof:
#   BatchMetadataUndo    -> 30-batch-metadata-undo.ps1         (batch editor metadata tab + undo)
#   LibraryFilterNaming  -> 40-library-filter-naming-undo.ps1  (filters, sorting, naming tab)
#
# The two slices already fence each other off in their own headers: 30 does not touch filters or
# naming and 40 does not use the metadata tab even for setup. This entry does not merge them and
# does not treat either as standing in for the other - it runs both, independently rooted, and
# reports both.
#
# This entry starts no product instance. Each slice is dispatched through the public entry as a
# separate process with a single named '-Scenario' value. A failing slice stays visible in
# aggregate.json and sets the composed status; the other slice passing never clears it.
#
# Integrated at scripts/ui-acceptance/scenarios/03-library.ps1; it has not yet been run against
# the real product as a composed aggregate.

param(
    [Parameter(Mandatory)][string] $ScenarioId,
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $ContractPath,
    [Parameter(Mandatory)][string] $OwnedRoot,
    [Parameter(Mandatory)][string] $RunToken,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][string] $SupportPath,
    [Parameter(Mandatory)][string] $WinAppPath,
    # The public entry hands the pinned driver archive path to an entry that declares it (the
    # one-line pass-through applied with the integration). Without it the aggregate reports
    # Blocked and runs nothing.
    [string] $WinAppCliArchivePath = '',
    # Overridable so a regression can point at a controlled substitute entry; defaults to the
    # public entry of the tree this file lives in (scripts/run-stage5cc-ui-acceptance.ps1).
    [string] $EntryScriptPath = '',
    [string] $EnginePath = '',
    [int] $TimeoutSeconds = 3600,
    [scriptblock] $InvokeChild,
    # Provenance seams. The aggregate binds every child to the source commit and the pinned driver
    # version of the tree this run is executing; both can be supplied directly, and the commit can
    # be supplied through a resolver, which is what a regression injects instead of spawning git.
    [string] $ExpectedSourceCommit = '',
    [scriptblock] $ResolveSourceCommit,
    [string] $ExpectedDriverVersion = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The engine sits one directory up after integration; the sibling lookup above still serves a
# candidate-layout copy.
$engineModuleName = 'TraceFold.UiAcceptance.Aggregate.psm1'
$engineModulePath = Join-Path $PSScriptRoot $engineModuleName
if (-not [IO.File]::Exists($engineModulePath)) {
    $engineModulePath = Join-Path (Split-Path -Parent $PSScriptRoot) $engineModuleName
}
if (-not [IO.File]::Exists($engineModulePath)) {
    throw ('aggregate_module_missing: ' + $engineModuleName)
}
Import-Module $engineModulePath -Force -DisableNameChecking

# Both library slices, in the registry's own order.
$childScenarios = @('BatchMetadataUndo', 'LibraryFilterNaming')

$entry = $EntryScriptPath
if ([string]::IsNullOrWhiteSpace($entry)) {
    $scriptsRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $entry = Join-Path $scriptsRoot 'run-stage5cc-ui-acceptance.ps1'
}

return Invoke-UiAggregateComposition -AggregateId 'Library' -ChildScenarios $childScenarios `
    -ExecutablePath $ExecutablePath -OutputDirectory $OutputDirectory -OwnedRoot $OwnedRoot `
    -RunToken $RunToken -WinAppCliArchivePath $WinAppCliArchivePath -EntryScriptPath $entry `
    -EnginePath $EnginePath -TimeoutSeconds $TimeoutSeconds -InvokeChild $InvokeChild `
    -ExpectedSourceCommit $ExpectedSourceCommit -ResolveSourceCommit $ResolveSourceCommit `
    -ExpectedDriverVersion $ExpectedDriverVersion
