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

function Read-TaxonomyScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/50-taxonomy-maintenance-undo.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'Taxonomy maintenance scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

<#
.SYNOPSIS
    Returns one raw driver response in the shape the pinned driver answers with.

.DESCRIPTION
    The fixture starts from windows[].elements[].children carrying the driver's own field names,
    because that is the only shape in which two reports of an element can still be told apart.

    Every element is named 'term' on purpose, so a node can only survive by what the driver actually
    reported and never by its name: the parent carries an id and a child, four siblings carry no id
    at all and are separated only by position, type, enablement or visibility, and one element is
    reported twice with every field identical, which is the one case that has to collapse.
#>
function Get-TaxonomyEvidenceRawTree {
    $json = @'
{
  "windows": [
    {
      "elements": [
        { "type": "Text", "automationId": "term-1", "name": "term", "isEnabled": true, "isOffscreen": false,
          "x": 10, "y": 20, "width": 100, "height": 30,
          "children": [
            { "type": "Text", "name": "term", "isEnabled": true, "isOffscreen": false,
              "x": 12, "y": 22, "width": 40, "height": 20 }
          ] },
        { "type": "Text", "name": "term", "isEnabled": true, "isOffscreen": false,
          "x": 10, "y": 60, "width": 100, "height": 30 },
        { "type": "Text", "name": "term", "isEnabled": true, "isOffscreen": false,
          "x": 10, "y": 100, "width": 100, "height": 30 },
        { "type": "ListItem", "name": "term", "isEnabled": true, "isOffscreen": false,
          "x": 10, "y": 100, "width": 100, "height": 30 },
        { "type": "Text", "name": "term", "isEnabled": false, "isOffscreen": false,
          "x": 10, "y": 140, "width": 100, "height": 30 },
        { "type": "Text", "name": "term", "isEnabled": true, "isOffscreen": true,
          "x": 10, "y": 180, "width": 100, "height": 30 },
        { "type": "Text", "name": "term", "isEnabled": true, "isOffscreen": false,
          "x": 10, "y": 60, "width": 100, "height": 30 }
      ]
    }
  ]
}
'@
    return (ConvertFrom-Json -InputObject $json)
}

<#
.SYNOPSIS
    Returns a small raw driver response built from the caller's JSON, for the cases that need a
    shape the main fixture does not carry.
#>
function ConvertFrom-TaxonomyEvidenceJson {
    param([Parameter(Mandatory)][string] $Json)

    return (ConvertFrom-Json -InputObject $Json)
}

function Get-TaxonomyEvidenceNodesAt {
    param(
        [Parameter(Mandatory)] $Tree,
        [Parameter(Mandatory)][int] $Y
    )

    # Returned without the comma on purpose: every caller wraps the call in @(), and a wrapped
    # return would be one array inside another, which reads as a single node of its own.
    return @($Tree | Where-Object { [int] $_.bounds.y -eq $Y })
}

<#
.SYNOPSIS
    Builds the text of a scope that carries the taxonomy scenario's own functions.

.DESCRIPTION
    The scenario is an owned-run entry point, so dot-sourcing the file would launch the product. Its
    top-level functions are lifted out of the parsed file instead, which keeps the conversion, the
    collapse of repeated reports and the call to the evidence writer the production ones. The only
    thing replaced is the observation, and it is replaced at the transport seam, so the chain that
    runs here is the same one a gate run uses from the driver's answer to the JSON left on disk.
#>
function Get-TaxonomyEvidenceScopeText {
    param(
        [Parameter(Mandatory)][string] $ScenarioPath,
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $OutputDirectory
    )

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($ScenarioPath, [ref] $tokens, [ref] $errors)
    if (@($errors).Count -gt 0) { throw 'The taxonomy scenario does not parse.' }
    $functions = @($ast.FindAll({
                param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst]
            }, $false))
    if (@($functions).Count -eq 0) { throw 'The taxonomy scenario defines no functions.' }

    $lines = @(
        'Set-StrictMode -Version Latest',
        '$ErrorActionPreference = ''Stop''',
        '$productProcessId = 4242',
        '$InspectDepth = 20',
        '$ObservationTimeoutSeconds = 20',
        '$WinAppPath = ''C:\fake\winapp.exe''',
        '$RunToken = ''run-token''',
        # Composed with -f rather than concatenated: inside an array literal a written-out
        # concatenation is not one element, so the path would land on a line of its own.
        ('$OwnedRoot = "{0}"' -f $Root),
        ('$OutputDirectory = "{0}"' -f $OutputDirectory),
        '$artifacts = New-Object ''System.Collections.Generic.List[string]'''
    )
    foreach ($function in $functions) { $lines += $function.Extent.Text }
    $lines += @(
        'function Invoke-WinAppObservation {',
        '    param(',
        '        [Parameter(Mandatory)][string[]] $Arguments,',
        '        [Parameter(Mandatory)][int] $ProcessId,',
        '        [Parameter(Mandatory)][int] $TimeoutSeconds,',
        '        [Parameter(Mandatory)][string] $WinAppPath,',
        '        [Parameter(Mandatory)][string] $RunToken,',
        '        [Parameter(Mandatory)][string] $OwnedRoot',
        '    )',
        '    return [pscustomobject] @{ Status = ''Passed''; TimedOut = $false; Data = $script:taxonomyStubObservation }',
        '}'
    )
    return ($lines -join "`n")
}

<#
.SYNOPSIS
    Runs the taxonomy scenario's evidence chain over one raw driver response and returns the tree
    that was written.

.DESCRIPTION
    The tree is read back from the final JSON rather than from the array the conversion returned, so
    a claim is about the evidence a gate run would leave behind and not about an intermediate value
    that nothing consumes. The owned root is created for this call only and is handed back whether or
    not the conversion succeeded.
#>
function Invoke-TaxonomyEvidenceRoundTrip {
    param(
        [Parameter(Mandatory)] $Raw,
        [Parameter(Mandatory)][string] $ScenarioPath,
        [string] $EvidenceId = 'taxonomy-evidence-regression'
    )

    $root = Join-Path ([IO.Path]::GetTempPath()) ('TraceFold-Uia-Test-' + [guid]::NewGuid().ToString('D'))
    [void][IO.Directory]::CreateDirectory($root)
    try {
        $out = Join-Path $root 'out'
        [void][IO.Directory]::CreateDirectory($out)
        $script:taxonomyStubObservation = $Raw
        $text = Get-TaxonomyEvidenceScopeText -ScenarioPath $ScenarioPath -Root $root -OutputDirectory $out
        . ([scriptblock]::Create($text))
        $file = Save-TaxonomyTree -EvidenceId $EvidenceId
        $json = [IO.File]::ReadAllText((Join-Path $out $file))
        # Returned without the comma on purpose: every caller wraps the call in @(), and a wrapped
        # return would be one array inside another, which reads as a single node of its own.
        return @((ConvertFrom-Json -InputObject $json).tree)
    }
    finally {
        Remove-UiTestTree -Path $root
    }
}

<#
.SYNOPSIS
    Returns every literal a scenario passes as a mutation step id.

.DESCRIPTION
    A step id may be executed once, so the same literal twice in one scenario is a defect the runner
    would only catch at run time. Only literal arguments are collected: a composed id is unique by
    construction as long as its parts are.
#>
function Get-ScenarioLiteralStepIds {
    param([Parameter(Mandatory)][string] $Source)

    $ids = @()
    foreach ($match in [regex]::Matches($Source, "-Step\s+'([^']+)'")) {
        $ids += $match.Groups[1].Value
    }
    return $ids
}

# The environment variable the library snapshot fixture reads to decide its exit status. It is set
# and cleared by the test that exercises the caller's exit-status handling.
$script:LibrarySnapshotFixtureExitVariable = 'TRACEFOLD_LIBRARY_SNAPSHOT_FIXTURE_EXIT'

<#
.SYNOPSIS
    Returns the executable of the native library snapshot fixture, building it once if needed.

.DESCRIPTION
    The production snapshot read is about the bytes a child process leaves on a pipe, so the fixture
    has to be a real child process. A script would be run in this session by the unfixed call and
    would never cross the boundary the defect lives on, so the fixture is a small console program.
    It is built from the repository source into a scratch directory outside the tree, keyed by the
    source digest, so a rebuild happens only when the source changes and no build output ever lands
    in the working copy.
#>
function Get-LibrarySnapshotFixturePath {
    $source = Join-Path $script:uiRoot 'tests/fixtures/library-snapshot-utf8'
    $project = Join-Path $source 'library-snapshot-utf8.csproj'
    $program = Join-Path $source 'Program.cs'
    foreach ($required in @($project, $program)) {
        if (-not [IO.File]::Exists($required)) { throw 'The library snapshot fixture source is missing.' }
    }

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $joined = [IO.File]::ReadAllBytes($program) + [IO.File]::ReadAllBytes($project)
        $stamp = [BitConverter]::ToString($sha.ComputeHash($joined)).Replace('-', '').Substring(0, 16)
    }
    finally { $sha.Dispose() }

    $target = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-library-snapshot-fixture-' + $stamp)
    $exe = Join-Path $target 'bin/library-snapshot-utf8.exe'
    if ([IO.File]::Exists($exe)) { return $exe }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) { throw 'The .NET SDK is required to build the library snapshot fixture.' }
    $scratch = Join-Path $target 'src'
    [void] [IO.Directory]::CreateDirectory($scratch)
    Copy-Item -LiteralPath $program -Destination (Join-Path $scratch 'Program.cs') -Force
    Copy-Item -LiteralPath $project -Destination (Join-Path $scratch 'library-snapshot-utf8.csproj') -Force
    $report = & $dotnet.Source build (Join-Path $scratch 'library-snapshot-utf8.csproj') -c Release `
        -o (Join-Path $target 'bin') 2>&1
    if ($LASTEXITCODE -ne 0 -or -not [IO.File]::Exists($exe)) {
        throw ('The library snapshot fixture did not build: ' + (($report | Out-String).Trim()))
    }
    return $exe
}

<#
.SYNOPSIS
    Builds the text of a scope that carries the library scenario's own snapshot read.

.DESCRIPTION
    The library scenario is an owned-run entry point, so dot-sourcing the file would launch the
    product. Its Read-LibraryState function is lifted out of the parsed file instead, which keeps the
    JSON parse, the field selection, the redaction, the file write and the artifact registration the
    production ones. Only the values around it are supplied, and the support path is a real child
    process, so the chain that runs here is the one a gate run uses.
#>
function Get-LibrarySnapshotScopeText {
    param(
        [Parameter(Mandatory)][string] $ScenarioPath,
        [Parameter(Mandatory)][string] $SupportPath,
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $OutputDirectory
    )

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($ScenarioPath, [ref] $tokens, [ref] $errors)
    if (@($errors).Count -gt 0) { throw 'The library scenario does not parse.' }

    $definition = $null
    foreach ($candidate in @($ast.FindAll({
                param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst]
            }, $false))) {
        if ($candidate.Name -ceq 'Read-LibraryState') { $definition = $candidate }
    }
    if ($null -eq $definition) { throw 'The library scenario does not define Read-LibraryState.' }

    $lines = @(
        'Set-StrictMode -Version Latest',
        '$ErrorActionPreference = ''Stop''',
        # Composed with -f rather than concatenated: inside an array literal a written-out
        # concatenation is not one element, so the path would land on a line of its own.
        ('$SupportPath = "{0}"' -f $SupportPath),
        ('$OwnedRoot = "{0}"' -f $OwnedRoot),
        '$RunToken = ''run-token''',
        ('$OutputDirectory = "{0}"' -f $OutputDirectory),
        '$artifacts = New-Object ''System.Collections.Generic.List[string]'''
    )
    $lines += $definition.Extent.Text
    return ($lines -join "`n")
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
            Name = 'the runner registers the single instance scenario between diagnostics and visual'
            Body = {
                Assert-Equal '61-single-instance-safety.ps1' (Get-UiAcceptanceScenarioFile -Name 'SingleInstance')
                $names = @(Get-UiAcceptanceScenarioName)
                Assert-True ($names -ccontains 'SingleInstance') 'the registry must offer SingleInstance'
                # Position matters: it is the execution order an All request produces, and the
                # diagnostics read-only slice must finish before the single instance checks start.
                $diagnosticsIndex = [array]::IndexOf($names, 'Diagnostics')
                $singleIndex = [array]::IndexOf($names, 'SingleInstance')
                $visualIndex = [array]::IndexOf($names, 'Visual')
                Assert-True ($diagnosticsIndex -ge 0) 'Diagnostics must be registered'
                Assert-True ($visualIndex -ge 0) 'Visual must be registered'
                Assert-True ($singleIndex -eq ($diagnosticsIndex + 1)) 'SingleInstance must follow Diagnostics'
                Assert-True ($visualIndex -eq ($singleIndex + 1)) 'Visual must follow SingleInstance'
                $entry = Get-UiAcceptanceScenarioFile -Name 'SingleInstance'
                Assert-True ([IO.File]::Exists((Join-Path $script:uiRoot ('scenarios/' + $entry)))) 'the entry file must exist'
            }
        },
        @{
            Name = 'the single instance scenario carries the behaviour the plan requires'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                Assert-True ([IO.File]::Exists($path)) 'the single instance scenario must exist'
                $tokens = $null
                $errors = $null
                $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $source = Get-Content -LiteralPath $path -Raw

                # The launch seam and the foreground seam are host seams, so a dispatcher can drive
                # this file without an interactive desktop.
                foreach ($required in @('ScenarioId', 'ExecutablePath', 'ContractPath', 'OwnedRoot', 'RunToken',
                                        'OutputDirectory', 'SupportPath', 'WinAppPath', 'LaunchProcess')) {
                    Assert-True ($source -cmatch ('\$' + $required + '\b')) ('the scenario must accept ' + $required)
                }

                # The second start must run the same executable against the same owned root and its
                # exit code must be the product's own: 0 is required and anything else fails.
                Assert-True ($source -cmatch 'TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT') 'the second start must carry the owned data root'
                Assert-True ($source -cmatch 'WaitForExit') 'the second process must be waited for'
                Assert-True ($source -cmatch 'ExitCode') 'the real exit code must be read'
                Assert-True ($source -cmatch "secondaryExit -ne 0") 'a non-zero second exit must fail the scenario'

                # Activation is proved by reading the foreground, never by making the window
                # foreground first; the inspect calls are observability only.
                Assert-True ($source -cmatch 'GetForegroundWindow') 'the foreground must be read'
                Assert-True ($source -cmatch 'ForegroundCheck') 'the foreground seam must be used'
                Assert-True (-not ($source -cmatch 'AppActivate')) 'foreground coaxing is banned'
                Assert-True (-not ($source -cmatch 'SetForegroundWindow')) 'forcing the foreground is banned'
                Assert-True ($source -cmatch 'not offered as the activation proof') 'observability must not be offered as activation'

                # The database rule is uniqueness, not a byte freeze.
                Assert-True ($source -cmatch 'second_database_created') 'a second database must fail the scenario'
                Assert-True ($source -cmatch 'second_library_used') 'another library must fail the scenario'
                Assert-True (-not ($source -cmatch 'Get-FileHash.*db')) 'no byte freeze of the database is required'

                # Termination rules.
                Assert-True (-not ($source -cmatch 'Stop-Process -Name')) 'termination by process name is banned'
                Assert-True (-not ($source -cmatch 'Start-Sleep -Seconds')) 'fixed sleeps are banned'
                Assert-True ($source -cmatch 'CloseMainWindow') 'the primary must be closed through its own window'
                Assert-True ($source -cmatch 'restartPid') 'the restart must be its own process'

                # The blocked path is for conditions the host cannot provide, not for product failures.
                Assert-True ($source -cmatch 'single\.instance\.desktop_unavailable') 'the desktop precondition must be Blocked'
                Assert-True ($source -cmatch "return \[pscustomobject\]") 'the scenario must return a result record'
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
                # The read-only facts come from the snapshot, and the snapshot is taken through the
                # support helper that pins the payload encoding instead of the caller's ambient one.
                Assert-Contains $source 'Invoke-UiAcceptanceSupportCommand'
                Assert-Contains $source "'snapshot', '--data-root'"
                Assert-NotContains $source 'snapshot --data-root'
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
        },
        @{
            Name = 'the library snapshot read keeps UTF-8 terms when the caller console is a double-byte code page'
            Body = {
                $scenarioPath = Join-Path $script:uiRoot 'scenarios/40-library-filter-naming-undo.ps1'
                $fixture = Get-LibrarySnapshotFixturePath
                # Composed from code points so this file stays ASCII; the fixture builds the same terms
                # the same way, and the ones written here end in a character whose final UTF-8 byte is
                # a lead byte in the double-byte code pages.
                $authorOne = ([string][char]0x4F5C) + [char]0x8005 + [char]0x7532
                $authorTwo = ([string][char]0x4F5C) + [char]0x8005 + [char]0x4E59
                $seriesOne = ([string][char]0x7CFB) + [char]0x5217 + [char]0x7532
                $tagOne = ([string][char]0x591C) + [char]0x666F
                $tagTwo = ([string][char]0x98CE) + [char]0x666F

                $root = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-library-snapshot-' + [guid]::NewGuid().ToString('D'))
                $out = Join-Path $root 'out'
                [void][IO.Directory]::CreateDirectory($out)
                # The caller's console is what an unpinned capture reads the child's pipe through, so
                # the test states it instead of inheriting whatever the machine happens to be set to.
                $savedEncoding = [Console]::OutputEncoding
                try {
                    [Console]::OutputEncoding = [Text.Encoding]::GetEncoding(936)
                    Assert-Equal 936 ([Console]::OutputEncoding.CodePage) 'the caller console has to be the double-byte one under test'

                    $text = Get-LibrarySnapshotScopeText -ScenarioPath $scenarioPath -SupportPath $fixture `
                        -OwnedRoot $root -OutputDirectory $out
                    . ([scriptblock]::Create($text))

                    $state = Read-LibraryState -EvidenceId 'library-snapshot-utf8'

                    # The terms are compared as values, never as a count: what the defect costs is the
                    # term itself.
                    Assert-Equal 2 @($state.taxonomy.author).Count
                    Assert-Equal $authorOne ([string] @($state.taxonomy.author)[0])
                    Assert-Equal $authorTwo ([string] @($state.taxonomy.author)[1])
                    Assert-Equal 1 @($state.taxonomy.series).Count
                    Assert-Equal $seriesOne ([string] @($state.taxonomy.series)[0])
                    Assert-Equal 2 @($state.taxonomy.tag).Count
                    Assert-Equal $tagOne ([string] @($state.taxonomy.tag)[0])
                    Assert-Equal $tagTwo ([string] @($state.taxonomy.tag)[1])

                    $photo = @($state.formalAssets | Where-Object { [string] $_.actualFilename -ceq 'photo.jpg' })
                    Assert-Equal 1 @($photo).Count
                    Assert-Equal 'key-photo' ([string] $photo[0].assetKey)
                    Assert-Equal 'ACC-photo-3' ([string] $photo[0].displayName)
                    Assert-Equal 'image' ([string] $photo[0].mediaKind)
                    Assert-Equal $authorOne ([string] @($photo[0].authors)[0])
                    Assert-Equal $seriesOne ([string] $photo[0].series)
                    Assert-Equal $tagOne ([string] @($photo[0].tags)[0])

                    $third = @($state.formalAssets | Where-Object { [string] $_.actualFilename -ceq 'photo-third.jpg' })
                    Assert-Equal 1 @($third).Count
                    Assert-Equal 2 @($third[0].tags).Count
                    Assert-Equal $tagOne ([string] @($third[0].tags)[0])
                    Assert-Equal $tagTwo ([string] @($third[0].tags)[1])
                    Assert-Equal $authorOne ([string] @($third[0].authors)[0])

                    # What a later read consumes is the file, not the return value, so the file is read
                    # back and checked field by field.
                    Assert-Equal 1 $artifacts.Count 'the snapshot is registered exactly once'
                    $relative = [string] $artifacts[0]
                    Assert-Equal 'library-snapshot-utf8-snapshot.json' $relative
                    $snapshotPath = Join-Path $out $relative
                    $raw = [IO.File]::ReadAllBytes($snapshotPath)
                    Assert-True (-not ($raw.Length -ge 3 -and $raw[0] -eq 0xEF -and $raw[1] -eq 0xBB -and $raw[2] -eq 0xBF)) `
                        'the snapshot is written without a BOM'
                    $written = [IO.File]::ReadAllText($snapshotPath, [Text.Encoding]::UTF8)
                    Assert-True ($written.Contains($authorOne)) 'the file carries the term as characters, not as an escape'
                    Assert-NotContains $written '\u7532' 'a JSON escape would survive the defect this test exists for'
                    $roundTrip = ConvertFrom-UiAcceptanceJson -Text $written -Source 'library-snapshot'
                    Assert-Equal $authorOne ([string] @($roundTrip.taxonomy.author)[0])
                    Assert-Equal $seriesOne ([string] @($roundTrip.taxonomy.series)[0])
                    Assert-Equal $tagTwo ([string] @($roundTrip.taxonomy.tag)[1])
                    $writtenThird = @($roundTrip.formalAssets | Where-Object { [string] $_.actualFilename -ceq 'photo-third.jpg' })
                    Assert-Equal 1 @($writtenThird).Count
                    Assert-Equal '[redacted-token]' ([string] $writtenThird[0].displayName) 'the write still redacts the run token'
                    Assert-NotContains $written 'run-token' 'the run token must not reach the evidence file'
                    Assert-Equal 'run-token' ([string] $third[0].displayName) 'the returned data is the payload the CLI sent'
                }
                finally {
                    [Console]::OutputEncoding = $savedEncoding
                    Remove-UiTestTree -Path $root
                }
                Assert-Equal $savedEncoding.CodePage ([Console]::OutputEncoding.CodePage) 'the console encoding has to be handed back'
            }
        },
        @{
            Name = 'the library snapshot read takes its exit status from the invocation it made'
            Body = {
                $scenarioPath = Join-Path $script:uiRoot 'scenarios/40-library-filter-naming-undo.ps1'
                $variable = $script:LibrarySnapshotFixtureExitVariable
                $fixture = Get-LibrarySnapshotFixturePath

                $root = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-library-snapshot-' + [guid]::NewGuid().ToString('D'))
                $out = Join-Path $root 'out'
                [void][IO.Directory]::CreateDirectory($out)
                $savedEncoding = [Console]::OutputEncoding
                $exitExisted = $null -ne (Get-Variable -Name 'LASTEXITCODE' -Scope Global -ErrorAction SilentlyContinue)
                $exitValue = $null
                if ($exitExisted) { $exitValue = (Get-Variable -Name 'LASTEXITCODE' -Scope Global).Value }
                $savedFixtureExit = [Environment]::GetEnvironmentVariable($variable, 'Process')
                try {
                    [Console]::OutputEncoding = [Text.Encoding]::GetEncoding(936)
                    Assert-Equal 936 ([Console]::OutputEncoding.CodePage) 'the caller console has to be the double-byte one under test'

                    $text = Get-LibrarySnapshotScopeText -ScenarioPath $scenarioPath -SupportPath $fixture `
                        -OwnedRoot $root -OutputDirectory $out
                    . ([scriptblock]::Create($text))

                    # A refusal is reported even though the exit status the caller is carrying says
                    # otherwise, and it leaves nothing behind for a later read to pick up.
                    [Environment]::SetEnvironmentVariable($variable, '5', 'Process')
                    $global:LASTEXITCODE = 0
                    $failure = ''
                    try { $null = Read-LibraryState -EvidenceId 'library-snapshot-refused' }
                    catch { $failure = $_.Exception.Message }
                    Assert-Equal 'library.snapshot_unavailable' $failure 'a refused snapshot reports the unavailable condition'
                    Assert-Equal 0 $artifacts.Count 'a refused snapshot is not registered'
                    Assert-Equal 0 @([IO.Directory]::GetFiles($out)).Count 'a refused snapshot is not written'

                    # The other direction: a success stays a success even though the exit status the
                    # caller is carrying is not a successful one.
                    [Environment]::SetEnvironmentVariable($variable, $null, 'Process')
                    $global:LASTEXITCODE = 7
                    $state = Read-LibraryState -EvidenceId 'library-snapshot-succeeded'
                    Assert-Equal 3 @($state.formalAssets).Count
                    Assert-Equal 1 $artifacts.Count 'the snapshot that succeeded is registered once'
                }
                finally {
                    [Environment]::SetEnvironmentVariable($variable, $savedFixtureExit, 'Process')
                    if ($exitExisted) { Set-Variable -Name 'LASTEXITCODE' -Value $exitValue -Scope Global }
                    else { Remove-Variable -Name 'LASTEXITCODE' -Scope Global -ErrorAction SilentlyContinue }
                    [Console]::OutputEncoding = $savedEncoding
                    Remove-UiTestTree -Path $root
                }
                Assert-Equal $savedEncoding.CodePage ([Console]::OutputEncoding.CodePage) 'the console encoding has to be handed back'
                Assert-Equal $exitExisted ($null -ne (Get-Variable -Name 'LASTEXITCODE' -Scope Global -ErrorAction SilentlyContinue)) `
                    'the exit status has to be handed back exactly as it was found'
            }
        },
        @{
            Name = 'the taxonomy scenario exists, parses and reports Passed'
            Body = {
                $source = Read-TaxonomyScenarioSource
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
            Name = 'the taxonomy scenario merges through the page and the preview dialog, then undoes once'
            Body = {
                $source = Read-TaxonomyScenarioSource
                # The terms are created through the ordinary metadata UI, then found on the taxonomy page
                # through its real kind, search and selection operations.
                Assert-Contains $source 'LibraryAuthorInput'
                Assert-Contains $source 'NavTaxonomy'
                Assert-Contains $source 'TaxonomyKindList'
                Assert-Contains $source 'TaxonomySearchBox'
                Assert-Contains $source 'TaxonomyTermList'
                Assert-Contains $source 'TaxonomySelectedCount'
                Assert-Contains $source 'TaxonomyMergeButton'
                # The merge runs on the page's own preview dialog, and the confirmation happens once.
                Assert-Contains $source 'TaxonomyMergeDialog'
                Assert-Contains $source 'TaxonomyMergeSources'
                Assert-Contains $source 'TaxonomyMergeTargetResults'
                Assert-Contains $source 'TaxonomyMergeTargetSearch'
                Assert-Contains $source 'TaxonomyMergeAffectedFiles'
                Assert-Contains $source 'TaxonomyMergeAffectedSummary'
                Assert-Contains $source 'TaxonomyMergeApplyButton'
                Assert-Equal 1 ([regex]::Matches($source, "ui','invoke','TaxonomyMergeApplyButton'").Count)
                # Rows of these lists carry no name of their own, so they are matched by the text they
                # render, and the affected files are compared as a set, never as a count.
                Assert-Contains $source 'Get-UiAcceptanceListRowTexts'
                Assert-Contains $source 'Test-UiAcceptanceRowSetMatch'
                # The merge is verified per file and per reference, and the source has to be gone.
                Assert-Contains $source 'taxonomy.merge_source_gone'
                Assert-Contains $source 'taxonomy.merge_references'
                Assert-Contains $source 'taxonomy.merge_duplicate'
                Assert-Contains $source 'taxonomy.merge_target_kept'
                # One undo for the merge, and the pre-merge state has to be restored exactly.
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'undo-merge'").Count)
                Assert-Contains $source 'taxonomy.undo_restore'
            }
        },
        @{
            Name = 'the taxonomy scenario proves the delete guard and deletes only an unreferenced term'
            Body = {
                $source = Read-TaxonomyScenarioSource
                # A referenced term must be shown as blocked, and the guard is read from the product:
                # the delete action is disabled and the blocking term is listed.
                Assert-Contains $source 'TaxonomyDeleteBlockers'
                Assert-Contains $source 'TaxonomyDeleteButton'
                Assert-Contains $source 'taxonomy.delete_guard'
                Assert-True ($source.Contains("-ceq 'False'")) 'the guarded delete action must be asserted disabled'
                # The reference is then released through the ordinary metadata UI, and the product's own
                # unused filter is what proves the term is unreferenced.
                Assert-Contains $source 'TaxonomyReferenceFilter'
                Assert-Contains $source 'taxonomy.term_unreferenced'
                # One confirm and one undo for the delete, on the dialog the product opens for it.
                Assert-Contains $source 'TaxonomyDeleteConfirmDialog'
                Assert-Contains $source 'TaxonomyDeleteTermList'
                Assert-Contains $source 'taxonomy.delete_completion'
                Assert-Contains $source 'taxonomy.delete_confirm'
                # One undo for the delete, and the term has to come back.
                Assert-Equal 1 ([regex]::Matches($source, "-Step 'undo-delete'").Count)
                Assert-Contains $source 'taxonomy.delete_restore'
            }
        },
        @{
            Name = 'the taxonomy scenario checks files, identity, relunch persistence and cleanup'
            Body = {
                $source = Read-TaxonomyScenarioSource
                Assert-Contains $source 'Get-FileHash'
                Assert-Contains $source 'taxonomy.file_changed'
                Assert-Contains $source 'taxonomy.record_identity'
                # The app is closed and reopened, and the persisted state is read from the product UI
                # and from a read-only snapshot taken while it is closed.
                Assert-Contains $source 'Stop-OwnedProcessTree'
                Assert-Contains $source 'taxonomy.restart_stop'
                Assert-Contains $source 'taxonomy.restart_window'
                Assert-Contains $source 'taxonomy.restart_persistence'
                # Every stage writes what the product showed, so the staged claims are auditable.
                Assert-Contains $source 'Save-TaxonomyStageState'
                Assert-Contains $source 'taxonomy-stage-'
            }
        },
        @{
            Name = 'the taxonomy scenario is safe, independent and uses one id per action'
            Body = {
                $source = Read-TaxonomyScenarioSource
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
                # The read-only facts come from the snapshot, and the snapshot is taken through the
                # support helper that pins the payload encoding instead of the caller's ambient one.
                Assert-Contains $source 'Invoke-UiAcceptanceSupportCommand'
                Assert-Contains $source "'snapshot','--data-root'"
                Assert-NotContains $source 'snapshot --data-root'
                # No temporary diagnostic may remain in the delivered scenario.
                Assert-NotContains $source 'DIAGNOSTIC (temporary)'
                Assert-NotContains $source 'taxonomy.diag'
                # The taxonomy page's search box is typed into rather than set, so a query would be
                # appended to whatever an earlier stage left there. Every query on that box is
                # therefore preceded by a clear, and the clear is a real dispatched action rather
                # than an assumption that the box was already empty.
                $queries = @([regex]::Matches($source, "Set-TaxonomySearch -OuterId 'TaxonomySearchBox'"))
                $clears = @([regex]::Matches($source, "Clear-TaxonomySearch -OuterId 'TaxonomySearchBox'"))
                Assert-True ($queries.Count -ge 3) 'the taxonomy search box must be queried by more than one stage'
                Assert-True ($clears.Count -ge 2) 'each later query on the taxonomy search box needs a clear first'
                # Evidence is written from the driver's own answer converted into the reviewed node
                # shape, because the writer's input contract is that shape and not the driver's.
                Assert-Contains $source 'ConvertTo-TaxonomyReviewedNode'
                Assert-Contains $source "Save-TaxonomyTree -EvidenceId"
                # A property is never read off a command that was wrapped in an array sub-expression:
                # under strict mode the property is then looked for on the array and refused, which
                # would make a claim fail for a reason that has nothing to do with the product.
                Assert-True ($source -cnotmatch '[@]\(Get-Taxonomy[A-Za-z]+[^)]*\)\.[A-Za-z]') `
                    'a property must not be read off an array-wrapped command'
                # A read-only claim is never taken from a total: ownership is read per file.
                Assert-Contains $source 'Get-TaxonomyTermOwners'
                # Every literal step id is used once, so no dispatched action can be repeated by copy.
                $ids = Get-ScenarioLiteralStepIds -Source $source
                Assert-True (@($ids).Count -ge 10) 'the scenario must carry a step id per dispatched action'
                $duplicated = @($ids | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
                Assert-Equal 0 @($duplicated).Count ('duplicate step ids: ' + (@($duplicated) -join ', '))
            }
        },
        @{
            Name = 'the runner points the taxonomy entry at the taxonomy scenario and leaves the other placeholders alone'
            Body = {
                Assert-Equal '50-taxonomy-maintenance-undo.ps1' (Get-UiAcceptanceScenarioFile -Name 'Taxonomy')
                $names = @(Get-UiAcceptanceScenarioName)
                Assert-True ($names -ccontains 'Taxonomy')
                # The aggregate entries that no task has implemented stay exactly as they were, so a
                # single scenario passing can never be read as the whole suite passing.
                Assert-Equal '02-conflict.ps1' (Get-UiAcceptanceScenarioFile -Name 'Conflict')
                Assert-Equal '03-library.ps1' (Get-UiAcceptanceScenarioFile -Name 'Library')
                Assert-Equal '06-visual.ps1' (Get-UiAcceptanceScenarioFile -Name 'Visual')
                foreach ($name in $names) {
                    Assert-True ((Get-UiAcceptanceScenarioFile -Name $name) -cmatch '^[0-9]{2}-[a-z][a-z0-9-]*\.ps1$')
                }
            }
        },
        @{
            Name = 'the taxonomy scenario is readable by Windows PowerShell 5.1'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/50-taxonomy-maintenance-undo.ps1'
                if (-not [IO.File]::Exists($path)) { throw 'Taxonomy maintenance scenario is missing.' }
                $bytes = [IO.File]::ReadAllBytes($path)
                $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
                $isAscii = $true
                foreach ($byte in $bytes) { if ($byte -gt 0x7F) { $isAscii = $false; break } }
                Assert-True ($isAscii -or $hasBom) 'a scenario containing non-ASCII text needs a UTF-8 BOM'
            }
        },
        @{
            Name = 'the taxonomy evidence keeps same-named nodes that differ in position, type or state'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/50-taxonomy-maintenance-undo.ps1'
                $tree = @(Invoke-TaxonomyEvidenceRoundTrip -Raw (Get-TaxonomyEvidenceRawTree) -ScenarioPath $path)
                # One node per element the driver reported, the child included, and the element the
                # driver reported twice with every field identical counted once.
                Assert-Equal 7 @($tree).Count 'every element the driver reported has to reach the evidence'
                # Every element of the fixture is named the same, so a node that survived because of
                # its name would prove nothing about what the driver actually reported.
                foreach ($node in $tree) {
                    Assert-Equal 'term' $node.name 'the fixture names every element the same'
                }
                $parent = @($tree | Where-Object { $_.automationId -ceq 'term-1' })
                Assert-Equal 1 @($parent).Count 'the parent has to keep the id the driver reported'
                Assert-Equal '' $parent[0].parentAutomationId
                Assert-Equal 'Text' $parent[0].controlType
                Assert-True $parent[0].enabled
                Assert-True (-not $parent[0].offscreen)
                Assert-Equal 10 $parent[0].bounds.x
                Assert-Equal 20 $parent[0].bounds.y
                Assert-Equal 100 $parent[0].bounds.width
                Assert-Equal 30 $parent[0].bounds.height
                # A child is its own node and keeps the id of the parent it was read under, so the
                # nesting the driver reported is still readable in the evidence.
                $children = @($tree | Where-Object { $_.parentAutomationId -ceq 'term-1' })
                Assert-Equal 1 @($children).Count 'the nested child has to reach the evidence'
                Assert-Equal 'Text' $children[0].controlType
                Assert-Equal 12 $children[0].bounds.x
                Assert-Equal 22 $children[0].bounds.y
                Assert-Equal 40 $children[0].bounds.width
                Assert-Equal 20 $children[0].bounds.height
                # Position alone separates a node: the five siblings closest to the parent carry no
                # id at all, and two of them differ from another sibling in nothing but position.
                $atSixty = @(Get-TaxonomyEvidenceNodesAt -Tree $tree -Y 60)
                Assert-Equal 1 @($atSixty).Count 'the element reported twice, identically, collapses once'
                $atHundred = @(Get-TaxonomyEvidenceNodesAt -Tree $tree -Y 100)
                Assert-Equal 2 @($atHundred).Count 'a same-named element of another type is its own node'
                $types = @($atHundred | ForEach-Object { $_.controlType } | Sort-Object)
                Assert-Equal 'ListItem' $types[0]
                Assert-Equal 'Text' $types[1]
                $atHundredForty = @(Get-TaxonomyEvidenceNodesAt -Tree $tree -Y 140)
                Assert-Equal 1 @($atHundredForty).Count
                Assert-True (-not $atHundredForty[0].enabled) 'a disabled element stays distinguishable from an enabled one'
                Assert-True (-not $atHundredForty[0].offscreen)
                $atHundredEighty = @(Get-TaxonomyEvidenceNodesAt -Tree $tree -Y 180)
                Assert-Equal 1 @($atHundredEighty).Count
                Assert-True $atHundredEighty[0].offscreen 'an off screen element stays distinguishable from a visible one'
                Assert-True $atHundredEighty[0].enabled
                # The child and the five siblings carry no id, and they are all still there.
                $anonymous = @($tree | Where-Object { $_.automationId -ceq '' })
                Assert-Equal 6 @($anonymous).Count 'an empty id is not an identity of its own'
            }
        },
        @{
            Name = 'the taxonomy evidence keeps an unmeasured element and still refuses a half-reported rectangle'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/50-taxonomy-maintenance-undo.ps1'
                $unmeasured = ConvertFrom-TaxonomyEvidenceJson -Json @'
{"windows":[{"elements":[{"type":"Text","name":"term","isEnabled":true,"isOffscreen":false}]}]}
'@
                $tree = @(Invoke-TaxonomyEvidenceRoundTrip -Raw $unmeasured -ScenarioPath $path)
                Assert-Equal 1 @($tree).Count
                Assert-Equal 'Text' $tree[0].controlType
                # An element the driver can enumerate but not measure is recorded with zeroes in
                # every field, so it keeps the shape of a measured node without claiming a position.
                Assert-Equal 0 $tree[0].bounds.x
                Assert-Equal 0 $tree[0].bounds.y
                Assert-Equal 0 $tree[0].bounds.width
                Assert-Equal 0 $tree[0].bounds.height
                # A rectangle the driver reported only in part describes no rectangle, so it is
                # refused rather than completed with edges the driver never measured.
                $partial = ConvertFrom-TaxonomyEvidenceJson -Json @'
{"windows":[{"elements":[{"type":"Text","name":"term","isEnabled":true,"isOffscreen":false,"x":10,"y":20}]}]}
'@
                Assert-Throws {
                    $null = Invoke-TaxonomyEvidenceRoundTrip -Raw $partial -ScenarioPath $path
                } 'a half-reported rectangle has to be refused'
            }
        },
        @{
            Name = 'the taxonomy evidence refuses a response that is not the driver shape'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/50-taxonomy-maintenance-undo.ps1'
                $message = ''
                try {
                    $null = Invoke-TaxonomyEvidenceRoundTrip -Raw ([pscustomobject] @{ tree = @() }) -ScenarioPath $path
                }
                catch { $message = $_.Exception.Message }
                Assert-Contains $message 'taxonomy.driver_schema_incompatible'
            }
        },
        @{
            Name = 'the taxonomy evidence keeps a pair of nodes that differ only in enablement'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/50-taxonomy-maintenance-undo.ps1'
                # Everything else the driver reports is the same for both: the automation id, the
                # selector, the class, the name, the native type, the rectangle and the visibility.
                # Enablement is therefore the only thing that can tell these two apart, so a collapse
                # that stops noticing the state flags turns them into one node here and nowhere else.
                $raw = ConvertFrom-TaxonomyEvidenceJson -Json @'
{"windows":[{"elements":[
{"type":"ListItem","selector":"row","className":"TermRow","automationId":"term-row","name":"term","isEnabled":true,"isOffscreen":false,"x":10,"y":20,"width":100,"height":30},
{"type":"ListItem","selector":"row","className":"TermRow","automationId":"term-row","name":"term","isEnabled":false,"isOffscreen":false,"x":10,"y":20,"width":100,"height":30}
]}]}
'@
                $tree = @(Invoke-TaxonomyEvidenceRoundTrip -Raw $raw -ScenarioPath $path `
                        -EvidenceId 'taxonomy-evidence-enabled-pair')
                Assert-Equal 2 @($tree).Count 'enablement alone separates one node from another'
                foreach ($node in $tree) {
                    Assert-Equal 'term-row' $node.automationId
                    Assert-Equal '' $node.parentAutomationId
                    Assert-Equal 'ListItem' $node.controlType
                    Assert-Equal 'term' $node.name
                    Assert-True (-not $node.offscreen) 'both nodes of the pair are on screen'
                    # Same rectangle in both nodes: the pair was never separated by its position.
                    Assert-Equal 10 $node.bounds.x
                    Assert-Equal 20 $node.bounds.y
                    Assert-Equal 100 $node.bounds.width
                    Assert-Equal 30 $node.bounds.height
                }
                Assert-Equal 1 @($tree | Where-Object { $_.enabled }).Count 'the enabled node has to survive'
                Assert-Equal 1 @($tree | Where-Object { -not $_.enabled }).Count 'the disabled node has to survive beside it'
            }
        },
        @{
            Name = 'the taxonomy evidence keeps a pair of nodes that differ only in visibility'
            Body = {
                $path = Join-Path $script:uiRoot 'scenarios/50-taxonomy-maintenance-undo.ps1'
                # The counterpart of the enablement pair: identical automation id, selector, class,
                # name, type, rectangle and enablement, with visibility as the only difference.
                $raw = ConvertFrom-TaxonomyEvidenceJson -Json @'
{"windows":[{"elements":[
{"type":"ListItem","selector":"row","className":"TermRow","automationId":"term-row","name":"term","isEnabled":true,"isOffscreen":false,"x":10,"y":20,"width":100,"height":30},
{"type":"ListItem","selector":"row","className":"TermRow","automationId":"term-row","name":"term","isEnabled":true,"isOffscreen":true,"x":10,"y":20,"width":100,"height":30}
]}]}
'@
                $tree = @(Invoke-TaxonomyEvidenceRoundTrip -Raw $raw -ScenarioPath $path `
                        -EvidenceId 'taxonomy-evidence-offscreen-pair')
                Assert-Equal 2 @($tree).Count 'visibility alone separates one node from another'
                foreach ($node in $tree) {
                    Assert-Equal 'term-row' $node.automationId
                    Assert-Equal '' $node.parentAutomationId
                    Assert-Equal 'ListItem' $node.controlType
                    Assert-Equal 'term' $node.name
                    Assert-True $node.enabled 'both nodes of the pair are enabled'
                    Assert-Equal 10 $node.bounds.x
                    Assert-Equal 20 $node.bounds.y
                    Assert-Equal 100 $node.bounds.width
                    Assert-Equal 30 $node.bounds.height
                }
                Assert-Equal 1 @($tree | Where-Object { $_.offscreen }).Count 'the off screen node has to survive'
                Assert-Equal 1 @($tree | Where-Object { -not $_.offscreen }).Count 'the visible node has to survive beside it'
            }
        }
    )
}
