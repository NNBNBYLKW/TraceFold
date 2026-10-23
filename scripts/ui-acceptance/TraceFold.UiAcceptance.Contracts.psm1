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

    # Absolute Windows paths that escaped the explicit root replacement. A backslash run that
    # reaches a quote is never consumed, so the escape of a closing quote survives and a redacted
    # document stays parseable.
    [void] $patterns.Add([pscustomobject]@{ Pattern = '[A-Za-z]:\\(?:[^\s"''\\]|\\(?!\\*"))*'; Label = '[redacted-path]' })
    # GUID-shaped identifiers (product entity ids, run ids).
    [void] $patterns.Add([pscustomobject]@{ Pattern = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'; Label = '[redacted-id]' })
    # Product card AutomationIds use Guid:N, without separators. Limit this rule to their known
    # prefixes so a fixture's 64-character SHA-256 hash remains available as evidence.
    [void] $patterns.Add([pscustomobject]@{ Pattern = '\b(?:InboxCard|LibraryCard)_[0-9a-fA-F]{32}\b'; Label = '[redacted-id]' })

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

<#
.SYNOPSIS
    The phase vocabulary a dynamic media row may declare.

.DESCRIPTION
    A dynamic row is only reachable in the phase that materialises it: the inbox row needs the
    inbox to have been seeded and scanned, and the library row only exists after an ingest. A
    gate must therefore never require a row the current phase cannot have produced.
#>
function Get-UiAcceptanceDynamicRowPhase {
    [CmdletBinding()]
    param()

    return @('startup', 'inbox-seeded', 'post-ingest')
}

function Test-UiAcceptanceDangerousKey {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Key
    )

    $denied = @(
        'token', 'ownertoken', 'receiptid', 'handbacktoken', 'handbacktokendigest',
        'runid', 'acceptancerunid',
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
        # A dictionary carries its fields as keys; PSObject.Properties only exposes the
        # dictionary's own members (Keys, Values, Count), so a key check must use Contains.
        $present = $false
        if ($InputObject -is [System.Collections.IDictionary]) {
            $present = $InputObject.Contains($name)
        }
        else {
            $present = ($null -ne $InputObject.PSObject.Properties[$name])
        }
        if (-not $present) {
            [void] $missing.Add($name)
        }
    }

    if ($missing.Count -gt 0) {
        throw ('Missing required fields: ' + ($missing -join ', ') + '.')
    }
}

<#
.SYNOPSIS
    Refuses a document that still contains a live capability value.

.DESCRIPTION
    Text redaction is field-agnostic: it removes run tokens, roots, user names and identifier
    shapes, but a hand-back receipt is a random 64-hex value that looks exactly like a digest and
    must stay available as evidence. A receipt therefore cannot be found by shape; it is checked
    by exact value at the boundary where a document is about to be persisted. An empty value
    means "this document has no capability to check", not "the check passed".
#>
function Assert-UiAcceptanceNoCapabilityValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Text,

        [AllowEmptyString()]
        [string] $Value = '',

        [string] $Code = 'capability_value_leak'
    )

    if ([string]::IsNullOrEmpty($Value)) { return }
    if ($Text.IndexOf($Value, [StringComparison]::Ordinal) -ge 0) { throw $Code }
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
    'Assert-UiAcceptanceNoCapabilityValue',
    'Get-UiAcceptanceRequiredViewport',
    'Get-UiAcceptanceDynamicRowPhase',
    'ConvertFrom-UiAcceptanceJson',
    'Assert-UiAcceptanceFields'
)
