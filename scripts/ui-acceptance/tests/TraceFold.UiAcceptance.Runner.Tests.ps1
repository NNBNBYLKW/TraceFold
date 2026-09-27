function Get-TestCases {
    $script:metadata = @{sourceCommit='abc';productHash='hash';productVersion='1';driverVersion='0.5.0';driverHash='hash';fixtureHashes=@();hostOs='Windows';dpi=96;theme='Light';viewport=@{width=1024;height=720}}
    return @(
        @{
            Name = 'runner cleans up and reports success'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                $script:cleanupCalled = 0
                try {
                    $result = Invoke-UiAcceptanceRun -ScenarioId 'startup' -Scenario { [pscustomobject]@{Status='Passed'} } -Cleanup { $script:cleanupCalled++ } -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata
                    Assert-Equal 0 $result.ExitCode
                    Assert-Equal 1 $script:cleanupCalled
                    $report = Get-Content -LiteralPath $result.ReportPath -Raw | ConvertFrom-Json
                    Assert-Equal 'Passed' $report.scenarios[0].status
                    Assert-Equal 'NotRun' $report.cleanVmStatus
                    Assert-True ($report.acceptanceId -match '^run-[0-9a-f]{32}$')
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'runner captures failure before cleanup and reports failure'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                $png = Join-Path $root 'shot.png'
                [IO.File]::WriteAllBytes($png, (Get-UiTestPngBytes))
                $script:order = @()
                try {
                    $collect = @{
                        screenshot={ $script:order += 'capture'; $png }
                        uiaTree={ '{"tree":[]}' }
                        lastCommand={ '{"status":"Failed"}' }
                        productLog={ '{"events":[]}' }
                        stateSnapshot={ '{"tables":{},"taxonomy":{}}' }
                        processLedger={ 'owned' }
                    }
                    $result = Invoke-UiAcceptanceRun -ScenarioId 'startup' -Scenario { throw 'product failure' } -Cleanup { $script:order += 'cleanup' } -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata -EvidenceCollectors $collect
                    Assert-Equal 1 $result.ExitCode
                    Assert-Equal 'capture' $script:order[0]
                    Assert-Equal 'cleanup' $script:order[1]
                    $report = Get-Content -LiteralPath $result.ReportPath -Raw | ConvertFrom-Json
                    Assert-Equal 6 @($report.scenarios[0].steps[0].evidencePaths).Count
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'runner cleanup executes after simulated interruption and timeout'
            Body = {
                foreach ($label in @('interrupt','timeout')) {
                    $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                    $root = Join-Path $base 'owned'
                    [void][IO.Directory]::CreateDirectory($root)
                    $script:cleanupCalled = 0
                    try {
                        $result = Invoke-UiAcceptanceRun -ScenarioId $label -Scenario { throw $label } -Cleanup { $script:cleanupCalled++ } -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata
                        Assert-Equal 1 $result.ExitCode
                        Assert-Equal 1 $script:cleanupCalled
                    }
                    finally { Remove-UiTestTree -Path $base }
                }
            }
        },
        @{
            Name = 'runner closes then stops verified process then requests owned root deletion'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                $log = Join-Path $base 'order.txt'
                $exe = Join-Path $base 'TraceFold.Media.exe'
                [IO.File]::WriteAllBytes($exe, [byte[]]@(1))
                $ledger = @([pscustomobject]@{ProcessId=77;StartUtcTicks=123;ExecutablePath=$exe})
                $inventory = { param($id) [pscustomobject]@{ProcessId=77;StartUtcTicks=123;ExecutablePath=$exe} }
                $stop = { param($id) Add-Content -LiteralPath $log -Value 'stop' }
                $close = { Add-Content -LiteralPath $log -Value 'close' }
                try {
                    $env:TRACEFOLD_FAKE_SUPPORT_LOG = $log
                    $support = Join-Path $script:uiRoot 'tests/fixtures/fake-support.ps1'
                    $result = Invoke-UiAcceptanceRun -ScenarioId 'cleanup' -Scenario { [pscustomobject]@{Status='Passed'} } -Cleanup $close -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata -RunToken 'test-token' -OwnedProcessLedger $ledger -ApprovedDirectories @($base) -SupportPath $support -ProcessInventory $inventory -ProcessStop $stop
                    Assert-Equal 0 $result.ExitCode
                    $lines = @(Get-Content -LiteralPath $log)
                    Assert-Equal 'close' $lines[0]
                    Assert-Equal 'stop' $lines[1]
                    Assert-Equal 'delete' $lines[2]
                }
                finally {
                    Remove-Item Env:TRACEFOLD_FAKE_SUPPORT_LOG -ErrorAction SilentlyContinue
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'cleanup failure prevents an overall pass'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-run-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                try {
                    $result = Invoke-UiAcceptanceRun -ScenarioId 'cleanup-fails' -Scenario { [pscustomobject]@{Status='Passed'} } -Cleanup { throw 'cleanup error' } -OwnedRoot $root -OutputDirectory (Join-Path $base 'out') -Metadata $script:metadata
                    Assert-Equal 1 $result.ExitCode
                    Assert-Equal 'Failed' $result.Status
                    $report = Get-Content -LiteralPath $result.ReportPath -Raw | ConvertFrom-Json
                    Assert-Equal 'Failed' $report.cleanup.status
                }
                finally { Remove-UiTestTree -Path $base }
            }
        }
    )
}
