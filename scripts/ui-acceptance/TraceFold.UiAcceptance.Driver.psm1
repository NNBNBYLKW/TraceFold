Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Contracts.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Processes.psm1') -DisableNameChecking
$script:mutationSteps = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$script:lastDriverResult = $null

function Set-UiDriverLastResult {
    param(
        [Parameter(Mandatory)][string] $WinAppPath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Arguments,
        [Parameter(Mandatory)] $Result,
        [string] $RunToken = '',
        [string] $OwnedRoot = ''
    )

    $exitCode = $null
    if ($null -ne $Result.PSObject.Properties['ExitCode'] -and $null -ne $Result.ExitCode) {
        $exitCode = [int] $Result.ExitCode
    }
    $timedOut = $null
    if ($null -ne $Result.PSObject.Properties['TimedOut'] -and $null -ne $Result.TimedOut) {
        $timedOut = [bool] $Result.TimedOut
    }
    $attempts = $null
    if ($null -ne $Result.PSObject.Properties['Attempts'] -and $null -ne $Result.Attempts) {
        $attempts = [int] $Result.Attempts
    }
    $errorText = ''
    if ($null -ne $Result.PSObject.Properties['Error']) {
        $errorText = Protect-UiAcceptanceText -Value ([string] $Result.Error) -RunToken $RunToken -OwnedRoot $OwnedRoot
    }
    $safeArguments = @()
    foreach ($argument in @($Arguments)) {
        $safeArguments += Protect-UiAcceptanceText -Value ([string] $argument) -RunToken $RunToken -OwnedRoot $OwnedRoot
    }
    $script:lastDriverResult = [pscustomobject] @{
        Status        = [string] $Result.Status
        ExitCode      = $exitCode
        TimedOut      = $timedOut
        Attempts      = $attempts
        Error         = $errorText
        Arguments     = [string[]] $safeArguments
        ExecutableName = [IO.Path]::GetFileName($WinAppPath)
        ObservedUtc   = [DateTimeOffset]::UtcNow.ToString('o')
    }
}

function Reset-UiDriverLastResult {
    [CmdletBinding()]
    param()
    $script:lastDriverResult = $null
}

function Get-UiDriverLastResult {
    [CmdletBinding()]
    param()
    if ($null -eq $script:lastDriverResult) { return $null }
    $record = $script:lastDriverResult
    return [pscustomobject] @{
        Status        = [string] $record.Status
        ExitCode      = $record.ExitCode
        TimedOut      = $record.TimedOut
        Attempts      = $record.Attempts
        Error         = [string] $record.Error
        Arguments     = [string[]] @($record.Arguments)
        ExecutableName = [string] $record.ExecutableName
        ObservedUtc   = [string] $record.ObservedUtc
    }
}

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

<#
.SYNOPSIS
    Quotes one argument for a hand-built Windows command line.

.DESCRIPTION
    Windows PowerShell 5.1 runs on a .NET whose ProcessStartInfo has no ArgumentList, so the
    command line must be built by hand. The escaping follows the CommandLineToArgvW rules
    exactly, otherwise an argument boundary would silently move and the driver would receive a
    different argument vector on one runtime than on the other.
#>
function ConvertTo-UiDriverArgument {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string] $Value
    )

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }

    $backslash = [char] 0x5C
    $quote = [char] 0x22
    $builder = New-Object System.Text.StringBuilder
    [void] $builder.Append($quote)
    $pending = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq $backslash) { $pending++; continue }
        if ($character -eq $quote) {
            [void] $builder.Append($backslash, ($pending * 2) + 1)
            [void] $builder.Append($quote)
            $pending = 0
            continue
        }
        if ($pending -gt 0) {
            [void] $builder.Append($backslash, $pending)
            $pending = 0
        }
        [void] $builder.Append($character)
    }
    if ($pending -gt 0) { [void] $builder.Append($backslash, $pending * 2) }
    [void] $builder.Append($quote)
    return $builder.ToString()
}

function Set-UiDriverArgumentList {
    param(
        [Parameter(Mandatory)] $StartInfo,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Arguments
    )

    $property = $StartInfo.PSObject.Properties['ArgumentList']
    if ($null -ne $property) {
        foreach ($item in @($Arguments)) { [void] $property.Value.Add([string] $item) }
        return
    }

    $quoted = @()
    foreach ($item in @($Arguments)) { $quoted += (ConvertTo-UiDriverArgument -Value ([string] $item)) }
    $StartInfo.Arguments = ($quoted -join ' ')
}

function Invoke-UiDriverProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $WinAppPath,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][int] $TimeoutMs,
        [string] $RunToken = '',
        [string] $OwnedRoot = ''
    )
    if ($TimeoutMs -le 0) { throw 'Driver timeout must be positive.' }
    try { $target = (Resolve-Path -LiteralPath $WinAppPath -ErrorAction Stop).ProviderPath }
    catch {
        $result = [pscustomobject]@{ Status='Blocked'; ExitCode=5; TimedOut=$false; Stdout=''; Stderr='driver unavailable'; Attempts=0; Error='driver unavailable'; Data=$null }
        Set-UiDriverLastResult -WinAppPath $WinAppPath -Arguments $Arguments -Result $result -RunToken $RunToken -OwnedRoot $OwnedRoot
        return $result
    }
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $commandArguments = New-Object 'System.Collections.Generic.List[string]'
    if ([IO.Path]::GetExtension($target) -ieq '.ps1') {
        $start.FileName = (Get-Command pwsh -ErrorAction Stop).Source
        foreach ($item in @('-NoProfile', '-File', $target)) { [void] $commandArguments.Add($item) }
    }
    else { $start.FileName = $target }
    foreach ($item in $Arguments) { [void] $commandArguments.Add([string] $item) }
    Set-UiDriverArgumentList -StartInfo $start -Arguments $commandArguments.ToArray()
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        try { [void] $process.Start() }
        catch {
            $result = [pscustomobject]@{ Status='Blocked'; ExitCode=5; TimedOut=$false; Stdout=''; Stderr='driver could not start'; Attempts=0; Error='driver could not start'; Data=$null }
            Set-UiDriverLastResult -WinAppPath $WinAppPath -Arguments $Arguments -Result $result -RunToken $RunToken -OwnedRoot $OwnedRoot
            return $result
        }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMs)) {
            try { $process.Kill($true) } catch { try { $process.Kill() } catch {} }
            [void] $process.WaitForExit(2000)
            $result = [pscustomobject]@{ Status='Blocked'; ExitCode=5; TimedOut=$true; Stdout=''; Stderr='driver deadline exceeded'; Attempts=0; Error='driver deadline exceeded'; Data=$null }
            Set-UiDriverLastResult -WinAppPath $WinAppPath -Arguments $Arguments -Result $result -RunToken $RunToken -OwnedRoot $OwnedRoot
            return $result
        }
        $result = [pscustomobject]@{
            ExitCode = $process.ExitCode
            TimedOut = $false
            Stdout = $stdout.GetAwaiter().GetResult()
            Stderr = $stderr.GetAwaiter().GetResult()
        }
        $projected = New-UiDriverResult -Raw $result -Attempts 1 -RunToken $RunToken -OwnedRoot $OwnedRoot
        Set-UiDriverLastResult -WinAppPath $target -Arguments $Arguments -Result $projected -RunToken $RunToken -OwnedRoot $OwnedRoot
        return $result
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
        $raw = Invoke-UiDriverProcess -WinAppPath $WinAppPath -Arguments $argv -TimeoutMs $remaining -RunToken $RunToken -OwnedRoot $OwnedRoot
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
    if (-not $script:mutationSteps.Add($StepId)) { throw ('A UI mutation step may be executed only once: ' + $StepId) }
    $argv = @($Arguments) + @('-a', [string] $ProcessId, '--json')
    $raw = Invoke-UiDriverProcess -WinAppPath $WinAppPath -Arguments $argv -TimeoutMs $TimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
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
        $result = [pscustomobject]@{ Status='Blocked'; ExitCode=5; Data=$null; Error='foreground desktop ownership unavailable'; Attempts=0; TimedOut=$false }
        Set-UiDriverLastResult -WinAppPath $WinAppPath -Arguments $Arguments -Result $result -RunToken $RunToken -OwnedRoot $OwnedRoot
        return $result
    }
    if (-not $script:mutationSteps.Add($StepId)) { throw ('A UI mutation step may be executed only once: ' + $StepId) }
    $argv = @($Arguments) + @('--via','send-input','-a',[string]$ProcessId,'--json')
    $raw = Invoke-UiDriverProcess -WinAppPath $WinAppPath -Arguments $argv -TimeoutMs $TimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
    return New-UiDriverResult -Raw $raw -Attempts 1 -RunToken $RunToken -OwnedRoot $OwnedRoot
}

Export-ModuleMember -Function @('Reset-UiAcceptanceMutationSteps','Reset-UiDriverLastResult','Get-UiDriverLastResult','Invoke-UiDriverProcess','Invoke-WinAppObservation','Invoke-WinAppMutation','Invoke-WinAppKeyboard','Test-UiForegroundProcess')
