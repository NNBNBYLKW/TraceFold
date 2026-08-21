[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
$packageDir = [IO.Path]::GetFullPath(
    (Join-Path $distRoot 'TraceFold-MediaDesktop-Stage1-win-x64'))
$zipPath = [IO.Path]::GetFullPath(
    (Join-Path $distRoot 'TraceFold-MediaDesktop-Stage1-win-x64.zip'))
$requiredPrefix = $distRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar

if (-not $packageDir.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe publish directory: $packageDir"
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $DotNetArguments)
    & dotnet @DotNetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE"
    }
}

$solution = Join-Path $repoRoot 'apps\media-desktop\TraceFold.MediaDesktop.slnx'
$project = Join-Path $repoRoot 'apps\media-desktop\src\TraceFold.Media.Desktop\TraceFold.Media.Desktop.csproj'
Invoke-DotNet -DotNetArguments @('restore', $solution)
Invoke-DotNet -DotNetArguments @('test', $solution, '-c', 'Release', '--no-restore')
Invoke-DotNet -DotNetArguments @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishProfile=win-x64',
    '-p:DebugType=none',
    '-p:DebugSymbols=false',
    '-o', $packageDir)

$exePath = Join-Path $packageDir 'TraceFold.Media.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Published executable is missing: $exePath"
}
if (Get-ChildItem -LiteralPath $packageDir -Recurse -File -Filter '*.pdb') {
    throw 'Published package contains PDB files.'
}
$forbidden = @('python.exe', 'uvicorn', 'uvicorn.exe', 'node.exe', 'msedge.exe', 'chrome.exe')
$forbiddenFiles = @(Get-ChildItem -LiteralPath $packageDir -Recurse -File |
    Where-Object { $forbidden -contains $_.Name.ToLowerInvariant() })
if ($forbiddenFiles.Count -ne 0) {
    throw "Published package contains forbidden executables: $($forbiddenFiles.Name -join ', ')"
}

Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath
Write-Output "Published: $packageDir"
Write-Output "Archive: $zipPath"
