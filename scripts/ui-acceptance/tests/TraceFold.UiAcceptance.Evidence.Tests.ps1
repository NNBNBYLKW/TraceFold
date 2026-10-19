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
        },
        @{
            Name = 'UIA tree output keeps only reviewed fields and redacts names'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-ev-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $nodes = @([pscustomobject]@{
                        automationId='Button1';controlType='Button';name='C:\Users\someone\private.txt'
                        enabled=$true;offscreen=$false
                        bounds=[pscustomobject]@{x=1;y=2;width=3;height=4}
                        pid=123; raw='secret-token'
                    })
                    $name = Write-UiRedactedTreeEvidence -OutputDirectory $base -EvidenceId 'startup' -Nodes $nodes -RunToken 'secret-token'
                    $text = Get-Content -LiteralPath (Join-Path $base $name) -Raw
                    Assert-NotContains $text 'C:\Users\someone'
                    Assert-NotContains $text 'secret-token'
                    Assert-NotContains $text '123'
                    Assert-Contains $text 'Button1'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a node with no identity field is recorded with an empty one'
            Body = {
                # A list row has no automation id, so the normalized node states that field as empty.
                # That is a legitimate element, not a malformed one, and the evidence has to carry it
                # with the field present so a reader never has to tell absent from empty.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-ev-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $nodes = @([pscustomobject]@{
                        controlType='ListItem';name='a row';enabled=$true;offscreen=$false
                        bounds=[pscustomobject]@{x=0;y=0;width=10;height=10}
                    })
                    $name = Write-UiRedactedTreeEvidence -OutputDirectory $base -EvidenceId 'rows' -Nodes $nodes
                    $parsed = Get-Content -LiteralPath (Join-Path $base $name) -Raw | ConvertFrom-Json
                    $recorded = @($parsed.tree)
                    Assert-Equal 1 $recorded.Count
                    # The recorded file states the identity for every node, so a reader never has to
                    # guess whether the element had no id or the writer lost it.
                    Assert-Equal '' ([string] $recorded[0].automationId)
                    Assert-Equal 'ListItem' ([string] $recorded[0].controlType)
                    Assert-Equal 'a row' ([string] $recorded[0].name)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a node with no retrievable rectangle is recorded as unmeasured'
            Body = {
                # The driver can enumerate a virtualized element and still fail to retrieve its
                # rectangle, in which case the normalized node carries no rectangle at all. That
                # element is real and belongs in the evidence, and its absent rectangle must not be
                # reported as a position.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-ev-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $nodes = @([pscustomobject]@{
                        automationId='TaxonomyMergeAffectedFiles';controlType='List'
                        name='preview';enabled=$true;offscreen=$false
                    })
                    $name = Write-UiRedactedTreeEvidence -OutputDirectory $base -EvidenceId 'unmeasured' -Nodes $nodes
                    $parsed = Get-Content -LiteralPath (Join-Path $base $name) -Raw | ConvertFrom-Json
                    $recorded = @($parsed.tree)
                    Assert-Equal 1 $recorded.Count
                    # The rectangle is present with zeroes in every field: an unmeasured element is
                    # recorded as unmeasured, and never as an element sitting at the origin.
                    Assert-Equal 0 ([int] $recorded[0].bounds.width)
                    Assert-Equal 0 ([int] $recorded[0].bounds.height)
                    Assert-Equal $false ([bool] $recorded[0].offscreen)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a node with a partially reported rectangle is refused'
            Body = {
                # Half a rectangle describes no rectangle, so a node that arrives with only part of
                # its rectangle is refused instead of having the missing edges filled in with zeroes
                # that would then read as real edges of the element.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-ev-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $nodes = @([pscustomobject]@{
                        controlType='Button';name='half';enabled=$true;offscreen=$false
                        bounds=[pscustomobject]@{x=1;y=2}
                    })
                    Assert-Throws { Write-UiRedactedTreeEvidence -OutputDirectory $base -EvidenceId 'half' -Nodes $nodes }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a node missing its state is refused rather than recorded'
            Body = {
                # Enablement and visibility are the two facts every later check depends on, so an
                # element that arrives without them is refused instead of being written down as an
                # element whose state is unknown.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-ev-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $nodes = @([pscustomobject]@{ controlType='Button';name='no state' })
                    Assert-Throws { Write-UiRedactedTreeEvidence -OutputDirectory $base -EvidenceId 'bad' -Nodes $nodes }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'theme evidence writer keeps its five fields, redacts the path and token, and never overwrites'
            Body = {
                # The theme document is a harness fact, not a verdict: the writer must persist all
                # five fields, run the shared redaction, and refuse a second write of the same id
                # instead of replacing the first receipt.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-theme-' + [guid]::NewGuid().ToString('N'))
                $out = Join-Path $base 'durable'
                [void][IO.Directory]::CreateDirectory($base)
                [void][IO.Directory]::CreateDirectory($out)
                try {
                    $token = 'theme-run-token-value'
                    $document = [ordered] @{
                        mode    = 'Both'
                        apply   = [ordered] @{ status = 'Passed'; detail = ('applied under ' + $base) }
                        # The fixture token rides inside an ordinary value, so a zero-hit search
                        # alone cannot tell "replaced" from "dropped".
                        restore = [ordered] @{ status = 'Passed'; detail = ('restored by ' + $token) }
                        readback = [ordered] @{
                            status = 'Passed'
                            theme  = $null
                            count  = 0
                            frozen = $false
                            empty  = @()
                            single = @('only-one')
                            width  = [long] 9007199254740993
                        }
                        journal = [ordered] @{ status = 'absent'; path = $base }
                    }
                    $name = Write-UiThemeEvidence -OutputDirectory $out -EvidenceId 'theme-1' -Document $document -RunToken $token -OwnedRoot $base
                    Assert-Equal 'theme-1-theme.json' $name
                    $target = Join-Path $out $name
                    $text = Get-Content -LiteralPath $target -Raw
                    Assert-NotContains $text $base
                    Assert-NotContains $text $token
                    Assert-Contains $text '[redacted-path]'
                    Assert-Contains $text '[redacted-token]'
                    $parsed = $text | ConvertFrom-Json
                    foreach ($field in @('mode', 'apply', 'restore', 'readback', 'journal')) {
                        Assert-True ($null -ne $parsed.PSObject.Properties[$field]) ('the theme document carries ' + $field)
                    }
                    Assert-Equal 'Both' ([string] $parsed.mode)
                    Assert-Equal 'Passed' ([string] $parsed.apply.status)
                    Assert-Equal 'Passed' ([string] $parsed.restore.status)
                    Assert-Equal 'Passed' ([string] $parsed.readback.status)
                    Assert-Equal 'absent' ([string] $parsed.journal.status)

                    # Replacement is asserted on the exact field, with its surrounding text kept:
                    # a placeholder that merely appears somewhere in the file proves nothing.
                    Assert-Equal 'applied under [redacted-path]' ([string] $parsed.apply.detail)
                    Assert-Equal '[redacted-path]' ([string] $parsed.journal.path)
                    Assert-Equal 'restored by [redacted-token]' ([string] $parsed.restore.detail)

                    # The writer's own JSON round trip must not collapse the value classes.
                    $rb = $parsed.readback
                    Assert-True ($null -ne $rb.PSObject.Properties['theme']) 'a null field is still present'
                    Assert-True ($null -eq $rb.theme) 'an unknown stays null'
                    Assert-True ($null -ne $rb.PSObject.Properties['count']) 'a measured zero is present'
                    Assert-True ($rb.count -eq 0) 'a measured zero stays zero'
                    Assert-True ($rb.count.GetType().Name -in @('Int32', 'Int64')) 'the zero keeps an integer type'
                    Assert-Equal 'Boolean' $rb.frozen.GetType().Name 'false is a boolean'
                    Assert-Equal $false $rb.frozen 'false is not dropped'
                    Assert-Equal 'Object[]' $rb.empty.GetType().Name 'an empty array is still an array'
                    Assert-Equal 0 @($rb.empty).Count 'the empty array has no entries'
                    Assert-Equal 'Object[]' $rb.single.GetType().Name 'a one-element array is still an array'
                    Assert-Equal 1 @($rb.single).Count 'the one-element array keeps its entry'
                    Assert-Equal 'only-one' ([string] @($rb.single)[0]) 'the single entry is the value, not a property bag'
                    Assert-Equal 'Int64' $rb.width.GetType().Name 'an Int64 beyond double precision keeps its type'
                    Assert-Equal '9007199254740993' ([string] $rb.width) 'the Int64 value survives without rounding'

                    # A second write of the same id must fail, and the first file must be byte-identical
                    # afterwards: decoded text equality can hide an encoding-only rewrite.
                    $hashBefore = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
                    $bytesBefore = [IO.File]::ReadAllBytes($target)
                    $threw = $false
                    try { $null = Write-UiThemeEvidence -OutputDirectory $out -EvidenceId 'theme-1' -Document $document -RunToken $token -OwnedRoot $base }
                    catch { $threw = $true }
                    Assert-Equal $true $threw 'a duplicate evidence id is refused'
                    $hashAfter = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
                    $bytesAfter = [IO.File]::ReadAllBytes($target)
                    Assert-Equal $hashBefore $hashAfter 'the first file is unchanged after the refusal'
                    Assert-Equal $bytesBefore.Length $bytesAfter.Length 'the first file keeps its byte count'
                    for ($i = 0; $i -lt $bytesBefore.Length; $i++) {
                        if ($bytesBefore[$i] -ne $bytesAfter[$i]) { throw ('the first file changed at byte ' + $i) }
                    }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'theme evidence writer refuses a missing field or a live capability value and writes nothing'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-theme-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $token = 'theme-run-token-value'
                    $partial = [ordered] @{
                        mode     = 'Both'
                        apply    = [ordered] @{ status = 'Passed' }
                        restore  = [ordered] @{ status = 'Passed' }
                        readback = [ordered] @{ status = 'Passed' }
                    }
                    Assert-Throws { Write-UiThemeEvidence -OutputDirectory $base -EvidenceId 'missing' -Document $partial -RunToken $token -OwnedRoot $base }
                    $full = [ordered] @{
                        mode     = 'Both'
                        apply    = [ordered] @{ status = 'Passed' }
                        restore  = [ordered] @{ status = 'Passed' }
                        readback = [ordered] @{ status = 'Passed' }
                        journal  = [ordered] @{ status = 'present'; receipt = 'fixture-receipt-9f31' }
                    }
                    # A random-looking receipt is not found by shape; it is refused by exact value
                    # at the moment the document would be persisted.
                    Assert-Throws { Write-UiThemeEvidence -OutputDirectory $base -EvidenceId 'leak' -Document $full -RunToken $token -OwnedRoot $base -ForbiddenValue 'fixture-receipt-9f31' }
                    Assert-Equal 0 @([IO.Directory]::GetFileSystemEntries($base)).Count
                }
                finally { Remove-UiTestTree -Path $base }
            }
        }
    )
}
