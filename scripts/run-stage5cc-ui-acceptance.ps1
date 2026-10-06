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
    [ValidateSet('All','StartupLayout','MainChain','Conflict','ConflictKeepBoth','ConflictReplace','ConflictSkip','ProcessingFailureRetry','BatchMetadataUndo','LibraryFilterNaming','Library','Taxonomy','Diagnostics','SingleInstance','Visual')]
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

<#
.SYNOPSIS
    Turns a precheck observation into the persisted 'checks' node of the summary.

.DESCRIPTION
    The node carries what this run checked, what it found missing and whether the outcome
    stopped dispatch, so a reader can verify the gate from disk. It never carries a path:
    summaries are shared artifacts and must stay free of machine specific locations. The
    observation time comes from the check itself, not from the moment the summary is written.

    'blockedBeforeDispatch' has exactly one meaning: the completeness precheck stopped this
    batch, so no scenario was dispatched at all. A later stage refusing to run (the product,
    the pinned driver, the interactive desktop) is not this case and must stay false.
    'dispatchedCount' counts scenarios that really reached their invocation, so a root or
    driver that never came up is never counted.
#>
function New-Stage5CCCompletenessNode {
    param(
        [Parameter(Mandatory)] $Completeness,
        [Parameter(Mandatory)][bool] $BlockedBeforeDispatch,
        [Parameter(Mandatory)][int] $DispatchedCount
    )

    $missing = @()
    foreach ($item in @($Completeness.Missing)) {
        $missing += [ordered]@{
            id        = [string] $item.id
            entryFile = [string] $item.entryFile
            reason    = [string] $item.reason
        }
    }

    return ,[ordered]@{
        scope                 = [string] $Completeness.Scope
        checkedCount          = [int] $Completeness.CheckedCount
        checkedIds            = @($Completeness.Checked | ForEach-Object { [string] $_ })
        missingCount          = [int] $Completeness.MissingCount
        missing               = $missing
        blockedBeforeDispatch = [bool] $BlockedBeforeDispatch
        dispatchedCount       = [int] $DispatchedCount
        checkedUtc            = [string] $Completeness.CheckedUtc
    }
}

function New-Stage5CCSummary {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.ArrayList] $Index,
        [Parameter(Mandatory)][string] $Status,
        [string] $Reason = '',
        $Checks
    )

    $summary = [ordered]@{
        checkpoint = '5CC'
        complete   = ($Status -ceq 'Passed')
        status     = $Status
        reason     = $Reason
        entries    = @($Index.ToArray())
    }
    if ($null -ne $Checks) { $summary['checks'] = $Checks }

    return ,$summary
}

function Write-Stage5CCSummary {
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)] $Summary
    )

    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    $reportPath = Join-Path $OutputDirectory 'summary.json'
    [IO.File]::WriteAllText($reportPath, (ConvertTo-Json -InputObject $Summary -Depth 20), [Text.UTF8Encoding]::new($false))
    return $reportPath
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
$dispatched = New-Object System.Collections.ArrayList
$blockedReason = ''

# --- request precheck ------------------------------------------------------------------
#
# Runs before anything else: no support publish, no owned root and no scenario is created when
# the registry owes an entry that does not exist. A named request owes only the entries it
# named, so verifying one slice is never blocked by a missing unrelated one; a request that
# contains 'All' owes the whole registry, and one missing entry stops the batch here.

$completeness = $null
try {
    $completeness = Test-UiAcceptanceRequestCompleteness -ScenariosRoot (Join-Path $script:uiRoot 'scenarios') -Requested $Scenario
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit $ExitInvalidArguments
}

if ($completeness.IncludesAll -and -not $completeness.Complete) {
    # 'All' asks for the checkpoint, so every registry entry must exist. Nothing is dispatched:
    # the walk stops here, before the product, driver, desktop or process checks and long before
    # a support publish or an owned root.
    foreach ($name in @($completeness.Checked)) {
        Write-Stage5CCIndexEntry -Index $index -Entry @{
            id = $name; status = 'Blocked'; reportStatus = 'Blocked'; executionStatus = 'Blocked'
            reason = 'scenario_entry_missing'; reportPath = ''; outputDirectory = ''
            durationMs = 0; complete = $false
        }
    }
    $checks = New-Stage5CCCompletenessNode -Completeness $completeness -BlockedBeforeDispatch $true -DispatchedCount 0
    $precheckStatus = Get-Stage5CCOverallStatus -Index $index
    $null = Write-Stage5CCSummary -OutputDirectory $output -Summary (New-Stage5CCSummary -Index $index -Status $precheckStatus -Reason 'scenario_entry_missing' -Checks $checks)
    [Console]::Out.WriteLine(('Stage 5CC: ' + $precheckStatus + ' (scenario_entry_missing)'))
    exit $ExitBlocked
}

$missingIds = @($completeness.Missing | ForEach-Object { [string] $_.id })
$missingRecorded = @()

if ($missingIds.Count -gt 0 -and -not $completeness.IncludesAll) {
    # A named request owes only the entries it named. Each missing one is refused here, before
    # anything is dispatched for it, and the rest of the request keeps running: verifying one
    # slice must never be blocked by an unrelated unimplemented scenario.
    foreach ($name in $missingIds) {
        Write-Stage5CCIndexEntry -Index $index -Entry @{
            id = $name; status = 'Blocked'; reportStatus = 'Blocked'; executionStatus = 'Blocked'
            reason = 'scenario_entry_missing'; reportPath = ''; outputDirectory = ''
            durationMs = 0; complete = $false
        }
        $missingRecorded += $name
    }
}

# Only the entries that still owe the later phases. Entries refused above are not walked again.
$pending = @($requested | Where-Object { $missingRecorded -cnotcontains $_ })

# Nothing left to run means no product, driver, desktop or process check needs answering: the
# run stops with the entries it already refused, and nothing was dispatched.
if ($pending.Count -gt 0) {
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
    if ([string]::IsNullOrWhiteSpace($blockedReason) -and $pending -contains 'MainChain') {
        $mediaManifestPath = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/ffmpeg/win-x64/manifest.json'
        $mediaManifest = [IO.File]::ReadAllText($mediaManifestPath) | ConvertFrom-Json
        foreach ($fileName in @('ffmpeg.exe','ffprobe.exe')) {
            $toolPath = Join-Path $product ('tools/ffmpeg/' + $fileName)
            if (-not [IO.File]::Exists($toolPath)) {
                $blockedReason = 'pinned_media_tools_unavailable'
                break
            }
            $expected = $mediaManifest.files.PSObject.Properties[$fileName].Value
            if ((Get-Item -LiteralPath $toolPath).Length -ne [long]$expected.size -or
                (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash -cne [string]$expected.sha256) {
                $blockedReason = 'pinned_media_tools_mismatch'
                break
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($blockedReason)) {
        if (-not (Test-UiInteractiveDesktop)) {
            $blockedReason = 'interactive_desktop_unavailable'
        }
        elseif (@(Get-Process -Name 'TraceFold.Media' -ErrorAction SilentlyContinue).Count -gt 0) {
            $blockedReason = 'ordinary_product_process_running'
        }
    }
}

if ($pending.Count -eq 0) {
    # Every requested entry was refused above, so there is nothing left to publish, own or run:
    # the precheck is what stopped this batch, and that is what the flag reports.
    $checks = New-Stage5CCCompletenessNode -Completeness $completeness -BlockedBeforeDispatch $true -DispatchedCount 0
    $refusedStatus = Get-Stage5CCOverallStatus -Index $index
    $null = Write-Stage5CCSummary -OutputDirectory $output -Summary (New-Stage5CCSummary -Index $index -Status $refusedStatus -Reason 'scenario_entry_missing' -Checks $checks)
    [Console]::Out.WriteLine(('Stage 5CC: ' + $refusedStatus + ' (scenario_entry_missing)'))
    exit $ExitBlocked
}

if (-not [string]::IsNullOrWhiteSpace($blockedReason)) {
    foreach ($name in $pending) {
        Write-Stage5CCIndexEntry -Index $index -Entry @{
            id = $name; status = 'Blocked'; reportStatus = 'Blocked'; executionStatus = 'Blocked'
            reason = $blockedReason; reportPath = ''; outputDirectory = ''
            durationMs = 0; complete = $false
        }
    }
    # The precheck ran and passed for this request, and that observation is recorded here too, so
    # every summary says which entries were checked and how many reached dispatch.
    $checks = New-Stage5CCCompletenessNode -Completeness $completeness -BlockedBeforeDispatch $false -DispatchedCount 0
    $preflightStatus = Get-Stage5CCOverallStatus -Index $index
    $null = Write-Stage5CCSummary -OutputDirectory $output -Summary (New-Stage5CCSummary -Index $index -Status $preflightStatus -Reason $blockedReason -Checks $checks)
    [Console]::Out.WriteLine(('Stage 5CC: ' + $preflightStatus + ' (' + $blockedReason + ')'))
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

foreach ($name in $pending) {
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

    # Checked again at dispatch time: an entry file may have appeared or vanished since the
    # request precheck, and this is the check that actually decides whether anything runs. The
    # reason stays 'scenario_not_implemented', which is an implementation debt, never a product
    # failure, so it must never be reported as one.
    if (-not [IO.File]::Exists($scenarioPath)) {
        $executionStatus = 'Blocked'
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
        # Preparation stays in this scope, never in a helper script block: the catch below has to
        # know the root it was given so it can stop whatever the root owns and hand it back to the
        # support tool, and every later step reads the paths produced here. Moving this away to
        # make counting easier made the walk forget where its own scaffold was.
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

        $cleanupBody = {
            $ledger = Get-Stage5CCOwnedLedger -OwnedRoot $runRoot
            if ($ledger.Count -gt 0) {
                $null = Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($product)
            }
        }
        # Provenance is best effort and decides nothing: git that is missing, that refuses, or that
        # is pointed at a tree this shell cannot read must not stop a scenario from running. On
        # Windows PowerShell a native tool writing to stderr is an error record, so an unavailable
        # commit used to abort the preparation of every scenario; an unknown commit stays unknown.
        $sourceCommit = ''
        try {
            $rawHead = & git -C $script:repositoryRoot rev-parse HEAD 2>$null
            if ($LASTEXITCODE -eq 0) { $sourceCommit = ($rawHead | Out-String).Trim() }
        }
        catch { $sourceCommit = '' }
        $metadata = @{
            sourceCommit  = $sourceCommit
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

        $result = Invoke-UiAcceptanceRun -ScenarioId $name `
            -Scenario {
                # Counted on the line where the scenario really runs and nowhere else: preparation
                # that failed above never reaches it, so 'dispatchedCount' cannot inflate, and a
                # scenario that does run is counted exactly once.
                Register-UiAcceptanceDispatch -Name $name -Ledger $dispatched
                Invoke-UiAcceptanceScenario -Path $scenarioPath -Arguments $entryArguments
            } `
            -Cleanup $cleanupBody `
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
$checks = New-Stage5CCCompletenessNode -Completeness $completeness -BlockedBeforeDispatch $false -DispatchedCount $dispatched.Count
$null = Write-Stage5CCSummary -OutputDirectory $output -Summary (New-Stage5CCSummary -Index $index -Status $overall -Checks $checks)
[Console]::Out.WriteLine('Stage 5CC: ' + $overall)

if ($overall -ceq 'Passed') { exit 0 }
exit $ExitBlocked
