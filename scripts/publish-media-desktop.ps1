[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('4A','4B','5A','5B','5C','5C-B')][string] $StageCheckpoint,
    [Parameter(Mandatory)][string] $FfmpegSourceRoot,
    [string] $NugetPackageSource
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
$packageName = "TraceFold-MediaDesktop-Stage$StageCheckpoint-win-x64"
$archiveName = "$packageName.zip"
$packageDir = [IO.Path]::GetFullPath((Join-Path $distRoot $packageName))
$zipPath = [IO.Path]::GetFullPath((Join-Path $distRoot $archiveName))
$stageSlug = $StageCheckpoint.ToLowerInvariant()
$versionStageSlug = $StageCheckpoint.Replace('-', '').ToLowerInvariant()
$stagingName = ".tracefold-stage$stageSlug-publish-$([Guid]::NewGuid().ToString('N'))"
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $distRoot $stagingName))
$stagingPackage = [IO.Path]::GetFullPath((Join-Path $stagingRoot $packageName))
$stagingZip = [IO.Path]::GetFullPath((Join-Path $stagingRoot $archiveName))
$backupName = ".tracefold-stage$stageSlug-backup-$([Guid]::NewGuid().ToString('N'))"
$backupRoot = [IO.Path]::GetFullPath((Join-Path $distRoot $backupName))
$backupPackage = [IO.Path]::GetFullPath((Join-Path $backupRoot $packageName))
$backupZip = [IO.Path]::GetFullPath((Join-Path $backupRoot $archiveName))

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

Assert-DirectChild -Path $packageDir -Parent $distRoot -ExpectedName $packageName
Assert-DirectChild -Path $zipPath -Parent $distRoot -ExpectedName $archiveName
Assert-DirectChild -Path $stagingRoot -Parent $distRoot -ExpectedName $stagingName
Assert-DirectChild -Path $backupRoot -Parent $distRoot -ExpectedName $backupName

$solution = Join-Path $repoRoot 'apps\media-desktop\TraceFold.MediaDesktop.slnx'
$project = Join-Path $repoRoot 'apps\media-desktop\src\TraceFold.Media.Desktop\TraceFold.Media.Desktop.csproj'
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
if (-not (Test-Path -LiteralPath $localPackageSource -PathType Container)) {
    throw "Local package source is missing: $localPackageSource"
}
Assert-NoReparseTree -Path $localPackageSource
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
$manifest = Get-Content -LiteralPath $toolManifestPath -Raw | ConvertFrom-Json
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
    Invoke-DotNet -DotNetArguments @(
        'test', $solution,
        '-c', 'Release',
        '--no-restore',
        "-p:RestorePackagesPath=$localPackageSource",
        '--disable-build-servers')

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

    [pscustomobject]@{
        StageCheckpoint = $StageCheckpoint
        SourceCommit = $sourceCommit.ToLowerInvariant()
        InformationalVersion = $informationalVersion
    } | ConvertTo-Json | Set-Content `
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
    $forbiddenDirectories = @('data', 'logs', 'work', 'node_modules', '.git')
    $unexpectedDirectories = @(Get-ChildItem -LiteralPath $stagingPackage -Recurse -Directory |
        Where-Object { $forbiddenDirectories -contains $_.Name.ToLowerInvariant() })
    if ($unexpectedDirectories.Count -ne 0) {
        throw "Published package contains runtime/source directories: $($unexpectedDirectories.FullName -join ', ')"
    }
    $databaseFiles = @(Get-ChildItem -LiteralPath $stagingPackage -Recurse -File |
        Where-Object { $_.Extension -in @('.db', '.sqlite', '.sqlite3') })
    if ($databaseFiles.Count -ne 0) {
        throw "Published package contains database files: $($databaseFiles.Name -join ', ')"
    }

    Compress-Archive `
        -Path (Join-Path $stagingPackage '*') `
        -DestinationPath $stagingZip
    Assert-NoReparseTree -Path $stagingRoot

    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    Assert-NoReparseTree -Path $backupRoot
    $hadPackage = $false
    $hadArchive = $false
    $installedPackage = $false
    $installedArchive = $false
    try {
        if (Test-Path -LiteralPath $packageDir) {
            Assert-NoReparseTree -Path $packageDir
            Move-Item -LiteralPath $packageDir -Destination $backupPackage
            $hadPackage = $true
        }
        if (Test-Path -LiteralPath $zipPath) {
            Assert-NoReparseTree -Path $zipPath
            Move-Item -LiteralPath $zipPath -Destination $backupZip
            $hadArchive = $true
        }

        Move-Item -LiteralPath $stagingPackage -Destination $packageDir
        $installedPackage = $true
        Move-Item -LiteralPath $stagingZip -Destination $zipPath
        $installedArchive = $true
    }
    catch {
        $promotionError = $_
        if ($installedArchive) {
            Remove-SafeDirectChild -Path $zipPath -Parent $distRoot -ExpectedName $archiveName
        }
        if ($installedPackage) {
            Remove-SafeDirectChild -Path $packageDir -Parent $distRoot -ExpectedName $packageName
        }
        if ($hadPackage) {
            Move-Item -LiteralPath $backupPackage -Destination $packageDir
        }
        if ($hadArchive) {
            Move-Item -LiteralPath $backupZip -Destination $zipPath
        }
        Remove-SafeDirectChild -Path $backupRoot -Parent $distRoot -ExpectedName $backupName
        throw $promotionError
    }
    Remove-SafeDirectChild -Path $backupRoot -Parent $distRoot -ExpectedName $backupName

    Write-Output "Published: $packageDir"
    Write-Output "Archive: $zipPath"
}
finally {
    Remove-SafeDirectChild `
        -Path $stagingRoot `
        -Parent $distRoot `
        -ExpectedName $stagingName
}
