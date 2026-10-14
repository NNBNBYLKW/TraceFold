# TraceFold Stage 5CC - aggregate entry for the 'Conflict' registry item.
#
# The Conflict aggregate is a composition of the three conflict decisions, each of which is its own
# scenario and its own proof:
#   ConflictKeepBoth -> 20-conflict-keep-both.ps1
#   ConflictReplace  -> 21-conflict-replace.ps1
#   ConflictSkip     -> 22-conflict-skip.ps1
#
# This entry starts no product instance. It dispatches each decision through the public entry as a
# separate process with a single named '-Scenario' value, so each one gets its own owned root, its
# own output, its own report and its own cleanup, and it re-reads what each child actually produced
# before composing a verdict. A failing decision stays visible in aggregate.json and sets the
# composed status; a later decision passing never clears it.
#
# Integrated at scripts/ui-acceptance/scenarios/02-conflict.ps1; it has not yet been run against
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

# Every conflict decision, in the registry's own order. None of them is an alias for another: the
# aggregate is the three of them together, and each keeps its own result.
$childScenarios = @('ConflictKeepBoth', 'ConflictReplace', 'ConflictSkip')

$entry = $EntryScriptPath
if ([string]::IsNullOrWhiteSpace($entry)) {
    $scriptsRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $entry = Join-Path $scriptsRoot 'run-stage5cc-ui-acceptance.ps1'
}

return Invoke-UiAggregateComposition -AggregateId 'Conflict' -ChildScenarios $childScenarios `
    -ExecutablePath $ExecutablePath -OutputDirectory $OutputDirectory -OwnedRoot $OwnedRoot `
    -RunToken $RunToken -WinAppCliArchivePath $WinAppCliArchivePath -EntryScriptPath $entry `
    -EnginePath $EnginePath -TimeoutSeconds $TimeoutSeconds -InvokeChild $InvokeChild `
    -ExpectedSourceCommit $ExpectedSourceCommit -ResolveSourceCommit $ResolveSourceCommit `
    -ExpectedDriverVersion $ExpectedDriverVersion
