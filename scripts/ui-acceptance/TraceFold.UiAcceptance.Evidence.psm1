Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Contracts.psm1') -DisableNameChecking

function Assert-UiEvidenceDirectory {
    param([string] $Path, [string] $OwnedRoot)
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence destination is outside the owned root.' }
    if (-not [IO.Directory]::Exists($OwnedRoot)) { throw 'Owned root is absent.' }
    [void][IO.Directory]::CreateDirectory($full)
}

function Assert-UiOwnedEvidenceFile {
    param([string] $Path, [string] $OwnedRoot)
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence source is outside the owned root.' }
    if (-not [IO.File]::Exists($full)) { throw 'Evidence source is absent.' }
    if ((Get-Item -LiteralPath $OwnedRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Owned evidence root is a reparse point.' }
    $cursor = $full
    while ($cursor.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Evidence source contains a reparse point.' }
        $cursor = Split-Path -Parent $cursor
    }
}

function Assert-UiPngBytes {
    param([byte[]] $Bytes)
    $signature = [byte[]]@(137,80,78,71,13,10,26,10)
    if ($Bytes.Length -lt 45 -or $Bytes.Length -gt 52428800) { throw 'Invalid screenshot PNG size.' }
    for ($i=0; $i -lt 8; $i++) { if ($Bytes[$i] -ne $signature[$i]) { throw 'Invalid screenshot PNG.' } }
    $offset = 8
    $first = $true
    $ended = $false
    while ($offset -lt $Bytes.Length) {
        if ($Bytes.Length - $offset -lt 12) { throw 'Truncated screenshot PNG.' }
        [uint64]$length = ([uint64]$Bytes[$offset] -shl 24) -bor ([uint64]$Bytes[$offset+1] -shl 16) -bor
            ([uint64]$Bytes[$offset+2] -shl 8) -bor [uint64]$Bytes[$offset+3]
        $type = [Text.Encoding]::ASCII.GetString($Bytes, $offset+4, 4)
        if ($length -gt ($Bytes.Length - $offset - 12)) { throw 'Invalid screenshot PNG chunk length.' }
        if ($first -and ($type -cne 'IHDR' -or $length -ne 13)) { throw 'Screenshot PNG is missing IHDR.' }
        $first = $false
        $offset += [int]$length + 12
        if ($type -ceq 'IEND') {
            if ($length -ne 0 -or $offset -ne $Bytes.Length) { throw 'Invalid screenshot PNG trailer.' }
            $ended = $true
            break
        }
    }
    if (-not $ended) { throw 'Screenshot PNG is missing IEND.' }
}

function ConvertTo-UiSafeValue {
    param($Value, [string] $RunToken, [string] $OwnedRoot)
    if ($null -eq $Value) { return $null }
    if ($Value -is [string]) { return (Protect-UiAcceptanceText -Value $Value -RunToken $RunToken -OwnedRoot $OwnedRoot) }
    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in $Value.Keys) {
            if (Test-UiAcceptanceDangerousKey -Key ([string]$key)) { throw 'Evidence contains a forbidden key.' }
            $result[[string]$key] = ConvertTo-UiSafeValue -Value $Value[$key] -RunToken $RunToken -OwnedRoot $OwnedRoot
        }
        return $result
    }
    if ($Value -is [pscustomobject]) {
        $result = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            if (Test-UiAcceptanceDangerousKey -Key $property.Name) { throw 'Evidence contains a forbidden key.' }
            $result[$property.Name] = ConvertTo-UiSafeValue -Value $property.Value -RunToken $RunToken -OwnedRoot $OwnedRoot
        }
        return $result
    }
    if ($Value -is [array]) {
        $items = @()
        foreach ($item in $Value) { $items += ,(ConvertTo-UiSafeValue -Value $item -RunToken $RunToken -OwnedRoot $OwnedRoot) }
        return ,$items
    }
    return $Value
}

function Write-UiFailureEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string] $ScenarioId,
        [Parameter(Mandatory)][string] $StepId,
        [Parameter(Mandatory)][hashtable] $Collectors,
        [string] $RunToken
    )
    $required = @('screenshot','uiaTree','lastCommand','productLog','stateSnapshot','processLedger')
    foreach ($key in $required) { if (-not $Collectors.ContainsKey($key)) { throw ('Missing evidence collector: ' + $key) } }
    $slug = ConvertTo-UiAcceptanceSlug -Value ($ScenarioId + '-' + $StepId)
    $temp = Join-Path $OwnedRoot ('evidence-' + $slug)
    Assert-UiEvidenceDirectory -Path $temp -OwnedRoot $OwnedRoot
    if (Test-Path -LiteralPath $OutputDirectory) { throw 'Durable evidence output already exists.' }
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    $references = [ordered]@{}
    foreach ($key in $required) {
        try {
            $value = & $Collectors[$key]
            if ($key -eq 'screenshot') {
            $source = [string] $value
            Assert-UiOwnedEvidenceFile -Path $source -OwnedRoot $OwnedRoot
            $bytes = [IO.File]::ReadAllBytes($source)
            Assert-UiPngBytes -Bytes $bytes
            $name = $slug + '-screenshot.png'
            [IO.File]::WriteAllBytes((Join-Path $temp $name), $bytes)
            [IO.File]::WriteAllBytes((Join-Path $OutputDirectory $name), $bytes)
            }
            else {
            if ($value -is [string]) {
                if ($key -in @('uiaTree','lastCommand','stateSnapshot','productLog')) {
                    $value = ConvertFrom-UiAcceptanceJson -Text $value -Source $key
                }
                else {
                    if ($key -eq 'processLedger') {
                        $value = [regex]::Replace($value, '(?m)^\s*\d+\|\d+\|[^\r\n]*', '[redacted-process]')
                        $value = [regex]::Replace($value, '(?i)\bpid\s*=\s*\d+', 'pid=[redacted]')
                    }
                    $value = [ordered]@{ text = $value }
                }
            }
            if ($key -eq 'uiaTree') { Assert-UiAcceptanceFields -InputObject $value -Required @('tree') }
            if ($key -eq 'lastCommand' -and $null -eq $value.PSObject.Properties['status'] -and $null -eq $value.PSObject.Properties['Status']) { throw 'Last command status is missing.' }
            if ($key -eq 'stateSnapshot') { Assert-UiAcceptanceFields -InputObject $value -Required @('tables','taxonomy') }
            if ($key -eq 'productLog') { Assert-UiAcceptanceFields -InputObject $value -Required @('events') }
            $safe = ConvertTo-UiSafeValue -Value $value -RunToken $RunToken -OwnedRoot $OwnedRoot
            $json = ConvertTo-Json -InputObject $safe -Depth 30
            $name = $slug + '-' + (ConvertTo-UiAcceptanceSlug -Value $key) + '.json'
            [IO.File]::WriteAllText((Join-Path $temp $name), $json, [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText((Join-Path $OutputDirectory $name), $json, [Text.UTF8Encoding]::new($false))
            }
        }
        catch {
            $name = $slug + '-' + (ConvertTo-UiAcceptanceSlug -Value $key) + '-unavailable.json'
            $detail = Protect-UiAcceptanceText -Value $_.Exception.Message -RunToken $RunToken -OwnedRoot $OwnedRoot
            $json = ConvertTo-Json -InputObject ([ordered]@{ status='Unavailable'; reason=$detail })
            [IO.File]::WriteAllText((Join-Path $temp $name), $json, [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText((Join-Path $OutputDirectory $name), $json, [Text.UTF8Encoding]::new($false))
        }
        $references[$key] = $name
    }
    return [pscustomobject] $references
}

function Write-UiAcceptanceReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)] $Report,
        [string] $RunToken,
        [string] $OwnedRoot
    )
    if (-not [IO.Directory]::Exists($OutputDirectory)) { throw 'Run output directory does not exist.' }
    $reportObject = if ($Report -is [System.Collections.IDictionary]) { [pscustomobject]$Report } else { $Report }
    Assert-UiAcceptanceFields -InputObject $reportObject -Required @('acceptanceId','checkpoint','startedUtc','endedUtc','scenarios','cleanup','cleanVmStatus')
    if ($Report.cleanVmStatus -cne 'NotRun') { throw 'Clean VM status is not established.' }
    foreach ($scenario in @($Report.scenarios)) {
        Assert-UiAcceptanceStatus -Status $scenario.status -Field 'scenario status'
        foreach ($step in @($scenario.steps)) {
            Assert-UiAcceptanceStatus -Status $step.status -Field 'step status'
            foreach ($path in @($step.evidencePaths)) {
                if ([IO.Path]::IsPathRooted([string]$path) -or [string]$path -match '(^|[\\/])\.\.([\\/]|$)') { throw 'Evidence path must be relative.' }
            }
        }
    }
    $safe = ConvertTo-UiSafeValue -Value $Report -RunToken $RunToken -OwnedRoot $OwnedRoot
    $json = ConvertTo-Json -InputObject $safe -Depth 40
    $target = Join-Path $OutputDirectory 'report.json'
    if ([IO.File]::Exists($target)) { throw 'Report already exists.' }
    [IO.File]::WriteAllText($target, $json, [Text.UTF8Encoding]::new($false))
    $statuses = @($Report.scenarios | ForEach-Object { $_.status })
    $overall = 'Failed'
    if ($Report.cleanup.status -ceq 'Passed') {
        if ($statuses.Count -gt 0 -and @($statuses | Where-Object { $_ -cne 'Passed' }).Count -eq 0) { $overall = 'Passed' }
        elseif (@($statuses | Where-Object { $_ -ceq 'Blocked' }).Count -gt 0 -and @($statuses | Where-Object { $_ -ceq 'Failed' }).Count -eq 0) { $overall = 'Blocked' }
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'summary.txt'), ('Stage ' + $Report.checkpoint + ': ' + $overall + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{ Status=$overall; ReportPath=$target }
}

<#
.SYNOPSIS
    Returns one optional text field of a node, treating an absent field as empty.

.DESCRIPTION
    The driver omits a field the element does not have, so absence is a legitimate answer and not a
    malformed node. Reading the property through its metadata keeps that distinction from turning
    into an exception under strict mode.
#>
function Get-UiRedactedNodeText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Node,
        [Parameter(Mandatory)][string] $Name
    )

    $property = $Node.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return '' }
    return [string] $property.Value
}

<#
.SYNOPSIS
    Returns one node's rectangle, or zeroes in every field when none was reported.

.DESCRIPTION
    The caller normalizes the driver's response before handing it over, so a rectangle is expected as
    a nested object with all four fields. The one case that is tolerated is a node with no rectangle
    at all, which the driver produces for an element it can enumerate but not measure; that node is
    recorded with zeroes in every field so every node keeps the same shape and the zeroes are never
    read as a real position. A partially reported rectangle describes no rectangle and is refused
    rather than completed with zeroes that would read as real edges of the element.
#>
function Get-UiRedactedNodeBounds {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Node)

    $property = $Node.PSObject.Properties['bounds']
    if ($null -eq $property -or $null -eq $property.Value) {
        return [ordered]@{ x = 0; y = 0; width = 0; height = 0 }
    }
    $bounds = $property.Value
    Assert-UiAcceptanceFields -InputObject $bounds -Required @('x','y','width','height')
    return [ordered]@{
        x = [int] $bounds.x; y = [int] $bounds.y
        width = [int] $bounds.width; height = [int] $bounds.height
    }
}

<#
.SYNOPSIS
    Writes one redacted UIA tree evidence file from already-normalized nodes.

.DESCRIPTION
    The pinned driver's response has nested children and native field names, so the scenario
    normalizes it first; this writer only whitelists the reviewed fields and redacts text. It
    deliberately drops every other field the driver may add, and it carries no UIA pattern claim
    because the driver does not report patterns: availability is established by the read-only
    native probe and recorded separately.
#>
function Write-UiRedactedTreeEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string] $EvidenceId,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Nodes,
        [string] $RunToken,
        [string] $OwnedRoot
    )
    if (-not [IO.Directory]::Exists($OutputDirectory)) { throw 'Run output directory does not exist.' }
    $redacted = @()
    foreach ($node in @($Nodes)) {
        # The caller normalizes the driver's response, so a node arrives in the reviewed shape. The
        # identity fields are read as optional here because the normalized shape states them as empty
        # for an element that has none, and the rectangle is read through its own helper for the same
        # reason. The state fields are required: a node whose visibility and enablement are unknown
        # cannot be reasoned about at all.
        Assert-UiAcceptanceFields -InputObject $node -Required @('controlType','enabled','offscreen')
        if ($node.enabled -isnot [bool] -or $node.offscreen -isnot [bool]) { throw 'Invalid UIA tree state.' }
        $redacted += [ordered]@{
            automationId = Protect-UiAcceptanceText -Value (Get-UiRedactedNodeText -Node $node -Name 'automationId') -RunToken $RunToken -OwnedRoot $OwnedRoot
            parentAutomationId = Protect-UiAcceptanceText -Value (Get-UiRedactedNodeText -Node $node -Name 'parentAutomationId') -RunToken $RunToken -OwnedRoot $OwnedRoot
            controlType = Protect-UiAcceptanceText -Value ([string]$node.controlType) -RunToken $RunToken -OwnedRoot $OwnedRoot
            name = Protect-UiAcceptanceText -Value (Get-UiRedactedNodeText -Node $node -Name 'name') -RunToken $RunToken -OwnedRoot $OwnedRoot
            enabled = [bool]$node.enabled
            offscreen = [bool]$node.offscreen
            bounds = Get-UiRedactedNodeBounds -Node $node
        }
    }
    $fileName = (ConvertTo-UiAcceptanceSlug -Value $EvidenceId) + '-uia.json'
    $target = Join-Path $OutputDirectory $fileName
    if ([IO.File]::Exists($target)) { throw 'UIA evidence already exists.' }
    $json = ConvertTo-Json -InputObject ([ordered]@{ tree=$redacted }) -Depth 20
    [IO.File]::WriteAllText($target, $json, [Text.UTF8Encoding]::new($false))
    return $fileName
}

<#
.SYNOPSIS
    Writes the theme apply/restore/readback facts as their own evidence document.

.DESCRIPTION
    Theme state is a harness fact, not a business verdict: the document keeps the requested mode,
    the apply result, the restore result, the independent readback and the journal state in
    separate fields so a failed restore is never hidden by a later readback. The writer only
    persists the document; the scenario decides whether an unknown value blocks or fails the run.
#>
function Write-UiThemeEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string] $EvidenceId,
        [Parameter(Mandatory)] $Document,
        [string] $RunToken,
        [string] $OwnedRoot,
        [AllowEmptyString()][string] $ForbiddenValue = ''
    )

    if (-not [IO.Directory]::Exists($OutputDirectory)) { throw 'Run output directory does not exist.' }
    Assert-UiAcceptanceFields -InputObject $Document -Required @('mode', 'apply', 'restore', 'readback', 'journal')
    $safe = ConvertTo-UiSafeValue -Value $Document -RunToken $RunToken -OwnedRoot $OwnedRoot
    $serialized = ConvertTo-Json -InputObject $safe -Depth 30
    # The receipt is checked by exact value before anything is written: it is a random 64-hex
    # string, so the shape-based redaction cannot find it and must not guess.
    Assert-UiAcceptanceNoCapabilityValue -Text $serialized -Value $ForbiddenValue `
        -Code 'Theme evidence contains a capability value.'
    $fileName = (ConvertTo-UiAcceptanceSlug -Value $EvidenceId) + '-theme.json'
    $target = Join-Path $OutputDirectory $fileName
    if ([IO.File]::Exists($target)) { throw 'Theme evidence already exists.' }
    [IO.File]::WriteAllText($target, $serialized, [Text.UTF8Encoding]::new($false))
    return $fileName
}

Export-ModuleMember -Function @(
    'Write-UiFailureEvidence',
    'Write-UiAcceptanceReport',
    'Write-UiRedactedTreeEvidence',
    'Write-UiThemeEvidence',
    'Assert-UiPngBytes',
    'Assert-UiOwnedEvidenceFile',
    'Assert-UiEvidenceDirectory'
)
