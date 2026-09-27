function Get-TestCases {
    $script:fake = Join-Path $script:uiRoot 'tests/fixtures/fake-winapp.ps1'
    return @(
        @{
            Name = 'driver preserves argv boundaries and PID'
            Body = {
                $log = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-argv-' + [guid]::NewGuid().ToString('N') + '.jsonl')
                try {
                    $env:TRACEFOLD_FAKE_ARGV_LOG = $log
                    Reset-UiAcceptanceMutationSteps
                    $r = Invoke-WinAppMutation -StepId 'value-1' -Arguments @('ui','set-value','NameBox','hello world & whoami') -ProcessId 31415 -WinAppPath $fake
                    Assert-Equal 'Passed' $r.Status
                    $lines = @(Get-Content -LiteralPath $log)
                    Assert-Equal 1 $lines.Count
                    $argv = (ConvertFrom-Json $lines[0]).argv
                    Assert-Equal 'hello world & whoami' $argv[3]
                    Assert-Equal '31415' $argv[5]
                    Assert-Equal '--json' $argv[6]
                    Assert-Throws { Invoke-WinAppMutation -StepId 'value-1' -Arguments @('ui','invoke','NameBox') -ProcessId 31415 -WinAppPath $fake }
                    Assert-Equal 1 @(Get-Content -LiteralPath $log).Count
                }
                finally { Remove-Item Env:TRACEFOLD_FAKE_ARGV_LOG -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue }
            }
        },
        @{
            Name = 'observation retries transient failure within deadline'
            Body = {
                $state = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-state-' + [guid]::NewGuid().ToString('N'))
                try {
                    $env:TRACEFOLD_FAKE_STATE = $state
                    $env:TRACEFOLD_FAKE_FAIL_FIRST = '1'
                    $r = Invoke-WinAppObservation -Arguments @('ui','inspect') -ProcessId 99 -TimeoutSeconds 6 -WinAppPath $fake
                    Assert-Equal 'Passed' $r.Status
                    Assert-Equal 2 $r.Attempts
                    Assert-Equal 2 ([int](Get-Content -LiteralPath $state -Raw))
                }
                finally {
                    Remove-Item Env:TRACEFOLD_FAKE_STATE -ErrorAction SilentlyContinue
                    Remove-Item Env:TRACEFOLD_FAKE_FAIL_FIRST -ErrorAction SilentlyContinue
                    Remove-Item -LiteralPath $state -ErrorAction SilentlyContinue
                }
            }
        },
        @{
            Name = 'mutation timeout remains consumed and invalid JSON fails'
            Body = {
                Reset-UiAcceptanceMutationSteps
                try {
                    $env:TRACEFOLD_FAKE_HANG_MS = '2000'
                    $r = Invoke-WinAppMutation -StepId 'timeout-1' -Arguments @('ui','invoke','Go') -ProcessId 22 -WinAppPath $fake -TimeoutMs 100
                    Assert-True $r.TimedOut
                    Assert-Throws { Invoke-WinAppMutation -StepId 'timeout-1' -Arguments @('ui','invoke','Go') -ProcessId 22 -WinAppPath $fake }
                }
                finally { Remove-Item Env:TRACEFOLD_FAKE_HANG_MS -ErrorAction SilentlyContinue }
                $env:TRACEFOLD_FAKE_RAW = 'bad json'
                try {
                    $r = Invoke-WinAppObservation -Arguments @('ui','inspect') -ProcessId 22 -TimeoutSeconds 2 -WinAppPath $fake
                    Assert-Equal 'Failed' $r.Status
                    Assert-Equal 10 $r.ExitCode
                }
                finally { Remove-Item Env:TRACEFOLD_FAKE_RAW -ErrorAction SilentlyContinue }
            }
        },
        @{
            Name = 'driver stderr is captured and redacted'
            Body = {
                $env:TRACEFOLD_FAKE_STDERR = 'secret-token C:\Users\someone\error.log'
                $env:TRACEFOLD_FAKE_EXIT_CODE = '1'
                try {
                    Reset-UiAcceptanceMutationSteps
                    $r = Invoke-WinAppMutation -StepId 'stderr-1' -Arguments @('ui','invoke','Go') -ProcessId 22 -WinAppPath $script:fake -RunToken 'secret-token'
                    Assert-Equal 'Failed' $r.Status
                    Assert-NotContains $r.Error 'secret-token'
                    Assert-NotContains $r.Error 'C:\Users\someone'
                }
                finally {
                    Remove-Item Env:TRACEFOLD_FAKE_STDERR -ErrorAction SilentlyContinue
                    Remove-Item Env:TRACEFOLD_FAKE_EXIT_CODE -ErrorAction SilentlyContinue
                }
            }
        },
        @{
            Name = 'keyboard mutation requires owned foreground and unlocked desktop'
            Body = {
                Reset-UiAcceptanceMutationSteps
                $log = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-argv-' + [guid]::NewGuid().ToString('N') + '.jsonl')
                try {
                    $env:TRACEFOLD_FAKE_ARGV_LOG = $log
                    $blocked = Invoke-WinAppKeyboard -StepId 'key-1' -Arguments @('ui','send-keys','Escape') -ProcessId 44 -WinAppPath $script:fake -ForegroundCheck { $false } -DesktopCheck { $true }
                    Assert-Equal 'Blocked' $blocked.Status
                    Assert-True (-not [IO.File]::Exists($log))
                    $allowed = Invoke-WinAppKeyboard -StepId 'key-1' -Arguments @('ui','send-keys','Escape') -ProcessId 44 -WinAppPath $script:fake -ForegroundCheck { param($id) $id -eq 44 } -DesktopCheck { $true }
                    Assert-Equal 'Passed' $allowed.Status
                    $argv = (Get-Content -LiteralPath $log -Raw | ConvertFrom-Json).argv
                    Assert-Equal '--via' $argv[3]
                    Assert-Equal 'send-input' $argv[4]
                    Assert-Throws { Invoke-WinAppKeyboard -StepId 'key-1' -Arguments @('ui','send-keys','Escape') -ProcessId 44 -WinAppPath $script:fake -ForegroundCheck { $true } -DesktopCheck { $true } }
                }
                finally { Remove-Item Env:TRACEFOLD_FAKE_ARGV_LOG -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue }
            }
        }
    )
}
