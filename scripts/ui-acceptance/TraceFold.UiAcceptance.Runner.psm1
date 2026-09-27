Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Contracts.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Evidence.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Processes.psm1') -DisableNameChecking

function Invoke-UiAcceptanceRun {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ScenarioId,
        [Parameter(Mandatory)][scriptblock] $Scenario,
        [Parameter(Mandatory)][scriptblock] $Cleanup,
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][hashtable] $Metadata,
        [hashtable] $EvidenceCollectors,
        [string] $RunToken,
        [object[]] $OwnedProcessLedger,
        [string[]] $ApprovedDirectories,
        [string] $SupportPath,
        [scriptblock] $ProcessInventory,
        [scriptblock] $ProcessStop
    )
    foreach ($field in @('sourceCommit','productHash','productVersion','driverVersion','driverHash','fixtureHashes','hostOs','dpi','theme','viewport')) {
        if (-not $Metadata.ContainsKey($field)) { throw ('Missing run metadata: ' + $field) }
    }
    if (Test-Path -LiteralPath $OutputDirectory) { throw 'Run output already exists.' }
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputDirectory))
    if (-not [IO.Directory]::Exists($parent)) { throw 'Run output parent does not exist.' }
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    if ((Get-Item -LiteralPath $OutputDirectory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Run output may not be a reparse point.' }
    if (@([IO.Directory]::GetFileSystemEntries($OutputDirectory)).Count -ne 0) { throw 'Run output was not empty.' }
    $marker = [IO.File]::Open((Join-Path $OutputDirectory '.tracefold-ui-acceptance-report'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = [Text.Encoding]::ASCII.GetBytes('TraceFold-UI-Acceptance-Report-v1')
        $marker.Write($bytes, 0, $bytes.Length)
    }
    finally { $marker.Dispose() }
    $start = [DateTimeOffset]::UtcNow
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $status = 'NotRun'
    $reason = ''
    $refs = @()
    $cleanupStatus = 'NotRun'
    try {
        $outcome = & $Scenario
        if ($null -eq $outcome) { throw 'Scenario returned no outcome.' }
        Assert-UiAcceptanceStatus -Status ([string]$outcome.Status) -Field 'scenario status'
        $status = [string]$outcome.Status
        if ($outcome.PSObject.Properties['Reason']) { $reason = [string]$outcome.Reason }
    }
    catch {
        $status = if ($_.Exception.Data.Contains('UiStatus') -and $_.Exception.Data['UiStatus'] -ceq 'Blocked') { 'Blocked' } else { 'Failed' }
        $reason = $_.Exception.Message
    }
    finally {
        if ($status -cne 'Passed' -and $null -ne $EvidenceCollectors) {
            try {
                $evidenceOutput = Join-Path $OutputDirectory 'evidence'
                $captured = Write-UiFailureEvidence -OwnedRoot $OwnedRoot -OutputDirectory $evidenceOutput -ScenarioId $ScenarioId -StepId 'failure' -Collectors $EvidenceCollectors -RunToken $RunToken
                $refs = @($captured.PSObject.Properties | ForEach-Object { 'evidence/' + $_.Value })
            }
            catch {
                $reason += '; evidence capture failed: ' + $_.Exception.Message
            }
        }
        $cleanupErrors = @()
        try { & $Cleanup } catch { $cleanupErrors += $_.Exception.Message }
        if ($null -ne $OwnedProcessLedger) {
            try {
                if ($null -eq $ApprovedDirectories -or $ApprovedDirectories.Count -eq 0) { throw 'Approved process directories are required.' }
                $stopArgs = @{ Ledger=$OwnedProcessLedger; ApprovedDirectories=$ApprovedDirectories }
                if ($ProcessInventory) { $stopArgs.Inventory = $ProcessInventory }
                if ($ProcessStop) { $stopArgs.Stop = $ProcessStop }
                $null = Stop-OwnedProcessTree @stopArgs
            }
            catch { $cleanupErrors += $_.Exception.Message }
        }
        if (-not [string]::IsNullOrWhiteSpace($SupportPath)) {
            try {
                if ([string]::IsNullOrWhiteSpace($RunToken)) { throw 'Root delete requires a run token.' }
                $null = & $SupportPath root delete --root $OwnedRoot --token $RunToken
                if ($LASTEXITCODE -ne 0) { throw 'Owned root deletion was refused.' }
            }
            catch { $cleanupErrors += $_.Exception.Message }
        }
        $cleanupStatus = if ($cleanupErrors.Count -eq 0) { 'Passed' } else { 'Failed' }
        foreach ($errorText in $cleanupErrors) { $reason += '; cleanup failed: ' + $errorText }
    }
    $clock.Stop()
    $end = [DateTimeOffset]::UtcNow
    $safeReason = Protect-UiAcceptanceText -Value $reason -RunToken $RunToken -OwnedRoot $OwnedRoot
    $scenarioRecord = [ordered]@{
        id = (ConvertTo-UiAcceptanceSlug -Value $ScenarioId)
        status = $status
        durationMs = $clock.ElapsedMilliseconds
        steps = @([ordered]@{ id='scenario'; status=$status; durationMs=$clock.ElapsedMilliseconds; reason=$safeReason; evidencePaths=$refs })
    }
    $report = [ordered]@{
        acceptanceId = 'run-' + [guid]::NewGuid().ToString('N')
        checkpoint = '5CC'
        sourceCommit = [string]$Metadata.sourceCommit
        productHash = [string]$Metadata.productHash
        productVersion = [string]$Metadata.productVersion
        driverVersion = [string]$Metadata.driverVersion
        driverHash = [string]$Metadata.driverHash
        fixtureHashes = $Metadata.fixtureHashes
        hostOs = [string]$Metadata.hostOs
        dpi = $Metadata.dpi
        theme = [string]$Metadata.theme
        viewport = $Metadata.viewport
        startedUtc = $start.ToString('o')
        endedUtc = $end.ToString('o')
        durationMs = $clock.ElapsedMilliseconds
        scenarios = @($scenarioRecord)
        cleanup = [ordered]@{ status=$cleanupStatus }
        screenshotIndex = @($refs | Where-Object { $_ -like '*.png' })
        cleanVmStatus = 'NotRun'
    }
    $written = Write-UiAcceptanceReport -OutputDirectory $OutputDirectory -Report $report -RunToken $RunToken -OwnedRoot $OwnedRoot
    return [pscustomobject]@{
        Status = if ($cleanupStatus -cne 'Passed') { 'Failed' } else { $status }
        ExitCode = if ($status -ceq 'Passed' -and $cleanupStatus -ceq 'Passed') { 0 } else { 1 }
        ReportPath = $written.ReportPath
        CleanupStatus = $cleanupStatus
    }
}

Export-ModuleMember -Function 'Invoke-UiAcceptanceRun'
