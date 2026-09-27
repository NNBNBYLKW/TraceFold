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

if ($command -ne 'ui') {
    Write-FakeFailure -ExitCode 2 -Message ('unsupported command group: ' + $command)
}

switch ($sub) {
    'inspect' {
        [Console]::Out.Write('{"tree":[{"automationId":"TraceFoldMainWindow","controlType":"Window"},{"automationId":"NavInbox","controlType":"NavigationViewItem"}]}')
        exit 0
    }
    'search' {
        [Console]::Out.Write('{"matches":[{"automationId":"InboxInspectorOpenButton","controlType":"Button","name":"open inbox inspector"}]}')
        exit 0
    }
    'wait-for' {
        [Console]::Out.Write('{"found":true,"automationId":"InboxOperationStatus"}')
        exit 0
    }
    'screenshot' {
        [Console]::Out.Write('{"captured":true,"name":"step.png"}')
        exit 0
    }
    'get-value' {
        [Console]::Out.Write('{"value":"sample-value"}')
        exit 0
    }
    'get-property' {
        [Console]::Out.Write('{"property":"IsEnabled","value":true}')
        exit 0
    }
    'invoke' {
        [Console]::Out.Write('{"result":"invoked"}')
        exit 0
    }
    'set-value' {
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
