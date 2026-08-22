[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
$packageName = 'TraceFold-MediaDesktop-Stage2-win-x64'
$archiveName = "$packageName.zip"
$packageDir = [IO.Path]::GetFullPath((Join-Path $distRoot $packageName))
$zipPath = [IO.Path]::GetFullPath((Join-Path $distRoot $archiveName))
$stagingName = ".tracefold-stage2-publish-$([Guid]::NewGuid().ToString('N'))"
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $distRoot $stagingName))
$stagingPackage = [IO.Path]::GetFullPath((Join-Path $stagingRoot $packageName))
$stagingZip = [IO.Path]::GetFullPath((Join-Path $stagingRoot $archiveName))
$backupName = ".tracefold-stage2-backup-$([Guid]::NewGuid().ToString('N'))"
$backupRoot = [IO.Path]::GetFullPath((Join-Path $distRoot $backupName))
$backupPackage = [IO.Path]::GetFullPath((Join-Path $backupRoot $packageName))
$backupZip = [IO.Path]::GetFullPath((Join-Path $backupRoot $archiveName))

function Assert-DirectChild {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Parent,
        [Parameter(Mandatory)][string] $ExpectedName
    )
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::TrimEndingDirectorySeparator(
        [IO.Path]::GetFullPath($Parent))
    $actualParent = [IO.Path]::TrimEndingDirectorySeparator(
        [IO.Path]::GetDirectoryName($fullPath))
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

Assert-DirectChild -Path $packageDir -Parent $distRoot -ExpectedName $packageName
Assert-DirectChild -Path $zipPath -Parent $distRoot -ExpectedName $archiveName
Assert-DirectChild -Path $stagingRoot -Parent $distRoot -ExpectedName $stagingName
Assert-DirectChild -Path $backupRoot -Parent $distRoot -ExpectedName $backupName

$solution = Join-Path $repoRoot 'apps\media-desktop\TraceFold.MediaDesktop.slnx'
$project = Join-Path $repoRoot 'apps\media-desktop\src\TraceFold.Media.Desktop\TraceFold.Media.Desktop.csproj'
$localPackageSource = Join-Path $repoRoot 'apps\media-desktop\.packages'
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

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$distItem = Get-Item -LiteralPath $distRoot -Force
if (($distItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Distribution root cannot be a reparse point: $distRoot"
}

try {
    Invoke-DotNet -DotNetArguments @(
        'restore', $solution,
        '--source', $localPackageSource,
        '--disable-build-servers',
        '--ignore-failed-sources')
    Invoke-DotNet -DotNetArguments @(
        'test', $solution,
        '-c', 'Release',
        '--no-restore',
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
        '--no-restore',
        '--disable-build-servers',
        '-o', $stagingPackage)

    Assert-NoReparseTree -Path $stagingPackage
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
        'chrome.exe',
        'ffmpeg.exe',
        'ffprobe.exe')
    $forbiddenFiles = @(Get-ChildItem -LiteralPath $stagingPackage -Recurse -File |
        Where-Object { $forbidden -contains $_.Name.ToLowerInvariant() })
    if ($forbiddenFiles.Count -ne 0) {
        throw "Published package contains forbidden executables: $($forbiddenFiles.Name -join ', ')"
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
