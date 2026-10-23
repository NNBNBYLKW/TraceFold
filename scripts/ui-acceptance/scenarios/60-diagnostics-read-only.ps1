# TraceFold UI acceptance - independent read-only diagnostics scenario (Task 12, first slice).
#
# This scenario owns a fresh owned root and never shares state with the main chain. It forms one
# verifiable formal asset first, because the diagnostics run must be shown not to change any
# business fact, and a run over an empty root would prove much less.
#
# Shape: configure the three owned directories through the product's own Settings UI, seed one
# hash-verified fixture, scan and ingest it once, then exercise Settings > System diagnostics.
#
# Read-only guarantees:
#   * Everything before the diagnostics marker prepares the owned root; from the marker on, the
#     scenario only observes and invokes, never sets a value or types.
#   * Before and after the diagnostics run it takes a read-only snapshot and re-hashes the formal
#     file, so a run that mutated business state cannot pass.
#   * The diagnostic categories, their status text and the overall status are read from the live
#     tree; refresh is dispatched exactly once and its completion is correlated with product logs.
#   * Live UI cancellation is explicitly NotRun. A gated C# view model test covers cancellation.
#
# Bans: no coordinates, no sleeps, no termination by process name, no shell folder picker, no
# direct database or service access, and no reuse of another scenario's root.
#
# Host seams mirror the other scenarios so a dispatcher can drive this file against the fake driver.

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
    [int] $DiagnosticsWaitSeconds = 120,
    [int] $ObservationTimeoutSeconds = 20,
    [int] $InspectDepth = 20,
    # The product performs the ingest on its own UI thread, so the driver call that triggers it can
    # block for several seconds while the file is copied and recorded. A bounded but generous
    # budget is required; a timeout still fails the step rather than resending it.
    [int] $MutationTimeoutMs = 60000,
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

# Localized strings are built from code points so this file stays pure ASCII and is therefore read
# identically by PowerShell 7 and Windows PowerShell 5.1.
$notCheckedText = [string]([char]0x5C1A + [char]0x672A + [char]0x68C0 + [char]0x67E5)
$recentCheckPrefix = [string]([char]0x6700 + [char]0x8FD1 + [char]0x68C0 + [char]0x67E5)

# The five diagnostic categories, each with the AutomationId the view model assigns it.
$diagnosticGroupIds = @(
    'DiagnosticsFoldersGroup',
    'DiagnosticsDatabaseGroup',
    'DiagnosticsToolsGroup',
    'DiagnosticsBackgroundGroup',
    'DiagnosticsRuntimeGroup'
)

$productProcessId = 0
$launched = $null
$artifacts = [System.Collections.Generic.List[string]]::new()

function Assert-DiagResult {
    param($Result, [string] $Code)
    if ($null -eq $Result -or $Result.Status -cne 'Passed') { throw $Code }
}

function Invoke-DiagAction {
    param([string] $Step, [string[]] $Arguments, [string] $Code)
    $result = Invoke-WinAppMutation -StepId ('diagnostics.' + $Step) -Arguments $Arguments `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -TimeoutMs $MutationTimeoutMs `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($result.Status -cne 'Passed') {
        $detail = 'driver_unavailable'
        if ($null -ne $result.Data -and $null -ne $result.Data.PSObject.Properties['error'] -and
            $null -ne $result.Data.error.PSObject.Properties['code']) {
            $detail = ConvertTo-UiAcceptanceSlug -Value ([string] $result.Data.error.code)
        }
        # The exit code stays in the code so a failure is diagnosable from the durable report
        # alone. The driver's message is only ever added as a redacted slug, never verbatim.
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

function Read-DiagNodes {
    return @(Read-UiAcceptanceTree -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -FailureCode 'diagnostics.inspect_unavailable' `
        -Depth $InspectDepth -TimeoutSeconds $ObservationTimeoutSeconds)
}

<#
.SYNOPSIS
    Reads one element's value through the driver, as an observation.
#>
function Read-DiagValue {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][string] $Code)

    $observation = Invoke-WinAppObservation -Arguments @('ui','get-value',$AutomationId) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed' -or $null -eq $observation.Data) { throw $Code }
    if ($null -eq $observation.Data.PSObject.Properties['text']) { throw 'diagnostics.driver_schema_incompatible' }
    return [string] $observation.Data.text
}

<#
.SYNOPSIS
    Waits for a driver-observed condition, letting the driver do the polling.

.DESCRIPTION
    The toolkit retries only its allowlisted observations, and a wait-for call already polls the
    window itself, so waiting here needs no repeated observation of its own.
#>
function Test-DiagWait {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    # A wait whose budget elapses is a negative answer, not a harness failure: the driver polls to
    # its own deadline and the caller decides what an unmet condition means. Any other non-passed
    # status is a real observation failure and is surfaced as such.
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'diagnostics.observation_unavailable' }
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['found']) {
        throw 'diagnostics.driver_schema_incompatible'
    }
    return [bool] $observation.Data.found
}


<#
.SYNOPSIS
    Takes a read-only business snapshot of the owned root and records it as redacted evidence.
#>
function Read-DiagSnapshot {
    param([Parameter(Mandatory)][string] $EvidenceId)

    $raw = & $SupportPath snapshot --data-root $OwnedRoot --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'diagnostics.snapshot_unavailable' }
    $data = ConvertFrom-UiAcceptanceJson -Text ($raw | Out-String) -Source 'snapshot'
    Assert-UiAcceptanceFields -InputObject $data -Required @('tables','taxonomy','formalAssets')
    $relative = (ConvertTo-UiAcceptanceSlug -Value $EvidenceId) + '-snapshot.json'
    $text = ConvertTo-Json -InputObject ([ordered] @{
        tables       = $data.tables
        taxonomy     = $data.taxonomy
        formalAssets = @($data.formalAssets | ForEach-Object {
            $asset = $_
            $fields = [ordered] @{}
            foreach ($field in @('actualFilename','displayName','mediaKind')) {
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

<#
.SYNOPSIS
    Reads the live state of the diagnostics surface.
#>
function Read-DiagSurface {
    param([Parameter(Mandatory)][int] $ProcessId)

    $nodes = Read-DiagNodes
    $present = @()
    foreach ($id in $diagnosticGroupIds) {
        if ($null -ne (Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $id -FailureCode 'diagnostics.group_ambiguous')) {
            $present += $id
        }
    }
    return [pscustomobject] @{
        Nodes           = $nodes
        PresentGroups   = $present
        OverallStatus   = Read-DiagValue -AutomationId 'DiagnosticsOverallStatus' -Code 'diagnostics.overall_status'
        LastChecked     = Read-DiagValue -AutomationId 'DiagnosticsLastChecked' -Code 'diagnostics.last_checked'
    }
}

# Run IDs stay in memory as correlation keys. Only aggregate facts are written to evidence.
# Serilog's shared file sink can be read while the owned product is still running.
function Read-DiagRunEvents {
    $events = [ordered]@{ Started = @(); Completed = @(); Cancelled = @() }
    $logDirectory = Join-Path $OwnedRoot 'logs'
    if (-not [IO.Directory]::Exists($logDirectory)) { return [pscustomobject] $events }
    foreach ($log in @(Get-ChildItem -LiteralPath $logDirectory -Filter 'tracefold-*.log' -File)) {
        $stream = [IO.FileStream]::new($log.FullName, [IO.FileMode]::Open,
            [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        try {
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 4096, $true)
            try { $content = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        finally { $stream.Dispose() }
        foreach ($match in [regex]::Matches($content,
            'SystemDiagnosticsRun(Started|Completed|Cancelled)[^\r\n]*?([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})')) {
            $kind = [string] $match.Groups[1].Value
            $events[$kind] += [string] $match.Groups[2].Value
        }
    }
    return [pscustomobject] $events
}

$failureCode = 'diagnostics.unexpected'
$blockedFailureCodes = @(
    'diagnostics.desktop_unavailable',
    'diagnostics.fixture_missing',
    'diagnostics.fixture_mismatch',
    'diagnostics.fixture_target',
    'diagnostics.snapshot_unavailable'
)

try {
    foreach ($requiredInput in @($ExecutablePath, $ContractPath, $OwnedRoot, $OutputDirectory, $SupportPath, $WinAppPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredInput)) { throw 'diagnostics.invalid_input' }
    }
    if ([IO.Path]::GetFileName($ExecutablePath) -ine 'TraceFold.Media.exe') { throw 'diagnostics.invalid_product' }
    if (-not [IO.File]::Exists($ContractPath)) { throw 'diagnostics.contract_missing' }
    if (-not [IO.File]::Exists($SupportPath) -or -not [IO.File]::Exists($WinAppPath)) { throw 'diagnostics.tooling_missing' }

    $contract = ConvertFrom-UiAcceptanceJson -Text ([IO.File]::ReadAllText($ContractPath)) -Source 'uia contract'
    Assert-UiAcceptanceFields -InputObject $contract -Required @('entries','dynamicRows','fixtureAssets','sessionPreparation')

    $preparation = $contract.sessionPreparation
    Assert-UiAcceptanceFields -InputObject $preparation -Required @(
        'seedFilenames','savedStatusText','savedStatusAutomationId','settingsNavigationAutomationId',
        'inboxNavigationAutomationId','saveFolderSettingsAutomationId','scanTriggerAutomationId','folderInputs')
    $savedStatusText = [string] $preparation.savedStatusText
    if ([string]::IsNullOrWhiteSpace($savedStatusText)) { throw 'diagnostics.contract_empty' }

    # The inbox row for the image is derived from the declared video row, so no localized literal
    # is hard-coded here.
    $inboxRows = @($contract.dynamicRows | Where-Object { [string] $_.phase -ceq 'inbox-seeded' })
    if ($inboxRows.Count -ne 1) { throw 'diagnostics.contract_empty' }
    $inboxRow = $inboxRows[0]
    Assert-UiAcceptanceFields -InputObject $inboxRow -Required @('fixedAccessibleName','fixedFilename','nativeControlType')
    $photoFilename = 'photo.jpg'
    # Ordinal, literal substitution: a regex replacement would keep the escape characters and the
    # derived prefix would never match the live accessible name.
    $photoRowPrefix = ([string] $inboxRow.fixedAccessibleName).Replace(
        [string] $inboxRow.fixedFilename, $photoFilename)

    $photoAsset = @($contract.fixtureAssets | Where-Object { [string] $_.fixedFilename -ceq $photoFilename })
    if ($photoAsset.Count -ne 1) { throw 'diagnostics.fixture_missing' }
    $verifiedPhoto = Test-UiAcceptanceFixtureAsset -Asset $photoAsset[0] -RepositoryRoot $repositoryRoot

    $folderInputs = @($preparation.folderInputs)
    if ($folderInputs.Count -eq 0) { throw 'diagnostics.contract_empty' }

    [void][IO.Directory]::CreateDirectory($OutputDirectory)

    Reset-UiAcceptanceMutationSteps
    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId

    $waited = Invoke-WinAppObservation -Arguments @('ui','wait-for','TraceFoldMainWindow') `
        -ProcessId $productProcessId -TimeoutSeconds $WaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($waited.Status -cne 'Passed' -or $null -eq $waited.Data -or $waited.Data.found -ne $true) {
        throw 'diagnostics.window_unavailable'
    }

    # --- owned preparation: three directories through the product's own Settings UI ---
    $folders = @{}
    foreach ($folderInput in $folderInputs) {
        Assert-UiAcceptanceFields -InputObject $folderInput -Required @('key','automationId','relativeDirectory')
        $relative = [string] $folderInput.relativeDirectory
        if ($relative -cnotmatch '^[a-z0-9][a-z0-9-]*$') { throw 'diagnostics.fixture_target' }
        $directory = [IO.Path]::GetFullPath((Join-Path $OwnedRoot $relative))
        $rootBoundary = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $directory.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'diagnostics.fixture_target' }
        [void][IO.Directory]::CreateDirectory($directory)
        $folders[[string] $folderInput.key] = $directory
    }

    foreach ($folderInput in $folderInputs) {
        $path = $folders[[string] $folderInput.key]
        $null = Invoke-DiagAction -Step ('set-' + [string] $folderInput.key + '-folder') `
            -Arguments @('ui','set-value',[string] $folderInput.automationId,$path) `
            -Code 'diagnostics.folder_settings'
        $readBack = Read-DiagValue -AutomationId ([string] $folderInput.automationId) -Code 'diagnostics.folder_settings'
        if ($readBack -cne $path) { throw 'diagnostics.folder_settings' }
    }
    $null = Invoke-DiagAction -Step 'save-folder-settings' `
        -Arguments @('ui','invoke',[string] $preparation.saveFolderSettingsAutomationId) `
        -Code 'diagnostics.folder_settings'
    $status = Read-DiagValue -AutomationId ([string] $preparation.savedStatusAutomationId) -Code 'diagnostics.folder_settings'
    if ($status -cne $savedStatusText) { throw 'diagnostics.folder_settings' }

    # Only the hash-verified fixture is seeded, and only into the owned inbox.
    [IO.File]::Copy($verifiedPhoto.Source, (Join-Path $folders['inbox'] $photoFilename), $false)

    $null = Invoke-DiagAction -Step 'open-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) -Code 'diagnostics.inbox_navigation'
    $null = Invoke-DiagAction -Step 'scan-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) -Code 'diagnostics.scan_trigger'
    $appeared = Invoke-WinAppObservation -Arguments @('ui','wait-for',$photoRowPrefix) `
        -ProcessId $productProcessId -TimeoutSeconds $RowWaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($appeared.Status -cne 'Passed' -or $null -eq $appeared.Data -or $appeared.Data.found -ne $true) {
        throw 'diagnostics.row_missing'
    }

    $rowResolver = {
        $raw = Read-DiagNodes
        $found = Find-UiAcceptanceUniqueNode -Nodes $raw -NativeType ([string] $inboxRow.nativeControlType) `
            -NamePrefix $photoRowPrefix -NameContains $photoFilename -FailureCode 'diagnostics.row_ambiguous'
        if ($null -eq $found) { throw 'diagnostics.row_missing' }
        return (Get-UiAcceptanceNodeSelector -Node $found -FailureCode 'diagnostics.row_missing')
    }
    $rowSelector = & $rowResolver
    $null = Invoke-DiagAction -Step 'select-row' -Arguments @('ui','invoke',$rowSelector) -Code 'diagnostics.row_selection'

    $inspector = Read-DiagNodes
    $ingestNode = Find-UiAcceptanceUniqueNode -Nodes $inspector -AutomationId 'IngestButton' -FailureCode 'diagnostics.inspector_ambiguous'
    if ($null -eq $ingestNode) { throw 'diagnostics.inspector_missing' }

    # A newly scanned item first reports that its file is still settling, and the ingest command is
    # disabled until it is ingestable. The command must be observed enabled before it is invoked
    # once - invoking a disabled button has no Invoke pattern and is not a retryable condition.
    if (-not (Test-DiagWait -Seconds $DiagnosticsWaitSeconds -Arguments @(
                'ui','wait-for','IngestButton','--property','IsEnabled','--value','True'))) {
        throw 'diagnostics.ingest_not_ready'
    }

    $null = Invoke-DiagAction -Step 'ingest' -Arguments @('ui','invoke','IngestButton') -Code 'diagnostics.ingest'
    $gone = Invoke-WinAppObservation -Arguments @('ui','wait-for',$photoFilename,'--gone') `
        -ProcessId $productProcessId -TimeoutSeconds $RowWaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($gone.Status -cne 'Passed') { throw 'diagnostics.ingest_incomplete' }

    # The formal file must exist in the owned library; its hash is the business fact the
    # diagnostics run is not allowed to change.
    $formalPhoto = @([IO.Directory]::EnumerateFiles($folders['library'], $photoFilename,
        [IO.SearchOption]::AllDirectories))
    if ($formalPhoto.Count -ne 1) { throw 'diagnostics.formal_asset' }
    $hashBefore = (Get-FileHash -LiteralPath $formalPhoto[0] -Algorithm SHA256).Hash
    if ($hashBefore -cne $verifiedPhoto.Sha256) { throw 'diagnostics.formal_asset_mismatch' }

    $beforeSnapshot = Read-DiagSnapshot -EvidenceId 'diagnostics-before'

    # --- diagnostics phase ---
    $null = Invoke-DiagAction -Step 'open-settings' `
        -Arguments @('ui','invoke',[string] $preparation.settingsNavigationAutomationId) -Code 'diagnostics.settings_navigation'
    $null = Invoke-DiagAction -Step 'select-diagnostics-tab' `
        -Arguments @('ui','invoke','SystemDiagnosticsTab') -Code 'diagnostics.tab'

    # The first run starts when the tab is first selected; wait for it to settle before reading.
    if (-not (Test-DiagWait -Arguments @('ui','wait-for','DiagnosticsRuntimeGroup') -Seconds $DiagnosticsWaitSeconds)) {
        throw 'diagnostics.first_run'
    }
    $firstRun = Read-DiagSurface -ProcessId $productProcessId
    if ($firstRun.PresentGroups.Count -ne $diagnosticGroupIds.Count) { throw 'diagnostics.first_run' }
    if (-not $firstRun.LastChecked.Contains($recentCheckPrefix)) { throw 'diagnostics.last_checked' }
    if ([string] $firstRun.OverallStatus -ceq $notCheckedText) { throw 'diagnostics.overall_status' }

    # The categories carry their status in the accessible name, so a status is verifiable per group.
    foreach ($id in $diagnosticGroupIds) {
        $group = Find-UiAcceptanceUniqueNode -Nodes $firstRun.Nodes -AutomationId $id -FailureCode 'diagnostics.group_ambiguous'
        $name = Get-UiAcceptanceNodeProperty -Node $group -Name 'name'
        if ([string]::IsNullOrWhiteSpace($name)) { throw 'diagnostics.group_status' }
    }

    $screenshotSource = Join-Path $OwnedRoot 'screenshots/diagnostics.png'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($screenshotSource))
    $captured = Invoke-WinAppObservation -Arguments @('ui','screenshot','SystemDiagnosticsTab','--output',$screenshotSource) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($captured.Status -cne 'Passed' -or -not [IO.File]::Exists($screenshotSource)) { throw 'diagnostics.screenshot_unavailable' }
    Assert-UiOwnedEvidenceFile -Path $screenshotSource -OwnedRoot $OwnedRoot
    $bytes = [IO.File]::ReadAllBytes($screenshotSource)
    Assert-UiPngBytes -Bytes $bytes
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'diagnostics-detail.png'), $bytes)
    $artifacts.Add('diagnostics-detail.png')

    # Correlate this one refresh with the product's completion event. The live gate does not
    # claim to observe cancellation, which finishes before a second driver command can navigate.
    $eventsBefore = Read-DiagRunEvents
    $null = Invoke-DiagAction -Step 'refresh' `
        -Arguments @('ui','invoke','DiagnosticsRefreshButton') -Code 'diagnostics.refresh'
    if (-not (Test-DiagWait -Seconds $DiagnosticsWaitSeconds -Arguments @(
                'ui','wait-for','DiagnosticsRefreshButton','--property','IsEnabled','--value','True'))) {
        throw 'diagnostics.refresh_incomplete'
    }
    $refreshed = Read-DiagSurface -ProcessId $productProcessId
    if ($refreshed.PresentGroups.Count -ne $diagnosticGroupIds.Count -or
        [string] $refreshed.OverallStatus -ceq $notCheckedText -or
        -not $refreshed.LastChecked.Contains($recentCheckPrefix)) {
        throw 'diagnostics.refresh_incomplete'
    }
    $eventsAfter = Read-DiagRunEvents
    $newRuns = @($eventsAfter.Started | Where-Object { $eventsBefore.Started -cnotcontains $_ })
    if ($newRuns.Count -ne 1 -or $eventsAfter.Completed -cnotcontains $newRuns[0]) {
        throw 'diagnostics.refresh_unconfirmed'
    }
    $eventEvidence = [ordered]@{
        refreshObserved = $true
        refreshCompleted = $true
        liveUiStatus = 'NotRun'
        liveUiReason = 'diagnostics_completed_before_navigation'
        controlledTest = 'SystemDiagnosticsViewModelTests.Cancellation_retains_the_previous_snapshot'
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'diagnostics-cancellation.json'),
        (ConvertTo-Json -InputObject $eventEvidence), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('diagnostics-cancellation.json')

    # A completed snapshot must survive navigation away from and back to Settings.
    $null = Invoke-DiagAction -Step 'refresh-leave-settings' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) -Code 'diagnostics.retention'
    if (-not (Test-DiagWait -Arguments @('ui','wait-for','InboxGrid') -Seconds $DiagnosticsWaitSeconds)) {
        throw 'diagnostics.retention_navigation'
    }
    $null = Invoke-DiagAction -Step 'refresh-return-settings' `
        -Arguments @('ui','invoke',[string] $preparation.settingsNavigationAutomationId) -Code 'diagnostics.retention'
    $null = Invoke-DiagAction -Step 'refresh-return-tab' `
        -Arguments @('ui','invoke','SystemDiagnosticsTab') -Code 'diagnostics.retention'

    if (-not (Test-DiagWait -Arguments @('ui','wait-for','DiagnosticsRuntimeGroup') -Seconds $DiagnosticsWaitSeconds)) {
        throw 'diagnostics.retention'
    }
    $retained = Read-DiagSurface -ProcessId $productProcessId
    if ($retained.PresentGroups.Count -ne $diagnosticGroupIds.Count -or
        $retained.LastChecked -cne $refreshed.LastChecked -or
        $retained.OverallStatus -cne $refreshed.OverallStatus) { throw 'diagnostics.retention' }
    foreach ($id in $diagnosticGroupIds) {
        $beforeGroup = Find-UiAcceptanceUniqueNode -Nodes $refreshed.Nodes -AutomationId $id -FailureCode 'diagnostics.retention'
        $afterGroup = Find-UiAcceptanceUniqueNode -Nodes $retained.Nodes -AutomationId $id -FailureCode 'diagnostics.retention'
        if ((Get-UiAcceptanceNodeProperty -Node $beforeGroup -Name 'name') -cne
            (Get-UiAcceptanceNodeProperty -Node $afterGroup -Name 'name')) {
            throw 'diagnostics.retention'
        }
    }

    # The pinned driver uses type/isEnabled/isOffscreen and flat x/y/width/height; the
    # evidence writer requires its reviewed normalized schema. Persist only stable diagnostic
    # elements so generated item identifiers and local diagnostic facts cannot escape.
    $evidenceIds = @($diagnosticGroupIds) + @(
        'DiagnosticsOverallStatus','DiagnosticsLastChecked','DiagnosticsRefreshButton')
    $diagnosticNodes = @($retained.Nodes | Where-Object {
        $evidenceIds -ccontains (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId')
    } | ForEach-Object {
        $node = $_
        [pscustomobject]@{
            automationId = Get-UiAcceptanceNodeProperty -Node $node -Name 'automationId'
            controlType = Get-UiAcceptanceNodeProperty -Node $node -Name 'type'
            name = Get-UiAcceptanceNodeProperty -Node $node -Name 'name'
            enabled = [bool]::Parse((Get-UiAcceptanceNodeProperty -Node $node -Name 'isEnabled'))
            offscreen = [bool]::Parse((Get-UiAcceptanceNodeProperty -Node $node -Name 'isOffscreen'))
            bounds = [pscustomobject]@{
                x = [int](Get-UiAcceptanceNodeProperty -Node $node -Name 'x')
                y = [int](Get-UiAcceptanceNodeProperty -Node $node -Name 'y')
                width = [int](Get-UiAcceptanceNodeProperty -Node $node -Name 'width')
                height = [int](Get-UiAcceptanceNodeProperty -Node $node -Name 'height')
            }
        }
    })
    if ($diagnosticNodes.Count -ne $evidenceIds.Count) { throw 'diagnostics.evidence_schema' }
    $treeRelative = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory `
        -EvidenceId 'diagnostics-surface' -Nodes $diagnosticNodes -RunToken $RunToken -OwnedRoot $OwnedRoot
    $artifacts.Add($treeRelative)

    # --- read-only facts: the run must not have changed any business state ---
    $afterSnapshot = Read-DiagSnapshot -EvidenceId 'diagnostics-after'
    $hashAfter = (Get-FileHash -LiteralPath $formalPhoto[0] -Algorithm SHA256).Hash
    if ($hashAfter -cne $hashBefore) { throw 'diagnostics.formal_asset_changed' }

    $beforeTables = ConvertTo-Json -InputObject $beforeSnapshot.tables -Depth 12 -Compress
    $afterTables = ConvertTo-Json -InputObject $afterSnapshot.tables -Depth 12 -Compress
    if ($beforeTables -cne $afterTables) { throw 'diagnostics.snapshot_changed' }
    $beforeTaxonomy = ConvertTo-Json -InputObject $beforeSnapshot.taxonomy -Depth 12 -Compress
    $afterTaxonomy = ConvertTo-Json -InputObject $afterSnapshot.taxonomy -Depth 12 -Compress
    if ($beforeTaxonomy -cne $afterTaxonomy) { throw 'diagnostics.snapshot_changed' }
    $beforeAssets = ConvertTo-Json -InputObject $beforeSnapshot.formalAssets -Depth 12 -Compress
    $afterAssets = ConvertTo-Json -InputObject $afterSnapshot.formalAssets -Depth 12 -Compress
    if ($beforeAssets -cne $afterAssets) { throw 'diagnostics.snapshot_changed' }
    return [pscustomobject] @{
        ScenarioId      = $ScenarioId
        Status          = 'Passed'
        Reason          = 'diagnostics.live_cancellation_not_run'
        DiagnosticGroups = $diagnosticGroupIds.Count
        OverallStatus   = [string] $retained.OverallStatus
        LiveUiCancellationStatus = 'NotRun'
        RetainedLastChecked  = [string] $retained.LastChecked
        Artifacts       = @($artifacts)
    }
}
catch {
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or $code -notlike 'diagnostics.*') {
        $cause = ConvertTo-UiAcceptanceSlug -Value ([string] $code)
        if ($cause.Length -gt 120) { $cause = $cause.Substring(0, 120) }
        $code = 'diagnostics.unexpected'
        if (-not [string]::IsNullOrWhiteSpace($cause)) { $code = $code + '.' + $cause }
    }
    $status = if ($blockedFailureCodes -ccontains $code) { 'Blocked' } else { 'Failed' }
    return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = $status; Reason = $code; Artifacts = @($artifacts) }
}
