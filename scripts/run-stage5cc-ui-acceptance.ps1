# TraceFold Stage 5CC - real window UI acceptance entry point.
#
# Proves the pinned winapp driver (v0.5.0) against a published TraceFold.Media.exe: every
# scenario gets its own run id, token, owned root, output directory and support/driver copy,
# and a scenario failure is contained so the remaining scenarios still run.
#
# Nothing is installed globally: the driver archive is verified against the repository
# manifest, extracted into this run's own directory, and the support CLI is published into the
# same place. A missing or mismatched archive is reported as Blocked, never as a product
# failure, and never triggers a download.
#
# Usage:
#   pwsh -NoProfile -File scripts/run-stage5cc-ui-acceptance.ps1 `
#       -ProductDirectory .superpowers/stage5cc-product `
#       -WinAppCliArchivePath D:\downloads\winappcli-x64.zip `
#       -OutputDirectory .superpowers/stage5cc-uia-gate `
#       -Scenario StartupLayout

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ProductDirectory,
    [Parameter(Mandatory)][string] $WinAppCliArchivePath,
    [string] $OutputDirectory,
    [ValidateSet('All','StartupLayout','MainChain','Conflict','Library','Taxonomy','Diagnostics','Visual')]
    [string[]] $Scenario = @('All')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:repositoryRoot = Split-Path -Parent $PSScriptRoot
$script:uiRoot = Join-Path $script:repositoryRoot 'scripts/ui-acceptance'
Import-Module (Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Contracts.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Processes.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Runner.psm1') -Force -DisableNameChecking

$ExitInvalidArguments = 2
$ExitBlocked = 1

function Write-Stage5CCIndexEntry {
    param([Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.ArrayList] $Index, [Parameter(Mandatory)][hashtable] $Entry)
    foreach ($field in @('reportPath','outputDirectory')) {
        $value = [string] $Entry[$field]
        if ([IO.Path]::IsPathRooted($value) -or $value -match '(^|[\\/])\.\.([\\/]|$)') {
            throw ('Summary path must be relative: ' + $field)
        }
    }
    [void] $Index.Add([ordered] @{
        id               = [string] $Entry.id
        status           = [string] $Entry.status
        reportStatus     = [string] $Entry.reportStatus
        executionStatus  = [string] $Entry.executionStatus
        reason           = Protect-UiAcceptanceText -Value ([string] $Entry.reason)
        reportPath       = [string] $Entry.reportPath
        outputDirectory  = [string] $Entry.outputDirectory
        durationMs       = [int] $Entry.durationMs
        complete         = [bool] $Entry.complete
    })
}

function Get-Stage5CCEntryEffectiveStatus {
    param(
        [Parameter(Mandatory)][string] $ReportStatus,
        [Parameter(Mandatory)][string] $ExecutionStatus
    )

    if ($ExecutionStatus -ceq 'Failed' -or $ReportStatus -ceq 'Failed') { return 'Failed' }
    if ($ExecutionStatus -ceq 'Blocked' -or $ReportStatus -ceq 'Blocked') { return 'Blocked' }
    if ($ReportStatus -ceq 'NotRun' -or $ExecutionStatus -ceq 'NotRun') { return 'NotRun' }
    return 'Passed'
}

function Get-Stage5CCOverallStatus {
    param([Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.ArrayList] $Index)

    $statuses = @($Index | ForEach-Object { [string] $_.status })
    if ($statuses.Count -eq 0) { return 'NotRun' }
    if (@($statuses | Where-Object { $_ -ceq 'Failed' }).Count -gt 0) { return 'Failed' }
    if (@($statuses | Where-Object { $_ -ceq 'Blocked' }).Count -gt 0) { return 'Blocked' }
    if (@($statuses | Where-Object { $_ -ceq 'NotRun' }).Count -gt 0) { return 'NotRun' }
    return 'Passed'
}

function Get-Stage5CCOwnedLedger {
    param([Parameter(Mandatory)][string] $OwnedRoot)

    $ledgerPath = Join-Path $OwnedRoot 'owned-processes.txt'
    $records = @()
    if (-not [IO.File]::Exists($ledgerPath)) { return ,$records }
    foreach ($line in [IO.File]::ReadAllLines($ledgerPath)) {
        $parts = $line.Split('|')
        if ($parts.Count -ne 3) { continue }
        $processId = 0
        [long]$ticks = 0
        if (-not [int]::TryParse($parts[0], [ref] $processId) -or $processId -le 0) { continue }
        if (-not [long]::TryParse($parts[1], [ref] $ticks) -or $ticks -le 0) { continue }
        if (-not [IO.Path]::IsPathRooted($parts[2])) { continue }
        $records += [pscustomobject] @{ ProcessId = $processId; StartUtcTicks = $ticks; ExecutablePath = $parts[2] }
    }
    return ,$records
}

# --- inputs ----------------------------------------------------------------------------

$product = [IO.Path]::GetFullPath($ProductDirectory)
$productExecutable = Join-Path $product 'TraceFold.Media.exe'
$driverManifestPath = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/winapp/win-x64/manifest.json'
$driverManifest = Get-Content -LiteralPath $driverManifestPath -Raw | ConvertFrom-Json
$archivePath = $WinAppCliArchivePath
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $defaultParent = Join-Path $script:repositoryRoot '.superpowers/stage5cc-uia-gate'
    [void][IO.Directory]::CreateDirectory($defaultParent)
    $OutputDirectory = Join-Path $defaultParent ('run-' + [guid]::NewGuid().ToString('N'))
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Directory]::Exists($output) -or [IO.File]::Exists($output)) {
    [Console]::Error.WriteLine('The output directory already exists; choose a fresh destination.')
    exit $ExitInvalidArguments
}

$requested = @()
try { $requested = @(Resolve-UiAcceptanceScenarioRequest -Requested $Scenario) }
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit $ExitInvalidArguments
}

$index = New-Object System.Collections.ArrayList
$blockedReason = ''

if (-not [IO.File]::Exists($productExecutable)) {
    $blockedReason = 'published_product_unavailable'
}
elseif (-not [IO.File]::Exists($archivePath)) {
    $blockedReason = 'pinned_driver_archive_unavailable'
}
else {
    $archive = Get-Item -LiteralPath $archivePath
    if ($archive.Length -ne [long] $driverManifest.archiveSize -or
        (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -cne [string] $driverManifest.sha256) {
        $blockedReason = 'pinned_driver_archive_mismatch'
    }
}

if (-not [string]::IsNullOrWhiteSpace($blockedReason)) {
    foreach ($name in $requested) {
        Write-Stage5CCIndexEntry -Index $index -Entry @{
            id = $name; status = 'Blocked'; reportStatus = 'Blocked'; executionStatus = 'Blocked'
            reason = $blockedReason; reportPath = ''; outputDirectory = ''
            durationMs = 0; complete = $false
        }
    }
    [void][IO.Directory]::CreateDirectory($output)
    $summary = [ordered] @{
        checkpoint = '5CC'
        complete = $false
        status = Get-Stage5CCOverallStatus -Index $index
        reason = $blockedReason
        entries = @($index.ToArray())
    }
    [IO.File]::WriteAllText((Join-Path $output 'summary.json'),
        (ConvertTo-Json -InputObject $summary -Depth 20), [Text.UTF8Encoding]::new($false))
    [Console]::Out.WriteLine(('Stage 5CC: ' + $summary.status + ' (' + $blockedReason + ')'))
    exit $ExitBlocked
}

# --- per scenario ----------------------------------------------------------------------

[void][IO.Directory]::CreateDirectory($output)
$supportProject = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/TraceFold.Media.UiAcceptance.Support.csproj'
$supportRoot = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap'
$packageCache = Join-Path $script:repositoryRoot 'apps/media-desktop/.packages'
& dotnet publish $supportProject -c Release -o $supportRoot -p:RestorePackagesPath=$packageCache | Out-Null
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine('The support CLI could not be published.')
    exit $ExitBlocked
}
$supportExecutable = Join-Path $supportRoot 'TraceFold.Media.UiAcceptance.Support.exe'
if (-not [IO.File]::Exists($supportExecutable)) {
    [Console]::Error.WriteLine('The support CLI is missing after publish.')
    exit $ExitBlocked
}

$productHash = (Get-FileHash -LiteralPath $productExecutable -Algorithm SHA256).Hash
$productVersion = [string] (Get-Item -LiteralPath $productExecutable).VersionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($productVersion)) { $productVersion = 'unknown' }

foreach ($name in $requested) {
    $started = [DateTimeOffset]::UtcNow
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $entryFile = Get-UiAcceptanceScenarioFile -Name $name
    $scenarioPath = Join-Path $script:uiRoot ('scenarios/' + $entryFile)
    $runId = [guid]::NewGuid()
    $runToken = [guid]::NewGuid().ToString('N')
    $runOutput = Join-Path $output $name
    $runRoot = ''
    $reportPath = ''
    $reportStatus = 'NotRun'
    $executionStatus = 'NotRun'
    $reason = ''

    if (-not [IO.File]::Exists($scenarioPath)) {
        $executionStatus = 'NotRun'
        $reason = 'scenario_not_implemented'
        $clock.Stop()
        Write-Stage5CCIndexEntry -Index $index -Entry @{
            id = $name; status = (Get-Stage5CCEntryEffectiveStatus -ReportStatus $reportStatus -ExecutionStatus $executionStatus)
            reportStatus = $reportStatus; executionStatus = $executionStatus; reason = $reason
            reportPath = ''; outputDirectory = $name
            durationMs = [int] $clock.ElapsedMilliseconds; complete = $false
        }
        continue
    }

    try {
        $runRoot = New-UiAcceptanceRunRoot -SupportPath $supportExecutable `
            -ParentDirectory ([IO.Path]::GetTempPath()) -RunId $runId -RunToken $runToken
        $runSupport = Join-Path $runRoot 'support'
        $runDriver = Join-Path $runRoot 'winapp'
        Copy-Item -LiteralPath $supportRoot -Destination $runSupport -Recurse -Force
        [void][IO.Directory]::CreateDirectory($runDriver)
        Expand-Archive -LiteralPath $archivePath -DestinationPath $runDriver
        if (-not [IO.File]::Exists((Join-Path $runDriver 'winapp.exe'))) { throw 'pinned_driver_executable_missing' }

        $entryArguments = @{
            ScenarioId        = (ConvertTo-UiAcceptanceSlug -Value $name)
            ExecutablePath    = $productExecutable
            ContractPath      = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
            OwnedRoot         = $runRoot
            RunToken          = $runToken
            OutputDirectory   = $runOutput
            SupportPath       = (Join-Path $runSupport 'TraceFold.Media.UiAcceptance.Support.exe')
            WinAppPath        = (Join-Path $runDriver 'winapp.exe')
        }

        $scenarioBody = { Invoke-UiAcceptanceScenario -Path $scenarioPath -Arguments $entryArguments }
        $cleanupBody = {
            $ledger = Get-Stage5CCOwnedLedger -OwnedRoot $runRoot
            if ($ledger.Count -gt 0) {
                $null = Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($product)
            }
        }
        $metadata = @{
            sourceCommit  = (& git -C $script:repositoryRoot rev-parse HEAD 2>$null | Out-String).Trim()
            productHash   = $productHash
            productVersion = $productVersion
            driverVersion = [string] $driverManifest.version
            driverHash    = [string] $driverManifest.sha256
            fixtureHashes = @()
            hostOs        = [Environment]::OSVersion.VersionString
            dpi           = 96
            theme         = 'unknown'
            viewport      = @{ width = 0; height = 0 }
        }

        $result = Invoke-UiAcceptanceRun -ScenarioId $name -Scenario $scenarioBody -Cleanup $cleanupBody `
            -OwnedRoot $runRoot -OutputDirectory $runOutput -Metadata $metadata `
            -RunToken $runToken -SupportPath $supportExecutable `
            -ApprovedDirectories @($product)
        $reportPath = [string] $result.ReportPath
        if ([IO.File]::Exists($reportPath)) {
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
            $reportStatus = [string] @($report.scenarios)[0].status
        }
        $executionStatus = if ($result.Status -ceq 'Passed') { 'Passed' } else { [string] $result.Status }
        if ($result.CleanupStatus -cne 'Passed') { $executionStatus = 'Failed' }
    }
    catch {
        $executionStatus = 'Blocked'
        $reason = Protect-UiAcceptanceText -Value $_.Exception.Message -RunToken $runToken -OwnedRoot $runRoot
        if (-not [string]::IsNullOrWhiteSpace($runRoot) -and [IO.Directory]::Exists($runRoot)) {
            try {
                $ledger = Get-Stage5CCOwnedLedger -OwnedRoot $runRoot
                if ($ledger.Count -gt 0) {
                    $null = Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($product)
                }
                $null = & $supportExecutable root delete --root $runRoot --token $runToken
                if ($LASTEXITCODE -ne 0) { throw 'owned_root_delete_refused' }
            }
            catch {
                $executionStatus = 'Failed'
                $reason += '; setup_cleanup_failed'
            }
        }
    }

    $clock.Stop()
    $status = Get-Stage5CCEntryEffectiveStatus -ReportStatus $reportStatus -ExecutionStatus $executionStatus
    Write-Stage5CCIndexEntry -Index $index -Entry @{
        id = $name; status = $status; reportStatus = $reportStatus; executionStatus = $executionStatus
        reason = $reason; reportPath = $(if ([IO.File]::Exists($reportPath)) { $name + '/report.json' } else { '' }); outputDirectory = $name
        durationMs = [int] $clock.ElapsedMilliseconds
        complete = ($status -ceq 'Passed' -and -not [string]::IsNullOrWhiteSpace($reportPath))
    }
}

$overall = Get-Stage5CCOverallStatus -Index $index
$summary = [ordered] @{
    checkpoint = '5CC'
    complete = ($overall -ceq 'Passed')
    status = $overall
    reason = ''
    entries = @($index.ToArray())
}
[IO.File]::WriteAllText((Join-Path $output 'summary.json'),
    (ConvertTo-Json -InputObject $summary -Depth 20), [Text.UTF8Encoding]::new($false))
[Console]::Out.WriteLine('Stage 5CC: ' + $overall)

if ($overall -ceq 'Passed') { exit 0 }
exit $ExitBlocked
