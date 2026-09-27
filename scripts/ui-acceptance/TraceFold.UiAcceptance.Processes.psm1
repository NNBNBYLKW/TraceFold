Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-UiInteractiveDesktop {
    if (-not [Environment]::UserInteractive -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return $false }
    if (-not ('TraceFoldUiDesktopProbe' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TraceFoldUiDesktopProbe {
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool GetUserObjectInformation(IntPtr handle, int index, IntPtr buffer, int length, out int required);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool CloseDesktop(IntPtr handle);
}
'@
    }
    $desktop = [TraceFoldUiDesktopProbe]::OpenInputDesktop(0, $false, 1)
    if ($desktop -eq [IntPtr]::Zero) { return $false }
    try {
        $needed = 0
        [void][TraceFoldUiDesktopProbe]::GetUserObjectInformation($desktop, 2, [IntPtr]::Zero, 0, [ref]$needed)
        if ($needed -lt 2 -or $needed -gt 1024) { return $false }
        $buffer = [Runtime.InteropServices.Marshal]::AllocHGlobal($needed)
        try {
            if (-not [TraceFoldUiDesktopProbe]::GetUserObjectInformation($desktop, 2, $buffer, $needed, [ref]$needed)) { return $false }
            return [Runtime.InteropServices.Marshal]::PtrToStringUni($buffer) -ceq 'Default'
        }
        finally { [Runtime.InteropServices.Marshal]::FreeHGlobal($buffer) }
    }
    finally { [void][TraceFoldUiDesktopProbe]::CloseDesktop($desktop) }
}

function Test-UiPathWithin {
    param([string] $Path, [string] $Directory)
    $full = [IO.Path]::GetFullPath($Path)
    $directoryPath = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $base = $directoryPath + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($base, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.File]::Exists($full)) { return $false }
    $cursor = $full
    while ($null -ne $cursor) {
        if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $true
}

function Assert-TraceFoldPreflight {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ExecutablePath,
        [Parameter(Mandatory)][string] $DriverPath,
        [Parameter(Mandatory)][string] $DriverManifestPath,
        [Parameter(Mandatory)][string] $DriverArchivePath,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [string] $ExtractedDriverManifestPath,
        [string] $ExpectedProductHash,
        [string] $ExistingRunRoot,
        [scriptblock] $HostCheck = { [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT -and [Environment]::Is64BitOperatingSystem -and [Environment]::OSVersion.Version.Build -ge 22000 },
        [scriptblock] $DesktopCheck = { Test-UiInteractiveDesktop },
        [scriptblock] $OrdinaryProcessCheck = { @(Get-Process -Name 'TraceFold.Media' -ErrorAction SilentlyContinue).Count -gt 0 }
    )
    if (-not (& $HostCheck)) { return [pscustomobject]@{ Status='Blocked'; Reason='unsupported host' } }
    if (-not (& $DesktopCheck)) { return [pscustomobject]@{ Status='Blocked'; Reason='interactive desktop unavailable' } }
    if (& $OrdinaryProcessCheck) { return [pscustomobject]@{ Status='Blocked'; Reason='ordinary product process is running' } }
    if (-not [string]::IsNullOrWhiteSpace($ExistingRunRoot) -and (Test-Path -LiteralPath $ExistingRunRoot)) {
        return [pscustomobject]@{ Status='Blocked'; Reason='a run root already exists' }
    }
    if (-not [IO.File]::Exists($ExecutablePath) -or [IO.Path]::GetFileName($ExecutablePath) -ine 'TraceFold.Media.exe') {
        return [pscustomobject]@{ Status='Blocked'; Reason='published product executable unavailable' }
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedProductHash) -and
        (Get-FileHash -LiteralPath $ExecutablePath -Algorithm SHA256).Hash -cne $ExpectedProductHash) {
        return [pscustomobject]@{ Status='Blocked'; Reason='published product identity mismatch' }
    }
    if (-not [IO.File]::Exists($DriverPath) -or -not [IO.File]::Exists($DriverManifestPath)) {
        return [pscustomobject]@{ Status='Blocked'; Reason='pinned driver archive unavailable' }
    }
    $manifest = Get-Content -LiteralPath $DriverManifestPath -Raw | ConvertFrom-Json
    if ($manifest.version -cne '0.5.0' -or $manifest.architecture -cne 'x64') {
        return [pscustomobject]@{ Status='Blocked'; Reason='pinned driver archive mismatch' }
    }
    $driverVerified = $false
    if ([IO.File]::Exists($DriverArchivePath)) {
        $driverVerified = (Get-Item -LiteralPath $DriverArchivePath).Length -eq [long] $manifest.archiveSize -and
            (Get-FileHash -LiteralPath $DriverArchivePath -Algorithm SHA256).Hash -ceq $manifest.sha256
    }
    elseif (-not [string]::IsNullOrWhiteSpace($ExtractedDriverManifestPath) -and [IO.File]::Exists($ExtractedDriverManifestPath)) {
        $extracted = Get-Content -LiteralPath $ExtractedDriverManifestPath -Raw | ConvertFrom-Json
        $driverVerified = $extracted.version -ceq $manifest.version -and
            $extracted.sha256 -ceq (Get-FileHash -LiteralPath $DriverPath -Algorithm SHA256).Hash
    }
    if (-not $driverVerified) { return [pscustomobject]@{ Status='Blocked'; Reason='pinned driver archive mismatch' } }
    if (Test-Path -LiteralPath $OutputDirectory) {
        return [pscustomobject]@{ Status='Blocked'; Reason='output directory already exists' }
    }
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputDirectory))
    if (-not [IO.Directory]::Exists($parent)) {
        return [pscustomobject]@{ Status='Blocked'; Reason='output parent unavailable' }
    }
    $test = Join-Path $parent ([guid]::NewGuid().ToString('N') + '.writecheck')
    try {
        $stream = [IO.File]::Open($test, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $stream.Dispose()
    }
    catch { return [pscustomobject]@{ Status='Blocked'; Reason='output parent not writable' } }
    finally { if ([IO.File]::Exists($test)) { [IO.File]::Delete($test) } }
    $tempCheck = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N') + '.tracefold-check')
    try {
        $stream = [IO.File]::Open($tempCheck, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $stream.Dispose()
    }
    catch { return [pscustomobject]@{ Status='Blocked'; Reason='system temporary directory not writable' } }
    finally { if ([IO.File]::Exists($tempCheck)) { [IO.File]::Delete($tempCheck) } }
    return [pscustomobject]@{ Status='Passed'; Reason='' }
}

function Start-TraceFoldScenario {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ExecutablePath,
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $RunToken,
        [Parameter(Mandatory)][string] $SupportPath,
        [scriptblock] $StartProcess
    )
    $exe = (Resolve-Path -LiteralPath $ExecutablePath).ProviderPath
    if ([IO.Path]::GetFileName($exe) -ine 'TraceFold.Media.exe') { throw 'Unexpected product executable.' }
    if ([string]::IsNullOrWhiteSpace($RunToken)) { throw 'Run token is required.' }
    $root = (Resolve-Path -LiteralPath $OwnedRoot).ProviderPath
    $validation = & $SupportPath root validate --root $root --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'Scenario root ownership validation failed.' }
    $validated = ($validation | Out-String | ConvertFrom-Json)
    if ($validated.owned -ne $true) { throw 'Scenario root ownership validation failed.' }
    $childEnv = @{ TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT = $root }
    if ($StartProcess) {
        $record = & $StartProcess $exe $childEnv
    }
    else {
        $info = New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName = $exe
        $info.UseShellExecute = $false
        $info.Environment['TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT'] = $root
        $process = [Diagnostics.Process]::Start($info)
        $record = [pscustomobject]@{ ProcessId=$process.Id; StartUtcTicks=$process.StartTime.ToUniversalTime().Ticks; ExecutablePath=$exe }
        $process.Dispose()
    }
    if ([int] $record.ProcessId -le 0 -or [long] $record.StartUtcTicks -le 0 -or
        -not [IO.Path]::IsPathRooted([string] $record.ExecutablePath) -or
        [IO.Path]::GetFullPath($record.ExecutablePath) -ine $exe) { throw 'Invalid launched process identity.' }
    $ledger = Join-Path $root 'owned-processes.txt'
    $line = '{0}|{1}|{2}' -f $record.ProcessId, $record.StartUtcTicks, $exe
    [IO.File]::AppendAllText($ledger, $line + [Environment]::NewLine)
    return [pscustomobject]@{ ProcessId=[int]$record.ProcessId; StartUtcTicks=[long]$record.StartUtcTicks; ExecutablePath=$exe; OwnedRoot=$root }
}

function Register-OwnedToolDescendants {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $RunToken,
        [Parameter(Mandatory)][string] $SupportPath,
        [Parameter(Mandatory)][object[]] $Ledger,
        [Parameter(Mandatory)][string[]] $ApprovedDirectories,
        [scriptblock] $Inventory = {
            $records = @()
            foreach ($item in @(Get-CimInstance Win32_Process -ErrorAction Stop)) {
                try {
                    $p = [Diagnostics.Process]::GetProcessById([int]$item.ProcessId)
                    try {
                        $records += [pscustomobject]@{
                            ProcessId=$p.Id; ParentProcessId=[int]$item.ParentProcessId
                            StartUtcTicks=$p.StartTime.ToUniversalTime().Ticks
                            ExecutablePath=$p.MainModule.FileName
                        }
                    }
                    finally { $p.Dispose() }
                }
                catch {}
            }
            return $records
        }
    )
    $root = (Resolve-Path -LiteralPath $OwnedRoot).ProviderPath
    $null = & $SupportPath root validate --root $root --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'Owned root validation failed.' }
    $all = @(& $Inventory)
    $verified = @{}
    foreach ($owned in $Ledger) {
        $match = @($all | Where-Object { $_.ProcessId -eq $owned.ProcessId -and
            $_.StartUtcTicks -eq $owned.StartUtcTicks -and
            [IO.Path]::GetFullPath([string]$_.ExecutablePath) -ieq [IO.Path]::GetFullPath([string]$owned.ExecutablePath) })
        if ($match.Count -eq 1) { $verified[[int]$owned.ProcessId] = $true }
    }
    $added = @()
    $progress = $true
    while ($progress) {
        $progress = $false
        foreach ($item in $all) {
            if ($verified.ContainsKey([int]$item.ProcessId) -or -not $verified.ContainsKey([int]$item.ParentProcessId)) { continue }
            if ([IO.Path]::GetFileName([string]$item.ExecutablePath) -inotmatch '^(ffmpeg|ffprobe)\.exe$') { continue }
            $approved = $false
            foreach ($directory in $ApprovedDirectories) {
                if (Test-UiPathWithin -Path $item.ExecutablePath -Directory $directory) { $approved = $true; break }
            }
            if (-not $approved -or [long]$item.StartUtcTicks -le 0) { continue }
            $verified[[int]$item.ProcessId] = $true
            $entry = [pscustomobject]@{ ProcessId=[int]$item.ProcessId; StartUtcTicks=[long]$item.StartUtcTicks; ExecutablePath=[IO.Path]::GetFullPath([string]$item.ExecutablePath) }
            $added += $entry
            [IO.File]::AppendAllText((Join-Path $root 'owned-processes.txt'), ('{0}|{1}|{2}' -f $entry.ProcessId,$entry.StartUtcTicks,$entry.ExecutablePath) + [Environment]::NewLine)
            $progress = $true
        }
    }
    return $added
}

function Stop-OwnedProcessTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]] $Ledger,
        [Parameter(Mandatory)][string[]] $ApprovedDirectories,
        [scriptblock] $Inventory = {
            param($id)
            try {
                $p = [Diagnostics.Process]::GetProcessById($id)
                try { [pscustomobject]@{ ProcessId=$p.Id; StartUtcTicks=$p.StartTime.ToUniversalTime().Ticks; ExecutablePath=$p.MainModule.FileName } }
                finally { $p.Dispose() }
            } catch { $null }
        },
        [scriptblock] $Stop = {
            param($id)
            $p = [Diagnostics.Process]::GetProcessById($id)
            try {
                if (-not $p.HasExited) {
                    [void]$p.CloseMainWindow()
                    if (-not $p.WaitForExit(1500) -and -not $p.HasExited) {
                        # Kill only this verified PID. An unrecorded descendant may belong to the user.
                        $p.Kill()
                        [void]$p.WaitForExit(3000)
                    }
                }
            }
            finally { $p.Dispose() }
        }
    )
    $stopped = @()
    foreach ($item in @($Ledger)) {
        if ([int]$item.ProcessId -le 0 -or [long]$item.StartUtcTicks -le 0) { continue }
        $path = [string]$item.ExecutablePath
        if (-not [IO.Path]::IsPathRooted($path)) { continue }
        $approved = $false
        foreach ($directory in $ApprovedDirectories) {
            if (Test-UiPathWithin -Path $path -Directory $directory) { $approved = $true; break }
        }
        if (-not $approved) { continue }
        $current = & $Inventory ([int]$item.ProcessId)
        if ($null -eq $current) { continue }
        if ([int]$current.ProcessId -ne [int]$item.ProcessId -or
            [long]$current.StartUtcTicks -ne [long]$item.StartUtcTicks -or
            [IO.Path]::GetFullPath([string]$current.ExecutablePath) -ine [IO.Path]::GetFullPath($path)) { continue }
        & $Stop ([int]$item.ProcessId)
        $stopped += [int]$item.ProcessId
    }
    return $stopped
}

Export-ModuleMember -Function @('Test-UiInteractiveDesktop','Assert-TraceFoldPreflight','Start-TraceFoldScenario','Register-OwnedToolDescendants','Stop-OwnedProcessTree')
