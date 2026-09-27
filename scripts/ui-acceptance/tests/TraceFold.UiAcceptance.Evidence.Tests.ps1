function Get-TestCases {
    return @(
        @{
            Name = 'evidence persists six redacted schema checked artifacts'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-ev-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                $out = Join-Path $base 'durable'
                [void][IO.Directory]::CreateDirectory($root)
                $png = Join-Path $root 'raw.png'
                [IO.File]::WriteAllBytes($png, (Get-UiTestPngBytes))
                try {
                    $token = 'secret-token'
                    $collect = @{
                        screenshot = { $png }
                        uiaTree = { '{"tree":[]}' }
                        lastCommand = { '{"status":"Failed","message":"secret-token"}' }
                        productLog = { '{"events":[{"message":"secret-token at C:\\Users\\someone\\log.txt"}]}' }
                        stateSnapshot = { '{"tables":{},"taxonomy":{}}' }
                        processLedger = { 'pid=123 secret-token' }
                    }
                    $refs = Write-UiFailureEvidence -OwnedRoot $root -OutputDirectory $out -ScenarioId 'startup' -StepId 'open' -Collectors $collect -RunToken $token
                    Assert-Equal 6 @($refs.PSObject.Properties).Count
                    Assert-True ([IO.File]::Exists((Join-Path $out $refs.screenshot)))
                    $text = Get-Content -LiteralPath (Join-Path $out $refs.lastCommand) -Raw
                    Assert-NotContains $text $token
                    Assert-Contains $text '[redacted-token]'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'report refuses absolute evidence path and forbidden key'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-report-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $report = [ordered]@{
                        acceptanceId='run-1';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@([ordered]@{status='Passed';evidencePaths=@('screenshot.png')})})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                    }
                    $result = Write-UiAcceptanceReport -OutputDirectory $base -Report $report
                    Assert-Equal 'Passed' $result.Status
                    Assert-True ([IO.File]::Exists((Join-Path $base 'summary.txt')))
                    $report.scenarios[0].steps[0].evidencePaths = @('C:\\Users\\person\\raw.png')
                    Assert-Throws { Write-UiAcceptanceReport -OutputDirectory $base -Report $report }
                    $report.scenarios[0].steps[0].evidencePaths = @('screenshot.png')
                    $report['token'] = 'secret-token'
                    Assert-Throws { Write-UiAcceptanceReport -OutputDirectory $base -Report $report }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'failed collector still produces an unavailable evidence record'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-ev-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                try {
                    $collect = @{
                        screenshot={ throw 'capture failed' }
                        uiaTree={ '{"tree":[]}' }
                        lastCommand={ '{"status":"Failed"}' }
                        productLog={ '{"events":[]}' }
                        stateSnapshot={ '{"tables":{},"taxonomy":{}}' }
                        processLedger={ '123|456|C:\Users\someone\TraceFold.Media.exe' }
                    }
                    $out = Join-Path $base 'out'
                    $refs = Write-UiFailureEvidence -OwnedRoot $root -OutputDirectory $out -ScenarioId 'a' -StepId 'b' -Collectors $collect
                    Assert-Equal 6 @($refs.PSObject.Properties).Count
                    $error = Get-Content -LiteralPath (Join-Path $out $refs.screenshot) -Raw | ConvertFrom-Json
                    Assert-Equal 'Unavailable' $error.status
                    $ledger = Get-Content -LiteralPath (Join-Path $out $refs.processLedger) -Raw
                    Assert-NotContains $ledger '123|456'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        }
    )
}
