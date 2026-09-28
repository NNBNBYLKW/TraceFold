# TraceFold UI acceptance toolkit - fake driver fixture.
#
# Stands in for the pinned winapp driver so the toolkit self-tests never start
# TraceFold and never touch the registry. It records the raw argument array it was
# invoked with and returns fixed, parseable JSON per subcommand.
#
# Simulation switches (environment variables):
#   TRACEFOLD_FAKE_ARGV_LOG    file that receives one JSON line per invocation
#   TRACEFOLD_FAKE_STATE       counter file used for ordering sensitive behaviour
#   TRACEFOLD_FAKE_FAIL_FIRST  '1' fails the first invocation only (retry tests)
#   TRACEFOLD_FAKE_EXIT_CODE   fixed exit code for every invocation
#   TRACEFOLD_FAKE_STDERR      text written to stderr before exiting
#   TRACEFOLD_FAKE_HANG_MS     sleeps this long before answering (timeout tests)
#   TRACEFOLD_FAKE_RAW         text written verbatim to stdout (JSON validation tests)
#   TRACEFOLD_FAKE_UIA_TREE_SEQ file with a JSON array of inspect payloads, consumed in order
#   TRACEFOLD_FAKE_UIA_STATE   counter file for TRACEFOLD_FAKE_UIA_TREE_SEQ

$Arguments = [string[]] $args

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($null -eq $Arguments) { $Arguments = @() }

function Get-FakeInvocationCount {
    $statePath = $env:TRACEFOLD_FAKE_STATE
    if ([string]::IsNullOrEmpty($statePath)) { return 1 }

    $count = 0
    if (Test-Path -LiteralPath $statePath) {
        $raw = Get-Content -LiteralPath $statePath -Raw -ErrorAction SilentlyContinue
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            $parsed = 0
            if ([int]::TryParse($raw.Trim(), [ref] $parsed)) { $count = $parsed }
        }
    }

    $count = $count + 1
    Set-Content -LiteralPath $statePath -Value ([string] $count) -Encoding ASCII
    return $count
}

$invocation = Get-FakeInvocationCount

if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_ARGV_LOG)) {
    $record = @{ argv = $Arguments; invocation = $invocation }
    $record | ConvertTo-Json -Compress -Depth 6 |
        Add-Content -LiteralPath $env:TRACEFOLD_FAKE_ARGV_LOG -Encoding UTF8
}

if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_HANG_MS)) {
    $hang = 0
    if ([int]::TryParse($env:TRACEFOLD_FAKE_HANG_MS, [ref] $hang) -and $hang -gt 0) {
        Start-Sleep -Milliseconds $hang
    }
}

function Write-FakeFailure {
    param([int] $ExitCode, [string] $Message)

    if (-not [string]::IsNullOrEmpty($Message)) {
        [Console]::Error.WriteLine($Message)
    }

    exit $ExitCode
}

if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_RAW)) {
    [Console]::Out.Write($env:TRACEFOLD_FAKE_RAW)
    exit 0
}

if ($env:TRACEFOLD_FAKE_FAIL_FIRST -eq '1' -and $invocation -eq 1) {
    Write-FakeFailure -ExitCode 5 -Message 'driver reported the window as unavailable'
}

if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_STDERR)) {
    [Console]::Error.WriteLine($env:TRACEFOLD_FAKE_STDERR)
}

if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_EXIT_CODE)) {
    $fixed = 0
    if ([int]::TryParse($env:TRACEFOLD_FAKE_EXIT_CODE, [ref] $fixed)) {
        exit $fixed
    }
}

if ($Arguments.Count -lt 1) {
    Write-FakeFailure -ExitCode 2 -Message 'missing command'
}

$command = $Arguments[0]
$sub = ''
if ($Arguments.Count -ge 2) { $sub = $Arguments[1] }

function Get-FakeUiaPayload {
    $sequencePath = $env:TRACEFOLD_FAKE_UIA_TREE_SEQ
    if ([string]::IsNullOrWhiteSpace($sequencePath) -or -not (Test-Path -LiteralPath $sequencePath)) {
        return $null
    }

    $statePath = $env:TRACEFOLD_FAKE_UIA_STATE
    $index = 0
    if (-not [string]::IsNullOrWhiteSpace($statePath) -and (Test-Path -LiteralPath $statePath)) {
        $raw = Get-Content -LiteralPath $statePath -Raw -ErrorAction SilentlyContinue
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            [void][int]::TryParse($raw.Trim(), [ref] $index)
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($statePath)) {
        Set-Content -LiteralPath $statePath -Value ([string] ($index + 1)) -Encoding ASCII
    }

    $payloads = @(Get-Content -LiteralPath $sequencePath -Raw | ConvertFrom-Json)
    if ($index -ge $payloads.Count) { return $null }
    return [string] $payloads[$index]
}

function Get-FakeOption {
    param([string] $Name)
    for ($index = 0; $index -lt $Arguments.Count - 1; $index++) {
        if ($Arguments[$index] -ceq ('--' + $Name)) { return [string] $Arguments[$index + 1] }
    }
    return ''
}

function Get-FakeValuesPath {
    if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_VALUES)) {
        return $env:TRACEFOLD_FAKE_VALUES
    }
    if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_UIA_STATE)) {
        return ($env:TRACEFOLD_FAKE_UIA_STATE + '.values.json')
    }
    return ''
}

function Read-FakeValues {
    $values = @{}
    $path = Get-FakeValuesPath
    if (-not [string]::IsNullOrEmpty($path) -and (Test-Path -LiteralPath $path)) {
        $raw = Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            $parsed = $raw | ConvertFrom-Json
            foreach ($property in $parsed.PSObject.Properties) {
                $values[$property.Name] = [string] $property.Value
            }
        }
    }
    return $values
}

function Write-FakeValue {
    param([string] $Name, [string] $Value)
    $path = Get-FakeValuesPath
    if ([string]::IsNullOrEmpty($path)) { return }
    $values = Read-FakeValues
    $values[$Name] = $Value
    [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $values -Compress -Depth 6),
        [Text.UTF8Encoding]::new($false))
}

if ($command -ne 'ui') {
    Write-FakeFailure -ExitCode 2 -Message ('unsupported command group: ' + $command)
}

switch ($sub) {
    'inspect' {
        $payload = Get-FakeUiaPayload
        if (-not [string]::IsNullOrWhiteSpace($payload)) {
            [Console]::Out.Write($payload)
            exit 0
        }
        [Console]::Out.Write('{"tree":[{"automationId":"TraceFoldMainWindow","controlType":"Window"},{"automationId":"NavInbox","controlType":"NavigationViewItem"}]}')
        exit 0
    }
    'search' {
        if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_SEARCH_JSON)) {
            [Console]::Out.Write($env:TRACEFOLD_FAKE_SEARCH_JSON)
            exit 0
        }
        $selector = 'InboxInspectorOpenButton'
        if ($Arguments.Count -ge 3) { $selector = [string] $Arguments[2] }
        $match = [ordered]@{
            type = 'Button'; name = $selector; automationId = $selector; className = 'Button'
            isEnabled = $true; isOffscreen = $false
            x = 700; y = 60; width = 32; height = 32
            selector = $selector; isInvokable = $true
        }
        [Console]::Out.Write(([pscustomobject]@{ matchCount = 1; hasMore = $false; matches = @($match) } | ConvertTo-Json -Depth 6 -Compress))
        exit 0
    }
    'get-property' {
        if (-not [string]::IsNullOrEmpty($env:TRACEFOLD_FAKE_PROPERTY_JSON)) {
            [Console]::Out.Write($env:TRACEFOLD_FAKE_PROPERTY_JSON)
            exit 0
        }
        $selector = 'NavInbox'
        if ($Arguments.Count -ge 3) { $selector = [string] $Arguments[2] }
        # The pinned driver answers null for a pattern-availability property it does not know how
        # to read; the fake reproduces that so no test can mistake null for a pattern answer.
        $properties = [ordered]@{
            AutomationId = $selector; ControlType = 'ListItem'; IsEnabled = 'True'; IsOffscreen = 'False'
        }
        $propertyNameIndex = [Array]::IndexOf($Arguments, '--property')
        if ($propertyNameIndex -ge 0 -and $propertyNameIndex + 1 -lt $Arguments.Count) {
            $properties = [ordered]@{ ([string] $Arguments[$propertyNameIndex + 1]) = $null }
        }
        [Console]::Out.Write(([pscustomobject]@{ elementId = 'el-fake-0001'; properties = $properties } | ConvertTo-Json -Depth 6 -Compress))
        exit 0
    }
    'wait-for' {
        $target = 'InboxOperationStatus'
        if ($Arguments.Count -ge 3) { $target = [string] $Arguments[2] }
        [Console]::Out.Write(([pscustomobject]@{ found = $true; automationId = $target } | ConvertTo-Json -Compress))
        exit 0
    }
    'screenshot' {
        $output = Get-FakeOption 'output'
        if (-not [string]::IsNullOrWhiteSpace($output)) {
            $directory = Split-Path -Parent $output
            if (-not [string]::IsNullOrWhiteSpace($directory) -and
                -not [IO.Directory]::Exists($directory)) {
                [void][IO.Directory]::CreateDirectory($directory)
            }
            $bytes = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lXcAAAAASUVORK5CYII=')
            [IO.File]::WriteAllBytes($output, $bytes)
        }
        [Console]::Out.Write(([pscustomobject]@{ captured = $true; path = $output } | ConvertTo-Json -Compress))
        exit 0
    }
    'get-value' {
        # Values set through 'set-value' are read back, so the scenario's read-back assertions are
        # exercised rather than assumed. The initial map comes from a file the test writes.
        $values = Read-FakeValues
        $target = if ($Arguments.Count -ge 3) { [string] $Arguments[2] } else { '' }
        $text = ''
        if ($values.ContainsKey($target)) { $text = $values[$target] }
        [Console]::Out.Write(([pscustomobject]@{ text = $text } | ConvertTo-Json -Compress))
        exit 0
    }
    'invoke' {
        if ($Arguments.Count -ge 3 -and [string] $Arguments[2] -ceq 'SaveFolderSettings') {
            Write-FakeValue -Name 'SettingsStatusMessage' -Value 'Folder settings saved.'
        }
        [Console]::Out.Write('{"result":"invoked"}')
        exit 0
    }
    'set-value' {
        $target = if ($Arguments.Count -ge 3) { [string] $Arguments[2] } else { '' }
        $value = if ($Arguments.Count -ge 4) { [string] $Arguments[3] } else { '' }
        Write-FakeValue -Name $target -Value $value
        [Console]::Out.Write('{"result":"set"}')
        exit 0
    }
    'scroll-into-view' {
        [Console]::Out.Write('{"result":"scrolled"}')
        exit 0
    }
    'send-keys' {
        [Console]::Out.Write('{"result":"sent"}')
        exit 0
    }
    default {
        Write-FakeFailure -ExitCode 2 -Message ('unsupported ui command: ' + $sub)
    }
}
