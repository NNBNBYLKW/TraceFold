# TraceFold UI acceptance toolkit - fake support CLI fixture.
#
# Stands in for TraceFold.Media.UiAcceptance.Support so the toolkit self-tests never create
# or delete a real owned root and never touch a real product window. Every command writes
# its action name to TRACEFOLD_FAKE_SUPPORT_LOG when that variable is set.
#
# Accepted shape: <group> <action> [--name value ...]
#   root validate|create|delete
#   window resize
#   snapshot
#   native lookup
#
# Simulation switches (environment variables):
#   TRACEFOLD_FAKE_SUPPORT_LOG         file that receives one action name per invocation
#   TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN   '1' accepts any non-empty token, not just test-token
#   TRACEFOLD_FAKE_SUPPORT_SCALE       device scale used for the DIP to pixel conversion
#   TRACEFOLD_FAKE_SUPPORT_CLIENT_DELTA physical pixels removed from each client axis
#   TRACEFOLD_FAKE_NATIVE_JSON         verbatim native lookup response (probe failure tests)
#   TRACEFOLD_FAKE_NATIVE_AVAILABLE    'false' reports the pattern as unsupported, 'null' as unknown
#   TRACEFOLD_FAKE_NATIVE_OUTER        outer match count to report
#   TRACEFOLD_FAKE_NATIVE_DESCENDANT   descendant match count to report
#   TRACEFOLD_FAKE_NATIVE_CLASS        class name the descendant lookup resolves to
#   TRACEFOLD_FAKE_NATIVE_NAME         accessible name the probe reports
#   TRACEFOLD_FAKE_NATIVE_WIDTH        pixel width the probe reports

$items = [string[]] $args

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($null -eq $items) { $items = @() }
if ($items.Count -lt 2) { exit 2 }

$group = $items[0]
$action = $items[1]

$values = @{}
$patterns = New-Object System.Collections.ArrayList
for ($index = 2; $index -lt $items.Count; $index++) {
    $token = $items[$index]
    if (-not $token.StartsWith('--')) { exit 2 }
    if ($index + 1 -ge $items.Count) { exit 2 }
    if ($token -ceq '--pattern') { [void] $patterns.Add([string] $items[$index + 1]) }
    else { $values[$token.Substring(2)] = $items[$index + 1] }
    $index++
}

function Test-FakeSupportOption {
    param([string] $Name)
    return $values.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($values[$Name])
}

if (-not (Test-FakeSupportOption 'token')) { exit 3 }
if ($values['token'] -cne 'test-token' -and $env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN -ne '1') { exit 3 }

if (-not [string]::IsNullOrWhiteSpace($env:TRACEFOLD_FAKE_SUPPORT_LOG)) {
    Add-Content -LiteralPath $env:TRACEFOLD_FAKE_SUPPORT_LOG -Value $action
}

function Get-FakeScale {
    $scale = 1.0
    if (-not [string]::IsNullOrWhiteSpace($env:TRACEFOLD_FAKE_SUPPORT_SCALE)) {
        $parsed = 0.0
        if ([double]::TryParse($env:TRACEFOLD_FAKE_SUPPORT_SCALE,
                [Globalization.NumberStyles]::Float,
                [Globalization.CultureInfo]::InvariantCulture, [ref] $parsed) -and $parsed -gt 0) {
            $scale = $parsed
        }
    }
    return $scale
}

function Get-FakeClientDelta {
    $delta = 0
    if (-not [string]::IsNullOrWhiteSpace($env:TRACEFOLD_FAKE_SUPPORT_CLIENT_DELTA)) {
        $parsed = 0
        if ([int]::TryParse($env:TRACEFOLD_FAKE_SUPPORT_CLIENT_DELTA, [ref] $parsed)) { $delta = $parsed }
    }
    return $delta
}

switch ($group) {
    'root' {
        if (-not (Test-FakeSupportOption 'root') -and $action -ne 'create') { exit 2 }
        switch ($action) {
            'validate' {
                Write-Output '{"owned":true}'
                exit 0
            }
            'create' {
                if (-not (Test-FakeSupportOption 'parent') -or
                    -not (Test-FakeSupportOption 'run-id')) { exit 2 }
                $runId = [guid]::Empty
                if (-not [guid]::TryParseExact($values['run-id'], 'D', [ref]$runId)) { exit 2 }
                $root = Join-Path $values['parent'] ('TraceFold-UIA-' + $runId.ToString('N'))
                if (-not [IO.Directory]::Exists($root)) { [void][IO.Directory]::CreateDirectory($root) }
                Write-Output ([pscustomobject]@{ root = $root } | ConvertTo-Json -Compress)
                exit 0
            }
            'delete' {
                Write-Output '{"deleted":true}'
                exit 0
            }
            default { exit 2 }
        }
    }
    'window' {
        if ($action -ne 'resize') { exit 2 }
        if (-not (Test-FakeSupportOption 'process-id') -or
            -not (Test-FakeSupportOption 'width-dip') -or
            -not (Test-FakeSupportOption 'height-dip')) { exit 2 }
        $scale = Get-FakeScale
        $delta = Get-FakeClientDelta
        $widthDip = 0
        $heightDip = 0
        [void][int]::TryParse($values['width-dip'], [ref] $widthDip)
        [void][int]::TryParse($values['height-dip'], [ref] $heightDip)
        $widthPixels = [int][Math]::Round($widthDip * $scale, [MidpointRounding]::AwayFromZero)
        $heightPixels = [int][Math]::Round($heightDip * $scale, [MidpointRounding]::AwayFromZero)
        Write-Output ([pscustomobject]@{
            processId = [int] $values['process-id']
            widthPixels = $widthPixels
            heightPixels = $heightPixels
            clientWidthPixels = $widthPixels - $delta
            clientHeightPixels = $heightPixels - $delta
        } | ConvertTo-Json -Compress)
        exit 0
    }
    'snapshot' {
        if (-not (Test-FakeSupportOption 'data-root')) { exit 2 }
        Write-Output '{"tables":{},"taxonomy":{}}'
        exit 0
    }
    'native' {
        if ($action -ne 'lookup') { exit 2 }
        if (-not (Test-FakeSupportOption 'automation-id')) { exit 2 }
        if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_NATIVE_JSON)) {
            [Console]::Out.Write($env:TRACEFOLD_FAKE_NATIVE_JSON)
            exit 0
        }
        $available = $true
        if ($env:TRACEFOLD_FAKE_NATIVE_AVAILABLE -ceq 'false') { $available = $false }
        if ($env:TRACEFOLD_FAKE_NATIVE_AVAILABLE -ceq 'null') { $available = $null }
        $outer = 1; $descendant = 1
        if (-not [string]::IsNullOrWhiteSpace($env:TRACEFOLD_FAKE_NATIVE_OUTER)) { [void][int]::TryParse($env:TRACEFOLD_FAKE_NATIVE_OUTER, [ref] $outer) }
        if (-not [string]::IsNullOrWhiteSpace($env:TRACEFOLD_FAKE_NATIVE_DESCENDANT)) { [void][int]::TryParse($env:TRACEFOLD_FAKE_NATIVE_DESCENDANT, [ref] $descendant) }
        $className = if ($values.ContainsKey('descendant-class')) { [string] $values['descendant-class'] } else { 'Fake.Container' }
        if (-not [string]::IsNullOrWhiteSpace($env:TRACEFOLD_FAKE_NATIVE_CLASS)) { $className = $env:TRACEFOLD_FAKE_NATIVE_CLASS }
        $name = [string] $values['automation-id']
        if (-not [string]::IsNullOrWhiteSpace($env:TRACEFOLD_FAKE_NATIVE_NAME)) { $name = $env:TRACEFOLD_FAKE_NATIVE_NAME }
        $width = 176
        if (-not [string]::IsNullOrWhiteSpace($env:TRACEFOLD_FAKE_NATIVE_WIDTH)) { [void][int]::TryParse($env:TRACEFOLD_FAKE_NATIVE_WIDTH, [ref] $width) }
        $facts = @()
        foreach ($pattern in @($patterns)) { $facts += [ordered]@{ name = [string] $pattern; available = $available } }
        [Console]::Out.Write(([pscustomobject] @{
            automationId         = [string] $values['automation-id']
            descendantClass      = if ($values.ContainsKey('descendant-class')) { [string] $values['descendant-class'] } else { $null }
            descendantName       = if ($values.ContainsKey('descendant-name')) { [string] $values['descendant-name'] } else { $null }
            outerMatchCount      = $outer
            descendantMatchCount = $descendant
            name                 = $name
            controlType          = 'Group'
            className            = $className
            isEnabled            = $true
            isOffscreen          = $false
            x = 0; y = 0
            width                = $width
            height               = 40
            patterns             = $facts
        } | ConvertTo-Json -Depth 6 -Compress))
        exit 0
    }
    default { exit 2 }
}
