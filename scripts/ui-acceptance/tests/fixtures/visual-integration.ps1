# Task 13 integration fixture.
#
# The real public entry, real Visual scenario top level, Runner, Evidence and report writers run
# from a test-owned source tree or a moved package. Only these boundaries are replaced:
#   * process start/query/desktop/foreground checks use one synthetic, positive identity;
#   * the Driver executable is the controlled fixture engine;
#   * root/window/native Support commands are answered by that engine;
#   * theme inspect/apply/restore are forwarded unchanged to the real Support CLI with the
#     fixture root and process licence already supplied by the real wrapper.
#
# The engine owns UI and workflow state, not scenario orchestration. It never creates a report,
# outcome, index or theme decision.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-VisualIntegrationJson {
    param([Parameter(Mandatory)][string] $Path)
    return ([IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json)
}

function Write-VisualIntegrationJson {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)] $Value
    )
    $directory = Split-Path -Parent $Path
    if (-not [IO.Directory]::Exists($directory)) { [void][IO.Directory]::CreateDirectory($directory) }
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20),
        [Text.UTF8Encoding]::new($false))
}

function Get-VisualIntegrationHash {
    param([Parameter(Mandatory)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-VisualIntegrationBytesHash {
    param([Parameter(Mandatory)][byte[]] $Bytes)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hasher.ComputeHash($Bytes))).Replace('-', '') }
    finally { $hasher.Dispose() }
}

function Get-VisualIntegrationTreeHash {
    param([Parameter(Mandatory)][string] $Root)
    $buffer = New-Object IO.MemoryStream
    try {
        foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName)) {
            $relative = (($file.FullName.Substring($Root.Length).TrimStart([char] 92)) -replace '\\', '/')
            $header = [Text.UTF8Encoding]::new($false).GetBytes(
                $relative + "`n" + [string] $file.Length + "`n")
            $buffer.Write($header, 0, $header.Length)
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            $buffer.Write($bytes, 0, $bytes.Length)
        }
        return Get-VisualIntegrationBytesHash -Bytes $buffer.ToArray()
    }
    finally { $buffer.Dispose() }
}

function Copy-VisualIntegrationFile {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination
    )
    $directory = Split-Path -Parent $Destination
    if (-not [IO.Directory]::Exists($directory)) { [void][IO.Directory]::CreateDirectory($directory) }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Move-VisualIntegrationOwnedTree {
    param([string] $Path, [string] $Destination, [string] $Boundary)
    $source = [IO.Path]::GetFullPath($Path)
    $target = [IO.Path]::GetFullPath($Destination)
    $prefix = [IO.Path]::GetFullPath($Boundary).TrimEnd([char] 92) + [char] 92
    foreach ($candidate in @($source, $target)) {
        if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'visual.integration.move_outside_owned_tree'
        }
    }
    if (Test-Path -LiteralPath $target) { throw 'visual.integration.move_target_exists' }
    if ((Get-Item -LiteralPath $source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'visual.integration.move_reparse_point'
    }
    Move-Item -LiteralPath $source -Destination $target
    if ((Test-Path -LiteralPath $source) -or -not [IO.Directory]::Exists($target)) {
        throw 'visual.integration.move_not_observed'
    }
}

function Write-VisualIntegrationFirstFailure {
    param([string] $Directory, [Parameter(Mandatory)] $Failure)
    $path = Join-Path $Directory 'first-failure.json'
    if ([IO.File]::Exists($path)) { return }
    Write-VisualIntegrationJson -Path $path -Value ([ordered] @{
        exception = $Failure.Exception.ToString()
        errorRecord = ($Failure | Out-String)
        scriptStackTrace = [string] $Failure.ScriptStackTrace
        position = [string] $Failure.InvocationInfo.PositionMessage
        recordedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    })
}

function Replace-VisualIntegrationOnce {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Old,
        [Parameter(Mandatory)][string] $New,
        [Parameter(Mandatory)][string] $Label
    )
    $encoding = [Text.UTF8Encoding]::new($false, $true)
    $text = [IO.File]::ReadAllText($Path, $encoding)
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $normalizedOld = ($Old -replace "`r`n", "`n") -replace "`n", $newline
    $normalizedNew = ($New -replace "`r`n", "`n") -replace "`n", $newline
    $first = $text.IndexOf($normalizedOld, [StringComparison]::Ordinal)
    if ($first -lt 0) { throw ('visual.integration.patch_missing: ' + $Label) }
    if ($text.IndexOf($normalizedOld, $first + 1, [StringComparison]::Ordinal) -ge 0) {
        throw ('visual.integration.patch_ambiguous: ' + $Label)
    }
    $after = $text.Substring(0, $first) + $normalizedNew +
        $text.Substring($first + $normalizedOld.Length)
    $beforeSha256 = Get-VisualIntegrationBytesHash -Bytes $encoding.GetBytes($text)
    $afterSha256 = Get-VisualIntegrationBytesHash -Bytes $encoding.GetBytes($after)
    [IO.File]::WriteAllText($Path, $after, $encoding)
    return [pscustomobject] @{
        label = $Label
        beforeSha256 = $beforeSha256
        afterSha256 = $afterSha256
        beforeText = $normalizedOld
        afterText = $normalizedNew
    }
}

function New-VisualIntegrationPayload {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Base,
        [Parameter(Mandatory)][string] $RealSupportExecutable
    )

    $projectSource = Join-Path $Root 'scripts/ui-acceptance/tests/fixtures/visual-integration'
    $project = Join-Path $Base 'engine-project'
    $engineOutput = Join-Path $Base 'engine-output'
    $payload = Join-Path $Base 'support-payload'
    [void][IO.Directory]::CreateDirectory($project)
    Copy-VisualIntegrationFile -Source (Join-Path $projectSource 'VisualFixture.csproj') `
        -Destination (Join-Path $project 'VisualFixture.csproj')
    Copy-VisualIntegrationFile -Source (Join-Path $projectSource 'Program.cs') `
        -Destination (Join-Path $project 'Program.cs')

    $publishLog = Join-Path $Base 'engine-publish.log'
    & dotnet publish (Join-Path $project 'VisualFixture.csproj') -c Release -o $engineOutput `
        --nologo -p:UseSharedCompilation=false -nodeReuse:false *> $publishLog
    $publishExit = $LASTEXITCODE
    if ($publishExit -ne 0) {
        $tail = if ([IO.File]::Exists($publishLog)) { ([IO.File]::ReadAllText($publishLog)).Trim() } else { '' }
        if ($tail.Length -gt 1200) { $tail = $tail.Substring($tail.Length - 1200) }
        throw ('visual.integration.engine_publish_failed: exit ' + $publishExit + ' ' + $tail)
    }

    [void][IO.Directory]::CreateDirectory($payload)
    $realSupportRoot = Split-Path -Parent $RealSupportExecutable
    $realSupportCopyRoot = Join-Path $payload 'real-support'
    [void][IO.Directory]::CreateDirectory($realSupportCopyRoot)
    Copy-Item -Path (Join-Path $realSupportRoot '*') -Destination $realSupportCopyRoot -Recurse -Force
    Copy-Item -Path (Join-Path $engineOutput '*') -Destination $payload -Recurse -Force
    $realSupportCopy = Join-Path $realSupportCopyRoot 'TraceFold.Media.UiAcceptance.Support.exe'

    return [pscustomobject] @{
        Payload = $payload
        Engine = Join-Path $payload 'TraceFold.Media.UiAcceptance.Support.exe'
        RealSupport = $realSupportCopy
        PublishLog = $publishLog
        PublishExit = $publishExit
        EngineSource = (Join-Path $projectSource 'Program.cs')
        EngineSourceSha256 = Get-VisualIntegrationHash -Path (Join-Path $projectSource 'Program.cs')
        EngineProject = (Join-Path $projectSource 'VisualFixture.csproj')
        EngineProjectSha256 = Get-VisualIntegrationHash -Path (Join-Path $projectSource 'VisualFixture.csproj')
        PayloadTreeSha256 = Get-VisualIntegrationTreeHash -Root $payload
    }
}

function New-VisualIntegrationTree {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Base,
        [Parameter(Mandatory)] $Payload,
        [ValidateSet('Source', 'Package')][string] $TreeKind
    )

    $tree = Join-Path $Base 's'
    $scriptsRoot = Join-Path $tree 'scripts'
    $uiRoot = Join-Path $scriptsRoot 'ui-acceptance'
    $scenarioRoot = Join-Path $uiRoot 'scenarios'
    [void][IO.Directory]::CreateDirectory($scenarioRoot)

    Copy-VisualIntegrationFile -Source (Join-Path $Root 'scripts/run-stage5cc-ui-acceptance.ps1') `
        -Destination (Join-Path $scriptsRoot 'run-stage5cc-ui-acceptance.ps1')
    foreach ($module in @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner',
            'Workflows', 'Metadata', 'VisualGate')) {
        Copy-VisualIntegrationFile `
            -Source (Join-Path $Root ('scripts/ui-acceptance/TraceFold.UiAcceptance.' + $module + '.psm1')) `
            -Destination (Join-Path $uiRoot ('TraceFold.UiAcceptance.' + $module + '.psm1'))
    }
    Copy-VisualIntegrationFile -Source (Join-Path $Root 'scripts/ui-acceptance/scenarios/70-visual-keyboard-matrix.ps1') `
        -Destination (Join-Path $scenarioRoot '70-visual-keyboard-matrix.ps1')
    Copy-Item -LiteralPath (Join-Path $Root 'scripts/ui-acceptance/report-template') `
        -Destination (Join-Path $uiRoot 'report-template') -Recurse -Force

    $supportRoot = Join-Path $tree 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap'
    [void][IO.Directory]::CreateDirectory($supportRoot)
    Copy-Item -Path (Join-Path $Payload.Payload '*') -Destination $supportRoot -Recurse -Force

    $contractRelative = 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
    $contract = Join-Path $tree $contractRelative
    Copy-VisualIntegrationFile -Source (Join-Path $Root $contractRelative) -Destination $contract
    $contractDocument = Read-VisualIntegrationJson -Path $contract
    foreach ($asset in @($contractDocument.fixtureAssets)) {
        $relative = [string] $asset.repositoryPath
        Copy-VisualIntegrationFile -Source (Join-Path $Root $relative) -Destination (Join-Path $tree $relative)
    }

    $driverStage = Join-Path $Base 'driver-stage'
    [void][IO.Directory]::CreateDirectory($driverStage)
    [IO.File]::WriteAllBytes((Join-Path $driverStage 'winapp.exe'), [byte[]] @(1, 2, 3))
    $driverArchive = Join-Path $Base 'winapp.zip'
    if ([IO.File]::Exists($driverArchive)) { Remove-Item -LiteralPath $driverArchive -Force }
    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    [IO.Compression.ZipFile]::CreateFromDirectory($driverStage, $driverArchive)
    $driverManifestPath = Join-Path $tree 'apps/media-desktop/tools/winapp/win-x64/manifest.json'
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $driverManifestPath))
    $driverManifest = [ordered] @{
        version = 'visual-integration-fixture'
        archiveSize = [long] (Get-Item -LiteralPath $driverArchive).Length
        sha256 = (Get-VisualIntegrationHash -Path $driverArchive)
    }
    Write-VisualIntegrationJson -Path $driverManifestPath -Value $driverManifest

    $patches = New-Object System.Collections.ArrayList
    $patchManifest = Join-Path $Base 'patches.json'
    function Add-PatchFact {
        param([string] $Path, [Parameter(Mandatory)] $Patch)
        [void] $patches.Add([ordered] @{
            label = [string] $Patch.label
            path = (($Path.Substring($tree.Length).TrimStart([char] 92)) -replace '\\', '/')
            beforeSha256 = [string] $Patch.beforeSha256
            afterSha256 = [string] $Patch.afterSha256
            beforeText = [string] $Patch.beforeText
            afterText = [string] $Patch.afterText
        })
        Write-VisualIntegrationJson -Path $patchManifest -Value ([ordered] @{
            treeKind = $TreeKind; tree = $tree; patches = $patches.ToArray()
        })
    }

    $entryPath = Join-Path $scriptsRoot 'run-stage5cc-ui-acceptance.ps1'
    $entryText = [IO.File]::ReadAllText($entryPath, [Text.UTF8Encoding]::new($false, $true))
    $nl = if ($entryText.Contains("`r`n")) { "`r`n" } else { "`n" }
    $preflightOld = @'
    if ([string]::IsNullOrWhiteSpace($blockedReason)) {
        if (-not (Test-UiInteractiveDesktop)) {
            $blockedReason = 'interactive_desktop_unavailable'
        }
        elseif (@(Get-Process -Name 'TraceFold.Media' -ErrorAction SilentlyContinue).Count -gt 0) {
            $blockedReason = 'ordinary_product_process_running'
        }
    }
'@
    $preflightNew = @'
    if ([string]::IsNullOrWhiteSpace($blockedReason) -and
        $env:TRACEFOLD_VISUAL_SYNTHETIC -ne '1') {
        if (-not (Test-UiInteractiveDesktop)) {
            $blockedReason = 'interactive_desktop_unavailable'
        }
        elseif (@(Get-Process -Name 'TraceFold.Media' -ErrorAction SilentlyContinue).Count -gt 0) {
            $blockedReason = 'ordinary_product_process_running'
        }
    }
'@
    $patch = Replace-VisualIntegrationOnce -Path $entryPath -Old $preflightOld -New $preflightNew `
        -Label 'entry-synthetic-preflight'
    Add-PatchFact -Path $entryPath -Patch $patch

    $publishOld = @'
    $supportProject = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/TraceFold.Media.UiAcceptance.Support.csproj'
    $supportRoot = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap'
    $packageCache = Join-Path $script:repositoryRoot 'apps/media-desktop/.packages'
    & dotnet publish $supportProject -c Release -o $supportRoot -p:RestorePackagesPath=$packageCache | Out-Null
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine('The support CLI could not be published.')
        exit $ExitBlocked
    }
    $supportExecutable = Join-Path $supportRoot 'TraceFold.Media.UiAcceptance.Support.exe'
'@
    $publishNew = @'
    $supportRoot = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap'
    $supportExecutable = Join-Path $supportRoot 'TraceFold.Media.UiAcceptance.Support.exe'
    if (-not [IO.File]::Exists($supportExecutable)) {
        $supportProject = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/TraceFold.Media.UiAcceptance.Support.csproj'
        $packageCache = Join-Path $script:repositoryRoot 'apps/media-desktop/.packages'
        & dotnet publish $supportProject -c Release -o $supportRoot -p:RestorePackagesPath=$packageCache | Out-Null
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine('The support CLI could not be published.')
            exit $ExitBlocked
        }
    }
'@
    $patch = Replace-VisualIntegrationOnce -Path $entryPath -Old $publishOld -New $publishNew `
        -Label 'entry-reuse-test-owned-support'
    Add-PatchFact -Path $entryPath -Patch $patch

    $driverPath = Join-Path $uiRoot 'TraceFold.UiAcceptance.Driver.psm1'
    $driverOld = @'
    try { $target = (Resolve-Path -LiteralPath $WinAppPath -ErrorAction Stop).ProviderPath }
    catch {
        $result = [pscustomobject]@{ Status='Blocked'; ExitCode=5; TimedOut=$false; Stdout=''; Stderr='driver unavailable'; Attempts=0; Error='driver unavailable'; Data=$null }
        Set-UiDriverLastResult -WinAppPath $WinAppPath -Arguments $Arguments -Result $result -RunToken $RunToken -OwnedRoot $OwnedRoot
        return $result
    }
'@
    $driverNew = @'
    $fixtureDriver = [string] $env:TRACEFOLD_VISUAL_DRIVER
    if (-not [string]::IsNullOrWhiteSpace($fixtureDriver) -and [IO.File]::Exists($fixtureDriver)) {
        $target = [IO.Path]::GetFullPath($fixtureDriver)
    }
    else {
        try { $target = (Resolve-Path -LiteralPath $WinAppPath -ErrorAction Stop).ProviderPath }
        catch {
            $result = [pscustomobject]@{ Status='Blocked'; ExitCode=5; TimedOut=$false; Stdout=''; Stderr='driver unavailable'; Attempts=0; Error='driver unavailable'; Data=$null }
            Set-UiDriverLastResult -WinAppPath $WinAppPath -Arguments $Arguments -Result $result -RunToken $RunToken -OwnedRoot $OwnedRoot
            return $result
        }
    }
'@
    $patch = Replace-VisualIntegrationOnce -Path $driverPath -Old $driverOld -New $driverNew `
        -Label 'driver-controlled-endpoint'
    Add-PatchFact -Path $driverPath -Patch $patch

    $processPath = Join-Path $uiRoot 'TraceFold.UiAcceptance.Processes.psm1'
    # Preserve Start-TraceFoldScenario's ledger and all identity/path checks. Only the
    # default OS inventory/stop scriptblocks read and update synthetic process state.
    # PS5.1 emits a top-level JSON array as one pipeline object, unlike PS7. Parse it
    # as an object property so [] stays an empty array on both hosts. Reject a non-array;
    # do not discard malformed records or default a missing Stopped field to success.
    $inventoryOld = @'
        [scriptblock] $Inventory = {
            $records = @()
            foreach ($item in @(Get-CimInstance Win32_Process -ErrorAction Stop)) {
'@
    $inventoryNew = @'
        [scriptblock] $Inventory = {
            if ($env:TRACEFOLD_VISUAL_SYNTHETIC -eq '1') {
                $stateDocument = ConvertFrom-Json -InputObject ('{"records":' + [IO.File]::ReadAllText($env:TRACEFOLD_VISUAL_PROCESS_STATE) + '}')
                if ($stateDocument.records -isnot [array]) { throw 'visual.integration.process_state_not_array' }
                $records = @($stateDocument.records)
                [IO.File]::AppendAllText($env:TRACEFOLD_VISUAL_SUPPORT_LOG,
                    '{"kind":"process","action":"inventory-all"}' + [Environment]::NewLine)
                return @($records | Where-Object { -not $_.Stopped })
            }
            $records = @()
            foreach ($item in @(Get-CimInstance Win32_Process -ErrorAction Stop)) {
'@
    $patch = Replace-VisualIntegrationOnce -Path $processPath -Old $inventoryOld -New $inventoryNew `
        -Label 'process-inventory-all-terminal'
    Add-PatchFact -Path $processPath -Patch $patch
    $inventoryOneOld = @'
        [scriptblock] $Inventory = {
            param($id)
            try {
'@
    $inventoryOneNew = @'
        [scriptblock] $Inventory = {
            param($id)
            if ($env:TRACEFOLD_VISUAL_SYNTHETIC -eq '1') {
                $stateDocument = ConvertFrom-Json -InputObject ('{"records":' + [IO.File]::ReadAllText($env:TRACEFOLD_VISUAL_PROCESS_STATE) + '}')
                if ($stateDocument.records -isnot [array]) { throw 'visual.integration.process_state_not_array' }
                $records = @($stateDocument.records)
                $fact = @{ kind = 'process'; action = 'inventory'; processId = [int] $id }
                [IO.File]::AppendAllText($env:TRACEFOLD_VISUAL_SUPPORT_LOG,
                    ($fact | ConvertTo-Json -Compress) + [Environment]::NewLine)
                return @($records | Where-Object { $_.ProcessId -eq $id -and -not $_.Stopped })
            }
            try {
'@
    $patch = Replace-VisualIntegrationOnce -Path $processPath -Old $inventoryOneOld -New $inventoryOneNew `
        -Label 'process-inventory-one-terminal'
    Add-PatchFact -Path $processPath -Patch $patch
    $stopOld = @'
        [scriptblock] $Stop = {
            param($id)
            $p = [Diagnostics.Process]::GetProcessById($id)
'@
    $stopNew = @'
        [scriptblock] $Stop = {
            param($id)
            if ($env:TRACEFOLD_VISUAL_SYNTHETIC -eq '1') {
                $stateDocument = ConvertFrom-Json -InputObject ('{"records":' + [IO.File]::ReadAllText($env:TRACEFOLD_VISUAL_PROCESS_STATE) + '}')
                if ($stateDocument.records -isnot [array]) { throw 'visual.integration.process_state_not_array' }
                $records = @($stateDocument.records)
                $matches = @($records | Where-Object { $_.ProcessId -eq $id -and -not $_.Stopped })
                if ($matches.Count -ne 1) { throw 'visual.integration.stop_identity_missing' }
                $matches[0].Stopped = $true
                [IO.File]::WriteAllText($env:TRACEFOLD_VISUAL_PROCESS_STATE,
                    (ConvertTo-Json -InputObject $records -Depth 6), [Text.UTF8Encoding]::new($false))
                $fact = @{ kind = 'process'; action = 'stop'; processId = [int] $id }
                [IO.File]::AppendAllText($env:TRACEFOLD_VISUAL_SUPPORT_LOG,
                    ($fact | ConvertTo-Json -Compress) + [Environment]::NewLine)
                return
            }
            $p = [Diagnostics.Process]::GetProcessById($id)
'@
    $patch = Replace-VisualIntegrationOnce -Path $processPath -Old $stopOld -New $stopNew `
        -Label 'process-stop-terminal'
    Add-PatchFact -Path $processPath -Patch $patch

    $scenarioPath = Join-Path $scenarioRoot '70-visual-keyboard-matrix.ps1'
    $scenarioDefaultsOld = @'
    [scriptblock] $LaunchProcess,
    [scriptblock] $DesktopCheck = { Test-UiInteractiveDesktop },
    [scriptblock] $ForegroundCheck = { param($id) Test-UiForegroundProcess -ProcessId $id }
'@
    $scenarioDefaultsNew = @'
    [scriptblock] $LaunchProcess,
    [scriptblock] $DesktopCheck,
    [scriptblock] $ForegroundCheck
'@
    $patch = Replace-VisualIntegrationOnce -Path $scenarioPath -Old $scenarioDefaultsOld -New $scenarioDefaultsNew `
        -Label 'scenario-synthetic-parameter-defaults'
    Add-PatchFact -Path $scenarioPath -Patch $patch

    $scenarioStrictOld = @'
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module
'@
    $scenarioStrictNew = @'
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:TRACEFOLD_VISUAL_SYNTHETIC -eq '1') {
    if ($null -eq $DesktopCheck) { $DesktopCheck = { $true } }
    if ($null -eq $ForegroundCheck) { $ForegroundCheck = { param($id) $true } }
    if ($null -eq $LaunchProcess) {
        $LaunchProcess = {
            param($executable, $childEnvironment)
            $stateDocument = ConvertFrom-Json -InputObject ('{"records":' + [IO.File]::ReadAllText($env:TRACEFOLD_VISUAL_PROCESS_STATE) + '}')
            if ($stateDocument.records -isnot [array]) { throw 'visual.integration.process_state_not_array' }
            $records = @($stateDocument.records)
            if (@($records | Where-Object { -not $_.Stopped }).Count -gt 0) {
                throw 'visual.integration.previous_product_still_live'
            }
            $record = [pscustomobject] @{
                ProcessId = 2147480000 + $records.Count
                ParentProcessId = 0
                StartUtcTicks = [long] 638000000000000000 + $records.Count
                ExecutablePath = $executable
                Stopped = $false
            }
            $records = @($records) + @($record)
            [IO.File]::WriteAllText($env:TRACEFOLD_VISUAL_PROCESS_STATE,
                (ConvertTo-Json -InputObject $records -Depth 6), [Text.UTF8Encoding]::new($false))
            $log = [string] $env:TRACEFOLD_VISUAL_SUPPORT_LOG
            if (-not [string]::IsNullOrWhiteSpace($log)) {
                $fact = [ordered] @{
                    kind = 'product'
                    action = 'launch-synthetic'
                    target = $executable
                    processId = $record.ProcessId
                    startUtcTicks = $record.StartUtcTicks
                    property = ''
                    value = ''
                    elapsedUtc = [DateTimeOffset]::UtcNow.ToString('o')
                }
                [IO.File]::AppendAllText($log, ($fact | ConvertTo-Json -Compress) + [Environment]::NewLine,
                    [Text.UTF8Encoding]::new($false))
            }
            return $record
        }
    }
}

Import-Module
'@
    $patch = Replace-VisualIntegrationOnce -Path $scenarioPath -Old $scenarioStrictOld -New $scenarioStrictNew `
        -Label 'scenario-synthetic-terminal-hooks'
    Add-PatchFact -Path $scenarioPath -Patch $patch

    $scenarioCatchOld = @'
catch {
    $failureCode = $_.Exception.Message
'@
    $scenarioCatchNew = @'
catch {
    if (-not [string]::IsNullOrWhiteSpace([string] $env:TRACEFOLD_VISUAL_FAILURE_LOG)) {
        $failureText = $_.Exception.ToString()
        [IO.File]::AppendAllText([string] $env:TRACEFOLD_VISUAL_FAILURE_LOG,
            $failureText + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    }
    $failureCode = $_.Exception.Message
'@
    $patch = Replace-VisualIntegrationOnce -Path $scenarioPath -Old $scenarioCatchOld -New $scenarioCatchNew `
        -Label 'scenario-diagnostic-failure'
    Add-PatchFact -Path $scenarioPath -Patch $patch

    $finalTree = $tree
    $entryForRun = $entryPath
    $relocation = $null
    if ($TreeKind -eq 'Package') {
        # Reuse the existing OfflineEntry package builder. The custom source root and contract
        # switch keep its production layout, member verification and transfer semantics while
        # binding the exact already-patched Visual tree and real declared assets.
        Import-Module (Join-Path $Root 'scripts/ui-acceptance/TraceFold.UiAcceptance.Runner.psm1') `
            -Force -DisableNameChecking
        . (Join-Path $Root 'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.OfflineEntry.Tests.ps1')
        $packageSource = Join-Path $Base 'p'
        $packageInfo = New-OfflinePackage -Root $packageSource `
            -SupportPayload $Payload.Payload -Scenario 'StartupLayout' `
            -RealScenario @('Visual') -SourceRoot $tree `
            -ContractSource (Join-Path $tree $contractRelative) -FixtureSourceRoot $tree
        $finalTree = Join-Path $Base 'm'
        $isolatedSource = Join-Path $Base 'x'
        $relocation = [ordered] @{
            sourcePackage = $packageSource
            movedPackage = $finalTree
            fixtureSource = $tree
            isolatedFixtureSource = $isolatedSource
            packageBeforeSha256 = Get-VisualIntegrationTreeHash -Root $packageSource
            fixtureBeforeSha256 = Get-VisualIntegrationTreeHash -Root $tree
            packageAfterSha256 = $null
            fixtureAfterSha256 = $null
            sourcePackageAbsent = $false
            fixtureSourceAbsent = $false
        }
        $relocationPath = Join-Path $Base 'relocation.json'
        Write-VisualIntegrationJson -Path $relocationPath -Value $relocation
        Move-VisualIntegrationOwnedTree -Path $packageSource -Destination $finalTree -Boundary $Base
        Move-VisualIntegrationOwnedTree -Path $tree -Destination $isolatedSource -Boundary $Base
        $relocation.packageAfterSha256 = Get-VisualIntegrationTreeHash -Root $finalTree
        $relocation.fixtureAfterSha256 = Get-VisualIntegrationTreeHash -Root $isolatedSource
        $relocation.sourcePackageAbsent = -not (Test-Path -LiteralPath $packageSource)
        $relocation.fixtureSourceAbsent = -not (Test-Path -LiteralPath $tree)
        Write-VisualIntegrationJson -Path $relocationPath -Value $relocation
        if ($relocation.packageBeforeSha256 -cne $relocation.packageAfterSha256 -or
            $relocation.fixtureBeforeSha256 -cne $relocation.fixtureAfterSha256 -or
            -not $relocation.sourcePackageAbsent -or -not $relocation.fixtureSourceAbsent) {
            throw 'visual.integration.relocation_not_verified'
        }
        $entryForRun = Join-Path $finalTree 'scripts/run-stage5cc-ui-acceptance.ps1'
    }

    Write-VisualIntegrationJson -Path $patchManifest -Value ([ordered] @{
        treeKind = $TreeKind
        tree = $finalTree
        patches = $patches.ToArray()
        relocation = $relocation
    })

    return [pscustomobject] @{
        Root = $finalTree
        Entry = $entryForRun
        SupportRoot = $(if ($TreeKind -eq 'Package') {
                Join-Path $finalTree 'runtime/support'
            } else { $supportRoot })
        Contract = Join-Path $finalTree $contractRelative
        DriverArchive = $driverArchive
        PatchManifest = $patchManifest
        Packaged = ($TreeKind -eq 'Package')
        Relocation = $relocation
    }
}

function New-VisualIntegrationFixtureRoot {
    param([Parameter(Mandatory)][string] $Base)
    $root = Join-Path $Base ('tracefold-theme-fixture-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($root)
    [IO.File]::WriteAllText((Join-Path $root 'registry.json'),
        '{"present":false,"kind":"DWord","bytesHex":"","failWrite":false,"failDelete":false}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $root 'broadcast.json'), '{"fail":false,"count":0}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $root 'probe.json'),
        '{"state":"live","identityReadable":true}', [Text.UTF8Encoding]::new($false))
    return $root
}

function Get-VisualIntegrationFixtureSnapshot {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $JournalPath
    )
    $registryPath = Join-Path $Root 'registry.json'
    $broadcastPath = Join-Path $Root 'broadcast.json'
    return [ordered] @{
        registryPath = $registryPath
        registryText = [IO.File]::ReadAllText($registryPath, [Text.UTF8Encoding]::new($false, $true))
        registrySha256 = Get-VisualIntegrationHash -Path $registryPath
        broadcastPath = $broadcastPath
        broadcastText = [IO.File]::ReadAllText($broadcastPath, [Text.UTF8Encoding]::new($false, $true))
        broadcastSha256 = Get-VisualIntegrationHash -Path $broadcastPath
        journalPath = $JournalPath
        journalPresent = [IO.File]::Exists($JournalPath)
        journalSha256 = $(if ([IO.File]::Exists($JournalPath)) {
                Get-VisualIntegrationHash -Path $JournalPath
            } else { $null })
    }
}

function New-VisualGateTestOwnedEntryTree {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Base,
        [Parameter(Mandatory)][string] $SupportExecutable
    )

    $tree = Join-Path $Base 's'
    $bootstrap = Join-Path $tree 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap'
    $bootstrapExecutable = [IO.Path]::GetFullPath((Join-Path $bootstrap 'TraceFold.Media.UiAcceptance.Support.exe'))
    if ($bootstrapExecutable.Length -gt 259) { throw 'visual.integration.entry_support_path_too_long' }
    $scriptsRoot = Join-Path $tree 'scripts'
    $uiRoot = Join-Path $scriptsRoot 'ui-acceptance'
    [void][IO.Directory]::CreateDirectory($uiRoot)
    Copy-VisualIntegrationFile -Source (Join-Path $Root 'scripts/run-stage5cc-ui-acceptance.ps1') `
        -Destination (Join-Path $scriptsRoot 'run-stage5cc-ui-acceptance.ps1')
    foreach ($module in @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner',
            'Workflows', 'Metadata', 'VisualGate')) {
        Copy-VisualIntegrationFile `
            -Source (Join-Path $Root ('scripts/ui-acceptance/TraceFold.UiAcceptance.' + $module + '.psm1')) `
            -Destination (Join-Path $uiRoot ('TraceFold.UiAcceptance.' + $module + '.psm1'))
    }
    # The public source entry reads the pinned driver declaration before its startup gate.
    # Copy that input unchanged even when this case must stop before driver preparation.
    $driverManifestRelative = 'apps/media-desktop/tools/winapp/win-x64/manifest.json'
    Copy-VisualIntegrationFile -Source (Join-Path $Root $driverManifestRelative) `
        -Destination (Join-Path $tree $driverManifestRelative)
    [void][IO.Directory]::CreateDirectory($bootstrap)
    Copy-Item -Path (Join-Path (Split-Path -Parent $SupportExecutable) '*') `
        -Destination $bootstrap -Recurse -Force
    return (Join-Path $scriptsRoot 'run-stage5cc-ui-acceptance.ps1')
}

function Invoke-VisualIntegrationChild {
    param(
        [Parameter(Mandatory)][string] $HostPath,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $WorkingDirectory,
        [Parameter(Mandatory)] $Environment,
        [Parameter(Mandatory)][string] $EvidenceDirectory,
        [int] $TimeoutMs = 900000
    )

    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $HostPath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $quoted = ($Arguments | ForEach-Object {
        $text = [string] $_
        if ($text -eq '' -or $text -match '\s') { '"' + $text + '"' } else { $text }
    }) -join ' '
    $start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + $quoted
    $start.WorkingDirectory = $WorkingDirectory
    foreach ($key in $Environment.Keys) { $start.EnvironmentVariables[[string] $key] = [string] $Environment[$key] }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    $environmentFacts = [ordered] @{}
    foreach ($key in @($Environment.Keys | Sort-Object)) {
        $environmentFacts[[string] $key] = [string] $Environment[$key]
    }
    $invocationPath = Join-Path $EvidenceDirectory 'invocation.json'
    if (Test-Path -LiteralPath $invocationPath) { throw 'visual.integration.invocation_already_exists' }
    Write-VisualIntegrationJson -Path $invocationPath -Value ([ordered] @{
        host = $HostPath; arguments = $Arguments
        completeCommandLine = $start.FileName + ' ' + $start.Arguments
        workingDirectory = $WorkingDirectory; environment = $environmentFacts
    })
    $stdoutPath = Join-Path $EvidenceDirectory 'stdout.txt'
    $stderrPath = Join-Path $EvidenceDirectory 'stderr.txt'
    $stdoutFile = $null
    $stderrFile = $null
    $stdout = $null
    $stderr = $null
    $observation = [ordered] @{
        started = $false; processId = $null; exitObserved = $false; exitCode = $null
        timedOut = $false; stdoutCompleted = $false; stderrCompleted = $false
        stdoutPath = $stdoutPath; stderrPath = $stderrPath
        exception = $null; terminationError = $null; elapsedMs = $null
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        # Stream bytes to disk while the child runs: a timeout or failed assertion must not
        # discard everything buffered by ReadToEndAsync. Unknown observations remain null.
        $stdoutFile = [IO.File]::Open($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $stderrFile = [IO.File]::Open($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        [void] $process.Start()
        $observation.started = $true
        $observation.processId = $process.Id
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
        $process.StandardInput.Close()
        $remaining = [Math]::Max(0, $TimeoutMs - [int] $clock.ElapsedMilliseconds)
        if (-not $process.WaitForExit($remaining)) {
            $observation.timedOut = $true
            throw ('visual.integration.entry_timeout: ' + $TimeoutMs)
        }
        $remaining = [Math]::Max(1, $TimeoutMs - [int] $clock.ElapsedMilliseconds)
        if (-not $stdout.Wait($remaining)) { throw 'visual.integration.stdout_not_drained' }
        if (-not $stderr.Wait($remaining)) { throw 'visual.integration.stderr_not_drained' }
    }
    catch {
        $observation.exception = $_.Exception.ToString()
        throw
    }
    finally {
        if ($observation.started) {
            try {
                if (-not $process.HasExited) {
                    try { $process.Kill($true) } catch { $process.Kill() }
                    [void] $process.WaitForExit(5000)
                }
                $observation.exitObserved = $process.HasExited
                if ($observation.exitObserved) { $observation.exitCode = [int] $process.ExitCode }
            }
            catch { $observation.terminationError = $_.Exception.ToString() }
        }
        foreach ($streamTask in @($stdout, $stderr)) {
            if ($null -ne $streamTask) { try { [void] $streamTask.Wait(5000) } catch { } }
        }
        $observation.stdoutCompleted = $null -ne $stdout -and $stdout.Status -eq 'RanToCompletion'
        $observation.stderrCompleted = $null -ne $stderr -and $stderr.Status -eq 'RanToCompletion'
        if ($null -ne $stdoutFile) { $stdoutFile.Dispose() }
        if ($null -ne $stderrFile) { $stderrFile.Dispose() }
        $clock.Stop()
        $observation.elapsedMs = [long] $clock.ElapsedMilliseconds
        $process.Dispose()
        Write-VisualIntegrationJson -Path (Join-Path $EvidenceDirectory 'child-result.json') -Value $observation
    }
    return [pscustomobject] @{
        ExitCode = $observation.exitCode
        Stdout = [IO.File]::ReadAllText($stdoutPath)
        Stderr = [IO.File]::ReadAllText($stderrPath)
        ElapsedMs = $observation.elapsedMs
        Arguments = $Arguments
        CommandLine = $start.FileName + ' ' + $start.Arguments
        WorkingDirectory = $WorkingDirectory
        HostPath = $HostPath
        Environment = $environmentFacts
    }
}

function Invoke-VisualIntegrationRepresentative {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Base,
        [Parameter(Mandatory)] $Payload,
        [Parameter(Mandatory)][string] $HostPath,
        [ValidateSet('Source', 'Package')][string] $TreeKind,
        [ValidateSet('CurrentOnly', 'Both')][string] $Mode
    )

    # Evidence keeps its stable caller-selected location. Executed trees live in a short,
    # unique sibling below the same TEMP; never shorten the production bootstrap layout.
    $runtimeRoot = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-r-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    if (Test-Path -LiteralPath (Join-Path $Base 'setup.json')) { throw 'visual.integration.evidence_already_exists' }
    if (Test-Path -LiteralPath $runtimeRoot) { throw 'visual.integration.runtime_already_exists' }
    [void][IO.Directory]::CreateDirectory($runtimeRoot)
    Write-VisualIntegrationJson -Path (Join-Path $Base 'setup.json') -Value ([ordered] @{
        treeKind = $TreeKind; mode = $Mode; runtimeRoot = $runtimeRoot; evidenceDirectory = $Base
        host = $HostPath; sourceRoot = $Root; payload = $Payload.Payload
    })
    $fixtureRoot = $null
    $journal = $null
    try {
    $stagedPayload = Join-Path $runtimeRoot 'a'
    Copy-Item -LiteralPath $Payload.Payload -Destination $stagedPayload -Recurse -Force
    $runPayload = [pscustomobject] @{
        Payload = $stagedPayload
        Engine = Join-Path $stagedPayload 'TraceFold.Media.UiAcceptance.Support.exe'
        RealSupport = Join-Path $stagedPayload 'real-support/TraceFold.Media.UiAcceptance.Support.exe'
        PublishLog = [string] $Payload.PublishLog
        PublishExit = [int] $Payload.PublishExit
        PayloadTreeSha256 = Get-VisualIntegrationTreeHash -Root $stagedPayload
    }
    $tree = New-VisualIntegrationTree -Root $Root -Base $runtimeRoot -Payload $runPayload -TreeKind $TreeKind
    $isolatedPayload = Join-Path $runtimeRoot 'z'
    if (Test-Path -LiteralPath $isolatedPayload) { throw 'visual.integration.payload_isolation_target_exists' }
    Move-VisualIntegrationOwnedTree -Path $stagedPayload -Destination $isolatedPayload -Boundary $runtimeRoot
    $fixtureRoot = New-VisualIntegrationFixtureRoot -Base $runtimeRoot
    $journal = Join-Path $fixtureRoot 'theme-recovery.json'
    $fixtureBefore = Get-VisualIntegrationFixtureSnapshot -Root $fixtureRoot -JournalPath $journal
    $product = Join-Path $Base 'product'
    [void][IO.Directory]::CreateDirectory($product)
    $productExe = Join-Path $product 'TraceFold.Media.exe'
    [IO.File]::WriteAllBytes($productExe, [Text.UTF8Encoding]::new($false).GetBytes('controlled product stand-in'))
    $working = Join-Path $Base 'cwd'
    $output = Join-Path $Base 'output'
    $uiState = Join-Path $Base 'engine-state.json'
    $processState = Join-Path $Base 'process-state.json'
    [IO.File]::WriteAllText($processState, '[]', [Text.UTF8Encoding]::new($false))
    $rootSnapshots = Join-Path $runtimeRoot 'r'
    $callLog = Join-Path $Base 'calls.jsonl'
    [void][IO.Directory]::CreateDirectory($working)

    $treeSupportRoot = [string] $tree.SupportRoot
    $engineEndpoint = Join-Path $treeSupportRoot 'TraceFold.Media.UiAcceptance.Support.exe'
    $realSupportEndpoint = Join-Path $treeSupportRoot 'real-support/TraceFold.Media.UiAcceptance.Support.exe'
    if (-not [IO.File]::Exists($engineEndpoint) -or -not [IO.File]::Exists($realSupportEndpoint)) {
        throw 'visual.integration.endpoint_missing'
    }
    $executablePaths = @($HostPath, $engineEndpoint, $realSupportEndpoint,
        (Join-Path $runtimeRoot ('TraceFold-UIA-' + ('0' * 32) + '/support/TraceFold.Media.UiAcceptance.Support.exe')),
        (Join-Path $runtimeRoot ('TraceFold-UIA-' + ('0' * 32) + '/support/real-support/TraceFold.Media.UiAcceptance.Support.exe')))
    $pathFacts = @($executablePaths | ForEach-Object {
        $full = [IO.Path]::GetFullPath($_)
        [ordered] @{ path = $full; length = $full.Length }
    })
    Write-VisualIntegrationJson -Path (Join-Path $Base 'executable-paths.json') -Value $pathFacts
    if (@($pathFacts | Where-Object { $_.length -gt 259 }).Count -gt 0) {
        throw 'visual.integration.executable_path_too_long'
    }
    $childModulePath = if ([IO.Path]::GetFileName($HostPath) -ieq 'powershell.exe') {
        @(
            (Join-Path $env:USERPROFILE 'Documents/WindowsPowerShell/Modules'),
            (Join-Path $env:ProgramFiles 'WindowsPowerShell/Modules'),
            (Join-Path $env:WINDIR 'system32/WindowsPowerShell/v1.0/Modules')
        ) -join ';'
    }
    else { [string] $env:PSModulePath }
    $environment = @{
        TEMP = $runtimeRoot
        TMP = $runtimeRoot
        GIT_OPTIONAL_LOCKS = '0'
        # ProcessStartInfo snapshots the native environment at host startup, while the Codex
        # runtime updates PSModulePath inside pwsh afterwards. Pass the live value explicitly so
        # Windows PowerShell 5.1 can autoload Microsoft.PowerShell.Utility in the child.
        PSModulePath = $childModulePath
        TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE = '1'
        TRACEFOLD_VISUAL_SYNTHETIC = '1'
        TRACEFOLD_VISUAL_UI_STATE = $uiState
        TRACEFOLD_VISUAL_PROCESS_STATE = $processState
        TRACEFOLD_VISUAL_ROOT_SNAPSHOTS = $rootSnapshots
        TRACEFOLD_VISUAL_SUPPORT_LOG = $callLog
        TRACEFOLD_VISUAL_DRIVER = $engineEndpoint
        TRACEFOLD_VISUAL_REAL_SUPPORT = $realSupportEndpoint
        TRACEFOLD_VISUAL_CONTRACT = $tree.Contract
        TRACEFOLD_VISUAL_FIXTURE_ROOT = $fixtureRoot
        TRACEFOLD_VISUAL_FAILURE_LOG = (Join-Path $Base 'scenario-failure.txt')
    }
    $arguments = New-Object System.Collections.Generic.List[string]
    [void] $arguments.Add($tree.Entry)
    if ($tree.Packaged) {
        [void] $arguments.Add('-ToolkitDirectory'); [void] $arguments.Add($tree.Root)
    }
    else {
        [void] $arguments.Add('-WinAppCliArchivePath'); [void] $arguments.Add($tree.DriverArchive)
    }
    foreach ($item in @(
            '-ProductDirectory', $product,
            '-OutputDirectory', $output,
            '-Scenario', 'Visual',
            '-ThemeMatrix', $Mode,
            '-ThemeJournalPath', $journal,
            '-ThemeFixtureRoot', $fixtureRoot,
            '-AllowThemeFixture',
            '-SyntheticObservation')) {
        [void] $arguments.Add([string] $item)
    }

    Write-VisualIntegrationJson -Path (Join-Path $Base 'run-inputs.json') -Value ([ordered] @{
        fixtureBefore = $fixtureBefore
        patchManifest = $tree.PatchManifest
        relocation = $tree.Relocation
        toolkitTreeSha256 = Get-VisualIntegrationTreeHash -Root $tree.Root
        engineEndpoint = $engineEndpoint
        engineEndpointSha256 = Get-VisualIntegrationHash -Path $engineEndpoint
        realSupportEndpoint = $realSupportEndpoint
        realSupportEndpointSha256 = Get-VisualIntegrationHash -Path $realSupportEndpoint
        payloadTreeSha256 = $runPayload.PayloadTreeSha256
        engineBuildLog = $runPayload.PublishLog
        engineBuildExit = $runPayload.PublishExit
    })
    $entryResult = Invoke-VisualIntegrationChild -HostPath $HostPath -Arguments $arguments.ToArray() `
        -WorkingDirectory $working -Environment $environment -EvidenceDirectory $Base
    $summaryPath = Join-Path $output 'summary.json'
    $reportPath = Join-Path $output 'Visual/report.json'
    $screenshotsPath = Join-Path $output 'Visual/screenshots.json'
    $indexPath = Join-Path $output 'Visual/index.html'
    foreach ($required in @($summaryPath, $reportPath, $screenshotsPath, $indexPath)) {
        if (-not [IO.File]::Exists($required)) {
            throw ('visual.integration.report_missing: ' + $required +
                '; exit=' + [string] $entryResult.ExitCode +
                '; stdout=' + [string] $entryResult.Stdout +
                '; stderr=' + [string] $entryResult.Stderr)
        }
    }

    $summary = Read-VisualIntegrationJson -Path $summaryPath
    $report = Read-VisualIntegrationJson -Path $reportPath
    $visual = Read-VisualIntegrationJson -Path $screenshotsPath
    $expectedStatus = if ($Mode -eq 'Both') { 'Passed' } else { 'NotRun' }
    $expectedExit = if ($Mode -eq 'Both') { 0 } else { 1 }
    if ($entryResult.ExitCode -ne $expectedExit) {
        throw ('visual.integration.entry_exit: expected ' + $expectedExit + ', got ' +
            $entryResult.ExitCode + '; stdout=' + $entryResult.Stdout + '; stderr=' + $entryResult.Stderr)
    }
    if ([string] $summary.status -cne $expectedStatus) {
        throw ('visual.integration.summary_status: expected ' + $expectedStatus + ', got ' +
            [string] $summary.status + '; reason=' + [string] $summary.reason)
    }
    if ([string] @($report.scenarios)[0].status -cne $expectedStatus) {
        throw ('visual.integration.report_status: expected ' + $expectedStatus + ', got ' +
            [string] @($report.scenarios)[0].status)
    }
    if ([bool] $visual.synthetic -ne $true) {
        throw 'visual.integration.synthetic_projection: expected true'
    }
    if ([string] $visual.themeMatrix -cne $Mode) {
        throw ('visual.integration.theme_matrix: expected ' + $Mode + ', got ' + [string] $visual.themeMatrix)
    }

    $entries = @($visual.entries)
    if ($entries.Count -eq 0) { throw 'visual.integration.entries_empty' }
    foreach ($entry in $entries) {
        if ([bool] $entry.synthetic -ne $true) { throw 'visual.integration.entry_synthetic: expected true' }
        if ([string] $entry.status -cne 'Passed' -and -not ($Mode -eq 'CurrentOnly' -and
                [string] $entry.state -ceq 'theme-mode-not-run')) {
            throw ('visual.integration.entry_status: ' + [string] $entry.state + '=' + [string] $entry.status)
        }
        foreach ($field in @('image', 'uia')) {
            $link = [string] $entry.PSObject.Properties[$field].Value
            if ([string]::IsNullOrWhiteSpace($link)) {
                if ([string] $entry.status -ceq 'Passed') {
                    throw ('visual.integration.relative_link_missing: ' + $field)
                }
                continue
            }
            if ([IO.Path]::IsPathRooted($link) -or
                $link -match '(^|[\\/])\.\.([\\/]|$)') {
                throw ('visual.integration.relative_link: ' + $field)
            }
            if (-not [IO.File]::Exists((Join-Path (Join-Path $output 'Visual') $link))) {
                throw ('visual.integration.link_target_missing: ' + $link)
            }
        }
    }
    $notRunEntries = @($entries | Where-Object { [string] $_.status -ceq 'NotRun' })
    if ($Mode -eq 'CurrentOnly') {
        if ($notRunEntries.Count -ne 1 -or [string] $notRunEntries[0].state -cne 'theme-mode-not-run') {
            throw 'visual.integration.current_only_not_run'
        }
        if (@($visual.themes).Count -ne 1) { throw 'visual.integration.current_only_theme_count' }
    }
    else {
        if ($notRunEntries.Count -ne 0) { throw 'visual.integration.both_has_not_run' }
        if (@($visual.themes).Count -ne 2) { throw 'visual.integration.both_theme_count' }
        foreach ($theme in @($visual.themes)) {
            if ([string] $theme.adapters -cne 'fixture') { throw 'visual.integration.theme_adapter' }
            if ([bool] $theme.applied -ne $true) { throw 'visual.integration.theme_applied' }
            if ($null -eq $theme.restore) { throw 'visual.integration.theme_restore_missing' }
            if ([bool] $theme.restore.ok -ne $true) { throw 'visual.integration.theme_restore_failed' }
            if ($null -eq $theme.readback -or [bool] $theme.readback.ok -ne $true) {
                throw 'visual.integration.theme_readback_failed'
            }
        }
    }

    if ([IO.File]::Exists($journal)) { throw 'visual.integration.journal_not_closed' }
    $fixtureAfter = Get-VisualIntegrationFixtureSnapshot -Root $fixtureRoot -JournalPath $journal
    $registry = Read-VisualIntegrationJson -Path (Join-Path $fixtureRoot 'registry.json')
    $broadcast = Read-VisualIntegrationJson -Path (Join-Path $fixtureRoot 'broadcast.json')
    if ([bool] $registry.present -ne $false) { throw 'visual.integration.registry_not_restored' }
    # Each Both round broadcasts once after apply and once after restore.
    $expectedBroadcasts = if ($Mode -eq 'Both') { 4 } else { 0 }
    if ([int] $broadcast.count -ne $expectedBroadcasts) {
        throw ('visual.integration.broadcast_count: expected ' + $expectedBroadcasts)
    }
    if ($Mode -eq 'CurrentOnly') {
        if ([string] $fixtureBefore.registrySha256 -cne [string] $fixtureAfter.registrySha256 -or
            [string] $fixtureBefore.broadcastSha256 -cne [string] $fixtureAfter.broadcastSha256) {
            throw 'visual.integration.current_only_fixture_changed'
        }
    }
    else {
        $beforeRegistry = $fixtureBefore.registryText | ConvertFrom-Json
        if ([bool] $beforeRegistry.present -ne [bool] $registry.present) {
            throw 'visual.integration.registry_restore_differs_from_before'
        }
    }
    $calls = @()
    if ([IO.File]::Exists($callLog)) {
        $calls = @(Get-Content -LiteralPath $callLog | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json })
    }
    if (@($calls | Where-Object { [string] $_.kind -ceq 'ui' -and [string] $_.action -ceq 'screenshot' }).Count -eq 0) {
        throw 'visual.integration.screenshot_dispatch_missing'
    }
    if (@($calls | Where-Object { [string] $_.kind -ceq 'product' -and [string] $_.action -ceq 'launch-synthetic' }).Count -lt 1) {
        throw 'visual.integration.synthetic_product_dispatch_missing'
    }
    if (@($calls | Where-Object { [string] $_.kind -ceq 'root' -and [string] $_.action -ceq 'validate' }).Count -eq 0) {
        throw 'visual.integration.root_validate_missing'
    }
    if ($Mode -eq 'Both') {
        $themeCalls = @($calls | Where-Object { [string] $_.kind -ceq 'theme' } | ForEach-Object {
            [string] $_.action
        })
        if (($themeCalls -join ',') -cne 'apply,restore,inspect,apply,restore,inspect') {
            throw ('visual.integration.theme_call_sequence: ' + ($themeCalls -join ','))
        }
    }
    elseif (@($calls | Where-Object { [string] $_.kind -ceq 'theme' }).Count -ne 0) {
        throw 'visual.integration.current_only_made_theme_write_or_restore'
    }

    $expectedLaunches = if ($Mode -eq 'Both') { 2 } else { 1 }
    $launches = @($calls | Where-Object { $_.kind -ceq 'product' -and $_.action -ceq 'launch-synthetic' })
    $stops = @($calls | Where-Object { $_.kind -ceq 'process' -and $_.action -ceq 'stop' })
    $inventories = @($calls | Where-Object { $_.kind -ceq 'process' -and $_.action -ceq 'inventory-all' })
    $processDocument = ConvertFrom-Json -InputObject ('{"records":' + [IO.File]::ReadAllText($processState) + '}')
    if ($processDocument.records -isnot [array]) { throw 'visual.integration.process_state_not_array' }
    $processes = @($processDocument.records)
    if ($launches.Count -ne $expectedLaunches -or $stops.Count -ne $expectedLaunches -or
        $inventories.Count -ne $expectedLaunches -or $processes.Count -ne $expectedLaunches -or
        @($processes | Where-Object { -not $_.Stopped }).Count -ne 0) {
        throw 'visual.integration.process_lifecycle_incomplete'
    }
    $snapshots = @(Get-ChildItem -LiteralPath $rootSnapshots -Directory)
    if ($snapshots.Count -ne 1) { throw 'visual.integration.owned_root_snapshot_missing' }
    $ledgerSnapshot = Join-Path $snapshots[0].FullName 'owned-processes.txt'
    $ledgerLines = [IO.File]::ReadAllLines($ledgerSnapshot)
    if ($ledgerLines.Count -ne $expectedLaunches) { throw 'visual.integration.ledger_count' }
    foreach ($record in $processes) {
        $expectedLine = '{0}|{1}|{2}' -f $record.ProcessId, $record.StartUtcTicks, $record.ExecutablePath
        if (@($ledgerLines | Where-Object { $_ -ceq $expectedLine }).Count -ne 1 -or
            @($launches | Where-Object { $_.processId -eq $record.ProcessId -and
                $_.startUtcTicks -eq $record.StartUtcTicks -and $_.target -ceq $record.ExecutablePath }).Count -ne 1 -or
            @($stops | Where-Object { $_.processId -eq $record.ProcessId }).Count -ne 1) {
            throw 'visual.integration.ledger_identity_mismatch'
        }
    }
    $lifecycle = @($calls | Where-Object {
        ($_.kind -ceq 'product' -and $_.action -ceq 'launch-synthetic') -or
        ($_.kind -ceq 'process' -and $_.action -ceq 'stop')
    } | ForEach-Object { [string] $_.action }) -join ','
    $expectedLifecycle = (@(1..$expectedLaunches | ForEach-Object { 'launch-synthetic,stop' }) -join ',')
    if ($lifecycle -cne $expectedLifecycle) { throw 'visual.integration.product_round_overlap' }
    if ($tree.Packaged -and ((Test-Path -LiteralPath $tree.Relocation.sourcePackage) -or
            (Test-Path -LiteralPath $tree.Relocation.fixtureSource) -or
            (Get-VisualIntegrationTreeHash -Root $tree.Root) -cne $tree.Relocation.packageBeforeSha256)) {
        throw 'visual.integration.package_isolation_lost'
    }

    $facts = [ordered] @{
        task = 'T13-CODEX-RESIDUAL-20261023-01'
        treeKind = $TreeKind
        runtimeRoot = $runtimeRoot
        relocation = $tree.Relocation
        mode = $Mode
        host = $HostPath
        hostVersion = (& $HostPath -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()')
        completeCommandLine = $entryResult.CommandLine
        childEnvironment = $entryResult.Environment
        entry = $tree.Entry
        workingDirectory = $working
        arguments = $arguments.ToArray()
        engineSource = (Join-Path $Root 'scripts/ui-acceptance/tests/fixtures/visual-integration/Program.cs')
        engineSourceSha256 = Get-VisualIntegrationHash -Path (Join-Path $Root 'scripts/ui-acceptance/tests/fixtures/visual-integration/Program.cs')
        engineProject = (Join-Path $Root 'scripts/ui-acceptance/tests/fixtures/visual-integration/VisualFixture.csproj')
        engineProjectSha256 = Get-VisualIntegrationHash -Path (Join-Path $Root 'scripts/ui-acceptance/tests/fixtures/visual-integration/VisualFixture.csproj')
        builtPayloadTreeSha256 = [string] $runPayload.PayloadTreeSha256
        stagedPayloadIsolated = (-not [IO.Directory]::Exists($stagedPayload))
        isolatedPayloadPath = $isolatedPayload
        engineEndpoint = $engineEndpoint
        engineEndpointSha256 = Get-VisualIntegrationHash -Path $engineEndpoint
        realSupportEndpoint = $realSupportEndpoint
        realSupportEndpointSha256 = Get-VisualIntegrationHash -Path $realSupportEndpoint
        engineBuildLog = [string] $runPayload.PublishLog
        engineBuildExit = [int] $runPayload.PublishExit
        toolkitTreeSha256 = Get-VisualIntegrationTreeHash -Root $tree.Root
        entryExitCode = $entryResult.ExitCode
        summaryStatus = [string] $summary.status
        reportStatus = [string] @($report.scenarios)[0].status
        synthetic = [bool] $visual.synthetic
        themeMatrix = [string] $visual.themeMatrix
        entryCount = $entries.Count
        notRunCount = $notRunEntries.Count
        themeCount = @($visual.themes).Count
        fixtureBefore = $fixtureBefore
        fixtureAfter = $fixtureAfter
        registryAfter = $registry
        broadcastAfter = $broadcast
        journalAbsent = (-not [IO.File]::Exists($journal))
        callLog = $callLog
        screenshots = $screenshotsPath
        index = $indexPath
        patchManifest = $tree.PatchManifest
        rootSnapshots = $rootSnapshots
        processState = $processState
        ledgerSnapshot = $ledgerSnapshot
        processLaunches = $launches.Count
        processStops = $stops.Count
        processInventories = $inventories.Count
        stdout = $entryResult.Stdout
        stderr = $entryResult.Stderr
        elapsedMs = $entryResult.ElapsedMs
    }
    $factsPath = Join-Path $Base 'facts.json'
    Write-VisualIntegrationJson -Path $factsPath -Value $facts
    $result = [pscustomobject] $facts
    $result | Add-Member -NotePropertyName FactsPath -NotePropertyValue $factsPath
    $result | Add-Member -NotePropertyName TreeRoot -NotePropertyValue $tree.Root
    $result | Add-Member -NotePropertyName Base -NotePropertyValue $Base
    return $result
    }
    catch {
        $firstFailure = $_
        Write-VisualIntegrationFirstFailure -Directory $Base -Failure $firstFailure
        if ($null -ne $fixtureRoot -and $null -ne $journal) {
            try {
                Write-VisualIntegrationJson -Path (Join-Path $Base 'fixture-after-failure.json') `
                    -Value (Get-VisualIntegrationFixtureSnapshot -Root $fixtureRoot -JournalPath $journal)
            }
            catch {
                Write-Warning ('Failure fixture snapshot unavailable; retained original files at ' + $fixtureRoot)
            }
        }
        throw $firstFailure
    }
}
