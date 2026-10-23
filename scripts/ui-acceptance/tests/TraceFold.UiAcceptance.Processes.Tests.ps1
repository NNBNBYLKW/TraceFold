function Get-TestCases {
    return @(
        @{
            Name = 'cleanup stops only exact recorded process identity in approved directory'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-owned-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                $exe = Join-Path $base 'TraceFold.Media.exe'
                [IO.File]::WriteAllBytes($exe, [byte[]]@(1))
                try {
                $ledger = @(
                    [pscustomobject]@{ProcessId=11;StartUtcTicks=100;ExecutablePath=$exe},
                    [pscustomobject]@{ProcessId=12;StartUtcTicks=200;ExecutablePath=$exe},
                    [pscustomobject]@{ProcessId=13;StartUtcTicks=300;ExecutablePath=(Join-Path ([IO.Path]::GetTempPath()) 'ordinary/TraceFold.Media.exe')}
                )
                $script:stoppedPids = @()
                $inventory = {
                    param($id)
                    if ($id -eq 11) { return [pscustomobject]@{ProcessId=11;StartUtcTicks=100;ExecutablePath=$exe} }
                    if ($id -eq 12) { return [pscustomobject]@{ProcessId=12;StartUtcTicks=201;ExecutablePath=$exe} }
                    return $null
                }
                $stop = { param($id) $script:stoppedPids += $id }
                $result = @(Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($base) -Inventory $inventory -Stop $stop)
                Assert-Equal 1 $result.Count
                Assert-Equal 11 $result[0]
                Assert-Equal 1 $script:stoppedPids.Count
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'preflight blocks unavailable driver and ordinary process'
            Body = {
                $a = Assert-TraceFoldPreflight -ExecutablePath 'Z:\none\TraceFold.Media.exe' -DriverPath 'Z:\none\winapp.exe' -DriverManifestPath 'Z:\none\manifest.json' -DriverArchivePath 'Z:\none\winapp.zip' -OutputDirectory 'Z:\none\out' -DesktopCheck { $false } -OrdinaryProcessCheck { $false }
                Assert-Equal 'Blocked' $a.Status
                $b = Assert-TraceFoldPreflight -ExecutablePath 'Z:\none\TraceFold.Media.exe' -DriverPath 'Z:\none\winapp.exe' -DriverManifestPath 'Z:\none\manifest.json' -DriverArchivePath 'Z:\none\winapp.zip' -OutputDirectory 'Z:\none\out' -DesktopCheck { $true } -OrdinaryProcessCheck { $true }
                Assert-Equal 'Blocked' $b.Status
            }
        },
        @{
            Name = 'registers only verified FFmpeg descendants of owned process'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-proc-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $product = Join-Path $base 'TraceFold.Media.exe'
                    $ffmpeg = Join-Path $base 'ffmpeg.exe'
                    [IO.File]::WriteAllBytes($product, [byte[]]@(1))
                    [IO.File]::WriteAllBytes($ffmpeg, [byte[]]@(1))
                    $userTool = Join-Path ([IO.Path]::GetTempPath()) 'ordinary/ffmpeg.exe'
                    $ledger = @([pscustomobject]@{ProcessId=10;StartUtcTicks=100;ExecutablePath=$product})
                    $inventory = {
                        @(
                            [pscustomobject]@{ProcessId=10;ParentProcessId=1;StartUtcTicks=100;ExecutablePath=$product},
                            [pscustomobject]@{ProcessId=11;ParentProcessId=10;StartUtcTicks=200;ExecutablePath=$ffmpeg},
                            [pscustomobject]@{ProcessId=12;ParentProcessId=10;StartUtcTicks=300;ExecutablePath=$userTool},
                            [pscustomobject]@{ProcessId=13;ParentProcessId=99;StartUtcTicks=400;ExecutablePath=$ffmpeg}
                        )
                    }
                    $support = Join-Path $script:uiRoot 'tests/fixtures/fake-support.ps1'
                    $added = @(Register-OwnedToolDescendants -OwnedRoot $base -RunToken 'test-token' -SupportPath $support -Ledger $ledger -ApprovedDirectories @($base) -Inventory $inventory)
                    Assert-Equal 1 $added.Count
                    Assert-Equal 11 $added[0].ProcessId
                    Assert-Contains (Get-Content -LiteralPath (Join-Path $base 'owned-processes.txt') -Raw) '11|200|'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'scenario launch injects data root only into child and writes identity ledger'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-proc-' + [guid]::NewGuid().ToString('N'))
                $root = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($root)
                $exe = Join-Path $base 'TraceFold.Media.exe'
                [IO.File]::WriteAllBytes($exe, [byte[]]@(1))
                $before = $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT
                try {
                    $script:childRoot = ''
                    $starter = {
                        param($path,$childEnvironment)
                        $script:childRoot = $childEnvironment.TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT
                        [pscustomobject]@{ProcessId=321;StartUtcTicks=654;ExecutablePath=$path}
                    }
                    $support = Join-Path $script:uiRoot 'tests/fixtures/fake-support.ps1'
                    $record = Start-TraceFoldScenario -ExecutablePath $exe -OwnedRoot $root -RunToken 'test-token' -SupportPath $support -StartProcess $starter
                    Assert-Equal $root $script:childRoot
                    Assert-Equal $before $env:TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT
                    Assert-Equal 321 $record.ProcessId
                    Assert-Contains (Get-Content -LiteralPath (Join-Path $root 'owned-processes.txt') -Raw) '321|654|'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        }
    )
}
