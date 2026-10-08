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
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $source = Get-Content -LiteralPath $path -Raw

                # The launch seam and the foreground seam are host seams, so a dispatcher can drive
                # this file without an interactive desktop.
                foreach ($required in @('ScenarioId', 'ExecutablePath', 'ContractPath', 'OwnedRoot', 'RunToken',
                                        'OutputDirectory', 'SupportPath', 'WinAppPath', 'LaunchProcess')) {
                    Assert-True ($source -cmatch ('\$' + $required + '\b')) ('the scenario must accept ' + $required)
                }

                # The second start has to run the same executable against the same canonical owned
                # root. That fact belongs to the real launch path, so it is proved by driving that path
                # with a capturing seam instead of by looking for the variable's name in this file's
                # text: the name lives in the launch path, and the scenario's own adapter only copies
                # the environment it is handed.
                # Created and cleaned up inside one try/finally lifecycle, and the cleanup goes
                # through the toolkit helper: it checks the canonical temporary boundary, the
                # tracefold-* name and every reparse point, so a cleanup can never reach outside
                # its own scratch directory. A cleanup failure fails this case visibly.
                $captureBase = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-si-capture-' + [guid]::NewGuid().ToString('N'))
                try {
                    $captureRoot = Join-Path $captureBase 'owned'
                    [void] [IO.Directory]::CreateDirectory($captureRoot)
                    $captureExe = Join-Path $captureBase 'TraceFold.Media.exe'
                    [IO.File]::WriteAllBytes($captureExe, [byte[]] @(1))
                    $script:capturedChildRoot = ''
                    $script:capturedExe = ''
                    $captureStarter = {
                        param($path, $childEnvironment)
                        $script:capturedExe = $path
                        $script:capturedChildRoot = $childEnvironment.TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT
                        [pscustomobject] @{ ProcessId = 321; StartUtcTicks = 654; ExecutablePath = $path }
                    }
                    $supportFixture = Join-Path $script:uiRoot 'tests/fixtures/fake-support.ps1'
                    $launched = Start-TraceFoldScenario -ExecutablePath $captureExe -OwnedRoot $captureRoot -RunToken 'test-token' `
                        -SupportPath $supportFixture -StartProcess $captureStarter
                    Assert-Equal ([IO.Path]::GetFullPath($captureExe)) ([IO.Path]::GetFullPath($script:capturedExe)) 'the second start must use the same executable'
                    Assert-Equal ([IO.Path]::GetFullPath($captureRoot)) ([IO.Path]::GetFullPath($script:capturedChildRoot)) 'the second start must carry the canonical owned root as its data root'
                    Assert-Equal 321 $launched.ProcessId
                    Assert-Contains (Get-Content -LiteralPath (Join-Path $captureRoot 'owned-processes.txt') -Raw) '321|654|' 'the launched process must be registered in the identity ledger'
                }
                finally {
                    Remove-UiTestTree -Path $captureBase
                }

                # Kept separate from that behaviour, and labelled as what it is: the scenario reaches
                # that path through its own adapter and hands the environment on. This is a connection
                # check on the source text, not evidence that the product was started with the root.
                Assert-True ($source -cmatch 'Start-TraceFoldScenario') 'the second start must go through the launch path'
                Assert-True ($source -cmatch 'secondaryStartBlock') 'the second start must supply its own start block'
                # The adapter hands the environment it was given to the child through this process's
                # own block, with the previous values restored right after the start: the legacy
                # Environment member does not exist on the older runtime and EnvironmentVariables
                # reads back as null on a first access there.
                Assert-True ($source -cmatch '\[Environment\]::SetEnvironmentVariable\(\$key, \[string\] \$launchEnv\[\$key\], ''Process''\)') 'the adapter must hand the environment it was given to the child'
                Assert-True ($source -cmatch 'GetEnvironmentVariable\(\$key, ''Process''\)') 'the previous value must be read so it can be restored'
                Assert-True (-not $source.Contains('$info.Environment[')) 'the legacy member that fails on the older runtime must not be used'
                # The primary and the restart start through this scenario's own launch seam with a
                # default, so they still go through Start-TraceFoldScenario while only the process
                # start itself is supplied; a dispatcher that passes its own seam replaces exactly it.
                Assert-True ($source -cmatch '\[scriptblock\] \$LaunchProcess = \{') 'the launch seam must have a default'
                Assert-True ($source -cmatch '\[Environment\]::SetEnvironmentVariable\(\$key, \[string\] \$childEnvironment\[\$key\], ''Process''\)') 'the default launch must hand the environment over through this process block'
                Assert-True ($source -cmatch 'WaitForExit') 'the second process must be waited for'
                Assert-True ($source -cmatch 'ExitCode') 'the real exit code must be read'
                # The exit decision itself is a unit, and the scenario has to go through it: the value
                # is read as a fact first and decided by the shared rule, so an unreadable exit can
                # never be read as a zero.
                Assert-True ($source -cmatch 'Assert-SingleInstanceSecondaryExit -ExitCode \$secondaryExitValue') 'the exit value must be decided by the shared unit'
                Assert-True ($source -cmatch 'secondary_exit_unknown') 'an unreadable exit must be refused by name'

                # Activation is proved by reading the foreground, never by making the window
                # foreground first; the inspect calls are observability only.
                Assert-True ($source -cmatch 'GetForegroundWindow') 'the foreground must be read'
                Assert-True ($source -cmatch 'ForegroundCheck') 'the foreground seam must be used'
                # Exactly one sample before the second start and exactly one after it: the post state
                # is read once, and the owner is then asked about that very handle. A second read
                # would allow two different windows to stand in for one fact.
                Assert-Equal 2 @($ast.FindAll(
                    { param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Get-ForegroundWindowHandle' }, $true)).Count 'the foreground must be sampled exactly twice in total'
                # The ban is on the executed action, not on the words: a comment may name the banned
                # technique while explaining why it is not used, so this reads the commands the file
                # actually invokes rather than matching the whole text.
                $invokedCommands = @($ast.FindAll(
                    { param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true) |
                    ForEach-Object { $_.GetCommandName() } |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                    Sort-Object -Unique)
                foreach ($banned in @('AppActivate', 'SetForegroundWindow', 'ShowWindowAsync')) {
                    Assert-True (-not ($invokedCommands -ccontains $banned)) ('the scenario must not invoke ' + $banned)
                }
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
            Name = 'the single instance inventory helpers keep one array boundary'
            Body = {
                # The three helpers are driven as they really are: their definitions are taken from
                # the scenario file by AST and executed here, so this is not a second copy of the
                # judgement. Only the file system they read is substituted, by pointing them at real
                # temporary directories.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $helperNames = @('Get-SingleInstanceDbInventory', 'Get-SingleInstanceDbSidecarInventory', 'Get-SingleInstanceLibraryInventory')
                $helperText = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($helperNames -ccontains $fn.Name) { $helperText.Add($fn.Extent.Text) }
                }
                Assert-Equal 3 $helperText.Count

                . ([scriptblock]::Create(($helperText -join [Environment]::NewLine)))

                # The older runtime's .NET has no IsPathFullyQualified, so the canonical path check
                # uses members both runtimes have. The standard is unchanged: the value still has to
                # be an absolute path equal to its own full path.
                $isCanonicalPath = {
                    param($Value)
                    if ($Value -isnot [string]) { return $false }
                    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
                    if (-not [IO.Path]::IsPathRooted($Value)) { return $false }
                    return ([IO.Path]::GetFullPath($Value) -ceq $Value)
                }
                # A decoded JSON count is an Int64 on one runtime and an Int32 on the other; both are
                # integers. Null, floats and strings are rejected rather than read as zero.
                $isIntegerValue = {
                    param($Value)
                    if ($null -eq $Value) { return $false }
                    return ($Value -is [int] -or $Value -is [long] -or $Value -is [int16] -or $Value -is [byte])
                }
                $asSet = {
                    param($Values)
                    return (@($Values) | Sort-Object)
                }

                # The scratch directory is created inside the same try/finally that removes it, and
                # the removal goes through the toolkit helper, so the canonical temporary boundary,
                # the tracefold-* name and the reparse point check all apply to this cleanup too.
                $sandbox = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-si-inventory-' + [guid]::NewGuid().ToString('N'))
                try {
                    [void] [IO.Directory]::CreateDirectory($sandbox)
                    # Each helper has its own filter, so every name written here is chosen against that
                    # filter: the files added later must match it, otherwise the helper would rightly
                    # not report them. Non-matching controls are written only in the one and many
                    # cases; the empty case stays genuinely empty.
                    $layouts = @(
                        @{ Name = 'Get-SingleInstanceDbInventory';
                           InLibrary = $false;
                           One = @('a.db');
                           Many = @('a.db', 'b.db', 'c.db');
                           Added = @('added.db', 'added2.db');
                           Other = @('note.txt') },
                        @{ Name = 'Get-SingleInstanceDbSidecarInventory';
                           InLibrary = $false;
                           One = @('a.db-wal');
                           Many = @('a.db-wal', 'b.db-shm', 'c.db-wal');
                           Added = @('added.db-wal', 'added2.db-shm');
                           Other = @('note.txt') },
                        @{ Name = 'Get-SingleInstanceLibraryInventory';
                           InLibrary = $true;
                           One = @('asset.bin');
                           Many = @('asset.bin', 'asset2.bin', 'asset3.bin');
                           Added = @('added.bin', 'added2.bin');
                           Other = @() }
                    )

                    foreach ($layout in $layouts) {
                        $name = [string] $layout.Name
                        $inLibrary = [bool] $layout.InLibrary

                        foreach ($case in @('missing', 'empty', 'one', 'many')) {
                            $caseRoot = Join-Path $sandbox ($name + '-' + $case)
                            $directory = $caseRoot
                            if ($inLibrary) { $directory = Join-Path $caseRoot 'library' }

                            $expected = New-Object System.Collections.Generic.List[string]
                            $expectedCount = 0
                            if ($case -ceq 'one') { $expectedCount = 1 }
                            elseif ($case -ceq 'many') { $expectedCount = @($layout.Many).Count }

                            # Only the missing case leaves the directory absent; every other case
                            # creates it, and the empty case writes nothing into it at all.
                            if ($case -cne 'missing') {
                                [void] [IO.Directory]::CreateDirectory($directory)
                            }
                            if ($case -ceq 'one' -or $case -ceq 'many') {
                                $matching = @($layout.One)
                                if ($case -ceq 'many') { $matching = @($layout.Many) }
                                foreach ($fileName in $matching) {
                                    $full = Join-Path $directory $fileName
                                    [IO.File]::WriteAllText($full, 'x')
                                    $expected.Add([IO.Path]::GetFullPath($full))
                                }
                                foreach ($other in @($layout.Other)) {
                                    [IO.File]::WriteAllText((Join-Path $directory $other), 'x')
                                }
                            }

                            # The fixture is checked against its own claim before the helper is called.
                            Assert-Equal $expectedCount $expected.Count ($name + '/' + $case + ' fixture must create ' + $expectedCount + ' matching file(s)')

                            $actual = @(& $name -Root $caseRoot)
                            Assert-Equal $expectedCount $actual.Count ($name + '/' + $case + ' count')
                            foreach ($item in $actual) {
                                Assert-True (& $isCanonicalPath $item) ($name + '/' + $case + ' must yield canonical absolute path strings')
                            }
                            Assert-Equal 0 @($actual | Where-Object { $_ -isnot [string] }).Count ($name + '/' + $case + ' must not nest arrays')
                            Assert-Equal ((& $asSet $expected) -join '|') ((& $asSet $actual) -join '|') ($name + '/' + $case + ' exact expected path set')

                            if ($case -ceq 'one' -or $case -ceq 'many') {
                                # Differences come from real calls of the helper before and after a real
                                # file is written, never from a hand-built array.
                                $firstAdded = Join-Path $directory ([string] $layout.Added[0])
                                [IO.File]::WriteAllText($firstAdded, 'x')
                                $after = @(& $name -Root $caseRoot)
                                Assert-Equal ($expectedCount + 1) $after.Count ($name + '/' + $case + ' must see the first added file')
                                $addedOne = @(& $asSet $after | Where-Object { (& $asSet $actual) -cnotcontains $_ })
                                Assert-Equal 1 $addedOne.Count ($name + '/' + $case + ' first difference adds exactly one path')
                                Assert-True (& $isCanonicalPath $addedOne[0]) ($name + '/' + $case + ' first difference must be a real canonical path')
                                Assert-Equal ([IO.Path]::GetFullPath($firstAdded)) $addedOne[0] ($name + '/' + $case + ' first difference must be the file that was created')
                                Assert-Equal 0 @(& $asSet $actual | Where-Object { (& $asSet $after) -cnotcontains $_ }).Count ($name + '/' + $case + ' first difference must not lose a path')

                                # No change between two real calls adds and loses nothing.
                                $again = @(& $name -Root $caseRoot)
                                Assert-Equal 0 @(& $asSet $again | Where-Object { (& $asSet $after) -cnotcontains $_ }).Count ($name + '/' + $case + ' no change adds nothing')
                                Assert-Equal 0 @(& $asSet $after | Where-Object { (& $asSet $again) -cnotcontains $_ }).Count ($name + '/' + $case + ' no change loses nothing')

                                # A second real file: the difference against the original inventory has
                                # to be exactly those two created paths, and nothing may disappear.
                                $secondAdded = Join-Path $directory ([string] $layout.Added[1])
                                [IO.File]::WriteAllText($secondAdded, 'x')
                                $grown = @(& $name -Root $caseRoot)
                                Assert-Equal ($expectedCount + 2) $grown.Count ($name + '/' + $case + ' must see both added files')
                                $addedTwo = @(& $asSet $grown | Where-Object { (& $asSet $actual) -cnotcontains $_ })
                                Assert-Equal 2 $addedTwo.Count ($name + '/' + $case + ' difference from the original inventory must be exactly two paths')
                                foreach ($one in $addedTwo) {
                                    Assert-True (& $isCanonicalPath $one) ($name + '/' + $case + ' the two-path difference must contain real canonical paths')
                                }
                                Assert-Equal ([IO.Path]::GetFullPath($firstAdded)) (& $asSet $addedTwo)[0] ($name + '/' + $case + ' the two-path difference must start with the first created file')
                                Assert-Equal ([IO.Path]::GetFullPath($secondAdded)) (& $asSet $addedTwo)[1] ($name + '/' + $case + ' the two-path difference must end with the second created file')
                                Assert-Equal 0 @(& $asSet $actual | Where-Object { (& $asSet $grown) -cnotcontains $_ }).Count ($name + '/' + $case + ' the grown inventory must not lose a path')
                                Assert-Equal (@(& $asSet $actual) -join '|') (@(@(& $asSet $grown) | Where-Object { (& $asSet $addedTwo) -cnotcontains $_ }) -join '|') ($name + '/' + $case + ' original plus difference must equal the grown set')
                                Assert-Equal 1 @(& $asSet $grown | Where-Object { (& $asSet $after) -cnotcontains $_ }).Count ($name + '/' + $case + ' difference from the first addition must be exactly one path')
                            }

                            # The evidence object must round trip with the same shape and numbers.
                            $json = ConvertTo-Json -InputObject ([ordered] @{ paths = @($actual); count = $actual.Count }) -Depth 6
                            $back = $json | ConvertFrom-Json
                            Assert-Equal $actual.Count @($back.paths).Count ($name + '/' + $case + ' JSON round trip keeps the count')
                            Assert-Equal 0 @(@($back.paths) | Where-Object { $_ -isnot [string] }).Count ($name + '/' + $case + ' JSON round trip must not nest')
                            Assert-True (& $isIntegerValue $back.count) ($name + '/' + $case + ' JSON round trip keeps an integer count')
                            Assert-True ($back.count -eq $actual.Count) ($name + '/' + $case + ' JSON round trip count equals numerically')
                            if ($expectedCount -eq 0) {
                                Assert-Equal 0 @($back.paths).Count ($name + '/' + $case + ' empty JSON round trip stays empty')
                            }
                        }
                    }
                }
                finally {
                    Remove-UiTestTree -Path $sandbox
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
        },
        @{
            Name = 'the single instance pre state and activation decisions refuse each counter-example'
            Body = {
                # The two decisions are driven as they really are: their definitions are lifted out of
                # the scenario by AST and executed here, never re-implemented. Only the facts are
                # injected, so an outcome that should be refused can be shown to be refused without a
                # live desktop, and the code it is refused with is asserted exactly.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Assert-SingleInstancePreState', 'Assert-SingleInstanceActivated')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 2 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                # The code a call was refused with, or 'none' when it was accepted.
                function Get-RefusalCode {
                    param([scriptblock] $Body)
                    try { & $Body } catch { return [string] $_.Exception.Message }
                    return 'none'
                }

                # A consistent set of facts is accepted by both decisions. This is the positive case:
                # the control window is the foreground, the primary is not, and afterwards the primary
                # is the foreground with its own window and its own owner.
                $accepted = Get-RefusalCode { Assert-SingleInstancePreState -PrimaryPid 700 -ControlHandle 55 -ControlOwnerPid 900 -PreForegroundHandle 55 -PreForegroundOwnerPid 900 }
                Assert-Equal 'none' $accepted 'a controlled non foreground state must be accepted'
                $acceptedActivation = Get-RefusalCode { Assert-SingleInstanceActivated -PrimaryPid 700 -PrimaryWindowHandle 11 -PrimaryWindowAfterHandle 11 -ForegroundHandle 11 -ForegroundOwnerPid 700 }
                Assert-Equal 'none' $acceptedActivation 'a matched single sample must be accepted'

                # Counter-example: the pre state was never established, because the foreground is some
                # other window rather than the control window the scenario owns.
                Assert-Equal 'single.instance.pre_state_not_controlled' (Get-RefusalCode {
                        Assert-SingleInstancePreState -PrimaryPid 700 -ControlHandle 55 -ControlOwnerPid 900 -PreForegroundHandle 66 -PreForegroundOwnerPid 900
                    }) 'a foreground that is not the control window must be refused'

                # Counter-example: the control window is in the foreground but is owned by something
                # else, so the ownership of the controlled object was never verified.
                Assert-Equal 'single.instance.pre_state_owner_mismatch' (Get-RefusalCode {
                        Assert-SingleInstancePreState -PrimaryPid 700 -ControlHandle 55 -ControlOwnerPid 900 -PreForegroundHandle 55 -PreForegroundOwnerPid 800
                    }) 'a control window owned by another process must be refused'

                # Counter-example: another window of the primary's own process is in the foreground, so
                # the state that was supposed to be established before the second start never was.
                Assert-Equal 'single.instance.pre_state_still_foreground' (Get-RefusalCode {
                        Assert-SingleInstancePreState -PrimaryPid 700 -ControlHandle 55 -ControlOwnerPid 700 -PreForegroundHandle 55 -PreForegroundOwnerPid 700
                    }) 'the primary being the pre state foreground must be refused'

                # Counter-example: after the second start the foreground is another window of the same
                # process, which is not the primary window even though the owner matches.
                Assert-Equal 'single.instance.foreground_window_not_primary' (Get-RefusalCode {
                        Assert-SingleInstanceActivated -PrimaryPid 700 -PrimaryWindowHandle 11 -PrimaryWindowAfterHandle 11 -ForegroundHandle 22 -ForegroundOwnerPid 700
                    }) 'another window of the same process must not count as activation'

                # Counter-example: the foreground is not activated at all: the handle is the primary's
                # window but the process behind that handle is not the primary.
                Assert-Equal 'single.instance.primary_not_activated' (Get-RefusalCode {
                        Assert-SingleInstanceActivated -PrimaryPid 700 -PrimaryWindowHandle 11 -PrimaryWindowAfterHandle 11 -ForegroundHandle 11 -ForegroundOwnerPid 800
                    }) 'a foreground owned by another process must not count as activation'

                # Counter-example: the primary window could not be read after the second start.
                Assert-Equal 'single.instance.primary_window_missing' (Get-RefusalCode {
                        Assert-SingleInstanceActivated -PrimaryPid 700 -PrimaryWindowHandle 11 -PrimaryWindowAfterHandle 0 -ForegroundHandle 11 -ForegroundOwnerPid 700
                    }) 'a missing primary window must be refused'
            }
        },
        @{
            Name = 'the single instance control window release is driveable and a factory failure is rethrown unchanged'
            Body = {
                # The release unit and the factory are driven as they really are, lifted out of the
                # scenario by AST. The release is exercised on a real disposable object and on stub
                # windows that refuse to be released in different ways, and the factory is driven
                # through its injected steps, so the failure paths are covered without a live
                # desktop. The stub windows declare every property the factory writes, because the
                # scenario runs under strict mode and an undeclared write would fail there.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $source = Get-Content -LiteralPath $path -Raw
                $wanted = @('Complete-SingleInstanceControlRelease', 'New-NonForegroundControlWindow', 'New-SingleInstanceReleaseAttempt')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 3 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                if (-not ('System.Windows.Forms.Form' -as [type])) { Add-Type -AssemblyName System.Windows.Forms }

                # Normal release: a real window object, built directly so that no window has to be
                # shown for this case, released and read back as disposed.
                $form = New-Object System.Windows.Forms.Form
                $normal = Complete-SingleInstanceControlRelease -ControlWindow $form
                Assert-Equal 'True' ([string] $normal.closed) 'a real window must be released'
                Assert-Equal 'disposed' ([string] $normal.releaseReadBack) 'the released fact must be read back'
                Assert-Equal '' ([string] $normal.releaseFailure) 'a clean release must record no failure'
                Assert-Equal 'True' ([string] $normal.isDisposedReadBack) 'the read back must be the disposed state'
                Assert-Equal '' ([string] $normal.actionError) 'a clean release must record no action error'
                Assert-Equal '' ([string] $normal.closeError) 'a clean release must record no close error'
                Assert-Equal '' ([string] $normal.disposeError) 'a clean release must record no dispose error'
                Assert-Equal '' ([string] $normal.readBackError) 'a clean release must record no read back error'
                Assert-True ($null -eq $normal.actionErrorRecord) 'a clean release must carry no action error record'

                # A Close that throws must not skip the Dispose that can still release the object: the
                # dispose counter is the proof that the second call really happened.
                $script:probeDisposeCalls = 0
                $throwing = New-Object psobject -Property @{ IsDisposed = $false }
                $throwing | Add-Member -MemberType ScriptMethod -Name Close -Value { throw 'stub close failed' }
                $throwing | Add-Member -MemberType ScriptMethod -Name Dispose -Value { $script:probeDisposeCalls = $script:probeDisposeCalls + 1 }
                $failed = Complete-SingleInstanceControlRelease -ControlWindow $throwing
                Assert-Equal 1 $script:probeDisposeCalls 'Dispose must still be attempted after Close threw'
                Assert-Equal 'False' ([string] $failed.closed) 'a throwing release must not count as released'
                Assert-Equal 'not-disposed' ([string] $failed.releaseReadBack) 'the read back still runs after an action error and reports the real state'
                Assert-True ($failed.closeError -clike 'single.instance.control_window_not_closed*') 'the close error must be named'
                Assert-Equal '' ([string] $failed.disposeError) 'a dispose that did not fail must record no error'
                Assert-True ($failed.releaseFailure -clike 'single.instance.control_window_not_closed*') 'the failure must carry the release code'
                Assert-Equal '' ([string] $failed.readBackError) 'the read back itself did not fail'
                Assert-True ($null -ne $failed.actionErrorRecord) 'the action error record must be kept for the facts'

                # A Dispose that throws is its own action error: the close error stays empty and the
                # standing failure is the dispose.
                $disposeThrowing = New-Object psobject -Property @{ IsDisposed = $false }
                $disposeThrowing | Add-Member -MemberType ScriptMethod -Name Close -Value { }
                $disposeThrowing | Add-Member -MemberType ScriptMethod -Name Dispose -Value { throw 'stub dispose failed' }
                $disposeFailed = Complete-SingleInstanceControlRelease -ControlWindow $disposeThrowing
                Assert-Equal 'False' ([string] $disposeFailed.closed) 'a throwing dispose must not count as released'
                Assert-Equal '' ([string] $disposeFailed.closeError) 'a close that did not fail must record no error'
                Assert-True ($disposeFailed.disposeError -clike 'single.instance.control_window_not_disposed*') 'the dispose error must be named'
                Assert-True ($disposeFailed.releaseFailure -clike 'single.instance.control_window_not_disposed*') 'the standing failure is the dispose error'
                Assert-True ($null -ne $disposeFailed.disposeErrorRecord) 'the dispose error record must be kept for the facts'

                # An object that claims to be disposed can never mask a failed action: released means
                # all of the calls did not fail and the object really is disposed.
                $disposedThrowing = New-Object psobject -Property @{ IsDisposed = $true }
                $disposedThrowing | Add-Member -MemberType ScriptMethod -Name Close -Value { throw 'stub close failed' }
                $disposedThrowing | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                $masked = Complete-SingleInstanceControlRelease -ControlWindow $disposedThrowing
                Assert-Equal 'False' ([string] $masked.closed) 'a disposed object must not turn a failed action into a release'
                Assert-Equal 'disposed' ([string] $masked.releaseReadBack) 'the read back reports the disposed state as its own fact'
                Assert-True ($masked.closeError -clike 'single.instance.control_window_not_closed*') 'the close error stays on record'
                Assert-True ($masked.releaseFailure -clike 'single.instance.control_window_not_closed*') 'the standing failure remains the action error'

                # A read back that cannot answer is its own failure: the stub has no IsDisposed member
                # at all and the scenario runs under strict mode, so that read is a real error.
                $noRead = [pscustomobject] @{ Note = 'x' }
                $noRead | Add-Member -MemberType ScriptMethod -Name Close -Value { }
                $noRead | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                $unreadable = Complete-SingleInstanceControlRelease -ControlWindow $noRead
                Assert-Equal 'False' ([string] $unreadable.closed) 'an unreadable release must not count as released'
                Assert-Equal 'unknown' ([string] $unreadable.releaseReadBack) 'an unreadable read back stays unknown'
                Assert-True ($unreadable.readBackError -clike 'single.instance.control_window_read_back_failed*') 'the read back error must be named'
                Assert-Equal 'single.instance.control_window_read_back_failed: PropertyNotFoundException' ([string] $unreadable.releaseFailure) 'an unreadable object is refused by the read back error'
                Assert-True ($null -ne $unreadable.readBackErrorRecord) 'the read back error record must be kept for the facts'

                # Both guards failing at once: the action error and the read back error stay beside
                # each other, and the standing failure is the first cause - the action.
                $both = [pscustomobject] @{ Note = 'x' }
                $both | Add-Member -MemberType ScriptMethod -Name Close -Value { throw 'stub close failed' }
                $both | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                $bothFailed = Complete-SingleInstanceControlRelease -ControlWindow $both
                Assert-Equal 'False' ([string] $bothFailed.closed) 'two failures cannot add up to a release'
                Assert-True ($bothFailed.actionError -clike 'single.instance.control_window_not_closed*') 'the action error must be kept'
                Assert-True ($bothFailed.closeError -clike 'single.instance.control_window_not_closed*') 'the close error is named on its own'
                Assert-Equal '' ([string] $bothFailed.disposeError) 'the dispose did not fail and stays empty'
                Assert-True ($bothFailed.readBackError -clike 'single.instance.control_window_read_back_failed*') 'the read back error must be kept beside it'
                Assert-True ($bothFailed.releaseFailure -clike 'single.instance.control_window_not_closed*') 'the standing failure is the first cause'
                Assert-True ($null -ne $bothFailed.actionErrorRecord) 'the action record survives a later read back failure'
                Assert-True ($null -ne $bothFailed.readBackErrorRecord) 'the read back record is kept too'

                # Release doing nothing: the calls do not throw but the object is not disposed, which is
                # the case that used to be mistaken for a successful release.
                $silent = New-Object psobject -Property @{ IsDisposed = $false }
                $silent | Add-Member -MemberType ScriptMethod -Name Close -Value { }
                $silent | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                $notReleased = Complete-SingleInstanceControlRelease -ControlWindow $silent
                Assert-Equal 'False' ([string] $notReleased.closed) 'an object that is still not disposed must not count as released'
                Assert-Equal 'single.instance.control_window_not_released' ([string] $notReleased.releaseFailure) 'the silent case must be refused by name'

                # The factory creating nothing: the injected step throws before an object exists, so
                # the factory must rethrow that very failure, and its report must say honestly that
                # no object was obtained and nothing was released.
                $factoryMessage = ''
                $factoryReport = $null
                try {
                    $null = New-NonForegroundControlWindow -CreateForm { throw 'single.instance.control_window_show_failed' }
                }
                catch {
                    $factoryMessage = [string] $_.Exception.Message
                    $factoryReport = $_.Exception.Data['TraceFold.ControlWindowRelease']
                }
                Assert-Equal 'single.instance.control_window_show_failed' $factoryMessage 'a construction failure must be rethrown unchanged'
                Assert-Equal 'False' ([string] $factoryReport.objectObtained) 'a failed construction must be recorded as no object obtained'
                Assert-Equal 'False' ([string] $factoryReport.releaseAttempted) 'a failed construction must not claim a release'
                Assert-True ($null -eq $factoryReport.attempt) 'a failed construction has no release attempt to report'

                # The factory failing after an object exists: the object goes through the shared
                # release unit, and the release facts travel with the original failure instead of
                # being lost. The default creation step builds a real disposable object.
                $afterMessage = ''
                $afterReport = $null
                try { $null = New-NonForegroundControlWindow -ShowAction { throw 'single.instance.control_window_activate_failed' } }
                catch {
                    $afterMessage = [string] $_.Exception.Message
                    $afterReport = $_.Exception.Data['TraceFold.ControlWindowRelease']
                }
                Assert-Equal 'single.instance.control_window_activate_failed' $afterMessage 'a step failure after the object exists must still be rethrown unchanged'
                Assert-Equal 'True' ([string] $afterReport.objectObtained) 'the obtained object must be recorded as such'
                Assert-Equal 'True' ([string] $afterReport.releaseAttempted) 'the object must go through the shared release unit'
                Assert-Equal 'factory' ([string] $afterReport.attempt.source) 'the attempt must say who released the object'
                Assert-Equal 'True' ([string] $afterReport.attempt.closed) 'the real object must really be released'

                # Release failing inside the factory: the original failure and the release failure are
                # both kept - the rethrow stays the original error, and the report carries the release
                # failure as its own fact.
                $stub = [pscustomobject] @{
                    Text = ''; Width = 0; Height = 0; StartPosition = $null; Location = $null
                    ShowInTaskbar = $true; IsDisposed = $false
                }
                $stub | Add-Member -MemberType ScriptMethod -Name Close -Value { throw 'stub close failed' }
                $stub | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                $bothMessage = ''
                $bothReport = $null
                try {
                    $null = New-NonForegroundControlWindow -CreateForm { $stub } -ShowAction { throw 'single.instance.control_window_activate_failed' }
                }
                catch {
                    $bothMessage = [string] $_.Exception.Message
                    $bothReport = $_.Exception.Data['TraceFold.ControlWindowRelease']
                }
                Assert-Equal 'single.instance.control_window_activate_failed' $bothMessage 'the original step failure must survive the release failure'
                Assert-Equal 'True' ([string] $bothReport.objectObtained) 'the object existed'
                Assert-Equal 'False' ([string] $bothReport.attempt.closed) 'the failed release must not be reported as closed'
                Assert-True ($bothReport.attempt.releaseFailure -clike 'single.instance.control_window_not_closed*') 'the release failure must be kept beside the original error'

                # A confirmed release is never repeated: the safety net unit only takes an object that
                # was not already confirmed released, and the scenario calls it with exactly that
                # state. Its behaviour is driven by its own case; this is the connection.
                Assert-True ($source -cmatch 'if \(\$null -eq \$ControlWindow -or \$AlreadyClosed\) \{ return \$outcome \}') 'the safety net must skip a confirmed release and a missing object'
                Assert-True ($source -cmatch '\$safety = Invoke-SingleInstanceSafetyNet -ControlWindow \$controlWindow -AlreadyClosed \$controlWindowClosed') 'the scenario must pass its own confirmed state to the safety net'
            }
        },
        @{
            Name = 'the failure fact conversion has a last resort that cannot throw and never leaks'
            Body = {
                # The safe wrapper and the fallback are driven as they really are. The conversion is
                # its own seam, so one conversion failure can be injected: even then the wrapper must
                # answer with a redacted fallback instead of throwing a new error or recursing into
                # the converter that just failed.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('ConvertTo-SingleInstanceErrorFact', 'New-SingleInstanceFallbackErrorFact', 'New-SingleInstanceErrorFactSafe')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 3 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                $record = $null
                try { throw 'single.instance.safe_fact_probe' } catch { $record = $_ }
                # The default path is the real converter.
                $fact = New-SingleInstanceErrorFactSafe -ErrorRecord $record -Stage 'probe-stage'
                Assert-Equal 'probe-stage' ([string] $fact.stage)
                Assert-True ($fact.message -like '*safe_fact_probe*') 'the default path must use the real converter'
                Assert-Equal 'System.Management.Automation.RuntimeException' ([string] $fact.errorType)

                # The injected conversion is driven once and fails: the wrapper still answers.
                $token = 'probe-token-4321'
                $root = 'C:\temp\tracefold-safe-probe'
                $leaky = $null
                try { throw ('single.instance.safe_fact_leak at ' + $root + ' with ' + $token) } catch { $leaky = $_ }
                $fallback = New-SingleInstanceErrorFactSafe -ErrorRecord $leaky -Stage 'probe-stage-2' -RunToken $token -OwnedRoot $root `
                    -Convert { param($errorRecord, $stage, $runToken, $ownedRoot) throw 'converter exploded' }
                Assert-Equal 'probe-stage-2' ([string] $fallback.stage) 'the fallback keeps the stage it was asked about'
                Assert-Equal 'unavailable' ([string] $fallback.errorType) 'the fallback uses static fields only'
                Assert-Equal 'fact-conversion-failed' ([string] $fallback.errorId)
                Assert-True ($fallback.message -like '*fact-conversion-failed*') 'the fallback announces the conversion failure'
                Assert-True ($fallback.message -notlike ('*' + $token + '*')) 'the fallback message must be redacted'
                Assert-True ($fallback.message -notlike ('*' + $root + '*')) 'the fallback message must be redacted'

                # The injected conversion is its own seam in the working direction too.
                $seamFact = New-SingleInstanceErrorFactSafe -ErrorRecord $record -Stage 'probe-stage-3' `
                    -Convert { param($errorRecord, $stage, $runToken, $ownedRoot) [pscustomobject] @{ stage = $stage; errorType = 'seam'; errorId = ''; category = ''; position = ''; stack = ''; message = '' } }
                Assert-Equal 'seam' ([string] $seamFact.errorType) 'the seam is used when it is supplied'
            }
        },
        @{
            Name = 'the factory hand over is read through its own unit and carries the confirmed fact'
            Body = {
                # The hand over a failing factory leaves beside its original error is read by the unit
                # the scenario itself calls: the attempt joins the list, a confirmed release is
                # reported as confirmed, a record without a hand over changes nothing, and a hand
                # over that cannot be read becomes its own fact instead of an exception.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Complete-SingleInstanceControlRelease', 'New-SingleInstanceReleaseAttempt',
                    'ConvertTo-SingleInstanceErrorFact', 'New-SingleInstanceFallbackErrorFact',
                    'New-SingleInstanceErrorFactSafe', 'Import-SingleInstanceFactoryReleaseReport',
                    'New-NonForegroundControlWindow')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 7 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                if (-not ('System.Windows.Forms.Form' -as [type])) { Add-Type -AssemblyName System.Windows.Forms }

                function New-HandoverProbeRecord {
                    param([string] $Message)
                    try { throw $Message } catch { return $_ }
                }

                # A record with no hand over at all: nothing joins the list and nothing is recorded.
                $plainAttempts = New-Object System.Collections.Generic.List[object]
                $plainFailures = New-Object System.Collections.Generic.List[object]
                $plain = New-HandoverProbeRecord 'single.instance.plain_probe_failure'
                $plainReceived = Import-SingleInstanceFactoryReleaseReport -ErrorRecord $plain -ReleaseAttempts $plainAttempts -Failures $plainFailures
                Assert-True ($null -eq $plainReceived.report) 'a record without a hand over reports nothing'
                Assert-True ($null -eq $plainReceived.attempt)
                Assert-Equal 0 $plainAttempts.Count
                Assert-Equal 0 $plainFailures.Count

                # A real factory failure whose release failed: the attempt joins the list with its
                # own failure, and the hand over is reported as it is.
                $stub = [pscustomobject] @{
                    Text = ''; Width = 0; Height = 0; StartPosition = $null; Location = $null
                    ShowInTaskbar = $true; IsDisposed = $false
                }
                $stub | Add-Member -MemberType ScriptMethod -Name Close -Value { throw 'stub close failed' }
                $stub | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                $failedRecord = $null
                try { $null = New-NonForegroundControlWindow -CreateForm { $stub } -ShowAction { throw 'single.instance.factory_probe_failure' } } catch { $failedRecord = $_ }
                $failedAttempts = New-Object System.Collections.Generic.List[object]
                $failedFailures = New-Object System.Collections.Generic.List[object]
                $failedReceived = Import-SingleInstanceFactoryReleaseReport -ErrorRecord $failedRecord -ReleaseAttempts $failedAttempts -Failures $failedFailures
                Assert-True ($null -ne $failedReceived.report) 'the hand over must be reported'
                Assert-True ($null -ne $failedReceived.attempt)
                Assert-Equal 1 $failedAttempts.Count
                Assert-Equal 'factory' ([string] $failedAttempts[0].source)
                Assert-Equal 'False' ([string] $failedAttempts[0].closed)
                Assert-True ([string] $failedAttempts[0].releaseFailure -clike 'single.instance.control_window_not_closed*')
                Assert-Equal 0 $failedFailures.Count 'a readable hand over records no failure'

                # A real factory failure whose release succeeded: the attempt is confirmed and that
                # confirmation travels with it.
                $releasedRecord = $null
                try { $null = New-NonForegroundControlWindow -ShowAction { throw 'single.instance.factory_released_probe' } } catch { $releasedRecord = $_ }
                $releasedAttempts = New-Object System.Collections.Generic.List[object]
                $releasedFailures = New-Object System.Collections.Generic.List[object]
                $releasedReceived = Import-SingleInstanceFactoryReleaseReport -ErrorRecord $releasedRecord -ReleaseAttempts $releasedAttempts -Failures $releasedFailures
                Assert-True ($null -ne $releasedReceived.attempt)
                Assert-Equal 'True' ([string] $releasedReceived.attempt.closed) 'a release the factory confirmed is confirmed'
                Assert-Equal '' ([string] $releasedReceived.attempt.releaseFailure)

                # A hand over that cannot be read: recorded as its own fact, never thrown, nothing
                # joins the attempt list.
                $badAttempts = New-Object System.Collections.Generic.List[object]
                $badFailures = New-Object System.Collections.Generic.List[object]
                $badRecord = New-HandoverProbeRecord 'single.instance.bad_handover_probe'
                $badRecord.Exception.Data['TraceFold.ControlWindowRelease'] = 'not-a-report'
                $badReceived = Import-SingleInstanceFactoryReleaseReport -ErrorRecord $badRecord -ReleaseAttempts $badAttempts -Failures $badFailures
                Assert-True ($null -eq $badReceived.report) 'an unreadable hand over reports nothing'
                Assert-True ($null -eq $badReceived.attempt)
                Assert-Equal 0 $badAttempts.Count
                Assert-Equal 1 $badFailures.Count 'the unreadable hand over must be recorded'
                Assert-Equal 'factory-release-report-read' ([string] $badFailures[0].stage)
            }
        },
        @{
            Name = 'the safety net releases only an unconfirmed object and never throws'
            Body = {
                # The safety net is driven as the scenario runs it: a missing object does nothing, a
                # confirmed release is never repeated (the close counter proves the object is not
                # touched at all), an open object goes through the shared release unit and records
                # its attempt, and a release that fails records the failure without throwing.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Complete-SingleInstanceControlRelease', 'New-SingleInstanceReleaseAttempt',
                    'ConvertTo-SingleInstanceErrorFact', 'New-SingleInstanceFallbackErrorFact',
                    'New-SingleInstanceErrorFactSafe', 'Invoke-SingleInstanceSafetyNet')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 6 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                if (-not ('System.Windows.Forms.Form' -as [type])) { Add-Type -AssemblyName System.Windows.Forms }

                $attempts = New-Object System.Collections.Generic.List[object]
                $failures = New-Object System.Collections.Generic.List[object]

                # Nothing to release.
                $none = Invoke-SingleInstanceSafetyNet -ControlWindow $null -AlreadyClosed $false -ReleaseAttempts $attempts -Failures $failures
                Assert-Equal 'False' ([string] $none.attempted)
                Assert-Equal 'False' ([string] $none.closed)
                Assert-Equal 0 $attempts.Count
                Assert-Equal 0 $failures.Count

                # A confirmed release is never repeated: the object would count a Close if it were.
                $script:safetyCloseCalls = 0
                $counted = [pscustomobject] @{ IsDisposed = $true }
                $counted | Add-Member -MemberType ScriptMethod -Name Close -Value { $script:safetyCloseCalls = $script:safetyCloseCalls + 1 }
                $counted | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                $repeat = Invoke-SingleInstanceSafetyNet -ControlWindow $counted -AlreadyClosed $true -ReleaseAttempts $attempts -Failures $failures
                Assert-Equal 'False' ([string] $repeat.attempted) 'a confirmed release must not be repeated'
                Assert-Equal 0 $script:safetyCloseCalls 'the object must not be touched at all'
                Assert-Equal 0 $attempts.Count

                # An open object: released through the shared unit and its attempt is recorded.
                $openForm = New-Object System.Windows.Forms.Form
                $released = Invoke-SingleInstanceSafetyNet -ControlWindow $openForm -AlreadyClosed $false -ReleaseAttempts $attempts -Failures $failures
                Assert-Equal 'True' ([string] $released.attempted)
                Assert-Equal 'True' ([string] $released.closed)
                Assert-Equal 'disposed' ([string] $released.readBack)
                Assert-Equal '' ([string] $released.failure)
                Assert-Equal 1 $attempts.Count
                Assert-Equal 'safety-net' ([string] $attempts[0].source)
                Assert-Equal 'release-safety-net' ([string] $attempts[0].stage)
                Assert-Equal 0 $failures.Count

                # A release that fails: recorded, not thrown, and the object stays unconfirmed.
                $silent = New-Object psobject -Property @{ IsDisposed = $false }
                $silent | Add-Member -MemberType ScriptMethod -Name Close -Value { }
                $silent | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                $failed = Invoke-SingleInstanceSafetyNet -ControlWindow $silent -AlreadyClosed $false -ReleaseAttempts $attempts -Failures $failures
                Assert-Equal 'True' ([string] $failed.attempted)
                Assert-Equal 'False' ([string] $failed.closed)
                Assert-Equal 'single.instance.control_window_not_released' ([string] $failed.failure)
                Assert-Equal 2 $attempts.Count
                Assert-Equal 0 $failures.Count 'a release that was attempted and failed is an attempt, not a guard failure'
            }
        },
        @{
            Name = 'the single instance error facts stay lightweight scalars and are redacted'
            Body = {
                # The fact converter is driven as it really is, lifted by AST. It is the only path
                # from an ErrorRecord to persisted evidence: scalars only, redacted, and never the
                # record itself - no CLIXML and no session state can pass through it.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('ConvertTo-SingleInstanceErrorFact')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 1 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                $token = 'probe-token-1234'
                $root = Join-Path ([IO.Path]::GetTempPath()) 'tracefold-probe-root'
                $record = $null
                try { throw ('single.instance.probe_failure at ' + $root + ' with ' + $token) } catch { $record = $_ }
                $fact = ConvertTo-SingleInstanceErrorFact -ErrorRecord $record -Stage 'probe-stage' -RunToken $token -OwnedRoot $root
                Assert-Equal 'probe-stage' ([string] $fact.stage)
                Assert-Equal 'System.Management.Automation.RuntimeException' ([string] $fact.errorType)
                Assert-True (-not [string]::IsNullOrWhiteSpace([string] $fact.errorId)) 'the error id must be retained'
                Assert-True (-not [string]::IsNullOrWhiteSpace([string] $fact.category)) 'the category must be retained'
                Assert-True (-not [string]::IsNullOrWhiteSpace([string] $fact.position)) 'the position must be retained'
                Assert-True (-not [string]::IsNullOrWhiteSpace([string] $fact.stack)) 'the stack must be retained'
                Assert-True ($fact.message -like '*single.instance.probe_failure*') 'the message must be retained'
                Assert-True ($fact.message -notlike ('*' + $token + '*')) 'the token must be redacted'
                Assert-True ($fact.message -notlike ('*' + $root + '*')) 'the owned root must be redacted'
                # The document shape is pinned: exactly these seven string fields, nothing nested and
                # nothing that would need a deep serialiser to read back.
                $json = ConvertTo-Json -InputObject $fact -Depth 6 | ConvertFrom-Json
                $names = @($json.PSObject.Properties.Name | Sort-Object)
                Assert-Equal 'category,errorId,errorType,message,position,stack,stage' ($names -join ',') 'the fact must carry exactly the lightweight scalar fields'
                foreach ($name in $names) { Assert-True ($json.$name -is [string]) ($name + ' must stay a string') }
            }
        },
        @{
            Name = 'the foreground probe initialisation refuses a check with no effect and keeps a real failure'
            Body = {
                # The initialisation is driven through the production connection and through its two
                # seams. It produces no evidence of its own, and the refusal is driven with an
                # injected lookup so the type a previous case may have created can never make the
                # negative pass.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Initialize-SingleInstanceForegroundProbe')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 1 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                function Get-ProbeRefusal {
                    param([scriptblock] $Body)
                    try { & $Body } catch { return [string] $_.Exception.Message }
                    return 'none'
                }

                # Positive with the injected check: it really runs and is handed the process id, and
                # the initialisation emits nothing of its own.
                $script:probeCheckPid = 0
                $emitted = @(Initialize-SingleInstanceForegroundProbe -ProcessId 4242 -ForegroundCheck { param($id) $script:probeCheckPid = $id } -ResolveProbeType { [pscustomobject] @{ ready = $true } })
                Assert-Equal 4242 $script:probeCheckPid 'the check must be driven with the process id'
                Assert-Equal 0 $emitted.Count 'the initialisation must produce no evidence of its own'

                # Positive through the production connection: the driver check really runs and the
                # default lookup then finds the probe type ready.
                Assert-Equal 'none' (Get-ProbeRefusal { Initialize-SingleInstanceForegroundProbe -ProcessId $PID -ForegroundCheck { param($id) $null = Test-UiForegroundProcess -ProcessId $id } }) 'the production probe must come up ready'
                Assert-True ($null -ne ('TraceFoldUiForegroundProbe' -as [type])) 'the driver check must have created the probe type'

                # Negative: the check ran but had no effect.
                $script:probeNoopRan = $false
                Assert-Equal 'single.instance.foreground_probe_unavailable' (Get-ProbeRefusal { Initialize-SingleInstanceForegroundProbe -ProcessId 4242 -ForegroundCheck { param($id) $script:probeNoopRan = $true } -ResolveProbeType { $null } }) 'a check with no effect must be refused'
                Assert-True $script:probeNoopRan 'the no-op check must really have run'

                # A check that throws leaves with its own exception, not with a rewritten code.
                Assert-Equal 'single.instance.foreground_check_failed' (Get-ProbeRefusal { Initialize-SingleInstanceForegroundProbe -ProcessId 4242 -ForegroundCheck { param($id) throw 'single.instance.foreground_check_failed' } }) 'the check failure must be preserved as it is'
            }
        },
        @{
            Name = 'the second start exit decision refuses unknown and non zero exits by name'
            Body = {
                # The exit decision is one unit: zero is the redirect the product promises, any other
                # number is its own failure, and a missing or unreadable value is unknown - the cast
                # that would turn it into a zero never happens.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Assert-SingleInstanceSecondaryExit')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 1 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                function Get-ExitRefusalCode {
                    param([scriptblock] $Body)
                    try { & $Body } catch { return [string] $_.Exception.Message }
                    return 'none'
                }

                Assert-Equal 'none' (Get-ExitRefusalCode { Assert-SingleInstanceSecondaryExit -ExitCode 0 }) 'a zero exit is the redirect the product promises'
                Assert-Equal 'single.instance.secondary_exit_1' (Get-ExitRefusalCode { Assert-SingleInstanceSecondaryExit -ExitCode 1 }) 'a non zero exit must be refused by its number'
                Assert-Equal 'single.instance.secondary_exit_42' (Get-ExitRefusalCode { Assert-SingleInstanceSecondaryExit -ExitCode 42 }) 'the exit number must be carried'
                Assert-Equal 'single.instance.secondary_exit_unknown' (Get-ExitRefusalCode { Assert-SingleInstanceSecondaryExit -ExitCode $null }) 'a missing exit must not be read as zero'
                Assert-Equal 'single.instance.secondary_exit_unknown' (Get-ExitRefusalCode { Assert-SingleInstanceSecondaryExit -ExitCode 'not-a-number' }) 'an unreadable exit must be refused as unknown'
            }
        },
        @{
            Name = 'the single instance database rule reads real files and refuses every wrong root'
            Body = {
                # The rule is driven with real temporary files through the real inventory helpers, so
                # the decision and the file system are exercised together: the one expected database
                # under the canonical owned root, a missing database, a duplicate or other database,
                # a second start that adds a file, and the WAL/SHM sidecars that are never counted as
                # a second database.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Get-SingleInstanceDbInventory', 'Get-SingleInstanceDbSidecarInventory',
                    'Get-SingleInstanceExpectedDbPath', 'Get-SingleInstanceLibraryInventory',
                    'Assert-SingleInstanceDatabaseState', 'Assert-SingleInstanceSecondStartDatabase')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 6 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                function Get-DatabaseRefusalCode {
                    param([scriptblock] $Body)
                    try { & $Body } catch { return [string] $_.Exception.Message }
                    return 'none'
                }

                $sandbox = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-si-database-' + [guid]::NewGuid().ToString('N'))
                try {
                    [void] [IO.Directory]::CreateDirectory($sandbox)

                    # The unique expected database.
                    $oneRoot = Join-Path $sandbox 'one'
                    [void] [IO.Directory]::CreateDirectory($oneRoot)
                    [IO.File]::WriteAllText((Join-Path $oneRoot 'media-desktop.db'), 'x')
                    $expected = Get-SingleInstanceExpectedDbPath -Root $oneRoot
                    Assert-Equal ([IO.Path]::GetFullPath((Join-Path $oneRoot 'media-desktop.db'))) $expected 'the expected path must be the canonical owned root database'
                    $one = @(Get-SingleInstanceDbInventory -Root $oneRoot)
                    Assert-Equal 1 $one.Count
                    Assert-Equal 'none' (Get-DatabaseRefusalCode { Assert-SingleInstanceDatabaseState -ExpectedDatabasePath $expected -Databases $one }) 'the one expected database must be accepted'

                    # A missing database.
                    $missingRoot = Join-Path $sandbox 'missing'
                    [void] [IO.Directory]::CreateDirectory($missingRoot)
                    $missing = @(Get-SingleInstanceDbInventory -Root $missingRoot)
                    Assert-Equal 0 $missing.Count
                    Assert-Equal 'single.instance.expected_database_count_0' (Get-DatabaseRefusalCode { Assert-SingleInstanceDatabaseState -ExpectedDatabasePath (Get-SingleInstanceExpectedDbPath -Root $missingRoot) -Databases $missing }) 'a missing database must be refused by count'

                    # A duplicate: the expected database and another one.
                    $dupRoot = Join-Path $sandbox 'duplicate'
                    [void] [IO.Directory]::CreateDirectory($dupRoot)
                    [IO.File]::WriteAllText((Join-Path $dupRoot 'media-desktop.db'), 'x')
                    [IO.File]::WriteAllText((Join-Path $dupRoot 'other.db'), 'x')
                    $duplicate = @(Get-SingleInstanceDbInventory -Root $dupRoot)
                    Assert-Equal 2 $duplicate.Count
                    Assert-Equal 'single.instance.expected_database_count_2' (Get-DatabaseRefusalCode { Assert-SingleInstanceDatabaseState -ExpectedDatabasePath (Get-SingleInstanceExpectedDbPath -Root $dupRoot) -Databases $duplicate }) 'a duplicate database must be refused by count'

                    # A single database that is not the expected one.
                    $otherRoot = Join-Path $sandbox 'other'
                    [void] [IO.Directory]::CreateDirectory($otherRoot)
                    [IO.File]::WriteAllText((Join-Path $otherRoot 'moved.db'), 'x')
                    $other = @(Get-SingleInstanceDbInventory -Root $otherRoot)
                    Assert-Equal 1 $other.Count
                    Assert-Equal 'single.instance.expected_database_missing' (Get-DatabaseRefusalCode { Assert-SingleInstanceDatabaseState -ExpectedDatabasePath (Get-SingleInstanceExpectedDbPath -Root $otherRoot) -Databases $other }) 'a database under another name must be refused'

                    # The sidecars belong to the main database and are never a second database.
                    $sidecarRoot = Join-Path $sandbox 'sidecar'
                    [void] [IO.Directory]::CreateDirectory($sidecarRoot)
                    [IO.File]::WriteAllText((Join-Path $sidecarRoot 'media-desktop.db'), 'x')
                    [IO.File]::WriteAllText((Join-Path $sidecarRoot 'media-desktop.db-wal'), 'x')
                    [IO.File]::WriteAllText((Join-Path $sidecarRoot 'media-desktop.db-shm'), 'x')
                    $sidecarExpected = Get-SingleInstanceExpectedDbPath -Root $sidecarRoot
                    $main = @(Get-SingleInstanceDbInventory -Root $sidecarRoot)
                    $sidecars = @(Get-SingleInstanceDbSidecarInventory -Root $sidecarRoot)
                    Assert-Equal 1 $main.Count 'the sidecars must not count as a second database'
                    Assert-Equal 2 $sidecars.Count 'both sidecars must be listed separately'
                    Assert-Equal 'none' (Get-DatabaseRefusalCode { Assert-SingleInstanceDatabaseState -ExpectedDatabasePath $sidecarExpected -Databases $main }) 'the active database with its sidecars must be accepted'

                    # The second start: before and after must be the same single database.
                    $libraryBefore = @(Get-SingleInstanceLibraryInventory -Root $sidecarRoot)
                    $secondStart = Assert-SingleInstanceSecondStartDatabase -ExpectedDatabasePath $sidecarExpected `
                        -DatabasesBefore $main -DatabasesAfter $main -LibraryBefore $libraryBefore -LibraryAfter $libraryBefore
                    Assert-Equal 0 @($secondStart.DatabasesCreated).Count 'an unchanged root creates no database'
                    Assert-Equal 0 @($secondStart.LibraryCreated).Count 'an unchanged root creates no library file'

                    # The second start creating a file is refused by count, a database that appeared
                    # where there was none is its own case, and a different single database is refused.
                    [IO.File]::WriteAllText((Join-Path $sidecarRoot 'added.db'), 'x')
                    $grown = @(Get-SingleInstanceDbInventory -Root $sidecarRoot)
                    Assert-Equal 'single.instance.expected_database_count_2' (Get-DatabaseRefusalCode { Assert-SingleInstanceSecondStartDatabase -ExpectedDatabasePath $sidecarExpected -DatabasesBefore $main -DatabasesAfter $grown -LibraryBefore $libraryBefore -LibraryAfter $libraryBefore }) 'a second file in the root must be refused'
                    Assert-Equal 'single.instance.second_database_created' (Get-DatabaseRefusalCode { Assert-SingleInstanceSecondStartDatabase -ExpectedDatabasePath $sidecarExpected -DatabasesBefore @() -DatabasesAfter @($sidecarExpected) -LibraryBefore @() -LibraryAfter @() }) 'a database created by the second start must be refused as its own case'
                    $swapped = @((Get-SingleInstanceExpectedDbPath -Root $dupRoot))
                    Assert-Equal 'single.instance.second_database_used' (Get-DatabaseRefusalCode { Assert-SingleInstanceSecondStartDatabase -ExpectedDatabasePath $sidecarExpected -DatabasesBefore @($sidecarExpected) -DatabasesAfter $swapped -LibraryBefore $libraryBefore -LibraryAfter $libraryBefore }) 'a different single database must be refused'

                    # A library file appearing between the two starts is refused too.
                    $libraryRoot = Join-Path $sandbox 'library'
                    [void] [IO.Directory]::CreateDirectory((Join-Path $libraryRoot 'library'))
                    [IO.File]::WriteAllText((Join-Path $libraryRoot 'media-desktop.db'), 'x')
                    [IO.File]::WriteAllText((Join-Path $libraryRoot 'library/asset.bin'), 'x')
                    $libraryExpected = Get-SingleInstanceExpectedDbPath -Root $libraryRoot
                    $libraryMain = @(Get-SingleInstanceDbInventory -Root $libraryRoot)
                    Assert-Equal 'single.instance.second_library_used' (Get-DatabaseRefusalCode { Assert-SingleInstanceSecondStartDatabase -ExpectedDatabasePath $libraryExpected -DatabasesBefore $libraryMain -DatabasesAfter $libraryMain -LibraryBefore @() -LibraryAfter @(Get-SingleInstanceLibraryInventory -Root $libraryRoot) }) 'a new library file must be refused'
                }
                finally {
                    Remove-UiTestTree -Path $sandbox
                }
            }
        },
        @{
            Name = 'the exit evidence writes every required document and lists a failed write as its own fact'
            Body = {
                # The writer is driven as it really is, lifted by AST, over real temporary directories.
                # Every required document is written, a document that cannot be written is recorded as
                # its own fact in the caller's failure list, and the required state goes incomplete so
                # a run whose evidence did not land can never be reported as a pass.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Complete-SingleInstanceControlRelease', 'New-SingleInstanceReleaseAttempt',
                    'ConvertTo-SingleInstanceErrorFact', 'New-SingleInstanceFallbackErrorFact',
                    'New-SingleInstanceErrorFactSafe', 'Write-SingleInstanceExitEvidence')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 6 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                if (-not ('System.Windows.Forms.Form' -as [type])) { Add-Type -AssemblyName System.Windows.Forms }

                $sandbox = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-si-evidence-' + [guid]::NewGuid().ToString('N'))
                try {
                    [void] [IO.Directory]::CreateDirectory($sandbox)

                    # The attempts come from real runs of the shared release unit, and they are
                    # handed over in the shape production uses: a generic object list. Wrapping one
                    # in @() fails on both engines, so the writer must take the list as it is.
                    $goodForm = New-Object System.Windows.Forms.Form
                    $closedAttempt = New-SingleInstanceReleaseAttempt -Source 'success-path' -Stage 'release-after-second-start' `
                        -Release (Complete-SingleInstanceControlRelease -ControlWindow $goodForm)
                    $listAttempts = New-Object System.Collections.Generic.List[object]
                    $listAttempts.Add($closedAttempt)
                    $silent = New-Object psobject -Property @{ IsDisposed = $false }
                    $silent | Add-Member -MemberType ScriptMethod -Name Close -Value { }
                    $silent | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                    $failedAttempt = New-SingleInstanceReleaseAttempt -Source 'safety-net' -Stage 'release-safety-net' `
                        -Release (Complete-SingleInstanceControlRelease -ControlWindow $silent)

                    # 1. a passed run: the release record and the success evidence are both required
                    # and both land, with the attempt facts inside the release record.
                    $okOut = Join-Path $sandbox 'ok'
                    [void] [IO.Directory]::CreateDirectory($okOut)
                    $artifacts = New-Object System.Collections.Generic.List[string]
                    $failures = New-Object System.Collections.Generic.List[object]
                    $outcome = Write-SingleInstanceExitEvidence -OutputDirectory $okOut -Artifacts $artifacts -Failures $failures `
                        -SuccessEvidence ([ordered] @{ scenarioId = 'single-instance'; value = 1 }) -BusinessErrorRecord $null `
                        -BusinessStage 'restart-verified' -ReleaseAttempts $listAttempts -FactoryReleaseReport $null
                    Assert-Equal 'True' ([string] $outcome.controlReleaseWritten) 'the release record is required once a release ran'
                    Assert-Equal 'True' ([string] $outcome.successEvidenceWritten) 'the success evidence is required for a pass'
                    Assert-Equal 'False' ([string] $outcome.stageErrorWritten) 'a pass writes no failure forensics'
                    Assert-Equal 'True' ([string] $outcome.requiredComplete) 'every required document was written'
                    Assert-Equal 0 $failures.Count 'a clean write records no failure'
                    Assert-Equal 2 $artifacts.Count 'both written documents must be registered'
                    # Read as UTF-8 explicitly: the evidence files are written without a BOM, and
                    # the older runtime's Get-Content would decode them with the console code page.
                    $releaseText = [IO.File]::ReadAllText((Join-Path $okOut 'single-instance-control-release.json'))
                    $releaseDoc = $releaseText | ConvertFrom-Json
                    Assert-Equal 'True' ([string] $releaseDoc.releaseAttempted)
                    Assert-Equal 'True' ([string] $releaseDoc.confirmed) 'the confirmed fact is the record of record'
                    Assert-Equal 1 ([int] $releaseDoc.attemptCount)
                    Assert-Equal 'success-path' ([string] @($releaseDoc.attempts)[0].source)
                    Assert-Equal 'release-after-second-start' ([string] $releaseDoc.stage)
                    # No raw ErrorRecord may ever reach the document.
                    Assert-True ($releaseText -notmatch 'actionErrorRecord') 'the raw record must not be serialised'
                    Assert-True ($releaseText -notmatch 'CLIXML') 'no CLIXML may be written'

                    # 2. a failed run: the failure forensics hold the original error at the top level
                    # and list the release and read back errors as their own facts.
                    $badOut = Join-Path $sandbox 'bad'
                    [void] [IO.Directory]::CreateDirectory($badOut)
                    $artifacts2 = New-Object System.Collections.Generic.List[string]
                    $failures2 = New-Object System.Collections.Generic.List[object]
                    $business = $null
                    try { throw 'single.instance.probe_business_failure' } catch { $business = $_ }
                    $stub = [pscustomobject] @{ Note = 'x' }
                    $stub | Add-Member -MemberType ScriptMethod -Name Close -Value { throw 'stub close failed' }
                    $stub | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                    $bothAttempt = New-SingleInstanceReleaseAttempt -Source 'success-path' -Stage 'release-after-second-start' `
                        -Release (Complete-SingleInstanceControlRelease -ControlWindow $stub)
                    # The writer only reads the two flags of this node; the real hand over path into
                    # the writer is driven by the factory hand over and connection cases.
                    $factoryReport = [pscustomobject] @{ objectObtained = $true; releaseAttempted = $true; attempt = $bothAttempt }
                    # Two attempts, so the record must keep the earlier one beside the standing one:
                    # the silent first attempt keeps no fact nodes, the failing last attempt does, and
                    # the standing facts come from that last attempt.
                    $outcome2 = Write-SingleInstanceExitEvidence -OutputDirectory $badOut -Artifacts $artifacts2 -Failures $failures2 `
                        -SuccessEvidence $null -BusinessErrorRecord $business -BusinessStage 'secondary-exited' `
                        -ReleaseAttempts @($failedAttempt, $bothAttempt) -FactoryReleaseReport $factoryReport
                    Assert-Equal 'True' ([string] $outcome2.stageErrorWritten)
                    Assert-Equal 'True' ([string] $outcome2.controlReleaseWritten)
                    Assert-Equal 'True' ([string] $outcome2.requiredComplete)
                    # Read as UTF-8 explicitly: an evidence file may carry a localised error message
                    # and the older runtime's Get-Content would decode it with the console code page.
                    $errorText = [IO.File]::ReadAllText((Join-Path $badOut 'single-instance-stage-error.json'))
                    $errorDoc = $errorText | ConvertFrom-Json
                    Assert-Equal 'secondary-exited' ([string] $errorDoc.stage)
                    Assert-True ($errorDoc.message -like '*probe_business_failure*') 'the original error must stay at the top level'
                    Assert-Equal 'release-after-second-start' ([string] $errorDoc.releaseActionError.stage)
                    Assert-True ($errorDoc.releaseActionError.errorType -like '*MethodInvocationException') 'the action error kind must be retained'
                    Assert-Equal 'release-after-second-start' ([string] $errorDoc.releaseReadBackError.stage)
                    Assert-True ($errorDoc.releaseReadBackError.errorType -like '*PropertyNotFoundException') 'the read back error kind must be retained'
                    Assert-Equal 'True' ([string] $errorDoc.factoryRelease.objectObtained)
                    Assert-Equal 'False' ([string] $errorDoc.release.confirmed) 'the failed release must not be confirmed'
                    Assert-True ($errorText -match '"exitFailures"') 'the exit failure list must be part of the forensics'
                    # Both attempts are kept: the earlier silent one is still in the record beside the
                    # failing one, and each attempt keeps its own fact nodes.
                    $badReleaseText = [IO.File]::ReadAllText((Join-Path $badOut 'single-instance-control-release.json'))
                    $badReleaseDoc = $badReleaseText | ConvertFrom-Json
                    Assert-Equal 2 ([int] $badReleaseDoc.attemptCount) 'both attempts must be kept'
                    Assert-Equal 'safety-net' ([string] @($badReleaseDoc.attempts)[0].source) 'the earlier attempt stays first'
                    Assert-Equal 'success-path' ([string] @($badReleaseDoc.attempts)[1].source) 'the standing attempt stays last'
                    Assert-True ($null -eq @($badReleaseDoc.attempts)[0].closeErrorFact) 'the silent attempt has no close fact'
                    Assert-True ($null -ne @($badReleaseDoc.attempts)[1].closeErrorFact) 'the failing attempt keeps its close fact'
                    Assert-Equal 'False' ([string] $badReleaseDoc.confirmed) 'a failed last attempt is not confirmed'

                    # 3. a document that cannot be written: the write is recorded as its own fact, the
                    # other required document still lands, and the required state goes incomplete.
                    $blockedOut = Join-Path $sandbox 'blocked'
                    [void] [IO.Directory]::CreateDirectory($blockedOut)
                    [void] [IO.Directory]::CreateDirectory((Join-Path $blockedOut 'single-instance-control-release.json'))
                    $artifacts3 = New-Object System.Collections.Generic.List[string]
                    $failures3 = New-Object System.Collections.Generic.List[object]
                    $outcome3 = Write-SingleInstanceExitEvidence -OutputDirectory $blockedOut -Artifacts $artifacts3 -Failures $failures3 `
                        -SuccessEvidence ([ordered] @{ scenarioId = 'single-instance' }) -BusinessErrorRecord $null `
                        -BusinessStage 'restart-verified' -ReleaseAttempts @($closedAttempt) -FactoryReleaseReport $null
                    Assert-Equal 'False' ([string] $outcome3.controlReleaseWritten) 'a blocked write must not be reported as written'
                    Assert-Equal 'False' ([string] $outcome3.requiredComplete) 'a required document that did not land makes the state incomplete'
                    Assert-Equal 'True' ([string] $outcome3.successEvidenceWritten) 'the other required document must still be written'
                    Assert-Equal 1 $failures3.Count 'the failed write must be listed as its own fact'
                    Assert-Equal 'control-release-write' ([string] $failures3[0].stage)
                    Assert-True (-not [string]::IsNullOrWhiteSpace([string] $failures3[0].message)) 'the write failure must carry its message'
                    Assert-Equal 1 $artifacts3.Count 'only the document that landed may be registered'

                    # 4. the failure forensics themselves failing: the write failure still reaches the
                    # caller's list, so it can be announced even though nothing could be written.
                    $blockedOut2 = Join-Path $sandbox 'blocked2'
                    [void] [IO.Directory]::CreateDirectory($blockedOut2)
                    [void] [IO.Directory]::CreateDirectory((Join-Path $blockedOut2 'single-instance-stage-error.json'))
                    $artifacts4 = New-Object System.Collections.Generic.List[string]
                    $failures4 = New-Object System.Collections.Generic.List[object]
                    $outcome4 = Write-SingleInstanceExitEvidence -OutputDirectory $blockedOut2 -Artifacts $artifacts4 -Failures $failures4 `
                        -SuccessEvidence $null -BusinessErrorRecord $business -BusinessStage 'secondary-exited' `
                        -ReleaseAttempts @() -FactoryReleaseReport $null
                    Assert-Equal 'False' ([string] $outcome4.stageErrorWritten)
                    Assert-Equal 'False' ([string] $outcome4.requiredComplete)
                    Assert-Equal 1 $failures4.Count 'the failed forensics write must still be announced'
                    Assert-Equal 'stage-error-write' ([string] $failures4[0].stage)
                    Assert-Equal 0 $artifacts4.Count 'nothing was written'
                }
                finally {
                    Remove-UiTestTree -Path $sandbox
                }
            }
        },
        @{
            Name = 'the final verdict turns a spoiled pass into a named failure and keeps a business failure'
            Body = {
                # The verdict is decided from the saved business outcome, the release facts and the
                # write failures together. A business failure keeps its own code; a pass that was
                # spoiled by an unconfirmed release or a missing required document becomes a named
                # failure instead.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Get-SingleInstanceFinalVerdict')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 1 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                $clean = Get-SingleInstanceFinalVerdict -BusinessOutcome 'Passed' -BusinessReason 'single.instance.verified' `
                    -ReleaseConfirmed $true -RequiredEvidenceComplete $true -ReleaseAttempts @()
                Assert-Equal 'Passed' ([string] $clean.Status) 'a confirmed release with complete evidence stays a pass'
                Assert-Equal 'single.instance.verified' ([string] $clean.Reason)

                # Production hands the attempts over as a generic object list, so one case drives
                # that exact shape: @() over such a list fails on both engines and the unit must
                # take the collection as it is.
                $listAttempts = New-Object System.Collections.Generic.List[object]
                $listAttempts.Add([pscustomobject] @{ releaseFailure = 'single.instance.control_window_not_released' })
                $releaseFailed = Get-SingleInstanceFinalVerdict -BusinessOutcome 'Passed' -BusinessReason 'single.instance.verified' `
                    -ReleaseConfirmed $false -RequiredEvidenceComplete $true `
                    -ReleaseAttempts $listAttempts
                Assert-Equal 'Failed' ([string] $releaseFailed.Status) 'a pass without a confirmed release is not a pass'
                Assert-Equal 'single.instance.control_window_not_released' ([string] $releaseFailed.Reason)

                $noAttempt = Get-SingleInstanceFinalVerdict -BusinessOutcome 'Passed' -BusinessReason 'single.instance.verified' `
                    -ReleaseConfirmed $false -RequiredEvidenceComplete $true -ReleaseAttempts @()
                Assert-Equal 'Failed' ([string] $noAttempt.Status)
                Assert-Equal 'single.instance.control_window_not_released' ([string] $noAttempt.Reason) 'an unconfirmed release without an attempt is still refused'

                $standing = Get-SingleInstanceFinalVerdict -BusinessOutcome 'Passed' -BusinessReason 'single.instance.verified' `
                    -ReleaseConfirmed $false -RequiredEvidenceComplete $true `
                    -ReleaseAttempts @([pscustomobject] @{ releaseFailure = 'single.instance.control_window_not_closed: MethodInvocationException' })
                Assert-Equal 'single.instance.control_window_not_closed: MethodInvocationException' ([string] $standing.Reason) 'the standing release failure is carried'

                $evidenceFailed = Get-SingleInstanceFinalVerdict -BusinessOutcome 'Passed' -BusinessReason 'single.instance.verified' `
                    -ReleaseConfirmed $true -RequiredEvidenceComplete $false -ReleaseAttempts @()
                Assert-Equal 'Failed' ([string] $evidenceFailed.Status) 'a missing required document must spoil the pass'
                Assert-Equal 'single.instance.required_evidence_write_failed' ([string] $evidenceFailed.Reason)

                $kept = Get-SingleInstanceFinalVerdict -BusinessOutcome 'Failed' -BusinessReason 'single.instance.secondary_exit_1' `
                    -ReleaseConfirmed $false -RequiredEvidenceComplete $false `
                    -ReleaseAttempts @([pscustomobject] @{ releaseFailure = 'single.instance.control_window_not_released' })
                Assert-Equal 'Failed' ([string] $kept.Status)
                Assert-Equal 'single.instance.secondary_exit_1' ([string] $kept.Reason) 'a business failure keeps its own code'

                $blocked = Get-SingleInstanceFinalVerdict -BusinessOutcome 'Blocked' -BusinessReason 'single.instance.desktop_unavailable' `
                    -ReleaseConfirmed $false -RequiredEvidenceComplete $true -ReleaseAttempts @()
                Assert-Equal 'Blocked' ([string] $blocked.Status)
                Assert-Equal 'single.instance.desktop_unavailable' ([string] $blocked.Reason) 'a host condition stays Blocked'
            }
        },
        @{
            Name = 'the single instance scenario hands its units to the main flow and builds its result last'
            Body = {
                # A connection check on the source, labelled as one: the units above are only evidence
                # about the scenario if the scenario really calls them, and the ordering below is the
                # one the result contract requires. Static checks do not replace the real run.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $source = Get-Content -LiteralPath $path -Raw
                $invocations = @($ast.FindAll(
                    { param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true) |
                    ForEach-Object { $_.GetCommandName() } |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                foreach ($unit in @(
                        'Initialize-SingleInstanceForegroundProbe',
                        'Assert-SingleInstanceDatabaseState',
                        'Assert-SingleInstanceSecondStartDatabase',
                        'Assert-SingleInstanceSecondaryExit',
                        'Get-SingleInstanceFinalVerdict',
                        'Invoke-SingleInstanceCloseAndRestart',
                        'Import-SingleInstanceFactoryReleaseReport',
                        'Invoke-SingleInstanceSafetyNet',
                        'Write-SingleInstanceExitEvidence')) {
                    Assert-Equal 1 @($invocations | Where-Object { $_ -ceq $unit }).Count ($unit + ' must be called exactly once by the main flow')
                }
                # The release unit is used by the factory, by the success path and by the safety net:
                # three call sites, one unit.
                Assert-Equal 3 @($invocations | Where-Object { $_ -ceq 'Complete-SingleInstanceControlRelease' }).Count 'the release unit must run from the factory, the success path and the safety net'
                Assert-Equal 3 @($invocations | Where-Object { $_ -ceq 'New-SingleInstanceReleaseAttempt' }).Count 'every release path must record its attempt'
                Assert-True (@($invocations | Where-Object { $_ -ceq 'ConvertTo-SingleInstanceErrorFact' }).Count -ge 6) 'every error kind must go through the shared fact converter'
                # The safe wrapper is the only conversion used inside any failure path: six call sites
                # (the hand over read, the safety net guard, the exit evidence guard and the three
                # document writes), and no catch converts through the raw converter.
                Assert-Equal 6 @($invocations | Where-Object { $_ -ceq 'New-SingleInstanceErrorFactSafe' }).Count 'every catch context must convert through the fallback backed wrapper'
                Assert-True (-not $source.Contains('ConvertTo-SingleInstanceErrorFact -ErrorRecord $_')) 'no catch context may call the raw converter'

                # The order is the contract: the release happens, the safety net follows it, then the
                # exit evidence is written, then the single result is decided.
                $releaseCall = $source.IndexOf('$release = Complete-SingleInstanceControlRelease -ControlWindow $controlWindow')
                $safetyCall = $source.IndexOf('$safety = Invoke-SingleInstanceSafetyNet -ControlWindow $controlWindow')
                $writerCall = $source.IndexOf('$exitEvidence = Write-SingleInstanceExitEvidence')
                $verdictCall = $source.IndexOf('$verdict = Get-SingleInstanceFinalVerdict')
                Assert-True ($releaseCall -gt 0) 'the release must stay a simple call site'
                Assert-True ($safetyCall -gt $releaseCall) 'the safety net must run after the release attempt'
                Assert-True ($writerCall -gt $safetyCall) 'the exit evidence must be written after the release'
                Assert-True ($verdictCall -gt $writerCall) 'the result must be decided after the exit evidence'
                # Every guarded failure in the finally and every write failure is named as its own fact.
                foreach ($stage in @('release-safety-net-guard', 'exit-evidence', 'control-release-write', 'stage-error-write', 'success-evidence-write')) {
                    Assert-True ($source -cmatch ('-Stage ''' + $stage + '''')) ('the failure of ' + $stage + ' must be recorded as its own fact')
                }
                # A generic object list must never be wrapped in the array subexpression: on both
                # engines @() over a List[object] fails with "Argument types do not match", so the
                # collections this scenario owns are handed over and read exactly as they are.
                foreach ($forbidden in @('@($releaseAttempts)', '@($exitFailures)', '@($ReleaseAttempts)', '@($Failures)')) {
                    Assert-True (-not $source.Contains($forbidden)) ($forbidden + ' must not wrap a generic object list')
                }
            }
        },
        @{
            Name = 'the production failure connection decides its final facts through the real units'
            Body = {
                # The scenario's catch -> finally -> result order runs here through the very units the
                # scenario calls, in the same order: the factory hand over is read, the safety net
                # releases what is still open, the exit evidence is written, and the verdict decides
                # the single result. Nothing is hand fed to the writer and no verdict boolean is
                # fabricated: every case asserts the final status, the final reason, the confirmed
                # facts and the persisted documents together.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Complete-SingleInstanceControlRelease', 'New-SingleInstanceReleaseAttempt',
                    'ConvertTo-SingleInstanceErrorFact', 'New-SingleInstanceFallbackErrorFact',
                    'New-SingleInstanceErrorFactSafe', 'New-NonForegroundControlWindow',
                    'Import-SingleInstanceFactoryReleaseReport', 'Invoke-SingleInstanceSafetyNet',
                    'Write-SingleInstanceExitEvidence', 'Get-SingleInstanceFinalVerdict')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 10 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                if (-not ('System.Windows.Forms.Form' -as [type])) { Add-Type -AssemblyName System.Windows.Forms }

                function New-ConnectionRecord {
                    param([string] $Message)
                    try { throw $Message } catch { return $_ }
                }

                function Invoke-ConnectionProbe {
                    param(
                        $BusinessRecord,
                        [string] $BusinessOutcome,
                        [string] $BusinessReason,
                        $ControlWindow,
                        [bool] $AlreadyClosed,
                        $Attempts,
                        $Failures,
                        [string] $OutDirectory,
                        $SuccessEvidence
                    )
                    $factoryReport = $null
                    $closed = [bool] $AlreadyClosed
                    $failure = ''
                    # The catch reads the hand over only when there is a failure to hand over from.
                    if ($null -ne $BusinessRecord) {
                        $received = Import-SingleInstanceFactoryReleaseReport -ErrorRecord $BusinessRecord -ReleaseAttempts $Attempts -Failures $Failures
                        $factoryReport = $received.report
                        if ($null -ne $received.attempt) {
                            $failure = [string] $received.attempt.releaseFailure
                            if ([bool] $received.attempt.closed) { $closed = $true }
                        }
                    }
                    $safety = Invoke-SingleInstanceSafetyNet -ControlWindow $ControlWindow -AlreadyClosed $closed -ReleaseAttempts $Attempts -Failures $Failures
                    if ([bool] $safety.attempted) { $failure = [string] $safety.failure }
                    $closed = [bool] $safety.closed
                    $artifacts = New-Object System.Collections.Generic.List[string]
                    $evidence = Write-SingleInstanceExitEvidence -OutputDirectory $OutDirectory -Artifacts $artifacts -Failures $Failures `
                        -SuccessEvidence $SuccessEvidence -BusinessErrorRecord $BusinessRecord -BusinessStage 'connection-probe' `
                        -ReleaseAttempts $Attempts -FactoryReleaseReport $factoryReport
                    $requiredComplete = $false
                    if ($null -ne $evidence) { $requiredComplete = [bool] $evidence.requiredComplete }
                    $verdict = Get-SingleInstanceFinalVerdict -BusinessOutcome $BusinessOutcome -BusinessReason $BusinessReason `
                        -ReleaseConfirmed $closed -RequiredEvidenceComplete $requiredComplete -ReleaseAttempts $Attempts
                    return [pscustomobject] @{
                        Status           = [string] $verdict.Status
                        Reason           = [string] $verdict.Reason
                        ReleaseConfirmed = [bool] $closed
                        ReleaseFailure   = [string] $failure
                    }
                }

                $sandbox = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-si-connection-' + [guid]::NewGuid().ToString('N'))
                try {
                    [void] [IO.Directory]::CreateDirectory($sandbox)

                    # 1. the factory original error with a release that really succeeded: the business
                    # failure and its code are kept, and the confirmed release reaches the final
                    # field, the release record and the forensics alike.
                    $releasedRecord = $null
                    try { $null = New-NonForegroundControlWindow -ShowAction { throw 'single.instance.connection_factory_failure' } } catch { $releasedRecord = $_ }
                    $attempts1 = New-Object System.Collections.Generic.List[object]
                    $failures1 = New-Object System.Collections.Generic.List[object]
                    $out1 = Join-Path $sandbox 'factory-released'
                    [void] [IO.Directory]::CreateDirectory($out1)
                    $result1 = Invoke-ConnectionProbe -BusinessRecord $releasedRecord -BusinessOutcome 'Failed' `
                        -BusinessReason 'single.instance.connection_factory_failure' -ControlWindow $null -AlreadyClosed $false `
                        -Attempts $attempts1 -Failures $failures1 -OutDirectory $out1 -SuccessEvidence $null
                    Assert-Equal 'Failed' ([string] $result1.Status) 'the original factory error must stay a business failure'
                    Assert-Equal 'single.instance.connection_factory_failure' ([string] $result1.Reason) 'the business code must be kept'
                    Assert-Equal 'True' ([string] $result1.ReleaseConfirmed) 'the release the factory confirmed must reach the final field'
                    Assert-Equal '' ([string] $result1.ReleaseFailure) 'a confirmed release records no failure'
                    Assert-Equal 1 $attempts1.Count
                    Assert-Equal 0 $failures1.Count
                    $releaseDoc1 = [IO.File]::ReadAllText((Join-Path $out1 'single-instance-control-release.json')) | ConvertFrom-Json
                    $errorDoc1 = [IO.File]::ReadAllText((Join-Path $out1 'single-instance-stage-error.json')) | ConvertFrom-Json
                    Assert-Equal 'True' ([string] $releaseDoc1.confirmed) 'the record must agree about the confirmed release'
                    Assert-Equal 'True' ([string] $errorDoc1.release.confirmed) 'the forensics must agree about it too'
                    Assert-Equal ([string] $releaseDoc1.confirmed) ([string] $result1.ReleaseConfirmed) 'the record and the final field must agree'
                    Assert-Equal ([string] $errorDoc1.release.confirmed) ([string] $result1.ReleaseConfirmed) 'the forensics and the final field must agree'

                    # 2. a business error with a release that fails in the safety net: the business
                    # code is kept, the release stays unconfirmed, and the action error is its own
                    # fact in the forensics.
                    $plainRecord = New-ConnectionRecord 'single.instance.connection_business_failure'
                    $stub = [pscustomobject] @{ Note = 'x' }
                    $stub | Add-Member -MemberType ScriptMethod -Name Close -Value { throw 'stub close failed' }
                    $stub | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                    $attempts2 = New-Object System.Collections.Generic.List[object]
                    $failures2 = New-Object System.Collections.Generic.List[object]
                    $out2 = Join-Path $sandbox 'action-error'
                    [void] [IO.Directory]::CreateDirectory($out2)
                    $result2 = Invoke-ConnectionProbe -BusinessRecord $plainRecord -BusinessOutcome 'Failed' `
                        -BusinessReason 'single.instance.connection_business_failure' -ControlWindow $stub -AlreadyClosed $false `
                        -Attempts $attempts2 -Failures $failures2 -OutDirectory $out2 -SuccessEvidence $null
                    Assert-Equal 'Failed' ([string] $result2.Status)
                    Assert-Equal 'single.instance.connection_business_failure' ([string] $result2.Reason)
                    Assert-Equal 'False' ([string] $result2.ReleaseConfirmed) 'a failed safety net release is not confirmed'
                    Assert-True ($result2.ReleaseFailure -clike 'single.instance.control_window_not_closed*') 'the close error is the standing failure'
                    Assert-Equal 1 $attempts2.Count
                    Assert-Equal 'safety-net' ([string] $attempts2[0].source)
                    Assert-Equal 0 $failures2.Count
                    $errorDoc2 = [IO.File]::ReadAllText((Join-Path $out2 'single-instance-stage-error.json')) | ConvertFrom-Json
                    Assert-Equal 'False' ([string] $errorDoc2.release.confirmed)
                    Assert-True ($errorDoc2.releaseActionError.errorType -like '*MethodInvocationException') 'the action error kind must be retained'

                    # 3. a read back that cannot answer: unknown stays unknown, its own fact is kept,
                    # and the business failure is untouched.
                    $noRead = [pscustomobject] @{ Note = 'x' }
                    $noRead | Add-Member -MemberType ScriptMethod -Name Close -Value { }
                    $noRead | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
                    $attempts3 = New-Object System.Collections.Generic.List[object]
                    $failures3 = New-Object System.Collections.Generic.List[object]
                    $out3 = Join-Path $sandbox 'read-back-error'
                    [void] [IO.Directory]::CreateDirectory($out3)
                    $result3 = Invoke-ConnectionProbe -BusinessRecord $plainRecord -BusinessOutcome 'Failed' `
                        -BusinessReason 'single.instance.connection_business_failure' -ControlWindow $noRead -AlreadyClosed $false `
                        -Attempts $attempts3 -Failures $failures3 -OutDirectory $out3 -SuccessEvidence $null
                    Assert-Equal 'Failed' ([string] $result3.Status)
                    Assert-Equal 'single.instance.connection_business_failure' ([string] $result3.Reason)
                    Assert-Equal 'False' ([string] $result3.ReleaseConfirmed)
                    Assert-Equal 'unknown' ([string] $attempts3[0].releaseReadBack) 'an unreadable read back stays unknown'
                    Assert-True ($attempts3[0].readBackError -clike 'single.instance.control_window_read_back_failed*') 'the read back error is named'
                    $errorDoc3 = [IO.File]::ReadAllText((Join-Path $out3 'single-instance-stage-error.json')) | ConvertFrom-Json
                    Assert-True ($errorDoc3.releaseReadBackError.errorType -like '*PropertyNotFoundException') 'the read back error kind must be retained'

                    # 4. a passed run whose success document cannot be written: the pass is spoiled by
                    # the named evidence failure, the release stays confirmed, and the write failure
                    # is listed in the caller's failure list.
                    $attempts4 = New-Object System.Collections.Generic.List[object]
                    $goodForm = New-Object System.Windows.Forms.Form
                    $attempts4.Add((New-SingleInstanceReleaseAttempt -Source 'success-path' -Stage 'release-after-second-start' `
                                -Release (Complete-SingleInstanceControlRelease -ControlWindow $goodForm)))
                    $failures4 = New-Object System.Collections.Generic.List[object]
                    $out4 = Join-Path $sandbox 'success-write-failure'
                    [void] [IO.Directory]::CreateDirectory($out4)
                    [void] [IO.Directory]::CreateDirectory((Join-Path $out4 'single-instance-safety.json'))
                    $result4 = Invoke-ConnectionProbe -BusinessRecord $null -BusinessOutcome 'Passed' `
                        -BusinessReason 'single.instance.verified' -ControlWindow $null -AlreadyClosed $true `
                        -Attempts $attempts4 -Failures $failures4 -OutDirectory $out4 -SuccessEvidence ([ordered] @{ scenarioId = 'single-instance' })
                    Assert-Equal 'Failed' ([string] $result4.Status) 'a missing required document must spoil the pass'
                    Assert-Equal 'single.instance.required_evidence_write_failed' ([string] $result4.Reason)
                    Assert-Equal 'True' ([string] $result4.ReleaseConfirmed)
                    Assert-Equal 1 $failures4.Count
                    Assert-Equal 'success-evidence-write' ([string] $failures4[0].stage)
                }
                finally {
                    Remove-UiTestTree -Path $sandbox
                }
            }
        },
        @{
            Name = 'the close and restart unit drives the real sequence and refuses every bounded counterexample'
            Body = {
                # The unit the scenario runs is driven with its seams: the order of the real calls is
                # read off an event trace, the restart is checked to carry the same executable and
                # the same owned root with the same timeout, and each refusal of the finite matrix
                # is reached by name.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                Assert-Equal 0 @($errors).Count
                $wanted = @('Invoke-SingleInstanceCloseAndRestart')
                $text = New-Object System.Collections.Generic.List[string]
                foreach ($fn in @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                    if ($wanted -ccontains $fn.Name) { $text.Add($fn.Extent.Text) }
                }
                Assert-Equal 1 $text.Count
                . ([scriptblock]::Create(($text -join [Environment]::NewLine)))

                $liveProcess = [pscustomobject] @{ Note = 'live' }
                $liveProcess | Add-Member -MemberType ScriptMethod -Name CloseMainWindow -Value { return $true }
                $rejectProcess = [pscustomobject] @{ Note = 'reject' }
                $rejectProcess | Add-Member -MemberType ScriptMethod -Name CloseMainWindow -Value { return $false }

                $script:crEvents = New-Object System.Collections.Generic.List[string]
                $script:crRestartArgs = ''
                $script:crFindCalls = 0
                $okFind = {
                    param($id)
                    $script:crFindCalls = $script:crFindCalls + 1
                    $script:crEvents.Add('find:' + $id)
                    if ($script:crFindCalls -eq 1) { return $liveProcess }
                    return $null
                }
                $okInventory = { $script:crEvents.Add('inventory'); return @() }
                $okStart = {
                    param($exe, $root, $token, $support)
                    $script:crEvents.Add('start')
                    $script:crRestartArgs = @($exe, $root, $token, $support) -join '|'
                    return [pscustomobject] @{ ProcessId = 4242 }
                }
                $okWindow = { param($id, $timeout) $script:crEvents.Add('window:' + $id + ':' + $timeout); return [IntPtr] 99 }
                $okObserve = { param($id) $script:crEvents.Add('observe:' + $id); return [pscustomobject] @{ Status = 'Passed' } }

                $facts = Invoke-SingleInstanceCloseAndRestart -PrimaryPid 7331 -ExecutablePath 'C:\probe\TraceFold.Media.exe' `
                    -OwnedRoot 'C:\probe\owned' -RunToken 'probe-token' -SupportPath 'C:\probe\support' `
                    -WaitTimeoutSeconds 5 -InteractiveTimeoutSeconds 7 `
                    -FindProcess $okFind -ProcessInventory $okInventory -StartScenario $okStart -WaitWindow $okWindow -Observe $okObserve -Sleep { param($milliseconds) }
                Assert-Equal 'True' ([string] $facts.closeAccepted)
                Assert-Equal 0 ([int] $facts.residueCount)
                Assert-Equal 4242 ([int] $facts.restartPid)
                Assert-Equal 99 ([long] $facts.restartWindowHandle)
                Assert-Equal 'Passed' ([string] $facts.restartObservationStatus)
                Assert-Equal 'C:\probe\TraceFold.Media.exe' ([string] $facts.restartExecutable)
                Assert-Equal 'C:\probe\owned' ([string] $facts.restartOwnedRoot)
                Assert-Equal 'find:7331|find:7331|inventory|start|window:4242:7|observe:4242' ($script:crEvents -join '|') 'the sequence must close, wait, check residue and then restart'
                Assert-Equal 'C:\probe\TraceFold.Media.exe|C:\probe\owned|probe-token|C:\probe\support' $script:crRestartArgs 'the restart must carry the same executable, root, token and support'

                function Get-CRRefusalCode {
                    param(
                        [scriptblock] $Find = $null,
                        [scriptblock] $Inventory = $null,
                        [scriptblock] $Start = $null,
                        [scriptblock] $Window = $null,
                        [scriptblock] $Observe = $null,
                        [int] $Wait = 5
                    )
                    if ($null -eq $Find) { $Find = $okFind }
                    if ($null -eq $Inventory) { $Inventory = $okInventory }
                    if ($null -eq $Start) { $Start = $okStart }
                    if ($null -eq $Window) { $Window = $okWindow }
                    if ($null -eq $Observe) { $Observe = $okObserve }
                    $script:crFindCalls = 0
                    try {
                        $null = Invoke-SingleInstanceCloseAndRestart -PrimaryPid 7331 -ExecutablePath 'C:\probe\TraceFold.Media.exe' `
                            -OwnedRoot 'C:\probe\owned' -RunToken 'probe-token' -SupportPath 'C:\probe\support' `
                            -WaitTimeoutSeconds $Wait -InteractiveTimeoutSeconds 7 `
                            -FindProcess $Find -ProcessInventory $Inventory -StartScenario $Start -WaitWindow $Window -Observe $Observe -Sleep { param($milliseconds) }
                    }
                    catch { return [string] $_.Exception.Message }
                    return 'none'
                }

                Assert-Equal 'single.instance.primary_close_rejected' (Get-CRRefusalCode -Find { param($id) $rejectProcess }) 'a rejected close must refuse by name'
                Assert-Equal 'single.instance.primary_close_rejected' (Get-CRRefusalCode -Find { param($id) $null }) 'a missing process cannot be closed'
                Assert-Equal 'single.instance.primary_close_timeout' (Get-CRRefusalCode -Wait 1 -Find { param($id) $liveProcess }) 'a close that never completes must time out'
                Assert-Equal 'single.instance.residue_after_close' (Get-CRRefusalCode -Inventory { @([pscustomobject] @{ Id = 1 }) }) 'residue after the close must refuse'
                Assert-Equal 'single.instance.restart_window_missing' (Get-CRRefusalCode -Window { param($id, $timeout) return [IntPtr]::Zero }) 'a restart without a window must refuse'
                Assert-Equal 'single.instance.restart_unresponsive' (Get-CRRefusalCode -Observe { param($id) return [pscustomobject] @{ Status = 'Failed' } }) 'a restart that does not respond must refuse'
            }
        },
        @{
            Name = 'the recorded secondary identity is what the cleanup chain re-checks'
            Body = {
                # The two seams the scenario relies on are driven in sequence: the real launch path
                # writes the identity ledger entry for the second start, and the real cleanup chain
                # re-checks pid, creation time and path before it stops anything. A record that does
                # not match is never stopped, and nothing is stopped by name.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-si-identity-' + [guid]::NewGuid().ToString('N'))
                try {
                    $root = Join-Path $base 'owned'
                    [void] [IO.Directory]::CreateDirectory($root)
                    $exe = Join-Path $base 'TraceFold.Media.exe'
                    [IO.File]::WriteAllBytes($exe, [byte[]] @(1))
                    $support = Join-Path $script:uiRoot 'tests/fixtures/fake-support.ps1'
                    $record = Start-TraceFoldScenario -ExecutablePath $exe -OwnedRoot $root -RunToken 'test-token' -SupportPath $support -StartProcess {
                        param($path, $childEnvironment)
                        [pscustomobject] @{ ProcessId = 4242; StartUtcTicks = 8484; ExecutablePath = $path }
                    }
                    Assert-Equal 4242 $record.ProcessId
                    Assert-Contains (Get-Content -LiteralPath (Join-Path $root 'owned-processes.txt') -Raw) '4242|8484|' 'the second start must be in the identity ledger'
                    $ledger = @([pscustomobject] @{ ProcessId = 4242; StartUtcTicks = 8484; ExecutablePath = $exe })

                    $script:identityStops = @()
                    $stop = { param($id) $script:identityStops += $id }
                    # A creation time that does not match the record: not ours to stop.
                    $mismatched = {
                        param($id)
                        [pscustomobject] @{ ProcessId = 4242; StartUtcTicks = 8485; ExecutablePath = $exe }
                    }
                    $stopped = @(Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($base) -Inventory $mismatched -Stop $stop)
                    Assert-Equal 0 $stopped.Count 'a creation time mismatch must not be stopped'
                    Assert-Equal 0 $script:identityStops.Count 'no stop may be requested on a mismatch'
                    # A path outside the approved directory: not stopped either.
                    $foreign = {
                        param($id)
                        [pscustomobject] @{ ProcessId = 4242; StartUtcTicks = 8484; ExecutablePath = (Join-Path ([IO.Path]::GetTempPath()) 'ordinary/TraceFold.Media.exe') }
                    }
                    $stopped = @(Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($base) -Inventory $foreign -Stop $stop)
                    Assert-Equal 0 $stopped.Count 'a path outside the approved directory must not be stopped'
                    # The recorded identity, re-checked exactly: now and only now the stop proceeds.
                    $matching = {
                        param($id)
                        [pscustomobject] @{ ProcessId = 4242; StartUtcTicks = 8484; ExecutablePath = $exe }
                    }
                    $stopped = @(Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($base) -Inventory $matching -Stop $stop)
                    Assert-Equal 1 $stopped.Count 'the recorded identity must be stopped'
                    Assert-Equal 4242 $stopped[0]
                    Assert-Equal 1 $script:identityStops.Count 'the stop must be requested exactly once'
                }
                finally {
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'the close and restart connections are ordered and carry the same root (auxiliary source check)'
            Body = {
                # An AUXILIARY source connection check, labelled as one: the close and restart
                # behaviour itself is driven by the unit case above, which runs the very unit the
                # scenario calls. This case only pins that the unit is wired into the main flow with
                # the same executable and the same owned root, and that the refusals it can produce
                # are reachable in that unit. It is not offered as the behaviour proof.
                $path = Join-Path $script:uiRoot 'scenarios/61-single-instance-safety.ps1'
                $source = Get-Content -LiteralPath $path -Raw
                foreach ($code in @('single.instance.primary_close_rejected', 'single.instance.primary_close_timeout',
                        'single.instance.residue_after_close', 'single.instance.restart_window_missing',
                        'single.instance.restart_unresponsive')) {
                    Assert-True ($source -cmatch $code) ($code + ' must be a reachable refusal')
                }
                # The unit closes the primary through its own window, and the restart goes through the
                # scenario's own launch seam with the same executable and the same owned root.
                Assert-True ($source -cmatch '\$primaryProcess\.CloseMainWindow\(\)') 'the primary must be closed through its own window'
                Assert-True ($source -cmatch '\$restartFacts = Invoke-SingleInstanceCloseAndRestart -PrimaryPid \$primaryPid -ExecutablePath \$exe -OwnedRoot \$root') 'the main flow must drive the unit with the same executable and the same owned root'
                Assert-True ($source -cmatch 'Start-TraceFoldScenario -ExecutablePath \$restartExe -OwnedRoot \$restartRoot -RunToken \$restartToken -SupportPath \$restartSupport -StartProcess \$LaunchProcess') 'the restart must start through the launch seam with the same root'
                # The order inside the unit: the close decision precedes the wait, the residue check
                # follows the confirmed exit, and the restart only starts after the root is clean; the
                # restart stage in the main flow is only claimed after the unit answered.
                $rejected = $source.IndexOf('single.instance.primary_close_rejected')
                $timeout = $source.IndexOf('single.instance.primary_close_timeout')
                $residue = $source.IndexOf('single.instance.residue_after_close')
                $restartCall = $source.IndexOf('$restart = & $StartScenario')
                $restartVerified = $source.IndexOf("`$scenarioStage = 'restart-verified'")
                Assert-True ($rejected -gt 0 -and $timeout -gt $rejected) 'the close rejection must be decided before the wait'
                Assert-True ($residue -gt $timeout) 'the residue check must follow the confirmed exit'
                Assert-True ($restartCall -gt $residue) 'the restart must start after the residue check'
                Assert-True ($restartVerified -gt $source.IndexOf('single.instance.restart_unresponsive')) 'the restart stage may only be claimed after the restart responded'
            }
        }
    )
}
