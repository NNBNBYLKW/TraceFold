# Stage 5CC main workflow acceptance. All actions use the owned process and its UI.
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
$contract = ConvertFrom-UiAcceptanceJson -Text ([IO.File]::ReadAllText($ContractPath))
Assert-UiAcceptanceFields -InputObject $contract -Required @('entries','dynamicRows','fixtureAssets','sessionPreparation')
$prep = $contract.sessionPreparation
$artifacts = [System.Collections.Generic.List[string]]::new()
$productProcessId = 0
$launched = $null

function Assert-MainResult {
    param($Result, [string] $Code)
    if ($null -eq $Result -or $Result.Status -cne 'Passed') { throw $Code }
}

function Invoke-MainAction {
    param([string] $Step, [string[]] $Arguments, [string] $Code)
    $result = Invoke-WinAppMutation -StepId ('main.' + $Step) -Arguments $Arguments `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($result.Status -cne 'Passed') {
        $detail = 'driver_unavailable'
        if ($null -ne $result.Data -and $null -ne $result.Data.PSObject.Properties['error'] -and
            $null -ne $result.Data.error.PSObject.Properties['code']) {
            $detail = ConvertTo-UiAcceptanceSlug -Value ([string] $result.Data.error.code)
        }
        throw ($Code + '.' + (ConvertTo-UiAcceptanceSlug -Value $Step) + '.' + $detail +
            '.' + (ConvertTo-UiAcceptanceSlug -Value ([string] $result.Status)) +
            '.exit' + [string] $result.ExitCode)
    }
    return $result
}

function Read-MainNodes {
    return @(Read-UiAcceptanceTree -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -FailureCode 'main.inspect_unavailable')
}

function Find-MainId {
    param([object[]] $Nodes, [string] $Id, [string] $Code)
    $node = Find-UiAcceptanceUniqueNode -Nodes $Nodes -AutomationId $Id -FailureCode $Code
    if ($null -eq $node) { throw $Code }
    return $node
}

function Wait-MainElement {
    param([string] $Selector, [string] $Code, [int] $Seconds = 45)
    $result = Invoke-WinAppObservation -Arguments @('ui','wait-for',$Selector) `
        -ProcessId $productProcessId -TimeoutSeconds $Seconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $result -Code $Code
}

function Get-MainValue {
    param([string] $Selector, [string] $Code)
    $result = Invoke-WinAppObservation -Arguments @('ui','get-value',$Selector) `
        -ProcessId $productProcessId -TimeoutSeconds 12 `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $result -Code $Code
    if ($null -eq $result.Data -or $null -eq $result.Data.PSObject.Properties['text']) { throw $Code }
    return [string] $result.Data.text
}

function Save-MainSnapshot {
    param([string] $Step)
    $raw = & $SupportPath snapshot --data-root $OwnedRoot --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'main.snapshot_unavailable' }
    $data = ConvertFrom-UiAcceptanceJson -Text ($raw | Out-String)
    Assert-UiAcceptanceFields -InputObject $data -Required @('tables','taxonomy','formalAssets')
    $name = 'main-' + (ConvertTo-UiAcceptanceSlug -Value $Step) + '-snapshot.json'
    $target = Join-Path $OutputDirectory $name
    if ([IO.File]::Exists($target)) { throw 'main.evidence_duplicate' }
    # The support CLI emits only allowlisted, redacted logical facts and never paths or IDs.
    [IO.File]::WriteAllText($target, (ConvertTo-Json -InputObject $data -Depth 20),
        [Text.UTF8Encoding]::new($false))
    $artifacts.Add($name)
    return $data
}

function Save-MainScreen {
    param([string] $Step)
    $source = Join-Path $OwnedRoot ('main-' + (ConvertTo-UiAcceptanceSlug -Value $Step) + '.png')
    $result = Invoke-WinAppObservation -Arguments @('ui','screenshot','TraceFoldMainWindow','--output',$source) `
        -ProcessId $productProcessId -TimeoutSeconds 20 `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $result -Code 'main.screenshot_unavailable'
    Assert-UiOwnedEvidenceFile -Path $source -OwnedRoot $OwnedRoot
    $bytes = [IO.File]::ReadAllBytes($source)
    Assert-UiPngBytes -Bytes $bytes
    $name = 'main-' + (ConvertTo-UiAcceptanceSlug -Value $Step) + '.png'
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory $name), $bytes)
    $artifacts.Add($name)
}

function Select-MainRow {
    param([string] $Prefix, [string] $Filename, [string] $Step)
    $resolver = {
        $nodes = Read-MainNodes
        $row = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
            -NamePrefix $Prefix -NameContains $Filename -FailureCode 'main.dynamic_row'
        if ($null -eq $row) { throw 'main.dynamic_row' }
        return (Get-UiAcceptanceNodeSelector -Node $row -FailureCode 'main.dynamic_row')
    }
    $result = Invoke-UiAcceptanceResolvedMutation -StepId ('main.' + $Step) `
        -ProcessId $productProcessId -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot `
        -Verb 'invoke' -ResolveSelector $resolver -FailureCode 'main.row_selection'
    Assert-MainResult -Result $result -Code 'main.row_selection'
}

function Add-MainTerm {
    param([string] $InputId, [string] $TermsId, [string] $Term, [string] $Step)
    $nodes = Read-MainNodes
    $outer = Find-MainId -Nodes $nodes -Id $InputId -Code 'main.taxonomy_input'
    $inner = Get-UiAcceptanceDescendantSelector -Node $outer -NativeType 'Edit' `
        -ClassName 'TextBox' -FailureCode 'main.taxonomy_input'
    $result = Set-UiAcceptanceTaxonomyTerm -StepId ('main.' + $Step) `
        -InnerSelector $inner -Term $Term -ProcessId $productProcessId `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot `
        -ForegroundCheck $ForegroundCheck -DesktopCheck $DesktopCheck -FailureCode 'main.taxonomy_commit'
    Assert-MainResult -Result $result -Code 'main.taxonomy_commit'
    $after = Read-MainNodes
    $terms = Find-MainId -Nodes $after -Id $TermsId -Code 'main.taxonomy_commit'
    if (@(Get-UiAcceptanceNodeChildren -Node $terms | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq $Term }).Count -ne 1) {
        # WinUI exposes the chip's text as a descendant of its item container.
        $named = @($after | Where-Object { (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq $Term })
        if ($named.Count -eq 0) { throw 'main.taxonomy_commit' }
    }
}

function Get-MainDescendantText {
    param($Node)
    $names = [System.Collections.Generic.List[string]]::new()
    $pending = [System.Collections.Generic.Stack[object]]::new()
    $pending.Push($Node)
    while ($pending.Count -gt 0) {
        $candidate = $pending.Pop()
        if ((Get-UiAcceptanceNodeProperty -Node $candidate -Name 'type') -ceq 'Text') {
            $name = Get-UiAcceptanceNodeProperty -Node $candidate -Name 'name'
            if (-not [string]::IsNullOrWhiteSpace($name)) { $names.Add($name) }
        }
        foreach ($child in @(Get-UiAcceptanceNodeChildren -Node $candidate)) { $pending.Push($child) }
    }
    return $names.ToArray()
}

function Assert-MainVideoReadable {
    param([string] $VideoPath)
    $probePath = Join-Path ([IO.Path]::GetDirectoryName($ExecutablePath)) 'tools/ffmpeg/ffprobe.exe'
    if (-not [IO.File]::Exists($probePath)) { throw 'main.formal_video' }
    $start = [Diagnostics.ProcessStartInfo]::new($probePath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    # Double quotes cannot occur in a Windows filename. The checked path ends in .mp4,
    # so the closing quote cannot be escaped by a trailing backslash on PS 5.1.
    $start.Arguments = '-v quiet -count_frames -select_streams v:0 ' +
        '-show_entries stream=nb_read_frames -of default=noprint_wrappers=1:nokey=1 ' +
        '"' + $VideoPath + '"'
    $probe = [Diagnostics.Process]::Start($start)
    try {
        if (-not $probe.WaitForExit(20000)) { throw 'main.formal_video' }
        $frames = $probe.StandardOutput.ReadToEnd().Trim()
        $count = 0
        if ($probe.ExitCode -ne 0 -or
            -not [int]::TryParse($frames, [ref] $count) -or $count -lt 1) {
            throw 'main.formal_video'
        }
    }
    finally {
        if (-not $probe.HasExited) { $probe.Kill() }
        $probe.Dispose()
    }
}

function Wait-MainDiagnostics {
    param([string] $Code)
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    do {
        $checked = Get-MainValue -Selector 'DiagnosticsLastChecked' -Code $Code
        $overall = Get-MainValue -Selector 'DiagnosticsOverallStatus' -Code $Code
        if ($checked -cne '尚未检查' -and $overall -cne '尚未检查' -and
            -not [string]::IsNullOrWhiteSpace($checked) -and
            -not [string]::IsNullOrWhiteSpace($overall)) {
            return [ordered]@{ checked = $checked; overall = $overall }
        }
    } while ($deadline.Elapsed.TotalSeconds -lt 60)
    throw $Code
}

try {
    if (-not (& $DesktopCheck)) { throw 'main.desktop_unavailable' }
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    Reset-UiAcceptanceMutationSteps
    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId
    Wait-MainElement -Selector 'TraceFoldMainWindow' -Code 'main.window_unavailable'
    Wait-MainElement -Selector 'InboxFolderPath' -Code 'main.settings_unavailable'

    $folders = @{}
    foreach ($input in @($prep.folderInputs)) {
        $relative = [string] $input.relativeDirectory
        if ($relative -cnotmatch '^[a-z0-9][a-z0-9-]*$') { throw 'main.folder_target' }
        $path = [IO.Path]::GetFullPath((Join-Path $OwnedRoot $relative))
        [void][IO.Directory]::CreateDirectory($path)
        $folders[[string] $input.key] = $path
        $null = Invoke-MainAction -Step ('folder.' + [string] $input.key) `
            -Arguments @('ui','set-value',[string] $input.automationId,$path) -Code 'main.folder_settings'
        if ((Get-MainValue -Selector ([string] $input.automationId) -Code 'main.folder_settings') -cne $path) {
            throw 'main.folder_settings'
        }
    }
    $null = Invoke-MainAction -Step 'folder.save' `
        -Arguments @('ui','invoke',[string] $prep.saveFolderSettingsAutomationId) -Code 'main.folder_settings'
    if ((Get-MainValue -Selector ([string] $prep.savedStatusAutomationId) -Code 'main.folder_settings') -cne
        [string] $prep.savedStatusText) { throw 'main.folder_settings' }

    foreach ($asset in @($contract.fixtureAssets)) {
        $verified = Test-UiAcceptanceFixtureAsset -Asset $asset -RepositoryRoot $repositoryRoot
        $target = Join-Path $folders['inbox'] ([string] $asset.fixedFilename)
        if ([IO.File]::Exists($target)) { throw 'main.fixture_target' }
        [IO.File]::Copy($verified.Source, $target, $false)
    }
    $null = Invoke-MainAction -Step 'open-inbox' -Arguments @('ui','invoke','NavInbox') -Code 'main.inbox_navigation'
    Wait-MainElement -Selector 'InboxRefreshButton' -Code 'main.inbox_navigation'
    $null = Invoke-MainAction -Step 'scan' -Arguments @('ui','invoke','InboxRefreshButton') -Code 'main.scan'
    Wait-MainElement -Selector '待入库文件 paired.mp4' -Code 'main.video_row'
    Wait-MainElement -Selector '待入库文件 photo.jpg' -Code 'main.photo_row'
    $null = Save-MainSnapshot -Step 'scanned'
    Select-MainRow -Prefix '待入库文件 paired.mp4' -Filename 'paired.mp4' -Step 'select-video'
    $nodes = Read-MainNodes
    $null = Find-MainId -Nodes $nodes -Id 'DisplayNameBox' -Code 'main.inspector_missing'
    $null = Find-MainId -Nodes $nodes -Id 'IngestButton' -Code 'main.inspector_missing'
    $null = Invoke-MainAction -Step 'video.display-name' `
        -Arguments @('ui','set-value','DisplayNameBox','验收视频') -Code 'main.video_metadata'
    if ((Get-MainValue -Selector 'DisplayNameBox' -Code 'main.video_metadata') -cne '验收视频') {
        throw 'main.video_metadata'
    }
    Add-MainTerm -InputId 'InboxAuthorInput' -TermsId 'InboxAuthorInputTerms' -Term '作者甲' -Step 'video.author-one'
    Add-MainTerm -InputId 'InboxAuthorInput' -TermsId 'InboxAuthorInputTerms' -Term '作者乙' -Step 'video.author-two'
    Add-MainTerm -InputId 'InboxSeriesInput' -TermsId 'InboxSeriesInputTerms' -Term '系列甲' -Step 'video.series'
    Add-MainTerm -InputId 'InboxTagInput' -TermsId 'InboxTagInputTerms' -Term '风景' -Step 'video.tag-one'
    Add-MainTerm -InputId 'InboxTagInput' -TermsId 'InboxTagInputTerms' -Term '夜景' -Step 'video.tag-two'
    $null = Save-MainSnapshot -Step 'video-metadata'

    # The pair and the image are two distinct pending rows. Add one author through the real
    # batch editor; that author is already on the video and is new on the image.
    $selectAll = Invoke-WinAppKeyboard -StepId 'main.batch.select-all' `
        -Arguments @('ui','send-keys','ctrl+a','--target','InboxGrid') -ProcessId $productProcessId `
        -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck -DesktopCheck $DesktopCheck `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $selectAll -Code 'main.batch_selection'
    Wait-MainElement -Selector 'InboxBatchMetadataButton' -Code 'main.batch_selection'
    $nodes = Read-MainNodes
    $batchButton = Find-MainId -Nodes $nodes -Id 'InboxBatchMetadataButton' -Code 'main.batch_selection'
    if ((Get-UiAcceptanceNodeProperty -Node $batchButton -Name 'isEnabled') -cne 'True') {
        throw 'main.batch_selection'
    }
    $null = Invoke-MainAction -Step 'batch.open' -Arguments @('ui','invoke','InboxBatchMetadataButton') `
        -Code 'main.batch_dialog'
    Wait-MainElement -Selector 'BatchMetadataAddAuthorInput' -Code 'main.batch_dialog'
    $nodes = Read-MainNodes
    $input = Find-MainId -Nodes $nodes -Id 'BatchMetadataAddAuthorInput' -Code 'main.batch_dialog'
    $inner = Get-UiAcceptanceDescendantSelector -Node $input -NativeType 'Edit' `
        -ClassName 'TextBox' -FailureCode 'main.batch_dialog'
    $added = Set-UiAcceptanceTaxonomyTerm -StepId 'main.batch.add-author' -InnerSelector $inner `
        -Term '作者甲' -ProcessId $productProcessId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -ForegroundCheck $ForegroundCheck `
        -DesktopCheck $DesktopCheck -FailureCode 'main.batch_dialog'
    Assert-MainResult -Result $added -Code 'main.batch_dialog'
    $null = Invoke-MainAction -Step 'batch.submit' -Arguments @('ui','invoke','BatchMetadataSubmitButton') `
        -Code 'main.batch_submit'
    $dialogClosed = Invoke-WinAppObservation -Arguments @('ui','wait-for','BatchMetadataAddAuthorInput','--gone') `
        -ProcessId $productProcessId -TimeoutSeconds 45 `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $dialogClosed -Code 'main.batch_completion'
    $null = Save-MainSnapshot -Step 'batch-metadata'

    $null = Invoke-MainAction -Step 'batch.clear-selection' `
        -Arguments @('ui','invoke','InboxCancelSelectionButton') -Code 'main.batch_selection'
    $null = Invoke-MainAction -Step 'processing.open-settings' `
        -Arguments @('ui','invoke','NavSettings') -Code 'main.processing_action'
    Wait-MainElement -Selector 'ProcessingToolStatus' -Code 'main.processing_action'
    $toolStatus = Get-MainValue -Selector 'ProcessingToolStatus' -Code 'main.processing_action'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-tool-status.json'),
        (ConvertTo-Json -InputObject ([ordered]@{
            status = Protect-UiAcceptanceText -Value $toolStatus -RunToken $RunToken -OwnedRoot $OwnedRoot
        })), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-tool-status.json')
    $null = Invoke-MainAction -Step 'processing.return-inbox' `
        -Arguments @('ui','invoke','NavInbox') -Code 'main.processing_action'
    Wait-MainElement -Selector 'InboxGrid' -Code 'main.processing_action'
    Select-MainRow -Prefix '待入库文件 验收视频' -Filename '验收视频' -Step 'reselect-video'
    $afterReturn = Read-MainNodes
    $refreshButton = Find-MainId -Nodes $afterReturn -Id 'InboxRefreshButton' -Code 'main.processing_action'
    $startButton = Find-MainId -Nodes $afterReturn -Id 'InboxStartProcessingButton' -Code 'main.processing_action'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-after-return.json'),
        (ConvertTo-Json -InputObject ([ordered]@{
            refreshEnabled = Get-UiAcceptanceNodeProperty -Node $refreshButton -Name 'isEnabled'
            refreshOffscreen = Get-UiAcceptanceNodeProperty -Node $refreshButton -Name 'isOffscreen'
            processEnabled = Get-UiAcceptanceNodeProperty -Node $startButton -Name 'isEnabled'
            processOffscreen = Get-UiAcceptanceNodeProperty -Node $startButton -Name 'isOffscreen'
        }) -Depth 4), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-after-return.json')
    $null = Invoke-MainAction -Step 'video.refresh-details' `
        -Arguments @('ui','invoke','InboxRefreshButton') -Code 'main.processing_action'
    Wait-MainElement -Selector 'InboxStartProcessingButton' -Code 'main.processing_action'
    $beforeProcessing = Read-MainNodes
    $processButton = Find-MainId -Nodes $beforeProcessing -Id 'InboxStartProcessingButton' `
        -Code 'main.processing_action'
    $batchBar = Find-UiAcceptanceUniqueNode -Nodes $beforeProcessing -AutomationId 'InboxBatchBar' -FailureCode 'main.batch_state'
    $displayBox = Find-UiAcceptanceUniqueNode -Nodes $beforeProcessing -AutomationId 'DisplayNameBox' -FailureCode 'main.batch_state'
    $state = [ordered]@{
        buttonEnabled = Get-UiAcceptanceNodeProperty -Node $processButton -Name 'isEnabled'
        buttonOffscreen = Get-UiAcceptanceNodeProperty -Node $processButton -Name 'isOffscreen'
        batchBarOffscreen = Get-UiAcceptanceNodeProperty -Node $batchBar -Name 'isOffscreen'
        displayNameOffscreen = Get-UiAcceptanceNodeProperty -Node $displayBox -Name 'isOffscreen'
        selectedCountPresent = @($beforeProcessing | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'InboxSelectedCount'
        }).Count
        rows = @($beforeProcessing | Where-Object {
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
            (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').StartsWith('待入库文件 ', [StringComparison]::Ordinal)
        } | ForEach-Object { Get-UiAcceptanceNodeProperty -Node $_ -Name 'name' })
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-before-processing.json'),
        (ConvertTo-Json -InputObject $state -Depth 5), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-before-processing.json')
    $null = Invoke-MainAction -Step 'video.start-processing' `
        -Arguments @('ui','invoke','InboxStartProcessingButton') -Code 'main.processing_start'
    $nodes = Read-MainNodes
    $queued = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
        -NamePrefix '待入库文件 验收视频，状态 排队中' -FailureCode 'main.processing_queue'
    if ($null -eq $queued) { throw 'main.processing_queue' }
    Save-MainScreen -Step 'video-queued'
    $null = Save-MainSnapshot -Step 'video-queued'
    Wait-MainElement -Selector '待入库文件 验收视频，状态 待入库' `
        -Code 'main.processing_ready' -Seconds 120
    $nodes = Read-MainNodes
    $ready = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
        -NamePrefix '待入库文件 验收视频，状态 待入库' -FailureCode 'main.processing_ready'
    if ($null -eq $ready) { throw 'main.processing_ready' }
    Save-MainScreen -Step 'video-ready'
    $null = Save-MainSnapshot -Step 'video-ready'

    $null = Invoke-MainAction -Step 'video.ingest' -Arguments @('ui','invoke','IngestButton') `
        -Code 'main.video_ingest'
    $videoGone = Invoke-WinAppObservation -Arguments @('ui','wait-for','待入库文件 验收视频','--gone') `
        -ProcessId $productProcessId -TimeoutSeconds 90 `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $videoGone -Code 'main.video_ingest'
    $afterVideo = Save-MainSnapshot -Step 'video-ingested'
    if ([int] $afterVideo.tables.media_assets -ne 1) { throw 'main.video_asset' }
    $video = @($afterVideo.formalAssets | Where-Object { [string] $_.actualFilename -ceq 'paired.mp4' })
    if ($video.Count -ne 1 -or [string] $video[0].displayName -cne '验收视频' -or
        [string] $video[0].mediaKind -cne 'video') { throw 'main.video_asset' }
    if (@($video[0].authors).Count -ne 2 -or @($video[0].authors) -cnotcontains '作者甲' -or
        @($video[0].authors) -cnotcontains '作者乙' -or [string] $video[0].series -cne '系列甲' -or
        @($video[0].tags).Count -ne 2 -or @($video[0].tags) -cnotcontains '风景' -or
        @($video[0].tags) -cnotcontains '夜景') { throw 'main.video_metadata' }

    Select-MainRow -Prefix '待入库文件 photo.jpg' -Filename 'photo.jpg' -Step 'select-photo'
    $photoNodes = Read-MainNodes
    $photoIngest = Find-MainId -Nodes $photoNodes -Id 'IngestButton' -Code 'main.photo_inspector'
    if ((Get-UiAcceptanceNodeProperty -Node $photoIngest -Name 'isEnabled') -cne 'True') {
        throw 'main.photo_not_ready'
    }
    $photoTerms = Find-MainId -Nodes $photoNodes -Id 'InboxAuthorInputTerms' -Code 'main.photo_metadata'
    $photoAuthor = @($photoNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq '作者甲'
    })
    if ($null -eq $photoTerms -or $photoAuthor.Count -eq 0) { throw 'main.photo_metadata' }
    Save-MainScreen -Step 'photo-preview'
    $null = Invoke-MainAction -Step 'photo.ingest' -Arguments @('ui','invoke','IngestButton') `
        -Code 'main.photo_ingest'
    $photoGone = Invoke-WinAppObservation -Arguments @('ui','wait-for','待入库文件 photo.jpg','--gone') `
        -ProcessId $productProcessId -TimeoutSeconds 90 `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $photoGone -Code 'main.photo_ingest'
    $afterPhoto = Save-MainSnapshot -Step 'photo-ingested'
    if ([int] $afterPhoto.tables.media_assets -ne 2) { throw 'main.asset_count' }
    $photos = @($afterPhoto.formalAssets | Where-Object { [string] $_.actualFilename -ceq 'photo.jpg' })
    if ($photos.Count -ne 1 -or [string] $photos[0].mediaKind -cne 'image' -or
        @($photos[0].authors) -cnotcontains '作者甲') { throw 'main.photo_asset' }
    if (@($afterPhoto.formalAssets | Where-Object { [string] $_.mediaKind -ceq 'audio' }).Count -ne 0) {
        throw 'main.audio_only_asset'
    }
    foreach ($sourceName in @('paired.mp4','paired.m4a','photo.jpg')) {
        if ([IO.File]::Exists((Join-Path $folders['inbox'] $sourceName))) { throw 'main.inbox_cleanup' }
    }
    $formalFiles = @([IO.Directory]::EnumerateFiles($folders['library'], '*',
        [IO.SearchOption]::AllDirectories) | ForEach-Object { [IO.Path]::GetFileName($_) })
    if ($formalFiles.Count -ne 2 -or $formalFiles -cnotcontains 'paired.mp4' -or
        $formalFiles -cnotcontains 'photo.jpg') { throw 'main.formal_files' }
    $formalPhoto = @([IO.Directory]::EnumerateFiles($folders['library'], 'photo.jpg',
        [IO.SearchOption]::AllDirectories))
    $formalVideo = @([IO.Directory]::EnumerateFiles($folders['library'], 'paired.mp4',
        [IO.SearchOption]::AllDirectories))
    if ($formalPhoto.Count -ne 1 -or $formalVideo.Count -ne 1 -or
        (Get-FileHash -LiteralPath $formalPhoto[0] -Algorithm SHA256).Hash -cne
        '7D1BD6791F1D40FFAFE6C7704FB2E71B6FBF6CBD26B71F9A45E62460D61312A6') {
        throw 'main.formal_photo'
    }
    $header = [byte[]]::new(12)
    $stream = [IO.File]::OpenRead($formalVideo[0])
    try { if ($stream.Read($header, 0, $header.Length) -lt 12) { throw 'main.formal_video' } }
    finally { $stream.Dispose() }
    if ([Text.Encoding]::ASCII.GetString($header, 4, 4) -cne 'ftyp') {
        throw 'main.formal_video'
    }
    Assert-MainVideoReadable -VideoPath $formalVideo[0]

    $null = Invoke-MainAction -Step 'library.open' -Arguments @('ui','invoke','NavLibrary') `
        -Code 'main.library_navigation'
    Wait-MainElement -Selector 'LibraryGrid' -Code 'main.library_navigation'
    $libraryNodes = Read-MainNodes
    $libraryRows = @($libraryNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').StartsWith('正式文件 ',
            [StringComparison]::Ordinal)
    } | ForEach-Object { Get-UiAcceptanceNodeProperty -Node $_ -Name 'name' })
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-library-rows.json'),
        (ConvertTo-Json -InputObject $libraryRows -Depth 3), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-library-rows.json')
    $null = Invoke-MainAction -Step 'library.filter-open' -Arguments @('ui','invoke','LibraryFilterButton') `
        -Code 'main.library_filter'
    $filterNodes = Read-MainNodes
    $filterFacts = @($filterNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId').StartsWith('Library',
            [StringComparison]::Ordinal) -and
        -not (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId').StartsWith('LibraryCard_',
            [StringComparison]::Ordinal)
    } | ForEach-Object {
        [ordered]@{
            id = Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId'
            name = Protect-UiAcceptanceText -Value (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') `
                -RunToken $RunToken -OwnedRoot $OwnedRoot
            type = Get-UiAcceptanceNodeProperty -Node $_ -Name 'type'
            enabled = Get-UiAcceptanceNodeProperty -Node $_ -Name 'isEnabled'
            offscreen = Get-UiAcceptanceNodeProperty -Node $_ -Name 'isOffscreen'
        }
    })
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-library-filter-facts.json'),
        (ConvertTo-Json -InputObject $filterFacts -Depth 4), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-library-filter-facts.json')
    $null = Invoke-MainAction -Step 'library.filter-media-open' `
        -Arguments @('ui','invoke','LibraryMediaFilter') -Code 'main.library_filter'
    $mediaNodes = Read-MainNodes
    $mediaOptions = @($mediaNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq '视频'
    } | ForEach-Object {
        [ordered]@{
            type = Get-UiAcceptanceNodeProperty -Node $_ -Name 'type'
            selector = Get-UiAcceptanceNodeSelector -Node $_ -FailureCode 'main.library_filter'
            offscreen = Get-UiAcceptanceNodeProperty -Node $_ -Name 'isOffscreen'
        }
    })
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-media-options.json'),
        (ConvertTo-Json -InputObject $mediaOptions -Depth 4), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-media-options.json')
    $videoOptions = @($mediaNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq '视频'
    } | ForEach-Object { Get-UiAcceptanceNodeSelector -Node $_ -FailureCode 'main.library_filter' } |
        Select-Object -Unique)
    if ($videoOptions.Count -ne 1) { throw 'main.library_filter' }
    $null = Invoke-MainAction -Step 'library.filter-media-video' `
        -Arguments @('ui','invoke',$videoOptions[0]) -Code 'main.library_filter'
    $null = Invoke-MainAction -Step 'library.filter-name-open' `
        -Arguments @('ui','invoke','LibraryDisplayNameFilter') -Code 'main.library_filter'
    $nameNodes = Read-MainNodes
    $customOptions = @($nameNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -ceq '已自定义'
    } | ForEach-Object { Get-UiAcceptanceNodeSelector -Node $_ -FailureCode 'main.library_filter' } |
        Select-Object -Unique)
    if ($customOptions.Count -ne 1) { throw 'main.library_filter' }
    $null = Invoke-MainAction -Step 'library.filter-name-custom' `
        -Arguments @('ui','invoke',$customOptions[0]) -Code 'main.library_filter'
    $null = Invoke-MainAction -Step 'library.filter-apply' `
        -Arguments @('ui','invoke','LibraryFilterApplyButton') -Code 'main.library_filter'
    $filtered = Read-MainNodes
    $visibleRows = @($filtered | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').StartsWith('正式文件 ',
            [StringComparison]::Ordinal)
    } | ForEach-Object { Get-UiAcceptanceNodeProperty -Node $_ -Name 'name' } | Select-Object -Unique)
    if ($visibleRows.Count -ne 1 -or $visibleRows[0] -cne
        '正式文件 验收视频，实际文件 paired.mp4，视频') { throw 'main.library_filter_result' }
    $null = Save-MainSnapshot -Step 'library-filtered'
    $null = Invoke-MainAction -Step 'library.filter-clear' `
        -Arguments @('ui','invoke','LibraryClearFiltersButton') -Code 'main.library_filter'
    $allRows = Read-MainNodes
    if (@($allRows | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').StartsWith('正式文件 ',
            [StringComparison]::Ordinal)
    } | ForEach-Object { Get-UiAcceptanceNodeProperty -Node $_ -Name 'name' } |
        Select-Object -Unique).Count -ne 2) { throw 'main.library_filter_clear' }
    $allSelection = Invoke-WinAppKeyboard -StepId 'main.library.select-all' `
        -Arguments @('ui','send-keys','ctrl+a','--target','LibraryGrid') -ProcessId $productProcessId `
        -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck -DesktopCheck $DesktopCheck `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $allSelection -Code 'main.library_selection'
    $selectionNodes = Read-MainNodes
    $libraryBatchButton = Find-MainId -Nodes $selectionNodes -Id 'LibraryBatchEditButton' `
        -Code 'main.library_selection'
    if ((Get-UiAcceptanceNodeProperty -Node $libraryBatchButton -Name 'isEnabled') -cne 'True') {
        throw 'main.library_selection'
    }
    $selectedCount = Get-MainValue -Selector 'LibrarySelectedCount' -Code 'main.library_selection'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-library-selection.json'),
        (ConvertTo-Json -InputObject ([ordered]@{ count = $selectedCount })),
        [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-library-selection.json')
    if ($selectedCount -cne '2') { throw 'main.library_selection' }
    $null = Invoke-MainAction -Step 'library.batch-open' `
        -Arguments @('ui','invoke','LibraryBatchEditButton') -Code 'main.library_batch'
    $batchOpening = Read-MainNodes
    $openingIds = @($batchOpening | ForEach-Object {
        Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId'
    } | Where-Object { $_ -like 'LibraryBatch*' } | Select-Object -Unique)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-library-batch-open.json'),
        (ConvertTo-Json -InputObject $openingIds -Depth 3), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-library-batch-open.json')
    $batchStatus = Get-MainValue -Selector 'LibraryBatchStatusMessage' -Code 'main.library_batch'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-library-batch-status.json'),
        (ConvertTo-Json -InputObject ([ordered]@{
            status = Protect-UiAcceptanceText -Value $batchStatus -RunToken $RunToken -OwnedRoot $OwnedRoot
        })), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-library-batch-status.json')
    if ($openingIds -cnotcontains 'LibraryBatchEditDialog') {
        $logLines = @()
        foreach ($log in @(Get-ChildItem -LiteralPath (Join-Path $OwnedRoot 'logs') -Filter 'tracefold-*.log' `
            -File -ErrorAction SilentlyContinue)) {
            $lines = @(Get-Content -LiteralPath $log.FullName -Tail 100)
            for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
                if ($lines[$lineIndex] -notlike '*LibraryBatchEditFailed*') { continue }
                $last = [Math]::Min($lineIndex + 24, $lines.Count - 1)
                foreach ($entry in $lines[$lineIndex..$last]) {
                    $logLines += Protect-UiAcceptanceText -Value $entry -RunToken $RunToken `
                        -OwnedRoot $OwnedRoot
                }
            }
        }
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-library-batch-error.json'),
            (ConvertTo-Json -InputObject $logLines -Depth 3), [Text.UTF8Encoding]::new($false))
        $artifacts.Add('main-library-batch-error.json')
        throw 'main.library_batch'
    }
    Wait-MainElement -Selector 'LibraryBatchEditScrollRoot' -Code 'main.library_batch'
    $null = Invoke-MainAction -Step 'library.naming-tab' `
        -Arguments @('ui','invoke','LibraryBatchNamingTab') -Code 'main.library_batch'
    $batchNodes = Read-MainNodes
    $batchFacts = @($batchNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId').StartsWith('Library',
            [StringComparison]::Ordinal) -and
        -not (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId').StartsWith('LibraryCard_',
            [StringComparison]::Ordinal)
    } | ForEach-Object {
        [ordered]@{
            id = Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId'
            name = Protect-UiAcceptanceText -Value (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') `
                -RunToken $RunToken -OwnedRoot $OwnedRoot
            type = Get-UiAcceptanceNodeProperty -Node $_ -Name 'type'
            enabled = Get-UiAcceptanceNodeProperty -Node $_ -Name 'isEnabled'
            offscreen = Get-UiAcceptanceNodeProperty -Node $_ -Name 'isOffscreen'
        }
    })
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-library-batch-facts.json'),
        (ConvertTo-Json -InputObject $batchFacts -Depth 4), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-library-batch-facts.json')
    $null = Invoke-MainAction -Step 'library.naming-add-prefix' `
        -Arguments @('ui','invoke','LibraryNamingAddRuleButton') -Code 'main.library_naming'
    Wait-MainElement -Selector 'LibraryNamingTextParameter' -Code 'main.library_naming'
    $null = Invoke-MainAction -Step 'library.naming-prefix-text' `
        -Arguments @('ui','set-value','LibraryNamingTextParameter','验收-') -Code 'main.library_naming'
    if ((Get-MainValue -Selector 'LibraryNamingTextParameter' -Code 'main.library_naming') -cne '验收-') {
        throw 'main.library_naming'
    }
    $previewNodes = Read-MainNodes
    $previewNames = @($previewNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'Text' -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name').StartsWith('验收-',
            [StringComparison]::Ordinal)
    } | ForEach-Object { Get-UiAcceptanceNodeProperty -Node $_ -Name 'name' } |
        Select-Object -Unique)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-naming-preview.json'),
        (ConvertTo-Json -InputObject $previewNames -Depth 3), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-naming-preview.json')
    if ($previewNames.Count -lt 2) { throw 'main.library_naming_preview' }
    $applyButton = Find-MainId -Nodes $previewNodes -Id 'LibraryBatchApplyButton' `
        -Code 'main.library_naming'
    if ((Get-UiAcceptanceNodeProperty -Node $applyButton -Name 'isEnabled') -cne 'True') {
        throw 'main.library_naming_preview'
    }
    $null = Invoke-MainAction -Step 'library.naming-apply' `
        -Arguments @('ui','invoke','LibraryBatchApplyButton') -Code 'main.library_naming'
    $namingClosed = Invoke-WinAppObservation `
        -Arguments @('ui','wait-for','LibraryNamingAddRuleButton','--gone') `
        -ProcessId $productProcessId -TimeoutSeconds 45 -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $namingClosed -Code 'main.library_naming_completion'
    $afterNaming = Save-MainSnapshot -Step 'library-named'
    if (@($afterNaming.formalAssets | Where-Object {
        -not ([string]$_.displayName).StartsWith('验收-', [StringComparison]::Ordinal)
    }).Count -ne 0) { throw 'main.library_naming_result' }
    $null = Invoke-MainAction -Step 'library.naming-undo' `
        -Arguments @('ui','invoke','LibraryBatchUndoButton') -Code 'main.library_undo'
    $afterUndo = Save-MainSnapshot -Step 'library-undo'
    $undoneVideo = @($afterUndo.formalAssets | Where-Object { $_.actualFilename -ceq 'paired.mp4' })
    $undonePhoto = @($afterUndo.formalAssets | Where-Object { $_.actualFilename -ceq 'photo.jpg' })
    if ($undoneVideo.Count -ne 1 -or $undonePhoto.Count -ne 1 -or
        $undoneVideo[0].displayName -cne '验收视频' -or
        $null -ne $undonePhoto[0].displayName) { throw 'main.library_undo_result' }
    $null = Invoke-MainAction -Step 'taxonomy.open' `
        -Arguments @('ui','invoke','NavTaxonomy') -Code 'main.taxonomy_navigation'
    Wait-MainElement -Selector 'TaxonomyTermList' -Code 'main.taxonomy_navigation'
    $null = Save-MainSnapshot -Step 'taxonomy-before'
    $null = Invoke-MainAction -Step 'taxonomy.create-open' `
        -Arguments @('ui','invoke','TaxonomyCreateButton') -Code 'main.taxonomy_create'
    Wait-MainElement -Selector 'TaxonomyNameInput' -Code 'main.taxonomy_create'
    $promptNodes = Read-MainNodes
    $promptFacts = @($promptNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -cin @('Button','Edit') -and
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'isOffscreen') -ceq 'False'
    } | ForEach-Object {
        [ordered]@{
            id = Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId'
            name = Protect-UiAcceptanceText -Value (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') `
                -RunToken $RunToken -OwnedRoot $OwnedRoot
            type = Get-UiAcceptanceNodeProperty -Node $_ -Name 'type'
            selector = Get-UiAcceptanceNodeSelector -Node $_ -FailureCode 'main.taxonomy_create'
        }
    })
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-taxonomy-prompt.json'),
        (ConvertTo-Json -InputObject $promptFacts -Depth 4), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-taxonomy-prompt.json')
    $null = Invoke-MainAction -Step 'taxonomy.create-name' `
        -Arguments @('ui','set-value','TaxonomyNameInput','临时作者') -Code 'main.taxonomy_create'
    if ((Get-MainValue -Selector 'TaxonomyNameInput' -Code 'main.taxonomy_create') -cne '临时作者') {
        throw 'main.taxonomy_create'
    }
    $null = Invoke-MainAction -Step 'taxonomy.create-confirm' `
        -Arguments @('ui','invoke','PrimaryButton') -Code 'main.taxonomy_create'
    $createClosed = Invoke-WinAppObservation -Arguments @('ui','wait-for','TaxonomyNameInput','--gone') `
        -ProcessId $productProcessId -TimeoutSeconds 30 -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $createClosed -Code 'main.taxonomy_create'
    $created = Save-MainSnapshot -Step 'taxonomy-created'
    if (@($created.taxonomy.author) -cnotcontains '临时作者') { throw 'main.taxonomy_create_fact' }
    $termNodes = Read-MainNodes
    $termRows = @($termNodes | Where-Object {
        (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
        @(Get-MainDescendantText -Node $_) -ccontains '临时作者'
    } | ForEach-Object {
        [ordered]@{
            selector = Get-UiAcceptanceNodeSelector -Node $_ -FailureCode 'main.taxonomy_row'
            text = @(Get-MainDescendantText -Node $_)
        }
    })
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-taxonomy-row.json'),
        (ConvertTo-Json -InputObject $termRows -Depth 4), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-taxonomy-row.json')
    if ($termRows.Count -ne 1) { throw 'main.taxonomy_row' }
    $renameButton = Find-MainId -Nodes $termNodes -Id 'TaxonomyRenameButton' `
        -Code 'main.taxonomy_rename'
    if ((Get-UiAcceptanceNodeProperty -Node $renameButton -Name 'isEnabled') -cne 'True') {
        throw 'main.taxonomy_rename'
    }
    $null = Invoke-MainAction -Step 'taxonomy.rename-open' `
        -Arguments @('ui','invoke','TaxonomyRenameButton') -Code 'main.taxonomy_rename'
    Wait-MainElement -Selector 'TaxonomyNameInput' -Code 'main.taxonomy_rename'
    $null = Invoke-MainAction -Step 'taxonomy.rename-name' `
        -Arguments @('ui','set-value','TaxonomyNameInput','验收作者') -Code 'main.taxonomy_rename'
    if ((Get-MainValue -Selector 'TaxonomyNameInput' -Code 'main.taxonomy_rename') -cne '验收作者') {
        throw 'main.taxonomy_rename'
    }
    $null = Invoke-MainAction -Step 'taxonomy.rename-confirm' `
        -Arguments @('ui','invoke','PrimaryButton') -Code 'main.taxonomy_rename'
    $renameClosed = Invoke-WinAppObservation -Arguments @('ui','wait-for','TaxonomyNameInput','--gone') `
        -ProcessId $productProcessId -TimeoutSeconds 30 -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $renameClosed -Code 'main.taxonomy_rename'
    $renamed = Save-MainSnapshot -Step 'taxonomy-renamed'
    if (@($renamed.taxonomy.author) -cnotcontains '验收作者' -or
        @($renamed.taxonomy.author) -ccontains '临时作者') { throw 'main.taxonomy_rename_fact' }
    $deleteButton = Find-MainId -Nodes (Read-MainNodes) -Id 'TaxonomyDeleteButton' `
        -Code 'main.taxonomy_delete'
    if ((Get-UiAcceptanceNodeProperty -Node $deleteButton -Name 'isEnabled') -cne 'True') {
        throw 'main.taxonomy_delete'
    }
    $null = Invoke-MainAction -Step 'taxonomy.delete-open' `
        -Arguments @('ui','invoke','TaxonomyDeleteButton') -Code 'main.taxonomy_delete'
    Wait-MainElement -Selector 'TaxonomyDeleteConfirmDialog' -Code 'main.taxonomy_delete'
    $null = Invoke-MainAction -Step 'taxonomy.delete-confirm' `
        -Arguments @('ui','invoke','PrimaryButton') -Code 'main.taxonomy_delete'
    $deleteClosed = Invoke-WinAppObservation `
        -Arguments @('ui','wait-for','TaxonomyDeleteConfirmDialog','--gone') `
        -ProcessId $productProcessId -TimeoutSeconds 30 -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $deleteClosed -Code 'main.taxonomy_delete'
    $termGone = Invoke-WinAppObservation -Arguments @('ui','wait-for','验收作者','--gone') `
        -ProcessId $productProcessId -TimeoutSeconds 30 -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
    Assert-MainResult -Result $termGone -Code 'main.taxonomy_delete'
    $deleted = Save-MainSnapshot -Step 'taxonomy-deleted'
    if (@($deleted.taxonomy.author) -ccontains '验收作者') { throw 'main.taxonomy_delete_fact' }
    $null = Invoke-MainAction -Step 'taxonomy.undo' `
        -Arguments @('ui','invoke','TaxonomyUndoButton') -Code 'main.taxonomy_undo'
    Wait-MainElement -Selector '验收作者' -Code 'main.taxonomy_undo' -Seconds 30
    $taxonomyUndone = Save-MainSnapshot -Step 'taxonomy-undo'
    if (@($taxonomyUndone.taxonomy.author) -cnotcontains '验收作者') {
        throw 'main.taxonomy_undo_fact'
    }
    $null = Invoke-MainAction -Step 'diagnostics.open-settings' `
        -Arguments @('ui','invoke','NavSettings') -Code 'main.diagnostics'
    Wait-MainElement -Selector 'SystemDiagnosticsTab' -Code 'main.diagnostics'
    $null = Invoke-MainAction -Step 'diagnostics.open-tab' `
        -Arguments @('ui','invoke','SystemDiagnosticsTab') -Code 'main.diagnostics'
    Wait-MainElement -Selector 'DiagnosticsOverallStatus' -Code 'main.diagnostics'
    $diagnostics = Wait-MainDiagnostics -Code 'main.diagnostics'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-diagnostics.json'),
        (ConvertTo-Json -InputObject $diagnostics -Depth 3), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-diagnostics.json')
    $null = Save-MainSnapshot -Step 'diagnostics'

    $stopped = @(Stop-OwnedProcessTree -Ledger @($launched) `
        -ApprovedDirectories @([IO.Path]::GetDirectoryName($ExecutablePath)))
    if ($stopped -cnotcontains $productProcessId) { throw 'main.restart_stop' }
    $closedSnapshot = Save-MainSnapshot -Step 'closed'
    if ([int] $closedSnapshot.tables.media_assets -ne 2 -or
        @($closedSnapshot.taxonomy.author) -cnotcontains '验收作者') {
        throw 'main.restart_persistence'
    }
    $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
        -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
    $productProcessId = [int] $launched.ProcessId
    Wait-MainElement -Selector 'TraceFoldMainWindow' -Code 'main.restart_window'
    $null = Invoke-MainAction -Step 'restart.open-settings' `
        -Arguments @('ui','invoke','NavSettings') -Code 'main.restart_settings'
    Wait-MainElement -Selector 'FolderSettingsTab' -Code 'main.restart_settings'
    $null = Invoke-MainAction -Step 'restart.folder-tab' `
        -Arguments @('ui','invoke','FolderSettingsTab') -Code 'main.restart_settings'
    Wait-MainElement -Selector 'InboxFolderPath' -Code 'main.restart_settings'
    $folderChecks = @()
    foreach ($input in @($prep.folderInputs)) {
        $actualFolder = Get-MainValue -Selector ([string]$input.automationId) `
            -Code 'main.restart_settings'
        $expectedFolder = $folders[[string]$input.key]
        $sameFolder = -not [string]::IsNullOrWhiteSpace($actualFolder) -and
            [IO.Path]::GetFullPath($actualFolder).TrimEnd([char[]]@('\','/')) -ieq
            $expectedFolder.TrimEnd([char[]]@('\','/'))
        $folderChecks += [ordered]@{
            key = [string]$input.key
            hasValue = -not [string]::IsNullOrWhiteSpace($actualFolder)
            matchesSaved = $sameFolder
            length = $actualFolder.Length
        }
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-restart-folders.json'),
        (ConvertTo-Json -InputObject $folderChecks -Depth 4), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-restart-folders.json')
    if (@($folderChecks | Where-Object { -not $_.matchesSaved }).Count -gt 0) {
        throw 'main.restart_settings'
    }
    $null = Invoke-MainAction -Step 'restart.library' -Arguments @('ui','invoke','NavLibrary') `
        -Code 'main.restart_library'
    Wait-MainElement -Selector '正式文件 验收视频' -Code 'main.restart_library'
    Wait-MainElement -Selector '正式文件 photo.jpg' -Code 'main.restart_library'
    $reopenNodes = Read-MainNodes
    $photoRow = Find-UiAcceptanceUniqueNode -Nodes $reopenNodes -NativeType 'ListItem' `
        -NamePrefix '正式文件 photo.jpg，实际文件 photo.jpg' -FailureCode 'main.restart_photo'
    if ($null -eq $photoRow) { throw 'main.restart_photo' }
    $photoSelector = Get-UiAcceptanceNodeSelector -Node $photoRow -FailureCode 'main.restart_photo'
    $null = Invoke-MainAction -Step 'restart.select-photo' `
        -Arguments @('ui','invoke',$photoSelector) -Code 'main.restart_photo'
    $selectedPhoto = Read-MainNodes
    $null = Find-MainId -Nodes $selectedPhoto -Id 'LibraryDisplayNameBox' `
        -Code 'main.restart_photo'
    Save-MainScreen -Step 'photo-reopened'
    $reopened = Save-MainSnapshot -Step 'reopened'
    if ([int] $reopened.tables.media_assets -ne 2 -or
        @($reopened.taxonomy.author) -cnotcontains '验收作者') {
        throw 'main.restart_persistence'
    }
    $null = Invoke-MainAction -Step 'restart.taxonomy' `
        -Arguments @('ui','invoke','NavTaxonomy') -Code 'main.restart_taxonomy'
    Wait-MainElement -Selector '验收作者' -Code 'main.restart_taxonomy'
    $null = Invoke-MainAction -Step 'restart.diagnostics-settings' `
        -Arguments @('ui','invoke','NavSettings') -Code 'main.restart_diagnostics'
    Wait-MainElement -Selector 'SystemDiagnosticsTab' -Code 'main.restart_diagnostics'
    $null = Invoke-MainAction -Step 'restart.diagnostics-tab' `
        -Arguments @('ui','invoke','SystemDiagnosticsTab') -Code 'main.restart_diagnostics'
    $diagnosticsReopened = Wait-MainDiagnostics -Code 'main.restart_diagnostics'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-diagnostics-reopened.json'),
        (ConvertTo-Json -InputObject $diagnosticsReopened -Depth 3), [Text.UTF8Encoding]::new($false))
    $artifacts.Add('main-diagnostics-reopened.json')
    foreach ($log in @(Get-ChildItem -LiteralPath (Join-Path $OwnedRoot 'logs') `
        -Filter 'tracefold-*.log' -File -ErrorAction SilentlyContinue)) {
        $logStream = [IO.FileStream]::new($log.FullName, [IO.FileMode]::Open,
            [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        try {
            $reader = [IO.StreamReader]::new($logStream, [Text.Encoding]::UTF8, $true,
                4096, $true)
            try { $logText = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        finally { $logStream.Dispose() }
        if ($logText.IndexOf('UnhandledException', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $logText.IndexOf($OwnedRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw 'main.product_log_privacy'
        }
    }
    $closed = @(Stop-OwnedProcessTree -Ledger @($launched) `
        -ApprovedDirectories @([IO.Path]::GetDirectoryName($ExecutablePath)))
    if ($closed -cnotcontains $productProcessId) { throw 'main.close' }
    return [pscustomobject]@{
        ScenarioId = $ScenarioId
        Status = 'Passed'
        Reason = ''
        Artifacts = $artifacts.ToArray()
        Viewports = @()
    }
}
catch {
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or -not $code.StartsWith('main.', [StringComparison]::Ordinal)) {
        $unexpected = [ordered]@{
            type = $_.Exception.GetType().FullName
            detail = Protect-UiAcceptanceText -Value $code -RunToken $RunToken -OwnedRoot $OwnedRoot
        }
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'main-unexpected.json'),
            (ConvertTo-Json -InputObject $unexpected -Depth 3), [Text.UTF8Encoding]::new($false))
        $artifacts.Add('main-unexpected.json')
        $code = 'main.unexpected'
    }
    $status = if ($code -in @('main.desktop_unavailable','main.window_unavailable','main.fixture_missing','main.fixture_mismatch')) {
        'Blocked'
    } else { 'Failed' }
    return [pscustomobject]@{ ScenarioId=$ScenarioId; Status=$status; Reason=$code; Artifacts=$artifacts.ToArray(); Viewports=@() }
}
finally {
    if ($null -ne $launched) {
        $null = Stop-OwnedProcessTree -Ledger @($launched) `
            -ApprovedDirectories @([IO.Path]::GetDirectoryName($ExecutablePath))
    }
}
