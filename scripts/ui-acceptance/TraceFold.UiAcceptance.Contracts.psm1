# TraceFold UI acceptance toolkit - shared contracts.
#
# Pure, self-contained helpers shared by every other toolkit module: run statuses,
# exit codes, slugging, redaction and JSON shape validation. ASCII only so the file
# loads identically under Windows PowerShell 5.1 and PowerShell 7.

Set-StrictMode -Version Latest

$script:UiAcceptanceStatuses = @('Passed', 'Failed', 'Blocked', 'NotRun')

$script:UiAcceptanceExitCodes = @{
    Success           = 0
    InvalidArguments  = 2
    UnsafeOwnership   = 3
    SnapshotUnavailable = 4
    WindowUnavailable = 5
    Unexpected        = 10
}

function Get-UiAcceptanceExitCode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Success', 'InvalidArguments', 'UnsafeOwnership', 'SnapshotUnavailable', 'WindowUnavailable', 'Unexpected')]
        [string] $Name
    )

    return $script:UiAcceptanceExitCodes[$Name]
}

function Get-UiAcceptanceStatusName {
    [CmdletBinding()]
    param()

    return $script:UiAcceptanceStatuses
}

function Test-UiAcceptanceStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Status
    )

    foreach ($candidate in $script:UiAcceptanceStatuses) {
        if ($candidate -ceq $Status) { return $true }
    }

    return $false
}

function Assert-UiAcceptanceStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Status,

        [Parameter(Mandatory = $true)]
        [string] $Field
    )

    if (-not (Test-UiAcceptanceStatus -Status $Status)) {
        throw ('Invalid ' + $Field + ': expected one of ' + ($script:UiAcceptanceStatuses -join ', ') + '.')
    }
}

<#
.SYNOPSIS
    Builds an evidence slug from identifiers only.

.DESCRIPTION
    Evidence file names must never embed user values. The slug is restricted to
    lower-case ASCII letters, digits and single hyphens; anything else collapses to
    a hyphen, and the result is bounded in length.
#>
function ConvertTo-UiAcceptanceSlug {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Value,

        [int] $MaximumLength = 60
    )

    if ($MaximumLength -lt 1) {
        throw 'MaximumLength must be positive.'
    }

    $builder = New-Object System.Text.StringBuilder
    $lastWasHyphen = $true
    # Case-insensitive on purpose: identifiers keep their meaning, only the case is folded.
    foreach ($character in $Value.ToLowerInvariant().ToCharArray()) {
        $isAllowed = ($character -cmatch '[a-z0-9]')
        if ($isAllowed) {
            [void] $builder.Append($character)
            $lastWasHyphen = $false
        }
        elseif (-not $lastWasHyphen) {
            [void] $builder.Append('-')
            $lastWasHyphen = $true
        }
    }

    $slug = $builder.ToString().Trim('-')
    if ($slug.Length -gt $MaximumLength) {
        $slug = $slug.Substring(0, $MaximumLength).Trim('-')
    }

    if ($slug.Length -eq 0) {
        return 'unnamed'
    }

    return $slug
}

function Get-UiAcceptanceRedactionPatterns {
    [CmdletBinding()]
    param(
        [string] $RunToken,

        [string] $OwnedRoot
    )

    $patterns = New-Object System.Collections.ArrayList
    if (-not [string]::IsNullOrEmpty($OwnedRoot)) {
        [void] $patterns.Add([pscustomobject]@{ Pattern = [regex]::Escape($OwnedRoot); Label = '[redacted-path]' })
    }

    if (-not [string]::IsNullOrEmpty($RunToken)) {
        [void] $patterns.Add([pscustomobject]@{ Pattern = [regex]::Escape($RunToken); Label = '[redacted-token]' })
    }

    $userName = [Environment]::UserName
    if (-not [string]::IsNullOrEmpty($userName)) {
        [void] $patterns.Add([pscustomobject]@{ Pattern = [regex]::Escape($userName); Label = '[redacted-user]' })
    }

    $machineName = [Environment]::MachineName
    if (-not [string]::IsNullOrEmpty($machineName)) {
        [void] $patterns.Add([pscustomobject]@{ Pattern = [regex]::Escape($machineName); Label = '[redacted-host]' })
    }

    # Absolute Windows paths that escaped the explicit root replacement.
    [void] $patterns.Add([pscustomobject]@{ Pattern = '[A-Za-z]:\\[^\s"'']*'; Label = '[redacted-path]' })
    # GUID-shaped identifiers (product entity ids, run ids).
    [void] $patterns.Add([pscustomobject]@{ Pattern = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'; Label = '[redacted-id]' })

    return $patterns.ToArray()
}

function Protect-UiAcceptanceText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Value,

        [string] $RunToken,

        [string] $OwnedRoot
    )

    $result = $Value
    foreach ($entry in (Get-UiAcceptanceRedactionPatterns -RunToken $RunToken -OwnedRoot $OwnedRoot)) {
        $result = [regex]::Replace($result, $entry.Pattern, $entry.Label, 'IgnoreCase')
    }

    return $result
}

<#
.SYNOPSIS
    The fixed acceptance viewport set every real-window gate must prove.

.DESCRIPTION
    The three client sizes are the acceptance contract, not a product literal, so the
    viewport triple lives here and every scenario reads it through this function instead of
    repeating the numbers.
#>
function Get-UiAcceptanceRequiredViewport {
    [CmdletBinding()]
    param()

    $viewports = @(
        [ordered]@{ WidthDip = 1024; HeightDip = 720 }
        [ordered]@{ WidthDip = 1280; HeightDip = 800 }
        [ordered]@{ WidthDip = 1600; HeightDip = 900 }
    )

    return ,$viewports
}

function Test-UiAcceptanceDangerousKey {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Key
    )

    $denied = @(
        'token', 'ownertoken', 'runid', 'acceptancerunid',
        'username', 'machinename', 'hostname', 'userprofile',
        'databasepath', 'datasource', 'connectionstring',
        'raw', 'stdout', 'stderr', 'commandline', 'processid', 'pid'
    )

    foreach ($candidate in $denied) {
        if ($Key.ToLowerInvariant() -eq $candidate) { return $true }
    }

    return $false
}

function ConvertFrom-UiAcceptanceJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Text,

        [string] $Source = 'driver'
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        throw ('The ' + $Source + ' produced no output to parse.')
    }

    try {
        return $Text | ConvertFrom-Json
    }
    catch {
        throw ('The ' + $Source + ' output is not valid JSON.')
    }
}

function Assert-UiAcceptanceFields {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $InputObject,

        [Parameter(Mandatory = $true)]
        [string[]] $Required
    )

    $missing = New-Object System.Collections.ArrayList
    foreach ($name in $Required) {
        $property = $InputObject.PSObject.Properties[$name]
        if ($null -eq $property) {
            [void] $missing.Add($name)
        }
    }

    if ($missing.Count -gt 0) {
        throw ('Missing required fields: ' + ($missing -join ', ') + '.')
    }
}

Export-ModuleMember -Function @(
    'Get-UiAcceptanceExitCode',
    'Get-UiAcceptanceStatusName',
    'Test-UiAcceptanceStatus',
    'Assert-UiAcceptanceStatus',
    'ConvertTo-UiAcceptanceSlug',
    'Get-UiAcceptanceRedactionPatterns',
    'Protect-UiAcceptanceText',
    'Test-UiAcceptanceDangerousKey',
    'Get-UiAcceptanceRequiredViewport',
    'ConvertFrom-UiAcceptanceJson',
    'Assert-UiAcceptanceFields'
)
