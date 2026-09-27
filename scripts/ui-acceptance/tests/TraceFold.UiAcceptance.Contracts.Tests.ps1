# TraceFold UI acceptance toolkit - shared contract tests.
#
# These cases only exercise the pure helpers in TraceFold.UiAcceptance.Contracts.

function Get-TestCases {
    return @(
        @{
            Name = 'exit codes follow the documented mapping'
            Body = {
                Assert-Equal 0 (Get-UiAcceptanceExitCode -Name 'Success')
                Assert-Equal 2 (Get-UiAcceptanceExitCode -Name 'InvalidArguments')
                Assert-Equal 3 (Get-UiAcceptanceExitCode -Name 'UnsafeOwnership')
                Assert-Equal 4 (Get-UiAcceptanceExitCode -Name 'SnapshotUnavailable')
                Assert-Equal 5 (Get-UiAcceptanceExitCode -Name 'WindowUnavailable')
                Assert-Equal 10 (Get-UiAcceptanceExitCode -Name 'Unexpected')
            }
        },
        @{
            Name = 'status vocabulary is exactly the four documented statuses'
            Body = {
                $names = @(Get-UiAcceptanceStatusName)
                Assert-Equal 4 $names.Count
                foreach ($expected in @('Passed', 'Failed', 'Blocked', 'NotRun')) {
                    Assert-True (Test-UiAcceptanceStatus -Status $expected) ('expected status ' + $expected)
                }

                Assert-True (-not (Test-UiAcceptanceStatus -Status 'passed')) 'status comparison must be case sensitive'
                Assert-True (-not (Test-UiAcceptanceStatus -Status 'Unknown')) 'unknown status must be rejected'
                Assert-Throws { Assert-UiAcceptanceStatus -Status 'Unknown' -Field 'status' }
            }
        },
        @{
            Name = 'slug keeps identifiers only and never emits user values'
            Body = {
                Assert-Equal 'inbox-step-1-open' (ConvertTo-UiAcceptanceSlug -Value 'Inbox/Step 1: open!')
                Assert-Equal 'unnamed' (ConvertTo-UiAcceptanceSlug -Value '///')
                Assert-Equal 'unnamed' (ConvertTo-UiAcceptanceSlug -Value '')
                $long = ConvertTo-UiAcceptanceSlug -Value ('a' * 200) -MaximumLength 20
                Assert-Equal 20 $long.Length
                Assert-NotContains $long '/' 'slug must not contain path separators'
            }
        },
        @{
            Name = 'redaction removes root token user host ids and absolute paths'
            Body = {
                $root = 'C:\Users\someone\AppData\Local\Temp\TraceFold-UIA-abc'
                $token = 'secret-token-value'
                $guid = '11111111-1111-1111-1111-111111111111'
                $text = 'failed at ' + $root + '\results\report.json token=' + $token +
                        ' user=' + [Environment]::UserName + ' host=' + [Environment]::MachineName +
                        ' id=' + $guid + ' other=D:\media\clip.mp4'

                $safe = Protect-UiAcceptanceText -Value $text -RunToken $token -OwnedRoot $root

                Assert-NotContains $safe $root
                Assert-NotContains $safe $token
                Assert-NotContains $safe $guid
                Assert-NotContains $safe 'D:\media'
                if (-not [string]::IsNullOrEmpty([Environment]::UserName)) {
                    Assert-NotContains $safe ([Environment]::UserName)
                }
                Assert-Contains $safe '[redacted-path]'
            }
        },
        @{
            Name = 'dangerous report keys are refused'
            Body = {
                foreach ($key in @('token', 'OwnerToken', 'runId', 'userName', 'machineName', 'databasePath', 'pid', 'stdout')) {
                    Assert-True (Test-UiAcceptanceDangerousKey -Key $key) ('expected ' + $key + ' to be refused')
                }

                foreach ($key in @('automationId', 'status', 'durationMs', 'evidencePaths', 'controlType')) {
                    Assert-True (-not (Test-UiAcceptanceDangerousKey -Key $key)) ('expected ' + $key + ' to be allowed')
                }
            }
        },
        @{
            Name = 'driver JSON is parsed and rejected explicitly'
            Body = {
                $parsed = ConvertFrom-UiAcceptanceJson -Text '{"found":true}' -Source 'fake'
                Assert-True ($parsed.found -eq $true) 'expected found=true'
                Assert-Throws { ConvertFrom-UiAcceptanceJson -Text '' -Source 'fake' }
                Assert-Throws { ConvertFrom-UiAcceptanceJson -Text 'not json' -Source 'fake' }
            }
        },
        @{
            Name = 'required field validation lists what is missing'
            Body = {
                $record = [pscustomobject] @{ status = 'Passed'; scenario = 'entry' }
                Assert-UiAcceptanceFields -InputObject $record -Required @('status', 'scenario')

                $error = $null
                try { Assert-UiAcceptanceFields -InputObject $record -Required @('status', 'viewport', 'durationMs') }
                catch { $error = $_.Exception.Message }

                Assert-True ($null -ne $error) 'expected missing fields to throw'
                Assert-Contains $error 'viewport'
                Assert-Contains $error 'durationMs'
                Assert-NotContains $error 'status'
            }
        }
    )
}
