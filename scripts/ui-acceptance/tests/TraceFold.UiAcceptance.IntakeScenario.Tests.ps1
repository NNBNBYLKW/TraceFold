# Main-chain contract tests. The driver protocol itself is exercised by the Workflows suite;
# these tests lock the scenario ordering and reject unsafe shortcuts before a real window run.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-IntakeScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/10-main-chain.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'MainChain scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Get-TestCases {
    return @(
        @{
            Name = 'the main chain parses and has no unfinished stop marker'
            Body = {
                $source = Read-IntakeScenarioSource
                $tokens = $null
                $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput(
                    $source, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 @($errors).Count
                Assert-NotContains $source "throw 'main.chain_incomplete'"
                Assert-Contains $source "Status = 'Passed'"
            }
        },
        @{
            Name = 'the UI mutations follow the owned intake, library, taxonomy and diagnostic order'
            Body = {
                $source = Read-IntakeScenarioSource
                $ordered = @(
                    "-Step 'folder.save'",
                    "-Step 'scan'",
                    "-Step 'select-video'",
                    "-Step 'batch.submit'",
                    "-Step 'video.start-processing'",
                    "-Step 'video.ingest'",
                    "-Step 'photo.ingest'",
                    "-Step 'library.filter-apply'",
                    "-Step 'library.naming-apply'",
                    "-Step 'library.naming-undo'",
                    "-Step 'taxonomy.create-confirm'",
                    "-Step 'taxonomy.rename-confirm'",
                    "-Step 'taxonomy.delete-confirm'",
                    "-Step 'taxonomy.undo'",
                    "-Step 'diagnostics.open-tab'",
                    "-Step 'restart.open-settings'"
                )
                $previous = -1
                foreach ($token in $ordered) {
                    $index = $source.IndexOf($token, [StringComparison]::Ordinal)
                    Assert-True ($index -gt $previous) ('Missing or out-of-order step: ' + $token)
                    $previous = $index
                }
            }
        },
        @{
            Name = 'each literal mutation step identity occurs once'
            Body = {
                $source = Read-IntakeScenarioSource
                $actions = [regex]::Matches($source,
                    "(?m)Invoke-MainAction\s+-Step\s+'([^']+)'", 'IgnoreCase')
                $keyboard = [regex]::Matches($source,
                    "(?m)Invoke-WinAppKeyboard\s+-StepId\s+'([^']+)'", 'IgnoreCase')
                $names = @($actions | ForEach-Object { $_.Groups[1].Value }) +
                    @($keyboard | ForEach-Object { $_.Groups[1].Value })
                Assert-True ($names.Count -ge 25)
                Assert-Equal $names.Count @($names | Select-Object -Unique).Count
            }
        },
        @{
            Name = 'fixtures are checked before copying and snapshots bracket durable UI actions'
            Body = {
                $source = Read-IntakeScenarioSource
                $verify = $source.IndexOf('Test-UiAcceptanceFixtureAsset', [StringComparison]::Ordinal)
                $copy = $source.IndexOf('[IO.File]::Copy($verified.Source', [StringComparison]::Ordinal)
                Assert-True ($verify -ge 0 -and $copy -gt $verify)
                foreach ($checkpoint in @('video-queued','video-ready','video-ingested',
                    'photo-ingested','library-named','library-undo','taxonomy-created',
                    'taxonomy-renamed','taxonomy-deleted','taxonomy-undo','closed','reopened')) {
                    Assert-Contains $source ("Save-MainSnapshot -Step '" + $checkpoint + "'")
                }
                Assert-Contains $source "snapshot --data-root"
                Assert-Contains $source 'Stop-OwnedProcessTree'
            }
        },
        @{
            Name = 'the scenario cannot bypass product UI or resend uncertain mutations'
            Body = {
                $source = Read-IntakeScenarioSource
                foreach ($forbidden in @('Start-Sleep', 'Stop-Process', 'PickInboxFolderButton',
                    'PickLibraryFolderButton', 'PickWorkFolderButton', "'ui','click'",
                    'sqlite3', 'UPDATE media_assets', 'INSERT INTO media_assets',
                    "'ui','invoke','InboxCard_", "'ui','invoke','LibraryCard_")) {
                    Assert-True ($source.IndexOf($forbidden, [StringComparison]::OrdinalIgnoreCase) -lt 0) `
                        ('Unsafe shortcut found: ' + $forbidden)
                }
                Assert-Contains $source 'Invoke-UiAcceptanceResolvedMutation'
                Assert-Contains $source 'Get-UiAcceptanceDescendantSelector'
                Assert-Contains $source 'Invoke-WinAppMutation'
            }
        }
    )
}
