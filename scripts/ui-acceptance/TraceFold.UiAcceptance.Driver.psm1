Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Contracts.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Processes.psm1') -DisableNameChecking
$script:mutationSteps = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)

function Test-UiForegroundProcess {
    param([int] $ProcessId)
    if ($ProcessId -le 0 -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return $false }
    if (-not ('TraceFoldUiForegroundProbe' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TraceFoldUiForegroundProbe {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
'@
    }
    $window = [TraceFoldUiForegroundProbe]::GetForegroundWindow()
    if ($window -eq [IntPtr]::Zero) { return $false }
    [uint32]$owner = 0
    [void][TraceFoldUiForegroundProbe]::GetWindowThreadProcessId($window, [ref]$owner)
    return $owner -eq [uint32]$ProcessId
}

function Reset-UiAcceptanceMutationSteps {
    $script:mutationSteps.Clear()
}

function Invoke-UiDriverProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $WinAppPath,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $TimeoutMs
    )
    if ($TimeoutMs -le 0) { throw 'Driver timeout must be positive.' }
    try { $target = (Resolve-Path -LiteralPath $WinAppPath -ErrorAction Stop).ProviderPath }
    catch { return [pscustomobject]@{ ExitCode=5; TimedOut=$false; Stdout=''; Stderr='driver unavailable' } }
    $start = New-Object System.Diagnostics.ProcessStartInfo
    if ([IO.Path]::GetExtension($target) -ieq '.ps1') {
        $start.FileName = (Get-Command pwsh -ErrorAction Stop).Source
        foreach ($item in @('-NoProfile', '-File', $target)) { [void] $start.ArgumentList.Add($item) }
    }
    else { $start.FileName = $target }
    foreach ($item in $Arguments) { [void] $start.ArgumentList.Add([string] $item) }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        try { [void] $process.Start() }
        catch { return [pscustomobject]@{ ExitCode=5; TimedOut=$false; Stdout=''; Stderr='driver could not start' } }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMs)) {
            try { $process.Kill($true) } catch { try { $process.Kill() } catch {} }
            [void] $process.WaitForExit(2000)
            return [pscustomobject]@{ ExitCode = 5; TimedOut = $true; Stdout = ''; Stderr = 'driver deadline exceeded' }
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            TimedOut = $false
            Stdout = $stdout.GetAwaiter().GetResult()
            Stderr = $stderr.GetAwaiter().GetResult()
        }
    }
    finally { $process.Dispose() }
}

function New-UiDriverResult {
    param($Raw, [int] $Attempts, [string] $RunToken, [string] $OwnedRoot)
    $safeError = Protect-UiAcceptanceText -Value ([string] $Raw.Stderr) -RunToken $RunToken -OwnedRoot $OwnedRoot
    $data = $null
    if (-not $Raw.TimedOut -and -not [string]::IsNullOrWhiteSpace($Raw.Stdout)) {
        try { $data = ConvertFrom-UiAcceptanceJson -Text $Raw.Stdout }
        catch { $safeError = 'driver returned invalid JSON'; $Raw.ExitCode = 10 }
    }
    elseif (-not $Raw.TimedOut -and $Raw.ExitCode -eq 0) {
        $safeError = 'driver returned empty JSON'
        $Raw.ExitCode = 10
    }
    $status = 'Passed'
    if ($Raw.TimedOut -or $Raw.ExitCode -in @(2,3,4,5)) { $status = 'Blocked' }
    elseif ($Raw.ExitCode -ne 0) { $status = 'Failed' }
    return [pscustomobject]@{
        Status = $status
        ExitCode = [int] $Raw.ExitCode
        Data = $data
        Error = $safeError
        Attempts = $Attempts
        TimedOut = [bool] $Raw.TimedOut
    }
}

function Invoke-WinAppObservation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][double] $TimeoutSeconds,
        [Parameter(Mandatory)][string] $WinAppPath,
        [string] $RunToken,
        [string] $OwnedRoot
    )
    if ($ProcessId -le 0 -or $TimeoutSeconds -le 0) { throw 'Invalid PID or deadline.' }
    if ($Arguments.Count -lt 2 -or $Arguments[0] -cne 'ui' -or
        $Arguments[1] -cnotin @('inspect','search','get-value','wait-for','screenshot')) {
        throw 'Only allowlisted UI observations may be retried.'
    }
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    $attempts = 0
    $result = [pscustomobject]@{ Status='Blocked'; ExitCode=5; Data=$null; Error='observation deadline elapsed'; Attempts=0; TimedOut=$true }
    do {
        $remaining = [int][Math]::Ceiling(($TimeoutSeconds * 1000) - $deadline.ElapsedMilliseconds)
        if ($remaining -le 0) { break }
        $argv = @($Arguments) + @('-a', [string] $ProcessId)
        if ($Arguments[1] -ceq 'wait-for') { $argv += @('--timeout', [string] $remaining) }
        $argv += '--json'
        $raw = Invoke-UiDriverProcess -WinAppPath $WinAppPath -Arguments $argv -TimeoutMs $remaining
        $attempts++
        $result = New-UiDriverResult -Raw $raw -Attempts $attempts -RunToken $RunToken -OwnedRoot $OwnedRoot
        if ($result.Status -eq 'Passed' -or $result.ExitCode -eq 2 -or $result.ExitCode -eq 10) { return $result }
        $pause = [int][Math]::Min(100, ($TimeoutSeconds * 1000) - $deadline.ElapsedMilliseconds)
        if ($pause -gt 0) { Start-Sleep -Milliseconds $pause }
    } while ($deadline.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    return $result
}

function Invoke-WinAppMutation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $StepId,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $WinAppPath,
        [int] $TimeoutMs = 5000,
        [string] $RunToken,
        [string] $OwnedRoot
    )
    if ([string]::IsNullOrWhiteSpace($StepId) -or $ProcessId -le 0) { throw 'Invalid step or PID.' }
    if ($Arguments.Count -lt 2 -or $Arguments[0] -cne 'ui' -or
        $Arguments[1] -cnotin @('invoke','set-value','scroll-into-view','send-keys')) {
        throw 'Unsupported UI mutation.'
    }
    if ($Arguments[1] -eq 'send-keys') { throw 'send-keys requires a separate foreground ownership gate.' }
    if (-not $script:mutationSteps.Add($StepId)) { throw 'A UI mutation step may be executed only once.' }
    $argv = @($Arguments) + @('-a', [string] $ProcessId, '--json')
    $raw = Invoke-UiDriverProcess -WinAppPath $WinAppPath -Arguments $argv -TimeoutMs $TimeoutMs
    return New-UiDriverResult -Raw $raw -Attempts 1 -RunToken $RunToken -OwnedRoot $OwnedRoot
}

function Invoke-WinAppKeyboard {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $StepId,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $WinAppPath,
        [scriptblock] $ForegroundCheck = { param($id) Test-UiForegroundProcess -ProcessId $id },
        [scriptblock] $DesktopCheck = { Test-UiInteractiveDesktop },
        [int] $TimeoutMs = 5000,
        [string] $RunToken,
        [string] $OwnedRoot
    )
    if ($Arguments.Count -lt 3 -or $Arguments[0] -cne 'ui' -or $Arguments[1] -cne 'send-keys') { throw 'Expected ui send-keys arguments.' }
    if (-not (& $DesktopCheck) -or -not (& $ForegroundCheck $ProcessId)) {
        return [pscustomobject]@{ Status='Blocked'; ExitCode=5; Data=$null; Error='foreground desktop ownership unavailable'; Attempts=0; TimedOut=$false }
    }
    if (-not $script:mutationSteps.Add($StepId)) { throw 'A UI mutation step may be executed only once.' }
    $argv = @($Arguments) + @('--via','send-input','-a',[string]$ProcessId,'--json')
    $raw = Invoke-UiDriverProcess -WinAppPath $WinAppPath -Arguments $argv -TimeoutMs $TimeoutMs
    return New-UiDriverResult -Raw $raw -Attempts 1 -RunToken $RunToken -OwnedRoot $OwnedRoot
}

Export-ModuleMember -Function @('Reset-UiAcceptanceMutationSteps','Invoke-UiDriverProcess','Invoke-WinAppObservation','Invoke-WinAppMutation','Invoke-WinAppKeyboard','Test-UiForegroundProcess')
