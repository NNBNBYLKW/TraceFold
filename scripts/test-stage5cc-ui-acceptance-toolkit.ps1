# TraceFold UI acceptance toolkit - self test runner.
#
# Self-contained: it does not depend on Pester and never starts TraceFold, touches the
# registry, or invokes the real pinned driver. Each *.Tests.ps1 file exports
# Get-TestCases returning an array of @{ Name = <string>; Body = <scriptblock> }.
#
# Usage:  pwsh -NoProfile -File scripts/test-stage5cc-ui-acceptance-toolkit.ps1 [-Suite All]

[CmdletBinding()]
param(
    [ValidateSet('All', 'Contracts', 'Driver', 'Processes', 'Evidence', 'Runner', 'Workflows', 'IntakeScenario', 'WorkflowScenario', 'Aggregate')]
    [string] $Suite = 'All'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This file lives in scripts/, so the repository root is its parent directory.
$script:root = Split-Path -Parent $PSScriptRoot
$script:uiRoot = Join-Path $script:root 'scripts/ui-acceptance'

# Explicit registration: a new test file must be added here and to the ValidateSet.
$script:suites = [ordered] @{
    Contracts = @(
        'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Contracts.Tests.ps1'
    )
    Driver = @(
        'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Driver.Tests.ps1'
    )
    Processes = @(
        'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Processes.Tests.ps1'
    )
    Evidence = @(
        'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Evidence.Tests.ps1'
    )
    Runner = @(
        'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Runner.Tests.ps1'
    )
    Workflows = @(
        'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Workflows.Tests.ps1'
    )
    IntakeScenario = @(
        'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.IntakeScenario.Tests.ps1'
    )
    WorkflowScenario = @(
        'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.WorkflowScenario.Tests.ps1'
    )
    Aggregate = @(
        'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.Aggregate.Tests.ps1'
    )
}

$script:passed = 0
$script:failed = 0
$script:failures = New-Object System.Collections.ArrayList

function Assert-True {
    param([Parameter(Mandatory = $true)] [bool] $Condition, [string] $Message = 'expected true')
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string] $Message = '')
    if ($Expected -ne $Actual) {
        $detail = 'expected [' + $Expected + '] but got [' + $Actual + ']'
        if (-not [string]::IsNullOrEmpty($Message)) { $detail = $Message + ': ' + $detail }
        throw $detail
    }
}

function Assert-Contains {
    param([string] $Value, [string] $Expected, [string] $Message = '')
    if ($Value -notlike ('*' + $Expected + '*')) {
        $detail = 'expected to contain [' + $Expected + ']'
        if (-not [string]::IsNullOrEmpty($Message)) { $detail = $Message + ': ' + $detail }
        throw $detail
    }
}

function Assert-NotContains {
    param([string] $Value, [string] $Expected, [string] $Message = '')
    if ($Value -like ('*' + $Expected + '*')) {
        $detail = 'expected NOT to contain [' + $Expected + ']'
        if (-not [string]::IsNullOrEmpty($Message)) { $detail = $Message + ': ' + $detail }
        throw $detail
    }
}

function Assert-Throws {
    param([Parameter(Mandatory = $true)] [scriptblock] $Body, [string] $Message = 'expected an exception')
    $threw = $false
    try { & $Body } catch { $threw = $true }
    if (-not $threw) { throw $Message }
}

function Remove-UiTestTree {
    param([Parameter(Mandatory)][string] $Path)
    $full = [IO.Path]::GetFullPath($Path)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($full) -notlike 'tracefold-*') { throw 'Test cleanup target is outside its temporary scope.' }
    if (-not (Test-Path -LiteralPath $full)) { return }
    $boundary = $full.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($full)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        if ($current -ne $full -and -not $current.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup escaped its temporary scope.' }
        if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Test cleanup refuses a reparse point.' }
        if ([IO.Directory]::Exists($current)) {
            foreach ($entry in [IO.Directory]::GetFileSystemEntries($current)) { $pending.Push([IO.Path]::GetFullPath($entry)) }
        }
    }
    Remove-Item -LiteralPath $full -Recurse -Force
}

function Get-UiTestPngBytes {
    return ,([Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lXcAAAAASUVORK5CYII='))
}

# Make the toolkit modules available to the test files.
foreach ($module in @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner', 'Workflows')) {
    $path = Join-Path $script:uiRoot ('TraceFold.UiAcceptance.' + $module + '.psm1')
    if (Test-Path -LiteralPath $path) {
        Import-Module -Name $path -Force -DisableNameChecking
    }
}

function Invoke-Suite {
    param([string] $Name)

    foreach ($relative in $script:suites[$Name]) {
        $file = Join-Path $script:root $relative
        if (-not (Test-Path -LiteralPath $file)) {
            $script:failed = $script:failed + 1
            [void] $script:failures.Add(($relative + ' :: required test file is missing'))
            Write-Host ('FAIL ' + $relative + ' (not present)')
            continue
        }

        # Dot-source so the file's Get-TestCases lands in this scope, then call it.
        . $file
        $cases = @(Get-TestCases)

        foreach ($case in $cases) {
            try {
                & $case.Body
                $script:passed = $script:passed + 1
                Write-Host ('PASS ' + $case.Name)
            }
            catch {
                $script:failed = $script:failed + 1
                [void] $script:failures.Add(($relative + ' :: ' + $case.Name + ' :: ' + $_.Exception.Message))
                Write-Host ('FAIL ' + $case.Name + ' - ' + $_.Exception.Message)
            }
        }
    }
}

if ($Suite -eq 'All') {
    foreach ($name in $script:suites.Keys) { Invoke-Suite -Name $name }
}
else {
    Invoke-Suite -Name $Suite
}

Write-Host ''
Write-Host ('passed={0} failed={1}' -f $script:passed, $script:failed)
foreach ($failure in $script:failures) { Write-Host ('  ' + $failure) }

if ($script:failed -gt 0) { exit 1 }
if ($script:passed -eq 0) {
    Write-Host 'no test cases ran'
    exit 1
}

exit 0
