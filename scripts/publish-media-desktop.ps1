# TraceFold - publish the desktop product, and for Stage 5C-C the offline acceptance toolkit.
#
# Stage 5C-C produces two separate artifacts from the same tested source:
#
#   dist/TraceFold-MediaDesktop-Stage5C-C-win-x64/                  (+ .zip)  the product
#   dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64/     (+ .zip)  the offline toolkit
#
# The product carries no acceptance code, support payload, driver, fixtures or template. The
# toolkit carries the complete support payload, the entry, its modules, scenarios and report
# template, the pinned driver extracted from the local v0.5.0 archive, the licenses, the contract
# and every fixture it declares - and a toolkit.json member table generated from the staged bytes,
# so the entry verifies exactly what this script wrote.
#
# Both artifacts are built in staging, both ZIPs are created there, the structural smokes run
# there, and only then are the four final targets replaced together. Any failure restores the
# previous pair.

[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('4A','4B','5A','5B','5C','5C-B','5C-C')][string] $StageCheckpoint,
    [Parameter(Mandatory)][string] $FfmpegSourceRoot,
    [string] $NugetPackageSource,
    [string] $WinAppCliArchivePath,
    # Verification hook: makes one promotion step fail on purpose so the rollback can be proven.
    # 'None' in every real publish.
    [ValidateSet(
        'None',
        'BeforePromotion',
        'AfterBackupProductDirectory',
        'AfterProductDirectory',
        'AfterProductArchive',
        'AfterToolkitDirectory',
        'AfterToolkitArchive')]
    [string] $FaultPoint = 'None',
    # Verification hook: makes the restoration of one previous artifact fail, so the case where a
    # rollback cannot finish is provable. The backup must then survive. 'None' in every real publish.
    [ValidateSet('None', 'ProductDirectory', 'ProductArchive', 'ToolkitDirectory', 'ToolkitArchive')]
    [string] $RecoveryFaultPoint = 'None',
    # Controlled execution dependency for the product gate only: the process the product smoke
    # starts instead of the real packaged product. Tests pass it so the complete gate - all four
    # probe requests and their exit-code rules - runs without opening a window. A real publish
    # must not pass it, and it cannot skip the gate: without it the smoke starts the product.
    [string] $ProductSmokeLauncher
)

$ErrorActionPreference = 'Stop'
# Compress-Archive writes a progress bar per file on Windows PowerShell; with two packages that
# is megabytes of console noise around the real output, and the caller only reads the summary.
$ProgressPreference = 'SilentlyContinue'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
$isStageCC = $StageCheckpoint -eq '5C-C'
$packageName = "TraceFold-MediaDesktop-Stage$StageCheckpoint-win-x64"
$archiveName = "$packageName.zip"
$packageDir = [IO.Path]::GetFullPath((Join-Path $distRoot $packageName))
$zipPath = [IO.Path]::GetFullPath((Join-Path $distRoot $archiveName))
$toolkitName = "TraceFold-MediaDesktop-Stage$StageCheckpoint-UiAcceptance-win-x64"
$toolkitArchiveName = "$toolkitName.zip"
$toolkitDir = [IO.Path]::GetFullPath((Join-Path $distRoot $toolkitName))
$toolkitZipPath = [IO.Path]::GetFullPath((Join-Path $distRoot $toolkitArchiveName))
$stageSlug = $StageCheckpoint.ToLowerInvariant()
$versionStageSlug = $StageCheckpoint.Replace('-', '').ToLowerInvariant()
$stagingName = ".tracefold-stage$stageSlug-publish-$([Guid]::NewGuid().ToString('N'))"
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $distRoot $stagingName))
# Keep internal directory names short; final names and archive contents remain unchanged.
$stagingPackage = [IO.Path]::GetFullPath((Join-Path $stagingRoot 'product'))
$stagingZip = [IO.Path]::GetFullPath((Join-Path $stagingRoot $archiveName))
$stagingToolkit = [IO.Path]::GetFullPath((Join-Path $stagingRoot 'toolkit'))
$stagingToolkitZip = [IO.Path]::GetFullPath((Join-Path $stagingRoot $toolkitArchiveName))
$backupName = ".tracefold-stage$stageSlug-backup-$([Guid]::NewGuid().ToString('N'))"
$backupRoot = [IO.Path]::GetFullPath((Join-Path $distRoot $backupName))
$backupPackage = [IO.Path]::GetFullPath((Join-Path $backupRoot $packageName))
$backupZip = [IO.Path]::GetFullPath((Join-Path $backupRoot $archiveName))
$backupToolkit = [IO.Path]::GetFullPath((Join-Path $backupRoot $toolkitName))
$backupToolkitZip = [IO.Path]::GetFullPath((Join-Path $backupRoot $toolkitArchiveName))

function Get-TrimmedFullPath {
    param([Parameter(Mandatory)][string] $Path)
    $separators = [char[]]@(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    return [IO.Path]::GetFullPath($Path).TrimEnd($separators)
}

function Test-FullyQualifiedPath {
    param([Parameter(Mandatory)][string] $Path)
    if (-not [IO.Path]::IsPathRooted($Path)) {
        return $false
    }
    $root = [IO.Path]::GetPathRoot($Path)
    return -not [string]::IsNullOrWhiteSpace($root) -and
        $root.Length -gt 1 -and
        ($root.EndsWith([string][IO.Path]::DirectorySeparatorChar) -or
         $root.EndsWith([string][IO.Path]::AltDirectorySeparatorChar))
}

function Read-JsonDocument {
    # Windows PowerShell decodes a BOM-less UTF-8 file as ANSI, which corrupts every non-ASCII
    # string before ConvertFrom-Json sees it. ReadAllText detects the BOM and defaults to UTF-8.
    param([Parameter(Mandatory)][string] $Path)
    return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json)
}

function Assert-DirectChild {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Parent,
        [Parameter(Mandatory)][string] $ExpectedName
    )
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = Get-TrimmedFullPath -Path $Parent
    $actualParent = Get-TrimmedFullPath -Path ([IO.Path]::GetDirectoryName($fullPath))
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualParent, $fullParent) -or
        -not [StringComparer]::Ordinal.Equals(
            [IO.Path]::GetFileName($fullPath),
            $ExpectedName)) {
        throw "Unsafe direct-child path: $fullPath"
    }
}

function Assert-NoReparseTree {
    param([Parameter(Mandatory)][string] $Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return
    }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing a reparse-point path: $Path"
    }
    if (-not $item.PSIsContainer) {
        return
    }

    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($item.FullName)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $attributes = [IO.File]::GetAttributes($entry)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a reparse point below a cleanup root: $entry"
            }
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                $pending.Push($entry)
            }
        }
    }
}

function Remove-SafeDirectChild {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Parent,
        [Parameter(Mandatory)][string] $ExpectedName
    )
    Assert-DirectChild -Path $Path -Parent $Parent -ExpectedName $ExpectedName
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return
    }
    Assert-NoReparseTree -Path $Path
    if ($item.PSIsContainer) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    else {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $DotNetArguments)
    & dotnet @DotNetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE"
    }
}

function Assert-SourceFile {
    param([Parameter(Mandatory)][string] $Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item -or $item.PSIsContainer) {
        throw "Required FFmpeg distribution file is missing: $Path"
    }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "FFmpeg distribution file cannot be a reparse point: $Path"
    }
    return $item
}

function Assert-MatchingFile {
    param(
        [Parameter(Mandatory)][string] $ActualPath,
        [Parameter(Mandatory)][string] $ExpectedPath
    )
    $actual = Get-Item -LiteralPath $ActualPath -Force
    $expected = Get-Item -LiteralPath $ExpectedPath -Force
    if ($actual.Length -ne $expected.Length) {
        throw "Published third-party file size changed: $ActualPath"
    }
    $actualHash = (Get-FileHash -LiteralPath $ActualPath -Algorithm SHA256).Hash
    $expectedHash = (Get-FileHash -LiteralPath $ExpectedPath -Algorithm SHA256).Hash
    if (-not [StringComparer]::Ordinal.Equals($actualHash, $expectedHash)) {
        throw "Published third-party file hash changed: $ActualPath"
    }
}

function Assert-RequiredPackageInput {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)] $Required
    )
    $missing = New-Object System.Collections.ArrayList
    foreach ($entry in $Required) {
        $full = Join-Path $RepoRoot ($entry.Path -replace '/', '\')
        if (-not [IO.File]::Exists($full)) {
            [void] $missing.Add($entry.Label)
            continue
        }
        if ((Get-Item -LiteralPath $full).Length -le 0) {
            [void] $missing.Add($entry.Label + ' (empty)')
        }
    }
    if ($missing.Count -gt 0) {
        # The release this checkpoint describes is not assembled yet. Refusing here keeps a
        # half-populated package out of dist instead of shipping something that silently differs
        # from the documented release.
        throw ('stage5c_c_not_ready: missing ' + ($missing -join ', '))
    }
}

function Read-WinAppArchive {
    param(
        [Parameter(Mandatory)][string] $ArchivePath,
        [Parameter(Mandatory)] $Manifest
    )
    $item = Get-Item -LiteralPath $ArchivePath -Force -ErrorAction SilentlyContinue
    if ($null -eq $item -or $item.PSIsContainer) {
        throw "Pinned driver archive is missing: $ArchivePath"
    }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Pinned driver archive cannot be a reparse point: $ArchivePath"
    }
    if (-not [StringComparer]::Ordinal.Equals($item.Name, [string] $Manifest.archiveName)) {
        throw "Pinned driver archive has the wrong file name: $($item.Name)"
    }
    if ($item.Length -ne [long] $Manifest.archiveSize) {
        throw "Pinned driver archive size mismatch: $($item.Length)"
    }
    $hash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    if (-not [StringComparer]::Ordinal.Equals($hash, [string] $Manifest.sha256)) {
        throw 'Pinned driver archive SHA-256 mismatch.'
    }

    try { $null = Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
    try { $null = Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }
    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $executables = New-Object System.Collections.ArrayList
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $name = [string] $entry.FullName
            if ([string]::IsNullOrWhiteSpace($name)) { continue }
            if ([IO.Path]::IsPathRooted($name)) { throw "Driver archive contains a rooted entry: $name" }
            $segments = @($name -split '[\\/]')
            foreach ($segment in $segments) {
                if ($segment -ceq '..') { throw "Driver archive contains a traversal entry: $name" }
                if ([IO.Path]::GetFileName($segment) -ne $segment) { throw "Driver archive contains an invalid entry: $name" }
                foreach ($character in $segment.ToCharArray()) {
                    if ([int] [char] $character -lt 32) { throw "Driver archive contains a control character: $name" }
                }
            }
            if (-not $names.Add($name)) { throw "Driver archive contains a duplicate entry: $name" }
            if ($name.ToLowerInvariant().EndsWith('.exe')) { [void] $executables.Add($name) }
        }
    }
    finally {
        $archive.Dispose()
    }
    if ($executables.Count -ne 1 -or -not [StringComparer]::OrdinalIgnoreCase.Equals([string] $executables[0], 'winapp.exe')) {
        throw ('Driver archive must carry exactly one executable, winapp.exe; found: ' + ($executables -join ', '))
    }
    return [pscustomobject] @{
        Path        = [IO.Path]::GetFullPath($ArchivePath)
        Name        = $item.Name
        Bytes       = [long] $item.Length
        Sha256      = $hash
        EntryCount  = $names.Count
    }
}

$script:toolkitMembers = New-Object System.Collections.Generic.List[object]

function Get-ToolkitRelativePath {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Path
    )
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    return (([IO.Path]::GetFullPath($Path).Substring($rootFull.Length).TrimStart([IO.Path]::DirectorySeparatorChar)) -replace '\\', '/')
}

function Add-ToolkitMember {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $SourceLabel
    )
    $relative = Get-ToolkitRelativePath -Root $stagingToolkit -Path $Path
    $item = Get-Item -LiteralPath $Path -Force
    $script:toolkitMembers.Add([pscustomobject] @{
        path   = $relative
        source = $SourceLabel
        bytes  = [long] $item.Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    })
    return $relative
}

function Copy-ToolkitFile {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $DestinationRelative,
        [Parameter(Mandatory)][string] $SourceLabel
    )
    $destination = Join-Path $stagingToolkit ($DestinationRelative -replace '/', '\')
    $directory = [IO.Path]::GetDirectoryName($destination)
    if (-not [IO.Directory]::Exists($directory)) {
        [void][IO.Directory]::CreateDirectory($directory)
    }
    Copy-Item -LiteralPath $Source -Destination $destination -Force
    $null = Add-ToolkitMember -Path $destination -SourceLabel $SourceLabel
}

function Add-ToolkitTree {
    param(
        [Parameter(Mandatory)][string] $SourceDirectory,
        [Parameter(Mandatory)][string] $DestinationRelative,
        [Parameter(Mandatory)][string] $SourceLabel
    )
    $sourceFull = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    foreach ($file in @(Get-ChildItem -LiteralPath $sourceFull -Recurse -File -Force | Sort-Object FullName)) {
        $relative = ($file.FullName.Substring($sourceFull.Length).TrimStart([IO.Path]::DirectorySeparatorChar)) -replace '\\', '/'
        Copy-ToolkitFile -Source $file.FullName -DestinationRelative ($DestinationRelative + '/' + $relative) -SourceLabel $SourceLabel
    }
}

function Register-ToolkitTree {
    param(
        [Parameter(Mandatory)][string] $Directory,
        [Parameter(Mandatory)][string] $SourceLabel
    )
    $directoryFull = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    foreach ($file in @(Get-ChildItem -LiteralPath $directoryFull -Recurse -File -Force | Sort-Object FullName)) {
        $null = Add-ToolkitMember -Path $file.FullName -SourceLabel $SourceLabel
    }
}

function Assert-ToolkitCoverage {
    param([Parameter(Mandatory)][string] $Root)
    $index = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($member in $script:toolkitMembers) { [void] $index.Add([string] $member.path) }

    $required = New-Object System.Collections.ArrayList
    [void] $required.Add('scripts/run-stage5cc-ui-acceptance.ps1')
    [void] $required.Add('Run-Stage5CC-Acceptance.cmd')
    [void] $required.Add('scripts/Run-Stage5CC-Acceptance.ps1')
    foreach ($moduleName in @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner', 'Workflows', 'Metadata', 'VisualGate', 'Aggregate')) {
        [void] $required.Add('scripts/ui-acceptance/TraceFold.UiAcceptance.' + $moduleName + '.psm1')
    }
    [void] $required.Add('scripts/ui-acceptance/report-template/index.html')
    [void] $required.Add('scripts/ui-acceptance/README.txt')
    [void] $required.Add('apps/media-desktop/tools/ffmpeg/win-x64/manifest.json')
    foreach ($scenarioFile in @($script:registryScenarioFiles)) {
        [void] $required.Add('scripts/ui-acceptance/scenarios/' + [string] $scenarioFile)
    }
    foreach ($relative in $required) {
        if (-not $index.Contains([string] $relative)) {
            throw ('stage5c_c_toolkit_incomplete: ' + $relative)
        }
    }
    if ($index.Contains('scripts/Run-Stage5CC-Acceptance.cmd')) {
        throw 'stage5c_c_toolkit_layout: the launcher must be at the package root, not under scripts.'
    }

    foreach ($tree in @('runtime/support', 'runtime/driver', 'scripts/ui-acceptance/report-template')) {
        $full = Join-Path $Root ($tree -replace '/', '\')
        if (-not [IO.Directory]::Exists($full)) { throw ('stage5c_c_toolkit_incomplete: ' + $tree) }
        foreach ($file in @(Get-ChildItem -LiteralPath $full -Recurse -File -Force)) {
            $relative = Get-ToolkitRelativePath -Root $Root -Path $file.FullName
            if (-not $index.Contains($relative)) {
                throw ('stage5c_c_toolkit_member_unlisted: ' + $relative)
            }
        }
    }
    foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $Root 'scripts/ui-acceptance') -File -Filter '*.psm1' -Force)) {
        $relative = Get-ToolkitRelativePath -Root $Root -Path $file.FullName
        if (-not $index.Contains($relative)) { throw ('stage5c_c_toolkit_member_unlisted: ' + $relative) }
    }
}

function Invoke-SmokeChild {
    param(
        [Parameter(Mandatory)][string] $ScriptPath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Arguments,
        [Parameter(Mandatory)][string] $Label
    )
    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') {
        Join-Path $PSHOME 'pwsh.exe'
    }
    else {
        Join-Path $PSHOME 'powershell.exe'
    }
    $previousErrorAction = $ErrorActionPreference
    try {
        # Windows PowerShell escalates a native writer's stderr to a terminating error while the
        # preference is Stop. The smoke's own message is the evidence, so the call runs with
        # Continue and the exit code is read immediately.
        $ErrorActionPreference = 'Continue'
        $output = (& $hostExe -NoProfile -File $ScriptPath @Arguments 2>&1 | Out-String)
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($code -ne 0) {
        throw ($Label + ' failed with exit ' + $code + ':' + [Environment]::NewLine + $output.Trim())
    }
    return $output
}

Assert-DirectChild -Path $packageDir -Parent $distRoot -ExpectedName $packageName
Assert-DirectChild -Path $zipPath -Parent $distRoot -ExpectedName $archiveName
Assert-DirectChild -Path $stagingRoot -Parent $distRoot -ExpectedName $stagingName
Assert-DirectChild -Path $backupRoot -Parent $distRoot -ExpectedName $backupName
if ($isStageCC) {
    Assert-DirectChild -Path $toolkitDir -Parent $distRoot -ExpectedName $toolkitName
    Assert-DirectChild -Path $toolkitZipPath -Parent $distRoot -ExpectedName $toolkitArchiveName
}
elseif ($FaultPoint -in @('AfterToolkitDirectory', 'AfterToolkitArchive') -or
    $RecoveryFaultPoint -in @('ToolkitDirectory', 'ToolkitArchive') -or
    -not [string]::IsNullOrWhiteSpace($ProductSmokeLauncher)) {
    throw 'The toolkit fault points and the product smoke launcher are defined for Stage 5C-C only.'
}
if ($RecoveryFaultPoint -cne 'None' -and $FaultPoint -ceq 'None') {
    throw 'A recovery fault point needs a promotion fault point to recover from.'
}
if (-not [string]::IsNullOrWhiteSpace($ProductSmokeLauncher)) {
    if (-not (Test-FullyQualifiedPath -Path $ProductSmokeLauncher)) {
        throw 'ProductSmokeLauncher must be a fully qualified path.'
    }
    $ProductSmokeLauncher = [IO.Path]::GetFullPath($ProductSmokeLauncher)
    if (-not [IO.File]::Exists($ProductSmokeLauncher)) {
        throw 'ProductSmokeLauncher does not exist.'
    }
}

$solution = Join-Path $repoRoot 'apps\media-desktop\TraceFold.MediaDesktop.slnx'
$project = Join-Path $repoRoot 'apps\media-desktop\src\TraceFold.Media.Desktop\TraceFold.Media.Desktop.csproj'
$supportProject = Join-Path $repoRoot 'apps\media-desktop\tools\TraceFold.Media.UiAcceptance.Support\TraceFold.Media.UiAcceptance.Support.csproj'
if ([string]::IsNullOrWhiteSpace($NugetPackageSource)) {
    $NugetPackageSource = Join-Path $repoRoot 'apps\media-desktop\.packages'
}
if (-not (Test-FullyQualifiedPath -Path $NugetPackageSource)) {
    throw 'NugetPackageSource must be a fully qualified path when supplied.'
}
$localPackageSource = [IO.Path]::GetFullPath($NugetPackageSource)
$toolManifestPath = Join-Path $repoRoot 'apps\media-desktop\tools\ffmpeg\win-x64\manifest.json'
$toolNoticePath = Join-Path $repoRoot 'apps\media-desktop\tools\ffmpeg\win-x64\THIRD-PARTY-NOTICES.txt'
if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Solution is missing: $solution"
}
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Desktop project is missing: $project"
}
if ($isStageCC -and -not (Test-Path -LiteralPath $supportProject -PathType Leaf)) {
    throw "Acceptance support project is missing: $supportProject"
}
if (-not (Test-Path -LiteralPath $localPackageSource -PathType Container)) {
    throw "Local package source is missing: $localPackageSource"
}
Assert-NoReparseTree -Path $localPackageSource

# --- Stage 5C-C inputs ------------------------------------------------------------------------
#
# Everything the release is made of is verified before dist is touched: the driver archive against
# its checked-in manifest, the source files the toolkit owes, the contract, and every fixture the
# contract declares. A missing piece is reported by name; nothing is substituted or skipped.
$driverArchive = $null
$driverManifest = $null
$contractPath = Join-Path $repoRoot 'apps\media-desktop\tools\ui-acceptance-contract\stage5cc-uia-contract.json'
$contract = $null
$contractFixtures = @()
if ($isStageCC) {
    if ([string]::IsNullOrWhiteSpace($WinAppCliArchivePath)) {
        throw 'Stage 5C-C requires -WinAppCliArchivePath: the pinned driver ships in the toolkit.'
    }
    if (-not (Test-FullyQualifiedPath -Path $WinAppCliArchivePath)) {
        throw 'WinAppCliArchivePath must be a fully qualified path.'
    }
    $driverManifestPath = Join-Path $repoRoot 'apps\media-desktop\tools\winapp\win-x64\manifest.json'
    if (-not [IO.File]::Exists($driverManifestPath)) {
        throw 'The pinned driver manifest is missing.'
    }
    $driverManifest = Read-JsonDocument -Path $driverManifestPath
    $driverArchive = Read-WinAppArchive -ArchivePath $WinAppCliArchivePath -Manifest $driverManifest

    # The registry is the only source of scenario names and entry files, so the producer asks it
    # instead of treating whatever happens to exist as the closure. A missing entry is refused by
    # name here, before dist exists, rather than discovered by the consumer after promotion.
    $runnerModulePath = Join-Path $repoRoot 'scripts\ui-acceptance\TraceFold.UiAcceptance.Runner.psm1'
    if (-not [IO.File]::Exists($runnerModulePath)) {
        throw 'stage5c_c_not_ready: missing acceptance-runner'
    }
    Import-Module -Name $runnerModulePath -Force -DisableNameChecking
    $registryScenarios = @(
        foreach ($scenarioName in @(Get-UiAcceptanceScenarioName)) {
            [pscustomobject] @{
                Name = [string] $scenarioName
                File = [string] (Get-UiAcceptanceScenarioFile -Name $scenarioName)
            }
        })
    $script:registryScenarioFiles = @($registryScenarios | ForEach-Object { $_.File })

    $requiredInputs = New-Object System.Collections.ArrayList
    foreach ($entry in @(
            [pscustomobject]@{ Path = 'scripts/Run-Stage5CC-Acceptance.cmd'; Label = 'launcher' },
            [pscustomobject]@{ Path = 'scripts/Run-Stage5CC-Acceptance.ps1'; Label = 'launcher-helper' },
            [pscustomobject]@{ Path = 'docs/media-desktop-stage5cc-visual-uia-test-guide.md'; Label = 'test-guide' },
            [pscustomobject]@{ Path = 'docs/media-desktop-stage5cc-visual-uia-acceptance.md'; Label = 'acceptance-record' },
            [pscustomobject]@{ Path = 'scripts/ui-acceptance/scenarios/70-visual-keyboard-matrix.ps1'; Label = 'visual-matrix-scenario' },
            [pscustomobject]@{ Path = 'apps/media-desktop/tools/winapp/win-x64/LICENSE.txt'; Label = 'driver-license' },
            [pscustomobject]@{ Path = 'apps/media-desktop/tools/winapp/win-x64/SOURCE-NOTICE.txt'; Label = 'driver-notice' },
            [pscustomobject]@{ Path = 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'; Label = 'contract' },
            [pscustomobject]@{ Path = 'apps/media-desktop/tools/ffmpeg/win-x64/manifest.json'; Label = 'media-manifest' },
            [pscustomobject]@{ Path = 'scripts/run-stage5cc-ui-acceptance.ps1'; Label = 'entry' },
            [pscustomobject]@{ Path = 'scripts/ui-acceptance/README.txt'; Label = 'readme' },
            [pscustomobject]@{ Path = 'scripts/ui-acceptance/report-template/index.html'; Label = 'report-template' })) {
        [void] $requiredInputs.Add($entry)
    }
    foreach ($moduleName in @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner', 'Workflows', 'Metadata', 'VisualGate', 'Aggregate')) {
        [void] $requiredInputs.Add([pscustomobject] @{
                Path  = 'scripts/ui-acceptance/TraceFold.UiAcceptance.' + $moduleName + '.psm1'
                Label = 'module:' + $moduleName
            })
    }
    foreach ($scenario in $registryScenarios) {
        [void] $requiredInputs.Add([pscustomobject] @{
                Path  = 'scripts/ui-acceptance/scenarios/' + $scenario.File
                Label = 'scenario:' + $scenario.Name
            })
    }
    Assert-RequiredPackageInput -RepoRoot $repoRoot -Required $requiredInputs

    $contract = Read-JsonDocument -Path $contractPath
    $contractFixtures = @($contract.fixtureAssets)
    if ($contractFixtures.Count -eq 0) {
        throw 'The acceptance contract declares no fixtures; the toolkit would be incomplete.'
    }
    foreach ($asset in $contractFixtures) {
        $relative = [string] $asset.repositoryPath
        $source = Join-Path $repoRoot ($relative -replace '/', '\')
        if (-not [IO.File]::Exists($source)) {
            throw ('stage5c_c_not_ready: fixture is missing: ' + $relative)
        }
        if ((Get-Item -LiteralPath $source).Length -ne [long] $asset.sizeBytes) {
            throw ('stage5c_c_not_ready: fixture size changed: ' + $relative)
        }
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne ([string] $asset.sha256).ToUpperInvariant()) {
            throw ('stage5c_c_not_ready: fixture hash changed: ' + $relative)
        }
    }
}

# --- FFmpeg distribution ----------------------------------------------------------------------
#
# The product's pinned media tools are verified next: size and SHA-256 against the checked-in
# manifest, never trusted from the folder name.
if (-not (Test-FullyQualifiedPath -Path $FfmpegSourceRoot)) {
    throw 'FfmpegSourceRoot must be a fully qualified path.'
}
$ffmpegRoot = [IO.Path]::GetFullPath($FfmpegSourceRoot)
$ffmpegRootItem = Get-Item -LiteralPath $ffmpegRoot -Force -ErrorAction SilentlyContinue
if ($null -eq $ffmpegRootItem -or -not $ffmpegRootItem.PSIsContainer) {
    throw "FFmpeg distribution root is missing: $ffmpegRoot"
}
if (($ffmpegRootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "FFmpeg distribution root cannot be a reparse point: $ffmpegRoot"
}
if (-not (Test-Path -LiteralPath $toolManifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $toolNoticePath -PathType Leaf)) {
    throw 'Pinned FFmpeg manifest or third-party notice is missing.'
}
$ffmpegPath = Join-Path $ffmpegRoot 'bin\ffmpeg.exe'
$ffprobePath = Join-Path $ffmpegRoot 'bin\ffprobe.exe'
$licensePath = Join-Path $ffmpegRoot 'LICENSE'
$readmePath = Join-Path $ffmpegRoot 'README.txt'
$ffmpegItem = Assert-SourceFile -Path $ffmpegPath
$ffprobeItem = Assert-SourceFile -Path $ffprobePath
$null = Assert-SourceFile -Path $licensePath
$null = Assert-SourceFile -Path $readmePath
$manifest = Read-JsonDocument -Path $toolManifestPath
$pinnedTools = @(
    [pscustomobject]@{
        Name = 'ffmpeg.exe'
        Item = $ffmpegItem
        Manifest = $manifest.files.'ffmpeg.exe'
    },
    [pscustomobject]@{
        Name = 'ffprobe.exe'
        Item = $ffprobeItem
        Manifest = $manifest.files.'ffprobe.exe'
    })
foreach ($tool in $pinnedTools) {
    if ($tool.Item.Length -ne [long]$tool.Manifest.size) {
        throw "Pinned size mismatch for $($tool.Name)."
    }
    $hash = (Get-FileHash -LiteralPath $tool.Item.FullName -Algorithm SHA256).Hash
    if (-not [StringComparer]::Ordinal.Equals(
            $hash,
            [string]$tool.Manifest.sha256)) {
        throw "Pinned SHA-256 mismatch for $($tool.Name)."
    }
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$distItem = Get-Item -LiteralPath $distRoot -Force
if (($distItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Distribution root cannot be a reparse point: $distRoot"
}
$sourceCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Unable to identify the source commit for the package.'
}
$trackedChanges = @(& git -C $repoRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $trackedChanges.Count -ne 0) {
    throw 'Refusing to package tracked source changes that are not committed.'
}
$informationalVersion = "4.0.0-stage$versionStageSlug+$sourceCommit"

try {
    Invoke-DotNet -DotNetArguments @(
        'restore', $solution,
        '--source', $localPackageSource,
        "-p:RestorePackagesPath=$localPackageSource",
        '--disable-build-servers',
        '--ignore-failed-sources')
    # Reports belong to this invocation, outside staging/rollback cleanup. Keep them on failure
    # as well as success; a later publish always gets a new leaf and cannot replace the first run.
    $testResultsRoot = Join-Path (Join-Path $repoRoot 'artifacts/reports/publish-media-desktop') ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $testResultsRoot | Out-Null
    Assert-NoReparseTree -Path $testResultsRoot
    Write-Output "Publish test evidence: $testResultsRoot"
    Invoke-DotNet -DotNetArguments @(
        'test', $solution,
        '-c', 'Release',
        '--no-restore',
        "-p:RestorePackagesPath=$localPackageSource",
        '--disable-build-servers',
        '--logger', 'trx',
        '--results-directory', $testResultsRoot)
    if (@(Get-ChildItem -LiteralPath $testResultsRoot -Filter '*.trx' -Recurse -File).Count -eq 0) {
        throw "The publish test run produced no TRX report: $testResultsRoot"
    }

    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    Assert-NoReparseTree -Path $stagingRoot
    Invoke-DotNet -DotNetArguments @(
        'publish', $project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishProfile=win-x64',
        '-p:DebugType=none',
        '-p:DebugSymbols=false',
        '-p:Version=4.0.0',
        '-p:FileVersion=4.0.0.0',
        "-p:InformationalVersion=$informationalVersion",
        '-p:IncludeSourceRevisionInInformationalVersion=false',
        "-p:TraceFoldFfmpegSourceRoot=$ffmpegRoot",
        '--no-restore',
        "-p:RestorePackagesPath=$localPackageSource",
        '--disable-build-servers',
        '-o', $stagingPackage)

    $productLaunchProbe = 'NotRun'
    $versionDocument = [ordered] @{
        StageCheckpoint = $StageCheckpoint
        SourceCommit = $sourceCommit.ToLowerInvariant()
        InformationalVersion = $informationalVersion
    }
    if ($isStageCC) { $versionDocument['ProductLaunchProbe'] = $productLaunchProbe }
    $versionDocument | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $stagingPackage 'package-version.json') `
        -Encoding UTF8

    Assert-NoReparseTree -Path $stagingPackage
    $publishedToolRoot = Join-Path $stagingPackage 'tools\ffmpeg'
    if (-not (Test-Path -LiteralPath $publishedToolRoot -PathType Container)) {
        throw "Published FFmpeg directory is missing: $publishedToolRoot"
    }
    Copy-Item -LiteralPath $toolNoticePath -Destination $publishedToolRoot
    Assert-NoReparseTree -Path $publishedToolRoot
    foreach ($tool in $pinnedTools) {
        $published = Join-Path $publishedToolRoot $tool.Name
        $publishedItem = Get-Item -LiteralPath $published -Force -ErrorAction SilentlyContinue
        if ($null -eq $publishedItem -or
            $publishedItem.Length -ne [long]$tool.Manifest.size) {
            throw "Published pinned tool size mismatch: $published"
        }
        $hash = (Get-FileHash -LiteralPath $published -Algorithm SHA256).Hash
        if (-not [StringComparer]::Ordinal.Equals(
                $hash,
                [string]$tool.Manifest.sha256)) {
            throw "Published pinned tool SHA-256 mismatch: $published"
        }
    }
    Assert-MatchingFile `
        -ActualPath (Join-Path $publishedToolRoot 'LICENSE') `
        -ExpectedPath $licensePath
    Assert-MatchingFile `
        -ActualPath (Join-Path $publishedToolRoot 'README.txt') `
        -ExpectedPath $readmePath
    Assert-MatchingFile `
        -ActualPath (Join-Path $publishedToolRoot 'THIRD-PARTY-NOTICES.txt') `
        -ExpectedPath $toolNoticePath
    $exePath = Join-Path $stagingPackage 'TraceFold.Media.exe'
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw "Published executable is missing: $exePath"
    }
    if (Get-ChildItem -LiteralPath $stagingPackage -Recurse -File -Filter '*.pdb') {
        throw 'Published package contains PDB files.'
    }
    $forbidden = @(
        'python.exe',
        'uvicorn.exe',
        'node.exe',
        'msedge.exe',
        'chrome.exe')
    $forbiddenFiles = @(Get-ChildItem -LiteralPath $stagingPackage -Recurse -File |
        Where-Object { $forbidden -contains $_.Name.ToLowerInvariant() })
    if ($forbiddenFiles.Count -ne 0) {
        throw "Published package contains forbidden executables: $($forbiddenFiles.Name -join ', ')"
    }
    $mediaExecutables = @(Get-ChildItem -LiteralPath $stagingPackage -Recurse -File |
        Where-Object { $_.Name -in @('ffmpeg.exe', 'ffprobe.exe') })
    if ($mediaExecutables.Count -ne 2) {
        throw "Published package must contain exactly two media tool executables."
    }
    foreach ($mediaExecutable in $mediaExecutables) {
        $expectedPath = Join-Path $publishedToolRoot $mediaExecutable.Name
        $expectedPath = [IO.Path]::GetFullPath($expectedPath)
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals(
                [IO.Path]::GetFullPath($mediaExecutable.FullName),
                $expectedPath)) {
            throw "Media tool executable is outside tools\ffmpeg: $($mediaExecutable.FullName)"
        }
    }
    $forbiddenDirectories = @('data', 'logs', 'work', 'node_modules', '.git', 'artifacts', '.cache', '.worktrees')
    $unexpectedDirectories = @(Get-ChildItem -LiteralPath $stagingPackage -Recurse -Directory -Force |
        Where-Object { $forbiddenDirectories -contains $_.Name.ToLowerInvariant() })
    if ($unexpectedDirectories.Count -ne 0) {
        throw "Published package contains runtime/source directories: $($unexpectedDirectories.FullName -join ', ')"
    }
    $databaseFiles = @(Get-ChildItem -LiteralPath $stagingPackage -Recurse -File -Force |
        Where-Object { $_.Extension -in @('.db', '.sqlite', '.sqlite3') })
    if ($databaseFiles.Count -ne 0) {
        throw "Published package contains database files: $($databaseFiles.Name -join ', ')"
    }
    if ($isStageCC) {
        # Acceptance code never rides along with the product. These are the exact shapes the
        # toolkit owns - including the flat ones: a support DLL or .deps.json, a module, a
        # scenario script or a guide dropped at the product root - so their presence means the
        # two trees were mixed.
        $acceptanceFileNames = @(
            'winapp.exe',
            'TraceFold.Media.UiAcceptance.Support.exe',
            'TraceFold.Media.UiAcceptance.Support.dll',
            'TraceFold.Media.UiAcceptance.Support.deps.json',
            'TraceFold.Media.UiAcceptance.Support.runtimeconfig.json',
            'toolkit.json',
            'stage5cc-uia-contract.json',
            'Run-Stage5CC-Acceptance.cmd',
            'run-stage5cc-ui-acceptance.ps1',
            'index.html',
            'media-desktop-stage5cc-visual-uia-test-guide.md',
            'media-desktop-stage5cc-visual-uia-acceptance.md',
            '70-visual-keyboard-matrix.ps1')
        $acceptancePayload = @(Get-ChildItem -LiteralPath $stagingPackage -Recurse -File -Force |
            Where-Object {
                $acceptanceFileNames -contains $_.Name -or
                $_.Name -like 'TraceFold.Media.UiAcceptance.Support.*' -or
                $_.Name -like 'TraceFold.UiAcceptance.*.psm1' -or
                $_.Extension -eq '.ps1'
            })
        if ($acceptancePayload.Count -ne 0) {
            throw "Published product contains acceptance payload: $($acceptancePayload.Name -join ', ')"
        }
        $acceptancePayloadDirectories = @(Get-ChildItem -LiteralPath $stagingPackage -Recurse -Directory -Force |
            Where-Object { $_.Name.ToLowerInvariant() -in @('ui-acceptance', 'report-template', 'fixtures', '.superpowers') })
        if ($acceptancePayloadDirectories.Count -ne 0) {
            throw "Published product contains acceptance directories: $($acceptancePayloadDirectories.FullName -join ', ')"
        }
    }

    if ($isStageCC) {
        # --- the offline toolkit -----------------------------------------------------------
        New-Item -ItemType Directory -Path $stagingToolkit | Out-Null
        Assert-NoReparseTree -Path $stagingToolkit

        $stagingSupport = Join-Path $stagingToolkit 'runtime\support'
        # The solution restore has no RID for Support. Resolve the exact self-contained target
        # from the same verified offline cache before publishing with --no-restore.
        Invoke-DotNet -DotNetArguments @(
            'restore', $supportProject,
            '-r', 'win-x64',
            '-p:Configuration=Release',
            '-p:SelfContained=true',
            '--source', $localPackageSource,
            "-p:RestorePackagesPath=$localPackageSource",
            '--disable-build-servers',
            '--ignore-failed-sources')
        Invoke-DotNet -DotNetArguments @(
            'publish', $supportProject,
            '-c', 'Release',
            '-r', 'win-x64',
            '--self-contained', 'true',
            '-p:DebugType=none',
            '-p:DebugSymbols=false',
            '--no-restore',
            "-p:RestorePackagesPath=$localPackageSource",
            '--disable-build-servers',
            '-o', $stagingSupport)
        if (-not (Test-Path -LiteralPath (Join-Path $stagingSupport 'TraceFold.Media.UiAcceptance.Support.exe') -PathType Leaf)) {
            throw 'The published acceptance support executable is missing.'
        }
        Register-ToolkitTree -Directory $stagingSupport -SourceLabel 'support'

        $driverExtract = Join-Path $stagingRoot 'driver-extract'
        Assert-DirectChild -Path $driverExtract -Parent $stagingRoot -ExpectedName 'driver-extract'
        [void][IO.Directory]::CreateDirectory($driverExtract)
        try { $null = Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
        try { $null = Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }
        [IO.Compression.ZipFile]::ExtractToDirectory($driverArchive.Path, $driverExtract)
        $excludedDriverMembers = New-Object System.Collections.ArrayList
        foreach ($file in @(Get-ChildItem -LiteralPath $driverExtract -Recurse -File -Force | Sort-Object FullName)) {
            $relative = $file.FullName.Substring($driverExtract.Length).TrimStart([IO.Path]::DirectorySeparatorChar) -replace '\\', '/'
            if ($file.Extension -ieq '.pdb') {
                # Debug symbols are not part of a distributed payload, exactly as the product
                # publish already drops them. The archive identity above still covers them.
                [void] $excludedDriverMembers.Add($relative)
                continue
            }
            Copy-ToolkitFile -Source $file.FullName -DestinationRelative ('runtime/driver/' + $relative) -SourceLabel 'driver'
        }
        Remove-SafeDirectChild -Path $driverExtract -Parent $stagingRoot -ExpectedName 'driver-extract'
        if (-not (Test-Path -LiteralPath (Join-Path $stagingToolkit 'runtime\driver\winapp.exe') -PathType Leaf)) {
            throw 'The packaged driver executable is missing after extraction.'
        }

        Copy-ToolkitFile -Source (Join-Path $repoRoot 'scripts/run-stage5cc-ui-acceptance.ps1') -DestinationRelative 'scripts/run-stage5cc-ui-acceptance.ps1' -SourceLabel 'entry'
        foreach ($module in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'scripts/ui-acceptance') -File -Filter '*.psm1' -Force | Sort-Object Name)) {
            Copy-ToolkitFile -Source $module.FullName -DestinationRelative ('scripts/ui-acceptance/' + $module.Name) -SourceLabel 'module'
        }
        foreach ($scenario in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'scripts/ui-acceptance/scenarios') -File -Force | Sort-Object Name)) {
            Copy-ToolkitFile -Source $scenario.FullName -DestinationRelative ('scripts/ui-acceptance/scenarios/' + $scenario.Name) -SourceLabel 'scenario'
        }
        Add-ToolkitTree -SourceDirectory (Join-Path $repoRoot 'scripts/ui-acceptance/report-template') -DestinationRelative 'scripts/ui-acceptance/report-template' -SourceLabel 'template'
        Copy-ToolkitFile -Source (Join-Path $repoRoot 'scripts/ui-acceptance/README.txt') -DestinationRelative 'scripts/ui-acceptance/README.txt' -SourceLabel 'readme'
        # The launcher is the package's front door: it belongs at the toolkit root, with its
        # single-purpose helper under scripts next to the public entry. Putting the .cmd under
        # scripts again would leave the root without the double-clickable entry.
        Copy-ToolkitFile -Source (Join-Path $repoRoot 'scripts/Run-Stage5CC-Acceptance.cmd') -DestinationRelative 'Run-Stage5CC-Acceptance.cmd' -SourceLabel 'launcher'
        Copy-ToolkitFile -Source (Join-Path $repoRoot 'scripts/Run-Stage5CC-Acceptance.ps1') -DestinationRelative 'scripts/Run-Stage5CC-Acceptance.ps1' -SourceLabel 'launcher-helper'
        Copy-ToolkitFile -Source (Join-Path $repoRoot 'docs/media-desktop-stage5cc-visual-uia-test-guide.md') -DestinationRelative 'docs/media-desktop-stage5cc-visual-uia-test-guide.md' -SourceLabel 'guide'
        Copy-ToolkitFile -Source (Join-Path $repoRoot 'docs/media-desktop-stage5cc-visual-uia-acceptance.md') -DestinationRelative 'docs/media-desktop-stage5cc-visual-uia-acceptance.md' -SourceLabel 'guide'
        Copy-ToolkitFile -Source $contractPath -DestinationRelative 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json' -SourceLabel 'contract'
        foreach ($asset in $contractFixtures) {
            $relative = ([string] $asset.repositoryPath) -replace '\\', '/'
            Copy-ToolkitFile -Source (Join-Path $repoRoot ($relative -replace '/', '\')) -DestinationRelative $relative -SourceLabel 'fixture'
        }
        Copy-ToolkitFile -Source (Join-Path $repoRoot 'apps/media-desktop/tools/winapp/win-x64/manifest.json') -DestinationRelative 'manifests/winapp-manifest.json' -SourceLabel 'manifest'
        # MainChain reads this declaration from the toolkit root, so the copy the producer already
        # verified against the FFmpeg distribution is the one that ships.
        Copy-ToolkitFile -Source $toolManifestPath -DestinationRelative 'apps/media-desktop/tools/ffmpeg/win-x64/manifest.json' -SourceLabel 'media-manifest'
        Copy-ToolkitFile -Source (Join-Path $repoRoot 'apps/media-desktop/tools/winapp/win-x64/LICENSE.txt') -DestinationRelative 'licenses/winapp-LICENSE.txt' -SourceLabel 'license'
        Copy-ToolkitFile -Source (Join-Path $repoRoot 'apps/media-desktop/tools/winapp/win-x64/SOURCE-NOTICE.txt') -DestinationRelative 'licenses/winapp-SOURCE-NOTICE.txt' -SourceLabel 'license'

        Assert-NoReparseTree -Path $stagingToolkit
        Assert-ToolkitCoverage -Root $stagingToolkit

        $supportExecutable = Join-Path $stagingToolkit 'runtime\support\TraceFold.Media.UiAcceptance.Support.exe'
        $driverExecutable = Join-Path $stagingToolkit 'runtime\driver\winapp.exe'
        $sortedMembers = @($script:toolkitMembers | Sort-Object -Property path)
        $toolkitDocument = [ordered] @{
            schema          = 'tracefold-ui-acceptance-toolkit'
            schemaVersion   = 1
            stageCheckpoint = $StageCheckpoint
            sourceCommit    = $sourceCommit.ToLowerInvariant()
            informationalVersion = $informationalVersion
            productDirectory = $packageName
            layout          = [ordered] @{
                supportRoot  = 'runtime/support'
                driverRoot   = 'runtime/driver'
                contractPath = 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
                resultsRoot  = 'results'
            }
            support         = [ordered] @{
                executable = 'TraceFold.Media.UiAcceptance.Support.exe'
                bytes      = [long] (Get-Item -LiteralPath $supportExecutable).Length
                sha256     = (Get-FileHash -LiteralPath $supportExecutable -Algorithm SHA256).Hash
            }
            driver          = [ordered] @{
                memberName      = 'winapp.exe'
                bytes           = [long] (Get-Item -LiteralPath $driverExecutable).Length
                sha256          = (Get-FileHash -LiteralPath $driverExecutable -Algorithm SHA256).Hash
                packageManifest = 'manifests/winapp-manifest.json'
                excludedMembers = @($excludedDriverMembers | Sort-Object)
                archive         = [ordered] @{
                    name       = $driverArchive.Name
                    bytes      = [long] $driverArchive.Bytes
                    sha256     = $driverArchive.Sha256
                    version    = [string] $driverManifest.version
                    architecture = [string] $driverManifest.architecture
                }
            }
            members         = $sortedMembers
        }
        $toolkitJson = $toolkitDocument | ConvertTo-Json -Depth 6
        [IO.File]::WriteAllText((Join-Path $stagingToolkit 'toolkit.json'), $toolkitJson, [Text.UTF8Encoding]::new($false))
        Assert-NoReparseTree -Path $stagingToolkit

        Compress-Archive `
            -Path (Join-Path $stagingToolkit '*') `
            -DestinationPath $stagingToolkitZip
    }

    if ($isStageCC) {
        # Promotion gate, unconditional and complete: the product smoke runs its 5C-C path - the
        # package identity, then the four probe scenarios - before any final target is touched.
        # A controlled launcher may substitute the process under test, but the four requests and
        # their exit-code rules are the real ones and the recorded launch result is NotRun; with
        # no launcher the smoke starts the packaged product and records Passed.
        $smokeArguments = @('-StageCheckpoint', $StageCheckpoint, '-PackageDirectory', $stagingPackage)
        if (-not [string]::IsNullOrWhiteSpace($ProductSmokeLauncher)) {
            $smokeArguments += @('-ProbeExecutable', $ProductSmokeLauncher)
        }
        $null = Invoke-SmokeChild `
            -ScriptPath (Join-Path $repoRoot 'scripts\smoke-media-desktop-package.ps1') `
            -Arguments $smokeArguments `
            -Label 'product package smoke'
        $productLaunchProbe = if ([string]::IsNullOrWhiteSpace($ProductSmokeLauncher)) { 'Passed' } else { 'NotRun' }
        $versionDocument['ProductLaunchProbe'] = $productLaunchProbe
        $versionDocument | ConvertTo-Json | Set-Content `
            -LiteralPath (Join-Path $stagingPackage 'package-version.json') `
            -Encoding UTF8
        Write-Output ('Product package gate: complete smoke passed (real probe ' + $productLaunchProbe + ')')
    }

    Compress-Archive `
        -Path (Join-Path $stagingPackage '*') `
        -DestinationPath $stagingZip
    Assert-NoReparseTree -Path $stagingRoot

    if ($isStageCC) {
        # The production smoke gate: the toolkit is checked by the script that ships in it, and by
        # the packaged entry itself (-ValidateOnly), so the manifest is proven consumable before
        # any of the four final targets is touched.
        $null = Invoke-SmokeChild `
            -ScriptPath (Join-Path $repoRoot 'scripts\smoke-stage5cc-ui-acceptance-toolkit.ps1') `
            -Arguments @('-ToolkitDirectory', $stagingToolkit, '-ProductDirectory', $stagingPackage, '-ExpectedVersion', $informationalVersion) `
            -Label 'toolkit structure smoke'
    }

    # --- transactional promotion ----------------------------------------------------------
    #
    # Four targets for Stage 5C-C, two for the earlier stages. Every target is a direct child of
    # dist with a fixed name, the old ones move to a backup inside dist first, and any failure
    # removes what was installed and puts the old ones back. The backup is only deleted when the
    # promotion completed or every previous artifact was restored: a rollback that cannot finish
    # must leave the only remaining copy where the operator can find it.
    $targets = New-Object System.Collections.ArrayList
    [void] $targets.Add([pscustomobject] @{ Kind = 'ProductDirectory'; Staged = $stagingPackage; StagedName = 'product'; Final = $packageDir; Backup = $backupPackage; Name = $packageName })
    [void] $targets.Add([pscustomobject] @{ Kind = 'ProductArchive'; Staged = $stagingZip; StagedName = $archiveName; Final = $zipPath; Backup = $backupZip; Name = $archiveName })
    if ($isStageCC) {
        [void] $targets.Add([pscustomobject] @{ Kind = 'ToolkitDirectory'; Staged = $stagingToolkit; StagedName = 'toolkit'; Final = $toolkitDir; Backup = $backupToolkit; Name = $toolkitName })
        [void] $targets.Add([pscustomobject] @{ Kind = 'ToolkitArchive'; Staged = $stagingToolkitZip; StagedName = $toolkitArchiveName; Final = $toolkitZipPath; Backup = $backupToolkitZip; Name = $toolkitArchiveName })
    }
    if ($FaultPoint -ceq 'BeforePromotion') {
        throw 'Injected promotion fault before any target moved.'
    }

    $promotionComplete = $false
    $backupDisposable = $true
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    Assert-NoReparseTree -Path $backupRoot
    $hadPrevious = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $installed = New-Object System.Collections.ArrayList
    try {
        foreach ($target in $targets) {
            Assert-DirectChild -Path $target.Final -Parent $distRoot -ExpectedName $target.Name
            Assert-DirectChild -Path $target.Backup -Parent $backupRoot -ExpectedName $target.Name
            if (Test-Path -LiteralPath $target.Final) {
                Assert-NoReparseTree -Path $target.Final
                Move-Item -LiteralPath $target.Final -Destination $target.Backup
                [void] $hadPrevious.Add($target.Kind)
                if ($FaultPoint -ceq ('AfterBackup' + $target.Kind)) {
                    throw ('Injected promotion fault after backing up ' + $target.Kind + '.')
                }
            }
        }

        foreach ($target in $targets) {
            Assert-DirectChild -Path $target.Staged -Parent $stagingRoot -ExpectedName $target.StagedName
            Assert-DirectChild -Path $target.Final -Parent $distRoot -ExpectedName $target.Name
            Assert-NoReparseTree -Path $target.Staged
            Move-Item -LiteralPath $target.Staged -Destination $target.Final
            [void] $installed.Add($target)
            if ($FaultPoint -ceq ('After' + $target.Kind)) {
                throw ('Injected promotion fault after ' + $target.Kind + '.')
            }
        }
        $promotionComplete = $true
    }
    catch {
        $promotionError = $_.Exception.Message
        $recoveryFailures = New-Object System.Collections.ArrayList
        for ($index = $installed.Count - 1; $index -ge 0; $index--) {
            $target = $installed[$index]
            try {
                Remove-SafeDirectChild -Path $target.Final -Parent $distRoot -ExpectedName $target.Name
            }
            catch {
                [void] $recoveryFailures.Add(('remove new ' + $target.Kind + ': ' + $_.Exception.Message))
            }
        }
        foreach ($target in $targets) {
            if (-not $hadPrevious.Contains($target.Kind)) { continue }
            try {
                if ($RecoveryFaultPoint -ceq $target.Kind) {
                    throw ('Injected recovery fault for ' + $target.Kind + '.')
                }
                Assert-DirectChild -Path $target.Backup -Parent $backupRoot -ExpectedName $target.Name
                Assert-DirectChild -Path $target.Final -Parent $distRoot -ExpectedName $target.Name
                if (-not (Test-Path -LiteralPath $target.Backup)) {
                    throw 'the backup copy is missing.'
                }
                if (Test-Path -LiteralPath $target.Final) {
                    throw 'the final path is still occupied.'
                }
                Assert-NoReparseTree -Path $target.Backup
                Move-Item -LiteralPath $target.Backup -Destination $target.Final
            }
            catch {
                [void] $recoveryFailures.Add(($target.Kind + ': ' + $_.Exception.Message))
            }
        }
        if ($recoveryFailures.Count -eq 0) {
            try {
                Remove-SafeDirectChild -Path $backupRoot -Parent $distRoot -ExpectedName $backupName
            }
            catch {
                [void] $recoveryFailures.Add(('remove backup: ' + $_.Exception.Message))
            }
        }
        if ($recoveryFailures.Count -gt 0) {
            $backupDisposable = $false
            $report = 'Promotion failed: ' + $promotionError + [Environment]::NewLine +
                'Recovery failed: ' + ($recoveryFailures -join '; ') + [Environment]::NewLine +
                'Previous artifacts that could not be restored remain in: ' + $backupRoot
            throw $report
        }
        throw ('Promotion failed: ' + $promotionError)
    }
    Remove-SafeDirectChild -Path $backupRoot -Parent $distRoot -ExpectedName $backupName

    Write-Output "Published: $packageDir"
    Write-Output "Archive: $zipPath"
    if ($isStageCC) {
        Write-Output "Product launch probe: $productLaunchProbe"
        Write-Output "Toolkit: $toolkitDir"
        Write-Output "Toolkit archive: $toolkitZipPath"
        Write-Output ('Toolkit members: ' + $sortedMembers.Count + ', fixtures: ' + $contractFixtures.Count)
    }
}
finally {
    $cleanupFailure = $null
    try {
        Remove-SafeDirectChild `
            -Path $stagingRoot `
            -Parent $distRoot `
            -ExpectedName $stagingName
    }
    catch {
        $cleanupFailure = $_
    }
    if ($backupDisposable) {
        # Only when the promotion completed or every previous artifact was restored. A rollback
        # that could not finish keeps this directory: it is the only copy of the old pair.
        try {
            Remove-SafeDirectChild `
                -Path $backupRoot `
                -Parent $distRoot `
                -ExpectedName $backupName
        }
        catch {
            if ($null -eq $cleanupFailure) { $cleanupFailure = $_ }
        }
    }
    if ($null -ne $cleanupFailure -and $promotionComplete) {
        throw $cleanupFailure
    }
}
