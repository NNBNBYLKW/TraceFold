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
                    $collectorOrder = New-Object System.Collections.Generic.List[string]
                    $collect = @{
                        screenshot = { $collectorOrder.Add('screenshot'); $png }
                        uiaTree = { '{"tree":[]}' }
                        lastCommand = { $collectorOrder.Add('lastCommand'); '{"status":"Failed","message":"secret-token"}' }
                        productLog = { '{"events":[{"message":"secret-token at C:\\Users\\someone\\log.txt"}]}' }
                        stateSnapshot = { '{"tables":{},"taxonomy":{}}' }
                        processLedger = { 'pid=123 secret-token' }
                    }
                    $refs = Write-UiFailureEvidence -OwnedRoot $root -OutputDirectory $out -ScenarioId 'startup' -StepId 'open' -Collectors $collect -RunToken $token
                    Assert-Equal 6 @($refs.PSObject.Properties).Count
                    Assert-Equal 'lastCommand' $collectorOrder[0]
                    Assert-Equal 'screenshot' $collectorOrder[1]
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
        },
        @{
            Name = 'a report without visual facts writes no index and keeps the report schema'
            Body = {
                # The index is an addition, not a new obligation: an ordinary report must produce
                # exactly the files it produced before this feature existed.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                $out = Join-Path $base 'durable'
                [void][IO.Directory]::CreateDirectory($out)
                try {
                    $report = [ordered]@{
                        acceptanceId='run-plain';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@([ordered]@{status='Passed';evidencePaths=@('screenshot.png')})})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                    }
                    $result = Write-UiAcceptanceReport -OutputDirectory $out -Report $report
                    Assert-Equal 'Passed' $result.Status
                    Assert-Equal 2 @([IO.Directory]::GetFileSystemEntries($out)).Count
                    Assert-True (-not [IO.File]::Exists((Join-Path $out 'index.html')))
                    Assert-True (-not [IO.File]::Exists((Join-Path $out 'screenshots.json')))
                    $written = Get-Content -LiteralPath (Join-Path $out 'report.json') -Raw
                    Assert-NotContains $written 'visual'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'visual facts through the report outlet write a relative index and a data document'
            Body = {
                # The page is produced by the same outlet that writes report.json, so the test calls
                # that outlet rather than a private helper.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                $out = Join-Path $base 'durable'
                [void][IO.Directory]::CreateDirectory((Join-Path $out 'shots'))
                [void][IO.Directory]::CreateDirectory((Join-Path $out 'uia'))
                try {
                    [IO.File]::WriteAllBytes((Join-Path $out 'shots\a.png'), (Get-UiTestPngBytes))
                    [IO.File]::WriteAllText((Join-Path $out 'uia\a.json'), '{"tree":[]}')
                    $report = [ordered]@{
                        acceptanceId='run-visual';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Failed';steps=@([ordered]@{status='Failed';evidencePaths=@('shots/a.png')})})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{
                            themeMatrix='light+dark';productHash='demo-product-hash';generatedUtc='2026-01-01T00:00:02Z'
                            synthetic=$false;themes=@();keyboard=@();status='Failed';reason='fixture'
                            entries=@([ordered]@{
                                state='library';status='Failed';theme='dark';viewport='1024x720';page='library';dialog='none'
                                clientWidth=1024;clientHeight=720;dpi=96;productHash='demo-product-hash';capturedUtc='2026-01-01T00:00:02Z'
                                image='shots\a.png';uia='uia\a.json'
                            })
                        }
                    }
                    $result = Write-UiAcceptanceReport -OutputDirectory $out -Report $report
                    Assert-Equal 'Failed' $result.Status
                    Assert-True ([IO.File]::Exists((Join-Path $out 'index.html')))
                    Assert-True ([IO.File]::Exists((Join-Path $out 'screenshots.json')))
                    $data = Get-Content -LiteralPath (Join-Path $out 'screenshots.json') -Raw | ConvertFrom-Json
                    Assert-Equal 1 @($data.entries).Count
                    Assert-Equal 'Failed' ([string] $data.entries[0].status)
                    Assert-Equal 'Failed' ([string] $data.status)
                    Assert-Equal 'shots/a.png' ([string] $data.entries[0].image)
                    Assert-Equal 'uia/a.json' ([string] $data.entries[0].uia)
                    Assert-Equal 'dark' ([string] $data.entries[0].theme)
                    Assert-Equal 1024 ([int] $data.entries[0].clientWidth)
                    $html = Get-Content -LiteralPath (Join-Path $out 'index.html') -Raw
                    Assert-Contains $html 'shots/a.png'
                    Assert-NotContains $html '__SCREENSHOTS_JSON__'
                    # report.json keeps its own schema: the matrix is a separate shared document.
                    # The check is the absent property, not a substring, because an ordinary value
                    # such as the acceptance id may legitimately contain the same word.
                    $writtenReport = Get-Content -LiteralPath (Join-Path $out 'report.json') -Raw | ConvertFrom-Json
                    Assert-True ($null -eq $writtenReport.PSObject.Properties['visual']) 'report.json carries no visual section'
                    Assert-Equal 'run-visual' ([string] $writtenReport.acceptanceId)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'visual index refuses absolute, traversing, mistyped and junction links before writing'
            Body = {
                # The rejection must happen before any successful artifact is persisted, so every
                # refused case is also checked for the absence of the report, the summary and both
                # index files.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory((Join-Path $base 'outside'))
                [IO.File]::WriteAllText((Join-Path $base 'outside\a.png'), 'external')
                $junctions = New-Object System.Collections.ArrayList
                try {
                    $cases = @(
                        @{ Link = 'C:\Users\person\shot.png'; Expect = 'must be relative' }
                        @{ Link = '../outside/a.png'; Expect = 'must not traverse' }
                        @{ Link = 'note.txt'; Expect = 'unexpected type' }
                        @{ Link = 'shots/a.png'; Expect = 'reparse' }
                    )
                    foreach ($case in $cases) {
                        $out = Join-Path $base ([guid]::NewGuid().ToString('N'))
                        [void][IO.Directory]::CreateDirectory($out)
                        [IO.File]::WriteAllText((Join-Path $out 'note.txt'), 'not a picture')
                        if ($case.Link -ceq 'shots/a.png') {
                            $junction = Join-Path $out 'shots'
                            $null = New-Item -ItemType Junction -Path $junction -Target (Join-Path $base 'outside')
                            [void] $junctions.Add($junction)
                        }
                        $report = [ordered]@{
                            acceptanceId='run-link';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                            scenarios=@([ordered]@{status='Passed';steps=@()})
                            cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                            visual=[ordered]@{status='Passed';entries=@([ordered]@{state='library';status='Passed';image=$case.Link})}
                        }
                        $message = ''
                        try { $null = Write-UiAcceptanceReport -OutputDirectory $out -Report $report }
                        catch { $message = $_.Exception.Message }
                        Assert-Contains $message $case.Expect ('refusal reason for ' + $case.Link)
                        foreach ($name in @('report.json', 'summary.txt', 'index.html', 'screenshots.json')) {
                            Assert-True (-not [IO.File]::Exists((Join-Path $out $name))) ('no ' + $name + ' after a refused link')
                        }
                    }
                }
                finally {
                    # A junction is deleted as a link, never followed: the shared test cleanup refuses
                    # reparse points, so the link itself is removed first.
                    foreach ($junction in $junctions) {
                        if ([IO.Directory]::Exists($junction)) { [IO.Directory]::Delete($junction) }
                    }
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a link name with a hash or a literal percent-escape is encoded and still names the file'
            Body = {
                # The browser URL is built from per-segment encoding, so '#' cannot become a fragment
                # and a literal '%2e%2e' directory cannot be decoded into a parent segment.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                $out = Join-Path $base 'durable'
                $folder = Join-Path $out 'shots\sub#1'
                [void][IO.Directory]::CreateDirectory($folder)
                try {
                    [IO.File]::WriteAllBytes((Join-Path $folder 'a %2e%2e.png'), (Get-UiTestPngBytes))
                    $report = [ordered]@{
                        acceptanceId='run-encode';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@()})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{status='Passed';entries=@([ordered]@{state='library';status='Passed';image='shots\sub#1\a %2e%2e.png'})}
                    }
                    $null = Write-UiAcceptanceReport -OutputDirectory $out -Report $report
                    $data = Get-Content -LiteralPath (Join-Path $out 'screenshots.json') -Raw | ConvertFrom-Json
                    $url = [string] $data.entries[0].image
                    Assert-Equal 'shots/sub%231/a%20%252e%252e.png' $url
                    $decoded = @(@($url -split '/') | ForEach-Object { [Uri]::UnescapeDataString($_) })
                    Assert-Equal 'shots/sub#1/a %2e%2e.png' ($decoded -join '/')
                    Assert-True ([IO.File]::Exists((Join-Path $out (($decoded -join '/') -replace '/', '\')))) 'the URL still names the verified file'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a link whose name redaction would rewrite is refused instead of published'
            Body = {
                # GUID-shaped and token-bearing names are exactly what the shared redaction rewrites.
                # Publishing the rewritten link would point at a file that was never verified.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $guidName = '01234567-89ab-cdef-0123-456789abcdef.png'
                    $out = Join-Path $base 'guid'
                    [void][IO.Directory]::CreateDirectory($out)
                    [IO.File]::WriteAllBytes((Join-Path $out $guidName), (Get-UiTestPngBytes))
                    $report = [ordered]@{
                        acceptanceId='run-guid';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@()})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{status='Passed';entries=@([ordered]@{state='library';status='Passed';image=$guidName})}
                    }
                    $guidMessage = ''
                    try { $null = Write-UiAcceptanceReport -OutputDirectory $out -Report $report } catch { $guidMessage = $_.Exception.Message }
                    Assert-Contains $guidMessage 'rewritten by redaction'
                    Assert-True (-not [IO.File]::Exists((Join-Path $out 'report.json'))) 'no report after a rewritten link'

                    $token = 'index-run-token-value'
                    $out2 = Join-Path $base 'token'
                    [void][IO.Directory]::CreateDirectory($out2)
                    [IO.File]::WriteAllBytes((Join-Path $out2 ('shot-' + $token + '.png')), (Get-UiTestPngBytes))
                    $report2 = [ordered]@{
                        acceptanceId='run-token';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@()})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{status='Passed';entries=@([ordered]@{state='library';status='Passed';image=('shot-' + $token + '.png')})}
                    }
                    $tokenMessage = ''
                    try { $null = Write-UiAcceptanceReport -OutputDirectory $out2 -Report $report2 -RunToken $token } catch { $tokenMessage = $_.Exception.Message }
                    Assert-Contains $tokenMessage 'rewritten by redaction'

                    # A token that URL encoding would expand: the escaped link no longer contains the
                    # raw token, so a check that runs only after encoding would publish a working
                    # pointer to a name the durable report refuses to print.
                    foreach ($rawToken in @('index token', 'index#token')) {
                        $encodedOut = Join-Path $base ([guid]::NewGuid().ToString('N'))
                        [void][IO.Directory]::CreateDirectory($encodedOut)
                        $encodedName = 'shot-' + $rawToken + '.png'
                        [IO.File]::WriteAllBytes((Join-Path $encodedOut $encodedName), (Get-UiTestPngBytes))
                        $encodedReport = [ordered]@{
                            acceptanceId='run-encoded-token';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                            scenarios=@([ordered]@{status='Passed';steps=@()})
                            cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                            visual=[ordered]@{status='Passed';entries=@([ordered]@{state='library';status='Passed';image=$encodedName})}
                        }
                        $encodedMessage = ''
                        try { $null = Write-UiAcceptanceReport -OutputDirectory $encodedOut -Report $encodedReport -RunToken $rawToken }
                        catch { $encodedMessage = $_.Exception.Message }
                        Assert-Contains $encodedMessage 'rewritten by redaction' ('raw token refusal: ' + $rawToken)
                        foreach ($name in @('report.json', 'summary.txt', 'index.html', 'screenshots.json')) {
                            Assert-True (-not [IO.File]::Exists((Join-Path $encodedOut $name))) ('no ' + $name + ' after a token-bearing link')
                        }
                    }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'synthetic accepts only booleans and keeps an unknown unknown'
            Body = {
                # true/false are facts; absent or null is an unknown source, never a definite false;
                # a string is refused rather than coerced.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    $make = {
                        param($synthetic, $entrySynthetic)
                        $out = Join-Path $base ([guid]::NewGuid().ToString('N'))
                        [void][IO.Directory]::CreateDirectory($out)
                        $visual = [ordered]@{ status='NotRun'; entries=@([ordered]@{state='library';status='NotRun';synthetic=$entrySynthetic}) }
                        if ($null -ne $synthetic) { $visual['synthetic'] = $synthetic }
                        $report = [ordered]@{
                            acceptanceId='run-synth';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                            scenarios=@([ordered]@{status='NotRun';steps=@()})
                            cleanup=[ordered]@{status='NotRun'};cleanVmStatus='NotRun'
                            visual=$visual
                        }
                        $null = Write-UiAcceptanceReport -OutputDirectory $out -Report $report
                        return (Get-Content -LiteralPath (Join-Path $out 'screenshots.json') -Raw | ConvertFrom-Json)
                    }
                    $trueData = & $make $true $null
                    Assert-Equal $true ([bool] $trueData.synthetic)
                    Assert-True ($null -eq $trueData.entries[0].synthetic) 'an entry without a flag stays unknown'
                    $falseData = & $make $false $null
                    Assert-Equal $false ([bool] $falseData.synthetic)
                    $absent = & $make $null $null
                    Assert-True ($null -ne $absent.PSObject.Properties['synthetic']) 'the key is present'
                    Assert-True ($null -eq $absent.synthetic) 'an absent flag stays unknown'
                    $mixed = & $make $true $false
                    Assert-Equal $true ([bool] $mixed.synthetic)
                    Assert-Equal $false ([bool] $mixed.entries[0].synthetic) 'the false entry stays false'
                    # The page banner describes the matrix, not every entry on it. A mixed matrix
                    # (top-level true, entry false or unknown) must not be summarised as all-synthetic.
                    $mixedOut = Join-Path $base 'mixed-page'
                    [void][IO.Directory]::CreateDirectory($mixedOut)
                    $mixedReport = [ordered]@{
                        acceptanceId='run-mixed';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='NotRun';steps=@()})
                        cleanup=[ordered]@{status='NotRun'};cleanVmStatus='NotRun'
                        visual=[ordered]@{
                            status='NotRun';synthetic=$true
                            entries=@(
                                [ordered]@{state='library';status='NotRun';synthetic=$false}
                                [ordered]@{state='settings';status='NotRun'}
                            )
                        }
                    }
                    $null = Write-UiAcceptanceReport -OutputDirectory $mixedOut -Report $mixedReport
                    $mixedPage = Get-Content -LiteralPath (Join-Path $mixedOut 'index.html') -Raw
                    Assert-Contains $mixedPage 'labelled by its own source'
                    Assert-True (-not $mixedPage.Contains('on this page were generated')) 'the banner does not claim every entry is synthetic'
                    $badOut = Join-Path $base 'string-flag'
                    [void][IO.Directory]::CreateDirectory($badOut)
                    $bad = [ordered]@{
                        acceptanceId='run-bad';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@()})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{status='Passed';synthetic='false';entries=@([ordered]@{state='library';status='Passed'})}
                    }
                    $message = ''
                    try { $null = Write-UiAcceptanceReport -OutputDirectory $badOut -Report $bad } catch { $message = $_.Exception.Message }
                    Assert-Contains $message 'boolean or null'
                    $badEntryOut = Join-Path $base 'string-entry-flag'
                    [void][IO.Directory]::CreateDirectory($badEntryOut)
                    $badEntry = [ordered]@{
                        acceptanceId='run-bad2';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@()})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{status='Passed';entries=@([ordered]@{state='library';status='Passed';synthetic='false'})}
                    }
                    $entryMessage = ''
                    try { $null = Write-UiAcceptanceReport -OutputDirectory $badEntryOut -Report $badEntry } catch { $entryMessage = $_.Exception.Message }
                    Assert-Contains $entryMessage 'boolean or null'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an occupied index target refuses the run before any report is written'
            Body = {
                # The older generation must survive untouched: a new data document next to the old
                # page would be a mixture of two runs.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                $out = Join-Path $base 'durable'
                [void][IO.Directory]::CreateDirectory($out)
                try {
                    [IO.File]::WriteAllBytes((Join-Path $out 'a.png'), (Get-UiTestPngBytes))
                    [IO.File]::WriteAllText((Join-Path $out 'index.html'), 'old generation', [Text.UTF8Encoding]::new($false))
                    $before = (Get-FileHash -LiteralPath (Join-Path $out 'index.html') -Algorithm SHA256).Hash
                    $report = [ordered]@{
                        acceptanceId='run-collide';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@()})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{status='Passed';entries=@([ordered]@{state='library';status='Passed';image='a.png'})}
                    }
                    $message = ''
                    try { $null = Write-UiAcceptanceReport -OutputDirectory $out -Report $report } catch { $message = $_.Exception.Message }
                    Assert-Contains $message 'already exists'
                    Assert-Equal $before (Get-FileHash -LiteralPath (Join-Path $out 'index.html') -Algorithm SHA256).Hash 'the old page is unchanged'
                    Assert-True (-not [IO.File]::Exists((Join-Path $out 'report.json'))) 'no report next to the old page'
                    Assert-True (-not [IO.File]::Exists((Join-Path $out 'summary.txt'))) 'no summary next to the old page'
                    Assert-True (-not [IO.File]::Exists((Join-Path $out 'screenshots.json'))) 'no new data next to the old page'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a directory holding an output name refuses the run before any report is written'
            Body = {
                # A directory is not a file, so a File.Exists precheck alone passes and the report is
                # persisted before the create-new write fails on the directory. Both output names are
                # covered because either one can be occupied.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    foreach ($sentinel in @('index.html', 'screenshots.json')) {
                        $out = Join-Path $base ([guid]::NewGuid().ToString('N'))
                        [void][IO.Directory]::CreateDirectory($out)
                        $occupied = Join-Path $out $sentinel
                        [void][IO.Directory]::CreateDirectory($occupied)
                        [IO.File]::WriteAllText((Join-Path $occupied 'keep.txt'), 'sentinel', [Text.UTF8Encoding]::new($false))
                        [IO.File]::WriteAllBytes((Join-Path $out 'a.png'), (Get-UiTestPngBytes))
                        $report = [ordered]@{
                            acceptanceId='run-directory';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                            scenarios=@([ordered]@{status='Passed';steps=@()})
                            cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                            visual=[ordered]@{status='Passed';entries=@([ordered]@{state='library';status='Passed';image='a.png'})}
                        }
                        $message = ''
                        try { $null = Write-UiAcceptanceReport -OutputDirectory $out -Report $report } catch { $message = $_.Exception.Message }
                        Assert-Contains $message 'already exists' ('directory refusal for ' + $sentinel)
                        Assert-True ([IO.Directory]::Exists($occupied)) ('the ' + $sentinel + ' directory survives')
                        Assert-Equal 'sentinel' ([IO.File]::ReadAllText((Join-Path $occupied 'keep.txt'))) ('the ' + $sentinel + ' sentinel is untouched')
                        Assert-True (-not [IO.File]::Exists((Join-Path $out 'report.json'))) 'no report next to an occupied directory'
                        Assert-True (-not [IO.File]::Exists((Join-Path $out 'summary.txt'))) 'no summary next to an occupied directory'
                        foreach ($name in @('index.html', 'screenshots.json')) {
                            $target = Join-Path $out $name
                            if ($name -cne $sentinel) {
                                Assert-True (-not [IO.File]::Exists($target)) ('no new ' + $name + ' next to an occupied directory')
                            }
                        }
                    }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the runner carries an optional visual matrix to the report outlet'
            Body = {
                # This is the consumer path, not a direct writer call: the scenario returns Visual on
                # its outcome and the runner must hand it to the existing outlet.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-runner-' + [guid]::NewGuid().ToString('N'))
                $owned = Join-Path $base 'owned'
                [void][IO.Directory]::CreateDirectory($owned)
                $metadata = @{
                    sourceCommit='3285dc9';productHash='demo-product';productVersion='0.0.0';driverVersion='0.0.0'
                    driverHash='0';fixtureHashes=@();hostOs='windows';dpi=96;theme='light';viewport='1024x720'
                }
                try {
                    $withVisual = Join-Path $base 'run-visual'
                    $runOutput = $withVisual
                    $pngBytes = Get-UiTestPngBytes
                    $scenario = {
                        [IO.File]::WriteAllBytes((Join-Path $runOutput 'a.png'), $pngBytes)
                        [pscustomobject]@{
                            Status='Passed';Reason='';Artifacts=@('a.png');Viewports=@('1024x720')
                            Visual=[ordered]@{status='NotRun';synthetic=$true;entries=@([ordered]@{state='library';status='NotRun';theme='light';viewport='1024x720';page='library';image='a.png'})}
                        }
                    }.GetNewClosure()
                    $result = Invoke-UiAcceptanceRun -ScenarioId 'index-consumer' -Scenario $scenario -Cleanup {} -OwnedRoot $owned -OutputDirectory $withVisual -Metadata $metadata
                    $statusDetail = ''
                    if ([IO.File]::Exists([string] $result.ReportPath)) {
                        $reportText = [IO.File]::ReadAllText([string] $result.ReportPath)
                        try {
                            $failedReport = $reportText | ConvertFrom-Json
                            $statusDetail = 'reason=' + [string] @($failedReport.scenarios)[0].steps[0].reason
                        }
                        catch {
                            $statusDetail = 'report-json=' + $reportText
                        }
                    }
                    Assert-Equal 'Passed' ([string] $result.Status) $statusDetail
                    Assert-Equal 0 ([int] $result.ExitCode)
                    Assert-True ([IO.File]::Exists((Join-Path $withVisual 'index.html'))) 'the runner produced the page'
                    $data = Get-Content -LiteralPath (Join-Path $withVisual 'screenshots.json') -Raw | ConvertFrom-Json
                    Assert-Equal 1 @($data.entries).Count
                    Assert-Equal 'a.png' ([string] $data.entries[0].image)
                    Assert-Equal 'light' ([string] $data.entries[0].theme)
                    Assert-Equal $true ([bool] $data.synthetic)

                    $plain = Join-Path $base 'run-plain'
                    $plainScenario = {
                        [pscustomobject]@{ Status='Passed'; Reason=''; Artifacts=@(); Viewports=@() }
                    }
                    $plainResult = Invoke-UiAcceptanceRun -ScenarioId 'no-visual' -Scenario $plainScenario -Cleanup {} -OwnedRoot $owned -OutputDirectory $plain -Metadata $metadata
                    Assert-Equal 'Passed' ([string] $plainResult.Status)
                    Assert-True (-not [IO.File]::Exists((Join-Path $plain 'index.html'))) 'a scenario without Visual keeps the old shape'
                    Assert-True (-not [IO.File]::Exists((Join-Path $plain 'screenshots.json'))) 'a scenario without Visual writes no data document'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'visual index escapes script-closing text and redacts the token and the root'
            Body = {
                # The JSON is embedded in a script element, so a raw '<' from a scenario string could
                # close that element. Every '<' must be escaped, and the shared redaction still runs.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                $out = Join-Path $base 'durable'
                [void][IO.Directory]::CreateDirectory($out)
                try {
                    [IO.File]::WriteAllBytes((Join-Path $out 'a.png'), (Get-UiTestPngBytes))
                    $token = 'index-run-token-value'
                    $report = [ordered]@{
                        acceptanceId='run-escape';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@()})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{
                            status='Passed';reason=''
                            entries=@([ordered]@{
                                state='</script><script>alert(1)</script>';status='Passed';theme=('theme ' + $base)
                                viewport='1024x720';page='page';image='a.png';productHash=$token
                            })
                        }
                    }
                    $null = Write-UiAcceptanceReport -OutputDirectory $out -Report $report -RunToken $token -OwnedRoot $base
                    $html = Get-Content -LiteralPath (Join-Path $out 'index.html') -Raw
                    Assert-Contains $html '\u003c/script'
                    Assert-NotContains $html '<script>alert(1)'
                    Assert-NotContains $html $token
                    Assert-NotContains $html $base
                    $data = Get-Content -LiteralPath (Join-Path $out 'screenshots.json') -Raw
                    Assert-NotContains $data $token
                    Assert-NotContains $data $base
                    Assert-Contains $data '[redacted-token]'
                    Assert-Contains $data '[redacted-path]'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an empty or not-run matrix stays empty and is never upgraded to passed'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                $out = Join-Path $base 'durable'
                [void][IO.Directory]::CreateDirectory($out)
                try {
                    $report = [ordered]@{
                        acceptanceId='run-empty';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Blocked';steps=@()})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{status='NotRun';reason='';entries=@()}
                    }
                    $null = Write-UiAcceptanceReport -OutputDirectory $out -Report $report
                    $data = Get-Content -LiteralPath (Join-Path $out 'screenshots.json') -Raw | ConvertFrom-Json
                    Assert-Equal 0 @($data.entries).Count
                    Assert-Equal 'NotRun' ([string] $data.status)
                    $html = Get-Content -LiteralPath (Join-Path $out 'index.html') -Raw
                    Assert-Contains $html 'No matrix entries were written.'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a synthetic matrix is marked synthetic in both the data and the page'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-index-' + [guid]::NewGuid().ToString('N'))
                $out = Join-Path $base 'durable'
                [void][IO.Directory]::CreateDirectory($out)
                try {
                    [IO.File]::WriteAllBytes((Join-Path $out 'a.png'), (Get-UiTestPngBytes))
                    $report = [ordered]@{
                        acceptanceId='run-synthetic';checkpoint='5CC';startedUtc='2026-01-01T00:00:00Z';endedUtc='2026-01-01T00:00:01Z'
                        scenarios=@([ordered]@{status='Passed';steps=@()})
                        cleanup=[ordered]@{status='Passed'};cleanVmStatus='NotRun'
                        visual=[ordered]@{status='Passed';synthetic=$true;entries=@([ordered]@{state='library';status='Passed';image='a.png'})}
                    }
                    $null = Write-UiAcceptanceReport -OutputDirectory $out -Report $report
                    $data = Get-Content -LiteralPath (Join-Path $out 'screenshots.json') -Raw | ConvertFrom-Json
                    Assert-Equal $true ([bool] $data.synthetic)
                    $html = Get-Content -LiteralPath (Join-Path $out 'index.html') -Raw
                    Assert-Contains $html 'Synthetic matrix'
                    Assert-Contains $html 'not a real product screenshot'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'durable projection returns a new redacted tree and keeps safe identity facts'
            Body = {
                $token = 'projection-secret-token'
                $root = 'C:\Users\someone\owned-run'
                $executable = Join-Path $root 'TraceFold.Media.exe'
                $hash = 'A'.PadRight(64, 'A')
                $raw = [ordered] @{
                    runTokenState = '[redacted-token]'
                    ownedRootState = '[redacted-path]'
                    message = ('command failed for ' + $token + ' at ' + $executable)
                    pid = 42
                    processId = [long] 43
                    exitCode = 1
                    exitObserved = $true
                    imageSha256 = $hash
                    close = [ordered] @{
                        processId = 44
                        startUtcTicks = [long] 638000000000000000
                        accepted = $true
                    }
                    items = @([ordered] @{ message = ('item failed at ' + $executable + ' with ' + $token) })
                }

                $safe = ConvertTo-UiDurableEvidenceProjection -Value $raw `
                    -RunToken $token -OwnedRoot $root -ExecutablePath $executable

                Assert-Equal '[redacted-token]' ([string] $safe.runTokenState)
                Assert-Equal '[redacted-path]' ([string] $safe.ownedRootState)
                Assert-NotContains ([string] $safe.message) $token
                Assert-NotContains ([string] $safe.message) $executable
                Assert-Contains ([string] $safe.message) '[redacted-token]'
                Assert-Contains ([string] $safe.message) '[redacted-path]'
                Assert-Equal 42 ([int] $safe.pid)
                Assert-Equal 43 ([long] $safe.processId)
                Assert-Equal 1 ([int] $safe.exitCode)
                Assert-Equal $true ([bool] $safe.exitObserved)
                Assert-Equal $hash ([string] $safe.imageSha256)
                Assert-Equal 44 ([int] $safe.close.processId)
                Assert-Equal ([long] 638000000000000000) ([long] $safe.close.startUtcTicks)
                Assert-Equal 1 @($safe.items).Count
                Assert-NotContains ([string] @($safe.items)[0].message) $token
                Assert-NotContains ([string] @($safe.items)[0].message) $executable
                Assert-Contains ([string] @($safe.items)[0].message) '[redacted-token]'
                Assert-Contains ([string] @($safe.items)[0].message) '[redacted-path]'
                # The source is an input only; every redaction lives in the returned tree.
                Assert-Equal ('command failed for ' + $token + ' at ' + $executable) ([string] $raw.message)
                Assert-Equal 44 ([int] $raw.close.processId)
                Assert-Throws {
                    ConvertTo-UiDurableEvidenceProjection -Value ([ordered] @{ token = 'forbidden' }) `
                        -RunToken $token -OwnedRoot $root -ExecutablePath $executable
                }
                Assert-Throws {
                    ConvertTo-UiDurableEvidenceProjection -Value ([ordered] @{ processId = '43' }) `
                        -RunToken $token -OwnedRoot $root -ExecutablePath $executable
                }
                Assert-Throws {
                    ConvertTo-UiDurableEvidenceProjection -Value ([ordered] @{ runToken = $token }) `
                        -RunToken $token -OwnedRoot $root -ExecutablePath $executable
                }
                Assert-Throws {
                    ConvertTo-UiDurableEvidenceProjection -Value ([ordered] @{ executablePath = $executable }) `
                        -RunToken $token -OwnedRoot $root -ExecutablePath $executable
                }
                try { throw [InvalidOperationException]::new(('nested failure ' + $executable)) }
                catch {
                    $errorFact = ConvertTo-UiDurableEvidenceProjection -Value $_ `
                        -RunToken $token -OwnedRoot $root -ExecutablePath $executable
                    Assert-NotContains ([string] $errorFact.message) $executable
                    Assert-Contains ([string] $errorFact.message) '[redacted-path]'
                }
            }
        }
    )
}
