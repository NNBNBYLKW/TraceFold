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
        }
    )
}
