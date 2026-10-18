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
# Metadata and provenance producers: one implementation per field family, consumed below.
Import-Module (Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Metadata.psm1') -Force -DisableNameChecking

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

# [D0203-E2 EVIDENCE-FUNCTION]
<#
.SYNOPSIS
    Writes the D0203-E2 host evidence file: one independent document per entry process.

.DESCRIPTION
    The file lands under this run's own fresh output directory. Nothing that already exists is
    ever overwritten by a process that has not written it itself, and every failure is returned
    instead of thrown, so the caller folds it into its own outcome bookkeeping and an original
    business error is never replaced by an evidence problem.
#>
function Write-Stage5CCHostEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)] $Document,
        [string] $RunToken = '',
        [string] $OwnedRoot = '',
        [Parameter(Mandatory)][bool] $ClaimWrite
    )

    $path = Join-Path $OutputDirectory 'host-condition.json'
    try {
        if ([IO.File]::Exists($path) -and -not $ClaimWrite) {
            return [pscustomobject] @{ ok = $false; reason = 'evidence_already_present'; path = $path }
        }
        $json = ConvertTo-Json -InputObject $Document -Depth 12
        if (-not [string]::IsNullOrWhiteSpace($RunToken) -or -not [string]::IsNullOrWhiteSpace($OwnedRoot)) {
            $json = Protect-UiAcceptanceText -Value $json -RunToken $RunToken -OwnedRoot $OwnedRoot
        }
        [void][IO.Directory]::CreateDirectory($OutputDirectory)
        [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
        return [pscustomobject] @{ ok = $true; reason = ''; path = $path }
    }
    catch {
        return [pscustomobject] @{ ok = $false; reason = [string] $_.Exception.Message; path = $path }
    }
}
# [/D0203-E2 EVIDENCE-FUNCTION]

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

# [D0203-E2 HOST-FACTS]
# The three registry names whose verification runs under the double-byte console condition in
# the process that actually executes them; each host records its own facts and no host reads
# another host's setting.
$hostConditionFamilies = @('Library', 'BatchMetadataUndo', 'LibraryFilterNaming')
# Every captured fact carries its own state and its own error text. A field that could not be
# read keeps state 'unknown' with the original error beside it, and is never replaced by an
# empty placeholder that later reads like a real answer; the host version is part of the
# evidence document itself instead of a construction that is discarded before it is written.
$hostConditionVersion = [ordered] @{
    psVersion = ''; psVersionState = 'unknown'; psVersionError = ''
    psEdition = ''; psEditionState = 'unknown'; psEditionError = ''
    clr = ''; clrState = 'unknown'; clrError = ''
}
try { $hostConditionVersion.psVersion = $PSVersionTable.PSVersion.ToString(); $hostConditionVersion.psVersionState = 'captured' }
catch { $hostConditionVersion.psVersionError = [string] $_.Exception.Message }
try { $hostConditionVersion.psEdition = [string] $PSVersionTable.PSEdition; $hostConditionVersion.psEditionState = 'captured' }
catch { $hostConditionVersion.psEditionError = [string] $_.Exception.Message }
try { $hostConditionVersion.clr = [Environment]::Version.ToString(); $hostConditionVersion.clrState = 'captured' }
catch { $hostConditionVersion.clrError = [string] $_.Exception.Message }
$hostConditionProcess = [ordered] @{
    pid = $PID
    startUtcTicks = $null; startUtcTicksState = 'unknown'; startUtcTicksError = ''
    path = ''; pathState = 'unknown'; pathError = ''
    parentPid = $null; parentPidState = 'unknown'; parentPidError = ''
    commandLine = ''; commandLineState = 'unknown'; commandLineError = ''
    product = [ordered] @{
        path = $productExecutable
        bytes = $null; bytesState = 'unknown'; bytesError = ''
        sha256 = $productHash; fileVersion = $productVersion
    }
}
try { $hostConditionProcess.startUtcTicks = [long] (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks; $hostConditionProcess.startUtcTicksState = 'captured' }
catch { $hostConditionProcess.startUtcTicksError = [string] $_.Exception.Message }
try { $hostConditionProcess.path = [string] (Get-Process -Id $PID).Path; $hostConditionProcess.pathState = 'captured' }
catch { $hostConditionProcess.pathError = [string] $_.Exception.Message }
try {
    $parentRecord = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $PID) -ErrorAction Stop
    if ($null -eq $parentRecord) { throw 'the process table answered no record for this process id' }
    $hostConditionProcess.parentPid = [int] $parentRecord.ParentProcessId
    $hostConditionProcess.parentPidState = 'captured'
}
catch { $hostConditionProcess.parentPidError = [string] $_.Exception.Message }
try { $hostConditionProcess.commandLine = [string] [Environment]::CommandLine; $hostConditionProcess.commandLineState = 'captured' }
catch { $hostConditionProcess.commandLineError = [string] $_.Exception.Message }
try { $hostConditionProcess.product.bytes = [long] ((Get-Item -LiteralPath $productExecutable).Length); $hostConditionProcess.product.bytesState = 'captured' }
catch { $hostConditionProcess.product.bytesError = [string] $_.Exception.Message }
$hostEvidence = [ordered] @{ evidence = 'host-condition'; version = 1; hostConditionVersion = $hostConditionVersion; process = $hostConditionProcess; scenarios = (New-Object System.Collections.ArrayList) }
$hostEvidenceClaimed = $false
$hostEvidenceWriteFailure = ''
# [/D0203-E2 HOST-FACTS]

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
    $hostConditionApplies = ($hostConditionFamilies -ccontains $name)
    $hostConditionAttempted = $false
    $hostConditionRestoreFailed = $false
    $savedOutputEncoding = $null

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
        # [D0203-E2 CONDITION-SETUP]
        # Same family as the WorkflowScenario cases: save the process console encoding, set and
        # measure the double-byte code page, and run this scenario's body here while it holds.
        # The restore is the finally on this same try; both paths run it.
        $scenarioEvidence = [ordered] @{ name = $name; condition = $null; instances = $null }
        [void] $hostEvidence.scenarios.Add($scenarioEvidence)
        if ($hostConditionApplies) {
            $condition = [ordered] @{ applies = $true; established = $false; beforeCodePage = $null; duringCodePage = $null; afterCodePage = $null; restoreOk = $false; setupError = ''; restoreError = '' }
            $scenarioEvidence.condition = $condition
            try {
                $savedOutputEncoding = [Console]::OutputEncoding
                $condition.beforeCodePage = [int] $savedOutputEncoding.CodePage
                $hostConditionAttempted = $true
                [Console]::OutputEncoding = [Text.Encoding]::GetEncoding(936)
                $condition.duringCodePage = [int] [Console]::OutputEncoding.CodePage
                if ($condition.duringCodePage -ne 936) { throw 'the double-byte host condition was not established' }
                $condition.established = $true
            }
            catch {
                $condition.setupError = [string] $_.Exception.Message
                throw ('host_condition_not_established: ' + [string] $_.Exception.Message)
            }
        }
        # [/D0203-E2 CONDITION-SETUP]
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

        # [D0203-E2 INSTANCE-FACTS]
        # The instances that are about to be used are bound per member, from this run's own records,
        # after the copy and the extraction and before anything is dispatched; errors are stored beside
        # the fields instead of being collapsed into one string. The support copy is compared member by
        # member against the published source it was copied from; the extracted driver executable is
        # compared against the member bytes inside the verified archive itself, because the archive hash
        # alone says nothing about the file that will actually run. Cleanup later deletes these
        # instances; the facts recorded here stay readable without them.
        $instanceFacts = [ordered] @{
            support = [ordered] @{
                sourceRoot = ''; instanceRoot = ''
                members = @(); memberCount = 0; allMatchSource = $false; captureState = 'unknown'; error = ''
            }
            driver = [ordered] @{
                archivePath = ''; archiveBytes = $null; archiveSha256 = ''
                manifestSha256 = ''; manifestArchiveSize = $null; matchesManifest = $false
                winappExePath = ''; winappExeBytes = $null; winappExeSha256 = ''; winappExeFileVersion = ''
                archiveMemberName = ''; archiveMemberBytes = $null; archiveMemberSha256 = ''
                matchesArchiveMember = $false; memberReadState = 'unknown'; error = ''
            }
        }
        try {
            $supportSourceFull = [IO.Path]::GetFullPath($supportRoot)
            $supportInstanceFull = [IO.Path]::GetFullPath($runSupport)
            $instanceFacts.support.sourceRoot = Protect-UiAcceptanceText -Value $supportSourceFull -RunToken $runToken -OwnedRoot $runRoot
            $instanceFacts.support.instanceRoot = Protect-UiAcceptanceText -Value $supportInstanceFull -RunToken $runToken -OwnedRoot $runRoot
            $memberRows = New-Object System.Collections.ArrayList
            foreach ($memberFile in @(Get-ChildItem -LiteralPath $supportInstanceFull -Recurse -File | Sort-Object FullName)) {
                $relative = $memberFile.FullName.Substring($supportInstanceFull.Length).TrimStart([char] 92)
                $memberRow = [ordered] @{ relative = $relative; instanceBytes = $null; instanceSha256 = ''; sourceBytes = $null; sourceSha256 = ''; matchesSource = $false; state = 'unknown'; error = '' }
                try {
                    $memberRow.instanceBytes = [long] $memberFile.Length
                    $memberRow.instanceSha256 = (Get-FileHash -LiteralPath $memberFile.FullName -Algorithm SHA256).Hash
                    $sourceMember = Join-Path $supportSourceFull $relative
                    if (-not [IO.File]::Exists($sourceMember)) { throw ('the published source member is missing: ' + $relative) }
                    $memberRow.sourceBytes = [long] ((Get-Item -LiteralPath $sourceMember).Length)
                    $memberRow.sourceSha256 = (Get-FileHash -LiteralPath $sourceMember -Algorithm SHA256).Hash
                    $memberRow.matchesSource = ($memberRow.instanceSha256 -ceq $memberRow.sourceSha256 -and $memberRow.instanceBytes -eq $memberRow.sourceBytes)
                    $memberRow.state = 'captured'
                }
                catch { $memberRow.error = [string] $_.Exception.Message }
                [void] $memberRows.Add($memberRow)
            }
            $instanceFacts.support.members = $memberRows.ToArray()
            $instanceFacts.support.memberCount = $memberRows.Count
            $instanceFacts.support.allMatchSource = ($memberRows.Count -gt 0 -and @($memberRows | Where-Object { -not [bool] $_.matchesSource }).Count -eq 0)
            $instanceFacts.support.captureState = 'captured'
        }
        catch { $instanceFacts.support.error = [string] $_.Exception.Message }
        try {
            $instanceFacts.driver.archivePath = Protect-UiAcceptanceText -Value $archivePath -RunToken $runToken -OwnedRoot $runRoot
            $instanceFacts.driver.archiveBytes = [long] ((Get-Item -LiteralPath $archivePath).Length)
            $instanceFacts.driver.archiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
            $instanceFacts.driver.manifestSha256 = [string] $driverManifest.sha256
            $instanceFacts.driver.manifestArchiveSize = [long] $driverManifest.archiveSize
            $instanceFacts.driver.matchesManifest = ($instanceFacts.driver.archiveSha256 -ceq [string] $driverManifest.sha256 -and $instanceFacts.driver.archiveBytes -eq [long] $driverManifest.archiveSize)
            $winappInstance = Join-Path $runDriver 'winapp.exe'
            $instanceFacts.driver.winappExePath = Protect-UiAcceptanceText -Value $winappInstance -RunToken $runToken -OwnedRoot $runRoot
            $instanceFacts.driver.winappExeBytes = [long] ((Get-Item -LiteralPath $winappInstance).Length)
            $instanceFacts.driver.winappExeSha256 = (Get-FileHash -LiteralPath $winappInstance -Algorithm SHA256).Hash
            $instanceFacts.driver.winappExeFileVersion = [string] ((Get-Item -LiteralPath $winappInstance).VersionInfo.FileVersion)
            # Both loader names are attempted so the same text runs on both hosts: Windows PowerShell
            # needs the compression assemblies loaded before their types can be used.
            try { Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
            try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }
            $driverZip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($archivePath))
            try {
                $driverMember = @($driverZip.Entries | Where-Object { $_.FullName -ceq 'winapp.exe' })
                if ($driverMember.Count -ne 1) { throw ('the archive carries ' + $driverMember.Count + ' winapp.exe members') }
                $memberStream = $driverMember[0].Open()
                try {
                    $memberBuffer = New-Object IO.MemoryStream
                    try { $memberStream.CopyTo($memberBuffer); $memberBytes = $memberBuffer.ToArray() }
                    finally { $memberBuffer.Dispose() }
                }
                finally { $memberStream.Dispose() }
                $memberHasher = [Security.Cryptography.SHA256]::Create()
                try { $memberHash = ([BitConverter]::ToString($memberHasher.ComputeHash($memberBytes))).Replace('-', '') }
                finally { $memberHasher.Dispose() }
                $instanceFacts.driver.archiveMemberName = 'winapp.exe'
                $instanceFacts.driver.archiveMemberBytes = [long] $memberBytes.Length
                $instanceFacts.driver.archiveMemberSha256 = $memberHash
                $instanceFacts.driver.matchesArchiveMember = ($instanceFacts.driver.winappExeSha256 -ceq $memberHash -and $instanceFacts.driver.winappExeBytes -eq [long] $memberBytes.Length)
                $instanceFacts.driver.memberReadState = 'captured'
            }
            finally { $driverZip.Dispose() }
        }
        catch { $instanceFacts.driver.error = [string] $_.Exception.Message }
        $scenarioEvidence.instances = $instanceFacts
        # [/D0203-E2 INSTANCE-FACTS]

        $entryArguments = @{
            ScenarioId        = (ConvertTo-UiAcceptanceSlug -Value $name)
            ExecutablePath    = $productExecutable
            ContractPath      = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
            OwnedRoot         = $runRoot
            RunToken          = $runToken
            OutputDirectory   = $runOutput
            SupportPath       = (Join-Path $runSupport 'TraceFold.Media.UiAcceptance.Support.exe')
            WinAppCliArchivePath = $WinAppCliArchivePath
            WinAppPath        = (Join-Path $runDriver 'winapp.exe')
        }

        $cleanupBody = {
            $ledger = Get-Stage5CCOwnedLedger -OwnedRoot $runRoot
            if ($ledger.Count -gt 0) {
                $null = Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($product)
            }
        }
        # Provenance, product, driver, fixture and host facts now come from the metadata module:
        # each field family has one producer, and a value that could not be read stays null beside
        # its own state. The legacy ten-key hashtable the Runner requires is projected from the
        # block below; the Runner keeps the values' types instead of collapsing them to text.
        $contractPath = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
        $metadataSourceProvenance = Get-UiMetadataSourceProvenance -ProductExecutable $productExecutable -RepositoryRoot $script:repositoryRoot -CandidateSha256 $null
        $metadataProductIdentity = Get-UiMetadataProductIdentity -ProductExecutable $productExecutable -ProductDirectory (Split-Path -Parent $productExecutable)
        $metadataDriverIdentity = Get-UiMetadataDriverIdentity -ArchivePath $WinAppCliArchivePath -ExtractedExePath (Join-Path $runDriver 'winapp.exe') -ManifestVersion ([string] $driverManifest.version) -ManifestSha256 ([string] $driverManifest.sha256) -ManifestArchiveSize ([long] $driverManifest.archiveSize)
        $declaredFixtureAssets = @()
        if ([IO.File]::Exists($contractPath)) {
            $contractDocument = Read-UiMetadataJsonDocument -Path $contractPath
            $contractValue = $contractDocument['value']
            if ($contractDocument['state'] -ceq 'captured' -and $null -ne $contractValue -and $null -ne $contractValue.PSObject.Properties['fixtureAssets']) {
                $declaredFixtureAssets = @($contractValue.fixtureAssets)
            }
        }
        $metadataFixtures = Get-UiMetadataFixtureManifest -DeclaredAssets $declaredFixtureAssets -RepositoryRoot $script:repositoryRoot
        $metadataHostObservation = Get-UiMetadataHostObservation
        $metadataBlock = New-UiMetadataReportBlock -SourceProvenance $metadataSourceProvenance -ProductIdentity $metadataProductIdentity -DriverIdentity $metadataDriverIdentity -FixtureManifest $metadataFixtures -HostObservation $metadataHostObservation -HostOs ([Environment]::OSVersion.VersionString) -Redactor { param([AllowNull()] $Value) if ($null -eq $Value) { return $null }; return (Protect-UiAcceptanceText -Value $Value -RunToken $runToken -OwnedRoot $runRoot) }
        # The legacy ten-key hashtable the Runner requires, now sourced from the block through its
        # indexer: a known value keeps its text, an unknown stays null instead of collapsing to an
        # empty string or to a placeholder. The Runner projection and the report writer decide the
        # legacy shape.
        $metadata = @{
            sourceCommit  = $metadataBlock['sourceCommit']
            productHash   = $metadataBlock['productHash']
            productVersion = $metadataBlock['productVersion']
            driverVersion = $metadataBlock['driverVersion']
            driverHash    = $metadataBlock['driverHash']
            fixtureHashes = $metadataBlock['fixtureHashes']
            hostOs        = $metadataBlock['hostOs']
            dpi           = $metadataBlock['dpi']
            theme         = $metadataBlock['theme']
            viewport      = $metadataBlock['viewport']
            metadataEvidence = $metadataBlock['metadataEvidence']
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
    finally {
        # [D0203-E2 CONDITION-RESTORE]
        # Runs whether the body succeeded, failed or threw: hand the console back and read it.
        if ($hostConditionApplies -and $hostConditionAttempted) {
            try {
                [Console]::OutputEncoding = $savedOutputEncoding
                $afterCodePage = [int] [Console]::OutputEncoding.CodePage
                $scenarioEvidence.condition.afterCodePage = $afterCodePage
                $scenarioEvidence.condition.restoreOk = ($afterCodePage -eq [int] $savedOutputEncoding.CodePage)
                if (-not $scenarioEvidence.condition.restoreOk) {
                    $hostConditionRestoreFailed = $true
                    try { [Console]::Error.WriteLine('e2-host-condition-restore-mismatch') } catch { }
                }
            }
            catch {
                $hostConditionRestoreFailed = $true
                try {
                    $scenarioEvidence.condition.restoreError = [string] $_.Exception.Message
                    $scenarioEvidence.condition.restoreOk = $false
                }
                catch { }
                try { [Console]::Error.WriteLine('e2-host-condition-restore-failed') } catch { }
            }
        }
        # [/D0203-E2 CONDITION-RESTORE]
    }

    $clock.Stop()

    # [D0203-E2 EVIDENCE-WRITE]
    # The independent evidence document is written here, before the outcome is booked, so an
    # evidence problem is visible in this entry's own status without touching the report schema.
    try {
        $evidenceWrite = Write-Stage5CCHostEvidence -OutputDirectory $output -Document $hostEvidence -RunToken $runToken -OwnedRoot $runRoot -ClaimWrite $hostEvidenceClaimed
        if ([bool] $evidenceWrite.ok) { $hostEvidenceClaimed = $true }
        else {
            $hostEvidenceWriteFailure = [string] $evidenceWrite.reason
            try { [Console]::Error.WriteLine('e2-host-evidence-write-failed: ' + [string] $evidenceWrite.reason) } catch { }
        }
    }
    catch {
        $hostEvidenceWriteFailure = [string] $_.Exception.Message
        try { [Console]::Error.WriteLine('e2-host-evidence-write-failed') } catch { }
    }
    # [/D0203-E2 EVIDENCE-WRITE]

    # [D0203-E2 FINALIZE]
    # A failed host condition, a failed restore or a failed evidence write never reads as a
    # pass, and an existing business failure or block is never overwritten by these markers.
    if ($hostConditionRestoreFailed -or $hostEvidenceWriteFailure -ne '') {
        if ($executionStatus -cne 'Failed' -and $executionStatus -cne 'Blocked') { $executionStatus = 'Failed' }
        if ($hostConditionRestoreFailed -and $reason -notlike '*host_condition_restore_failed*') { $reason += '; host_condition_restore_failed' }
        if ($hostEvidenceWriteFailure -ne '' -and $reason -notlike '*host_evidence_write_failed*') { $reason += '; host_evidence_write_failed' }
    }
    # [/D0203-E2 FINALIZE]

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
