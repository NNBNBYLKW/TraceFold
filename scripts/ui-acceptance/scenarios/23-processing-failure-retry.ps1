# TraceFold UI acceptance - independent processing failure and retry scenario (Task 12).
#
# One owned root, created for this run only. The scenario proves that a real preprocessing failure
# is visible, that both sources survive it untouched, and that a retry against changed sources is
# refused. After the product re-observes the new bytes, one Start action recovers the item.
#
# The failure is produced by the product's own tools on fixtures that pair correctly but are not
# readable media, so no package tool is modified, no failure hook is injected, and the database is
# never written directly. Preprocessing cannot succeed on these bytes, which is what makes the
# outcome deterministic rather than timing dependent.
#
# Honest observation:
#   * The brief states (loading, queued, running) are sampled by real driver round-trips. Whatever
#     is actually seen is recorded with its state list; anything never seen is recorded as
#     unobserved rather than asserted from a fixed delay.
#   * Failure, source-change refusal, and recovery are accepted only through product UI evidence.
#
# Read-only guarantees:
#   * Only the two owned fixtures are replaced; conclusions are paired with a read-only snapshot
#     or a file hash. Retry never re-registers changed source identity.
#   * A dispatched action is never re-sent: a timeout or an ambiguous outcome fails the run.
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
    [int] $ProcessingWaitSeconds = 90,
    [int] $ObservationTimeoutSeconds = 20,
    [int] $InspectDepth = 20,
    [int] $MutationTimeoutMs = 60000,
    [int] $StateSampleBudget = 12,
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
$sourceChangedMessage = -join ([char[]] @(
    0x6E90,0x6587,0x4EF6,0x5DF2,0x7ECF,0x53D8,0x5316,0xFF0C,0x8BF7,0x5148,0x5237,0x65B0,0x3002))

$productProcessId = 0
$launched = $null
$artifacts = [System.Collections.Generic.List[string]]::new()

function Assert-ProcessingResult {
    param($Result, [string] $Code)
    if ($null -eq $Result -or $Result.Status -cne 'Passed') { throw $Code }
}

<#
.SYNOPSIS
    Dispatches one UI mutation under a step name unique to this scenario.
#>
function Invoke-ProcessingAction {
    param([string] $Step, [string[]] $Arguments, [string] $Code)
    $result = Invoke-WinAppMutation -StepId ('processing.' + $Step) -Arguments $Arguments `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -TimeoutMs $MutationTimeoutMs `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($result.Status -cne 'Passed') {
        $detail = 'driver_unavailable'
        if ($null -ne $result.Data -and $null -ne $result.Data.PSObject.Properties['error'] -and
            $null -ne $result.Data.error.PSObject.Properties['code']) {
            $detail = ConvertTo-UiAcceptanceSlug -Value ([string] $result.Data.error.code)
        }
        $errorSlug = ''
        if (-not [string]::IsNullOrWhiteSpace([string] $result.Error)) {
            $errorSlug = '.' + (ConvertTo-UiAcceptanceSlug -Value ([string] $result.Error))
            if ($errorSlug.Length -gt 120) { $errorSlug = $errorSlug.Substring(0, 120) }
        }
        throw ($Code + '.' + (ConvertTo-UiAcceptanceSlug -Value $Step) + '.' + $detail +
            '.' + (ConvertTo-UiAcceptanceSlug -Value ([string] $result.Status)) +
            '.exit' + [string] $result.ExitCode + $errorSlug)
    }
    return $result
}

function Read-ProcessingNodes {
    return @(Read-UiAcceptanceTree -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -FailureCode 'processing.inspect_unavailable' `
        -Depth $InspectDepth -TimeoutSeconds $ObservationTimeoutSeconds)
}

<#
.SYNOPSIS
    Waits for a driver-observed condition, letting the driver do the polling.
#>
function Test-ProcessingWait {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'processing.observation_unavailable' }
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['found']) {
        throw 'processing.driver_schema_incompatible'
    }
    return [bool] $observation.Data.found
}

<#
.SYNOPSIS
    Waits for an element to disappear, which is how a withdrawn action is observed.
#>
function Wait-ProcessingGone {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'processing.observation_unavailable' }
    return $true
}

<#
.SYNOPSIS
    The accessible name of the one pending row for this scenario's fixture.
#>
function Get-ProcessingRowName {
    param([Parameter(Mandatory)][string] $FailureCode)

    $nodes = Read-ProcessingNodes
    $row = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType ([string] $inboxRow.nativeControlType) `
        -NamePrefix $rowPrefix -NameContains $inboxFilename -FailureCode $FailureCode
    if ($null -eq $row) { return '' }
    return (Get-UiAcceptanceNodeProperty -Node $row -Name 'name')
}

<#
.SYNOPSIS
    Resolves the pending row's semantic selector from a fresh inspection.
#>
function Get-ProcessingRowSelector {
    $nodes = Read-ProcessingNodes
    $row = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType ([string] $inboxRow.nativeControlType) `
        -NamePrefix $rowPrefix -NameContains $inboxFilename -FailureCode 'processing.row_ambiguous'
    if ($null -eq $row) { throw 'processing.row_missing' }
    return (Get-UiAcceptanceNodeSelector -Node $row -FailureCode 'processing.row_missing')
}

<#
.SYNOPSIS
    Whether an element is currently present and enabled, read as an observation.
#>
function Test-ProcessingActionReady {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][int] $Seconds)

    return (Test-ProcessingWait -Seconds $Seconds -Arguments @(
            'ui','wait-for',$AutomationId,'--property','IsEnabled','--value','True'))
}

<#
.SYNOPSIS
    Samples the live states a bounded number of times and records what was actually seen.

.DESCRIPTION
    Each sample is a real driver round-trip, so the loop needs no fixed delay. States that never
    appear are reported as unobserved, because a brief state that was not seen is not evidence.
#>
function Get-ProcessingStateSamples {
    param([Parameter(Mandatory)][int] $Samples, [Parameter(Mandatory)][string] $Label)

    $seen = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $Samples; $index++) {
        $name = Get-ProcessingRowName -FailureCode 'processing.row_missing'
        if ([string]::IsNullOrWhiteSpace($name)) { break }
        if (-not $seen.Contains($name)) { $seen.Add($name) }
    }
    return [pscustomobject] @{ Label = $Label; Samples = $seen.ToArray() }
}

<#
.SYNOPSIS
    Takes a read-only business snapshot of the owned root and records it as redacted evidence.
#>
function Read-ProcessingSnapshot {
    param([Parameter(Mandatory)][string] $EvidenceId)

    $raw = & $SupportPath snapshot --data-root $OwnedRoot --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'processing.snapshot_unavailable' }
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
            $fields
        })
    }) -Depth 12
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $relative), $text, [Text.UTF8Encoding]::new($false))
    $artifacts.Add($relative)
    return $data
}

function ConvertTo-ProcessingEvidenceNodes {
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

function Save-ProcessingEvidence {
    param([Parameter(Mandatory)][string] $EvidenceId, [Parameter(Mandatory)][object[]] $Nodes)
    return (Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory -EvidenceId $EvidenceId `
        -Nodes (ConvertTo-ProcessingEvidenceNodes -Nodes $Nodes) -RunToken $RunToken -OwnedRoot $OwnedRoot)
}

function Save-ProcessingScreenshot {
    param([Parameter(Mandatory)][string] $Selector, [Parameter(Mandatory)][string] $Name)

    $source = Join-Path $OwnedRoot ('screenshots/' + $Name + '.png')
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($source))
    $captured = Invoke-WinAppObservation -Arguments @('ui','screenshot',$Selector,'--output',$source) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($captured.Status -cne 'Passed' -or -not [IO.File]::Exists($source)) {
        throw 'processing.screenshot_unavailable'
    }
    Assert-UiOwnedEvidenceFile -Path $source -OwnedRoot $OwnedRoot
    $bytes = [IO.File]::ReadAllBytes($source)
    Assert-UiPngBytes -Bytes $bytes
    $relative = $Name + '.png'
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory $relative), $bytes)
    $artifacts.Add($relative)
    return $relative
}

function Read-ProcessingInboxFiles {
    param([Parameter(Mandatory)][string] $InboxDirectory)
    $files = @()
    foreach ($path in @([IO.Directory]::EnumerateFiles($InboxDirectory, '*', [IO.SearchOption]::AllDirectories))) {
        $files += [pscustomobject] @{
            Name = [IO.Path]::GetFileName($path)
            Hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }
    return $files
}

$failureCode = 'processing.unexpected'
$blockedFailureCodes = @(
    'processing.desktop_unavailable',
    'processing.fixture_missing',
    'processing.fixture_mismatch',
    'processing.fixture_target',
    'processing.snapshot_unavailable'
)

try {
    foreach ($requiredInput in @($ExecutablePath, $ContractPath, $OwnedRoot, $OutputDirectory, $SupportPath, $WinAppPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredInput)) { throw 'processing.invalid_input' }
    }
    if ([IO.Path]::GetFileName($ExecutablePath) -ine 'TraceFold.Media.exe') { throw 'processing.invalid_product' }
    if (-not [IO.File]::Exists($ContractPath)) { throw 'processing.contract_missing' }
    if (-not [IO.File]::Exists($SupportPath) -or -not [IO.File]::Exists($WinAppPath)) { throw 'processing.tooling_missing' }

    $contract = ConvertFrom-UiAcceptanceJson -Text ([IO.File]::ReadAllText($ContractPath)) -Source 'uia contract'
    Assert-UiAcceptanceFields -InputObject $contract -Required @('entries','dynamicRows','fixtureAssets','sessionPreparation')

    $entryIndex = @{}
    foreach ($entry in @($contract.entries)) {
        Assert-UiAcceptanceFields -InputObject $entry -Required @('automationId','name','requiredPatterns')
        $entryIndex[[string] $entry.automationId] = $entry
    }
    foreach ($requiredId in @('IngestButton','InboxStartProcessingButton','InboxRetryProcessingButton',
            'InboxProcessingStep','InboxProcessingProgress')) {
        if (-not $entryIndex.ContainsKey($requiredId)) { throw ('processing.contract_missing_entry.' + $requiredId) }
    }

    $preparation = $contract.sessionPreparation
    Assert-UiAcceptanceFields -InputObject $preparation -Required @(
        'seedFilenames','savedStatusText','savedStatusAutomationId','settingsNavigationAutomationId',
        'inboxNavigationAutomationId','saveFolderSettingsAutomationId','scanTriggerAutomationId','folderInputs')

    # The pending row and its filename come from the declared video row, so no localized literal and
    # no per-run identifier is hard-coded here.
    $inboxRows = @($contract.dynamicRows | Where-Object { [string] $_.phase -ceq 'inbox-seeded' })
    if ($inboxRows.Count -ne 1) { throw 'processing.contract_empty' }
    $inboxRow = $inboxRows[0]
    Assert-UiAcceptanceFields -InputObject $inboxRow -Required @('fixedAccessibleName','fixedFilename','nativeControlType')

    $inboxFilename = 'broken.mp4'
    $companionFilename = 'broken.m4a'
    $pairedVideo = 'paired.mp4'
    $pairedAudio = 'paired.m4a'
    $rowPrefix = ([string] $inboxRow.fixedAccessibleName).Replace(
        [string] $inboxRow.fixedFilename, $inboxFilename)

    $assets = @($contract.fixtureAssets)
    $verified = @{}
    foreach ($name in @($inboxFilename, $companionFilename, $pairedVideo, $pairedAudio)) {
        $matches = @($assets | Where-Object { [string] $_.fixedFilename -ceq $name })
        if ($matches.Count -ne 1) { throw 'processing.fixture_missing' }
        $verified[$name] = Test-UiAcceptanceFixtureAsset -Asset $matches[0] -RepositoryRoot $repositoryRoot
    }
    # The invalid pair must be genuinely different content from the valid pair, otherwise the
    # failure would not be produced by the bytes.
    if ($verified[$inboxFilename].Sha256 -ceq $verified[$pairedVideo].Sha256) {
        throw 'processing.fixture_identical'
    }

    $folderInputs = @($preparation.folderInputs)
    if ($folderInputs.Count -eq 0) { throw 'processing.contract_empty' }

    [void][IO.Directory]::CreateDirectory($OutputDirectory)

    Reset-UiAcceptanceMutationSteps
    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId

    $waited = Invoke-WinAppObservation -Arguments @('ui','wait-for','TraceFoldMainWindow') `
        -ProcessId $productProcessId -TimeoutSeconds $WaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($waited.Status -cne 'Passed' -or $null -eq $waited.Data -or $waited.Data.found -ne $true) {
        throw 'processing.window_unavailable'
    }

    # --- owned preparation: three directories through the product's own Settings UI ---
    $folders = @{}
    foreach ($folderInput in $folderInputs) {
        Assert-UiAcceptanceFields -InputObject $folderInput -Required @('key','automationId','relativeDirectory')
        $relative = [string] $folderInput.relativeDirectory
        if ($relative -cnotmatch '^[a-z0-9][a-z0-9-]*$') { throw 'processing.fixture_target' }
        $directory = [IO.Path]::GetFullPath((Join-Path $OwnedRoot $relative))
        $rootBoundary = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $directory.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'processing.fixture_target' }
        [void][IO.Directory]::CreateDirectory($directory)
        $folders[[string] $folderInput.key] = $directory
    }

    foreach ($folderInput in $folderInputs) {
        $path = $folders[[string] $folderInput.key]
        $null = Invoke-ProcessingAction -Step ('set-' + [string] $folderInput.key + '-folder') `
            -Arguments @('ui','set-value',[string] $folderInput.automationId,$path) -Code 'processing.folder_settings'
        $readBack = Invoke-WinAppObservation -Arguments @('ui','get-value',[string] $folderInput.automationId) `
            -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
            -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($readBack.Status -cne 'Passed' -or $null -eq $readBack.Data -or
            $null -eq $readBack.Data.PSObject.Properties['text'] -or [string] $readBack.Data.text -cne $path) {
            throw 'processing.folder_settings'
        }
    }
    $null = Invoke-ProcessingAction -Step 'save-folder-settings' `
        -Arguments @('ui','invoke',[string] $preparation.saveFolderSettingsAutomationId) -Code 'processing.folder_settings'
    $status = Invoke-WinAppObservation -Arguments @('ui','get-value',[string] $preparation.savedStatusAutomationId) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($status.Status -cne 'Passed' -or $null -eq $status.Data -or
        [string] $status.Data.text -cne [string] $preparation.savedStatusText) { throw 'processing.folder_settings' }

    # --- the invalid pair is scanned as one pending candidate ---
    foreach ($name in @($inboxFilename, $companionFilename)) {
        [IO.File]::Copy($verified[$name].Source, (Join-Path $folders['inbox'] $name), $false)
    }
    $null = Invoke-ProcessingAction -Step 'open-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) -Code 'processing.inbox_navigation'
    $null = Invoke-ProcessingAction -Step 'scan' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) -Code 'processing.scan_trigger'
    if (-not (Test-ProcessingWait -Arguments @('ui','wait-for',$rowPrefix) -Seconds $RowWaitTimeoutSeconds)) {
        throw 'processing.row_missing'
    }

    $beforeSnapshot = Read-ProcessingSnapshot -EvidenceId 'processing-before'

    # --- start processing through the product's own action ---
    $null = Invoke-ProcessingAction -Step 'select-row' -Arguments @('ui','invoke',(Get-ProcessingRowSelector)) `
        -Code 'processing.row_selection'
    if (-not (Test-ProcessingActionReady -AutomationId 'InboxStartProcessingButton' -Seconds $ProcessingWaitSeconds)) {
        throw 'processing.start_not_offered'
    }
    $null = Invoke-ProcessingAction -Step 'start' -Arguments @('ui','invoke','InboxStartProcessingButton') -Code 'processing.start'

    # Whatever brief states actually appear are sampled; the rest are reported as unobserved.
    $startSamples = Get-ProcessingStateSamples -Samples $StateSampleBudget -Label 'after-start'

    # A real failure is what offers the retry action. Nothing else is accepted as failure evidence.
    if (-not (Test-ProcessingActionReady -AutomationId 'InboxRetryProcessingButton' -Seconds $ProcessingWaitSeconds)) {
        throw 'processing.failure_not_observed'
    }
    $failedRowName = Get-ProcessingRowName -FailureCode 'processing.row_missing'
    $failedNodes = Read-ProcessingNodes
    $null = Save-ProcessingScreenshot -Selector 'InboxRetryProcessingButton' -Name 'processing-failed'
    $failedTree = Save-ProcessingEvidence -EvidenceId 'processing-failed' -Nodes $failedNodes
    $artifacts.Add($failedTree)
    $failedSnapshot = Read-ProcessingSnapshot -EvidenceId 'processing-failed'

    # Both sources must have survived the failure untouched.
    $afterFailure = Read-ProcessingInboxFiles -InboxDirectory $folders['inbox']
    foreach ($name in @($inboxFilename, $companionFilename)) {
        $found = @($afterFailure | Where-Object { $_.Name -ceq $name })
        if ($found.Count -ne 1) { throw 'processing.sources_missing' }
        if ($found[0].Hash -cne $verified[$name].Sha256) { throw 'processing.sources_changed' }
    }

    # --- replace only those two owned files with the valid pair, under the same names ---
    foreach ($pair in @(@{ name = $inboxFilename; source = $pairedVideo },
                        @{ name = $companionFilename; source = $pairedAudio })) {
        [IO.File]::Copy($verified[$pair.source].Source, (Join-Path $folders['inbox'] $pair.name), $true)
        $written = (Get-FileHash -LiteralPath (Join-Path $folders['inbox'] $pair.name) -Algorithm SHA256).Hash
        if ($written -cne $verified[$pair.source].Sha256) { throw 'processing.sources_changed' }
    }

    # Retry is still offered for the failed record, but it must reject the changed source identity.
    # This verifies the safety boundary without pretending that Retry can register replacement bytes.
    if (-not (Test-ProcessingActionReady -AutomationId 'InboxRetryProcessingButton' -Seconds $ProcessingWaitSeconds)) {
        throw 'processing.retry_not_offered'
    }
    $null = Invoke-ProcessingAction -Step 'retry' -Arguments @('ui','invoke','InboxRetryProcessingButton') -Code 'processing.retry'
    $postRetryNodes = Read-ProcessingNodes
    $postRetryTree = Save-ProcessingEvidence -EvidenceId 'processing-retry-dispatched' -Nodes $postRetryNodes
    $artifacts.Add($postRetryTree)
    if (-not (Test-ProcessingWait -Arguments @('ui','wait-for',$sourceChangedMessage) -Seconds $ProcessingWaitSeconds)) {
        throw 'processing.changed_source_requires_rescan'
    }
    $rejectedNodes = Read-ProcessingNodes
    $messageNodes = @($rejectedNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').Contains($sourceChangedMessage) })
    if ($messageNodes.Count -lt 1) { throw 'processing.changed_source_requires_rescan' }
    $rejectedTree = Save-ProcessingEvidence -EvidenceId 'processing-retry-rejected' -Nodes $rejectedNodes
    $artifacts.Add($rejectedTree)
    $rejectedSnapshot = Read-ProcessingSnapshot -EvidenceId 'processing-retry-rejected'
    $rejectedRowName = Get-ProcessingRowName -FailureCode 'processing.row_missing'
    if ($rejectedRowName -cne $failedRowName) { throw 'processing.changed_source_requires_rescan' }
    if ($rejectedSnapshot.tables.processing_jobs -ne $failedSnapshot.tables.processing_jobs) {
        throw 'processing.changed_source_requires_rescan'
    }
    $afterRejectedRetry = Read-ProcessingInboxFiles -InboxDirectory $folders['inbox']
    foreach ($pair in @(@{ name = $inboxFilename; source = $pairedVideo },
                        @{ name = $companionFilename; source = $pairedAudio })) {
        $found = @($afterRejectedRetry | Where-Object { $_.Name -ceq $pair.name })
        if ($found.Count -ne 1 -or $found[0].Hash -cne $verified[$pair.source].Sha256) {
            throw 'processing.sources_changed'
        }
    }

    # A scan records the new source identity and changes the offered action from Retry to Start.
    $null = Invoke-ProcessingAction -Step 'rescan' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) -Code 'processing.scan_trigger'
    if (-not (Test-ProcessingWait -Arguments @('ui','wait-for',$rowPrefix) -Seconds $RowWaitTimeoutSeconds)) {
        throw 'processing.row_missing'
    }
    $null = Invoke-ProcessingAction -Step 'reselect-row' -Arguments @('ui','invoke',(Get-ProcessingRowSelector)) `
        -Code 'processing.row_selection'
    if (-not (Test-ProcessingActionReady -AutomationId 'InboxStartProcessingButton' -Seconds $ProcessingWaitSeconds)) {
        throw 'processing.start_not_offered'
    }
    if (-not (Wait-ProcessingGone -Arguments @('ui','wait-for','InboxRetryProcessingButton','--gone') -Seconds $ProcessingWaitSeconds)) {
        throw 'processing.retry_still_offered'
    }
    $null = Invoke-ProcessingAction -Step 'start-recovered' `
        -Arguments @('ui','invoke','InboxStartProcessingButton') -Code 'processing.start'
    $recoverySamples = Get-ProcessingStateSamples -Samples $StateSampleBudget -Label 'after-rescan-start'

    # Recovery is the product completing the newly observed item: it becomes ingestable.
    if (-not (Test-ProcessingWait -Seconds $ProcessingWaitSeconds -Arguments @(
                'ui','wait-for','IngestButton','--property','IsEnabled','--value','True'))) {
        throw 'processing.recovered_not_observed'
    }
    $recoveredRowName = Get-ProcessingRowName -FailureCode 'processing.row_missing'
    $recoveredTree = Save-ProcessingEvidence -EvidenceId 'processing-recovered' -Nodes (Read-ProcessingNodes)
    $artifacts.Add($recoveredTree)
    $recoveredSnapshot = Read-ProcessingSnapshot -EvidenceId 'processing-recovered'

    $finalInbox = Read-ProcessingInboxFiles -InboxDirectory $folders['inbox']
    if ($finalInbox.Count -ne 2) { throw 'processing.sources_missing' }
    foreach ($pair in @(@{ name = $inboxFilename; source = $pairedVideo },
                        @{ name = $companionFilename; source = $pairedAudio })) {
        $found = @($finalInbox | Where-Object { $_.Name -ceq $pair.name })
        if ($found.Count -ne 1 -or $found[0].Hash -cne $verified[$pair.source].Sha256) {
            throw 'processing.sources_changed'
        }
    }

    # The state list is recorded with what was actually seen, so an unobserved brief state is
    # reported as unobserved rather than asserted.
    $observedNames = @($startSamples.Samples) + @($recoverySamples.Samples)
    $briefTokens = [ordered] @{
        analyzing = -join ([char[]] @(0x6B63,0x5728,0x5206,0x6790))
        queued    = -join ([char[]] @(0x6392,0x961F,0x4E2D))
        running   = -join ([char[]] @(0x5904,0x7406,0x4E2D))
    }
    $briefStatuses = [ordered] @{}
    foreach ($state in @($briefTokens.Keys)) {
        $token = [string] $briefTokens[$state]
        $matches = @($observedNames | Where-Object { $_.Contains($token) })
        $briefStatuses[$state] = if ($matches.Count -gt 0) { 'Observed' } else { 'NotObserved' }
    }
    $unobservedBriefStates = @($briefStatuses.Keys | Where-Object { $briefStatuses[$_] -ceq 'NotObserved' })
    $stateEvidence = [ordered] @{
        failedRowName    = $failedRowName
        recoveredRowName = $recoveredRowName
        samples          = @(
            [ordered] @{ label = $startSamples.Label; observed = @($startSamples.Samples) }
            [ordered] @{ label = $recoverySamples.Label; observed = @($recoverySamples.Samples) }
        )
        observedCount    = $startSamples.Samples.Count + $recoverySamples.Samples.Count
        retryOutcome     = 'SourceChanged'
        recoveryAction   = 'RescanThenStart'
        sourceHashes     = [ordered] @{
            afterFailure       = @($afterFailure | ForEach-Object { [ordered] @{ name = $_.Name; sha256 = $_.Hash } })
            afterRejectedRetry = @($afterRejectedRetry | ForEach-Object { [ordered] @{ name = $_.Name; sha256 = $_.Hash } })
            afterRecovery      = @($finalInbox | ForEach-Object { [ordered] @{ name = $_.Name; sha256 = $_.Hash } })
        }
        briefStatesLive  = $briefStatuses
        unobserved       = $unobservedBriefStates
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'processing-states.json'),
        (ConvertTo-Json -InputObject $stateEvidence -Depth 6), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('processing-states.json')

    return [pscustomobject] @{
        ScenarioId       = $ScenarioId
        Status           = 'Passed'
        Reason           = if ($unobservedBriefStates.Count -gt 0) {
            'processing.transient_ui_states_partially_observed'
        } else { '' }
        FailureObserved  = $true
        RecoveryObserved = $true
        ObservedStates   = @($startSamples.Samples) + @($recoverySamples.Samples)
        FailedRowName    = $failedRowName
        RecoveredRowName = $recoveredRowName
        BeforeTables     = ($beforeSnapshot.tables | ConvertTo-Json -Depth 6 -Compress)
        FailedTables     = ($failedSnapshot.tables | ConvertTo-Json -Depth 6 -Compress)
        RecoveredTables  = ($recoveredSnapshot.tables | ConvertTo-Json -Depth 6 -Compress)
        Artifacts        = @($artifacts)
    }
}
catch {
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or $code -notlike 'processing.*') {
        $cause = ConvertTo-UiAcceptanceSlug -Value ([string] $code)
        if ($cause.Length -gt 120) { $cause = $cause.Substring(0, 120) }
        $code = 'processing.unexpected'
        if (-not [string]::IsNullOrWhiteSpace($cause)) { $code = $code + '.' + $cause }
    }
    $status = if ($blockedFailureCodes -ccontains $code) { 'Blocked' } else { 'Failed' }
    return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = $status; Reason = $code; Artifacts = @($artifacts) }
}
