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

<#
.SYNOPSIS
    Projects a durable evidence document without changing the in-memory source.

.DESCRIPTION
    Scenario code keeps the real process identities it needs for its own checks. This public
    projection is the one boundary to use before that evidence is serialised: it returns a new
    object tree, redacts capability values and machine paths, and preserves safe scalar facts such
    as hashes, booleans, exit codes and timestamps. Dictionary and custom-object properties become
    PSCustomObject nodes; enumerable values become arrays. Raw capability/path fields, other
    dangerous keys and unknown CLR objects are refused rather than being serialised implicitly;
    callers carry logical state labels and safe executable name/hash fields instead.
#>
function Test-UiDurableEvidenceKey {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string] $Key,
        [AllowNull()] $Value
    )

    if ($Key -ieq 'runtoken' -or $Key -ieq 'ownedroot' -or $Key -ieq 'executablepath') { return $false }
    if ($Key -ieq 'pid' -or $Key -ieq 'processid') {
        if ($null -eq $Value -or $Value -is [bool] -or -not $Value.GetType().IsValueType) { return $false }
        try { return ([long] $Value -ge 0) }
        catch { return $false }
    }
    return -not (Test-UiAcceptanceDangerousKey -Key $Key)
}

function ConvertTo-UiDurableEvidenceProjection {
    [CmdletBinding()]
    param(
        [AllowNull()] $Value,
        [AllowEmptyString()][string] $RunToken = '',
        [AllowEmptyString()][string] $OwnedRoot = '',
        [AllowEmptyString()][string] $ExecutablePath = ''
    )

    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Management.Automation.ErrorRecord]) {
        $position = ''
        $stack = ''
        if ($null -ne $Value.InvocationInfo) {
            $position = [string] $Value.InvocationInfo.PositionMessage
        }
        if ($null -ne $Value.ScriptStackTrace) { $stack = [string] $Value.ScriptStackTrace }
        $fact = [ordered] @{
            errorType = $Value.Exception.GetType().FullName
            errorId   = [string] $Value.FullyQualifiedErrorId
            category  = [string] $Value.CategoryInfo.Category
            position  = $position
            stack     = $stack
            message   = [string] $Value.Exception.Message
        }
        return ConvertTo-UiDurableEvidenceProjection -Value $fact -RunToken $RunToken -OwnedRoot $OwnedRoot -ExecutablePath $ExecutablePath
    }
    if ($Value -is [System.Exception]) {
        $fact = [ordered] @{
            errorType = $Value.GetType().FullName
            message   = [string] $Value.Message
            stack     = [string] $Value.StackTrace
        }
        return ConvertTo-UiDurableEvidenceProjection -Value $fact -RunToken $RunToken -OwnedRoot $OwnedRoot -ExecutablePath $ExecutablePath
    }
    if ($Value -is [string]) {
        $safe = [string] $Value
        if (-not [string]::IsNullOrEmpty($ExecutablePath)) {
            $safe = [regex]::Replace($safe, [regex]::Escape($ExecutablePath), '[redacted-path]', 'IgnoreCase')
        }
        return Protect-UiAcceptanceText -Value $safe -RunToken $RunToken -OwnedRoot $OwnedRoot
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $safe = [ordered] @{}
        foreach ($key in $Value.Keys) {
            $safeKey = [string] $key
            if (-not (Test-UiDurableEvidenceKey -Key $safeKey -Value $Value[$key])) {
                throw ('Evidence contains a forbidden key: ' + $safeKey)
            }
            $safe[$safeKey] = ConvertTo-UiDurableEvidenceProjection -Value $Value[$key] `
                -RunToken $RunToken -OwnedRoot $OwnedRoot -ExecutablePath $ExecutablePath
        }
        return [pscustomobject] $safe
    }
    if ($Value -is [pscustomobject]) {
        $safe = [ordered] @{}
        foreach ($property in $Value.PSObject.Properties) {
            if (-not (Test-UiDurableEvidenceKey -Key $property.Name -Value $property.Value)) {
                throw ('Evidence contains a forbidden key: ' + $property.Name)
            }
            $safe[$property.Name] = ConvertTo-UiDurableEvidenceProjection -Value $property.Value `
                -RunToken $RunToken -OwnedRoot $OwnedRoot -ExecutablePath $ExecutablePath
        }
        return [pscustomobject] $safe
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $safeItems = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Value) {
            $safeItems.Add((ConvertTo-UiDurableEvidenceProjection -Value $item `
                -RunToken $RunToken -OwnedRoot $OwnedRoot -ExecutablePath $ExecutablePath))
        }
        return ,$safeItems.ToArray()
    }
    if ($Value -is [ValueType] -or $Value -is [Guid]) { return $Value }
    throw ('Durable evidence contains an unsupported value type: ' + $Value.GetType().FullName)
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
    # Read the already-finished command first: the screenshot and UIA collectors below issue new
    # driver calls and would otherwise replace the failure-causing result before it is persisted.
    $required = @('lastCommand','screenshot','uiaTree','productLog','stateSnapshot','processLedger')
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
            $safe = ConvertTo-UiDurableEvidenceProjection -Value $value -RunToken $RunToken -OwnedRoot $OwnedRoot
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
    # The visual matrix is a separate shared document: it is never folded into report.json, so a
    # report that carries one keeps the report schema every existing consumer already parses.
    $visual = Get-UiVisualField -Value $reportObject -Name 'visual'
    $reportForJson = Remove-UiReportVisual -Report $reportObject
    $safe = ConvertTo-UiSafeValue -Value $reportForJson -RunToken $RunToken -OwnedRoot $OwnedRoot
    $json = ConvertTo-Json -InputObject $safe -Depth 40
    $target = Join-Path $OutputDirectory 'report.json'
    if ([IO.File]::Exists($target)) { throw 'Report already exists.' }
    # Everything the new branch needs is validated and built before the first successful artifact is
    # persisted: a refused link, a missing template or an occupied target cannot leave a report
    # behind that claims a complete run without its index.
    $prepared = $null
    if ($null -ne $visual) {
        $prepared = Write-UiVisualIndex -OutputDirectory $OutputDirectory -Visual $visual -RunToken $RunToken -OwnedRoot $OwnedRoot -PrepareOnly
        if ([IO.File]::Exists($prepared.DataTarget) -or [IO.Directory]::Exists($prepared.DataTarget)) { throw 'Screenshot index data already exists.' }
        if ([IO.File]::Exists($prepared.IndexTarget) -or [IO.Directory]::Exists($prepared.IndexTarget)) { throw 'Screenshot index already exists.' }
    }
    [IO.File]::WriteAllText($target, $json, [Text.UTF8Encoding]::new($false))
    $statuses = @($Report.scenarios | ForEach-Object { $_.status })
    $overall = 'Failed'
    if ($Report.cleanup.status -ceq 'Passed') {
        if ($statuses.Count -gt 0 -and @($statuses | Where-Object { $_ -cne 'Passed' }).Count -eq 0) { $overall = 'Passed' }
        elseif (@($statuses | Where-Object { $_ -ceq 'Blocked' }).Count -gt 0 -and @($statuses | Where-Object { $_ -ceq 'Failed' }).Count -eq 0) { $overall = 'Blocked' }
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'summary.txt'), ('Stage ' + $Report.checkpoint + ': ' + $overall + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    # A report without visual facts is written exactly as before: no new file, no new field.
    if ($null -ne $prepared) {
        Write-UiUtf8NewFile -Path $prepared.DataTarget -Text $prepared.Json
        Write-UiUtf8NewFile -Path $prepared.IndexTarget -Text $prepared.Html
    }
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

$script:ReportTemplatePath = Join-Path $PSScriptRoot 'report-template\index.html'

<#
.SYNOPSIS
    Reads one optional field from a dictionary or a property bag.

.DESCRIPTION
    The visual matrix arrives either as a decoded JSON object or as an ordered dictionary built by
    a scenario. An absent field is a legitimate answer, so the read goes through the metadata and
    returns null instead of raising under strict mode.
#>
function Get-UiVisualField {
    [CmdletBinding()]
    param($Value, [Parameter(Mandatory)][string] $Name)

    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Collections.IDictionary]) {
        if ($Value.Contains($Name)) { return ,$Value[$Name] }
        return $null
    }
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return ,$property.Value
}

<#
.SYNOPSIS
    Writes one UTF-8 document that must not already exist.

.DESCRIPTION
    Both shared documents are first-writers-only: the create-new mode is the refusal, so a second
    run can never replace an existing index or data document with a different generation.
#>
function Write-UiUtf8NewFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][AllowEmptyString()][string] $Text
    )

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush() } finally { $stream.Dispose() }
}

<#
.SYNOPSIS
    Copies a report without its visual matrix, keeping every other field in order.

.DESCRIPTION
    report.json is the durable acceptance report every consumer already parses. The visual matrix is
    a separate shared document, so it is removed here rather than folded into that schema.
#>
function Remove-UiReportVisual {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Report)

    $copy = [ordered]@{}
    if ($Report -is [System.Collections.IDictionary]) {
        foreach ($key in @($Report.Keys)) {
            if ([string]$key -cne 'visual') { $copy[[string]$key] = $Report[$key] }
        }
        return $copy
    }
    foreach ($property in @($Report.PSObject.Properties)) {
        if ($property.Name -cne 'visual') { $copy[$property.Name] = $property.Value }
    }
    return $copy
}

<#
.SYNOPSIS
    Validates one report-relative evidence link and returns it in URL form.

.DESCRIPTION
    Only a canonical relative path inside the report directory may reach the page. A rooted path, a
    traversal segment, a URL scheme, an empty segment or an absent target is refused, and the target
    must parse as the expected type so a broken link never renders as a picture.
#>
function Get-UiVisualLink {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Value,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string[]] $AllowedExtension,
        [string] $RunToken,
        [string] $OwnedRoot
    )

    $text = [string] $Value
    if ([string]::IsNullOrWhiteSpace($text)) { throw 'Visual evidence link is empty.' }
    if ([IO.Path]::IsPathRooted($text)) { throw 'Visual evidence link must be relative.' }
    if ($text -match '(^|[\\/])\.\.([\\/]|$)') { throw 'Visual evidence link must not traverse.' }
    $segments = @($text -split '[\\/]')
    foreach ($segment in $segments) {
        if ([string]::IsNullOrEmpty($segment) -or $segment -ceq '.') { throw 'Visual evidence link is not canonical.' }
        foreach ($character in $segment.ToCharArray()) {
            if ([int] [char] $character -lt 32) { throw 'Visual evidence link contains a control character.' }
        }
    }
    # The shared redaction runs on the name that actually points at the file, before URL encoding.
    # Encoding a token into its escaped form would otherwise hide it from the same patterns that
    # rewrite it everywhere else, and the link would be published as a working pointer to a file
    # whose name the durable report refuses to print.
    if ((Protect-UiAcceptanceText -Value $text -RunToken $RunToken -OwnedRoot $OwnedRoot) -cne $text) {
        throw 'Visual evidence link would be rewritten by redaction.'
    }
    $full = [IO.Path]::GetFullPath((Join-Path $OutputDirectory $text))
    # A lexical containment check does not resolve junctions: a directory inside the report root may
    # itself point outside it. The shared check walks the root, every directory on the way and the
    # target itself for a reparse point before anything is read.
    Assert-UiOwnedEvidenceFile -Path $full -OwnedRoot $OutputDirectory
    $extension = [IO.Path]::GetExtension($full).ToLowerInvariant()
    if ($AllowedExtension -notcontains $extension) { throw 'Visual evidence link has an unexpected type.' }
    if ($extension -ceq '.png') {
        Assert-UiPngBytes -Bytes ([IO.File]::ReadAllBytes($full))
    }
    else {
        $null = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($full, [Text.UTF8Encoding]::new($false, $true)))
    }
    # Each segment is encoded on its own, so '#', '%' and spaces can never change what the href
    # means. The round trip then proves the URL still names exactly the file that was verified.
    $encoded = New-Object 'System.Collections.Generic.List[string]'
    foreach ($segment in $segments) { $encoded.Add([Uri]::EscapeDataString($segment)) }
    $url = ($encoded.ToArray() -join '/')
    $decoded = New-Object 'System.Collections.Generic.List[string]'
    foreach ($part in @($url -split '/')) { $decoded.Add([Uri]::UnescapeDataString($part)) }
    if (($decoded.ToArray() -join '/') -cne ($segments -join '/')) { throw 'Visual evidence link does not round trip through its URL form.' }
    return $url
}

<#
.SYNOPSIS
    Normalizes one visual entry, keeping only the facts the page renders.

.DESCRIPTION
    The matrix is a report, not a verdict: state and status are carried exactly as given, an unknown
    stays null, and nothing is upgraded. The known field set is explicit so a scenario cannot smuggle
    an unreviewed field into the shared document.
#>
function ConvertTo-UiVisualEntry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Entry,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [string] $RunToken,
        [string] $OwnedRoot
    )

    if ($null -eq $Entry) { throw 'Visual entry is missing.' }
    $state = Get-UiVisualField -Value $Entry -Name 'state'
    if ($state -isnot [string] -or [string]::IsNullOrWhiteSpace($state)) { throw 'Visual entry state is missing.' }
    $status = Get-UiVisualField -Value $Entry -Name 'status'
    if ($status -isnot [string]) { throw 'Visual entry status is missing.' }

    $image = Get-UiVisualField -Value $Entry -Name 'image'
    $imageLink = $null
    if ($null -ne $image -and -not [string]::IsNullOrWhiteSpace([string]$image)) {
        $imageLink = Get-UiVisualLink -Value $image -OutputDirectory $OutputDirectory -AllowedExtension @('.png') -RunToken $RunToken -OwnedRoot $OwnedRoot
    }
    $uia = Get-UiVisualField -Value $Entry -Name 'uia'
    $uiaLink = $null
    if ($null -ne $uia -and -not [string]::IsNullOrWhiteSpace([string]$uia)) {
        $uiaLink = Get-UiVisualLink -Value $uia -OutputDirectory $OutputDirectory -AllowedExtension @('.json') -RunToken $RunToken -OwnedRoot $OwnedRoot
    }
    $synthetic = Get-UiVisualField -Value $Entry -Name 'synthetic'
    $syntheticValue = $null
    if ($null -ne $synthetic) {
        # A string 'false' is not a boolean: coercing it would turn an unknown provenance into a
        # definite one (and 'false' into true). Absent/null stays unknown.
        if ($synthetic -isnot [bool]) { throw 'Visual entry synthetic flag must be a boolean or null.' }
        $syntheticValue = [bool] $synthetic
    }

    return [ordered]@{
        state        = [string] $state
        status       = [string] $status
        theme        = Get-UiVisualField -Value $Entry -Name 'theme'
        viewport     = Get-UiVisualField -Value $Entry -Name 'viewport'
        page         = Get-UiVisualField -Value $Entry -Name 'page'
        dialog       = Get-UiVisualField -Value $Entry -Name 'dialog'
        clientWidth  = Get-UiVisualField -Value $Entry -Name 'clientWidth'
        clientHeight = Get-UiVisualField -Value $Entry -Name 'clientHeight'
        dpi          = Get-UiVisualField -Value $Entry -Name 'dpi'
        productHash  = Get-UiVisualField -Value $Entry -Name 'productHash'
        capturedUtc  = Get-UiVisualField -Value $Entry -Name 'capturedUtc'
        synthetic    = $syntheticValue
        image        = $imageLink
        uia          = $uiaLink
    }
}

<#
.SYNOPSIS
    Writes the offline screenshot index and its data document into a finished report directory.

.DESCRIPTION
    This is the reporting outlet's own writer, not a separate tool: it consumes the visual facts the
    caller already holds, validates every link inside the report directory, redacts through the same
    text protection the durable report uses, and embeds the JSON with the script-closing sequence
    escaped. Neither file is overwritten, and a synthetic matrix is marked as synthetic in both the
    data and the page.
#>
function Write-UiVisualIndex {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)] $Visual,
        [string] $RunToken,
        [string] $OwnedRoot,
        [switch] $PrepareOnly
    )

    if (-not [IO.Directory]::Exists($OutputDirectory)) { throw 'Run output directory does not exist.' }
    $entriesValue = Get-UiVisualField -Value $Visual -Name 'entries'
    if ($entriesValue -isnot [array]) { throw 'Visual matrix entries are missing.' }

    $entries = New-Object System.Collections.ArrayList
    foreach ($entry in $entriesValue) {
        [void] $entries.Add((ConvertTo-UiVisualEntry -Entry $entry -OutputDirectory $OutputDirectory -RunToken $RunToken -OwnedRoot $OwnedRoot))
    }
    $themesValue = Get-UiVisualField -Value $Visual -Name 'themes'
    $themes = @()
    if ($themesValue -is [array]) { $themes = $themesValue }
    $keyboardValue = Get-UiVisualField -Value $Visual -Name 'keyboard'
    $keyboard = @()
    if ($keyboardValue -is [array]) { $keyboard = $keyboardValue }
    $syntheticRaw = Get-UiVisualField -Value $Visual -Name 'synthetic'
    $syntheticValue = $null
    if ($null -ne $syntheticRaw) {
        # Unknown provenance must stay unknown: only a real boolean states the source of the data.
        if ($syntheticRaw -isnot [bool]) { throw 'Visual synthetic flag must be a boolean or null.' }
        $syntheticValue = [bool] $syntheticRaw
    }
    $generatedUtc = Get-UiVisualField -Value $Visual -Name 'generatedUtc'

    $doc = [ordered] @{
        schemaVersion       = 1
        scenario            = 'Visual'
        themeMatrix         = Get-UiVisualField -Value $Visual -Name 'themeMatrix'
        productHash         = Get-UiVisualField -Value $Visual -Name 'productHash'
        generatedUtc        = $(if ($null -ne $generatedUtc) { [string] $generatedUtc } else { [DateTimeOffset]::UtcNow.ToString('o') })
        synthetic           = $syntheticValue
        journal             = Get-UiVisualField -Value $Visual -Name 'journal'
        matrixPolicy        = Get-UiVisualField -Value $Visual -Name 'matrixPolicy'
        themes              = $themes
        keyboard            = $keyboard
        entries             = $entries.ToArray()
        themeRestoreFailure = Get-UiVisualField -Value $Visual -Name 'themeRestoreFailure'
        stopReason          = Get-UiVisualField -Value $Visual -Name 'stopReason'
        status              = Get-UiVisualField -Value $Visual -Name 'status'
        reason              = Get-UiVisualField -Value $Visual -Name 'reason'
    }
    $safe = ConvertTo-UiSafeValue -Value $doc -RunToken $RunToken -OwnedRoot $OwnedRoot
    # Redaction is field-agnostic and can rewrite a file name (GUID-shaped names, a token inside a
    # name). A link that no longer names the verified file is refused instead of published.
    $safeEntries = @($safe['entries'])
    for ($index = 0; $index -lt $safeEntries.Count; $index++) {
        foreach ($key in @('image', 'uia')) {
            $before = $entries[$index][$key]
            if ($null -eq $before) { continue }
            if ([string] $safeEntries[$index][$key] -cne [string] $before) {
                throw 'Visual evidence link would be rewritten by redaction.'
            }
        }
    }
    $json = ConvertTo-Json -InputObject $safe -Depth 24
    Assert-UiAcceptanceNoCapabilityValue -Text $json -Value $RunToken -Code 'visual.capability_value_leak'

    if (-not [IO.File]::Exists($script:ReportTemplatePath)) { throw 'visual.index_template_missing' }
    $embedded = $json.Replace('<', '\u003c')
    $embedded = $embedded.Replace([string] [char] 0x2028, '\u2028').Replace([string] [char] 0x2029, '\u2029')
    $template = [IO.File]::ReadAllText($script:ReportTemplatePath)
    if ($template.IndexOf('__SCREENSHOTS_JSON__', [StringComparison]::Ordinal) -lt 0) { throw 'visual.index_template_placeholder_missing' }
    $html = $template.Replace('__SCREENSHOTS_JSON__', $embedded)
    Assert-UiAcceptanceNoCapabilityValue -Text $html -Value $RunToken -Code 'visual.capability_value_leak'

    $dataTarget = Join-Path $OutputDirectory 'screenshots.json'
    $indexTarget = Join-Path $OutputDirectory 'index.html'
    if ($PrepareOnly) {
        # The report outlet validates and prepares both documents before it persists anything, so a
        # refused link or an occupied target cannot leave a report without its index.
        return [pscustomobject] @{
            Json        = $json
            Html        = $html
            DataTarget  = $dataTarget
            IndexTarget = $indexTarget
            EntryCount  = $entries.Count
            Synthetic   = $syntheticValue
        }
    }
    if ([IO.File]::Exists($dataTarget) -or [IO.Directory]::Exists($dataTarget)) { throw 'Screenshot index data already exists.' }
    if ([IO.File]::Exists($indexTarget) -or [IO.Directory]::Exists($indexTarget)) { throw 'Screenshot index already exists.' }
    Write-UiUtf8NewFile -Path $dataTarget -Text $json
    Write-UiUtf8NewFile -Path $indexTarget -Text $html

    return [pscustomobject] @{
        ScreenshotsPath = $dataTarget
        IndexPath       = $indexTarget
        EntryCount      = $entries.Count
        Synthetic       = $syntheticValue
    }
}

Export-ModuleMember -Function @(
    'ConvertTo-UiDurableEvidenceProjection',
    'Write-UiFailureEvidence',
    'Write-UiAcceptanceReport',
    'Write-UiRedactedTreeEvidence',
    'Write-UiThemeEvidence',
    'Write-UiVisualIndex',
    'Assert-UiPngBytes',
    'Assert-UiOwnedEvidenceFile',
    'Assert-UiEvidenceDirectory'
)
