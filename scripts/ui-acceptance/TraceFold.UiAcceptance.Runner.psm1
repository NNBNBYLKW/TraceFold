Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Contracts.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Evidence.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Processes.psm1') -DisableNameChecking

# The scenario registry is the only source of scenario names and their entry files. Order is
# fixed: it is the execution order an 'All' request produces.
$script:uiAcceptanceScenarios = [ordered] @{
    StartupLayout = '00-startup-layout.ps1'
    MainChain     = '10-main-chain.ps1'
    # Each conflict decision is its own scenario. The aggregate 'Conflict' entry still points at a
    # script no task creates, so it must never be read as covering all three decisions.
    ConflictKeepBoth = '20-conflict-keep-both.ps1'
    ConflictSkip  = '22-conflict-skip.ps1'
    Conflict      = '02-conflict.ps1'
    Library       = '03-library.ps1'
    Taxonomy      = '04-taxonomy.ps1'
    Diagnostics   = '60-diagnostics-read-only.ps1'
    Visual        = '06-visual.ps1'
}

function Get-UiAcceptanceScenarioName {
    [CmdletBinding()]
    param()

    return @($script:uiAcceptanceScenarios.Keys)
}

function Get-UiAcceptanceScenarioFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Name
    )

    # OrderedDictionary exposes Contains, not ContainsKey, and compares ordinal by default.
    if (-not $script:uiAcceptanceScenarios.Contains($Name)) {
        throw ('Unknown scenario: ' + $Name + '.')
    }

    return [string] $script:uiAcceptanceScenarios[$Name]
}

function Resolve-UiAcceptanceScenarioRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Requested
    )

    $resolved = New-Object 'System.Collections.Generic.List[string]'
    foreach ($candidate in @($Requested)) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { throw 'A requested scenario is empty.' }
        if ($candidate -ceq 'All') {
            foreach ($name in $script:uiAcceptanceScenarios.Keys) {
                if (-not $resolved.Contains($name)) { [void] $resolved.Add($name) }
            }
            continue
        }
        if (-not $script:uiAcceptanceScenarios.Contains($candidate)) {
            throw ('Unknown scenario: ' + $candidate + '.')
        }
        if (-not $resolved.Contains($candidate)) { [void] $resolved.Add($candidate) }
    }

    if ($resolved.Count -eq 0) { throw 'At least one scenario must be requested.' }
    return $resolved.ToArray()
}

<#
.SYNOPSIS
    Reads the declared parameter block of a scenario entry file.

.DESCRIPTION
    A dispatcher must never guess which host seams an entry understands: it hands over only
    the parameters that entry declares. A .ps1 file's parameter block lives on the script
    block AST, so it is parsed explicitly rather than reflected over a command.
#>
function Get-UiAcceptanceEntryParameterDetail {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path
    )

    if (-not [IO.File]::Exists($Path)) { throw ('The entry script is missing: ' + $Path) }
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref] $tokens, [ref] $parseErrors)
    if ($parseErrors.Count -gt 0) { throw ('The entry script does not parse: ' + $Path) }

    $details = @()
    $property = $ast.PSObject.Properties['ParamBlock']
    if ($null -eq $property -or $null -eq $property.Value) { return $details }
    foreach ($parameter in $property.Value.Parameters) {
        $mandatory = $false
        foreach ($attribute in @($parameter.Attributes)) {
            if ($attribute -is [System.Management.Automation.Language.AttributeAst]) {
                foreach ($named in @($attribute.NamedArguments)) {
                    if ($named.ArgumentName -ceq 'Mandatory') { $mandatory = $true }
                }
            }
        }
        $details += [pscustomobject] @{
            Name      = $parameter.Name.VariablePath.UserPath
            Mandatory = $mandatory
        }
    }

    # Intentionally not ',': every caller iterates with foreach, and an empty parameter block
    # must iterate zero times rather than once over an empty array.
    return $details
}

function Get-UiAcceptanceEntryParameter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path
    )

    $names = New-Object 'System.Collections.Generic.List[string]'
    foreach ($detail in (Get-UiAcceptanceEntryParameterDetail -Path $Path)) {
        [void] $names.Add([string] $detail.Name)
    }

    return $names.ToArray()
}

<#
.SYNOPSIS
    Keeps only the host seams an entry actually declares.

.DESCRIPTION
    Extra candidates are dropped silently: the candidate set is the runner's whole support
    surface, and an entry that does not declare a seam must not receive it. A declared
    mandatory parameter with no candidate is a hard failure, never a prompt.
#>
function New-UiAcceptanceEntryArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][hashtable] $Candidates
    )

    $filtered = @{}
    $missing = @()
    foreach ($detail in (Get-UiAcceptanceEntryParameterDetail -Path $Path)) {
        if ($Candidates.ContainsKey([string] $detail.Name)) {
            $filtered[[string] $detail.Name] = $Candidates[[string] $detail.Name]
        }
        elseif ($detail.Mandatory) {
            $missing += [string] $detail.Name
        }
    }

    if ($missing.Count -gt 0) {
        throw ('Missing entry arguments: ' + ($missing -join ', ') + '.')
    }

    return ,$filtered
}

function Invoke-UiAcceptanceScenario {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][hashtable] $Arguments
    )

    $filtered = New-UiAcceptanceEntryArguments -Path $Path -Candidates $Arguments
    $outcome = & $Path @filtered
    if ($null -eq $outcome) { throw 'The scenario returned no outcome.' }
    Assert-UiAcceptanceStatus -Status ([string] $outcome.Status) -Field 'scenario status'
    return $outcome
}

<#
.SYNOPSIS
    Creates a fresh owned run root through the support tool, never by hand.

.DESCRIPTION
    The support tool owns the ownership rules, so the root is created by it and then verified
    to be exactly where it was requested: a fresh leaf under the canonical temporary directory
    named after the run id.
#>
function New-UiAcceptanceRunRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $SupportPath,
        [Parameter(Mandatory)][string] $ParentDirectory,
        [Parameter(Mandatory)][guid] $RunId,
        [Parameter(Mandatory)][string] $RunToken
    )

    if ([string]::IsNullOrWhiteSpace($RunToken)) { throw 'A run token is required.' }
    if ($RunId -eq [guid]::Empty) { throw 'A non-empty run id is required.' }
    $parent = [IO.Path]::GetFullPath($ParentDirectory)
    if (-not [IO.Directory]::Exists($parent)) { throw 'The owned root parent does not exist.' }

    $raw = & $SupportPath root create --parent $parent --run-id $RunId.ToString('D') --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'Owned root creation was refused.' }
    $created = ($raw | Out-String | ConvertFrom-Json)
    Assert-UiAcceptanceFields -InputObject $created -Required @('root')

    $root = [IO.Path]::GetFullPath([string] $created.root)
    $expected = [IO.Path]::GetFullPath((Join-Path $parent ('TraceFold-UIA-' + $RunId.ToString('N'))))
    if ($root -ine $expected) { throw 'The created owned root is not where it was requested.' }
    if (-not [IO.Directory]::Exists($root)) { throw 'The created owned root is missing.' }

    return $root
}

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
    $viewports = @()
    $cleanupStatus = 'NotRun'
    try {
        $outcome = & $Scenario
        if ($null -eq $outcome) { throw 'Scenario returned no outcome.' }
        Assert-UiAcceptanceStatus -Status ([string]$outcome.Status) -Field 'scenario status'
        $status = [string]$outcome.Status
        if ($outcome.PSObject.Properties['Reason']) { $reason = [string]$outcome.Reason }
        if ($outcome.PSObject.Properties['Artifacts']) {
            foreach ($artifact in @($outcome.Artifacts)) { $refs += [string]$artifact }
        }
        if ($outcome.PSObject.Properties['Viewports']) {
            $viewports = @($outcome.Viewports)
        }
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
        viewports = $viewports
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

Export-ModuleMember -Function @(
    'Invoke-UiAcceptanceRun',
    'Get-UiAcceptanceScenarioName',
    'Get-UiAcceptanceScenarioFile',
    'Resolve-UiAcceptanceScenarioRequest',
    'Get-UiAcceptanceEntryParameter',
    'Get-UiAcceptanceEntryParameterDetail',
    'New-UiAcceptanceEntryArguments',
    'Invoke-UiAcceptanceScenario',
    'New-UiAcceptanceRunRoot'
)
