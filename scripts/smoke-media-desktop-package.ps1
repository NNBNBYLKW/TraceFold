[CmdletBinding()]
param(
    [string] $PackageDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot 'dist\TraceFold-MediaDesktop-Stage3-win-x64'
}
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
$exePath = [IO.Path]::GetFullPath((Join-Path $packageRoot 'TraceFold.Media.exe'))
$packageToolRoot = [IO.Path]::GetFullPath((Join-Path $packageRoot 'tools\ffmpeg'))
$packageFfmpegPath = [IO.Path]::GetFullPath((Join-Path $packageToolRoot 'ffmpeg.exe'))
$packageFfprobePath = [IO.Path]::GetFullPath((Join-Path $packageToolRoot 'ffprobe.exe'))
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Package executable is missing: $exePath"
}
if (-not (Test-Path -LiteralPath $packageFfmpegPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $packageFfprobePath -PathType Leaf)) {
    throw 'Package-local FFmpeg or FFprobe is missing.'
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeName = "tracefold-media-desktop-smoke-$([Guid]::NewGuid().ToString('N'))"
$tempRoot = [IO.Path]::GetFullPath((Join-Path $tempBase $smokeName))
$oldDataRoot = $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT
$oldPath = $env:PATH
$primary = $null
$secondary = $null
$fatalLaunch = $null
$probeProcess = $null
$probeToken = [Guid]::NewGuid().ToString('D')
$probeName = "tracefold-media-desktop-stage3-probe-$([Guid]::NewGuid().ToString('N'))"
$probeRoot = [IO.Path]::GetFullPath((Join-Path $tempBase $probeName))
$probeMarker = Join-Path $probeRoot 'probe-owner.txt'

function Get-TrimmedFullPath {
    param([Parameter(Mandatory)][string] $Path)
    $separators = [char[]]@(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    return [IO.Path]::GetFullPath($Path).TrimEnd($separators)
}

function Get-BrowserProcessIds {
    return @(Get-Process -Name 'msedge', 'chrome' -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id)
}

function Get-PackageProcesses {
    return @(Get-Process -Name 'TraceFold.Media' -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                [IO.Path]::GetFullPath($_.Path) -eq $exePath
            }
            catch {
                $false
            }
    })
}

function Get-PackageFfmpegProcesses {
    return @(Get-Process -Name 'ffmpeg' -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                [IO.Path]::GetFullPath($_.Path) -eq $packageFfmpegPath
            }
            catch {
                $false
            }
    })
}

function Assert-DirectTempChild {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $ExpectedName
    )
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = Get-TrimmedFullPath -Path ([IO.Path]::GetDirectoryName($fullPath))
    $temp = Get-TrimmedFullPath -Path $tempBase
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($parent, $temp) -or
        -not [StringComparer]::Ordinal.Equals(
            [IO.Path]::GetFileName($fullPath),
            $ExpectedName)) {
        throw "Unsafe smoke cleanup root: $fullPath"
    }
}

function Assert-NoReparseTree {
    param([Parameter(Mandatory)][string] $Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return
    }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing a reparse-point cleanup root: $Path"
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

$preExistingProcesses = @(Get-PackageProcesses)
if ($preExistingProcesses.Count -ne 0) {
    throw "Refusing to run while package processes already exist: $($preExistingProcesses.Id -join ', ')"
}
$preExistingFfmpeg = @(Get-PackageFfmpegProcesses)
if ($preExistingFfmpeg.Count -ne 0) {
    throw "Refusing to run while package FFmpeg processes already exist: $($preExistingFfmpeg.Id -join ', ')"
}

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT = $tempRoot
    $env:PATH = Join-Path $env:SystemRoot 'System32'
    $browserBefore = @(Get-BrowserProcessIds)

    $primary = Start-Process -FilePath $exePath -PassThru -WindowStyle Normal
    $windowDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 200
        $primary.Refresh()
        if ($primary.HasExited) {
            throw "Primary process exited early with code $($primary.ExitCode)."
        }
    } while ([string]::IsNullOrWhiteSpace($primary.MainWindowTitle) -and
             [DateTime]::UtcNow -lt $windowDeadline)

    if ([string]::IsNullOrWhiteSpace($primary.MainWindowTitle)) {
        throw 'Native main window did not appear within 15 seconds.'
    }

    $secondary = Start-Process -FilePath $exePath -PassThru -WindowStyle Normal
    if (-not $secondary.WaitForExit(10000)) {
        throw 'Secondary launch did not redirect and exit within 10 seconds.'
    }
    if ($secondary.ExitCode -ne 0) {
        throw "Secondary launch failed with exit code $($secondary.ExitCode)."
    }

    $packageProcesses = @(Get-PackageProcesses)
    if ($packageProcesses.Count -ne 1) {
        throw "Expected one package process, found $($packageProcesses.Count)."
    }

    $browserAfter = @(Get-BrowserProcessIds)
    $newBrowserIds = @($browserAfter | Where-Object { $_ -notin $browserBefore })
    if ($newBrowserIds.Count -ne 0) {
        throw "Application opened a browser process: $($newBrowserIds -join ', ')"
    }

    New-Item -ItemType Directory -Path $probeRoot | Out-Null
    [IO.File]::WriteAllText($probeMarker, $probeToken)
    $probeFixtures = Join-Path $probeRoot 'fixtures'
    New-Item -ItemType Directory -Path $probeFixtures | Out-Null
    $fixtureSource = Join-Path $repoRoot 'apps\media-api\tests\fixtures\media'
    Copy-Item `
        -LiteralPath (Join-Path $fixtureSource 'paired.mp4') `
        -Destination $probeFixtures
    Copy-Item `
        -LiteralPath (Join-Path $fixtureSource 'paired.m4a') `
        -Destination $probeFixtures
    $probeArguments = @(
        '--stage3-package-probe-root',
        ('"{0}"' -f $probeRoot),
        '--stage3-package-probe-token',
        $probeToken)
    $probeProcess = Start-Process `
        -FilePath $exePath `
        -ArgumentList $probeArguments `
        -PassThru `
        -WindowStyle Hidden
    if (-not $probeProcess.WaitForExit(45000)) {
        throw 'Stage 3 package probe did not exit within 45 seconds.'
    }
    if ($probeProcess.ExitCode -ne 0) {
        throw "Stage 3 package probe failed with exit code $($probeProcess.ExitCode)."
    }

    $probeResultPath = Join-Path $probeRoot 'stage3-probe-result.json'
    if (-not (Test-Path -LiteralPath $probeResultPath -PathType Leaf)) {
        throw 'Stage 3 package probe result is missing.'
    }
    $result = Get-Content -LiteralPath $probeResultPath -Raw | ConvertFrom-Json
    if (-not $result.Succeeded) {
        throw "Stage 3 package probe reported failure: $($result.ErrorCode)"
    }
    $reportedToolPath = [IO.Path]::GetFullPath([string]$result.ToolPath)
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals(
            $reportedToolPath,
            $packageFfmpegPath)) {
        throw "Probe used a tool outside the package: $reportedToolPath"
    }
    if ((Test-Path -LiteralPath (Join-Path $probeRoot 'inbox\paired.mp4')) -or
        (Test-Path -LiteralPath (Join-Path $probeRoot 'inbox\paired.m4a'))) {
        throw 'Probe sources were not removed after verified ingest.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $probeRoot 'library\paired.mp4'))) {
        throw 'Probe formal file is missing.'
    }
    if (-not $result.HasVideo -or -not $result.HasAudio) {
        throw 'Probe formal media did not validate video and audio streams.'
    }
    $processingRoot = Join-Path $probeRoot 'work\.tracefold\processing'
    if ((Test-Path -LiteralPath $processingRoot) -and
        @(Get-ChildItem -LiteralPath $processingRoot -Force).Count -ne 0) {
        throw 'Probe processing workspace was not cleaned.'
    }

    $packageProcesses = @(Get-PackageProcesses)
    if ($packageProcesses.Count -ne 1 -or $primary.Id -notin $packageProcesses.Id) {
        throw 'The package probe interfered with the running primary instance.'
    }

    if (-not $primary.CloseMainWindow()) {
        throw 'Primary native window did not accept CloseMainWindow.'
    }
    if (-not $primary.WaitForExit(10000)) {
        throw 'Primary process did not exit cleanly within 10 seconds.'
    }
    if (@(Get-PackageProcesses).Count -ne 0) {
        throw 'A package process remained after the main window closed.'
    }
    if (@(Get-PackageFfmpegProcesses).Count -ne 0) {
        throw 'A package-owned FFmpeg process remained after clean exit.'
    }

    if (-not (Test-Path -LiteralPath (Join-Path $tempRoot 'media-desktop.db') -PathType Leaf)) {
        throw 'Stage 3 database was not created under the smoke root.'
    }
    $logs = @(Get-ChildItem -LiteralPath (Join-Path $tempRoot 'logs') -File -Filter 'tracefold-*.log')
    if ($logs.Count -eq 0) {
        throw 'No rolling log was created under the smoke root.'
    }

    $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT = 'relative-invalid-smoke-root'
    $fatalLaunch = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden
    if (-not $fatalLaunch.WaitForExit(10000)) {
        throw 'Invalid configuration left a headless primary process running.'
    }
    if ($fatalLaunch.ExitCode -eq 0) {
        throw 'Invalid configuration did not produce a failing process exit code.'
    }

    Write-Output 'PASS: native window, single instance, offline startup, and clean exit verified'
    Write-Output 'PASS: Stage 3 package-local FFmpeg processing, preview, and ingest flow verified'
}
finally {
    @(Get-PackageFfmpegProcesses) |
        ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    @($probeProcess, $fatalLaunch, $secondary, $primary) |
        Where-Object { $null -ne $_ } |
        ForEach-Object {
            try {
                $_.Refresh()
                if (-not $_.HasExited -and
                    [IO.Path]::GetFullPath($_.Path) -eq $exePath) {
                    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                }
            }
            catch {
            }
        }
    if ($null -eq $oldDataRoot) {
        Remove-Item Env:\TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT = $oldDataRoot
    }
    $env:PATH = $oldPath

    Assert-DirectTempChild -Path $probeRoot -ExpectedName $probeName
    if ((Test-Path -LiteralPath $probeRoot) -and
        $probeName.StartsWith('tracefold-media-desktop-stage3-probe-', [StringComparison]::Ordinal)) {
        Assert-NoReparseTree -Path $probeRoot
        if (-not (Test-Path -LiteralPath $probeMarker -PathType Leaf) -or
            [IO.File]::ReadAllText($probeMarker) -ne $probeToken) {
            throw 'Probe cleanup ownership marker is missing or changed.'
        }
        Remove-Item -LiteralPath $probeRoot -Recurse -Force
    }
    Assert-DirectTempChild -Path $tempRoot -ExpectedName $smokeName
    if (Test-Path -LiteralPath $tempRoot) {
        Assert-NoReparseTree -Path $tempRoot
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
