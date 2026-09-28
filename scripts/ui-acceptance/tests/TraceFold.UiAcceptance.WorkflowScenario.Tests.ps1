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
        }
    )
}
