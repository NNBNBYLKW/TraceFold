# TraceFold UI acceptance - independent "skip conflict" decision scenario (Task 12).
#
# One owned root, created for this run only. This scenario proves exactly one of the three conflict
# decisions. Replace is a separate scenario and is not claimed here.
#
# Shape: configure the three owned directories through the product's own Settings UI, ingest a
# verified image formally, copy the same verified image back into the owned inbox, scan, and let the
# ingest raise the comparison against the already registered asset. Capture the comparison and the
# incoming preview, take exactly one skip decision, then prove the outcome from read-only facts.
#
# Completion is the product's own outcome message, not the closing popup:
#   * A skip leaves the pending item in place, so the item leaving the live UI is the wrong signal
#     and is deliberately not awaited.
#   * The dialog closing only proves the overlay vanished; the asynchronous resolution may still be
#     committing. The run therefore waits for the product to report the skipped outcome before it
#     reads any final database or file fact.
#
# Read-only guarantees:
#   * The decision is the last write of the run. From the conflict marker on, nothing is set or
#     typed, and every conclusion is paired with a read-only snapshot or a file hash.
#   * If the conflict state changes between the observation and the decision, the run fails and the
#     decision is not re-issued; a failure or a timeout never re-sends it.
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
    [int] $ConflictWaitSeconds = 60,
    [int] $ObservationTimeoutSeconds = 20,
    [int] $InspectDepth = 20,
    # The product performs ingest and conflict resolution on its own UI thread, so the driver call
    # that triggers one can block for several seconds. A bounded but generous budget is required;
    # a timeout still fails the step rather than resending it.
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

# The product states this scenario observes, composed from code points so this file stays pure
# ASCII and is read identically by PowerShell 7 and Windows PowerShell 5.1.
function New-ConflictText {
    param([Parameter(Mandatory)][int[]] $CodePoints)

    $builder = [System.Text.StringBuilder]::new()
    foreach ($codePoint in $CodePoints) { [void] $builder.Append([char] $codePoint) }
    return $builder.ToString()
}

# The inbox row state while a conflict is open.
$conflictStateToken = New-ConflictText -CodePoints @(0x5B58, 0x5728, 0x51B2, 0x7A81)
# The inspector's own conflict line.
$conflictMessageToken = New-ConflictText -CodePoints @(0x540C, 0x540D, 0x6216, 0x91CD, 0x590D, 0x5185, 0x5BB9)

$productProcessId = 0
$launched = $null
$artifacts = [System.Collections.Generic.List[string]]::new()

function Assert-ConflictResult {
    param($Result, [string] $Code)
    if ($null -eq $Result -or $Result.Status -cne 'Passed') { throw $Code }
}

<#
.SYNOPSIS
    Dispatches one UI mutation under a step name unique to this scenario.

.DESCRIPTION
    Every mutation carries its own step id, because the toolkit records a step once and a repeated
    id is itself a defect. A failure keeps the exit code and a redacted slug of the driver message
    in the code, so the durable report is diagnosable without the raw output.
#>
function Invoke-ConflictAction {
    param([string] $Step, [string[]] $Arguments, [string] $Code)
    $result = Invoke-WinAppMutation -StepId ('conflict.' + $Step) -Arguments $Arguments `
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

function Read-ConflictNodes {
    return @(Read-UiAcceptanceTree -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -FailureCode 'conflict.inspect_unavailable' `
        -Depth $InspectDepth -TimeoutSeconds $ObservationTimeoutSeconds)
}

<#
.SYNOPSIS
    Reads one element's value through the driver, as an observation.
#>
function Read-ConflictValue {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][string] $Code)

    $observation = Invoke-WinAppObservation -Arguments @('ui','get-value',$AutomationId) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed' -or $null -eq $observation.Data) { throw $Code }
    if ($null -eq $observation.Data.PSObject.Properties['text']) { throw 'conflict.driver_schema_incompatible' }
    return [string] $observation.Data.text
}

<#
.SYNOPSIS
    Waits for a driver-observed condition, letting the driver do the polling.

.DESCRIPTION
    Only inspect, search, get-value, wait-for and screenshot observations may be retried, and a
    wait-for call already polls the window itself. A wait whose budget elapses is a negative
    answer; any other non-passed status is a real observation failure.
#>
function Test-ConflictWait {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'conflict.observation_unavailable' }
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['found']) {
        throw 'conflict.driver_schema_incompatible'
    }
    return [bool] $observation.Data.found
}

<#
.SYNOPSIS
    Waits for an element to disappear, which is how completion is observed for a finished ingest
    and for a dismissed dialog.

.DESCRIPTION
    A {@code --gone} wait reports whether the element was found, so its 'found' field is false on
    success; completion is therefore the observation itself succeeding. A wait whose budget
    elapses is a negative answer.
#>
function Wait-ConflictGone {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'conflict.observation_unavailable' }
    return $true
}

<#
.SYNOPSIS
    Captures one element to a redacted screenshot artifact.
#>
function Save-ConflictScreenshot {
    param([Parameter(Mandatory)][string] $Selector, [Parameter(Mandatory)][string] $Name)

    $source = Join-Path $OwnedRoot ('screenshots/' + $Name + '.png')
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($source))
    $captured = Invoke-WinAppObservation -Arguments @('ui','screenshot',$Selector,'--output',$source) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($captured.Status -cne 'Passed' -or -not [IO.File]::Exists($source)) {
        throw 'conflict.screenshot_unavailable'
    }
    Assert-UiOwnedEvidenceFile -Path $source -OwnedRoot $OwnedRoot
    $bytes = [IO.File]::ReadAllBytes($source)
    Assert-UiPngBytes -Bytes $bytes
    $relative = $Name + '.png'
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory $relative), $bytes)
    $artifacts.Add($relative)
    return $relative
}

<#
.SYNOPSIS
    Projects driver nodes into the reviewed shape the evidence writer accepts.

.DESCRIPTION
    The driver reports its own field names and only the fields an element actually has; the writer
    takes the normalized shape instead, so the translation happens here rather than in the writer.
#>
function ConvertTo-ConflictEvidenceNodes {
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

<#
.SYNOPSIS
    Takes a read-only business snapshot of the owned root and records it as redacted evidence.
#>
function Read-ConflictSnapshot {
    param([Parameter(Mandatory)][string] $EvidenceId)

    $raw = & $SupportPath snapshot --data-root $OwnedRoot --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'conflict.snapshot_unavailable' }
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
    Reads the owned formal storage as plain facts: one entry per file, with its hash.
#>
function Read-ConflictFormalFiles {
    param([Parameter(Mandatory)][string] $LibraryDirectory)

    $files = @()
    foreach ($path in @([IO.Directory]::EnumerateFiles($LibraryDirectory, '*', [IO.SearchOption]::AllDirectories))) {
        $files += [pscustomobject] @{
            Name = [IO.Path]::GetFileName($path)
            Hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }
    return $files
}

$failureCode = 'conflict.unexpected'
$blockedFailureCodes = @(
    'conflict.desktop_unavailable',
    'conflict.fixture_missing',
    'conflict.fixture_mismatch',
    'conflict.fixture_target',
    'conflict.snapshot_unavailable'
)

try {
    foreach ($requiredInput in @($ExecutablePath, $ContractPath, $OwnedRoot, $OutputDirectory, $SupportPath, $WinAppPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredInput)) { throw 'conflict.invalid_input' }
    }
    if ([IO.Path]::GetFileName($ExecutablePath) -ine 'TraceFold.Media.exe') { throw 'conflict.invalid_product' }
    if (-not [IO.File]::Exists($ContractPath)) { throw 'conflict.contract_missing' }
    if (-not [IO.File]::Exists($SupportPath) -or -not [IO.File]::Exists($WinAppPath)) { throw 'conflict.tooling_missing' }

    $contract = ConvertFrom-UiAcceptanceJson -Text ([IO.File]::ReadAllText($ContractPath)) -Source 'uia contract'
    Assert-UiAcceptanceFields -InputObject $contract -Required @('entries','dynamicRows','fixtureAssets','sessionPreparation')

    $entryIndex = @{}
    foreach ($entry in @($contract.entries)) {
        Assert-UiAcceptanceFields -InputObject $entry -Required @('automationId','name','requiredPatterns')
        if ($entryIndex.ContainsKey([string] $entry.automationId)) { throw 'conflict.contract_duplicate_entry' }
        $entryIndex[[string] $entry.automationId] = $entry
    }
    foreach ($requiredId in @('IngestButton','ResolveInboxConflictButton','ConflictComparisonDialog',
            'ConflictIncomingPreview','ConflictSkipButton')) {
        if (-not $entryIndex.ContainsKey($requiredId)) { throw ('conflict.contract_missing_entry.' + $requiredId) }
    }

    $preparation = $contract.sessionPreparation
    Assert-UiAcceptanceFields -InputObject $preparation -Required @(
        'seedFilenames','savedStatusText','savedStatusAutomationId','settingsNavigationAutomationId',
        'inboxNavigationAutomationId','saveFolderSettingsAutomationId','scanTriggerAutomationId','folderInputs')
    $savedStatusText = [string] $preparation.savedStatusText
    if ([string]::IsNullOrWhiteSpace($savedStatusText)) { throw 'conflict.contract_empty' }

    # The inbox row for the image is derived from the declared video row, so no localized literal
    # is hard-coded here. The substitution is ordinal: a regex replacement would keep the escapes.
    $inboxRows = @($contract.dynamicRows | Where-Object { [string] $_.phase -ceq 'inbox-seeded' })
    if ($inboxRows.Count -ne 1) { throw 'conflict.contract_empty' }
    $inboxRow = $inboxRows[0]
    Assert-UiAcceptanceFields -InputObject $inboxRow -Required @('fixedAccessibleName','fixedFilename','nativeControlType')
    $photoFilename = 'photo.jpg'
    $photoRowPrefix = ([string] $inboxRow.fixedAccessibleName).Replace(
        [string] $inboxRow.fixedFilename, $photoFilename)

    $photoAsset = @($contract.fixtureAssets | Where-Object { [string] $_.fixedFilename -ceq $photoFilename })
    if ($photoAsset.Count -ne 1) { throw 'conflict.fixture_missing' }
    $verifiedPhoto = Test-UiAcceptanceFixtureAsset -Asset $photoAsset[0] -RepositoryRoot $repositoryRoot

    $folderInputs = @($preparation.folderInputs)
    if ($folderInputs.Count -eq 0) { throw 'conflict.contract_empty' }

    [void][IO.Directory]::CreateDirectory($OutputDirectory)

    Reset-UiAcceptanceMutationSteps
    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId

    $waited = Invoke-WinAppObservation -Arguments @('ui','wait-for','TraceFoldMainWindow') `
        -ProcessId $productProcessId -TimeoutSeconds $WaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($waited.Status -cne 'Passed' -or $null -eq $waited.Data -or $waited.Data.found -ne $true) {
        throw 'conflict.window_unavailable'
    }

    # --- owned preparation: three directories through the product's own Settings UI ---
    $folders = @{}
    foreach ($folderInput in $folderInputs) {
        Assert-UiAcceptanceFields -InputObject $folderInput -Required @('key','automationId','relativeDirectory')
        $relative = [string] $folderInput.relativeDirectory
        if ($relative -cnotmatch '^[a-z0-9][a-z0-9-]*$') { throw 'conflict.fixture_target' }
        $directory = [IO.Path]::GetFullPath((Join-Path $OwnedRoot $relative))
        $rootBoundary = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $directory.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'conflict.fixture_target' }
        [void][IO.Directory]::CreateDirectory($directory)
        $folders[[string] $folderInput.key] = $directory
    }

    foreach ($folderInput in $folderInputs) {
        $path = $folders[[string] $folderInput.key]
        $null = Invoke-ConflictAction -Step ('set-' + [string] $folderInput.key + '-folder') `
            -Arguments @('ui','set-value',[string] $folderInput.automationId,$path) `
            -Code 'conflict.folder_settings'
        $readBack = Invoke-WinAppObservation -Arguments @('ui','get-value',[string] $folderInput.automationId) `
            -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
            -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($readBack.Status -cne 'Passed' -or $null -eq $readBack.Data -or
            $null -eq $readBack.Data.PSObject.Properties['text']) { throw 'conflict.folder_settings' }
        if ([string] $readBack.Data.text -cne $path) { throw 'conflict.folder_settings' }
    }
    $null = Invoke-ConflictAction -Step 'save-folder-settings' `
        -Arguments @('ui','invoke',[string] $preparation.saveFolderSettingsAutomationId) `
        -Code 'conflict.folder_settings'
    $status = Invoke-WinAppObservation -Arguments @('ui','get-value',[string] $preparation.savedStatusAutomationId) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($status.Status -cne 'Passed' -or $null -eq $status.Data -or
        [string] $status.Data.text -cne $savedStatusText) { throw 'conflict.folder_settings' }

    # --- the original asset, ingested formally and verified by hash ---
    [IO.File]::Copy($verifiedPhoto.Source, (Join-Path $folders['inbox'] $photoFilename), $false)
    $null = Invoke-ConflictAction -Step 'open-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) -Code 'conflict.inbox_navigation'
    $null = Invoke-ConflictAction -Step 'scan-first' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) -Code 'conflict.scan_trigger'
    if (-not (Test-ConflictWait -Arguments @('ui','wait-for',$photoRowPrefix) -Seconds $RowWaitTimeoutSeconds)) {
        throw 'conflict.row_missing'
    }

    # The row is addressed by the semantic selector of the inspection that observed it, resolved
    # immediately before the invocation: its accessible name carries a status that changes.
    $firstRowResolver = {
        $raw = Read-ConflictNodes
        $found = Find-UiAcceptanceUniqueNode -Nodes $raw -NativeType ([string] $inboxRow.nativeControlType) `
            -NamePrefix $photoRowPrefix -NameContains $photoFilename -FailureCode 'conflict.row_ambiguous'
        if ($null -eq $found) { throw 'conflict.row_missing' }
        return (Get-UiAcceptanceNodeSelector -Node $found -FailureCode 'conflict.row_missing')
    }
    $null = Invoke-ConflictAction -Step 'select-first' -Arguments @('ui','invoke',(& $firstRowResolver)) -Code 'conflict.row_selection'

    # A newly scanned item reports that its file is still settling, and the ingest command stays
    # disabled until the item is ingestable.
    if (-not (Test-ConflictWait -Seconds $ConflictWaitSeconds -Arguments @(
                'ui','wait-for','IngestButton','--property','IsEnabled','--value','True'))) {
        throw 'conflict.ingest_not_ready'
    }
    $null = Invoke-ConflictAction -Step 'ingest-first' -Arguments @('ui','invoke','IngestButton') -Code 'conflict.ingest'
    if (-not (Wait-ConflictGone -Arguments @('ui','wait-for',$photoFilename,'--gone') -Seconds $RowWaitTimeoutSeconds)) {
        throw 'conflict.ingest_incomplete'
    }

    $formalBefore = Read-ConflictFormalFiles -LibraryDirectory $folders['library']
    if ($formalBefore.Count -ne 1 -or $formalBefore[0].Name -cne $photoFilename) { throw 'conflict.original_missing' }
    if ($formalBefore[0].Hash -cne $verifiedPhoto.Sha256) { throw 'conflict.hash_mismatch' }

    $beforeSnapshot = Read-ConflictSnapshot -EvidenceId 'conflict-before'

    # --- conflict phase ---
    # The same verified bytes arrive again, so the product must raise a conflict against the asset
    # that is already registered.
    [IO.File]::Copy($verifiedPhoto.Source, (Join-Path $folders['inbox'] $photoFilename), $false)
    $null = Invoke-ConflictAction -Step 'scan-second' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) -Code 'conflict.scan_trigger'
    if (-not (Test-ConflictWait -Arguments @('ui','wait-for',$photoRowPrefix) -Seconds $RowWaitTimeoutSeconds)) {
        throw 'conflict.row_missing'
    }

    $secondRowResolver = {
        $raw = Read-ConflictNodes
        $found = Find-UiAcceptanceUniqueNode -Nodes $raw -NativeType ([string] $inboxRow.nativeControlType) `
            -NamePrefix $photoRowPrefix -NameContains $photoFilename -FailureCode 'conflict.row_ambiguous'
        if ($null -eq $found) { throw 'conflict.row_missing' }
        return (Get-UiAcceptanceNodeSelector -Node $found -FailureCode 'conflict.row_missing')
    }
    $null = Invoke-ConflictAction -Step 'select-second' -Arguments @('ui','invoke',(& $secondRowResolver)) -Code 'conflict.row_selection'

    # The product raises the comparison when an ingest reports a conflict against an already
    # registered asset, so the ingest command is what opens the dialog. It must be observed enabled
    # before it is invoked, and it is dispatched exactly once.
    if (-not (Test-ConflictWait -Seconds $ConflictWaitSeconds -Arguments @(
                'ui','wait-for','IngestButton','--property','IsEnabled','--value','True'))) {
        throw 'conflict.ingest_not_ready'
    }
    $null = Invoke-ConflictAction -Step 'ingest-second' -Arguments @('ui','invoke','IngestButton') -Code 'conflict.ingest'
    if (-not (Test-ConflictWait -Arguments @('ui','wait-for','ConflictComparisonDialog') -Seconds $ConflictWaitSeconds)) {
        throw 'conflict.dialog_missing'
    }
    # The re-open entry point exists for an already recorded conflict; it must be present and
    # addressable even though this flow is driven by the ingest itself.
    if ($null -eq (Find-UiAcceptanceUniqueNode -Nodes (Read-ConflictNodes) `
                -AutomationId 'ResolveInboxConflictButton' -FailureCode 'conflict.resolve_missing')) {
        throw 'conflict.resolve_missing'
    }

    # Capture the comparison and the incoming preview before any decision is taken, and record the
    # conflict state the decision is about to be based on.
    $dialogNodes = Read-ConflictNodes
    $incoming = Find-UiAcceptanceUniqueNode -Nodes $dialogNodes -AutomationId 'ConflictIncomingPreview' -FailureCode 'conflict.preview_ambiguous'
    if ($null -eq $incoming) { throw 'conflict.preview_missing' }
    $null = Save-ConflictScreenshot -Selector 'ConflictComparisonDialog' -Name 'conflict-dialog'
    $null = Save-ConflictScreenshot -Selector 'ConflictIncomingPreview' -Name 'conflict-incoming'
    $treeRelative = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory -EvidenceId 'conflict-dialog' `
        -Nodes (ConvertTo-ConflictEvidenceNodes -Nodes $dialogNodes) -RunToken $RunToken -OwnedRoot $OwnedRoot
    $artifacts.Add($treeRelative)

    # A ContentDialog is reported as its window and its content, so the dialog id legitimately
    # appears more than once. The decisive control inside it must be unique.
    $observedDialog = @($dialogNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'ConflictComparisonDialog' }).Count
    $observedDecision = @($dialogNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'ConflictSkipButton' }).Count
    if ($observedDialog -lt 1) { throw 'conflict.dialog_missing' }
    if ($observedDecision -ne 1) { throw 'conflict.decision_ambiguous' }

    # The conflict must be live in the product's own UI before any decision is taken. Two
    # independent signals are required: the conflict entry point is offered, and the inspector
    # reports the conflict in its own words. Neither of those lines carries an identifier.
    if (-not (Test-ConflictWait -Seconds $ConflictWaitSeconds -Arguments @(
                'ui','wait-for','ResolveInboxConflictButton'))) {
        throw 'conflict.precondition_state'
    }
    $liveConflict = Read-ConflictNodes
    $reported = @($liveConflict | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').Contains($conflictMessageToken) })
    if ($reported.Count -lt 1) { throw 'conflict.precondition_state' }

    # Between the observation and the decision the conflict must still be the same one. A changed
    # dialog means the decision would apply to a different case, so the run fails rather than
    # re-issuing it.
    $decisionNodes = Read-ConflictNodes
    $stillOpen = @($decisionNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'ConflictComparisonDialog' }).Count
    $stillDecidable = @($decisionNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'ConflictSkipButton' }).Count
    $stillHasPreview = $null -ne (Find-UiAcceptanceUniqueNode -Nodes $decisionNodes `
        -AutomationId 'ConflictIncomingPreview' -FailureCode 'conflict.decision_state_changed')
    if ($stillOpen -lt 1 -or $stillDecidable -ne 1 -or -not $stillHasPreview) {
        throw 'conflict.decision_state_changed'
    }

    # The single decision of this run. It is never re-sent, on failure or on timeout.
    $null = Invoke-ConflictAction -Step 'skip' -Arguments @('ui','invoke','ConflictSkipButton') -Code 'conflict.decision'
    if (-not (Wait-ConflictGone -Arguments @('ui','wait-for','ConflictComparisonDialog','--gone') -Seconds $ConflictWaitSeconds)) {
        throw 'conflict.dialog_stuck'
    }

    # The dialog closing only proves the overlay vanished. A skip keeps the pending item in place,
    # so the item leaving the UI is the wrong signal and is not awaited. Completion is the product
    # withdrawing its own conflict state: the conflict entry point is gone, the inspector no longer
    # reports the conflict, and the row no longer carries the conflict state token.
    if (-not (Wait-ConflictGone -Arguments @(
                'ui','wait-for','ResolveInboxConflictButton','--gone') -Seconds $ConflictWaitSeconds)) {
        throw 'conflict.resolution_incomplete'
    }
    $settledNodes = Read-ConflictNodes
    $stillReported = @($settledNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').Contains($conflictMessageToken) }).Count
    $stillInState = @($settledNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').Contains($conflictStateToken) }).Count
    if ($stillReported -ne 0 -or $stillInState -ne 0) { throw 'conflict.resolution_incomplete' }

    # --- read-only facts: the original is untouched, nothing was added, the source stays ---
    $afterSnapshot = Read-ConflictSnapshot -EvidenceId 'conflict-after'
    $formalAfter = Read-ConflictFormalFiles -LibraryDirectory $folders['library']

    $original = @($formalAfter | Where-Object { $_.Name -ceq $photoFilename })
    if ($original.Count -ne 1) { throw 'conflict.original_missing' }
    if ($original[0].Hash -cne $verifiedPhoto.Sha256) { throw 'conflict.original_changed' }
    if ($formalAfter.Count -ne 1) { throw 'conflict.copy_created' }

    # The conflict source file stays safely pending in the owned inbox, byte-identical.
    $inboxSource = @([IO.Directory]::EnumerateFiles($folders['inbox'], $photoFilename, [IO.SearchOption]::AllDirectories))
    if ($inboxSource.Count -ne 1) { throw 'conflict.source_missing' }
    if ((Get-FileHash -LiteralPath $inboxSource[0] -Algorithm SHA256).Hash -cne $verifiedPhoto.Sha256) {
        throw 'conflict.source_changed'
    }

    $assets = @($afterSnapshot.formalAssets)
    if ($assets.Count -ne 1) { throw 'conflict.snapshot_assets' }
    if ([string] $assets[0].actualFilename -cne $photoFilename) { throw 'conflict.snapshot_assets' }

    $beforeTables = ConvertTo-Json -InputObject $beforeSnapshot.tables -Depth 12 -Compress
    $afterTables = ConvertTo-Json -InputObject $afterSnapshot.tables -Depth 12 -Compress
    if ($beforeTables -ceq $afterTables) { throw 'conflict.snapshot_unchanged' }

    return [pscustomobject] @{
        ScenarioId     = $ScenarioId
        Status         = 'Passed'
        Reason         = ''
        Decision       = 'skip'
        OriginalFile   = $photoFilename
        SourceRetained = $true
        FormalFiles    = @($formalAfter | ForEach-Object { $_.Name })
        Artifacts      = @($artifacts)
    }
}
catch {
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or $code -notlike 'conflict.*') {
        $cause = ConvertTo-UiAcceptanceSlug -Value ([string] $code)
        if ($cause.Length -gt 120) { $cause = $cause.Substring(0, 120) }
        $code = 'conflict.unexpected'
        if (-not [string]::IsNullOrWhiteSpace($cause)) { $code = $code + '.' + $cause }
    }
    $status = if ($blockedFailureCodes -ccontains $code) { 'Blocked' } else { 'Failed' }
    return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = $status; Reason = $code; Artifacts = @($artifacts) }
}
