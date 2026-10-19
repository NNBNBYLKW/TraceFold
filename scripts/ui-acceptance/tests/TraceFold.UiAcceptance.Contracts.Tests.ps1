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
            Name = 'redaction removes compact product card ids without hiding fixture hashes'
            Body = {
                $id = '9a83076933364ef2a8653c1191256999'
                $hash = '7D1BD6791F1D40FFAFE6C7704FB2E71B6FBF6CBD26B71F9A45E62460D61312A6'
                $safe = Protect-UiAcceptanceText -Value ("InboxCard_$id LibraryCard_$id $hash")
                Assert-NotContains $safe $id
                Assert-Contains $safe '[redacted-id]'
                Assert-Contains $safe $hash
            }
        },
        @{
            Name = 'dangerous report keys are refused'
            Body = {
                foreach ($key in @('token', 'OwnerToken', 'runId', 'userName', 'machineName', 'databasePath', 'pid', 'stdout',
                        'handbackToken', 'handbackTokenDigest', 'receiptId')) {
                    Assert-True (Test-UiAcceptanceDangerousKey -Key $key) ('expected ' + $key + ' to be refused')
                }

                foreach ($key in @('automationId', 'status', 'durationMs', 'evidencePaths', 'controlType')) {
                    Assert-True (-not (Test-UiAcceptanceDangerousKey -Key $key)) ('expected ' + $key + ' to be allowed')
                }
            }
        },
        @{
            Name = 'a capability value is found by exact value, never by shape'
            Body = {
                $token = 'C' * 64
                Assert-Throws { Assert-UiAcceptanceNoCapabilityValue -Text ('mode=' + $token) -Value $token }
                Assert-UiAcceptanceNoCapabilityValue -Text 'mode=light' -Value $token
                Assert-UiAcceptanceNoCapabilityValue -Text ('mode=' + $token) -Value ''
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
        },
        @{
            Name = 'redaction keeps a serialized document parseable when a quoted no-space path is masked'
            Body = {
                $document = [ordered] @{
                    commandLine = '"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -File T:\TraceFold5CC\scripts\run-stage5cc-ui-acceptance.ps1'
                    scenario    = 'Conflict'
                    counter     = 7
                    flag        = $true
                }
                $json = ConvertTo-Json -InputObject $document -Depth 5
                $safe = Protect-UiAcceptanceText -Value $json -RunToken 'token-not-in-document' -OwnedRoot 'T:\root-not-in-document'
                $parsed = ConvertFrom-UiAcceptanceJson -Text $safe -Source 'document'
                Assert-True ($safe.Contains('[redacted-path]')) 'the path must be masked'
                Assert-True ($safe.Contains('\"[redacted-path]\"')) 'the escaped quotes around the mask must survive'
                Assert-NotContains $safe 'C:\WINDOWS'
                Assert-Equal 'Conflict' ([string] $parsed.scenario)
                Assert-Equal 7 ([int] $parsed.counter)
                Assert-True ($parsed.flag -eq $true) 'a boolean must stay a boolean'
            }
        },
        @{
            Name = 'redaction keeps the previous mask bound for spaced and bare paths and never rewrites other values'
            Body = {
                $hash = '7D1BD6791F1D40FFAFE6C7704FB2E71B6FBF6CBD26B71F9A45E62460D61312A6'
                $cjk = -join @([char]0x4F5C, [char]0x8005, [char]0x7532)
                $document = [ordered] @{
                    commandLine = '"C:\Program Files\WindowsApps\Microsoft.PowerShell_7.6.3.0_x64__8wekyb3d8bbwe\pwsh.exe" -NoLogo'
                    bare        = 'raw=C:\Users\someone\AppData\Local\Temp\x.json end'
                    hash        = $hash
                    number      = 12345
                    flag        = $false
                    when        = '2026-10-14T12:00:00.0000000Z'
                    cjk         = $cjk
                }
                $json = ConvertTo-Json -InputObject $document -Depth 5
                $safe = Protect-UiAcceptanceText -Value $json -RunToken 'token-not-in-document' -OwnedRoot 'T:\root-not-in-document'
                $parsed = ConvertFrom-UiAcceptanceJson -Text $safe -Source 'document'
                # The spaced and bare shapes keep exactly the mask bound they had before: the fix is
                # a no-op for them, proven against the previous pattern in the same run.
                $oldShape = [regex]::Replace($json, '[A-Za-z]:\\[^\s"'']*', '[redacted-path]', 'IgnoreCase')
                $newShape = [regex]::Replace($json, '[A-Za-z]:\\(?:[^\s"''\\]|\\(?!\\*"))*', '[redacted-path]', 'IgnoreCase')
                Assert-True ($oldShape -ceq $newShape) 'a spaced or bare path must keep its previous mask bound'
                Assert-True ($safe.Contains('[redacted-path]')) 'the paths must be masked'
                Assert-NotContains $safe 'C:\Program Files\WindowsApps'
                Assert-NotContains $safe 'C:\Users\someone'
                Assert-True ($safe.Contains($hash)) 'a fixture hash must not be rewritten'
                Assert-Equal 12345 ([int] $parsed.number)
                Assert-True ($parsed.flag -eq $false) 'a boolean must stay a boolean'
                Assert-True ($safe.Contains('2026-10-14T12:00:00.0000000Z')) 'an ISO instant must not be rewritten'
                Assert-Equal $cjk ([string] $parsed.cjk)
            }
        },
        @{
            Name = 'redaction keeps escape runs and multiple quoted paths valid and masks token and root occurrences'
            Body = {
                $token = 'sekrit-token'
                $root = 'C:\Users\someone\AppData\Local\Temp\TraceFold-UIA-abc'
                $document = [ordered] @{
                    trailing      = 'C:\dir\'
                    quotedNoSpace = '"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe"'
                    multi         = '"D:\aaa\b.exe" and "C:\ccc\d.exe" and bare E:\e\f.mp4'
                    tokenNote     = 'token=' + $token
                    rootNote      = $root + '\screenshots\x.png'
                }
                $json = ConvertTo-Json -InputObject $document -Depth 5
                $safe = Protect-UiAcceptanceText -Value $json -RunToken $token -OwnedRoot $root
                $parsed = ConvertFrom-UiAcceptanceJson -Text $safe -Source 'document'
                Assert-True ($safe.Contains('[redacted-token]')) 'the run token must be masked'
                Assert-NotContains $safe $token
                Assert-NotContains $safe $root
                Assert-NotContains $safe 'C:\WINDOWS'
                Assert-NotContains $safe 'D:\aaa'
                Assert-NotContains $safe 'E:\e\f.mp4'
                Assert-True ($safe.Contains('[redacted-path]')) 'the paths must be masked'
                Assert-True ($null -ne $parsed) 'the protected document must parse'
            }
        }
    @{
        Name = 'required fields are judged per container: dictionary keys, ordered keys and object properties'
        Body = {
            # A key that exists is present even when its value is null, zero, false or empty.
            Assert-UiAcceptanceFields -InputObject @{ a = 1; b = $null } -Required @('a', 'b')
            Assert-UiAcceptanceFields -InputObject ([ordered] @{ a = 1; b = 0; c = $false; d = @() }) -Required @('a', 'b', 'c', 'd')
            Assert-UiAcceptanceFields -InputObject ([pscustomobject] @{ a = 1; b = $null }) -Required @('a', 'b')

            # The same three containers must still refuse a missing field.
            $refused = 0
            foreach ($document in @(@{ a = 1 }, ([ordered] @{ a = 1 }), ([pscustomobject] @{ a = 1 }))) {
                $threw = $false
                try { Assert-UiAcceptanceFields -InputObject $document -Required @('a', 'missing') }
                catch { $threw = $true }
                if ($threw) { $refused++ }
            }
            Assert-Equal 3 $refused 'every container refuses a missing field'
        }
    }
    )
}
