# TraceFold Stage 5C-C - package-root launcher cases.
#
# What this suite is:
#   * every case builds a moved toolkit with the real package-root launcher, the real helper and
#     the real public entry, then starts the .cmd exactly as an operator would;
#   * the toolkit's support executable is the same compiled test double the offline-entry suite
#     uses, so the entry reaches its runner without a real product, driver or desktop session;
#   * the harness waits (bounded) for the launcher's own terminal line, then proves the process is
#     still alive before it sends the one newline the pause needs, and only then reads the code.
#
# What it is not: the real product/Support/driver, the complete All+ThemeMatrix run, a clean VM, or
# a claim that the visual acceptance passed. The complete mode must refuse with its own code.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The moved-toolkit fixtures (base, product, compiled support double, package and manifest
# helpers) are owned by the offline-entry suite; this suite adds the launcher and drives the .cmd.
. (Join-Path $script:root 'scripts/ui-acceptance/tests/TraceFold.UiAcceptance.OfflineEntry.Tests.ps1')

$script:launcherFinishedLine = '[Run-Stage5CC] Finished. Press Enter to close this window.'

function Add-LauncherToPackage {
    param([Parameter(Mandatory)] $Package)

    Copy-Item -LiteralPath (Join-Path $script:root 'scripts/Run-Stage5CC-Acceptance.cmd') -Destination (Join-Path $Package.Root 'Run-Stage5CC-Acceptance.cmd')
    Copy-Item -LiteralPath (Join-Path $script:root 'scripts/Run-Stage5CC-Acceptance.ps1') -Destination (Join-Path $Package.Root 'scripts/Run-Stage5CC-Acceptance.ps1')

    # The member table is data, and the entry verifies every row; adding the two launcher files
    # means adding their rows with the bytes that are actually on disk.
    $manifestPath = Join-Path $Package.Root 'toolkit.json'
    $manifest = Read-OfflineManifest -Path $manifestPath
    $members = New-Object 'System.Collections.Generic.List[object]'
    foreach ($file in @(Get-ChildItem -LiteralPath $Package.Root -Recurse -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($Package.Root.Length).TrimStart([char] 92) -replace '\\', '/'
        if ($relative -ceq 'toolkit.json') { continue }
        [void] $members.Add([ordered] @{
                path   = $relative
                bytes  = [long] $file.Length
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                source = (Get-OfflineMemberSource -Relative $relative)
            })
    }
    $manifest.members = $members.ToArray()
    Write-OfflineManifest -Path $manifestPath -Document $manifest
    return $Package
}

<#
.SYNOPSIS
    Starts the package-root .cmd and waits for its own terminal line before feeding the pause.

.DESCRIPTION
    The .cmd is started through cmd.exe because a batch file is not a process image. The harness
    reads the launcher's stdout line by line with one pending read and a deadline; it stops only
    when the fixed line the .cmd prints immediately before its pause appears. The helper prints
    the same "Exit code" text earlier, so that text is deliberately not the marker. Only after the
    marker is seen and the process is proven alive is one newline sent; the exit code is then read
    from the process itself.
#>
function Invoke-LauncherCommand {
    param(
        [Parameter(Mandatory)][string] $CommandPath,
        [AllowEmptyCollection()][string[]] $Arguments = @(),
        [Parameter(Mandatory)][string] $WorkingDirectory,
        [int] $PromptTimeoutSeconds = 60
    )

    $quoted = @($Arguments | ForEach-Object {
            if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
        })
    $commandLine = '""' + $CommandPath + '"'
    if ($quoted.Count -gt 0) { $commandLine += ' ' + ($quoted -join ' ') }
    $commandLine += '"'

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $env:ComSpec
    $startInfo.Arguments = '/d /c ' + $commandLine
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $errorTask = $process.StandardError.ReadToEndAsync()
    $lines = New-Object System.Collections.Generic.List[string]
    $promptSeen = $false
    $promptAt = -1
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $deadline = [DateTime]::UtcNow.AddSeconds($PromptTimeoutSeconds)
    $reader = $process.StandardOutput
    $pending = $reader.ReadLineAsync()
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($pending.Wait(100)) {
            $line = $pending.Result
            if ($null -eq $line) { break }
            [void] $lines.Add($line)
            if ($line.TrimEnd() -ceq $script:launcherFinishedLine) {
                $promptSeen = $true
                $promptAt = [int] $watch.ElapsedMilliseconds
                break
            }
            $pending = $reader.ReadLineAsync()
            continue
        }
        if ($process.HasExited) { break }
    }
    $watch.Stop()

    $aliveAtPrompt = $false
    if ($promptSeen) {
        $aliveAtPrompt = -not $process.HasExited
        try { $process.StandardInput.WriteLine('') } catch { }
        try { $process.StandardInput.Close() } catch { }
        if (-not $process.WaitForExit(120000)) {
            try { $process.Kill() } catch { }
            throw ('the launcher did not exit after the console input; lines: ' + ($lines -join ' | '))
        }
        $rest = $reader.ReadToEnd()
        if (-not [string]::IsNullOrEmpty($rest)) {
            foreach ($line in ($rest -split "`r?`n")) {
                if (-not [string]::IsNullOrEmpty($line)) { [void] $lines.Add($line) }
            }
        }
    }
    else {
        try { $process.Kill() } catch { }
        try { [void] $process.WaitForExit(5000) } catch { }
    }

    $errorText = ''
    if ($errorTask.Wait(5000)) { $errorText = $errorTask.Result }

    return [pscustomobject] @{
        PromptSeen            = $promptSeen
        PromptAtMilliseconds  = $promptAt
        AliveAtPrompt         = $aliveAtPrompt
        ExitCode              = $process.ExitCode
        Lines                 = @($lines.ToArray())
        Output                = (($lines.ToArray()) -join [Environment]::NewLine) + $errorText
        ErrorText             = $errorText
    }
}

function Write-LauncherEvidence {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)] $Result
    )

    $directory = $env:TRACEFOLD_LAUNCHER_EVIDENCE
    if ([string]::IsNullOrWhiteSpace($directory)) {
        $directory = Join-Path ([IO.Path]::GetTempPath()) 'tracefold-launcher-evidence'
    }
    [void][IO.Directory]::CreateDirectory($directory)
    $safe = $Name -replace '[^A-Za-z0-9.-]', '-'
    $path = Join-Path $directory ('launcher-' + $safe + '.txt')
    $rows = New-Object System.Collections.Generic.List[string]
    [void] $rows.Add('case=' + $Name)
    [void] $rows.Add('prompt_seen=' + $Result.PromptSeen)
    [void] $rows.Add('prompt_at_ms=' + $Result.PromptAtMilliseconds)
    [void] $rows.Add('alive_at_prompt=' + $Result.AliveAtPrompt)
    [void] $rows.Add('exit_code=' + $Result.ExitCode)
    [void] $rows.Add('--- stdout ---')
    foreach ($line in @($Result.Lines)) { [void] $rows.Add([string] $line) }
    [void] $rows.Add('--- stderr ---')
    [void] $rows.Add([string] $Result.ErrorText)
    [IO.File]::WriteAllLines($path, $rows.ToArray(), [Text.UTF8Encoding]::new($false))
}

function New-LauncherFixture {
    param(
        [Parameter(Mandatory)][string] $Base,
        [string] $Scenario = 'StartupLayout',
        [ValidateSet('Passed', 'Failed')][string] $Outcome = 'Passed',
        [string] $PackageName = 'toolkit',
        [string] $ParentName = ''
    )

    $parent = if ([string]::IsNullOrWhiteSpace($ParentName)) { $Base } else { Join-Path $Base $ParentName }
    [void][IO.Directory]::CreateDirectory($parent)
    $payload = New-OfflineSupportPayload
    $package = New-OfflinePackage -Root (Join-Path $parent $PackageName) -SupportPayload $payload -Scenario $Scenario -Outcome $Outcome
    $null = Add-LauncherToPackage -Package $package
    $product = New-OfflineProduct -Path (Join-Path $parent 'TraceFold-MediaDesktop-Stage5C-C-win-x64')
    $working = Join-Path $Base 'cwd'
    [void][IO.Directory]::CreateDirectory($working)
    return [pscustomobject] @{
        Package = $package
        Product = $product
        Cwd     = $working
        Command = Join-Path $package.Root 'Run-Stage5CC-Acceptance.cmd'
    }
}

function Get-TestCases {
    return @(
        @{
            Name = 'the package-root launcher refuses the complete mode with code 3 and pauses'
            Body = {
                $base = New-OfflineBase
                try {
                    $fixture = New-LauncherFixture -Base $base
                    $result = Invoke-LauncherCommand -CommandPath $fixture.Command -WorkingDirectory $fixture.Cwd
                    Write-LauncherEvidence -Name 'complete-mode' -Result $result
                    Assert-True $result.PromptSeen 'the launcher terminal line must appear'
                    Assert-True $result.AliveAtPrompt 'the launcher must still be waiting after its terminal line'
                    Assert-Equal 3 $result.ExitCode ('complete mode exit code; output: ' + $result.Output)
                    Assert-Contains $result.Output 'ThemeMatrix' ('the missing capability must be named: ' + $result.Output)
                    Assert-Contains $result.Output 'Not ready' ('the refusal must be explicit: ' + $result.Output)
                    Assert-Contains $result.Output 'Exit code: 3' ('the summary line must carry the code: ' + $result.Output)
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $fixture.Package.Root 'results'))) 'the complete mode must not create a results leaf'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an entry refusal without a summary is reported as not generated'
            Body = {
                $base = New-OfflineBase
                try {
                    $fixture = New-LauncherFixture -Base $base
                    # Lower-case all is not the complete-mode alias: the public entry's registry
                    # lookup is exact and refuses it as Unknown scenario before writing a summary.
                    $result = Invoke-LauncherCommand -CommandPath $fixture.Command -WorkingDirectory $fixture.Cwd -Arguments @('-Scenario', 'all')
                    Write-LauncherEvidence -Name 'no-summary' -Result $result
                    Assert-True $result.PromptSeen 'the launcher terminal line must appear'
                    Assert-True $result.AliveAtPrompt 'the launcher must still be waiting after its terminal line'
                    Assert-Equal 2 $result.ExitCode ('the entry refusal code; output: ' + $result.Output)
                    Assert-Contains $result.Output 'No summary was generated' ('the helper must say the summary is absent: ' + $result.Output)
                    Assert-Contains $result.Output 'Status: not reported' ('the helper must not guess a status: ' + $result.Output)
                    Assert-NotContains $result.Output 'Generated:' ('nothing may be announced as generated: ' + $result.Output)
                    Assert-Contains $result.Output 'Exit code: 2' ('the native code must be preserved: ' + $result.Output)
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $fixture.Package.Root 'results'))) 'the refusal must not create a results leaf'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the launcher runs one scenario from a package under a path with spaces and Chinese'
            Body = {
                $base = New-OfflineBase
                try {
                    # A real non-ASCII directory name, built from code points so this test file
                    # stays ASCII: Windows PowerShell decodes a BOM-less script as ANSI.
                    $chineseName = (@(0x5305, 0x7A7A, 0x683C) | ForEach-Object { [string] [char] $_ }) -join ' '
                    $fixture = New-LauncherFixture -Base $base -ParentName $chineseName
                    $result = Invoke-LauncherCommand -CommandPath $fixture.Command -WorkingDirectory $fixture.Cwd -Arguments @('-Scenario', 'StartupLayout') -PromptTimeoutSeconds 120
                    Write-LauncherEvidence -Name 'success-chinese' -Result $result
                    Assert-True $result.PromptSeen 'the launcher terminal line must appear'
                    Assert-True $result.AliveAtPrompt 'the launcher must still be waiting after its terminal line'
                    Assert-Equal 0 $result.ExitCode ('the controlled scenario must pass; output: ' + $result.Output)
                    Assert-Contains $result.Output 'Status: Passed' ('the summary status must be reported: ' + $result.Output)
                    Assert-Contains $result.Output 'Completed.' ('a passing run must say so: ' + $result.Output)

                    # The package was found from the launcher itself, not from the caller cwd; the
                    # default results leaf lives under the package and nothing was written to cwd.
                    # (The captured console re-encodes non-ASCII paths, so the path proof is the
                    # filesystem below, not a text comparison.)
                    Assert-Contains $result.Output 'Scenario: StartupLayout' ('the scenario must be on the console: ' + $result.Output)
                    $leaf = Find-OfflineResultLeaf -ResultsRoot (Join-Path $fixture.Package.Root 'results')
                    Assert-True ($null -ne $leaf) 'the default results leaf must be under the toolkit'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $fixture.Cwd 'results'))) 'the caller cwd must not receive results'
                    $summary = Read-OfflineJson -Path (Join-Path $leaf 'summary.json')
                    Assert-Equal 'Passed' ([string] $summary.status)
                    Assert-True (Test-Path -LiteralPath (Join-Path $leaf 'StartupLayout\report.json')) 'the scenario report must exist'
                    Assert-Contains $result.Output 'Generated: ' ('only existing results may be announced: ' + $result.Output)
                    Assert-NotContains $result.Output 'index.html' 'this scenario produced no screenshot index, so it must not be announced'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a failed scenario keeps the entry code and pauses with the summary'
            Body = {
                $base = New-OfflineBase
                try {
                    $fixture = New-LauncherFixture -Base $base -Outcome 'Failed'
                    $leaf = Join-Path $base 'failed-run'
                    $result = Invoke-LauncherCommand -CommandPath $fixture.Command -WorkingDirectory $fixture.Cwd -Arguments @('-Scenario', 'StartupLayout', '-OutputDirectory', $leaf) -PromptTimeoutSeconds 120
                    Write-LauncherEvidence -Name 'failed' -Result $result
                    Assert-True $result.PromptSeen 'the launcher terminal line must appear'
                    Assert-True $result.AliveAtPrompt 'the launcher must still be waiting after its terminal line'
                    Assert-Equal 1 $result.ExitCode ('a failed scenario maps to the entry code; output: ' + $result.Output)
                    Assert-Contains $result.Output 'Status: Failed' ('the failed status must be reported: ' + $result.Output)
                    $summary = Read-OfflineJson -Path (Join-Path $leaf 'summary.json')
                    Assert-Equal 'Failed' ([string] $summary.status)
                    Assert-Contains $result.Output 'Exit code: 1' ('the code must be on the console: ' + $result.Output)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a blocked run reports Blocked and pauses'
            Body = {
                $base = New-OfflineBase
                try {
                    $fixture = New-LauncherFixture -Base $base
                    # An existing but empty product directory: the entry reaches its preflight and
                    # reports Blocked instead of starting anything.
                    $emptyProduct = Join-Path $base 'empty-product'
                    [void][IO.Directory]::CreateDirectory($emptyProduct)
                    $leaf = Join-Path $base 'blocked-run'
                    $result = Invoke-LauncherCommand -CommandPath $fixture.Command -WorkingDirectory $fixture.Cwd -Arguments @('-Scenario', 'StartupLayout', '-ProductDirectory', $emptyProduct, '-OutputDirectory', $leaf) -PromptTimeoutSeconds 120
                    Write-LauncherEvidence -Name 'blocked' -Result $result
                    Assert-True $result.PromptSeen 'the launcher terminal line must appear'
                    Assert-True $result.AliveAtPrompt 'the launcher must still be waiting after its terminal line'
                    Assert-Equal 1 $result.ExitCode ('a blocked run maps to the entry code; output: ' + $result.Output)
                    Assert-Contains $result.Output 'Status: Blocked' ('the blocked status must be reported: ' + $result.Output)
                    $summary = Read-OfflineJson -Path (Join-Path $leaf 'summary.json')
                    Assert-Equal 'Blocked' ([string] $summary.status)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a missing product is refused before anything starts'
            Body = {
                $base = New-OfflineBase
                try {
                    $fixture = New-LauncherFixture -Base $base
                    $missing = Join-Path $base 'no-such-product'
                    $result = Invoke-LauncherCommand -CommandPath $fixture.Command -WorkingDirectory $fixture.Cwd -Arguments @('-Scenario', 'StartupLayout', '-ProductDirectory', $missing)
                    Write-LauncherEvidence -Name 'missing-product' -Result $result
                    Assert-True $result.PromptSeen 'the launcher terminal line must appear'
                    Assert-True $result.AliveAtPrompt 'the launcher must still be waiting after its terminal line'
                    Assert-Equal 4 $result.ExitCode ('a missing product is a pre-launch error; output: ' + $result.Output)
                    Assert-Contains $result.Output 'The product directory was not found' ('the reason must be explicit: ' + $result.Output)
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $fixture.Package.Root 'results'))) 'nothing may be created for a pre-launch error'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an existing results leaf is refused without overwriting it'
            Body = {
                $base = New-OfflineBase
                try {
                    $fixture = New-LauncherFixture -Base $base
                    $leaf = Join-Path $base 'existing-run'
                    [void][IO.Directory]::CreateDirectory($leaf)
                    [IO.File]::WriteAllText((Join-Path $leaf 'keep.txt'), 'keep', [Text.UTF8Encoding]::new($false))
                    $result = Invoke-LauncherCommand -CommandPath $fixture.Command -WorkingDirectory $fixture.Cwd -Arguments @('-Scenario', 'StartupLayout', '-OutputDirectory', $leaf)
                    Write-LauncherEvidence -Name 'existing-leaf' -Result $result
                    Assert-True $result.PromptSeen 'the launcher terminal line must appear'
                    Assert-True $result.AliveAtPrompt 'the launcher must still be waiting after its terminal line'
                    Assert-Equal 4 $result.ExitCode ('an existing leaf is a pre-launch error; output: ' + $result.Output)
                    Assert-Contains $result.Output 'already exists' ('the reason must be explicit: ' + $result.Output)
                    Assert-Equal 'keep' ([IO.File]::ReadAllText((Join-Path $leaf 'keep.txt'))) 'the existing leaf must not be touched'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        }
    )
}
