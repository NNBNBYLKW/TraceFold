# Workflow scenario contract tests. These lock the shape and the safety rules of the independent
# Task 12 scenarios before any real window run, and they are deliberately source-based: a
# scenario that omits a safety rule must fail here rather than during a gate run.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-DiagnosticsScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/60-diagnostics-read-only.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'Diagnostics scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Get-ScenarioParameterNames {
    param([Parameter(Mandatory)][string] $Source)

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($Source, [ref]$tokens, [ref]$errors)
    if (@($errors).Count -gt 0) { throw 'The scenario does not parse.' }
    $names = @()
    foreach ($parameter in $ast.ParamBlock.Parameters) {
        $names += $parameter.Name.VariablePath.UserPath
    }
    return $names
}

function Read-ConflictScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/20-conflict-keep-both.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'Conflict keep-both scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Read-SkipScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/22-conflict-skip.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'Conflict skip scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Read-ReplaceScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/21-conflict-replace.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'Conflict replace scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Read-RetryScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/23-processing-failure-retry.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'Processing failure retry scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Read-BatchScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/30-batch-metadata-undo.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'Batch metadata undo scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Read-LibraryFilterScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/40-library-filter-naming-undo.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'Library filter and naming scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Get-TestCases {
    return @(
        @{
            Name = 'the diagnostics scenario exists, parses and reports Passed'
            Body = {
                $source = Read-DiagnosticsScenarioSource
                $tokens = $null
                $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput(
                    $source, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 @($errors).Count
                Assert-True ($source -cmatch "Status\s*=\s*'Passed'") 'the scenario must report a pass result'
                # It must be an owned-run entry point, not a standalone script.
                $parameters = Get-ScenarioParameterNames -Source $source
                foreach ($required in @('ScenarioId','ExecutablePath','OwnedRoot','RunToken','OutputDirectory','SupportPath','WinAppPath')) {
                    Assert-True ($parameters -ccontains $required) ('the scenario must accept ' + $required)
                }
                Assert-True ($parameters -ccontains 'LaunchProcess') 'the scenario must accept the launch seam'
            }
        },
        @{
            Name = 'the runner registers the diagnostics scenario under its Task 12 file name'
            Body = {
                Assert-Equal '60-diagnostics-read-only.ps1' (Get-UiAcceptanceScenarioFile -Name 'Diagnostics')
                $names = @(Get-UiAcceptanceScenarioName)
                Assert-True ($names -ccontains 'Diagnostics')
                Assert-Equal '10-main-chain.ps1' (Get-UiAcceptanceScenarioFile -Name 'MainChain')
                Assert-Equal '00-startup-layout.ps1' (Get-UiAcceptanceScenarioFile -Name 'StartupLayout')
                foreach ($name in $names) {
                    Assert-True ((Get-UiAcceptanceScenarioFile -Name $name) -cmatch '^[0-9]{2}-[a-z0-9-]+\.ps1$')
                }
            }
        },
        @{
            Name = 'the diagnostics scenario reaches the tab, the five groups and one refresh'
            Body = {
                $source = Read-DiagnosticsScenarioSource
                # Navigation ids come from the contract, not from a literal copied into the script.
                Assert-Contains $source 'settingsNavigationAutomationId'
                Assert-Contains $source 'inboxNavigationAutomationId'
                Assert-Contains $source 'SystemDiagnosticsTab'
                foreach ($group in @(
                    'DiagnosticsFoldersGroup','DiagnosticsDatabaseGroup','DiagnosticsToolsGroup',
                    'DiagnosticsBackgroundGroup','DiagnosticsRuntimeGroup')) {
                    Assert-Contains $source $group
                }
                Assert-Contains $source 'DiagnosticsRefreshButton'
                Assert-Contains $source 'DiagnosticsOverallStatus'
                Assert-Contains $source 'DiagnosticsLastChecked'
                # Exactly one refresh dispatch.
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'refresh'").Count)
            }
        },
        @{
            Name = 'the diagnostics scenario is read-only and free of unsafe shortcuts'
            Body = {
                $source = Read-DiagnosticsScenarioSource
                # No coordinate input, no fixed delay, no termination by process name.
                Assert-NotContains $source 'Stop-Process -Name'
                Assert-NotContains $source 'taskkill'
                Assert-NotContains $source '--capture-screen'
                Assert-NotContains $source 'ui click'
                Assert-NotContains $source 'Start-Sleep'
                # No shell folder picker and no direct database or service access.
                Assert-NotContains $source 'PickInboxFolder'
                Assert-NotContains $source 'FolderPicker'
                Assert-NotContains $source 'Microsoft.Data.Sqlite'
                Assert-NotContains $source 'Invoke-Sqlite'
                Assert-NotContains $source 'DirectorySettingsService'
                # The diagnostics phase itself only reads: every mutation in the file belongs to
                # the owned preparation and ingest that precede it.
                $marker = '# --- diagnostics phase ---'
                Assert-Contains $source $marker
                $diagnosticsPhase = $source.Substring($source.IndexOf($marker))
                Assert-NotContains $diagnosticsPhase "'set-value'"
                Assert-NotContains $diagnosticsPhase "'send-keys'"
            }
        },
        @{
            Name = 'the diagnostics scenario pairs its UI conclusions with read-only facts'
            Body = {
                $source = Read-DiagnosticsScenarioSource
                Assert-Contains $source 'snapshot --data-root'
                # A before/after pair, so the run can be shown not to have changed business facts.
                Assert-True ([regex]::Matches($source, 'Read-DiagSnapshot -EvidenceId').Count -ge 2) 'a before/after snapshot pair is required'
                Assert-Contains $source 'Get-FileHash'
                Assert-Contains $source 'Assert-UiPngBytes'
                Assert-Contains $source 'Write-UiRedactedTreeEvidence'
            }
        },
        @{
            Name = 'the diagnostics scenario separates live refresh from controlled cancellation'
            Body = {
                $source = Read-DiagnosticsScenarioSource
                Assert-Contains $source "liveUiStatus = 'NotRun'"
                Assert-Contains $source 'Cancellation_retains_the_previous_snapshot'
                Assert-Contains $source 'diagnostics.live_cancellation_not_run'
                Assert-True ($source.IndexOf('$eventsAfter.Completed -cnotcontains $newRuns[0]',
                    [StringComparison]::Ordinal) -ge 0)
                Assert-NotContains $source 'CancellationObserved = $true'
                Assert-NotContains $source 'diagnostics.cancel_unobserved'
                $refresh = $source.IndexOf("-Step 'refresh'", [StringComparison]::Ordinal)
                $leave = $source.IndexOf("-Step 'refresh-leave-settings'", [StringComparison]::Ordinal)
                Assert-True ($refresh -ge 0 -and $leave -gt $refresh)
                $between = $source.Substring($refresh, $leave - $refresh)
                Assert-Contains $between "'--property','IsEnabled','--value','True'"
                Assert-Contains $between '$eventsAfter = Read-DiagRunEvents'
            }
        },
        @{
            Name = 'the diagnostics scenario uses a fresh owned root and verified fixtures only'
            Body = {
                $source = Read-DiagnosticsScenarioSource
                Assert-Contains $source 'Test-UiAcceptanceFixtureAsset'
                Assert-Contains $source 'photo.jpg'
                Assert-Contains $source '$OwnedRoot'
                # It must never reach into a user data directory or another scenario's root.
                Assert-NotContains $source 'TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT ='
                Assert-NotContains $source 'AppData'
                Assert-NotContains $source 'LocalApplicationData'
                Assert-NotContains $source 'MyDocuments'
            }
        },
        @{
            Name = 'the diagnostics scenario is readable by Windows PowerShell 5.1'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/60-diagnostics-read-only.ps1'
                if (-not [IO.File]::Exists($path)) { throw 'Diagnostics scenario is missing.' }
                $bytes = [IO.File]::ReadAllBytes($path)
                $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
                $isAscii = $true
                foreach ($byte in $bytes) {
                    if ($byte -gt 0x7F) { $isAscii = $false; break }
                }
                # Non-ASCII text must carry a UTF-8 BOM, otherwise 5.1 reads the file as ANSI.
                Assert-True ($isAscii -or $hasBom) 'a scenario containing non-ASCII text needs a UTF-8 BOM'
            }
        },
        @{
            Name = 'the conflict keep-both scenario exists, parses and reports Passed'
            Body = {
                $source = Read-ConflictScenarioSource
                $tokens = $null
                $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 @($errors).Count
                Assert-True ($source -cmatch "Status\s*=\s*'Passed'") 'the scenario must report a pass result'
                $parameters = Get-ScenarioParameterNames -Source $source
                foreach ($required in @('ScenarioId','ExecutablePath','OwnedRoot','RunToken','OutputDirectory','SupportPath','WinAppPath')) {
                    Assert-True ($parameters -ccontains $required) ('the scenario must accept ' + $required)
                }
                Assert-True ($parameters -ccontains 'LaunchProcess') 'the scenario must accept the launch seam'
            }
        },
        @{
            Name = 'the runner registers the conflict keep-both scenario without claiming the others'
            Body = {
                Assert-Equal '20-conflict-keep-both.ps1' (Get-UiAcceptanceScenarioFile -Name 'ConflictKeepBoth')
                $names = @(Get-UiAcceptanceScenarioName)
                Assert-True ($names -ccontains 'ConflictKeepBoth')
                # The three decisions are separate scenarios; the aggregate entry must not be read
                # as standing in for any of them.
                Assert-Equal '02-conflict.ps1' (Get-UiAcceptanceScenarioFile -Name 'Conflict')
                Assert-True ($names -ccontains 'ConflictKeepBoth')
                foreach ($name in $names) {
                    Assert-True ((Get-UiAcceptanceScenarioFile -Name $name) -cmatch '^[0-9]{2}-[a-z][a-z0-9-]*\.ps1$')
                }
            }
        },
        @{
            Name = 'the conflict scenario opens the dialog, captures the preview and decides exactly once'
            Body = {
                $source = Read-ConflictScenarioSource
                Assert-Contains $source 'ResolveInboxConflictButton'
                Assert-Contains $source 'ConflictComparisonDialog'
                Assert-Contains $source 'ConflictIncomingPreview'
                Assert-Contains $source 'ConflictKeepBothButton'
                Assert-Contains $source 'IngestButton'
                # The preview is captured before the decision, and the decision is dispatched once.
                $previewAt = $source.IndexOf('ConflictIncomingPreview')
                $decideAt = $source.IndexOf("-Step 'keep-both'")
                Assert-True ($previewAt -gt 0 -and $decideAt -gt $previewAt) 'the preview must be captured before the decision'
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'keep-both'").Count)
                # A conflict observed to change between observation and decision must fail, not re-decide.
                Assert-Contains $source 'conflict.decision_state_changed'
            }
        },
        @{
            Name = 'the conflict scenario proves the outcome with read-only facts'
            Body = {
                $source = Read-ConflictScenarioSource
                Assert-Contains $source 'snapshot --data-root'
                Assert-True ([regex]::Matches($source, 'Read-ConflictSnapshot -EvidenceId').Count -ge 2) 'a before/after snapshot pair is required'
                Assert-Contains $source 'Get-FileHash'
                Assert-Contains $source 'Assert-UiPngBytes'
                Assert-Contains $source 'Write-UiRedactedTreeEvidence'
                # Both copies must be accounted for by hash and no stray artefact may remain.
                Assert-Contains $source 'conflict.original_missing'
                Assert-Contains $source 'conflict.copy_missing'
                Assert-Contains $source 'conflict.extra_files'
                Assert-Contains $source 'conflict.hash_mismatch'
                Assert-Contains $source 'Test-UiAcceptanceFixtureAsset'
            }
        },
        @{
            Name = 'the conflict scenario waits for the resolved row before reading final facts'
            Body = {
                $source = Read-ConflictScenarioSource
                $decision = $source.IndexOf("-Step 'keep-both'", [StringComparison]::Ordinal)
                $rowGone = $source.IndexOf("'ui','wait-for',`$photoRowPrefix,'--gone'", $decision, [StringComparison]::Ordinal)
                $snapshot = $source.IndexOf("Read-ConflictSnapshot -EvidenceId 'conflict-after'", [StringComparison]::Ordinal)
                Assert-True ($decision -ge 0 -and $rowGone -gt $decision -and $snapshot -gt $rowGone)
                Assert-Contains $source 'conflict.resolution_incomplete'
            }
        },
        @{
            Name = 'the conflict scenario is read-only after the decision and free of unsafe shortcuts'
            Body = {
                $source = Read-ConflictScenarioSource
                Assert-NotContains $source 'Stop-Process -Name'
                Assert-NotContains $source 'taskkill'
                Assert-NotContains $source 'ui click'
                Assert-NotContains $source 'Start-Sleep'
                Assert-NotContains $source 'PickInboxFolder'
                Assert-NotContains $source 'FolderPicker'
                Assert-NotContains $source 'Microsoft.Data.Sqlite'
                Assert-NotContains $source 'DirectorySettingsService'
                Assert-NotContains $source 'TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT ='
                # From the conflict phase on, nothing is written: the decision is the last mutation.
                $marker = '# --- conflict phase ---'
                Assert-Contains $source $marker
                $conflictPhase = $source.Substring($source.IndexOf($marker))
                Assert-NotContains $conflictPhase "'set-value'"
            }
        },
        @{
            Name = 'the conflict scenario addresses the dynamic row through its own inspection'
            Body = {
                $source = Read-ConflictScenarioSource
                Assert-Contains $source 'Find-UiAcceptanceUniqueNode'
                Assert-Contains $source 'Get-UiAcceptanceNodeSelector'
                # The row is never invoked by a pre-built prefix nor by a generated identifier.
                Assert-NotContains $source 'InboxCard_'
                Assert-NotContains $source "invoke',`$photoRowPrefix"
            }
        },
        @{
            Name = 'the conflict scenario is readable by Windows PowerShell 5.1'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/20-conflict-keep-both.ps1'
                if (-not [IO.File]::Exists($path)) { throw 'Conflict keep-both scenario is missing.' }
                $bytes = [IO.File]::ReadAllBytes($path)
                $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
                $isAscii = $true
                foreach ($byte in $bytes) { if ($byte -gt 0x7F) { $isAscii = $false; break } }
                Assert-True ($isAscii -or $hasBom) 'a scenario containing non-ASCII text needs a UTF-8 BOM'
            }
        },
        @{
            Name = 'the conflict skip scenario exists, parses and reports Passed'
            Body = {
                $source = Read-SkipScenarioSource
                $tokens = $null
                $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 @($errors).Count
                Assert-True ($source -cmatch "Status\s*=\s*'Passed'") 'the scenario must report a pass result'
                $parameters = Get-ScenarioParameterNames -Source $source
                foreach ($required in @('ScenarioId','ExecutablePath','OwnedRoot','RunToken','OutputDirectory','SupportPath','WinAppPath')) {
                    Assert-True ($parameters -ccontains $required) ('the scenario must accept ' + $required)
                }
                Assert-True ($parameters -ccontains 'LaunchProcess') 'the scenario must accept the launch seam'
            }
        },
        @{
            Name = 'the runner registers the conflict skip scenario without claiming the others'
            Body = {
                Assert-Equal '22-conflict-skip.ps1' (Get-UiAcceptanceScenarioFile -Name 'ConflictSkip')
                $names = @(Get-UiAcceptanceScenarioName)
                Assert-True ($names -ccontains 'ConflictSkip')
                Assert-Equal '20-conflict-keep-both.ps1' (Get-UiAcceptanceScenarioFile -Name 'ConflictKeepBoth')
                # Each decision is its own scenario; the aggregate entry must not stand in for any.
                Assert-Equal '02-conflict.ps1' (Get-UiAcceptanceScenarioFile -Name 'Conflict')
                Assert-Equal '21-conflict-replace.ps1' (Get-UiAcceptanceScenarioFile -Name 'ConflictReplace')
                foreach ($name in $names) {
                    Assert-True ((Get-UiAcceptanceScenarioFile -Name $name) -cmatch '^[0-9]{2}-[a-z][a-z0-9-]*\.ps1$')
                }
            }
        },
        @{
            Name = 'the skip scenario opens the dialog, captures the preview and skips exactly once'
            Body = {
                $source = Read-SkipScenarioSource
                Assert-Contains $source 'ConflictComparisonDialog'
                Assert-Contains $source 'ConflictIncomingPreview'
                Assert-Contains $source 'ConflictSkipButton'
                Assert-Contains $source 'IngestButton'
                $previewAt = $source.IndexOf('ConflictIncomingPreview')
                $decideAt = $source.IndexOf("-Step 'skip'")
                Assert-True ($previewAt -gt 0 -and $decideAt -gt $previewAt) 'the preview must be captured before the decision'
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'skip'").Count)
                Assert-Contains $source 'conflict.decision_state_changed'
                # A skip never decides the other way, and a failure never re-issues the decision.
                Assert-NotContains $source "'ConflictKeepBothButton'"
                Assert-NotContains $source "'ConflictReplaceButton'"
            }
        },
        @{
            Name = 'the skip scenario waits for the product completion message, not just the closed popup'
            Body = {
                $source = Read-SkipScenarioSource
                # A closed dialog only proves the popup vanished: the asynchronous resolution may
                # still be committing. Completion is the product withdrawing its own conflict
                # state, observed in the live UI on lines that carry no identifier.
                Assert-Contains $source 'conflictStateToken'
                Assert-Contains $source 'conflictMessageToken'
                Assert-Contains $source 'New-ConflictText -CodePoints'
                Assert-Contains $source 'conflict.resolution_incomplete'
                Assert-Contains $source "'ResolveInboxConflictButton','--gone'"
                Assert-Contains $source 'conflict.precondition_state'
                Assert-NotContains $source 'InboxOperationStatus'
                $dialogAt = $source.IndexOf("-Step 'skip'")
                $goneAt = $source.IndexOf("'ConflictComparisonDialog','--gone'")
                $completeAt = $source.IndexOf('conflict.resolution_incomplete')
                Assert-True ($dialogAt -gt 0 -and $goneAt -gt $dialogAt) 'the dialog close must be awaited after the decision'
                Assert-True ($completeAt -gt $goneAt) 'the completion message must be awaited after the dialog closes'
                # Skip keeps the item pending, so waiting for the row to disappear would be wrong.
                Assert-NotContains $source "wait-for',`$photoRowPrefix,'--gone'"
            }
        },
        @{
            Name = 'the skip scenario proves the outcome with read-only facts'
            Body = {
                $source = Read-SkipScenarioSource
                Assert-Contains $source 'snapshot --data-root'
                Assert-True ([regex]::Matches($source, 'Read-ConflictSnapshot -EvidenceId').Count -ge 2) 'a before/after snapshot pair is required'
                Assert-Contains $source 'Get-FileHash'
                Assert-Contains $source 'Assert-UiPngBytes'
                Assert-Contains $source 'Write-UiRedactedTreeEvidence'
                Assert-Contains $source 'conflict.original_changed'
                Assert-Contains $source 'conflict.copy_created'
                Assert-Contains $source 'conflict.source_missing'
                Assert-Contains $source 'conflict.source_changed'
                Assert-Contains $source 'Test-UiAcceptanceFixtureAsset'
            }
        },
        @{
            Name = 'the skip scenario is read-only after the decision and free of unsafe shortcuts'
            Body = {
                $source = Read-SkipScenarioSource
                Assert-NotContains $source 'Stop-Process -Name'
                Assert-NotContains $source 'taskkill'
                Assert-NotContains $source 'ui click'
                Assert-NotContains $source 'Start-Sleep'
                Assert-NotContains $source 'PickInboxFolder'
                Assert-NotContains $source 'FolderPicker'
                Assert-NotContains $source 'Microsoft.Data.Sqlite'
                Assert-NotContains $source 'DirectorySettingsService'
                Assert-NotContains $source 'TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT ='
                $marker = '# --- conflict phase ---'
                Assert-Contains $source $marker
                $conflictPhase = $source.Substring($source.IndexOf($marker))
                Assert-NotContains $conflictPhase "'set-value'"
            }
        },
        @{
            Name = 'the skip scenario addresses the dynamic row through its own inspection'
            Body = {
                $source = Read-SkipScenarioSource
                Assert-Contains $source 'Find-UiAcceptanceUniqueNode'
                Assert-Contains $source 'Get-UiAcceptanceNodeSelector'
                Assert-NotContains $source 'InboxCard_'
                Assert-NotContains $source "invoke',`$photoRowPrefix"
            }
        },
        @{
            Name = 'the skip scenario is readable by Windows PowerShell 5.1'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/22-conflict-skip.ps1'
                if (-not [IO.File]::Exists($path)) { throw 'Conflict skip scenario is missing.' }
                $bytes = [IO.File]::ReadAllBytes($path)
                $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
                $isAscii = $true
                foreach ($byte in $bytes) { if ($byte -gt 0x7F) { $isAscii = $false; break } }
                Assert-True ($isAscii -or $hasBom) 'a scenario containing non-ASCII text needs a UTF-8 BOM'
            }
        },
        @{
            Name = 'the conflict replace scenario exists, parses and reports Passed'
            Body = {
                $source = Read-ReplaceScenarioSource
                $tokens = $null
                $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 @($errors).Count
                Assert-True ($source -cmatch "Status\s*=\s*'Passed'") 'the scenario must report a pass result'
                $parameters = Get-ScenarioParameterNames -Source $source
                foreach ($required in @('ScenarioId','ExecutablePath','OwnedRoot','RunToken','OutputDirectory','SupportPath','WinAppPath')) {
                    Assert-True ($parameters -ccontains $required) ('the scenario must accept ' + $required)
                }
                Assert-True ($parameters -ccontains 'LaunchProcess') 'the scenario must accept the launch seam'
            }
        },
        @{
            Name = 'the runner registers all three conflict decisions separately'
            Body = {
                Assert-Equal '21-conflict-replace.ps1' (Get-UiAcceptanceScenarioFile -Name 'ConflictReplace')
                $names = @(Get-UiAcceptanceScenarioName)
                foreach ($name in @('ConflictKeepBoth','ConflictReplace','ConflictSkip')) {
                    Assert-True ($names -ccontains $name) ('the registry must offer ' + $name)
                }
                # Replace, skip and keep-both are three scenarios: the aggregate entry must not be
                # presented as covering them.
                Assert-Equal '02-conflict.ps1' (Get-UiAcceptanceScenarioFile -Name 'Conflict')
                foreach ($name in $names) {
                    Assert-True ((Get-UiAcceptanceScenarioFile -Name $name) -cmatch '^[0-9]{2}-[a-z][a-z0-9-]*\.ps1$')
                }
            }
        },
        @{
            Name = 'the replace scenario captures both previews and decides exactly once'
            Body = {
                $source = Read-ReplaceScenarioSource
                Assert-Contains $source 'ConflictComparisonDialog'
                Assert-Contains $source 'ConflictIncomingPreview'
                Assert-Contains $source 'ConflictReplaceButton'
                Assert-Contains $source 'IngestButton'
                $previewAt = $source.IndexOf('ConflictIncomingPreview')
                $decideAt = $source.IndexOf("-Step 'replace'")
                Assert-True ($previewAt -gt 0 -and $decideAt -gt $previewAt) 'the previews must be captured before the decision'
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'replace'").Count)
                Assert-Contains $source 'conflict.decision_state_changed'
                # A replace never decides the other way, and a failure never re-issues the decision.
                Assert-NotContains $source "'ConflictSkipButton'"
                Assert-NotContains $source "'ConflictKeepBothButton'"
            }
        },
        @{
            Name = 'the replace scenario waits for the pending row to leave the live UI'
            Body = {
                $source = Read-ReplaceScenarioSource
                Assert-Contains $source "'ConflictComparisonDialog','--gone'"
                # A replace consumes the incoming item, so the row leaving the live UI is the
                # completion signal; the dialog closing alone only proves the overlay vanished.
                Assert-Contains $source "wait-for',`$photoRowPrefix,'--gone'"
                Assert-Contains $source 'conflict.resolution_incomplete'
                $decideAt = $source.IndexOf("-Step 'replace'")
                $rowGoneAt = $source.IndexOf("wait-for',`$photoRowPrefix,'--gone'")
                Assert-True ($rowGoneAt -gt $decideAt) 'the row must be awaited after the decision'
            }
        },
        @{
            Name = 'the replace scenario proves replacement with read-only facts and a stable record key'
            Body = {
                $source = Read-ReplaceScenarioSource
                Assert-Contains $source 'snapshot --data-root'
                Assert-True ([regex]::Matches($source, 'Read-ConflictSnapshot -EvidenceId').Count -ge 2) 'a before/after snapshot pair is required'
                # The record identity must come from the read-only projection, never from the database.
                Assert-Contains $source 'assetKey'
                Assert-Contains $source 'conflict.record_identity'
                # Both keys must be present: comparing two absent values would pass vacuously.
                Assert-Contains $source 'IsNullOrWhiteSpace($beforeKey)'
                Assert-Contains $source 'conflict.hash_unchanged'
                Assert-Contains $source 'conflict.extra_records'
                Assert-Contains $source 'conflict.inbox_not_cleared'
                Assert-Contains $source 'conflict.extra_files'
                Assert-Contains $source 'Get-FileHash'
                Assert-Contains $source 'Assert-UiPngBytes'
                Assert-Contains $source 'Write-UiRedactedTreeEvidence'
            }
        },
        @{
            Name = 'the replace scenario uses a distinct verified fixture, never a byte copy'
            Body = {
                $source = Read-ReplaceScenarioSource
                Assert-Contains $source 'photo-replacement.jpg'
                Assert-Contains $source 'Test-UiAcceptanceFixtureAsset'
                Assert-Contains $source 'conflict.fixture_identical'
                # Both the original and the replacement must be verified before either is used.
                Assert-True ([regex]::Matches($source, 'Test-UiAcceptanceFixtureAsset').Count -ge 2) 'both fixtures must be verified'
            }
        },
        @{
            Name = 'the replace scenario is read-only after the decision and free of unsafe shortcuts'
            Body = {
                $source = Read-ReplaceScenarioSource
                Assert-NotContains $source 'Stop-Process -Name'
                Assert-NotContains $source 'taskkill'
                Assert-NotContains $source 'ui click'
                Assert-NotContains $source 'Start-Sleep'
                Assert-NotContains $source 'PickInboxFolder'
                Assert-NotContains $source 'FolderPicker'
                Assert-NotContains $source 'Microsoft.Data.Sqlite'
                Assert-NotContains $source 'DirectorySettingsService'
                Assert-NotContains $source 'TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT ='
                $marker = '# --- conflict phase ---'
                Assert-Contains $source $marker
                $conflictPhase = $source.Substring($source.IndexOf($marker))
                Assert-NotContains $conflictPhase "'set-value'"
            }
        },
        @{
            Name = 'the replace scenario addresses the dynamic row through its own inspection'
            Body = {
                $source = Read-ReplaceScenarioSource
                Assert-Contains $source 'Find-UiAcceptanceUniqueNode'
                Assert-Contains $source 'Get-UiAcceptanceNodeSelector'
                Assert-NotContains $source 'InboxCard_'
                Assert-NotContains $source "invoke',`$photoRowPrefix"
            }
        },
        @{
            Name = 'the replace scenario is readable by Windows PowerShell 5.1'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/21-conflict-replace.ps1'
                if (-not [IO.File]::Exists($path)) { throw 'Conflict replace scenario is missing.' }
                $bytes = [IO.File]::ReadAllBytes($path)
                $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
                $isAscii = $true
                foreach ($byte in $bytes) { if ($byte -gt 0x7F) { $isAscii = $false; break } }
                Assert-True ($isAscii -or $hasBom) 'a scenario containing non-ASCII text needs a UTF-8 BOM'
            }
        },
        @{
            Name = 'the processing failure retry scenario exists, parses and reports Passed'
            Body = {
                $source = Read-RetryScenarioSource
                $tokens = $null
                $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 @($errors).Count
                Assert-True ($source -cmatch "Status\s*=\s*'Passed'") 'the scenario must report a pass result'
                $parameters = Get-ScenarioParameterNames -Source $source
                foreach ($required in @('ScenarioId','ExecutablePath','OwnedRoot','RunToken','OutputDirectory','SupportPath','WinAppPath')) {
                    Assert-True ($parameters -ccontains $required) ('the scenario must accept ' + $required)
                }
                Assert-True ($parameters -ccontains 'LaunchProcess') 'the scenario must accept the launch seam'
            }
        },
        @{
            Name = 'the runner registers the retry scenario alongside the three conflict decisions'
            Body = {
                Assert-Equal '23-processing-failure-retry.ps1' (Get-UiAcceptanceScenarioFile -Name 'ProcessingFailureRetry')
                $names = @(Get-UiAcceptanceScenarioName)
                Assert-True ($names -ccontains 'ProcessingFailureRetry')
                foreach ($name in @('ConflictKeepBoth','ConflictReplace','ConflictSkip')) {
                    Assert-True ($names -ccontains $name) ('the registry must still offer ' + $name)
                }
                # The aggregate conflict entry still points at a script no task creates, so it must
                # never be read as covering the conflict scenarios.
                Assert-Equal '02-conflict.ps1' (Get-UiAcceptanceScenarioFile -Name 'Conflict')
                foreach ($name in $names) {
                    Assert-True ((Get-UiAcceptanceScenarioFile -Name $name) -cmatch '^[0-9]{2}-[a-z][a-z0-9-]*\.ps1$')
                }
            }
        },
        @{
            Name = 'the retry scenario starts processing and retries exactly once'
            Body = {
                $source = Read-RetryScenarioSource
                Assert-Contains $source 'InboxStartProcessingButton'
                Assert-Contains $source 'InboxRetryProcessingButton'
                Assert-Contains $source 'InboxProcessingStep'
                Assert-Contains $source 'InboxProcessingProgress'
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'retry'").Count)
                # A dispatched action is never resent.
                Assert-NotContains $source 'Start-Sleep'
                Assert-NotContains $source "ConflictSkipButton"
            }
        },
        @{
            Name = 'changed sources reject retry and recover only after rescan and start'
            Body = {
                $source = Read-RetryScenarioSource
                $retry = $source.IndexOf("-Step 'retry'", [StringComparison]::Ordinal)
                $rescan = $source.IndexOf("-Step 'rescan'", [StringComparison]::Ordinal)
                $restart = $source.IndexOf("-Step 'start-recovered'", [StringComparison]::Ordinal)
                Assert-True ($retry -ge 0 -and $rescan -gt $retry -and $restart -gt $rescan)
                Assert-Contains $source 'processing.changed_source_requires_rescan'
                Assert-True ($source -cmatch "retryOutcome\s*=\s*'SourceChanged'")
                Assert-True ($source -cmatch "recoveryAction\s*=\s*'RescanThenStart'")
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'retry'").Count)
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'start-recovered'").Count)
            }
        },
        @{
            Name = 'the retry scenario proves the failure with retained sources and unchanged hashes'
            Body = {
                $source = Read-RetryScenarioSource
                Assert-Contains $source 'broken.mp4'
                Assert-Contains $source 'broken.m4a'
                Assert-Contains $source 'Test-UiAcceptanceFixtureAsset'
                Assert-Contains $source 'processing.sources_missing'
                Assert-Contains $source 'processing.sources_changed'
                Assert-Contains $source 'sourceHashes'
                Assert-Contains $source 'processing.failure_not_observed'
                # The invalid pair is declared and verified like every other fixture, and the valid
                # recovery pair comes from the manifest rather than from generated bytes.
                Assert-Contains $source 'paired.mp4'
                Assert-Contains $source 'paired.m4a'
            }
        },
        @{
            Name = 'the retry scenario records the observed states and reports what it could not see'
            Body = {
                $source = Read-RetryScenarioSource
                # Brief states must be reported honestly rather than asserted from a fixed delay.
                Assert-Contains $source 'processing-states.json'
                Assert-Contains $source 'observed'
                Assert-Contains $source 'unobserved'
                Assert-Contains $source 'processing.recovered_not_observed'
                # Every conclusion is paired with a read-only snapshot or a file hash.
                Assert-Contains $source 'snapshot --data-root'
                Assert-Contains $source 'Get-FileHash'
            }
        },
        @{
            Name = 'the retry scenario injects no fault and touches no product tool'
            Body = {
                $source = Read-RetryScenarioSource
                Assert-NotContains $source 'ffmpeg.exe'
                Assert-NotContains $source 'ffprobe.exe'
                Assert-NotContains $source 'Microsoft.Data.Sqlite'
                Assert-NotContains $source 'Stop-Process -Name'
                Assert-NotContains $source 'taskkill'
                Assert-NotContains $source 'ui click'
                Assert-NotContains $source 'PickInboxFolder'
                Assert-NotContains $source 'TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT ='
                Assert-NotContains $source 'InboxCard_'
            }
        },
        @{
            Name = 'the retry scenario is readable by Windows PowerShell 5.1'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/23-processing-failure-retry.ps1'
                if (-not [IO.File]::Exists($path)) { throw 'Processing failure retry scenario is missing.' }
                $bytes = [IO.File]::ReadAllBytes($path)
                $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
                $isAscii = $true
                foreach ($byte in $bytes) { if ($byte -gt 0x7F) { $isAscii = $false; break } }
                Assert-True ($isAscii -or $hasBom) 'a scenario containing non-ASCII text needs a UTF-8 BOM'
            }
        },
        @{
            Name = 'the batch metadata scenario exists, parses and reports Passed, per ruling B'
            Body = {
                $source = Read-BatchScenarioSource
                $tokens = $null
                $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 @($errors).Count
                Assert-True ($source -cmatch "Status\s*=\s*'Passed'") 'the scenario must report a pass result'
                $parameters = Get-ScenarioParameterNames -Source $source
                foreach ($required in @('ScenarioId','ExecutablePath','OwnedRoot','RunToken','OutputDirectory','SupportPath','WinAppPath')) {
                    Assert-True ($parameters -ccontains $required) ('the scenario must accept ' + $required)
                }
                Assert-True ($parameters -ccontains 'LaunchProcess') 'the scenario must accept the launch seam'
            }
        },
        @{
            Name = 'the batch metadata scenario runs on the library editor because inbox metadata has no undo'
            Body = {
                $source = Read-BatchScenarioSource
                # Ruling B must be recorded where the next reader will see it.
                Assert-True ($source -cmatch 'Ruling B') 'the header must record the ruling'
                Assert-Contains $source 'LibraryBatchEditDialog'
                Assert-Contains $source 'LibraryBatchMetadataTab'
                Assert-Contains $source 'LibraryBatchUndoButton'
                # The inbox batch metadata operation exposes no undo, so this scenario must not
                # pretend otherwise: no inbox batch control and no invented undo may appear.
                Assert-NotContains $source 'BatchMetadataSubmitButton'
                Assert-NotContains $source 'InboxBatchMetadataButton'
                Assert-NotContains $source 'InboxBatchUndoButton'
            }
        },
        @{
            Name = 'the runner registers the batch metadata scenario alongside the other workflows'
            Body = {
                Assert-Equal '30-batch-metadata-undo.ps1' (Get-UiAcceptanceScenarioFile -Name 'BatchMetadataUndo')
                $names = @(Get-UiAcceptanceScenarioName)
                Assert-True ($names -ccontains 'BatchMetadataUndo')
                # The aggregate conflict entry must still not stand in for the conflict scenarios.
                Assert-Equal '02-conflict.ps1' (Get-UiAcceptanceScenarioFile -Name 'Conflict')
                foreach ($name in $names) {
                    Assert-True ((Get-UiAcceptanceScenarioFile -Name $name) -cmatch '^[0-9]{2}-[a-z][a-z0-9-]*\.ps1$')
                }
            }
        },
        @{
            Name = 'the batch metadata scenario appends and replaces authors tags and one series'
            Body = {
                $source = Read-BatchScenarioSource
                Assert-Contains $source 'LibraryBatchAddAuthorInput'
                Assert-Contains $source 'LibraryBatchAddTagInput'
                Assert-Contains $source 'LibraryBatchMetadataModeList'
                Assert-Contains $source 'LibraryBatchReplaceAuthorInput'
                Assert-Contains $source 'LibraryBatchReplaceSeriesToggle'
                Assert-Contains $source 'LibraryBatchReplaceTagInput'
                Assert-Contains $source 'LibraryBatchDialogStatus'
                Assert-Contains $source 'LibraryBatchApplyButton'
                Assert-Contains $source 'Assert-LibraryUndoCount'
                Assert-Contains $source 'LibraryBatchUndoBanner'
                # Append supports authors and tags only; series is replace-or-clear, so the scenario
                # must not claim an append of series.
                Assert-NotContains $source 'LibraryBatchAddSeriesInput'
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'apply-append'").Count)
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'apply-replace'").Count)
            }
        },
        @{
            Name = 'the batch metadata scenario proves an invalid draft is refused and nothing changed'
            Body = {
                $source = Read-BatchScenarioSource
                Assert-Contains $source 'library.invalid_draft'
                Assert-Contains $source 'library.draft_changed'
                # The invalid draft is the unchanged draft, which the product refuses by disabling
                # apply; the scenario must observe that rather than claim a partial write, because
                # the product's write is transactional.
                Assert-Contains $source 'batch.invalid-draft'
                Assert-Contains $source 'transactional'
            }
        },
        @{
            Name = 'the batch metadata scenario undoes exactly once and checks prior values and hashes'
            Body = {
                $source = Read-BatchScenarioSource
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'undo'").Count)
                Assert-Contains $source 'library.undo'
                Assert-Contains $source 'library.prior_values'
                Assert-Contains $source 'library.file_changed'
                Assert-Contains $source 'Get-FileHash'
                Assert-Contains $source 'snapshot --data-root'
                Assert-True ([regex]::Matches($source, 'Read-LibrarySnapshot -EvidenceId').Count -ge 3) 'before, applied and undone snapshots are required'
            }
        },
        @{
            Name = 'the batch metadata scenario is safe and independent'
            Body = {
                $source = Read-BatchScenarioSource
                Assert-NotContains $source 'Stop-Process -Name'
                Assert-NotContains $source 'taskkill'
                Assert-NotContains $source 'ui click'
                Assert-NotContains $source 'Start-Sleep'
                Assert-NotContains $source 'PickInboxFolder'
                Assert-NotContains $source 'Microsoft.Data.Sqlite'
                Assert-NotContains $source 'DirectorySettingsService'
                Assert-NotContains $source 'TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT ='
                Assert-NotContains $source 'InboxCard_'
                Assert-Contains $source 'Test-UiAcceptanceFixtureAsset'
            }
        },
        @{
            Name = 'the batch metadata scenario is readable by Windows PowerShell 5.1'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/30-batch-metadata-undo.ps1'
                if (-not [IO.File]::Exists($path)) { throw 'Batch metadata undo scenario is missing.' }
                $bytes = [IO.File]::ReadAllBytes($path)
                $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
                $isAscii = $true
                foreach ($byte in $bytes) { if ($byte -gt 0x7F) { $isAscii = $false; break } }
                Assert-True ($isAscii -or $hasBom) 'a scenario containing non-ASCII text needs a UTF-8 BOM'
            }
        },
        @{
            Name = 'the library filter and naming scenario exists, parses and reports Passed'
            Body = {
                $source = Read-LibraryFilterScenarioSource
                $tokens = $null
                $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 @($errors).Count
                Assert-True ($source -cmatch "Status\s*=\s*'Passed'") 'the scenario must report a pass result'
                $parameters = Get-ScenarioParameterNames -Source $source
                foreach ($required in @('ScenarioId','ExecutablePath','OwnedRoot','RunToken','OutputDirectory','SupportPath','WinAppPath')) {
                    Assert-True ($parameters -ccontains $required) ('the scenario must accept ' + $required)
                }
                Assert-True ($parameters -ccontains 'LaunchProcess') 'the scenario must accept the launch seam'
            }
        },
        @{
            Name = 'the library scenario covers filters sorting and naming without the metadata tab'
            Body = {
                $source = Read-LibraryFilterScenarioSource
                Assert-Contains $source 'LibrarySearchBox'
                Assert-Contains $source 'LibraryMediaFilter'
                Assert-Contains $source 'LibraryAuthorFilter'
                Assert-Contains $source 'LibrarySeriesFilter'
                Assert-Contains $source 'LibraryTagFilter'
                Assert-Contains $source 'LibraryFilterApplyButton'
                Assert-Contains $source 'LibrarySortField'
                Assert-Contains $source 'LibrarySortDirection'
                Assert-Contains $source 'LibraryBatchNamingTab'
                Assert-Contains $source 'LibraryNamingAddRuleButton'
                Assert-Contains $source 'LibraryNamingPreviewList'
                Assert-Contains $source 'LibraryNamingPresetList'
                Assert-Contains $source 'LibraryBatchUndoButton'
                # The batch metadata tab belongs to the other scenario, so this one must not use it.
                Assert-NotContains $source 'LibraryBatchMetadataTab'
                Assert-NotContains $source 'LibraryBatchAddAuthorInput'
                Assert-NotContains $source 'LibraryBatchReplaceAuthorInput'
            }
        },
        @{
            Name = 'the library scenario expands a combo box and matches an option on its own label'
            Body = {
                $source = Read-LibraryFilterScenarioSource
                # Measured product behaviour: a closed combo box exposes no list items at all, and
                # the option labels are substrings of other accessible names in the same window, so
                # every selection goes through the shared helper - expand the box through its own
                # id, resolve the option as the unique list item whose own name is the label, then
                # invoke that option's own selector.
                # Two sort controls, the media-kind condition in its proving and final stages, and
                # the three naming rule kinds.
                Assert-Equal 8 ([regex]::Matches($source, 'Select-LibraryOption -ComboBoxId').Count)
                Assert-True ($source.Contains("ui','invoke',`$ComboBoxId)")) 'the box is expanded before an option is chosen'
                Assert-True ($source.Contains("-Name 'name') -ceq `$OptionName")) 'an option is matched on its own name'
                Assert-True ($source.Contains("-Name 'type') -ceq 'ListItem'")) 'an option is matched on its native type'
                # A rule kind must never be dispatched by its label as free text, because the driver
                # refuses an ambiguous text selector and would report a false product failure.
                Assert-True (-not $source.Contains("ui','invoke',`$prefixRuleOption")) 'a rule kind is never invoked as free text'
                Assert-True (-not $source.Contains("ui','invoke',`$sequenceRuleOption")) 'a rule kind is never invoked as free text'
                Assert-True (-not $source.Contains("ui','invoke',`$replaceRuleOption")) 'a rule kind is never invoked as free text'
            }
        },
        @{
            Name = 'the library scenario commits a filter term by choosing a suggestion and confirms its chip'
            Body = {
                $source = Read-LibraryFilterScenarioSource
                # Measured product behaviour: the filter pickers have no query handler, so a typed
                # term is committed only by choosing a suggestion. Typing therefore goes through the
                # per-character transport, the suggestion is chosen with the arrow key, and the
                # product's own draft chip is what confirms the term landed in the draft.
                Assert-Contains $source 'Set-LibraryFilterTerm'
                Assert-Contains $source 'send-input'
                Assert-Contains $source "'down'"
                Assert-Contains $source 'library.filter_suggestion'
                Assert-Contains $source 'library.filter_chip'
                Assert-Contains $source "'--verbatim'"
                # The inspector helper confirms a term with Enter, which the filter pickers ignore,
                # so it must never be used on a filter picker.
                Assert-NotContains $source "Set-LibraryTerm -OuterId 'LibraryAuthorFilter'"
                Assert-NotContains $source "Set-LibraryTerm -OuterId 'LibraryTagFilter'"
                Assert-NotContains $source "Set-LibraryTerm -OuterId 'LibrarySeriesFilter'"
                # The applied conditions are counted from the page's own chip container, not read
                # back from a static label.
                Assert-Contains $source 'Get-LibraryAppliedFilterCount'
                # All three term pickers are driven, each confirmed by its own draft chip, so no
                # picker is listed as required and then never used.
                Assert-Contains $source "Set-LibraryFilterTerm -OuterId 'LibraryAuthorFilter'"
                Assert-Contains $source "Set-LibraryFilterTerm -OuterId 'LibraryTagFilter'"
                Assert-Contains $source "Set-LibraryFilterTerm -OuterId 'LibrarySeriesFilter'"
                Assert-Contains $source '-DraftChipName $authorDraftChip'
                Assert-Contains $source '-DraftChipName $tagDraftChip'
                Assert-Contains $source '-DraftChipName $seriesDraftChip'
                # The series draft chip is the product's own name for it, given as code points so
                # the scenario file stays pure ASCII.
                Assert-Contains $source '$seriesDraftChip = New-LibraryText'
            }
        },
        @{
            Name = 'the runner registers the library scenario without disturbing the conflict placeholder'
            Body = {
                Assert-Equal '40-library-filter-naming-undo.ps1' (Get-UiAcceptanceScenarioFile -Name 'LibraryFilterNaming')
                $names = @(Get-UiAcceptanceScenarioName)
                Assert-True ($names -ccontains 'LibraryFilterNaming')
                # The aggregate conflict entry stays exactly as it was, so a passing slice can never
                # be read as the whole suite passing.
                Assert-Equal '02-conflict.ps1' (Get-UiAcceptanceScenarioFile -Name 'Conflict')
                foreach ($name in $names) {
                    Assert-True ((Get-UiAcceptanceScenarioFile -Name $name) -cmatch '^[0-9]{2}-[a-z][a-z0-9-]*\.ps1$')
                }
            }
        },
        @{
            Name = 'the library scenario proves a strict filter intersection and sort stability'
            Body = {
                $source = Read-LibraryFilterScenarioSource
                Assert-Contains $source 'library.intersection'
                Assert-Contains $source 'library.sort_stable'
                Assert-Contains $source 'LibraryClearFiltersButton'
                Assert-Contains $source 'LibraryActiveFilters'
                # Three assets with distinguishable metadata are required for a meaningful
                # intersection, and the metadata is set through the single-item inspector, not the
                # batch metadata tab.
                Assert-Contains $source 'LibraryInspectorOpenButton'
                Assert-Contains $source 'LibraryAuthorInput'
                Assert-Contains $source 'LibrarySeriesInput'
                Assert-Contains $source 'LibraryTagInput'
                Assert-True ([regex]::Matches($source, 'photo-third.jpg').Count -ge 1) 'a third fixed asset is required'
                # The intersection is claimed from the visible rows and the record facts, never from
                # a chip count alone: the chip count is asserted as its own separate fact.
                Assert-Contains $source 'library.intersection_rows'
                Assert-Contains $source 'library.intersection_row_name'
                Assert-Contains $source 'library.intersection_filters'
                Assert-Contains $source 'library.intersection_search'
            }
        },
        @{
            Name = 'the library scenario drives all five filter conditions and proves each changes the visible list'
            Body = {
                $source = Read-LibraryFilterScenarioSource
                # The search box is driven for real: its stable outer id is resolved to the one inner
                # native Edit, the query is typed through the product UI, the value is read back, and
                # the visible rows are asserted for that query.
                Assert-Contains $source "Set-LibrarySearchText -OuterId 'LibrarySearchBox'"
                Assert-Contains $source 'library.search_input'
                Assert-Contains $source 'library.search_value'
                Assert-Contains $source 'library.search_rows'
                Assert-Contains $source '$noMatchQuery'
                # A query that matches nothing has to be shown to empty the list, so the search is
                # proven to drive the visible result before the final query is left in place.
                Assert-True ($source.Contains('$noMatchQuery')) 'a non-matching query is required to prove the search takes effect'
                Assert-Contains $source 'Get-LibrarySearchValue'
                Assert-Contains $source 'LibrarySearchBox'
                # The box is a composite, so the value is read and typed through the one native Edit
                # found beneath the stable outer id, never through an id of its own.
                Assert-Contains $source 'Get-LibraryPickerTextBox'
                Assert-Contains $source '-NativeType ''Edit'''
                # The proving query has to be emptied again before the final one is typed. The box is
                # emptied by the select-all-then-delete combination the driver supports, and the empty
                # state is asserted from the placeholder the product shows only while it is empty,
                # because an emptied text box reports its accessible name rather than a value.
                Assert-Contains $source 'library.search_clear'
                Assert-Contains $source 'Clear-LibrarySearchText'
                Assert-Contains $source "'end'"
                Assert-Contains $source "'vk=0x08'"
                Assert-Contains $source 'Test-LibrarySearchEmpty'
                Assert-Contains $source '$searchPlaceholder'
                # Each of the other three driven conditions is proven to change the visible rows in
                # its own stage, so none of the five is merely listed as required.
                Assert-Contains $source 'library.kind_rows'
                Assert-Contains $source 'library.series_rows'
                Assert-Contains $source 'library.filter_chips'
                # Rows are read from the grid's own container only, on screen only, and never folded
                # together by name, so a header item, a navigation item or a duplicate row can never
                # stand in for a library row.
                Assert-Contains $source 'Get-UiAcceptanceListRowNames'
                Assert-Contains $source "-ContainerId 'LibraryGrid'"
                # Every "is this element on screen" decision goes through the shared visible-only
                # match: a collapsed flyout keeps previously realised controls in the tree, so a
                # deselected term would otherwise still read as selected and confirm a condition that
                # is not applied. That helper's own test covers the hidden-element case.
                Assert-Contains $source 'Get-UiAcceptanceNamedNodes'
                Assert-Contains $source '-VisibleOnly'
                # Every stage asserts the exact set of named rows it expects and records that set,
                # which is what stops a stage passing on the right number of the wrong rows.
                Assert-Contains $source 'Wait-LibraryRowSet'
                Assert-Contains $source '$firstAndThirdRows'
                Assert-Contains $source '$thirdRowOnly'
                Assert-Contains $source 'Save-LibraryStageState'
                Assert-Contains $source '-RowSet $'
                # The final state is one observation, asserting both the count and the whole row name.
                Assert-Contains $source 'library.intersection_row_count'
                Assert-Contains $source 'library.intersection_row_name'
                # The five conditions are applied together in one draft and the final state is
                # asserted as one row, that row's identity, four chips, the search value read back,
                # and the media kind read back from its own picker.
                Assert-Contains $source "'kind'"
                Assert-Contains $source "'search'"
                Assert-Contains $source 'library.intersection_rows'
                Assert-Contains $source 'library.intersection_kind'
                Assert-Contains $source '$finalChips -ne 4'
                Assert-Contains $source '$finalSearch'
                # The term families cannot be read back by value: native UIA gives every applied chip
                # the same fixed accessible name, so exactly one chip per family is asserted and the
                # labels are captured in a screenshot instead of being claimed as read values.
                Assert-Contains $source 'Get-LibraryFlyoutChipCount'
                Assert-Contains $source "'library-intersection-filters'"
                # No temporary diagnostic may remain in the delivered scenario.
                Assert-NotContains $source 'DIAGNOSTIC (temporary)'
                Assert-NotContains $source 'library.diag'
            }
        },
        @{
            Name = 'the library scenario exercises a disabled rule and an invalid draft without applying it'
            Body = {
                $source = Read-LibraryFilterScenarioSource
                Assert-Contains $source 'library.rule_disabled'
                Assert-Contains $source 'library.invalid_draft'
                Assert-Contains $source 'LibraryNamingRuleKindSelector'
                Assert-Contains $source 'LibraryNamingSequenceStartParameter'
                # The rows are rendered in the reverse order of the rule set, so a rule is addressed
                # by the label its row renders and never by its position in the tree.
                Assert-Contains $source 'Get-LibraryRuleElementId'
                Assert-Contains $source "-Suffix 'Enabled'"
                Assert-Contains $source "-Suffix 'Delete'"
                Assert-NotContains $source '$ruleRowsAfter'
                Assert-NotContains $source '$enableIds'
                # An invalid draft must be refused by the product, never applied.
                Assert-Contains $source 'batch.invalid-draft'
            }
        },
        @{
            Name = 'the library scenario applies naming once and undoes once with fact checks'
            Body = {
                $source = Read-LibraryFilterScenarioSource
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'naming-apply'").Count)
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'undo'").Count)
                Assert-Contains $source 'library.display_name'
                Assert-Contains $source 'library.file_changed'
                Assert-Contains $source 'library.record_identity'
                Assert-Contains $source 'library.preset_changed'
                Assert-Contains $source 'Get-FileHash'
                Assert-Contains $source 'snapshot --data-root'
                Assert-True ([regex]::Matches($source, 'Read-LibraryState -EvidenceId').Count -ge 3) 'before, named and undone snapshots are required'
            }
        },
        @{
            Name = 'the library scenario is safe and independent'
            Body = {
                $source = Read-LibraryFilterScenarioSource
                Assert-NotContains $source 'Stop-Process -Name'
                Assert-NotContains $source 'taskkill'
                Assert-NotContains $source 'ui click'
                Assert-NotContains $source 'Start-Sleep'
                Assert-NotContains $source 'PickInboxFolder'
                Assert-NotContains $source 'Microsoft.Data.Sqlite'
                Assert-NotContains $source 'DirectorySettingsService'
                Assert-NotContains $source 'TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT ='
                Assert-NotContains $source 'InboxCard_'
                Assert-Contains $source 'Test-UiAcceptanceFixtureAsset'
                # A dispatch the harness refused never reached the product, so the declared blocked
                # environment condition is what it must be reported as.
                Assert-Contains $source 'library.desktop_unavailable'
                Assert-True ($source.Contains("return 'library.desktop_unavailable'")) 'a refused dispatch must report the blocked condition'
            }
        },
        @{
            Name = 'the library scenario is readable by Windows PowerShell 5.1'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/40-library-filter-naming-undo.ps1'
                if (-not [IO.File]::Exists($path)) { throw 'Library filter and naming scenario is missing.' }
                $bytes = [IO.File]::ReadAllBytes($path)
                $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
                $isAscii = $true
                foreach ($byte in $bytes) { if ($byte -gt 0x7F) { $isAscii = $false; break } }
                Assert-True ($isAscii -or $hasBom) 'a scenario containing non-ASCII text needs a UTF-8 BOM'
            }
        }
    )
}
