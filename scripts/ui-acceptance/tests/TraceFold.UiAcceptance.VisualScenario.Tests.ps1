# Task 13 visual matrix contract tests.
#
# Two layers, deliberately separated:
#   * behaviour: the shipped gate module is driven with counted stubs, and one controlled
#     integration case runs the real entry, Visual scenario, Runner, Evidence and report writers
#     from a test-owned source tree and moved package.
#   * structure: the scenario, entry, registry, template and support surface are read and parsed
#     to prove wiring that a local behaviour case does not reach.
#
# The integration fixture replaces only process/desktop/Driver and non-theme Support endpoints.
# Theme inspect/apply/restore are forwarded to the real Support CLI with its isolated fixture
# root and process licence. It starts no product and does not prove production adapters, a real
# window, real keyboard input, real layout or a real visual result.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'fixtures/visual-integration.ps1')

function Read-VisualScenarioSource {
    $path = Join-Path $script:uiRoot 'scenarios/70-visual-keyboard-matrix.ps1'
    if (-not [IO.File]::Exists($path)) { throw 'The visual matrix scenario is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Read-VisualTemplateSource {
    $path = Join-Path $script:uiRoot 'report-template/index.html'
    if (-not [IO.File]::Exists($path)) { throw 'The report template is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Read-VisualGateSource {
    $path = Join-Path $script:uiRoot 'TraceFold.UiAcceptance.VisualGate.psm1'
    if (-not [IO.File]::Exists($path)) { throw 'The visual gate module is missing.' }
    return [IO.File]::ReadAllText($path)
}

function Assert-VisualAscii {
    param([Parameter(Mandatory)][string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -gt 127) { throw ('Non-ASCII byte in ' + $Path + ' at ' + $index + '.') }
    }
}

<#
.SYNOPSIS
    Creates a journal file inside a test-owned temporary directory.

.DESCRIPTION
    The gate is pointed at this path, never at the real per-user journal, so a behaviour test can
    never disturb an interrupted real run.

    The directory is a complete fixture selection in its own right: the reviewed prefix, the
    adapter leaves and the journal directly inside the root. A private journal without that
    boundary is refused before a command runs, so a bare temporary file would only ever exercise
    the refusal and never the behaviour under test.
#>
function New-VisualGateJournalFile {
    $directory = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-theme-fixture-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($directory)
    [IO.File]::WriteAllText((Join-Path $directory 'registry.json'),
        '{"present":false,"kind":"DWord","bytesHex":"","failWrite":false,"failDelete":false}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $directory 'broadcast.json'), '{"fail":false,"count":0}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $directory 'probe.json'), '{"state":"live","identityReadable":true}',
        [Text.UTF8Encoding]::new($false))
    $path = Join-Path $directory 'theme-recovery.json'
    [IO.File]::WriteAllText($path, '{"schemaVersion":1}', [Text.UTF8Encoding]::new($false))
    return [pscustomobject] @{ Directory = $directory; Root = $directory; Path = $path }
}

<#
.SYNOPSIS
    Builds a counted support stub. The responder decides what each action answers.

.DESCRIPTION
    This is a data stub for the child process boundary only. It never re-implements a decision:
    every assertion below is made against the shipped module functions, and the counter is how a
    refusal proves it did not call anything else.
#>
function New-VisualGateStub {
    param([Parameter(Mandatory)][scriptblock] $Responder)

    $state = [pscustomobject] @{
        Calls = New-Object 'System.Collections.Generic.List[string]'
    }
    $stub = {
        param($SupportPath, $Arguments)
        $action = [string] $Arguments[1]
        [void] $state.Calls.Add($action)
        return (& $Responder $action $Arguments)
    }.GetNewClosure()
    return [pscustomobject] @{ State = $state; Stub = $stub }
}

function ConvertTo-VisualGateJson {
    param([Parameter(Mandatory)] $Value)
    return (ConvertTo-Json -InputObject $Value -Depth 8 -Compress)
}

<#
.SYNOPSIS
    Returns one function body from the visual scenario without running the scenario.

.DESCRIPTION
    Write-VisualMatrix is the shipped report writer, but the scenario is a script with top-level
    execution, so dot-sourcing the file would start a run. Parsing the file and taking the one
    definition of the named function exercises the real body while leaving the rest untouched. A
    file that does not parse, or a definition that is not unique, is an error rather than a guess.
#>
function Get-VisualScenarioFunction {
    param([Parameter(Mandatory)][string] $Name)

    $path = Join-Path $script:uiRoot 'scenarios/70-visual-keyboard-matrix.ps1'
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
    if (@($errors).Count -gt 0) { throw 'The visual scenario does not parse.' }
    $found = @($ast.FindAll({
                param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $Name
            }, $true))
    if ($found.Count -ne 1) { throw ('The scenario does not define exactly one ' + $Name + '.') }
    return [scriptblock]::Create($found[0].Extent.Text)
}

<#
.SYNOPSIS
    Publishes the shipped support CLI once per run and returns its executable.

.DESCRIPTION
    A stub child proves a stub, not the transport. The default-branch witness has to reach the
    real command surface, so this publishes the repository project into a test-owned temporary
    directory with the shared compiler and node reuse disabled: the build must not leave a
    compiler service behind for the next window to explain.

    The publish happens at most once per test file. A failed publish throws with the observed
    exit code instead of letting a zero-output run stand in for a built CLI.
#>
function Get-VisualGateRealSupportCli {
    $cached = Get-Variable -Name 'visualGateRealCli' -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $cached -and -not [string]::IsNullOrWhiteSpace([string] $cached.Value)) {
        return [string] $cached.Value
    }
    $project = Join-Path $script:root 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/TraceFold.Media.UiAcceptance.Support.csproj'
    if (-not [IO.File]::Exists($project)) { throw 'The support project is missing.' }
    $target = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visualgate-cli-' + [guid]::NewGuid().ToString('N'))
    $log = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visualgate-publish-' + [guid]::NewGuid().ToString('N') + '.log')
    $publishExit = $null
    try {
        # UseSharedCompilation is a property; node reuse is its own MSBuild switch, not a property.
        & dotnet publish $project -c Release -o $target --nologo `
            -p:UseSharedCompilation=false -nodeReuse:false *> $log
        $publishExit = $LASTEXITCODE
    }
    catch {
        throw ('The support CLI publish could not run: ' + [string] $_.Exception.Message)
    }
    if ($publishExit -ne 0) {
        $tail = ''
        if ([IO.File]::Exists($log)) { $tail = ([IO.File]::ReadAllText($log)).Trim() }
        if ($tail.Length -gt 800) { $tail = $tail.Substring($tail.Length - 800) }
        throw ('The support CLI publish failed with exit ' + [string] $publishExit + '. ' + $tail)
    }
    $exe = Join-Path $target 'TraceFold.Media.UiAcceptance.Support.exe'
    if (-not [IO.File]::Exists($exe)) { throw 'The published support CLI is missing.' }
    $script:visualGateRealCli = $exe
    return $exe
}

<#
.SYNOPSIS
    Creates an acceptance fixture root with the three leaves the adapters require.

.DESCRIPTION
    The root carries the reviewed prefix and lives under the temporary directory, so the CLI's own
    boundary checks accept it. The probe leaf decides what the owner probe answers; the registry
    and broadcast leaves start from a clean, writable state.
#>
function New-VisualGateFixtureRoot {
    param([ValidateSet('live', 'dead', 'pid-reused', 'unknown')][string] $OwnerState = 'live')

    $root = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-theme-fixture-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($root)
    [IO.File]::WriteAllText((Join-Path $root 'registry.json'),
        '{"present":false,"kind":"DWord","bytesHex":"","failWrite":false,"failDelete":false}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $root 'broadcast.json'), '{"fail":false,"count":0}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $root 'probe.json'),
        ('{"state":"' + $OwnerState + '","identityReadable":true}'), [Text.UTF8Encoding]::new($false))
    return $root
}

<#
.SYNOPSIS
    Runs the public entry as a child process of the current host.

.DESCRIPTION
    The entry ends with exit, so it cannot be called in-process inside a test body. The child is
    the same host executable the suite is already running under, which keeps the case identical
    on both hosts. Arguments are quoted because Windows PowerShell has no ArgumentList.

    The reads are asynchronous and share the one declared budget with the exit wait: a synchronous
    ReadToEnd would sit outside the timeout and could hold the suite open indefinitely. The process
    object is disposed in a finally, and a child that outlives the budget is killed as a known
    child of this call, never by name.
#>
function Invoke-VisualEntryChild {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [int] $TimeoutMs = 180000
    )

    $hostExe = (Get-Process -Id $PID).Path
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $hostExe
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $true
    # Only tokens that need it are quoted: a quoted "-Switch" would arrive as a string and fail to
    # bind as a switch, while a path with a space must keep its quotes.
    $quoted = ($Arguments | ForEach-Object {
            $text = [string] $_
            if ($text -eq '' -or $text -match '\s') { '"' + $text + '"' } else { $text }
        }) -join ' '
    $start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + $quoted
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    $budget = [Diagnostics.Stopwatch]::StartNew()
    try {
        [void] $process.Start()
        try { $process.StandardInput.Close() } catch { }
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        $remaining = [Math]::Max(0, $TimeoutMs - [int] $budget.ElapsedMilliseconds)
        if (-not $process.WaitForExit($remaining)) {
            try { [void] $process.Kill($true) } catch { try { [void] $process.Kill() } catch { } }
            throw ('The entry did not exit within ' + [string] $TimeoutMs + ' ms.')
        }
        $remaining = [Math]::Max(1, $TimeoutMs - [int] $budget.ElapsedMilliseconds)
        if (-not $outTask.Wait($remaining)) { throw 'The entry standard output did not drain within its budget.' }
        if (-not $errTask.Wait($remaining)) { throw 'The entry standard error did not drain within its budget.' }
        return [pscustomobject] @{
            ExitCode = $process.ExitCode; Stdout = [string] $outTask.Result; Stderr = [string] $errTask.Result
        }
    }
    finally {
        $process.Dispose()
    }
}

function New-VisualGateCommandResult {
    param([AllowNull()] $ExitCode, [string] $Stdout = '', [string] $Stderr = '')
    return [pscustomobject] @{ ExitCode = $ExitCode; Stdout = $Stdout; Stderr = $Stderr }
}

function New-VisualGateIdentity {
    param(
        [int] $ProcessId = 4242,
        [long] $StartUtcTicks = 638000000000000000,
        [string] $Context = 'ctx-visual-1',
        [string] $State = 'captured'
    )
    return [pscustomobject] @{
        state = $State; error = ''; pid = $ProcessId; startUtcTicks = $StartUtcTicks; context = $Context
    }
}

function New-VisualGateBinding {
    param(
        [string] $ProductSha256 = ('A' * 64),
        [string] $ToolkitSha256 = ('B' * 64),
        [string] $State = 'captured'
    )
    return [pscustomobject] @{
        state = $State; error = ''
        productHashKind = 'sha256'; productMembers = 1; productSha256 = $ProductSha256; productBytes = 1000
        productMemberNames = @('TraceFold.Media.exe')
        toolkitHashKind = 'sha256'; toolkitMembers = 2; toolkitSha256 = $ToolkitSha256
        toolkitMemberNames = @('visual-gate', 'support')
        members = @()
    }
}

function New-VisualGateApplyBody {
    param(
        [string] $Mode = 'light',
        [object] $Applied = $true,
        [object] $JournalState = 'pending',
        [object] $RegistryWritten = $true,
        [object] $BroadcastSent = $true,
        [string] $HandbackToken = ('C' * 64),
        [object] $JournalWritten = $null,
        [string] $Adapters = 'production',
        [int] $OwnerPid = 4242,
        [long] $OwnerStartUtcTicks = 638000000000000000,
        [string] $OwnerContext = 'ctx-visual-1',
        [string] $ProductSha256 = ('A' * 64),
        [string] $ToolkitSha256 = ('B' * 64),
        [bool] $IncludeOwner = $true,
        [bool] $IncludeBinding = $true
    )
    $written = $(if ($null -ne $JournalWritten) { [bool] $JournalWritten } else { $JournalState -ceq 'pending' })
    $body = [ordered] @{
        adapters = $Adapters
        applied = $Applied
        mode = $Mode
        journalState = $JournalState
        journalWritten = $written
        registryWritten = $RegistryWritten
        broadcast = [ordered] @{ sent = $BroadcastSent }
        original = [ordered] @{ present = $true; valueKind = 'DWord'; bytes = '00000000' }
        handbackToken = $(if ($written) { $HandbackToken } else { '' })
    }
    if ($IncludeOwner) {
        $body['owner'] = [ordered] @{
            pid = $OwnerPid; startUtcTicks = $OwnerStartUtcTicks; context = $OwnerContext
        }
    }
    if ($IncludeBinding) {
        $body['binding'] = [ordered] @{
            productHashKind = 'sha256'; productMembers = 1; productSha256 = $ProductSha256
            productMemberNames = @('TraceFold.Media.exe')
            toolkitHashKind = 'sha256'; toolkitMembers = 2; toolkitSha256 = $ToolkitSha256
            toolkitMemberNames = @('visual-gate', 'support')
        }
    }
    return $body
}

function New-VisualGateRestoreBody {
    param(
        [object] $Restored = $true,
        [object] $NoAction = $false,
        [string] $JournalState = 'absent',
        [object] $MatchesOriginal = $true,
        [object] $BroadcastSent = $true,
        [string] $Disposition = 'handback',
        [string] $OwnerState = '',
        [object] $JournalRemoved = $true,
        [object] $JournalPresent = $true,
        [object] $JournalChecksumValid = $true,
        [object] $RegistryRestored = $true,
        [object] $BindingMatchesJournal = $true,
        [string] $Adapters = 'production',
        [int] $OwnerPid = 4242,
        [long] $OwnerStartUtcTicks = 638000000000000000,
        [string] $OwnerContext = 'ctx-visual-1',
        [bool] $IncludeOwner = $true
    )
    $state = $(if (-not [string]::IsNullOrWhiteSpace($OwnerState)) { $OwnerState }
        elseif ($Disposition -ceq 'handback') { 'live' } else { 'dead' })
    $body = [ordered] @{
        adapters = $Adapters
        disposition = $Disposition
        restored = $Restored
        noAction = $NoAction
        journalState = $JournalState
        journalPresent = $JournalPresent
        journalRemoved = $JournalRemoved
        journalChecksumValid = $JournalChecksumValid
        ownerState = $state
        original = [ordered] @{ present = $true; valueKind = 'DWord'; bytes = '00000000' }
        readback = [ordered] @{
            present = $true; valueKind = 'DWord'; bytes = '00000000'; matchesOriginal = $MatchesOriginal
        }
        broadcast = [ordered] @{ sent = $BroadcastSent }
        registryRestored = $RegistryRestored
        bindingMatchesJournal = $BindingMatchesJournal
    }
    if ($IncludeOwner) {
        $body['owner'] = [ordered] @{
            pid = $OwnerPid; startUtcTicks = $OwnerStartUtcTicks; context = $OwnerContext
        }
    }
    return $body
}

function New-VisualGateInspectBody {
    param(
        [string] $JournalState = 'restore-required',
        [string] $OwnerState = 'dead',
        [string] $Adapters = 'production',
        [bool] $StartupRecoveryAllowed = $false,
        [bool] $HandbackAccepted = $false,
        [object] $HandbackRejection = $null
    )
    return [ordered] @{
        adapters = $Adapters
        journalState = $JournalState
        ownerState = $OwnerState
        startupRecoveryAllowed = $StartupRecoveryAllowed
        handbackAccepted = $HandbackAccepted
        handbackRejection = $HandbackRejection
    }
}

# The exact identity and binding every apply/restore case expects. They are values, not a stub of
# the implementation: the module compares the response echo against these facts.
$script:visualGateTestIdentity = New-VisualGateIdentity
$script:visualGateTestBinding = New-VisualGateBinding

function Get-VisualCommandAst {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Name)

    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref] $tokens, [ref] $errors)
    if (@($errors).Count -gt 0) { throw ('The file does not parse: ' + $Path) }
    return @($ast.FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -and
                [string] $node.GetCommandName() -ceq $Name
            }, $true))
}

<#
.SYNOPSIS
    Runs the real visual scenario top level once for one theme-matrix mode against a fixture.
#>
function Invoke-VisualMatrixBoundaryRun {
    param(
        [Parameter(Mandatory)][string] $Mode,
        [Parameter(Mandatory)][string] $Scenario,
        [Parameter(Mandatory)][string] $Product,
        [Parameter(Mandatory)][string] $Contract,
        [Parameter(Mandatory)][string] $Stub,
        [Parameter(Mandatory)][string] $Log,
        [Parameter(Mandatory)][string] $Base
    )

    $fixture = New-VisualGateFixtureRoot -OwnerState 'dead'
    $journal = Join-Path $fixture 'theme-recovery.json'
    [IO.File]::WriteAllText($journal, '{"schemaVersion":1}', [Text.UTF8Encoding]::new($false))
    $before = (Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash
    if ([IO.File]::Exists($Log)) { Remove-Item -LiteralPath $Log -Force }
    $owned = Join-Path $Base ('owned-' + $Mode)
    $out = Join-Path $Base ('out-' + $Mode)
    [void][IO.Directory]::CreateDirectory($owned)
    [void][IO.Directory]::CreateDirectory($out)
    $outcome = $null
    $errorText = ''
    try {
        $outcome = & $Scenario -ScenarioId 'visual' -ExecutablePath $Product -ContractPath $Contract `
            -OwnedRoot $owned -RunToken ('T' * 32) -OutputDirectory $out -SupportPath $Stub `
            -WinAppPath (Join-Path $Base 'winapp.exe') -ThemeMatrix $Mode -FixtureRoot $fixture -AllowFixture `
            -ThemeJournalPath $journal
    }
    catch { $errorText = [string] $_.Exception.Message }
    $calls = @()
    if ([IO.File]::Exists($Log)) { $calls = @(Get-Content -LiteralPath $Log) }
    return [pscustomobject] @{
        Mode = $Mode
        Outcome = $outcome
        ErrorText = $errorText
        Calls = $calls
        JournalBefore = $before
        JournalAfter = $(if ([IO.File]::Exists($journal)) { (Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash } else { '' })
        JournalExists = [IO.File]::Exists($journal)
        Fixture = $fixture
        Output = $out
    }
}

function Get-TestCases {
    return @(
        @{
            Name = 'the visual scenario parses and stays ASCII for both PowerShell hosts'
            Body = {
                $source = Read-VisualScenarioSource
                $tokens = $null
                $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
                Assert-Equal 0 @($errors).Count
                Assert-VisualAscii -Path (Join-Path $script:uiRoot 'scenarios/70-visual-keyboard-matrix.ps1')
                Assert-VisualAscii -Path (Join-Path $script:uiRoot 'report-template/index.html')
                Assert-VisualAscii -Path (Join-Path $script:uiRoot 'TraceFold.UiAcceptance.VisualGate.psm1')
            }
        },
        @{
            Name = 'the visual registry entry points at the Task 13 file and not the old missing file'
            Body = {
                $path = Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Runner.psm1'
                $source = [IO.File]::ReadAllText($path)
                Assert-Contains $source "Visual        = '70-visual-keyboard-matrix.ps1'"
                Assert-True ($source.IndexOf("'06-visual.ps1'", [StringComparison]::Ordinal) -lt 0) `
                    'The missing 06 entry is still registered.'
            }
        },
        @{
            Name = 'the public entry exposes ThemeMatrix and hands it to entries that declare it'
            Body = {
                $path = Join-Path $script:root 'scripts/run-stage5cc-ui-acceptance.ps1'
                $source = [IO.File]::ReadAllText($path)
                # Brackets are wildcard syntax inside Assert-Contains, so these are ordinal checks.
                Assert-True ($source.IndexOf("[ValidateSet('Both','CurrentOnly')]", [StringComparison]::Ordinal) -ge 0) `
                    'The entry does not declare the ThemeMatrix set.'
                Assert-True ($source.IndexOf("[string] `$ThemeMatrix = 'Both'", [StringComparison]::Ordinal) -ge 0) `
                    'The entry does not default ThemeMatrix to Both.'
                Assert-Contains $source 'ThemeMatrix       = $ThemeMatrix'
            }
        },
        @{
            Name = 'the entry asks the theme journal gate before any preflight, publish or product launch'
            Body = {
                $path = Join-Path $script:root 'scripts/run-stage5cc-ui-acceptance.ps1'
                $gateCalls = @(Get-VisualCommandAst -Path $path -Name 'Invoke-UiAcceptanceThemeJournalGate')
                Assert-Equal 1 $gateCalls.Count
                $gateOffset = [int] $gateCalls[0].Extent.StartOffset

                foreach ($probe in @(
                    'Test-UiAcceptanceRequestCompleteness',
                    'New-UiAcceptanceRunRoot',
                    'Invoke-UiAcceptanceRun',
                    'Invoke-UiAcceptanceScenario'
                )) {
                    $calls = @(Get-VisualCommandAst -Path $path -Name $probe)
                    Assert-True ($calls.Count -ge 1) ('The entry never calls ' + $probe + '.')
                    Assert-True ([int] $calls[0].Extent.StartOffset -gt $gateOffset) `
                        ('The journal gate runs after ' + $probe + '.')
                }

                # 'dotnet publish' is the support publish: it must come after the gate too.
                $publish = @(Get-VisualCommandAst -Path $path -Name 'dotnet' | Where-Object {
                    [string] $_.Extent.Text -like '*publish*'
                })
                Assert-Equal 1 $publish.Count
                Assert-True ([int] $publish[0].Extent.StartOffset -gt $gateOffset) `
                    'The support publish runs before the journal gate.'

                # The refusal is a real exit before the preflight, not a later status field.
                $tokens = $null
                $errors = $null
                $ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref] $tokens, [ref] $errors)
                $preflightCalls = @(Get-VisualCommandAst -Path $path -Name 'Test-UiAcceptanceRequestCompleteness')
                $preflightOffset = [int] $preflightCalls[0].Extent.StartOffset
                $guards = @($ast.FindAll({
                            param($node)
                            $node -is [Management.Automation.Language.IfStatementAst] -and
                            [string] $node.Clauses[0].Item1.Extent.Text -like '*themeJournalGate.state*' -and
                            [int] $node.Extent.StartOffset -gt $gateOffset -and
                            [int] $node.Extent.EndOffset -lt $preflightOffset
                        }, $true))
                Assert-Equal 1 $guards.Count
                $exitInside = @($guards[0].FindAll({
                            param($node)
                            $node -is [Management.Automation.Language.ExitStatementAst]
                        }, $true))
                Assert-Equal 1 $exitInside.Count
            }
        },
        @{
            Name = 'Both is the full matrix and CurrentOnly is never a passed visual run'
            Body = {
                $source = Read-VisualScenarioSource
                Assert-Contains $source "if (`$ThemeMatrix -ceq 'Both')"
                Assert-Contains $source "`$themeModes = @('light','dark')"
                Assert-Contains $source "if (`$ThemeMatrix -ceq 'CurrentOnly') { `$themeModes = @('current') }"
                Assert-Contains $source "`$status = 'NotRun'"
                Assert-Contains $source 'visual.theme_matrix_current_only'
                # One apply per theme, inside the Both branch; CurrentOnly never reaches it.
                $applies = [regex]::Matches($source, 'Invoke-UiAcceptanceThemeApplyPhase -Round')
                Assert-Equal 1 @($applies).Count
                # The old bare command wrapper with an [int] exit cast is gone.
                Assert-True ($source.IndexOf('function Invoke-ThemeCommand', [StringComparison]::Ordinal) -lt 0) `
                    'The scenario still owns a second theme command wrapper.'
            }
        },
        @{
            Name = 'the matrix declares both themes, four pages and the contract declares the three DIP viewports'
            Body = {
                $source = Read-VisualScenarioSource
                foreach ($token in @(
                    "`$themeModes = @('light','dark')",
                    "@('inbox', 'library', 'taxonomy', 'settings')",
                    'Get-UiAcceptanceRequiredViewport',
                    'layoutViewports'
                )) {
                    Assert-Contains $source $token
                }
                # The viewports are declared by the contract the scenario consumes, not hardcoded in
                # the scenario. Assert the declaration itself, so a missing or altered viewport is
                # caught where it is defined.
                $contractPath = Join-Path $script:root 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
                # Read as UTF-8 bytes: Get-Content -Raw decodes this file with the console code page
                # on Windows PowerShell 5.1, which mangles the non-ASCII names it carries.
                $contract = [IO.File]::ReadAllText($contractPath) | ConvertFrom-Json
                $declared = @($contract.layoutViewports)
                Assert-Equal 3 $declared.Count
                foreach ($pair in @(@(1024, 720), @(1280, 800), @(1600, 900))) {
                    $matches = @($declared | Where-Object {
                            [int] $_.widthDip -eq [int] $pair[0] -and [int] $_.heightDip -eq [int] $pair[1]
                        })
                    Assert-Equal 1 $matches.Count
                }
            }
        },
        @{
            Name = 'every state and dialog named by the plan is materialised by the scenario'
            Body = {
                $source = Read-VisualScenarioSource
                foreach ($state in @(
                    'empty-selection', 'processing-queued', 'processing-ready',
                    'disabled-primary-action', 'drawer-open', 'drawer-closed',
                    'validation-error', 'diagnostics-running', 'diagnostics-complete',
                    'folder-form'
                )) {
                    Assert-Contains $source ("'" + $state + "'")
                }
                foreach ($dialog in @(
                    'BatchMetadataDialog', 'ConflictComparisonDialog',
                    'LibraryBatchEditDialog', 'TaxonomyMergeDialog'
                )) {
                    Assert-Contains $source $dialog
                }
            }
        },
        @{
            Name = 'the keyboard path focuses an owned control and reads UIA focus after every key'
            Body = {
                $source = Read-VisualScenarioSource
                Assert-Contains $source 'Invoke-WinAppKeyboard'
                Assert-Contains $source "'ui','send-keys'"
                foreach ($key in @('tab', 'shift+tab', 'down', 'up', 'enter', 'space', 'escape')) {
                    Assert-Contains $source $key
                }
                Assert-Contains $source 'Assert-VisualFocusReadable'
                Assert-Contains $source 'Assert-VisualFocusInside'
                Assert-Contains $source 'Assert-VisualFocusOnRow'
                # The focus read must live inside the key loop, not only at the drawer.
                $loop = $source.IndexOf('foreach ($key in @(''tab'',''shift+tab''', [StringComparison]::Ordinal)
                $focus = $source.IndexOf('Assert-VisualFocusReadable', $loop, [StringComparison]::Ordinal)
                Assert-True ($loop -ge 0 -and $focus -gt $loop) 'The per-key focus read is missing.'
            }
        },
        @{
            Name = 'the dialog and page assertions check bounds and the scroll pattern, not only screenshots'
            Body = {
                $source = Read-VisualScenarioSource
                Assert-Contains $source 'Assert-VisualBounds'
                Assert-Contains $source 'Assert-VisualPageLayout'
                Assert-Contains $source '--pattern'
                Assert-Contains $source 'Scroll'
                Assert-Contains $source 'Write-UiRedactedTreeEvidence'
                Assert-Contains $source 'Assert-UiPngBytes'
                # A screenshot must never be the only producer of a passed entry.
                Assert-Contains $source 'Assert-VisualPageLayout -Page $page'
            }
        },
        @{
            Name = 'the journal path and the gate are the shared implementation, not a scenario copy'
            Body = {
                $gate = Read-VisualGateSource
                Assert-Contains $gate "'TraceFold.UiAcceptance\theme-recovery.json'"
                Assert-Contains $gate 'LocalApplicationData'
                Assert-Contains $gate 'theme_coordinator_not_integrated'
                $source = Read-VisualScenarioSource
                Assert-Contains $source 'Get-UiAcceptanceThemeJournalPath'
                Assert-Contains $source 'Invoke-UiAcceptanceThemeJournalGate'
                Assert-Contains $source 'Invoke-UiAcceptanceThemeApplyPhase'
                Assert-Contains $source 'Invoke-UiAcceptanceThemeRestorePhase'
                Assert-Contains $source 'visual.theme_journal_blocked'
                Assert-Contains $source 'Write-UiThemeEvidence'
                # The gate is called before the first theme is applied.
                $gateCall = $source.IndexOf('Invoke-UiAcceptanceThemeJournalGate', [StringComparison]::Ordinal)
                $applyCall = $source.IndexOf('Invoke-UiAcceptanceThemeApplyPhase', [StringComparison]::Ordinal)
                Assert-True ($gateCall -ge 0 -and $applyCall -gt $gateCall) 'The scenario applies before it gates.'
            }
        },
        @{
            Name = 'the report template is local-only and carries the embedded matrix data'
            Body = {
                $template = Read-VisualTemplateSource
                Assert-Contains $template '__SCREENSHOTS_JSON__'
                Assert-Contains $template 'id="screenshots-data"'
                Assert-True ($template.IndexOf('http://', [StringComparison]::OrdinalIgnoreCase) -lt 0) `
                    'The template references an HTTP resource.'
                Assert-True ($template.IndexOf('https://', [StringComparison]::OrdinalIgnoreCase) -lt 0) `
                    'The template references an HTTPS resource.'
                Assert-True ($template.IndexOf('//cdn', [StringComparison]::OrdinalIgnoreCase) -lt 0) `
                    'The template references a CDN.'
                # One writer, one outlet: the scenario returns its matrix and never writes the
                # index itself. The template is consumed by the outlet's writer, which validates
                # every link, redacts once and refuses an occupied target.
                $scenario = Read-VisualScenarioSource
                Assert-True ($scenario.IndexOf("'screenshots.json'", [StringComparison]::Ordinal) -lt 0) `
                    'The visual scenario still writes its own screenshots index.'
                Assert-True ($scenario.IndexOf("'index.html'", [StringComparison]::Ordinal) -lt 0) `
                    'The visual scenario still writes its own index page.'
                Assert-Contains $scenario 'New-VisualMatrix'
                Assert-Contains $scenario 'Assert-UiAcceptanceNoCapabilityValue'
                $evidence = [IO.File]::ReadAllText((Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Evidence.psm1'))
                Assert-Contains $evidence 'function Write-UiVisualIndex'
                Assert-Contains $evidence '__SCREENSHOTS_JSON__'
            }
        },
        @{
            Name = 'the support theme surface is one coordinator call, not a second implementation'
            Body = {
                $path = Join-Path $script:root 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/Program.cs'
                $source = [IO.File]::ReadAllText($path)
                Assert-Contains $source '"theme" => RunTheme(args, output, input)'
                Assert-Contains $source 'WindowsAppThemeCoordinator.Apply('
                Assert-Contains $source 'WindowsAppThemeCoordinator.Restore('
                Assert-Contains $source 'WindowsAppThemeCoordinator.Inspect('
                Assert-Contains $source 'handbackToken = result.HandbackToken'
                Assert-Contains $source 'The hand-back receipt is read from standard input only.'
                # The stage must not reach the registry from the support CLI; the coordinator owns it.
                Assert-True ($source.IndexOf('Microsoft.Win32', [StringComparison]::Ordinal) -lt 0) `
                    'The support CLI reads the registry directly.'
                Assert-True ($source.IndexOf('RegistryKey', [StringComparison]::Ordinal) -lt 0) `
                    'The support CLI owns registry keys directly.'
                # A component record must never be serialised as the consumer response.
                Assert-True ($source.IndexOf('JsonSerializer.Serialize(result', [StringComparison]::Ordinal) -lt 0) `
                    'The support CLI serialises a component record instead of a projection.'
                Assert-True ($source.IndexOf('"ownerToken"', [StringComparison]::Ordinal) -lt 0) `
                    'The support CLI still emits the retired ownerToken field.'
            }
        },
        @{
            Name = 'the self-test runner registers the visual suite and imports the gate module'
            Body = {
                $path = Join-Path $script:root 'scripts/test-stage5cc-ui-acceptance-toolkit.ps1'
                $source = [IO.File]::ReadAllText($path)
                Assert-True ($source.IndexOf('Visual = @(', [StringComparison]::Ordinal) -ge 0) `
                    'The runner does not register the visual suite.'
                Assert-Contains $source "'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.VisualScenario.Tests.ps1'"
                Assert-Contains $source "'VisualGate'"
            }
        },
        @{
            Name = 'the scenario does not use forbidden shortcuts or machine paths'
            Body = {
                $source = Read-VisualScenarioSource
                foreach ($forbidden in @(
                    'Start-Sleep', 'Stop-Process', "'ui','click'", 'sqlite3',
                    'UPDATE media_assets', 'INSERT INTO media_assets',
                    'C:\Users', 'https://', 'http://'
                )) {
                    Assert-True ($source.IndexOf($forbidden, [StringComparison]::OrdinalIgnoreCase) -lt 0) `
                        ('Forbidden shortcut found: ' + $forbidden)
                }
                Assert-True ($source.IndexOf('ShowWindowAsync', [StringComparison]::Ordinal) -lt 0) `
                    'The scenario uses a forbidden window activation API.'
                Assert-True ($source.IndexOf('SetForegroundWindow', [StringComparison]::Ordinal) -lt 0) `
                    'The scenario activates another window directly.'
            }
        },

        # --- behaviour: the journal gate -------------------------------------------------------
        @{
            Name = 'gate: no journal file is clear and makes no support call at all'
            Body = {
                $root = New-VisualGateFixtureRoot
                $journal = Join-Path $root 'theme-recovery.json'
                try {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        throw ('the gate called support for a missing journal: ' + $action)
                    }
                    # A complete selection whose journal is missing: the only clear this gate may
                    # answer without asking support.
                    $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal `
                        -FixtureRoot $root -AllowFixture -InvokeSupport $probe.Stub
                    Assert-Equal 'clear' ([string] $gate.state)
                    Assert-Equal 0 ([int] @($gate.calls).Count)
                    Assert-Equal 0 ([int] $probe.State.Calls.Count)
                    Assert-Equal $false ([bool] $gate.supportUsed)
                }
                finally { Remove-UiTestTree -Path $root }
            }
        },
        @{
            Name = 'gate: a read-only start refuses a dead-owner journal without restoring it'
            Body = {
                $fixture = New-VisualGateFixtureRoot -OwnerState 'dead'
                $journal = Join-Path $fixture 'theme-recovery.json'
                [IO.File]::WriteAllText($journal, '{"schemaVersion":1}', [Text.UTF8Encoding]::new($false))
                $before = (Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash
                $calls = New-Object 'System.Collections.ArrayList'
                $responder = {
                    param($SupportPath, $Arguments, $StandardInput)
                    $action = [string] $Arguments[1]
                    [void] $calls.Add($action)
                    if ($action -ceq 'restore') {
                        # A restore answer is provided on purpose: if the read-only start asked
                        # for one, the call list and the journal would show it.
                        return [pscustomobject] @{
                            ExitCode = 0; Stderr = ''
                            Stdout = '{"adapters":"fixture","disposition":"startup","restored":true,"noAction":false,"journalState":"absent","journalPresent":true,"journalRemoved":true,"journalChecksumValid":true,"ownerState":"dead","original":{"present":true,"valueKind":"DWord","bytes":"00000000"},"readback":{"present":true,"valueKind":"DWord","bytes":"00000000","matchesOriginal":true},"broadcast":{"sent":true},"registryRestored":true,"bindingMatchesJournal":true}'
                        }
                    }
                    return [pscustomobject] @{
                        ExitCode = 0; Stderr = ''
                        Stdout = '{"adapters":"fixture","journalState":"restore-required","ownerState":"dead","startupRecoveryAllowed":false,"handbackAccepted":false,"handbackRejection":null}'
                    }
                }.GetNewClosure()
                $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal -SupportPath 'stub' `
                    -FixtureRoot $fixture -AllowFixture -AllowRestore $false -InvokeSupport $responder
                Assert-Equal 'blocked' ([string] $gate.state) 'a read-only start must block on a pending journal'
                Assert-Equal 'theme_journal_restore_not_allowed' ([string] $gate.reason)
                Assert-Equal 1 (@($calls).Count) ('only the classification call may run: ' + (@($calls) -join ','))
                Assert-Equal 'inspect' ([string] @($calls)[0])
                Assert-Equal $before ((Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash) 'the journal must be untouched'
                Assert-True ($null -eq $gate.restore) 'no restore decision may exist for a read-only start'
                Assert-Equal 'dead' ([string] $gate.ownerState) 'the classification itself must still be recorded'
            }
        },
        @{
            Name = 'gate: a fixture request that hears production is refused and the journal stays'
            Body = {
                $fixture = New-VisualGateFixtureRoot -OwnerState 'dead'
                $journal = Join-Path $fixture 'theme-recovery.json'
                [IO.File]::WriteAllText($journal, '{"schemaVersion":1}', [Text.UTF8Encoding]::new($false))
                $calls = New-Object 'System.Collections.ArrayList'
                $responder = {
                    param($SupportPath, $Arguments, $StandardInput)
                    $action = [string] $Arguments[1]
                    [void] $calls.Add([string] $action)
                    if ($action -ceq 'restore') {
                        # The command claims a successful restore, but from the wrong adapter: a
                        # fixture-bound run must not accept a production receipt.
                        return [pscustomobject] @{
                            ExitCode = 0; Stderr = ''
                            Stdout = '{"adapters":"production","disposition":"startup","restored":true,"noAction":false,"journalState":"absent","journalPresent":true,"journalRemoved":true,"journalChecksumValid":true,"ownerState":"dead","original":{"present":true,"valueKind":"DWord","bytes":"00000000"},"readback":{"present":true,"valueKind":"DWord","bytes":"00000000","matchesOriginal":true},"broadcast":{"sent":true},"registryRestored":true,"bindingMatchesJournal":true}'
                        }
                    }
                    return [pscustomobject] @{
                        ExitCode = 0; Stderr = ''
                        Stdout = '{"adapters":"fixture","journalState":"restore-required","ownerState":"dead","startupRecoveryAllowed":false,"handbackAccepted":false,"handbackRejection":null}'
                    }
                }.GetNewClosure()
                $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal -SupportPath 'stub' `
                    -FixtureRoot $fixture -AllowFixture -EnforceAdapterSelection -InvokeSupport $responder
                Assert-Equal 'blocked' ([string] $gate.state) 'a mismatched adapter must block the start'
                Assert-Equal 'theme_journal_recovery_failed' ([string] $gate.reason)
                Assert-Equal 'adapters.mismatch' ([string] $gate.restore.failure)
                # No readback followed the refused restore, and the journal is untouched.
                Assert-Equal 2 (@($calls).Count) ('the refused restore must stop the gate: ' + (@($calls) -join ','))
                Assert-True ([IO.File]::Exists($journal)) 'the refused restore must keep the journal'
            }
        },
        @{
            Name = 'gate: the final startup readback must still match the selected adapter'
            Body = {
                $fixture = New-VisualGateFixtureRoot -OwnerState 'dead'
                $journal = Join-Path $fixture 'theme-recovery.json'
                [IO.File]::WriteAllText($journal, '{"schemaVersion":1}', [Text.UTF8Encoding]::new($false))
                $calls = New-Object 'System.Collections.ArrayList'
                $state = [pscustomobject] @{ inspects = 0 }
                $responder = {
                    param($SupportPath, $Arguments, $StandardInput)
                    $action = [string] $Arguments[1]
                    [void] $calls.Add($action)
                    if ($action -ceq 'restore') {
                        # The restore itself is a valid fixture recovery and closes the journal.
                        # Only the following readback lies about which adapter produced it.
                        if ([IO.File]::Exists($journal)) { Remove-Item -LiteralPath $journal -Force }
                        return [pscustomobject] @{
                            ExitCode = 0; Stderr = ''
                            Stdout = '{"adapters":"fixture","disposition":"startup","restored":true,"noAction":false,"journalState":"absent","journalPresent":true,"journalRemoved":true,"journalChecksumValid":true,"ownerState":"dead","original":{"present":true,"valueKind":"DWord","bytes":"00000000"},"readback":{"present":true,"valueKind":"DWord","bytes":"00000000","matchesOriginal":true},"broadcast":{"sent":true},"registryRestored":true,"bindingMatchesJournal":true}'
                        }
                    }
                    $state.inspects++
                    $adapter = if ($state.inspects -eq 1) { 'fixture' } else { 'production' }
                    $journalState = if ($state.inspects -eq 1) { 'restore-required' } else { 'absent' }
                    $ownerState = if ($state.inspects -eq 1) { 'dead' } else { 'none' }
                    return [pscustomobject] @{
                        ExitCode = 0; Stderr = ''
                        Stdout = ('{"adapters":"' + $adapter + '","journalState":"' + $journalState +
                            '","ownerState":"' + $ownerState + '","startupRecoveryAllowed":false,' +
                            '"handbackAccepted":false,"handbackRejection":null}')
                    }
                }.GetNewClosure()
                $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal -SupportPath 'stub' `
                    -FixtureRoot $fixture -AllowFixture -EnforceAdapterSelection -InvokeSupport $responder
                Assert-Equal 'blocked' ([string] $gate.state)
                Assert-Equal 'theme_journal_recovery_unconfirmed' ([string] $gate.reason)
                Assert-Equal 'inspect,restore,inspect' (@($calls) -join ',')
                Assert-Equal $true ([bool] $gate.restore.ok) 'the valid restore must remain recorded'
                Assert-Equal 'adapters.mismatch' ([string] $gate.readback.failure)
                Assert-Equal $false ([IO.File]::Exists($journal)) `
                    'the fixture restore really closed the journal; the readback mismatch is the only defect'
            }
        },
        @{
            Name = 'gate: an unintegrated coordinator blocks before any restore is attempted'
            Body = {
                $journal = New-VisualGateJournalFile
                try {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        return (New-VisualGateCommandResult -ExitCode 10 -Stdout '{"error":"theme_coordinator_not_integrated","errorCode":"theme_coordinator_not_integrated"}')
                    }
                    $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal.Path -FixtureRoot $journal.Root -AllowFixture -InvokeSupport $probe.Stub
                    Assert-Equal 'blocked' ([string] $gate.state)
                    Assert-Equal 'theme_journal_gate_unavailable' ([string] $gate.reason)
                    Assert-Equal 'inspect' (@($probe.State.Calls) -join ',')
                    Assert-True ([IO.File]::Exists($journal.Path)) 'The gate deleted a journal it did not restore.'
                }
                finally { Remove-UiTestTree -Path $journal.Directory }
            }
        },
        @{
            Name = 'gate: an unreadable inspect answer blocks and never guesses a journal state'
            Body = {
                $journal = New-VisualGateJournalFile
                try {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout 'this is not json')
                    }
                    $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal.Path -FixtureRoot $journal.Root -AllowFixture -InvokeSupport $probe.Stub
                    Assert-Equal 'blocked' ([string] $gate.state)
                    Assert-Equal 'theme_journal_gate_inspect_failed' ([string] $gate.reason)
                    Assert-Equal 'inspect' (@($probe.State.Calls) -join ',')
                }
                finally { Remove-UiTestTree -Path $journal.Directory }
            }
        },
        @{
            Name = 'gate: an unknown exit is not read as success and blocks'
            Body = {
                $journal = New-VisualGateJournalFile
                try {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        return (New-VisualGateCommandResult -ExitCode $null -Stdout '')
                    }
                    $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal.Path -FixtureRoot $journal.Root -AllowFixture -InvokeSupport $probe.Stub
                    Assert-Equal 'blocked' ([string] $gate.state)
                    Assert-Equal 'theme_journal_gate_inspect_failed' ([string] $gate.reason)
                    Assert-Equal 'inspect' (@($probe.State.Calls) -join ',')
                }
                finally { Remove-UiTestTree -Path $journal.Directory }
            }
        },
        @{
            Name = 'gate: the exported gate facts are serialisable and carry no machine path'
            Body = {
                $root = New-VisualGateFixtureRoot
                $journal = Join-Path $root 'theme-recovery.json'
                try {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        throw ('the gate called support for a missing journal: ' + $action)
                    }
                    $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal `
                        -FixtureRoot $root -AllowFixture -InvokeSupport $probe.Stub
                    $export = Export-UiAcceptanceThemeJournalGate -Gate $gate
                    Assert-Equal 'clear' ([string] $export.state)
                    Assert-Equal 'absent' ([string] $export.presenceState)
                    Assert-Equal $false ([bool] $export.journalPresent)
                    $json = ConvertTo-Json -InputObject $export -Depth 6
                    Assert-True ($json.IndexOf('C:\', [StringComparison]::Ordinal) -lt 0) `
                        'The exported gate facts carry an absolute path.'
                }
                finally { Remove-UiTestTree -Path $root }
            }
        },
        @{
            Name = 'gate: a journal path that is a directory is unknown and blocks without a probe'
            Body = {
                # The selection itself is complete and the journal sits directly in the root; what
                # the presence classification cannot answer is a path that is a directory, which is
                # the state under test. (A path below a file is refused earlier, as outside the
                # fixture boundary, so it can no longer reach this check.)
                $root = New-VisualGateFixtureRoot
                $journal = Join-Path $root 'theme-recovery.json'
                [void][IO.Directory]::CreateDirectory($journal)
                try {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        throw ('the gate called support for an unknown presence: ' + $action)
                    }
                    $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal `
                        -FixtureRoot $root -AllowFixture -InvokeSupport $probe.Stub
                    Assert-Equal 'blocked' ([string] $gate.state)
                    Assert-Equal 'theme_journal_presence_unknown' ([string] $gate.reason)
                    Assert-Equal 'unknown' ([string] $gate.presenceState)
                    Assert-Equal 0 ([int] $probe.State.Calls.Count)
                }
                finally { Remove-UiTestTree -Path $root }
            }
        },
        @{
            Name = 'gate: a foreign live owner, a live owner and an unknown owner all block'
            Body = {
                foreach ($case in @(
                    @{ Journal = 'foreign-live'; Owner = 'live'; Reason = 'theme_journal_foreign_live' },
                    @{ Journal = 'restore-required'; Owner = 'live'; Reason = 'theme_journal_owner_live' },
                    @{ Journal = 'restore-required'; Owner = 'unknown'; Reason = 'theme_journal_owner_unknown' }
                )) {
                    $journal = New-VisualGateJournalFile
                    try {
                        $body = New-VisualGateInspectBody -JournalState $case.Journal -OwnerState $case.Owner
                        $probe = New-VisualGateStub -Responder {
                            param($action, $arguments)
                            return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $body))
                        }
                        $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal.Path -FixtureRoot $journal.Root -AllowFixture -InvokeSupport $probe.Stub
                        Assert-Equal 'blocked' ([string] $gate.state)
                        Assert-Equal $case.Reason ([string] $gate.reason)
                        Assert-Equal 'inspect' (@($probe.State.Calls) -join ',')
                        Assert-True ([IO.File]::Exists($journal.Path)) 'A blocked gate removed the journal.'
                    }
                    finally { Remove-UiTestTree -Path $journal.Directory }
                }
            }
        },
        @{
            Name = 'gate: a dead owner is restored only through a confirmed restore and a closed journal'
            Body = {
                $journal = New-VisualGateJournalFile
                try {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        if ($action -ceq 'inspect') {
                            if ([IO.File]::Exists($journal.Path)) {
                                return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody -JournalState 'restore-required' -OwnerState 'dead')))
                            }
                            return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody -JournalState 'absent' -OwnerState 'none')))
                        }
                        if ($action -ceq 'restore') {
                            # Stands in for the coordinator's journal removal, which the gate then
                            # re-reads from disk rather than trusting the response alone.
                            Remove-Item -LiteralPath $journal.Path -Force
                            return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateRestoreBody -Disposition 'startup')))
                        }
                        throw ('unexpected action: ' + $action)
                    }
                    $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal.Path -FixtureRoot $journal.Root -AllowFixture -InvokeSupport $probe.Stub
                    Assert-Equal 'recovered' ([string] $gate.state)
                    Assert-Equal 'inspect,restore,inspect' (@($probe.State.Calls) -join ',')
                    Assert-Equal $false ([IO.File]::Exists($journal.Path))
                }
                finally { Remove-UiTestTree -Path $journal.Directory }
            }
        },
        @{
            Name = 'gate: a restore that says it failed blocks and preserves the journal'
            Body = {
                $journal = New-VisualGateJournalFile
                try {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        if ($action -ceq 'inspect') {
                            return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody)))
                        }
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateRestoreBody -Restored $false -Disposition 'startup')))
                    }
                    $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal.Path -FixtureRoot $journal.Root -AllowFixture -InvokeSupport $probe.Stub
                    Assert-Equal 'blocked' ([string] $gate.state)
                    Assert-Equal 'theme_journal_recovery_failed' ([string] $gate.reason)
                    Assert-True ([IO.File]::Exists($journal.Path)) 'A failed restore removed the journal.'
                }
                finally { Remove-UiTestTree -Path $journal.Directory }
            }
        },
        @{
            Name = 'gate: a restore response that claims success while the file survives stays blocked'
            Body = {
                $journal = New-VisualGateJournalFile
                try {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        if ($action -ceq 'inspect') {
                            return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody)))
                        }
                        # A valid restore answer that never actually removed anything.
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateRestoreBody -Disposition 'startup')))
                    }
                    $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal.Path -FixtureRoot $journal.Root -AllowFixture -InvokeSupport $probe.Stub
                    Assert-Equal 'blocked' ([string] $gate.state)
                    Assert-Equal 'theme_journal_recovery_unconfirmed' ([string] $gate.reason)
                    Assert-True ([IO.File]::Exists($journal.Path)) 'The gate trusted the response over the file.'
                }
                finally { Remove-UiTestTree -Path $journal.Directory }
            }
        },

        # --- behaviour: strict apply/restore responses -----------------------------------------
        @{
            Name = 'apply: a string boolean, a null and a missing field are three different refusals'
            Body = {
                foreach ($case in @(
                    @{ Body = (New-VisualGateApplyBody -Applied 'false'); Failure = 'applied.wrong-type' },
                    @{ Body = (New-VisualGateApplyBody -Applied $null); Failure = 'applied.null' }
                )) {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $case.Body))
                    }
                    $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe.Stub
                    $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                    Assert-Equal $false ([bool] $decision.ok)
                    Assert-Equal $case.Failure ([string] $decision.failure)
                    Assert-Equal 'unknown' ([string] $decision.writesPossible)
                }

                $missing = New-VisualGateApplyBody
                $missing.Remove('registryWritten')
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $missing))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                Assert-Equal 'registry_written.missing' ([string] $decision.failure)
            }
        },
        @{
            Name = 'apply: a parsed refusal without writes is none, while a pending journal still owes a restore'
            Body = {
                $clean = New-VisualGateApplyBody -Applied $false -JournalState 'absent' -RegistryWritten $false -BroadcastSent $false
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $clean))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                Assert-Equal $false ([bool] $decision.ok)
                Assert-Equal 'none' ([string] $decision.writesPossible)

                $partial = New-VisualGateApplyBody -Applied $false -JournalState 'pending' -RegistryWritten $false
                $probe2 = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $partial))
                }
                $result2 = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe2.Stub
                $decision2 = Test-UiAcceptanceThemeApplyResponse -Result $result2 -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                Assert-Equal 'journal' ([string] $decision2.writesPossible)
                Assert-Equal $true ([bool] $decision2.ownershipConfirmed)
            }
        },
        @{
            Name = 'apply: a written registry without an ownership receipt is never claimed as passable'
            Body = {
                $body = New-VisualGateApplyBody -HandbackToken ''
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $body))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                Assert-Equal $false ([bool] $decision.ok)
                Assert-Equal 'ownership.missing' ([string] $decision.failure)
                Assert-Equal 'registry' ([string] $decision.writesPossible)
                Assert-Equal $false ([bool] $decision.ownershipConfirmed)
            }
        },
        @{
            Name = 'apply: a complete response is the only one that passes'
            Body = {
                $body = New-VisualGateApplyBody
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $body))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                Assert-Equal $true ([bool] $decision.ok)
                Assert-Equal 'registry' ([string] $decision.writesPossible)
                Assert-Equal $true ([bool] $decision.ownershipConfirmed)
                Assert-Equal 'DWord' ([string] $decision.originalValueKind)
            }
        },
        @{
            Name = 'apply: a written registry with a failed broadcast, or without a pending journal, is refused'
            Body = {
                foreach ($case in @(
                    @{ Body = (New-VisualGateApplyBody -BroadcastSent $false); Failure = 'broadcast.unsent' },
                    @{ Body = (New-VisualGateApplyBody -JournalState 'absent'); Failure = 'journal_state.apply-without-pending' }
                )) {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $case.Body))
                    }
                    $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe.Stub
                    $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                    Assert-Equal $false ([bool] $decision.ok)
                    Assert-Equal $case.Failure ([string] $decision.failure)
                    # The response itself does not prove which write landed, so the round stays
                    # 'unknown' rather than claiming a clean refusal.
                    Assert-Equal 'unknown' ([string] $decision.writesPossible)
                }
            }
        },
        @{
            Name = 'apply: an echoed owner or binding that differs from the captured facts is rejected'
            Body = {
                foreach ($case in @(
                    @{ Body = (New-VisualGateApplyBody -OwnerPid 9999); Failure = 'ownership.identity_mismatch' },
                    @{ Body = (New-VisualGateApplyBody -ProductSha256 ('C' * 64)); Failure = 'ownership.binding_mismatch' },
                    @{ Body = (New-VisualGateApplyBody -IncludeOwner $false); Failure = 'ownership.owner.missing' }
                )) {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $case.Body))
                    }
                    $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe.Stub
                    $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode 'light' `
                        -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                    Assert-Equal $false ([bool] $decision.ok)
                    Assert-Equal $case.Failure ([string] $decision.failure)
                    Assert-Equal 'registry' ([string] $decision.writesPossible)
                }
            }
        },
        @{
            Name = 'apply: a structured not-integrated answer stays unknown, never a clean no-write'
            Body = {
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 10 -Stdout '{"error":"theme_coordinator_not_integrated","errorCode":"theme_coordinator_not_integrated"}')
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode 'light' `
                    -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                Assert-Equal $false ([bool] $decision.ok)
                Assert-Equal $true ([bool] $decision.notIntegrated)
                Assert-Equal 'exit.10' ([string] $decision.failure)
                Assert-Equal 'unknown' ([string] $decision.writesPossible)
            }
        },
        @{
            Name = 'restore: an echoed owner that differs from the round identity is rejected'
            Body = {
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateRestoreBody -OwnerPid 7777)))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' -JournalPath (Get-UiAcceptanceThemeJournalPath) -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeRestoreResponse -Result $result -JournalExpected $true `
                    -ExpectedOwnerIdentity $script:visualGateTestIdentity -ExpectedDisposition 'handback'
                Assert-Equal $false ([bool] $decision.ok)
                Assert-Equal 'ownership.identity_mismatch' ([string] $decision.failure)
            }
        },
        @{
            Name = 'restore: false, noAction, a string boolean and a mismatched readback all refuse'
            Body = {
                foreach ($case in @(
                    @{ Body = (New-VisualGateRestoreBody -Restored $false); Failure = 'restored.false' },
                    @{ Body = (New-VisualGateRestoreBody -NoAction $true); Failure = 'no_action.for-existing-journal' },
                    @{ Body = (New-VisualGateRestoreBody -Restored 'true'); Failure = 'restored.wrong-type' },
                    @{ Body = (New-VisualGateRestoreBody -MatchesOriginal $false); Failure = 'readback.mismatch' },
                    @{ Body = (New-VisualGateRestoreBody -BroadcastSent $false); Failure = 'broadcast.unsent' },
                    @{ Body = (New-VisualGateRestoreBody -JournalState 'pending'); Failure = 'journal_state.pending' },
                    @{ Body = (New-VisualGateRestoreBody -JournalRemoved $false); Failure = 'journal.not-removed' },
                    @{ Body = (New-VisualGateRestoreBody -Adapters 'something-else'); Failure = 'adapters.unknown' }
                )) {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $case.Body))
                    }
                    $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' -JournalPath (Get-UiAcceptanceThemeJournalPath) -InvokeSupport $probe.Stub
                    $decision = Test-UiAcceptanceThemeRestoreResponse -Result $result -JournalExpected $true `
                        -ExpectedOwnerIdentity $script:visualGateTestIdentity -ExpectedDisposition 'handback'
                    Assert-Equal $false ([bool] $decision.ok)
                    Assert-Equal $case.Failure ([string] $decision.failure)
                }

                # A readback that claims to match while reporting different bytes is refused on
                # the bytes, not on the claim.
                $lying = New-VisualGateRestoreBody
                $lying['readback']['bytes'] = 'FFFFFFFF'
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $lying))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' -JournalPath (Get-UiAcceptanceThemeJournalPath) -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeRestoreResponse -Result $result -JournalExpected $true `
                    -ExpectedOwnerIdentity $script:visualGateTestIdentity -ExpectedDisposition 'handback'
                Assert-Equal 'readback.bytes_mismatch' ([string] $decision.failure)
            }
        },
        @{
            Name = 'restore: an absent or checksum-invalid journal cannot pass behind restored=true'
            Body = {
                foreach ($case in @(
                    @{ Body = (New-VisualGateRestoreBody -JournalPresent $false); Failure = 'journal_present.false' },
                    @{ Body = (New-VisualGateRestoreBody -JournalChecksumValid $false); Failure = 'journal_checksum.invalid' }
                )) {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $case.Body))
                    }
                    $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' -JournalPath (Get-UiAcceptanceThemeJournalPath) -InvokeSupport $probe.Stub
                    $decision = Test-UiAcceptanceThemeRestoreResponse -Result $result -JournalExpected $true `
                        -ExpectedOwnerIdentity $script:visualGateTestIdentity -ExpectedDisposition 'handback'
                    Assert-Equal $false ([bool] $decision.ok)
                    Assert-Equal $case.Failure ([string] $decision.failure)
                }
            }
        },
        @{
            Name = 'fixture: a root without the explicit call licence starts no child and names the refusal'
            Body = {
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    throw 'the fixture root must not reach the child boundary'
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'inspect' `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) `
                    -FixtureRoot 'C:\Users\public\AppData\Local\Temp\tracefold-theme-fixture-refused' `
                    -InvokeSupport $probe.Stub
                Assert-Equal $false ([bool] $result.Ran)
                Assert-Equal 0 ([int] $probe.State.Calls.Count)
                Assert-Equal 'adapters.fixture' ([string] $result.ErrorText)
            }
        },
        @{
            Name = 'fixture: the restore phase without the call licence attempts two calls and starts none'
            Body = {
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    throw 'the fixture root must not reach the child boundary'
                }
                $round = New-UiAcceptanceThemeRoundState -Mode 'light' `
                    -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                $round.RestoreOwed = $true
                $round.OwnershipConfirmed = $true
                $round.HandbackToken = ('C' * 64)
                $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' `
                    -FixtureRoot 'C:\Users\public\AppData\Local\Temp\tracefold-theme-fixture-refused' `
                    -InvokeSupport $probe.Stub
                Assert-Equal 2 ([int] $outcome.calls)
                Assert-Equal 0 ([int] $probe.State.Calls.Count)
                Assert-Equal $false ([bool] $outcome.ok)
                Assert-Equal 'not-run' ([string] $outcome.decision.failure)
            }
        },
        @{
            Name = 'restore: only a complete, readback-matching, broadcast-closed answer passes'
            Body = {
                $body = New-VisualGateRestoreBody
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $body))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' -JournalPath (Get-UiAcceptanceThemeJournalPath) -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeRestoreResponse -Result $result -JournalExpected $true `
                    -ExpectedOwnerIdentity $script:visualGateTestIdentity -ExpectedDisposition 'handback'
                Assert-Equal $true ([bool] $decision.ok)
                Assert-Equal 'absent' ([string] $decision.journalState)
                Assert-Equal $true ([bool] $decision.readbackMatches)
            }
        },

        # --- behaviour: capability boundary ------------------------------------------------------
        @{
            Name = 'apply: the hand-back receipt lives only on the round, never in the decision'
            Body = {
                $token = ('C' * 64)
                $body = New-VisualGateApplyBody -HandbackToken $token
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $body))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe.Stub
                $round = New-UiAcceptanceThemeRoundState -Mode 'light'
                $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode 'light' `
                    -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding -Round $round
                Assert-Equal $true ([bool] $decision.ok)
                Assert-Equal $true ([bool] $decision.handbackTokenPresent)
                Assert-Equal $token ([string] $round.HandbackToken)
                Assert-Equal $null ($decision.PSObject.Properties['handbackToken'])
                Assert-Equal $null ($decision.PSObject.Properties['ownerToken'])
                Assert-Equal $null ($decision.PSObject.Properties['receiptId'])
                $projection = Export-UiAcceptanceThemeDecision -Decision $decision -Fields @('ok', 'applied', 'handbackTokenPresent')
                Assert-Equal $null ($projection.PSObject.Properties['handbackToken'])
                Assert-Equal $false ((ConvertTo-Json -InputObject $decision -Depth 8).Contains($token))
            }
        },
        @{
            Name = 'restore: a volunteered receipt is refused and an unexpected adapter word is refused'
            Body = {
                $volunteered = New-VisualGateRestoreBody
                $volunteered['handbackToken'] = ('C' * 64)
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $volunteered))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' -JournalPath (Get-UiAcceptanceThemeJournalPath) -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeRestoreResponse -Result $result -JournalExpected $true `
                    -ExpectedOwnerIdentity $script:visualGateTestIdentity -ExpectedDisposition 'handback'
                Assert-Equal 'handback_token.unexpected' ([string] $decision.failure)

                $fixture = New-VisualGateApplyBody -Adapters 'fixture'
                $probe2 = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $fixture))
                }
                $result2 = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'apply' -JournalPath (Get-UiAcceptanceThemeJournalPath) -Mode 'light' -InvokeSupport $probe2.Stub
                $denied = Test-UiAcceptanceThemeApplyResponse -Result $result2 -Mode 'light' `
                    -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                Assert-Equal 'adapters.fixture' ([string] $denied.failure)
                $licensed = Test-UiAcceptanceThemeApplyResponse -Result $result2 -Mode 'light' `
                    -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding -AllowFixture
                Assert-Equal $true ([bool] $licensed.ok)
                Assert-Equal 'fixture' ([string] $licensed.adapters)
            }
        },
        @{
            Name = 'command: the receipt travels on stdin, never in argv'
            Body = {
                $token = ('C' * 64)
                $captured = New-Object 'System.Collections.ArrayList'
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    [void] $captured.Add(@($arguments))
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout '{"restored":false}')
                }
                $null = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -Disposition 'handback' `
                    -HandbackToken $token -OwnerIdentity $script:visualGateTestIdentity `
                    -Binding $script:visualGateTestBinding -InvokeSupport $probe.Stub
                $argv = @($captured[0])
                Assert-True ($argv -contains '--handback-token') 'The receipt option is missing.'
                Assert-True ($argv -contains '-') 'The stdin sentinel is missing.'
                Assert-Equal $false ($argv -contains $token) 'The receipt value reached argv.'
            }
        },
        @{
            Name = 'round: a restore whose original differs from the apply is refused'
            Body = {
                $applyBody = New-VisualGateApplyBody -Applied $false -JournalState 'pending' -RegistryWritten $false
                $restoreBody = New-VisualGateRestoreBody
                $restoreBody['original']['bytes'] = '01000000'
                $restoreBody['readback']['bytes'] = '01000000'
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    if ($action -ceq 'apply') {
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $applyBody))
                    }
                    if ($action -ceq 'restore') {
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $restoreBody))
                    }
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody -JournalState 'absent' -OwnerState 'none')))
                }
                $round = New-UiAcceptanceThemeRoundState -Mode 'light' `
                    -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                $null = Invoke-UiAcceptanceThemeApplyPhase -Round $round `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' -Mode 'light' -InvokeSupport $probe.Stub
                $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' -InvokeSupport $probe.Stub
                Assert-Equal $false ([bool] $outcome.decision.ok)
                Assert-Equal 'original.apply_restore_mismatch' ([string] $outcome.decision.failure)
            }
        },
        @{
            Name = 'inspect: a reused pid is its own owner state, not an unknown one'
            Body = {
                $body = New-VisualGateInspectBody -JournalState 'restore-required' -OwnerState 'pid-reused'
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $body))
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'inspect' -JournalPath (Get-UiAcceptanceThemeJournalPath) -InvokeSupport $probe.Stub
                $decision = Test-UiAcceptanceThemeInspectResponse -Result $result
                Assert-Equal $true ([bool] $decision.ok)
                Assert-Equal 'pid-reused' ([string] $decision.ownerState)
            }
        },

        # --- behaviour: round ownership, stop gate and cleanup ---------------------------------
        @{
            Name = 'round: a lost apply answer owes a restore but may not perform one'
            Body = {
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    throw 'the support command died before answering'
                }
                $round = New-UiAcceptanceThemeRoundState -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                $decision = Invoke-UiAcceptanceThemeApplyPhase -Round $round `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' -Mode 'light' -InvokeSupport $probe.Stub
                Assert-Equal $false ([bool] $decision.ok)
                Assert-Equal $true ([bool] $round.Attempted)
                Assert-Equal 'unknown' ([string] $round.WritesPossible)
                Assert-Equal $true ([bool] $round.RestoreOwed)
                Assert-Equal $false (Test-UiAcceptanceThemeRoundMayRestore -State $round)

                $before = [int] $probe.State.Calls.Count
                $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round -JournalPath (Get-UiAcceptanceThemeJournalPath) `
                    -SupportPath 'stub' -InvokeSupport $probe.Stub
                Assert-Equal $false ([bool] $outcome.ok)
                Assert-Equal 'ownership.unconfirmed' ([string] $outcome.failure)
                Assert-Equal $before ([int] $probe.State.Calls.Count)
                Assert-Equal $false ([bool] $round.RestoreConfirmed)

                $start = Test-UiAcceptanceThemeRoundMayStart -State $round
                Assert-Equal $false ([bool] $start.Allowed)
                Assert-Equal 'ownership_unconfirmed' ([string] $start.Reason)
            }
        },
        @{
            Name = 'round: a partial apply that kept its receipt can be restored and then stop cleanly'
            Body = {
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    if ($action -ceq 'apply') {
                        $body = New-VisualGateApplyBody -Applied $false -JournalState 'pending' -RegistryWritten $false
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $body))
                    }
                    if ($action -ceq 'restore') {
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateRestoreBody)))
                    }
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody -JournalState 'absent' -OwnerState 'none')))
                }
                $round = New-UiAcceptanceThemeRoundState -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                $decision = Invoke-UiAcceptanceThemeApplyPhase -Round $round `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' -Mode 'light' -InvokeSupport $probe.Stub
                Assert-Equal $false ([bool] $decision.ok)
                Assert-Equal 'journal' ([string] $round.WritesPossible)
                Assert-Equal $true (Test-UiAcceptanceThemeRoundMayRestore -State $round)

                $evidence = { param($restoreDecision, $readbackDecision) }
                $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round -JournalPath (Get-UiAcceptanceThemeJournalPath) `
                    -SupportPath 'stub' -InvokeSupport $probe.Stub -WriteEvidence $evidence
                Assert-Equal $true ([bool] $outcome.ok)
                Assert-Equal $true ([bool] $round.RestoreConfirmed)
                Assert-Equal $true ([bool] (Test-UiAcceptanceThemeRoundMayStart -State $round).Allowed)
            }
        },
        @{
            Name = 'round: a restore that does not confirm stops the next theme instead of colliding'
            Body = {
                foreach ($case in @(
                    @{ RestoreBody = (New-VisualGateRestoreBody -Restored $false); Failure = 'restored.false'; JournalState = 'absent' },
                    @{ RestoreBody = (New-VisualGateRestoreBody); Failure = 'journal.open'; JournalState = 'pending' }
                )) {
                    $probe = New-VisualGateStub -Responder {
                        param($action, $arguments)
                        if ($action -ceq 'apply') {
                            return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateApplyBody)))
                        }
                        if ($action -ceq 'restore') {
                            return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $case.RestoreBody))
                        }
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody -JournalState $case.JournalState -OwnerState 'none')))
                    }
                    $round = New-UiAcceptanceThemeRoundState -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                    $null = Invoke-UiAcceptanceThemeApplyPhase -Round $round `
                        -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' -Mode 'light' -InvokeSupport $probe.Stub
                    $evidence = { param($restoreDecision, $readbackDecision) }
                    $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round -JournalPath (Get-UiAcceptanceThemeJournalPath) `
                        -SupportPath 'stub' -InvokeSupport $probe.Stub -WriteEvidence $evidence
                    Assert-Equal $false ([bool] $outcome.ok)
                    Assert-Equal $case.Failure ([string] $outcome.failure)
                    Assert-Equal $false ([bool] $round.RestoreConfirmed)
                    $start = Test-UiAcceptanceThemeRoundMayStart -State $round
                    Assert-Equal $false ([bool] $start.Allowed)
                    Assert-True (-not [string]::IsNullOrWhiteSpace([string] $start.Reason)) 'A stopped round allowed a next theme.'
                }
            }
        },
        @{
            Name = 'round: a successful restore never erases the failed apply that caused it'
            Body = {
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    if ($action -ceq 'apply') {
                        $body = New-VisualGateApplyBody -Applied $false -JournalState 'pending' -RegistryWritten $false
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson $body))
                    }
                    if ($action -ceq 'restore') {
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateRestoreBody)))
                    }
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody -JournalState 'absent' -OwnerState 'none')))
                }
                $round = New-UiAcceptanceThemeRoundState -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                $apply = Invoke-UiAcceptanceThemeApplyPhase -Round $round `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' -Mode 'light' -InvokeSupport $probe.Stub
                Assert-Equal $false ([bool] $apply.ok)
                $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round -JournalPath (Get-UiAcceptanceThemeJournalPath) `
                    -SupportPath 'stub' -InvokeSupport $probe.Stub -WriteEvidence { param($decision, $inspect) }
                Assert-Equal $true ([bool] $outcome.ok)
                # Two facts kept apart: the apply failed, and the restore that followed succeeded.
                Assert-Equal $false ([bool] $round.ApplySucceeded)
                Assert-Equal $true ([bool] $round.RestoreConfirmed)
                Assert-Equal 'applied.false' ([string] $round.ApplyDecision.failure)
            }
        },
        @{
            Name = 'round: an unrecorded restore is not a confirmed restore'
            Body = {
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    if ($action -ceq 'apply') {
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateApplyBody)))
                    }
                    if ($action -ceq 'restore') {
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateRestoreBody)))
                    }
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody -JournalState 'absent' -OwnerState 'none')))
                }
                $round = New-UiAcceptanceThemeRoundState -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                $null = Invoke-UiAcceptanceThemeApplyPhase -Round $round `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' -Mode 'light' -InvokeSupport $probe.Stub
                $evidence = { param($restoreDecision, $readbackDecision) throw 'evidence write failed' }
                $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round -JournalPath (Get-UiAcceptanceThemeJournalPath) `
                    -SupportPath 'stub' -InvokeSupport $probe.Stub -WriteEvidence $evidence
                Assert-Equal $false ([bool] $outcome.ok)
                Assert-Equal 'evidence.unwritten' ([string] $outcome.failure)
                Assert-Equal $false ([bool] $round.RestoreConfirmed)
            }
        },

        # --- behaviour: the matrix set gate ----------------------------------------------------
        @{
            Name = 'matrix: a complete grid passes and a missing or duplicate cell is a defect'
            Body = {
                $themes = @('light', 'dark')
                $pages = @('inbox', 'library', 'taxonomy', 'settings')
                $viewports = @('1024x720', '1280x800', '1600x900')
                $entries = New-Object 'System.Collections.Generic.List[object]'
                foreach ($theme in $themes) {
                    foreach ($viewport in $viewports) {
                        foreach ($page in $pages) {
                            [void] $entries.Add([ordered] @{
                                        theme = $theme; viewport = $viewport; page = $page
                                        state = 'matrix'; status = 'Passed'
                                    })
                        }
                    }
                }
                $required = @(@{ Page = 'inbox'; State = 'empty-selection' }, @{ Page = 'settings'; State = 'validation-error' })
                foreach ($theme in $themes) {
                    foreach ($item in $required) {
                        [void] $entries.Add([ordered] @{ theme = $theme; viewport = '1024x720'; page = $item.Page; state = $item.State; status = 'Passed' })
                    }
                    [void] $entries.Add([ordered] @{ theme = $theme; viewport = '1024x720'; page = 'inbox'; state = 'batch-metadata-dialog'; status = 'Passed' })
                }
                $dialogs = @(@{ State = 'batch-metadata-dialog' })

                $complete = Test-UiAcceptanceVisualMatrixSet -Entries @($entries.ToArray()) -Themes $themes `
                    -Pages $pages -Viewports $viewports -RequiredStates $required -DialogStates $dialogs
                Assert-Equal $true ([bool] $complete.ok)
                Assert-Equal 0 (@($complete.notPassed).Count)

                $missingGrid = @($entries.ToArray() | Where-Object {
                        -not ([string] $_.theme -ceq 'dark' -and [string] $_.viewport -ceq '1600x900' -and
                            [string] $_.page -ceq 'settings' -and [string] $_.state -ceq 'matrix')
                    })
                $missing = Test-UiAcceptanceVisualMatrixSet -Entries $missingGrid -Themes $themes `
                    -Pages $pages -Viewports $viewports -RequiredStates $required -DialogStates $dialogs
                Assert-Equal $false ([bool] $missing.ok)
                Assert-True (@($missing.defects | Where-Object { [string] $_ -like 'base:dark|1600x900|settings*' }).Count -eq 1) `
                    'A missing base-grid cell was not reported.'

                $duplicated = @($entries.ToArray()) + @([ordered] @{
                        theme = 'light'; viewport = '1024x720'; page = 'inbox'; state = 'matrix'; status = 'Passed'
                    })
                $duplicate = Test-UiAcceptanceVisualMatrixSet -Entries $duplicated -Themes $themes `
                    -Pages $pages -Viewports $viewports -RequiredStates $required -DialogStates $dialogs
                Assert-Equal $false ([bool] $duplicate.ok)
                Assert-True (@($duplicate.defects | Where-Object { [string] $_ -like 'duplicate:light|1024x720|inbox|matrix' }).Count -eq 1) `
                    'A duplicate matrix key was not reported.'
            }
        },
        @{
            Name = 'matrix: a required state that is present but NotRun lowers the run instead of passing it'
            Body = {
                $entries = @(
                    [ordered] @{ theme = 'light'; viewport = '1024x720'; page = 'inbox'; state = 'matrix'; status = 'Passed' }
                    [ordered] @{ theme = 'light'; viewport = '1024x720'; page = 'inbox'; state = 'diagnostics-running'; status = 'NotRun' }
                )
                $result = Test-UiAcceptanceVisualMatrixSet -Entries $entries -Themes @('light') `
                    -Pages @('inbox') -Viewports @('1024x720') `
                    -RequiredStates @(@{ Page = 'inbox'; State = 'diagnostics-running' }) -DialogStates @()
                Assert-Equal $true ([bool] $result.ok)
                Assert-Equal 1 (@($result.notPassed).Count)
                Assert-True ([string] @($result.notPassed)[0] -like 'state:diagnostics-running') `
                    'The unobserved required state was not reported as not passed.'
            }
        },
        @{
            Name = 'evidence: the scenario returns matrix data and the outlet writes the only index'
            Body = {
                # The shipped projection function is parsed out of the scenario and called with the
                # script state a run would have set. Its return value then goes through the real
                # reporting outlet, which owns the single index: fake screenshot and UIA files
                # stand in for a capture; no browser, product or keyboard input is involved.
                $out = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visualprojection-' + [guid]::NewGuid().ToString('N'))
                $bad = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visualprojection-bad-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($out)
                [void][IO.Directory]::CreateDirectory($bad)
                try {
                    $token = 'fixture-matrix-run-token'
                    $png = Get-UiTestPngBytes
                    $entries = New-Object System.Collections.ArrayList
                    $artifacts = New-Object 'System.Collections.Generic.List[string]'
                    foreach ($pair in @(
                            @('light', '1024x720', 'inbox'),
                            @('dark', '1024x720', 'inbox'))) {
                        $name = ($pair[0] + '-' + $pair[1] + '-' + $pair[2] + '-matrix')
                        [IO.File]::WriteAllBytes((Join-Path $out ($name + '.png')), $png)
                        [IO.File]::WriteAllText((Join-Path $out ($name + '.uia.json')), '{"focus":"owned"}', [Text.UTF8Encoding]::new($false))
                        [void] $artifacts.Add($name + '.png')
                        [void] $artifacts.Add($name + '.uia.json')
                        [void] $entries.Add([ordered] @{
                                theme = $pair[0]; viewport = $pair[1]; page = $pair[2]; state = 'matrix'
                                status = 'Passed'; image = ($name + '.png'); uia = ($name + '.uia.json')
                                dialog = $false; synthetic = $true; reason = ('run under ' + $token)
                            })
                    }

                    $script:productHash = ('A' * 64)
                    $script:entries = $entries
                    $script:themeFacts = New-Object System.Collections.ArrayList
                    [void] $script:themeFacts.Add([ordered] @{ mode = 'light'; applied = $true })
                    [void] $script:themeFacts.Add([ordered] @{ mode = 'dark'; applied = $true })
                    $script:keyboardFacts = New-Object System.Collections.ArrayList
                    [void] $script:keyboardFacts.Add([ordered] @{ key = 'Tab'; focus = 'owned' })
                    $script:artifacts = $artifacts
                    $script:themeRound = $null
                    $script:themeRestoreFailure = ''
                    $script:themeStopReason = ''
                    # The fixture run states its provenance: every entry and the matrix itself say
                    # they come from a controlled observation, not from a real product.
                    $syntheticFlag = $true
                    $journalGateFacts = [pscustomobject] @{ state = 'clear' }
                    $OutputDirectory = $out
                    $RunToken = $token
                    $OwnedRoot = $out

                    . (Get-VisualScenarioFunction -Name 'New-VisualMatrix')
                    $visual = New-VisualMatrix -ThemeMatrix 'Both' -Status 'Passed' -Reason ''

                    Assert-Equal 'Both' ([string] $visual.themeMatrix) 'The projection lost its theme matrix.'
                    Assert-Equal 'clear' ([string] $visual.journal.gate) 'The projection lost its gate fact.'
                    Assert-Equal 2 (@($visual.entries).Count) 'The projection lost an entry.'
                    Assert-Equal $true ([bool] $visual.synthetic) 'The projection must state its provenance.'
                    foreach ($entry in @($visual.entries)) {
                        Assert-Equal $true ([bool] $entry.synthetic) 'An entry must carry the same provenance.'
                    }
                    # The scenario is data only: it writes neither the index nor its page.
                    Assert-True (-not [IO.File]::Exists((Join-Path $out 'screenshots.json'))) 'The scenario wrote the index itself.'
                    Assert-True (-not [IO.File]::Exists((Join-Path $out 'index.html'))) 'The scenario wrote the index page itself.'

                    $written = Write-UiVisualIndex -OutputDirectory $out -Visual $visual -RunToken $token -OwnedRoot $out
                    Assert-Equal 2 $written.EntryCount 'The outlet lost an entry.'
                    $jsonPath = Join-Path $out 'screenshots.json'
                    $htmlPath = Join-Path $out 'index.html'
                    Assert-True ([IO.File]::Exists($jsonPath)) 'The outlet did not write the matrix JSON.'
                    Assert-True ([IO.File]::Exists($htmlPath)) 'The outlet did not write the matrix HTML.'
                    $raw = [IO.File]::ReadAllText($jsonPath)
                    $html = [IO.File]::ReadAllText($htmlPath)
                    $doc = $raw | ConvertFrom-Json

                    Assert-Equal 'Visual' ([string] $doc.scenario)
                    Assert-Equal 'Both' ([string] $doc.themeMatrix)
                    Assert-Equal 'clear' ([string] $doc.journal.gate)
                    Assert-Equal $true ([bool] $doc.synthetic) 'The published matrix must say it is synthetic.'
                    Assert-Equal $true ([bool] @($doc.entries)[0].synthetic) 'A published entry must say it too.'
                    Assert-Equal 2 (@($doc.entries).Count)
                    foreach ($entry in @($doc.entries)) {
                        $image = [string] $entry.image
                        Assert-True (-not [IO.Path]::IsPathRooted($image)) 'An entry image is an absolute path.'
                        Assert-True ([IO.File]::Exists((Join-Path $out $image))) ('The image ' + $image + ' is missing beside the report.')
                        Assert-True ([IO.File]::Exists((Join-Path $out ([string] $entry.uia)))) 'The UIA link is missing beside the report.'
                        Assert-True ([string] $entry.theme -ne '') 'An entry lost its theme.'
                        Assert-True ([string] $entry.viewport -eq '1024x720') 'An entry lost its viewport.'
                    }
                    # Relative links only: no machine root and no remote origin reaches the page.
                    Assert-True ($raw.IndexOf($out, [StringComparison]::OrdinalIgnoreCase) -lt 0) 'The JSON carries the output path.'
                    Assert-True ($html.IndexOf($out, [StringComparison]::OrdinalIgnoreCase) -lt 0) 'The HTML carries the output path.'
                    Assert-True ($html.IndexOf('http://', [StringComparison]::OrdinalIgnoreCase) -lt 0) 'The HTML references HTTP.'
                    Assert-True ($html.IndexOf('file://', [StringComparison]::OrdinalIgnoreCase) -lt 0) 'The HTML references a file URL.'
                    # The page embeds the data it renders, so the provenance is visible there too.
                    Assert-True ($html -match '"synthetic"\s*:\s*true') 'The page does not carry the synthetic marker.'
                    # The run token is replaced by the shared redactor, not merely absent.
                    # The outlet publishes only the reviewed entry fields: the free-text reason is
                    # not one of them, so the capability-bearing text never reaches either file.
                    Assert-True ($raw.IndexOf($token, [StringComparison]::Ordinal) -lt 0) 'The run token reached the JSON.'
                    Assert-True ($html.IndexOf($token, [StringComparison]::Ordinal) -lt 0) 'The run token reached the HTML.'

                    # One writer, one leaf: a second index write is refused instead of overwriting.
                    $secondWrite = $false
                    try { $null = Write-UiVisualIndex -OutputDirectory $out -Visual $visual -RunToken $token -OwnedRoot $out }
                    catch { $secondWrite = $true }
                    Assert-Equal $true $secondWrite 'A second index write into the same leaf must be refused.'

                    # Negative: a capability value that the shape-based redactor cannot find must
                    # stop the projection before the outlet renders it. The guard is the shipped one.
                    $script:entries = New-Object System.Collections.ArrayList
                    [void] $script:entries.Add([ordered] @{
                            theme = 'light'; viewport = '1024x720'; page = 'inbox'; state = 'matrix'
                            status = 'Passed'; image = 'x.png'; uia = 'x.uia.json'; reason = ('leaked ' + ('F' * 64))
                        })
                    $script:themeRound = [pscustomobject] @{ HandbackToken = ('F' * 64) }
                    $threw = $false
                    try { $null = New-VisualMatrix -ThemeMatrix 'Both' -Status 'Passed' -Reason '' }
                    catch { $threw = $true; Assert-Contains ([string] $_.Exception.Message) 'visual.capability_value_leak' }
                    Assert-Equal $true $threw
                    Assert-Equal 0 (@([IO.Directory]::GetFileSystemEntries($bad)).Count) `
                        'A refused projection wrote something.'
                }
                finally {
                    Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction SilentlyContinue
                    Remove-Item -LiteralPath $bad -Recurse -Force -ErrorAction SilentlyContinue
                    foreach ($name in @('entries', 'themeFacts', 'keyboardFacts', 'artifacts', 'productHash',
                            'themeRound', 'themeRestoreFailure', 'themeStopReason', 'syntheticFlag')) {
                        Remove-Variable -Name $name -Scope Script -ErrorAction SilentlyContinue
                    }
                }
            }
        },
        @{
            Name = 'command: the injected seam carries the same receipt the default wrapper sends'
            Body = {
                $token = ('E' * 64)
                $seen = New-Object 'System.Collections.ArrayList'
                # A three-parameter witness: it records exactly what the seam handed it, which is
                # the one fact a two-parameter stub silently drops.
                $witness = {
                    param($SupportPath, $Arguments, $StandardInput)
                    [void] $seen.Add([pscustomobject] @{
                            Arguments = @($Arguments); StandardInput = [string] $StandardInput
                        })
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout '{"restored":false}')
                }
                $null = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -Disposition 'handback' `
                    -HandbackToken $token -OwnerIdentity $script:visualGateTestIdentity `
                    -Binding $script:visualGateTestBinding -InvokeSupport $witness
                Assert-Equal 1 (@($seen).Count)
                Assert-Equal ($token + "`n") ([string] @($seen)[0].StandardInput)
                Assert-Equal $false (@(@($seen)[0].Arguments) -contains $token) `
                    'The receipt reached argv through the seam.'
                Assert-True (@(@($seen)[0].Arguments) -contains '-') 'The stdin sentinel is missing.'

                # An inspect call sends no receipt at all: the seam is a channel for one payload,
                # not a permanent attachment.
                $null = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'inspect' `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -InvokeSupport $witness
                Assert-Equal 2 (@($seen).Count)
                Assert-Equal '' ([string] @($seen)[1].StandardInput)

                # A two-parameter stub keeps working: the third argument is extra, not required.
                $twoParam = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout '{"restored":false}')
                }
                $result = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -Disposition 'handback' `
                    -HandbackToken $token -OwnerIdentity $script:visualGateTestIdentity `
                    -Binding $script:visualGateTestBinding -InvokeSupport $twoParam.Stub
                Assert-Equal $true ([bool] $result.Ran)
            }
        },
        @{
            Name = 'gate: the fixture licence reaches every command and validator the gate runs'
            Body = {
                $fixture = New-VisualGateFixtureRoot -OwnerState 'dead'
                $journal = Join-Path $fixture 'theme-recovery.json'
                [IO.File]::WriteAllText($journal, '{"schemaVersion":1}', [Text.UTF8Encoding]::new($false))

                # The whole three-step gate, not one command: inspect, restore, readback. The
                # responder also removes the journal on restore, so the gate's own physical
                # re-check sees the closure it claims.
                $calls = New-Object 'System.Collections.ArrayList'
                $probeLog = New-Object 'System.Collections.ArrayList'
                $responder = {
                    param($SupportPath, $Arguments, $StandardInput)
                    $action = [string] $Arguments[1]
                    try {
                        [void] $probeLog.Add('enter:' + [string] $action + '#' + [string] @($calls).Count)
                        [void] $calls.Add(@($arguments))
                        if ($action -ceq 'restore') {
                            [IO.File]::Delete($journal)
                            return [pscustomobject] @{
                                ExitCode = 0; Stderr = ''
                                Stdout = '{"adapters":"fixture","disposition":"startup","restored":true,"noAction":false,"journalState":"absent","journalPresent":true,"journalRemoved":true,"journalChecksumValid":true,"ownerState":"dead","original":{"present":true,"valueKind":"DWord","bytes":"00000000"},"readback":{"present":true,"valueKind":"DWord","bytes":"00000000","matchesOriginal":true},"broadcast":{"sent":true},"registryRestored":true,"bindingMatchesJournal":true}'
                            }
                        }
                        $state = 'restore-required'
                        $owner = 'dead'
                        if (@($calls).Count -ge 3) { $state = 'absent'; $owner = 'none' }
                        return [pscustomobject] @{
                            ExitCode = 0; Stderr = ''
                            Stdout = ('{"adapters":"fixture","journalState":"' + $state + '","ownerState":"' + $owner +
                                '","startupRecoveryAllowed":false,"handbackAccepted":false,"handbackRejection":null}')
                        }
                    }
                    catch {
                        [void] $probeLog.Add('throw:' + [string] $_.Exception.Message)
                        throw
                    }
                }.GetNewClosure()
                $gate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal -SupportPath 'stub' `
                    -FixtureRoot $fixture -AllowFixture -EnforceAdapterSelection -InvokeSupport $responder

                Assert-True ($gate.state -ceq 'recovered') ('the gate stopped at ' + [string] $gate.reason +
                    ' / inspect=' + [string] $gate.inspect.failure + ' parse=' + [string] $gate.inspect.parseState +
                    ' / log=' + (@($probeLog) -join ','))
                Assert-Equal 'recovered' ([string] $gate.state)
                Assert-Equal 3 (@($calls).Count)
                # The exported facts are read as parsed values, not as a text search: no string
                # field may carry a drive-qualified machine path.
                $facts = Export-UiAcceptanceThemeJournalGate -Gate $gate
                foreach ($property in @($facts.PSObject.Properties)) {
                    if ($property.Value -isnot [string]) { continue }
                    Assert-True (([string] $property.Value) -notmatch '^[A-Za-z]:[\\/]') `
                        ('the gate facts carry a machine path in ' + [string] $property.Name)
                }
                $index = 0
                foreach ($argv in $calls) {
                    Assert-True (@($argv) -contains '--fixture-root') ('call ' + $index + ' carries no fixture root.')
                    $at = [array]::IndexOf(@($argv), '--fixture-root')
                    Assert-Equal $fixture ([string] @($argv)[$at + 1])
                    $index++
                }

                # Layer one is the consumer switch: without it the root is not sent and no child
                # starts, so naming a fixture root alone cannot move a run off the production
                # journal.
                $unlicensedCalls = New-Object 'System.Collections.ArrayList'
                $unlicensed = {
                    param($action, $arguments)
                    [void] $unlicensedCalls.Add($action)
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout '{}')
                }.GetNewClosure()
                $refusedCommand = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'inspect' `
                    -JournalPath $journal -FixtureRoot $fixture -InvokeSupport $unlicensed
                Assert-Equal 0 (@($unlicensedCalls).Count)
                Assert-Equal 'adapters.fixture' ([string] $refusedCommand.ErrorText)
                # The licensed run above closed the journal; put one back so the unlicensed gate is
                # refused by the licence and not by an absent file.
                [IO.File]::WriteAllText($journal, '{"schemaVersion":1}', [Text.UTF8Encoding]::new($false))
                $blocked = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journal -SupportPath 'stub' `
                    -FixtureRoot $fixture -InvokeSupport $unlicensed
                Assert-Equal 'blocked' ([string] $blocked.state)
                Assert-Equal 0 (@($unlicensedCalls).Count)

                # Layer two is the child's own process licence. The switch above only decides
                # whether the root is sent; the CLI refuses a root it is not licensed for, so the
                # two are separate gates and neither substitutes for the other.
                $supportSource = [IO.File]::ReadAllText((Join-Path $script:root 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/ThemeFixtureAdapters.cs'))
                $supportProgram = [IO.File]::ReadAllText((Join-Path $script:root 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/Program.cs'))
                Assert-Contains $supportSource 'TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE'
                Assert-Contains $supportProgram 'The fixture adapters are not licensed for this process.'

                # Both response validators treat the fixture adapter word as a refusal unless the
                # same licence is passed to them. The result under test is the command wrapper the
                # gate really consumes, not a bare process record.
                $fixtureInspectStub = {
                    param($SupportPath, $Arguments)
                    return [pscustomobject] @{
                        ExitCode = 0; Stderr = ''
                        Stdout = '{"adapters":"fixture","journalState":"restore-required","ownerState":"dead","startupRecoveryAllowed":false,"handbackAccepted":false,"handbackRejection":null}'
                    }
                }.GetNewClosure()
                $inspectCommand = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'inspect' `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -InvokeSupport $fixtureInspectStub
                Assert-Equal $true ([bool] $inspectCommand.Ran)
                # the production default is still accepted when the caller runs no fixture
                $strictInspect = Test-UiAcceptanceThemeInspectResponse -Result $inspectCommand -ExpectedAdapter 'production'
                Assert-Equal $false ([bool] $strictInspect.ok)
                Assert-Equal 'adapters.fixture' ([string] $strictInspect.failure)
                $licensedInspect = Test-UiAcceptanceThemeInspectResponse -Result $inspectCommand -AllowFixture
                Assert-Equal $true ([bool] $licensedInspect.ok)
                Assert-Equal 'fixture' ([string] $licensedInspect.adapters)

                $fixtureRestoreStub = {
                    param($SupportPath, $Arguments)
                    return [pscustomobject] @{
                        ExitCode = 0; Stderr = ''
                        Stdout = '{"adapters":"fixture","disposition":"startup","restored":true,"noAction":false,"journalState":"absent","journalPresent":true,"journalRemoved":true,"journalChecksumValid":true,"ownerState":"dead","original":{"present":true,"valueKind":"DWord","bytes":"00000000"},"readback":{"present":true,"valueKind":"DWord","bytes":"00000000","matchesOriginal":true},"broadcast":{"sent":true},"registryRestored":true,"bindingMatchesJournal":true}'
                    }
                }.GetNewClosure()
                $restoreCommand = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'restore' `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -Disposition 'startup' -InvokeSupport $fixtureRestoreStub
                Assert-Equal $true ([bool] $restoreCommand.Ran)
                $strictRestore = Test-UiAcceptanceThemeRestoreResponse -Result $restoreCommand -JournalExpected $true
                Assert-Equal $false ([bool] $strictRestore.ok)
                Assert-Equal 'adapters.fixture' ([string] $strictRestore.failure)
                $licensedRestore = Test-UiAcceptanceThemeRestoreResponse -Result $restoreCommand -JournalExpected $true -AllowFixture
                Assert-Equal $true ([bool] $licensedRestore.ok)
            }
        },
        @{
            Name = 'round: a real writer failure leaves the restore unconfirmed and writes no evidence'
            Body = {
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    if ($action -ceq 'apply') {
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateApplyBody)))
                    }
                    if ($action -ceq 'restore') {
                        return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateRestoreBody)))
                    }
                    return (New-VisualGateCommandResult -ExitCode 0 -Stdout (ConvertTo-VisualGateJson (New-VisualGateInspectBody -JournalState 'absent' -OwnerState 'none')))
                }
                $round = New-UiAcceptanceThemeRoundState -Mode 'light' -OwnerIdentity $script:visualGateTestIdentity -Binding $script:visualGateTestBinding
                $null = Invoke-UiAcceptanceThemeApplyPhase -Round $round `
                    -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' -Mode 'light' -InvokeSupport $probe.Stub

                $output = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visualgate-ev-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($output)
                try {
                    # The shipped writer, not a second one. A document without its journal section
                    # is a real refusal inside Write-UiThemeEvidence, so this drives the phase's own
                    # error path instead of a callback that merely returns false.
                    $evidence = {
                        param($restoreDecision, $readbackDecision)
                        [void] (Write-UiThemeEvidence -OutputDirectory $output -EvidenceId 'theme-light' `
                                -Document ([ordered] @{ mode = 'light' }) -RunToken 'fixture-token-not-a-secret' `
                                -OwnedRoot $output)
                    }.GetNewClosure()
                    $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round `
                        -JournalPath (Get-UiAcceptanceThemeJournalPath) -SupportPath 'stub' `
                        -InvokeSupport $probe.Stub -WriteEvidence $evidence

                    Assert-Equal $false ([bool] $outcome.ok)
                    Assert-Equal 'evidence.unwritten' ([string] $outcome.failure)
                    Assert-Equal $false ([bool] $outcome.evidenceWritten)
                    Assert-Equal $false ([bool] $round.RestoreConfirmed)
                    Assert-Equal 'restore_unconfirmed' ([string] $round.StopReason)
                    # A type and a message, not a dumped record: the record stays in memory.
                    Assert-True (-not [string]::IsNullOrWhiteSpace([string] $outcome.evidenceErrorType)) `
                        'The writer failure did not record its error type.'
                    Assert-True (-not [string]::IsNullOrWhiteSpace([string] $outcome.evidenceErrorText)) `
                        'The writer failure did not record its message.'
                    Assert-True (-not ([string] $outcome.evidenceErrorText).Contains($output)) `
                        'The writer error text carries the output path.'
                    Assert-True (-not ([string] $outcome.evidenceErrorText).Contains('fixture-token-not-a-secret')) `
                        'The writer error text carries the run token.'
                    # Nothing was written before the refusal: the check precedes the file.
                    Assert-Equal 0 (@(Get-ChildItem -LiteralPath $output -File -ErrorAction SilentlyContinue).Count)
                }
                finally { Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue }
            }
        },
        @{
            Name = 'entry: a non-visual request is refused by the journal gate before the preflight'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visualgate-entry-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                $fixture = New-VisualGateFixtureRoot -OwnerState 'dead'
                try {
                    # A directory at the journal path cannot be classified as a journal, so the gate
                    # refuses before it could ever ask for support. The selection itself is complete
                    # and legal, so the refusal is still the presence check and not the new
                    # combination guard; the ordering stays visible without a product, a driver or a
                    # published support tool.
                    $journalDirectory = Join-Path $fixture 'theme-recovery.json'
                    [void][IO.Directory]::CreateDirectory($journalDirectory)
                    $output = Join-Path $base 'out'

                    $entry = Invoke-VisualEntryChild -Arguments @(
                        (Join-Path $script:root 'scripts/run-stage5cc-ui-acceptance.ps1')
                        '-ProductDirectory', (Join-Path $base 'product')
                        '-WinAppCliArchivePath', (Join-Path $base 'winapp.zip')
                        '-OutputDirectory', $output
                        '-Scenario', 'SingleInstance'
                        '-ThemeJournalPath', $journalDirectory
                        '-ThemeFixtureRoot', $fixture
                        '-AllowThemeFixture'
                    )
                    Assert-Equal 1 ([int] $entry.ExitCode)
                    $summaryPath = Join-Path $output 'summary.json'
                    Assert-True ([IO.File]::Exists($summaryPath)) 'The blocked batch wrote no summary.'
                    $summary = [IO.File]::ReadAllText($summaryPath) | ConvertFrom-Json

                    # The gate ran, and it is the reason the batch stopped.
                    Assert-True ($null -ne $summary.PSObject.Properties['themeJournalGate']) `
                        'The summary does not carry the gate facts.'
                    Assert-Equal 'blocked' ([string] $summary.themeJournalGate.state)
                    Assert-Equal 'theme_journal_presence_unknown' ([string] $summary.reason)
                    Assert-Equal 'Blocked' ([string] @($summary.entries)[0].status)
                    Assert-Equal 'SingleInstance' ([string] @($summary.entries)[0].id)
                    # The request precheck never ran, so there is no completeness node at all.
                    Assert-True ($null -eq $summary.PSObject.Properties['checks']) `
                        'The preflight answered before the journal gate.'
                    Assert-Equal 'Blocked' ([string] $summary.status)
                }
                finally {
                    Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction SilentlyContinue
                    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
        },
        @{
            Name = 'scenario: CurrentOnly keeps a pending journal while Both restores it at the gate'
            Body = {
                # The real scenario top level is invoked, not an extracted function: everything up
                # to and including its own journal gate runs. The support child answers the theme
                # surface from a controlled fixture root, so no registry and no product is touched;
                # the product never starts because the gate answers first.
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visual-70-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                $previous = $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE
                $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE = '1'
                try {
                    $product = Join-Path $base 'TraceFold.Media.exe'
                    [IO.File]::WriteAllText($product, 'controlled product stand-in', [Text.UTF8Encoding]::new($false))
                    $winapp = Join-Path $base 'winapp.exe'
                    [IO.File]::WriteAllText($winapp, 'controlled driver stand-in', [Text.UTF8Encoding]::new($false))
                    $contract = Join-Path $script:root 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
                    $log = Join-Path $base 'calls.log'
                    $stub = Join-Path $base 'support.ps1'
                    $env:TRACEFOLD_MATRIX_LOG = $log
                    [IO.File]::WriteAllText($stub, @'
$action = ''
$journal = ''
for ($i = 0; $i -lt $args.Count - 1; $i++) {
    if ($args[$i] -eq 'theme') { $action = [string] $args[$i + 1] }
    if ($args[$i] -eq '--journal') { $journal = [string] $args[$i + 1] }
}
if ($env:TRACEFOLD_MATRIX_LOG) { Add-Content -LiteralPath $env:TRACEFOLD_MATRIX_LOG -Value $action }
if ($action -eq 'restore') {
    if ($journal -ne '' -and (Test-Path -LiteralPath $journal)) { Remove-Item -LiteralPath $journal -Force }
    '{"adapters":"fixture","disposition":"startup","restored":true,"noAction":false,"journalState":"absent","journalPresent":true,"journalRemoved":true,"journalChecksumValid":true,"ownerState":"dead","original":{"present":true,"valueKind":"DWord","bytes":"00000000"},"readback":{"present":true,"valueKind":"DWord","bytes":"00000000","matchesOriginal":true},"broadcast":{"sent":true},"registryRestored":true,"bindingMatchesJournal":true}'
    exit 0
}
$state = 'restore-required'
$owner = 'dead'
if ($journal -ne '' -and -not (Test-Path -LiteralPath $journal)) { $state = 'absent'; $owner = 'none' }
'{"adapters":"fixture","journalState":"' + $state + '","ownerState":"' + $owner + '","startupRecoveryAllowed":false,"handbackAccepted":false,"handbackRejection":null}'
'@, [Text.UTF8Encoding]::new($false))

                    $scenario = Join-Path $script:uiRoot 'scenarios/70-visual-keyboard-matrix.ps1'
                    $readOnly = Invoke-VisualMatrixBoundaryRun -Mode 'CurrentOnly' -Scenario $scenario `
                        -Product $product -Contract $contract -Stub $stub -Log $log -Base $base
                    try {
                        Assert-Equal 'Blocked' ([string] $readOnly.Outcome.Status) 'a read-only start must stop at the gate'
                        Assert-True (([string] $readOnly.Outcome.Reason).Contains('visual.theme_journal_blocked')) `
                            ('the refusal must name the gate: ' + [string] $readOnly.Outcome.Reason)
                        Assert-Equal 'inspect' ((@($readOnly.Calls) -join ',')) 'no restore may be issued'
                        Assert-Equal $true $readOnly.JournalExists 'the journal must survive a read-only start'
                        Assert-Equal $readOnly.JournalBefore $readOnly.JournalAfter 'the journal bytes must not change'
                        Assert-Equal 0 (@(Get-ChildItem -LiteralPath $readOnly.Output -File -ErrorAction SilentlyContinue).Count) 'nothing may be written'
                    }
                    finally { Remove-Item -LiteralPath $readOnly.Fixture -Recurse -Force -ErrorAction SilentlyContinue }

                    $both = Invoke-VisualMatrixBoundaryRun -Mode 'Both' -Scenario $scenario `
                        -Product $product -Contract $contract -Stub $stub -Log $log -Base $base
                    try {
                        # Both keeps the recovery contract and then proceeds: the first round
                        # asks for an apply, which this stub answers with an inspect body, so the
                        # run stops there with Blocked instead of touching a product.
                        Assert-Equal 'inspect,restore,inspect,apply' ((@($both.Calls) -join ',')) 'Both keeps the recovery contract'
                        Assert-Equal $false $both.JournalExists 'the restored journal must be closed'
                        Assert-Equal 'Failed' ([string] $both.Outcome.Status) 'the run stops later, at its first apply'
                        Assert-True (-not ([string] $both.Outcome.Reason).Contains('theme_journal_blocked')) `
                            ('the Both gate must pass: ' + [string] $both.Outcome.Reason)
                    }
                    finally { Remove-Item -LiteralPath $both.Fixture -Recurse -Force -ErrorAction SilentlyContinue }
                }
                finally {
                    Remove-Item Env:TRACEFOLD_MATRIX_LOG -ErrorAction SilentlyContinue
                    if ($null -eq $previous) { Remove-Item Env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE -ErrorAction SilentlyContinue }
                    else { $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE = $previous }
                    Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
        },
        @{
            Name = 'entry: a CurrentOnly start blocks on a dead-owner journal without restoring it'
            Body = {
                # The public entry is started with -File in source mode. Its own gate runs against
                # a test-owned copy of the source entry and support payload. The active checkout's
                # bootstrap directory is neither inspected nor overwritten.
                $cli = Get-VisualGateRealSupportCli
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-e-' + [guid]::NewGuid().ToString('N').Substring(0, 16))
                [void][IO.Directory]::CreateDirectory($base)
                $fixture = $null
                $casePassed = $false
                $previous = $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE
                try {
                    $entryScript = New-VisualGateTestOwnedEntryTree -Root $script:root -Base $base `
                        -SupportExecutable $cli
                    $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE = '1'
                    $fixture = New-VisualGateFixtureRoot -OwnerState 'live'
                    $journal = Join-Path $fixture 'theme-recovery.json'
                    # A journal the real CLI will accept: it is written by the real apply phase
                    # against the same fixture root, so its checksum, owner and binding are the
                    # ones the reviewed coordinator produces.
                    $identity = New-VisualGateIdentity -ProcessId $PID `
                        -StartUtcTicks ((Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks)
                    $binding = New-VisualGateBinding
                    $round = New-UiAcceptanceThemeRoundState -Mode 'light' -OwnerIdentity $identity -Binding $binding
                    $apply = Invoke-UiAcceptanceThemeApplyPhase -Round $round -JournalPath $journal `
                        -SupportPath $cli -Mode 'light' -FixtureRoot $fixture -AllowFixture
                    Assert-Equal $true ([bool] $apply.ok) ('the fixture apply must write a journal: ' + [string] $apply.failure)
                    [IO.File]::WriteAllText((Join-Path $fixture 'probe.json'),
                        '{"state":"dead","identityReadable":true}', [Text.UTF8Encoding]::new($false))
                    $journalBefore = (Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash
                    $registryBefore = [IO.File]::ReadAllText((Join-Path $fixture 'registry.json'))
                    $broadcastBefore = [IO.File]::ReadAllText((Join-Path $fixture 'broadcast.json'))
                    $output = Join-Path $base 'out'

                    $entryArguments = @(
                        $entryScript
                        '-ProductDirectory', (Join-Path $base 'product')
                        '-WinAppCliArchivePath', (Join-Path $base 'winapp.zip')
                        '-OutputDirectory', $output
                        '-Scenario', 'Visual'
                        '-ThemeMatrix', 'CurrentOnly'
                        '-ThemeJournalPath', $journal
                        '-ThemeFixtureRoot', $fixture
                        '-AllowThemeFixture'
                    )
                    $entry = Invoke-VisualEntryChild -Arguments $entryArguments
                    Write-VisualIntegrationJson -Path (Join-Path $base 'entry-child.json') -Value ([ordered] @{
                        host = (Get-Process -Id $PID).Path
                        workingDirectory = (Get-Location).Path
                        arguments = $entryArguments
                        result = $entry
                    })
                    Assert-Equal 1 ([int] $entry.ExitCode) 'the entry must block instead of starting'
                    Assert-True ([IO.File]::Exists((Join-Path $output 'summary.json'))) `
                        ('the entry did not write its summary: ' + [string] $entry.Stderr)
                    $summary = [IO.File]::ReadAllText((Join-Path $output 'summary.json')) | ConvertFrom-Json
                    Assert-Equal 'theme_journal_restore_not_allowed' ([string] $summary.reason)
                    Assert-Equal 'blocked' ([string] $summary.themeJournalGate.state)
                    Assert-Equal $journalBefore ((Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash) 'the journal must be untouched'
                    Assert-Equal $registryBefore ([IO.File]::ReadAllText((Join-Path $fixture 'registry.json'))) 'the registry fixture must not change'
                    Assert-Equal $broadcastBefore ([IO.File]::ReadAllText((Join-Path $fixture 'broadcast.json'))) 'no broadcast may be counted'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $output 'Visual'))) 'no scenario output may exist'
                    $casePassed = $true
                }
                catch {
                    Write-VisualIntegrationFirstFailure -Directory $base -Failure $_
                    throw
                }
                finally {
                    if ($null -eq $previous) { Remove-Item Env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE -ErrorAction SilentlyContinue }
                    else { $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE = $previous }
                    if ($casePassed) {
                        if ($null -ne $fixture) { Remove-UiTestTree -Path $fixture }
                        Remove-UiTestTree -Path $base
                    }
                    else { Write-Host ('current-only entry evidence retained: ' + $base) }
                }
            }
        },
        @{
            Name = 'transport: the default wrapper drives the real fixture CLI through the real phases and writer'
            Body = {
                $cli = Get-VisualGateRealSupportCli
                $fixture = New-VisualGateFixtureRoot -OwnerState 'live'
                $journal = Join-Path $fixture 'theme-recovery.json'
                $output = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visualgate-ev-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($output)
                $previous = $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE
                $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE = '1'
                try {
                    $identity = New-VisualGateIdentity -ProcessId $PID `
                        -StartUtcTicks ((Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks)
                    $binding = New-VisualGateBinding
                    $round = New-UiAcceptanceThemeRoundState -Mode 'light' -OwnerIdentity $identity -Binding $binding

                    # No InvokeSupport anywhere below: default branch, real wrapper, real child,
                    # real apply phase, real restore phase and the shipped evidence writer. This
                    # is the one case that walks the whole consumer chain instead of joining two
                    # stand-ins together.
                    $applyDecision = Invoke-UiAcceptanceThemeApplyPhase -Round $round -JournalPath $journal `
                        -SupportPath $cli -Mode 'light' -FixtureRoot $fixture -AllowFixture
                    Assert-Equal $true ([bool] $applyDecision.ok)
                    Assert-Equal 'fixture' ([string] $applyDecision.adapters)
                    Assert-Equal $true ([bool] $applyDecision.journalWritten)
                    Assert-Equal $true ([bool] $round.RestoreOwed)
                    Assert-True ([IO.File]::Exists($journal)) 'The apply wrote no journal to the fixture root.'
                    $token = [string] $round.HandbackToken
                    Assert-Equal 64 $token.Length

                    $evidence = {
                        param($restoreDecision, $readbackDecision)
                        [void] (Write-UiThemeEvidence -OutputDirectory $output -EvidenceId 'theme-light' `
                                -Document ([ordered] @{
                                    mode = 'light'
                                    apply = (Export-UiAcceptanceThemeDecision -Decision $applyDecision -Fields @(
                                        'ok', 'failure', 'adapters', 'applied', 'journalState', 'journalWritten',
                                        'registryWritten', 'readBackVerified', 'broadcastSent', 'writesPossible'))
                                    restore = (Export-UiAcceptanceThemeDecision -Decision $restoreDecision -Fields @(
                                        'ok', 'failure', 'adapters', 'disposition', 'restored', 'journalState',
                                        'journalClosed', 'ownerState', 'identityMatch'))
                                    readback = (Export-UiAcceptanceThemeDecision -Decision $readbackDecision -Fields @(
                                        'ok', 'failure', 'journalState', 'ownerState', 'adapters'))
                                    journal = [ordered] @{ state = 'closed'; path = $journal }
                                }) -OwnedRoot $fixture)
                    }.GetNewClosure()

                    # The receipt travels on stdin here: the CLI accepts only the sentinel in argv,
                    # so a confirmed hand-back is itself the proof that the pipe carried the value.
                    $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round -JournalPath $journal `
                        -SupportPath $cli -FixtureRoot $fixture -AllowFixture -WriteEvidence $evidence
                    Assert-Equal $true ([bool] $outcome.ok)
                    Assert-Equal $true ([bool] $outcome.journalClosed)
                    Assert-Equal $true ([bool] $outcome.evidenceWritten)
                    Assert-Equal $false ([IO.File]::Exists($journal)) 'The hand-back left its journal behind.'

                    # The writer's own artifact, read back from disk.
                    $written = Join-Path $output 'theme-light-theme.json'
                    Assert-True ([IO.File]::Exists($written)) 'No evidence file was written for the theme.'
                    $raw = [IO.File]::ReadAllText($written)
                    $document = $raw | ConvertFrom-Json
                    Assert-Equal 'light' ([string] $document.mode)
                    Assert-Equal 'fixture' ([string] $document.apply.adapters)
                    Assert-Equal $true ([bool] $document.apply.journalWritten)
                    Assert-Equal 'handback' ([string] $document.restore.disposition)
                    Assert-Equal $true ([bool] $document.restore.restored)
                    Assert-Equal 'absent' ([string] $document.readback.journalState)
                    Assert-True ($null -ne $document.PSObject.Properties['journal']) 'The evidence lost its journal section.'
                    # The shared writer redacted on this path too: neither the token nor the
                    # fixture root reaches the artifact.
                    Assert-True ($raw.IndexOf($token, [StringComparison]::Ordinal) -lt 0) 'The receipt reached the evidence file.'
                    Assert-True ($raw.IndexOf($fixture, [StringComparison]::OrdinalIgnoreCase) -lt 0) 'The fixture root reached the evidence file.'
                    # Field level, on the parsed document rather than a raw substring: the journal
                    # path is the shared label with the rest of the name kept, not the original
                    # path and not an empty value.
                    $journalField = [string] $document.journal.path
                    Assert-True ($journalField -cne $journal) 'the evidence kept the original journal path'
                    Assert-True ($journalField.StartsWith('[redacted-path]', [StringComparison]::Ordinal)) ('the journal path is not redacted: ' + $journalField)
                    Assert-True ($journalField.IndexOf($fixture, [StringComparison]::OrdinalIgnoreCase) -lt 0) 'the journal path still carries the fixture root'
                }
                finally {
                    if ($null -eq $previous) { Remove-Item Env:\TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE -ErrorAction SilentlyContinue }
                    else { $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE = $previous }
                    Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue
                    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
        }
        @{
            Name = 'override: a partial private-journal selection is refused as a set'
            Body = {
                $root = New-VisualGateFixtureRoot -OwnerState 'dead'
                $inside = Join-Path $root 'theme-recovery.json'
                $outside = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visualgate-outside-' + [guid]::NewGuid().ToString('N') + '.json')
                $canonical = Get-UiAcceptanceThemeJournalPath
                # A reviewed prefix on a directory that does not exist is still not a fixture root:
                # its presence would answer "absent" and clear the gate before any C# boundary.
                $missing = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-theme-fixture-' + [guid]::NewGuid().ToString('N'))
                $rows = @(
                    @{ Journal = ''; Root = ''; Licence = $false; Require = $true; Code = '' }
                    @{ Journal = ''; Root = ''; Licence = $false; Require = $false; Code = '' }
                    @{ Journal = $canonical; Root = ''; Licence = $false; Require = $false; Code = '' }
                    @{ Journal = $inside; Root = ''; Licence = $false; Require = $true; Code = 'theme.override_incomplete' }
                    @{ Journal = $inside; Root = ''; Licence = $false; Require = $false; Code = 'theme.override_incomplete' }
                    @{ Journal = ''; Root = $root; Licence = $false; Require = $true; Code = 'adapters.fixture' }
                    @{ Journal = ''; Root = ''; Licence = $true; Require = $true; Code = 'theme.override_incomplete' }
                    @{ Journal = ''; Root = $root; Licence = $true; Require = $true; Code = 'theme.override_incomplete' }
                    @{ Journal = $inside; Root = $root; Licence = $true; Require = $true; Code = '' }
                    @{ Journal = $inside; Root = $root; Licence = $true; Require = $false; Code = '' }
                    @{ Journal = $outside; Root = $root; Licence = $true; Require = $true; Code = 'theme.override_journal_outside_fixture' }
                    @{ Journal = $inside; Root = (Join-Path $env:SystemRoot 'not-a-fixture'); Licence = $true; Require = $true; Code = 'theme.override_fixture_root' }
                    @{ Journal = (Join-Path $missing 'theme-recovery.json'); Root = $missing; Licence = $true; Require = $true; Code = 'theme.override_fixture_root' }
                )
                foreach ($row in $rows) {
                    $decision = Test-UiAcceptanceThemeOverride -JournalPath $row.Journal -FixtureRoot $row.Root `
                        -AllowFixture:$row.Licence -RequireExplicitJournal:$row.Require
                    $label = 'journal=' + $row.Journal + ' root=' + $row.Root + ' licence=' + $row.Licence + ' require=' + $row.Require
                    Assert-Equal ([bool] ($row.Code -eq '')) ([bool] $decision.ok) ('ok mismatch, ' + $label)
                    Assert-Equal ([string] $row.Code) ([string] $decision.code) ('code mismatch, ' + $label)
                }

                # The missing root is refused before a child could start, not merely named in an
                # error string: the command never reaches the transport.
                $probe = New-VisualGateStub -Responder {
                    param($action, $arguments)
                    throw 'a missing fixture root must not reach the child boundary'
                }
                $refused = Invoke-UiAcceptanceThemeCommand -SupportPath 'stub' -Action 'inspect' `
                    -JournalPath (Join-Path $missing 'theme-recovery.json') `
                    -FixtureRoot $missing -AllowFixture -InvokeSupport $probe.Stub
                Assert-Equal $false ([bool] $refused.Ran) 'the command ran for a missing fixture root'
                Assert-Equal 0 ([int] $probe.State.Calls.Count) 'a child-shaped call happened for a missing root'
                Assert-Equal 'theme.override_fixture_root' ([string] $refused.ErrorText)

                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        },
        @{
            Name = 'entry: an incomplete override is refused before the journal is consulted'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-visualgate-entry2-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                $fixture = New-VisualGateFixtureRoot -OwnerState 'dead'
                $entryScript = Join-Path $script:root 'scripts/run-stage5cc-ui-acceptance.ps1'
                try {
                    # (a) a private journal on its own: no adapter boundary, so no recovery may run.
                    $outA = Join-Path $base 'a'
                    $a = Invoke-VisualEntryChild -Arguments @(
                        $entryScript, '-ProductDirectory', (Join-Path $base 'product'),
                        '-WinAppCliArchivePath', (Join-Path $base 'winapp.zip'),
                        '-OutputDirectory', $outA, '-Scenario', 'SingleInstance',
                        '-ThemeJournalPath', (Join-Path $fixture 'theme-recovery.json')
                    )
                    Assert-Equal 1 ([int] $a.ExitCode)
                    $summaryA = [IO.File]::ReadAllText((Join-Path $outA 'summary.json')) | ConvertFrom-Json
                    Assert-Equal 'theme.override_incomplete' ([string] $summaryA.reason)
                    Assert-True ($null -eq $summaryA.PSObject.Properties['themeJournalGate']) `
                        'the gate ran before the selection was validated'

                    # (b) a fixture root and its licence without a private journal. Without the
                    # guard this shape would fall back to the production journal, which is exactly
                    # what must not happen.
                    $outB = Join-Path $base 'b'
                    $b = Invoke-VisualEntryChild -Arguments @(
                        $entryScript, '-ProductDirectory', (Join-Path $base 'product'),
                        '-WinAppCliArchivePath', (Join-Path $base 'winapp.zip'),
                        '-OutputDirectory', $outB, '-Scenario', 'SingleInstance',
                        '-ThemeFixtureRoot', $fixture, '-AllowThemeFixture'
                    )
                    Assert-Equal 1 ([int] $b.ExitCode)
                    $summaryB = [IO.File]::ReadAllText((Join-Path $outB 'summary.json')) | ConvertFrom-Json
                    Assert-Equal 'theme.override_incomplete' ([string] $summaryB.reason)
                    Assert-True ($null -eq $summaryB.PSObject.Properties['themeJournalGate']) `
                        'the production journal was consulted for a partial selection'
                }
                finally {
                    Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction SilentlyContinue
                    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
        },
        @{
            Name = 'transport: a licensed fixture selection without the process licence is refused by the CLI'
            Body = {
                $cli = Get-VisualGateRealSupportCli
                $fixture = New-VisualGateFixtureRoot -OwnerState 'live'
                $journal = Join-Path $fixture 'theme-recovery.json'
                $registryPath = Join-Path $fixture 'registry.json'
                $broadcastPath = Join-Path $fixture 'broadcast.json'
                $registryBefore = [IO.File]::ReadAllBytes($registryPath)
                $broadcastBefore = [IO.File]::ReadAllText($broadcastPath)
                $previous = $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE
                Remove-Item Env:\TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE -ErrorAction SilentlyContinue
                try {
                    $identity = New-VisualGateIdentity -ProcessId $PID `
                        -StartUtcTicks ((Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks)
                    $binding = New-VisualGateBinding
                    $round = New-UiAcceptanceThemeRoundState -Mode 'light' -OwnerIdentity $identity -Binding $binding

                    # Complete selection, real wrapper, real child, and the child's own licence is
                    # absent: the original refusal code, no journal and no adapter state change.
                    $decision = Invoke-UiAcceptanceThemeApplyPhase -Round $round -JournalPath $journal `
                        -SupportPath $cli -Mode 'light' -FixtureRoot $fixture -AllowFixture

                    Assert-Equal $false ([bool] $decision.ok)
                    Assert-Equal 'exit.2' ([string] $decision.failure)
                    # The consumer keeps its contract: a child that exited with a refusal code says
                    # nothing about what it did before it exited, so the round stays unproven and
                    # still owes a restore. The unchanged leaves below are independent observations
                    # of this fixture, not a statement that the consumer had no write capability.
                    Assert-Equal 'unknown' ([string] $round.WritesPossible) 'a refused exit was read as a clean refusal'
                    Assert-Equal $true ([bool] $round.RestoreOwed) 'the round dropped its restore obligation'
                    Assert-Equal $false ([bool] $round.OwnershipConfirmed)
                    Assert-Equal 'ownership_unconfirmed' ([string] $round.StopReason)
                    Assert-Equal $false ([IO.File]::Exists($journal)) 'the refused apply still wrote a journal'
                    $registryAfter = [IO.File]::ReadAllBytes($registryPath)
                    Assert-Equal $registryBefore.Length $registryAfter.Length
                    $same = $true
                    for ($index = 0; $index -lt $registryBefore.Length; $index++) {
                        if ($registryBefore[$index] -ne $registryAfter[$index]) { $same = $false; break }
                    }
                    Assert-Equal $true $same 'the fixture registry changed'
                    Assert-Equal $broadcastBefore ([IO.File]::ReadAllText($broadcastPath)) 'the fixture broadcast state changed'
                }
                finally {
                    if ($null -eq $previous) { Remove-Item Env:\TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE -ErrorAction SilentlyContinue }
                    else { $env:TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE = $previous }
                    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
        },
        @{
            Name = 'integration: source and moved package drive CurrentOnly and Both through the public entry'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-v-' +
                    [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                Write-Host ('visual-integration evidence retained: ' + $base)
                try {
                    $support = Get-VisualGateRealSupportCli
                    $payloadBase = Join-Path $base 'payload'
                    [void][IO.Directory]::CreateDirectory($payloadBase)
                    $payload = New-VisualIntegrationPayload -Root $script:root -Base $payloadBase `
                        -RealSupportExecutable $support
                    $hostPath = (Get-Process -Id $PID).Path
                    $results = New-Object System.Collections.ArrayList
                    foreach ($treeKind in @('Source', 'Package')) {
                        foreach ($mode in @('CurrentOnly', 'Both')) {
                            $representativeBase = Join-Path $base (
                                $treeKind.Substring(0, 1).ToLowerInvariant() + '-' + $mode.Substring(0, 1).ToLowerInvariant())
                            [void][IO.Directory]::CreateDirectory($representativeBase)
                            $result = Invoke-VisualIntegrationRepresentative -Root $script:root `
                                -Base $representativeBase -Payload $payload -HostPath $hostPath `
                                -TreeKind $treeKind -Mode $mode
                            [void] $results.Add($result)
                            Write-Host ('visual-integration ' + ([ordered] @{
                                treeKind = $result.treeKind
                                mode = $result.mode
                                host = $result.hostVersion
                                status = $result.summaryStatus
                                reportStatus = $result.reportStatus
                                exitCode = $result.entryExitCode
                                entries = $result.entryCount
                                notRun = $result.notRunCount
                                themes = $result.themeCount
                                synthetic = $result.synthetic
                                journalAbsent = $result.journalAbsent
                                facts = $result.FactsPath
                            } | ConvertTo-Json -Compress))
                        }
                    }
                    Assert-Equal 4 $results.Count
                    foreach ($result in $results) {
                        Assert-Equal $true ([bool] $result.synthetic)
                        Assert-Equal 'True' ([string] $result.journalAbsent)
                        $rounds = if ($result.mode -eq 'Both') { 2 } else { 1 }
                        Assert-Equal $rounds ([int] $result.processLaunches)
                        Assert-Equal $rounds ([int] $result.processStops)
                        Assert-Equal $rounds ([int] $result.processInventories)
                        Assert-True ([IO.File]::Exists([string] $result.ledgerSnapshot)) 'the real ledger snapshot is missing'
                        foreach ($leaf in @('setup.json', 'run-inputs.json', 'invocation.json', 'child-result.json', 'stdout.txt', 'stderr.txt', 'facts.json')) {
                            Assert-True ([IO.File]::Exists((Join-Path $result.Base $leaf))) ('missing retained evidence: ' + $leaf)
                        }
                        if ($result.treeKind -eq 'Package') {
                            Assert-Equal $true ([bool] $result.relocation.sourcePackageAbsent)
                            Assert-Equal $true ([bool] $result.relocation.fixtureSourceAbsent)
                            Assert-Equal ([string] $result.relocation.packageBeforeSha256) ([string] $result.relocation.packageAfterSha256)
                            Assert-Equal ([string] $result.relocation.fixtureBeforeSha256) ([string] $result.relocation.fixtureAfterSha256)
                        }
                        Assert-True (@((Get-Content -LiteralPath $result.patchManifest -Raw |
                                ConvertFrom-Json).patches).Count -ge 6) `
                            'the test-owned tree did not carry the expected terminal shims'
                        Assert-True ([IO.File]::Exists([string] $result.screenshots)) `
                            'the consumer index data is missing'
                        Assert-True ([IO.File]::Exists([string] $result.index)) `
                            'the consumer HTML index is missing'
                    }
                }
                catch {
                    Write-VisualIntegrationFirstFailure -Directory $base -Failure $_
                    throw
                }
                # Keep successful and failed evidence, including the short runtime trees named
                # in setup.json. Cleanup is a separate explicit action after independent review.
            }
        }
    )
}
