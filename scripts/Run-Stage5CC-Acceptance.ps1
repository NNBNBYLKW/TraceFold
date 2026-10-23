# TraceFold Stage 5C-C - package-root acceptance launcher helper.
#
# The package-root Run-Stage5CC-Acceptance.cmd starts this file with the system Windows
# PowerShell 5.1. It resolves the package from its own location, finds the sibling product or the
# explicit one, chooses a fresh results leaf and starts the public entry with the real arguments.
# It never searches the machine, builds, downloads or changes the caller's working directory.
#
# Exit codes:
#   0  the entry finished and every requested scenario passed
#   1  the entry reported Blocked/Failed/NotRun (the summary says which)
#   2  the entry refused the arguments
#   3  the complete mode was requested but this entry has no ThemeMatrix support
#   4  a pre-launch problem (missing entry, product directory, output leaf or PowerShell 5.1)

[CmdletBinding()]
param(
    # One scenario from the entry's real registry. Empty (or All) means the complete acceptance
    # run: every registry entry with -ThemeMatrix Both, exactly as the entry defines it.
    [string] $Scenario,
    # Explicit product directory. Empty means the sibling product next to the toolkit.
    [string] $ProductDirectory,
    # Fresh results leaf. Empty means <toolkit>/results/run-<utc timestamp>.
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitPreLaunch = 4

function Write-LauncherLine {
    param([Parameter(Mandatory)][string] $Message)
    Write-Host ('[Run-Stage5CC] ' + $Message)
}

$packageRoot = Split-Path -Parent $PSScriptRoot
$entry = Join-Path $PSScriptRoot 'run-stage5cc-ui-acceptance.ps1'

if (-not [IO.File]::Exists($entry)) {
    Write-LauncherLine ('The public entry is missing: ' + $entry)
    exit $ExitPreLaunch
}

if ([string]::IsNullOrWhiteSpace($Scenario) -or $Scenario -ceq 'All') {
    # The complete run is the checkpoint itself: every registered scenario, and the visual matrix
    # in both Windows app themes. A ThemeMatrix argument is passed for real - the entry now owns
    # the two apply/scenario/restore rounds - so this is a parameter, never an empty placeholder.
    $Scenario = 'All'
}

if ([string]::IsNullOrWhiteSpace($ProductDirectory)) {
    $ProductDirectory = Join-Path (Split-Path -Parent $packageRoot) 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
}
if (-not [IO.Path]::IsPathRooted($ProductDirectory)) {
    $ProductDirectory = Join-Path (Get-Location).Path $ProductDirectory
}
$productFull = [IO.Path]::GetFullPath($ProductDirectory)
if (-not [IO.Directory]::Exists($productFull)) {
    Write-LauncherLine ('The product directory was not found: ' + $productFull)
    Write-LauncherLine 'Pass -ProductDirectory <path> or place the unpacked product next to the toolkit.'
    exit $ExitPreLaunch
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $packageRoot ('results\run-' + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
}
$outputFull = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Directory]::Exists($outputFull)) {
    Write-LauncherLine ('The results leaf already exists; a run never overwrites one: ' + $outputFull)
    exit $ExitPreLaunch
}

$ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not [IO.File]::Exists($ps51)) {
    Write-LauncherLine ('Windows PowerShell 5.1 was not found: ' + $ps51)
    exit $ExitPreLaunch
}

$entryArguments = @(
    '-NoProfile',
    '-File', $entry,
    '-ToolkitDirectory', $packageRoot,
    '-ProductDirectory', $productFull,
    '-OutputDirectory', $outputFull,
    '-Scenario', $Scenario,
    '-ThemeMatrix', 'Both')

Write-LauncherLine ('Package: ' + $packageRoot)
Write-LauncherLine ('Product: ' + $productFull)
Write-LauncherLine ('Results: ' + $outputFull)
Write-LauncherLine ('Scenario: ' + $Scenario)

& $ps51 @entryArguments
$code = $LASTEXITCODE

# Two independent facts: whether summary.json exists, and whether a readable status came out of it.
# "The file was never written" and "the file is there but says nothing usable" are different
# outcomes and must not be reported with the same sentence.
$status = $null
$statusReadable = $false
$summaryPath = Join-Path $outputFull 'summary.json'
$summaryExists = [IO.File]::Exists($summaryPath)
if ($summaryExists) {
    try {
        $summary = ([IO.File]::ReadAllText($summaryPath) | ConvertFrom-Json)
        if ($null -ne $summary) {
            $statusProperty = $summary.PSObject.Properties['status']
            if ($null -ne $statusProperty -and
                -not [string]::IsNullOrWhiteSpace([string] $statusProperty.Value)) {
                $status = [string] $statusProperty.Value
                $statusReadable = $true
            }
        }
    }
    catch {
        Write-LauncherLine ('The summary could not be read: ' + $summaryPath)
    }
}

if ($statusReadable) {
    Write-LauncherLine ('Status: ' + $status)
}
elseif ($summaryExists) {
    Write-LauncherLine 'Status: not reported (the summary exists but its status could not be read)'
}
else {
    # No readable summary is a fact, not a status: the helper must not guess "Blocked" or
    # "InvalidArguments" from the exit code alone.
    Write-LauncherLine 'Status: not reported (no summary was generated)'
}
Write-LauncherLine ('Exit code: ' + $code)
if ($statusReadable -and $status -ceq 'Passed') {
    Write-LauncherLine 'Completed.'
}
elseif ($statusReadable) {
    Write-LauncherLine 'Review the summary before continuing; this was not a complete acceptance run.'
}
elseif ($summaryExists) {
    Write-LauncherLine ('The summary exists at ' + $summaryPath +
        ' but its status could not be read; read the console output above.')
}
else {
    Write-LauncherLine 'No summary was generated; read the console output above.'
}

foreach ($artifact in @('summary.json', 'host-condition.json')) {
    $path = Join-Path $outputFull $artifact
    if ([IO.File]::Exists($path)) {
        Write-LauncherLine ('Generated: ' + $path)
    }
}
# The scenario evidence lives in the scenario's own directory, not at the leaf root. Only the
# selected scenario is inspected, and only existing files are announced.
$scenarioRoot = Join-Path $outputFull $Scenario
foreach ($artifact in @('screenshots.json', 'index.html', 'report.json')) {
    $path = Join-Path $scenarioRoot $artifact
    if ([IO.File]::Exists($path)) {
        Write-LauncherLine ('Generated: ' + $path)
    }
}

exit $code
