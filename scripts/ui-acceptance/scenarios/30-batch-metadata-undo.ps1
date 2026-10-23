# TraceFold UI acceptance - batch metadata edit with undo (Task 12).
#
# Ruling B, recorded here for the next reader: this scenario runs on the LIBRARY batch editor
# (LibraryBatchEditDialog, metadata tab) with the library undo banner, because the inbox batch
# metadata operation exposes NO undo at all - it offers only Prepare and Apply, its result is
# aggregate-only, and no inbox undo control exists. The library editor is the surface that has a
# real undo contract (LibraryBatchUndoReceipt / UndoLastAsync / LibraryBatchUndoButton), so the
# undo-bearing assertion lives here. The library filter and naming rules belong to the later
# library scenario and are not touched here.
#
# One owned root, created for this run only. The scenario ingests two fixed assets through the
# product's own UI, then edits their metadata in two passes - an append and a replace - and undoes
# the second pass once.
#
# What is proven:
#   * an invalid draft is refused: the unchanged draft leaves apply unavailable, and a read-only
#     snapshot shows every asset untouched. The product's write is transactional, so no partial
#     write is claimed or possible.
#   * the append adds several authors and tags, and the replace sets two authors, a single series
#     and a tag. On success the editor writes its changed/unchanged line and then hides itself
#     (LibraryBatchEditDialog.xaml.cs hides the dialog when LastResult.Succeeded). Completion is
#     the dialog closing plus the visible undo banner and its affected count, which exist only
#     when the apply succeeded and recorded a receipt.
#   * one undo restores each asset's exact prior values, with the formal file hashes unchanged.
#
# Read-only guarantees: every conclusion is paired with a read-only snapshot or a file hash, and
# every mutation carries its own step id. A timeout or an ambiguous outcome fails the run and is
# never re-sent.
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
    [int] $BatchWaitSeconds = 60,
    [int] $ObservationTimeoutSeconds = 20,
    [int] $InspectDepth = 20,
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

function New-LibraryText {
    param([Parameter(Mandatory)][int[]] $CodePoints)

    $builder = [System.Text.StringBuilder]::new()
    foreach ($codePoint in $CodePoints) { [void] $builder.Append([char] $codePoint) }
    return $builder.ToString()
}

# Taxonomy names, composed from code points so this file stays pure ASCII.
$authorOne = New-LibraryText -CodePoints @(0x4F5C, 0x8005, 0x7532)
$authorTwo = New-LibraryText -CodePoints @(0x4F5C, 0x8005, 0x4E59)
$authorThree = New-LibraryText -CodePoints @(0x4F5C, 0x8005, 0x4E19)
$tagOne = New-LibraryText -CodePoints @(0x98CE, 0x666F)
$tagTwo = New-LibraryText -CodePoints @(0x591C, 0x666F)
$seriesOne = New-LibraryText -CodePoints @(0x7CFB, 0x5217, 0x7532)
# The mode list entries of the library metadata editor.
$replaceModeText = New-LibraryText -CodePoints @(0x66FF, 0x6362, 0x6216, 0x6E05, 0x7A7A)
$productProcessId = 0
$launched = $null
$artifacts = [System.Collections.Generic.List[string]]::new()

function Assert-LibraryResult {
    param($Result, [string] $Code)
    if ($null -eq $Result -or $Result.Status -cne 'Passed') { throw $Code }
}

function Invoke-LibraryAction {
    param([string] $Step, [string[]] $Arguments, [string] $Code)
    $result = Invoke-WinAppMutation -StepId ('library.' + $Step) -Arguments $Arguments `
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

function Read-LibraryNodes {
    return @(Read-UiAcceptanceTree -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -FailureCode 'library.inspect_unavailable' `
        -Depth $InspectDepth -TimeoutSeconds $ObservationTimeoutSeconds)
}

function Test-LibraryWait {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'library.observation_unavailable' }
    if ($null -eq $observation.Data -or $null -eq $observation.Data.PSObject.Properties['found']) {
        throw 'library.driver_schema_incompatible'
    }
    return [bool] $observation.Data.found
}

function Wait-LibraryGone {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $Seconds
    )

    $observation = Invoke-WinAppObservation -Arguments $Arguments `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'library.observation_unavailable' }
    return $true
}

function Read-LibraryValue {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][string] $Code)

    $observation = Invoke-WinAppObservation -Arguments @('ui','get-value',$AutomationId) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed' -or $null -eq $observation.Data) { throw $Code }
    if ($null -eq $observation.Data.PSObject.Properties['text']) { throw 'library.driver_schema_incompatible' }
    return [string] $observation.Data.text
}

function Test-LibraryEnabled {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][int] $Seconds)

    return (Test-LibraryWait -Seconds $Seconds -Arguments @(
            'ui','wait-for',$AutomationId,'--property','IsEnabled','--value','True'))
}

function Assert-LibraryUndoCount {
    param([Parameter(Mandatory)][string] $Expected, [Parameter(Mandatory)][string] $EvidenceId)

    $nodes = Read-LibraryNodes
    $banner = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId 'LibraryBatchUndoBanner' `
        -FailureCode 'library.apply_feedback'
    if ($null -eq $banner) { throw 'library.apply_feedback' }
    $pending = [System.Collections.Generic.Stack[object]]::new()
    foreach ($child in (Get-UiAcceptanceNodeChildren -Node $banner)) { $pending.Push($child) }
    $matches = 0
    while ($pending.Count -gt 0) {
        $node = $pending.Pop()
        if ((Get-UiAcceptanceNodeProperty -Node $node -Name 'type') -ceq 'Text' -and
            (Get-UiAcceptanceNodeProperty -Node $node -Name 'name') -ceq $Expected) {
            $matches++
        }
        foreach ($child in (Get-UiAcceptanceNodeChildren -Node $node)) { $pending.Push($child) }
    }
    if ($matches -ne 1) { throw 'library.apply_feedback.count' }
    $relative = Save-LibraryEvidence -EvidenceId $EvidenceId -Nodes $nodes
    $artifacts.Add($relative)
}

function Read-LibrarySnapshot {
    param([Parameter(Mandatory)][string] $EvidenceId)

    # The support CLI publishes its snapshot as UTF-8, so it is read through the shared launcher
    # that pins the payload encoding on both runtimes: a raw capture decodes with the console code
    # page, and on a machine whose page is a double-byte one that mangles the values and can even
    # consume a delimiter. The guard status is the one this invocation returned.
    $invocation = Invoke-UiAcceptanceSupportCommand -SupportPath $SupportPath `
        -Arguments @('snapshot', '--data-root', $OwnedRoot, '--token', $RunToken)
    if ($invocation.ExitCode -ne 0) { throw 'library.snapshot_unavailable' }
    $data = ConvertFrom-UiAcceptanceJson -Text $invocation.Stdout -Source 'snapshot'
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
            $fields['authors'] = @($asset.authors | ForEach-Object {
                Protect-UiAcceptanceText -Value ([string] $_) -RunToken $RunToken -OwnedRoot $OwnedRoot })
            $fields['series'] = Protect-UiAcceptanceText -Value ([string] $asset.series) -RunToken $RunToken -OwnedRoot $OwnedRoot
            $fields['tags'] = @($asset.tags | ForEach-Object {
                Protect-UiAcceptanceText -Value ([string] $_) -RunToken $RunToken -OwnedRoot $OwnedRoot })
            $fields
        })
    }) -Depth 12
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $relative), $text, [Text.UTF8Encoding]::new($false))
    $artifacts.Add($relative)
    return $data
}

<#
.SYNOPSIS
    Reads one asset's metadata as a comparable fact set.
#>
function Get-AssetFacts {
    param([Parameter(Mandatory)] $Snapshot, [Parameter(Mandatory)][string] $Filename,
        [Parameter(Mandatory)][string] $Code)

    $assets = @($Snapshot.formalAssets | Where-Object { [string] $_.actualFilename -ceq $Filename })
    if ($assets.Count -ne 1) { throw $Code }
    $authors = @($assets[0].authors | ForEach-Object { [string] $_ } | Sort-Object)
    $tags = @($assets[0].tags | ForEach-Object { [string] $_ } | Sort-Object)
    $series = ''
    if ($null -ne $assets[0].PSObject.Properties['series'] -and $null -ne $assets[0].series) {
        $series = [string] $assets[0].series
    }
    return [pscustomobject] @{
        Filename = $Filename
        AssetKey = [string] $assets[0].assetKey
        Authors  = $authors
        Series   = $series
        Tags     = $tags
        DisplayName = [string] $assets[0].displayName
    }
}

<#
.SYNOPSIS
    Compares two fact sets and returns the differing field names.
#>
function Compare-AssetFacts {
    param([Parameter(Mandatory)] $Left, [Parameter(Mandatory)] $Right)

    $differences = [System.Collections.Generic.List[string]]::new()
    if ((@($Left.Authors) -join '|') -cne (@($Right.Authors) -join '|')) { $differences.Add('authors') }
    if ([string] $Left.Series -cne [string] $Right.Series) { $differences.Add('series') }
    if ((@($Left.Tags) -join '|') -cne (@($Right.Tags) -join '|')) { $differences.Add('tags') }
    # The call sites wrap this in @(), which handles an empty, single or many result correctly;
    # pinning the array here as well would wrap it twice and make every comparison report one.
    return $differences.ToArray()
}

function Read-LibraryFormalFiles {
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

function ConvertTo-LibraryEvidenceNodes {
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

function Save-LibraryEvidence {
    param([Parameter(Mandatory)][string] $EvidenceId, [Parameter(Mandatory)][object[]] $Nodes)
    return (Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory -EvidenceId $EvidenceId `
        -Nodes (ConvertTo-LibraryEvidenceNodes -Nodes $Nodes) -RunToken $RunToken -OwnedRoot $OwnedRoot)
}

function Save-LibraryScreenshot {
    param([Parameter(Mandatory)][string] $Selector, [Parameter(Mandatory)][string] $Name)

    $source = Join-Path $OwnedRoot ('screenshots/' + $Name + '.png')
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($source))
    $captured = Invoke-WinAppObservation -Arguments @('ui','screenshot',$Selector,'--output',$source) `
        -ProcessId $productProcessId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($captured.Status -cne 'Passed' -or -not [IO.File]::Exists($source)) {
        throw 'library.screenshot_unavailable'
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
    Commits one taxonomy token into one of the library editor's composite pickers.
#>
function Set-LibraryTerm {
    param(
        [Parameter(Mandatory)][string] $OuterId,
        [Parameter(Mandatory)][string] $Term,
        [Parameter(Mandatory)][string] $Step
    )

    $failure = 'library.draft_input.' + $Step
    $nodes = Read-LibraryNodes
    $outer = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId $OuterId -FailureCode $failure
    if ($null -eq $outer) { throw $failure }
    $inner = Get-UiAcceptanceDescendantSelector -Node $outer -NativeType 'Edit' -ClassName 'TextBox' `
        -FailureCode $failure
    $result = Set-UiAcceptanceTaxonomyTerm -StepId ('library.' + $Step) `
        -InnerSelector $inner -Term $Term -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -ForegroundCheck $ForegroundCheck `
        -DesktopCheck $DesktopCheck -TimeoutMs $MutationTimeoutMs -FailureCode $failure
    Assert-LibraryResult -Result $result -Code ($failure + '.' + [string] $result.Status)
}

<#
.SYNOPSIS
    Selects every library row and requires the product to report the expected count.
#>
function Select-AllLibraryRows {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][string] $Expected)

    $selection = Invoke-WinAppKeyboard -StepId ('library.' + $Step) `
        -Arguments @('ui','send-keys','ctrl+a','--target','LibraryGrid') -ProcessId $productProcessId `
        -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck -DesktopCheck $DesktopCheck `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -TimeoutMs $MutationTimeoutMs
    Assert-LibraryResult -Result $selection -Code 'library.selection'
    if (-not (Test-LibraryEnabled -AutomationId 'LibraryBatchEditButton' -Seconds $BatchWaitSeconds)) {
        throw 'library.selection'
    }
    $count = Read-LibraryValue -AutomationId 'LibrarySelectedCount' -Code 'library.selection'
    if ($count -cne $Expected) { throw 'library.selection' }
}

$failureCode = 'library.unexpected'
$blockedFailureCodes = @(
    'library.desktop_unavailable',
    'library.fixture_missing',
    'library.fixture_mismatch',
    'library.fixture_target',
    'library.snapshot_unavailable'
)

try {
    foreach ($requiredInput in @($ExecutablePath, $ContractPath, $OwnedRoot, $OutputDirectory, $SupportPath, $WinAppPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredInput)) { throw 'library.invalid_input' }
    }
    if ([IO.Path]::GetFileName($ExecutablePath) -ine 'TraceFold.Media.exe') { throw 'library.invalid_product' }
    if (-not [IO.File]::Exists($ContractPath)) { throw 'library.contract_missing' }
    if (-not [IO.File]::Exists($SupportPath) -or -not [IO.File]::Exists($WinAppPath)) { throw 'library.tooling_missing' }

    $contract = ConvertFrom-UiAcceptanceJson -Text ([IO.File]::ReadAllText($ContractPath)) -Source 'uia contract'
    Assert-UiAcceptanceFields -InputObject $contract -Required @('entries','dynamicRows','fixtureAssets','sessionPreparation')

    $entryIndex = @{}
    foreach ($entry in @($contract.entries)) {
        Assert-UiAcceptanceFields -InputObject $entry -Required @('automationId','name','requiredPatterns')
        $entryIndex[[string] $entry.automationId] = $entry
    }
    foreach ($requiredId in @('LibraryGrid','LibrarySelectedCount','LibraryBatchEditButton',
            'LibraryBatchEditDialog','LibraryBatchMetadataTab','LibraryBatchMetadataModeList',
            'LibraryBatchAddAuthorInput','LibraryBatchAddTagInput','LibraryBatchReplaceAuthorInput',
            'LibraryBatchReplaceSeriesToggle','LibraryBatchReplaceSeriesInput',
            'LibraryBatchReplaceTagsToggle','LibraryBatchReplaceTagInput',
            'LibraryBatchApplyButton','LibraryBatchDialogStatus','IngestButton')) {
        if (-not $entryIndex.ContainsKey($requiredId)) { throw ('library.contract_missing_entry.' + $requiredId) }
    }

    $preparation = $contract.sessionPreparation
    Assert-UiAcceptanceFields -InputObject $preparation -Required @(
        'seedFilenames','savedStatusText','savedStatusAutomationId','settingsNavigationAutomationId',
        'inboxNavigationAutomationId','saveFolderSettingsAutomationId','scanTriggerAutomationId','folderInputs')

    $assets = @($contract.fixtureAssets)
    $verified = @{}
    foreach ($name in @('photo.jpg','photo-replacement.jpg')) {
        $matches = @($assets | Where-Object { [string] $_.fixedFilename -ceq $name })
        if ($matches.Count -ne 1) { throw 'library.fixture_missing' }
        $verified[$name] = Test-UiAcceptanceFixtureAsset -Asset $matches[0] -RepositoryRoot $repositoryRoot
    }

    # The pending rows are addressed by the prefix the manifest declares for the inbox scope, so no
    # localized literal is hard-coded and the library row naming is not confused with it.
    $inboxRows = @($contract.dynamicRows | Where-Object { [string] $_.phase -ceq 'inbox-seeded' })
    if ($inboxRows.Count -ne 1) { throw 'library.contract_empty' }
    $inboxRow = $inboxRows[0]
    Assert-UiAcceptanceFields -InputObject $inboxRow -Required @('fixedAccessibleName','fixedFilename','nativeControlType')
    $firstRowPrefix = ([string] $inboxRow.fixedAccessibleName).Replace(
        [string] $inboxRow.fixedFilename, 'photo.jpg')
    $secondRowPrefix = ([string] $inboxRow.fixedAccessibleName).Replace(
        [string] $inboxRow.fixedFilename, 'photo-replacement.jpg')

    $folderInputs = @($preparation.folderInputs)
    if ($folderInputs.Count -eq 0) { throw 'library.contract_empty' }

    [void][IO.Directory]::CreateDirectory($OutputDirectory)

    Reset-UiAcceptanceMutationSteps
    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId

    $waited = Invoke-WinAppObservation -Arguments @('ui','wait-for','TraceFoldMainWindow') `
        -ProcessId $productProcessId -TimeoutSeconds $WaitTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($waited.Status -cne 'Passed' -or $null -eq $waited.Data -or $waited.Data.found -ne $true) {
        throw 'library.window_unavailable'
    }

    # --- owned preparation: three directories through the product's own Settings UI ---
    $folders = @{}
    foreach ($folderInput in $folderInputs) {
        Assert-UiAcceptanceFields -InputObject $folderInput -Required @('key','automationId','relativeDirectory')
        $relative = [string] $folderInput.relativeDirectory
        if ($relative -cnotmatch '^[a-z0-9][a-z0-9-]*$') { throw 'library.fixture_target' }
        $directory = [IO.Path]::GetFullPath((Join-Path $OwnedRoot $relative))
        $rootBoundary = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $directory.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'library.fixture_target' }
        [void][IO.Directory]::CreateDirectory($directory)
        $folders[[string] $folderInput.key] = $directory
    }

    foreach ($folderInput in $folderInputs) {
        $path = $folders[[string] $folderInput.key]
        $null = Invoke-LibraryAction -Step ('set-' + [string] $folderInput.key + '-folder') `
            -Arguments @('ui','set-value',[string] $folderInput.automationId,$path) -Code 'library.folder_settings'
        $readBack = Read-LibraryValue -AutomationId ([string] $folderInput.automationId) -Code 'library.folder_settings'
        if ($readBack -cne $path) { throw 'library.folder_settings' }
    }
    $null = Invoke-LibraryAction -Step 'save-folder-settings' `
        -Arguments @('ui','invoke',[string] $preparation.saveFolderSettingsAutomationId) -Code 'library.folder_settings'
    $status = Read-LibraryValue -AutomationId ([string] $preparation.savedStatusAutomationId) -Code 'library.folder_settings'
    if ($status -cne [string] $preparation.savedStatusText) { throw 'library.folder_settings' }

    # --- two fixed assets are ingested through the product's own UI ---
    foreach ($name in @('photo.jpg','photo-replacement.jpg')) {
        [IO.File]::Copy($verified[$name].Source, (Join-Path $folders['inbox'] $name), $false)
    }
    $null = Invoke-LibraryAction -Step 'open-inbox' `
        -Arguments @('ui','invoke',[string] $preparation.inboxNavigationAutomationId) -Code 'library.inbox_navigation'
    $null = Invoke-LibraryAction -Step 'scan' `
        -Arguments @('ui','invoke',[string] $preparation.scanTriggerAutomationId) -Code 'library.scan_trigger'

    $firstRowResolver = {
        $nodes = Read-LibraryNodes
        $found = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType ([string] $inboxRow.nativeControlType) `
            -NamePrefix $firstRowPrefix -NameContains 'photo.jpg' -FailureCode 'library.row_missing'
        if ($null -eq $found) { throw 'library.row_missing' }
        return (Get-UiAcceptanceNodeSelector -Node $found -FailureCode 'library.row_missing')
    }
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for',$firstRowPrefix) -Seconds $RowWaitTimeoutSeconds)) {
        throw 'library.row_missing'
    }
    $null = Invoke-LibraryAction -Step 'select-first' -Arguments @('ui','invoke',(& $firstRowResolver)) -Code 'library.row_selection'
    if (-not (Test-LibraryEnabled -AutomationId 'IngestButton' -Seconds $BatchWaitSeconds)) { throw 'library.ingest_not_ready' }
    $null = Invoke-LibraryAction -Step 'ingest-first' -Arguments @('ui','invoke','IngestButton') -Code 'library.ingest'
    if (-not (Wait-LibraryGone -Arguments @('ui','wait-for',$firstRowPrefix,'--gone') -Seconds $RowWaitTimeoutSeconds)) {
        throw 'library.ingest_incomplete'
    }

    if (-not (Test-LibraryWait -Arguments @('ui','wait-for',$secondRowPrefix) -Seconds $RowWaitTimeoutSeconds)) {
        throw 'library.row_missing'
    }
    $secondRowResolver = {
        $nodes = Read-LibraryNodes
        $found = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType ([string] $inboxRow.nativeControlType) `
            -NamePrefix $secondRowPrefix -NameContains 'photo-replacement.jpg' -FailureCode 'library.row_missing'
        if ($null -eq $found) { throw 'library.row_missing' }
        return (Get-UiAcceptanceNodeSelector -Node $found -FailureCode 'library.row_missing')
    }
    $null = Invoke-LibraryAction -Step 'select-second' -Arguments @('ui','invoke',(& $secondRowResolver)) -Code 'library.row_selection'
    if (-not (Test-LibraryEnabled -AutomationId 'IngestButton' -Seconds $BatchWaitSeconds)) { throw 'library.ingest_not_ready' }
    $null = Invoke-LibraryAction -Step 'ingest-second' -Arguments @('ui','invoke','IngestButton') -Code 'library.ingest'
    if (-not (Wait-LibraryGone -Arguments @('ui','wait-for',$secondRowPrefix,'--gone') -Seconds $RowWaitTimeoutSeconds)) {
        throw 'library.ingest_incomplete'
    }

    $formalBefore = Read-LibraryFormalFiles -LibraryDirectory $folders['library']
    if ($formalBefore.Count -ne 2) { throw 'library.ingest_result' }
    $beforeSnapshot = Read-LibrarySnapshot -EvidenceId 'library-before'
    $beforeVideo = Get-AssetFacts -Snapshot $beforeSnapshot -Filename 'photo-replacement.jpg' -Code 'library.asset_missing'
    $beforePhoto = Get-AssetFacts -Snapshot $beforeSnapshot -Filename 'photo.jpg' -Code 'library.asset_missing'

    # --- the library editor, with two rows selected ---
    # The library page is reached through the navigation item the contract declares.
    $null = Invoke-LibraryAction -Step 'open-library-page' -Arguments @('ui','invoke','NavLibrary') -Code 'library.library_navigation'
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryGrid') -Seconds $WaitTimeoutSeconds)) {
        throw 'library.library_navigation'
    }
    Select-AllLibraryRows -Step 'select-all' -Expected '2'

    $null = Invoke-LibraryAction -Step 'batch-open' -Arguments @('ui','invoke','LibraryBatchEditButton') -Code 'library.batch_open'
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryBatchEditDialog') -Seconds $BatchWaitSeconds)) {
        throw 'library.batch_open'
    }
    $null = Invoke-LibraryAction -Step 'metadata-tab' -Arguments @('ui','invoke','LibraryBatchMetadataTab') -Code 'library.batch_open'

    # --- the invalid draft: nothing changed, so the product must refuse it ---
    $invalidNodes = Read-LibraryNodes
    $invalidApply = Find-UiAcceptanceUniqueNode -Nodes $invalidNodes -AutomationId 'LibraryBatchApplyButton' -FailureCode 'library.invalid_draft'
    if ($null -eq $invalidApply) { throw 'library.invalid_draft' }
    $invalidEnabled = (Get-UiAcceptanceNodeProperty -Node $invalidApply -Name 'isEnabled') -ceq 'True'
    # The editor's status line is observable while the draft is open, so its state is recorded here.
    $invalidStatus = Read-LibraryValue -AutomationId 'LibraryBatchDialogStatus' -Code 'library.invalid_draft'
    $invalidSnapshot = Read-LibrarySnapshot -EvidenceId 'library-invalid-draft'
    # The refusal is proven by the product, and the read-only facts must show nothing changed.
    if ($invalidEnabled) { throw 'batch.invalid-draft' }
    $invalidTree = Save-LibraryEvidence -EvidenceId 'library-invalid-draft' -Nodes $invalidNodes
    $artifacts.Add($invalidTree)
    if (@(Compare-AssetFacts -Left $beforeVideo -Right (Get-AssetFacts -Snapshot $invalidSnapshot -Filename 'photo-replacement.jpg' -Code 'library.asset_missing')).Count -ne 0 -or
        @(Compare-AssetFacts -Left $beforePhoto -Right (Get-AssetFacts -Snapshot $invalidSnapshot -Filename 'photo.jpg' -Code 'library.asset_missing')).Count -ne 0) {
        throw 'library.draft_changed'
    }

    # --- append several authors and tags ---
    Set-LibraryTerm -OuterId 'LibraryBatchAddAuthorInput' -Term $authorOne -Step 'add-author-1'
    Set-LibraryTerm -OuterId 'LibraryBatchAddAuthorInput' -Term $authorTwo -Step 'add-author-2'
    Set-LibraryTerm -OuterId 'LibraryBatchAddTagInput' -Term $tagOne -Step 'add-tag-1'
    Set-LibraryTerm -OuterId 'LibraryBatchAddTagInput' -Term $tagTwo -Step 'add-tag-2'
    if (-not (Test-LibraryEnabled -AutomationId 'LibraryBatchApplyButton' -Seconds $BatchWaitSeconds)) {
        throw 'library.apply_not_ready'
    }
    $null = Save-LibraryScreenshot -Selector 'LibraryBatchEditDialog' -Name 'library-batch-draft'
    $null = Invoke-LibraryAction -Step 'apply-append' -Arguments @('ui','invoke','LibraryBatchApplyButton') -Code 'library.batch_apply'
    if (-not (Wait-LibraryGone -Arguments @('ui','wait-for','LibraryBatchApplyButton','--gone') -Seconds $BatchWaitSeconds)) {
        throw 'library.batch_completion'
    }
    # The editor writes its success line and then hides itself, so the line cannot be read after a
    # successful apply. The product's own confirmation is the undo it now exposes, which exists
    # only when the apply succeeded and recorded a receipt.
    if (-not (Test-LibraryEnabled -AutomationId 'LibraryBatchUndoButton' -Seconds $BatchWaitSeconds)) {
        throw 'library.apply_feedback'
    }
    Assert-LibraryUndoCount -Expected '2' -EvidenceId 'library-append-feedback'

    $appendedSnapshot = Read-LibrarySnapshot -EvidenceId 'library-appended'
    $appendedVideo = Get-AssetFacts -Snapshot $appendedSnapshot -Filename 'photo-replacement.jpg' -Code 'library.asset_missing'
    $appendedPhoto = Get-AssetFacts -Snapshot $appendedSnapshot -Filename 'photo.jpg' -Code 'library.asset_missing'
    foreach ($facts in @($appendedVideo, $appendedPhoto)) {
        if ((@($facts.Authors) -join '|') -cne (@(@($authorOne, $authorTwo) | Sort-Object) -join '|')) {
            throw 'library.append_result'
        }
        if ((@($facts.Tags) -join '|') -cne (@(@($tagOne, $tagTwo) | Sort-Object) -join '|')) {
            throw 'library.append_result'
        }
    }

    # --- replace with two authors, one series and one tag ---
    Select-AllLibraryRows -Step 'reselect-all' -Expected '2'
    $null = Invoke-LibraryAction -Step 'batch-reopen' -Arguments @('ui','invoke','LibraryBatchEditButton') -Code 'library.batch_open'
    if (-not (Test-LibraryWait -Arguments @('ui','wait-for','LibraryBatchEditDialog') -Seconds $BatchWaitSeconds)) {
        throw 'library.batch_open'
    }
    $null = Invoke-LibraryAction -Step 'metadata-tab-again' -Arguments @('ui','invoke','LibraryBatchMetadataTab') -Code 'library.batch_open'
    # The editor's metadata mode is chosen from its mode list; the replace entry is addressed by
    # its own visible text, which is unique in the dialog.
    $null = Invoke-LibraryAction -Step 'replace-mode' -Arguments @('ui','invoke',$replaceModeText) -Code 'library.draft_mode'

    $null = Invoke-LibraryAction -Step 'replace-authors' -Arguments @('ui','invoke','LibraryBatchReplaceAuthorsToggle') -Code 'library.draft_toggle'
    Set-LibraryTerm -OuterId 'LibraryBatchReplaceAuthorInput' -Term $authorTwo -Step 'replace-author-1'
    Set-LibraryTerm -OuterId 'LibraryBatchReplaceAuthorInput' -Term $authorThree -Step 'replace-author-2'
    $null = Invoke-LibraryAction -Step 'replace-series' -Arguments @('ui','invoke','LibraryBatchReplaceSeriesToggle') -Code 'library.draft_toggle'
    Set-LibraryTerm -OuterId 'LibraryBatchReplaceSeriesInput' -Term $seriesOne -Step 'replace-series-1'
    $null = Invoke-LibraryAction -Step 'replace-tags' -Arguments @('ui','invoke','LibraryBatchReplaceTagsToggle') -Code 'library.draft_toggle'
    Set-LibraryTerm -OuterId 'LibraryBatchReplaceTagInput' -Term $tagTwo -Step 'replace-tag-1'

    if (-not (Test-LibraryEnabled -AutomationId 'LibraryBatchApplyButton' -Seconds $BatchWaitSeconds)) {
        throw 'library.apply_not_ready'
    }
    $null = Save-LibraryScreenshot -Selector 'LibraryBatchEditDialog' -Name 'library-batch-replace-draft'
    $null = Invoke-LibraryAction -Step 'apply-replace' -Arguments @('ui','invoke','LibraryBatchApplyButton') -Code 'library.batch_apply'
    if (-not (Wait-LibraryGone -Arguments @('ui','wait-for','LibraryBatchApplyButton','--gone') -Seconds $BatchWaitSeconds)) {
        throw 'library.batch_completion'
    }
    if (-not (Test-LibraryEnabled -AutomationId 'LibraryBatchUndoButton' -Seconds $BatchWaitSeconds)) {
        throw 'library.apply_feedback'
    }
    Assert-LibraryUndoCount -Expected '2' -EvidenceId 'library-replace-feedback'

    $replacedSnapshot = Read-LibrarySnapshot -EvidenceId 'library-replaced'
    $replacedVideo = Get-AssetFacts -Snapshot $replacedSnapshot -Filename 'photo-replacement.jpg' -Code 'library.asset_missing'
    $replacedPhoto = Get-AssetFacts -Snapshot $replacedSnapshot -Filename 'photo.jpg' -Code 'library.asset_missing'
    foreach ($facts in @($replacedVideo, $replacedPhoto)) {
        if ((@($facts.Authors) -join '|') -cne (@(@($authorTwo, $authorThree) | Sort-Object) -join '|')) {
            throw 'library.replace_result'
        }
        if ([string] $facts.Series -cne $seriesOne) { throw 'library.replace_result' }
        if ((@($facts.Tags) -join '|') -cne $tagTwo) { throw 'library.replace_result' }
    }
    # The records must be the same records and the files untouched.
    if ($replacedVideo.AssetKey -cne $beforeVideo.AssetKey -or $replacedPhoto.AssetKey -cne $beforePhoto.AssetKey) {
        throw 'library.record_identity'
    }
    foreach ($file in (Read-LibraryFormalFiles -LibraryDirectory $folders['library'])) {
        $expected = $verified[$file.Name]
        if ($null -eq $expected -or $file.Hash -cne $expected.Sha256) { throw 'library.file_changed' }
    }

    # --- one undo, then the exact prior values ---
    if (-not (Test-LibraryEnabled -AutomationId 'LibraryBatchUndoButton' -Seconds $BatchWaitSeconds)) {
        throw 'library.undo_unavailable'
    }
    $null = Invoke-LibraryAction -Step 'undo' -Arguments @('ui','invoke','LibraryBatchUndoButton') -Code 'library.undo'
    if (-not (Wait-LibraryGone -Arguments @('ui','wait-for','LibraryBatchUndoButton','--gone') -Seconds $BatchWaitSeconds)) {
        throw 'library.undo_incomplete'
    }
    $undoneSnapshot = Read-LibrarySnapshot -EvidenceId 'library-undone'
    foreach ($pair in @(@{ file = 'photo-replacement.jpg'; before = $appendedVideo },
                        @{ file = 'photo.jpg'; before = $appendedPhoto })) {
        $facts = Get-AssetFacts -Snapshot $undoneSnapshot -Filename $pair.file -Code 'library.asset_missing'
        if (@(Compare-AssetFacts -Left $pair.before -Right $facts).Count -ne 0) { throw 'library.prior_values' }
    }
    foreach ($file in (Read-LibraryFormalFiles -LibraryDirectory $folders['library'])) {
        $expected = $verified[$file.Name]
        if ($null -eq $expected -or $file.Hash -cne $expected.Sha256) { throw 'library.file_changed' }
    }

    return [pscustomobject] @{
        ScenarioId       = $ScenarioId
        Status           = 'Passed'
        Reason           = ''
        DraftStatus      = $invalidStatus
        AppendedAuthors  = @($appendedVideo.Authors)
        AppendedTags     = @($appendedVideo.Tags)
        ReplacedAuthors  = @($replacedVideo.Authors)
        ReplacedSeries   = [string] $replacedVideo.Series
        ReplacedTags     = @($replacedVideo.Tags)
        UndoneAuthors    = @((Get-AssetFacts -Snapshot $undoneSnapshot -Filename 'photo-replacement.jpg' -Code 'library.asset_missing').Authors)
        Artifacts        = @($artifacts)
    }
}
catch {
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or
        ($code -notlike 'library.*' -and $code -notlike 'batch.*')) {
        $cause = ConvertTo-UiAcceptanceSlug -Value ([string] $code)
        if ($cause.Length -gt 120) { $cause = $cause.Substring(0, 120) }
        $code = 'library.unexpected'
        if (-not [string]::IsNullOrWhiteSpace($cause)) { $code = $code + '.' + $cause }
    }
    $status = if ($blockedFailureCodes -ccontains $code) { 'Blocked' } else { 'Failed' }
    return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = $status; Reason = $code; Artifacts = @($artifacts) }
}
