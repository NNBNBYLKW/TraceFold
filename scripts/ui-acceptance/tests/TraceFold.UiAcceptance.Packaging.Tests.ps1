# TraceFold Stage 5C-C - package producer cases.
#
# What this suite is:
#   * every case builds a fresh fixture repository and starts the REAL publish script through
#     -File, with the real winapp v0.5.0 archive and a real dist inside that repository;
#   * the SDK is a stand-in that creates the product and support payloads, so nothing here is a
#     real product publish; each case writes that limitation into its own name;
#   * the toolkit is assembled from the candidate sources, its member table is generated from the
#     staged bytes, both ZIPs are really created, the structural smokes run, and the four final
#     targets are really replaced.
#
# What it is not: a real product build, a real desktop probe, a release archive for the repository,
# or a claim that Task 14 is accepted. The product smoke is exercised with a request stand-in; its
# real launch layer stays NotRun.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- fixture helpers --------------------------------------------------------------------------

function New-PackagingAttempt {
    # The name stays short on purpose: the extracted driver and the staged support payload sit
    # several directories below this root, and the Win32 version API cannot read a file beyond
    # MAX_PATH even when the file itself was copied correctly.
    # Stay under the caller's owned TEMP with the prefix required by Remove-UiTestTree.
    $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-p-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    [void][IO.Directory]::CreateDirectory($base)
    return $base
}

function Write-PackagingText {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Text
    )
    $directory = [IO.Path]::GetDirectoryName($Path)
    if (-not [IO.Directory]::Exists($directory)) { [void][IO.Directory]::CreateDirectory($directory) }
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Read-PackagingJson {
    # Windows PowerShell decodes a BOM-less UTF-8 file as ANSI, which corrupts every non-ASCII
    # string before ConvertFrom-Json sees it. ReadAllText detects the BOM and defaults to UTF-8.
    param([Parameter(Mandatory)][string] $Path)
    return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json)
}

function Invoke-PackagingGit {
    # Windows PowerShell turns a native writer's stderr into a terminating error while the
    # preference is Stop, and git writes harmless CRLF advisories to stderr. The call therefore
    # runs with Continue and the real exit code is kept.
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Arguments)
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& git @Arguments 2>&1)
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    return [pscustomobject] @{ ExitCode = $code; Output = ($output -join ' ') }
}

function New-PackagingFfmpegRoot {
    param([Parameter(Mandatory)][string] $Path)
    [void][IO.Directory]::CreateDirectory((Join-Path $Path 'bin'))
    $ffmpegBytes = [Text.Encoding]::ASCII.GetBytes('MZ fixture ffmpeg' + ('0' * 64))
    $ffprobeBytes = [Text.Encoding]::ASCII.GetBytes('MZ fixture ffprobe' + ('1' * 64))
    [IO.File]::WriteAllBytes((Join-Path $Path 'bin\ffmpeg.exe'), $ffmpegBytes)
    [IO.File]::WriteAllBytes((Join-Path $Path 'bin\ffprobe.exe'), $ffprobeBytes)
    Write-PackagingText -Path (Join-Path $Path 'LICENSE') -Text 'fixture ffmpeg license'
    Write-PackagingText -Path (Join-Path $Path 'README.txt') -Text 'fixture ffmpeg readme'
    return [pscustomobject] @{
        ffmpeg  = [pscustomobject] @{ name = 'ffmpeg.exe'; bytes = $ffmpegBytes.Length; sha256 = (Get-FileHash -LiteralPath (Join-Path $Path 'bin\ffmpeg.exe') -Algorithm SHA256).Hash }
        ffprobe = [pscustomobject] @{ name = 'ffprobe.exe'; bytes = $ffprobeBytes.Length; sha256 = (Get-FileHash -LiteralPath (Join-Path $Path 'bin\ffprobe.exe') -Algorithm SHA256).Hash }
    }
}

function Write-PackagingDotNetShim {
    param([Parameter(Mandatory)][string] $BinDirectory)

    $shim = @'
$ErrorActionPreference = 'Stop'
# No param block on purpose: the producer passes dotnet's own switches (-o, -p:..., --source),
# and named binding would reject several of them. Everything arrives in $args.
$rawArguments = @($args)
# Windows PowerShell -File splits native -p:Name=value into -p and Name=value before this
# script sees it. Adapt that observed transport shape; keep the original tokens as evidence.
$Arguments = @(
    for ($argumentIndex = 0; $argumentIndex -lt $rawArguments.Count; $argumentIndex++) {
        $argument = [string] $rawArguments[$argumentIndex]
        if ($argument -ceq '-p' -and ($argumentIndex + 1) -lt $rawArguments.Count -and
            [string] $rawArguments[$argumentIndex + 1] -cmatch '^[A-Za-z_][A-Za-z0-9_.]*=') {
            '-p:' + [string] $rawArguments[$argumentIndex + 1]
            $argumentIndex++
        }
        else { $argument }
    }
)
$journal = $env:TRACEFOLD_FIXTURE_DOTNET_JOURNAL
if (-not [string]::IsNullOrWhiteSpace($journal)) {
    Add-Content -LiteralPath $journal -Value (ConvertTo-Json -InputObject ([ordered] @{ rawArguments = $rawArguments; arguments = $Arguments }) -Compress)
}
function Get-ShimArgument {
    param([string] $Name)
    for ($index = 0; $index -lt ($Arguments.Count - 1); $index++) {
        if ($Arguments[$index] -eq $Name) { return [string] $Arguments[$index + 1] }
    }
    return ''
}
$mode = if ($Arguments.Count -gt 0) { [string] $Arguments[0] } else { '' }
$project = if ($Arguments.Count -gt 1) { [string] $Arguments[1] } else { '' }
$cacheArguments = @($Arguments | Where-Object { $_ -like '-p:RestorePackagesPath=*' })
if ($cacheArguments.Count -ne 1) { throw 'shim: every SDK call must name exactly one local package cache' }
$cache = ([string] $cacheArguments[0]).Substring('-p:RestorePackagesPath='.Length)
if ($mode -eq 'restore') {
    if ((Get-ShimArgument '--source') -cne $cache) { throw 'shim: restore source and package cache must match' }
    if ($project -like '*UiAcceptance.Support*') {
        if ((Get-ShimArgument '-r') -cne 'win-x64' -or $Arguments -notcontains '-p:SelfContained=true' -or
            $Arguments -notcontains '-p:Configuration=Release') { throw 'shim: support restore must resolve the Release self-contained win-x64 target' }
        if ($env:TRACEFOLD_FIXTURE_FAIL_SUPPORT_RESTORE -eq '1') { exit 6 }
        [IO.File]::WriteAllText(($journal + '.support-rid'), ($project + '|' + $cache))
    }
    exit 0
}
if ($mode -eq 'test') {
    $results = Get-ShimArgument '--results-directory'
    if ((Get-ShimArgument '--logger') -cne 'trx' -or [string]::IsNullOrWhiteSpace($results)) { throw 'shim: test must request TRX in an explicit results directory' }
    if (-not [IO.Directory]::Exists($results)) { throw 'shim: publisher must create its owned evidence leaf' }
    if (@(Get-ChildItem -LiteralPath $results -Filter '*.trx' -File).Count -ne 0) { throw 'shim: evidence leaf was reused' }
    if ($env:TRACEFOLD_FIXTURE_OMIT_TRX -ne '1') {
        $outcome = if ($env:TRACEFOLD_FIXTURE_FAIL_TEST -eq '1') { 'Failed' } else { 'Passed' }
        # This is a controlled SDK shape, never a report of real .NET test execution.
        [IO.File]::WriteAllText((Join-Path $results 'fixture.trx'),
            ('<TestRun name="CONTROLLED PACKAGING SDK FIXTURE"><ResultSummary outcome="' + $outcome + '" /></TestRun>'))
    }
    if ($env:TRACEFOLD_FIXTURE_FAIL_TEST -eq '1') { exit 7 }
    exit 0
}
if ($mode -ne 'publish') { throw ('shim: unsupported SDK operation: ' + $mode) }
if ($Arguments -notcontains '--no-restore') { throw 'shim: publish must not implicitly restore' }
$out = ''
for ($index = 0; $index -lt $Arguments.Count; $index++) {
    if ($Arguments[$index] -eq '-o' -and ($index + 1) -lt $Arguments.Count) { $out = [string] $Arguments[$index + 1] }
}
if ([string]::IsNullOrWhiteSpace($out)) { [Console]::Error.WriteLine('shim: no -o argument'); exit 4 }
$project = if ($Arguments.Count -gt 1) { [string] $Arguments[1] } else { '' }
[void][IO.Directory]::CreateDirectory($out)
if ($project -like '*UiAcceptance.Support*') {
    if (-not [IO.File]::Exists(($journal + '.support-rid')) -or
        [IO.File]::ReadAllText(($journal + '.support-rid')) -cne ($project + '|' + $cache)) {
        throw 'shim: support publish has no matching RID restore in this invocation'
    }
    if ((Get-ShimArgument '-r') -cne 'win-x64' -or (Get-ShimArgument '--self-contained') -cne 'true') {
        throw 'shim: support publish changed its restored target'
    }
    if ($env:TRACEFOLD_FIXTURE_FAIL_SUPPORT -eq '1') { exit 5 }
    [IO.File]::WriteAllBytes((Join-Path $out 'TraceFold.Media.UiAcceptance.Support.exe'), [Text.Encoding]::ASCII.GetBytes('MZ fixture support'))
    [IO.File]::WriteAllText((Join-Path $out 'TraceFold.Media.UiAcceptance.Support.dll'), 'fixture support assembly')
    [IO.File]::WriteAllText((Join-Path $out 'TraceFold.Media.UiAcceptance.Support.runtimeconfig.json'), '{"runtimeOptions":{}}')
    [IO.File]::WriteAllText((Join-Path $out 'Microsoft.Data.Sqlite.dll'), 'fixture sqlite dependency')
    [IO.File]::WriteAllText((Join-Path $out 'SQLitePCLRaw.core.dll'), 'fixture sqlite raw dependency')
    exit 0
}
[IO.File]::WriteAllBytes((Join-Path $out 'TraceFold.Media.exe'), [Text.Encoding]::ASCII.GetBytes('MZ fixture product'))
[IO.File]::WriteAllText((Join-Path $out 'TraceFold.Media.dll'), 'fixture product assembly')
$ffmpegRoot = [string] $env:TRACEFOLD_FIXTURE_FFMPEG
$toolRoot = Join-Path $out 'tools\ffmpeg'
[void][IO.Directory]::CreateDirectory($toolRoot)
foreach ($name in @('ffmpeg.exe', 'ffprobe.exe')) {
    Copy-Item -LiteralPath (Join-Path $ffmpegRoot ('bin\' + $name)) -Destination $toolRoot
}
Copy-Item -LiteralPath (Join-Path $ffmpegRoot 'LICENSE') -Destination $toolRoot
Copy-Item -LiteralPath (Join-Path $ffmpegRoot 'README.txt') -Destination $toolRoot
if ($env:TRACEFOLD_FIXTURE_CONTAMINATE -eq 'flat') {
    # The shape the flat gate has to catch: a support dependency at the product root, outside any
    # directory the older rules looked at.
    [IO.File]::WriteAllText((Join-Path $out 'TraceFold.Media.UiAcceptance.Support.dll'), 'flat acceptance payload')
}
elseif ($env:TRACEFOLD_FIXTURE_CONTAMINATE -eq '1') {
    $leak = Join-Path $out 'scripts\ui-acceptance'
    [void][IO.Directory]::CreateDirectory($leak)
    [IO.File]::WriteAllText((Join-Path $leak 'leaked.ps1'), '# acceptance payload that must never ride along')
}
elseif ($env:TRACEFOLD_FIXTURE_CONTAMINATE -in @('artifacts', '.cache', '.worktrees')) {
    $leak = Join-Path $out $env:TRACEFOLD_FIXTURE_CONTAMINATE
    [void][IO.Directory]::CreateDirectory($leak)
    [IO.File]::WriteAllText((Join-Path $leak 'private.txt'), 'CONTROLLED private workspace payload')
    if ($env:TRACEFOLD_FIXTURE_HIDE_PRIVATE -eq '1') {
        [IO.File]::SetAttributes($leak, ([IO.File]::GetAttributes($leak) -bor [IO.FileAttributes]::Hidden))
        if (-not ([IO.File]::GetAttributes($leak) -band [IO.FileAttributes]::Hidden)) { throw 'shim: Hidden attribute was not set' }
    }
}
exit 0
'@
    Write-PackagingText -Path (Join-Path $BinDirectory 'dotnet-shim.ps1') -Text $shim
    $wrapper = @'
@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dotnet-shim.ps1" %*
exit /b %ERRORLEVEL%
'@
    Write-PackagingText -Path (Join-Path $BinDirectory 'dotnet.cmd') -Text $wrapper
}

function Copy-PackagingSource {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination
    )
    $directory = [IO.Path]::GetDirectoryName($Destination)
    if (-not [IO.Directory]::Exists($directory)) { [void][IO.Directory]::CreateDirectory($directory) }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

<#
.SYNOPSIS
    Builds one throwaway repository that the real producer can be pointed at.
.DESCRIPTION
    The candidate scripts are copied from this checkout, so the code under test is the code being
    delivered. Everything the release is assembled from is present: the pinned driver manifest and
    licenses, the contract with its fixtures, the support project, the launcher and the documents
    the checkpoint requires. The launcher, the guide, the acceptance record and the visual matrix
    scenario are controlled test shapes, clearly marked as such: they exist in this fixture so the
    readiness gate can be exercised, and they are never added to the repository.
#>
function New-PackagingFixture {
    param(
        [Parameter(Mandatory)][string] $Attempt,
        [string] $Name = 'default',
        [switch] $WithoutLauncher,
        [switch] $WithoutVisualScenario
    )

    $repo = Join-Path $Attempt 'fixture-repo'
    if (-not [IO.File]::Exists($script:packagingWinAppArchive)) {
        throw ('the pinned v0.5.0 driver archive is required: ' + $script:packagingWinAppArchive)
    }
    if ([IO.Directory]::Exists($repo)) { Remove-Item -LiteralPath $repo -Recurse -Force }
    [void][IO.Directory]::CreateDirectory($repo)

    foreach ($relative in @(
            'scripts/publish-media-desktop.ps1',
            'scripts/smoke-media-desktop-package.ps1',
            'scripts/smoke-stage5cc-ui-acceptance-toolkit.ps1',
            'scripts/run-stage5cc-ui-acceptance.ps1',
            'scripts/Run-Stage5CC-Acceptance.ps1',
            'scripts/ui-acceptance/README.txt',
            'scripts/test-stage5cc-ui-acceptance-toolkit.ps1',
            'apps/media-desktop/TraceFold.MediaDesktop.slnx',
            'apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj',
            'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/TraceFold.Media.UiAcceptance.Support.csproj',
            'apps/media-desktop/tools/winapp/win-x64/manifest.json',
            'apps/media-desktop/tools/winapp/win-x64/LICENSE.txt',
            'apps/media-desktop/tools/winapp/win-x64/SOURCE-NOTICE.txt',
            'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json')) {
        Copy-PackagingSource -Source (Join-Path $script:root ($relative -replace '/', '\')) -Destination (Join-Path $repo ($relative -replace '/', '\'))
    }
    foreach ($module in @(Get-ChildItem -LiteralPath $script:uiRoot -File -Filter '*.psm1')) {
        Copy-PackagingSource -Source $module.FullName -Destination (Join-Path $repo ('scripts\ui-acceptance\' + $module.Name))
    }
    foreach ($scenario in @(Get-ChildItem -LiteralPath (Join-Path $script:uiRoot 'scenarios') -File | Where-Object { $_.Name -ne '70-visual-keyboard-matrix.ps1' })) {
        Copy-PackagingSource -Source $scenario.FullName -Destination (Join-Path $repo ('scripts\ui-acceptance\scenarios\' + $scenario.Name))
    }
    Copy-Item -LiteralPath (Join-Path $script:uiRoot 'report-template') -Destination (Join-Path $repo 'scripts\ui-acceptance\report-template') -Recurse
    foreach ($fixture in @(Get-ChildItem -LiteralPath (Join-Path $script:root 'apps\media-api\tests\fixtures\media') -File)) {
        Copy-PackagingSource -Source $fixture.FullName -Destination (Join-Path $repo ('apps\media-api\tests\fixtures\media\' + $fixture.Name))
    }

    $ffmpegRoot = Join-Path $Attempt 'ffmpeg'
    $ffmpegFact = New-PackagingFfmpegRoot -Path $ffmpegRoot
    Write-PackagingText -Path (Join-Path $repo 'apps\media-desktop\tools\ffmpeg\win-x64\THIRD-PARTY-NOTICES.txt') -Text 'fixture third party notices'
    $ffmpegManifest = [ordered] @{
        files = [ordered] @{
            'ffmpeg.exe'  = [ordered] @{ size = [long] $ffmpegFact.ffmpeg.bytes; sha256 = $ffmpegFact.ffmpeg.sha256 }
            'ffprobe.exe' = [ordered] @{ size = [long] $ffmpegFact.ffprobe.bytes; sha256 = $ffmpegFact.ffprobe.sha256 }
        }
    }
    Write-PackagingText -Path (Join-Path $repo 'apps\media-desktop\tools\ffmpeg\win-x64\manifest.json') -Text ($ffmpegManifest | ConvertTo-Json -Depth 5)

    if (-not $WithoutLauncher) {
        # The real launcher: the package the fixture produces must carry the same bytes the
        # toolkit will, not a marker that only satisfies the readiness gate.
        Copy-PackagingSource -Source (Join-Path $script:root 'scripts\Run-Stage5CC-Acceptance.cmd') -Destination (Join-Path $repo 'scripts\Run-Stage5CC-Acceptance.cmd')
    }
    if (-not $WithoutVisualScenario) {
        # The registry's Visual entry points at this file. The real matrix is not run by the
        # packaging tests; a controlled shape satisfies the readiness gate and nothing else.
        Write-PackagingText -Path (Join-Path $repo 'scripts\ui-acceptance\scenarios\70-visual-keyboard-matrix.ps1') -Text "# CONTROLLED TEST SHAPE - visual matrix scenario marker for the readiness gate.`n"
    }
    Write-PackagingText -Path (Join-Path $repo 'docs\media-desktop-stage5cc-visual-uia-test-guide.md') -Text "# CONTROLLED TEST SHAPE - test guide marker.`n"
    Write-PackagingText -Path (Join-Path $repo 'docs\media-desktop-stage5cc-visual-uia-acceptance.md') -Text "# CONTROLLED TEST SHAPE - acceptance record marker.`n"

    $bin = Join-Path $Attempt 'bin'
    [void][IO.Directory]::CreateDirectory($bin)
    Write-PackagingDotNetShim -BinDirectory $bin
    # The product gate runs the complete smoke, which starts the process under test. The fixture
    # product is a byte stub, so every fixture publish hands the gate a controlled launcher that
    # exits 0: the four requests, waits and exit-code rules are the real ones, the recorded
    # launch result stays NotRun, and no window is opened.
    $probeLauncher = Join-Path $Attempt 'probe-launcher.cmd'
    Write-PackagingText -Path $probeLauncher -Text "@echo off`r`nexit /b 0`r`n"
    [void][IO.Directory]::CreateDirectory((Join-Path $Attempt 'nuget'))
    $archivePath = Join-Path $Attempt 'winappcli-x64.zip'
    # Every case gets its own byte copy: a case that tampers with the archive must not be able to
    # reach the pinned input through a hard link, and the pin has to stay exactly as reviewed.
    Copy-Item -LiteralPath $script:packagingWinAppArchive -Destination $archivePath -Force

    $init = Invoke-PackagingGit -Arguments @('-C', $repo, 'init', '-q')
    if ($init.ExitCode -ne 0) { throw ('the fixture repository could not be initialised: ' + $init.Output) }
    $add = Invoke-PackagingGit -Arguments @('-C', $repo, 'add', '-A')
    if ($add.ExitCode -ne 0) { throw ('the fixture files could not be staged: ' + $add.Output) }
    $commit = Invoke-PackagingGit -Arguments @('-C', $repo, '-c', 'user.name=fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-q', '-m', 'packaging fixture')
    if ($commit.ExitCode -ne 0) { throw ('the fixture repository could not be committed: ' + $commit.Output) }

    return [pscustomobject] @{
        Attempt    = $Attempt
        Repo       = $repo
        FfmpegRoot = $ffmpegRoot
        Archive    = $archivePath
        NugetRoot  = (Join-Path $Attempt 'nuget')
        Bin        = $bin
        Dist       = (Join-Path $repo 'dist')
        Launcher   = $probeLauncher
    }
}

function Invoke-PackagingPublisher {
    param(
        [Parameter(Mandatory)] $Fixture,
        [string] $StageCheckpoint = '5C-C',
        [AllowEmptyCollection()][string[]] $ExtraArguments = @(),
        [hashtable] $Environment = @{},
        [string] $ProbeLauncher = ''
    )

    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
    $arguments = @(
        '-NoProfile', '-File', (Join-Path $Fixture.Repo 'scripts\publish-media-desktop.ps1'),
        '-StageCheckpoint', $StageCheckpoint,
        '-FfmpegSourceRoot', $Fixture.FfmpegRoot,
        '-WinAppCliArchivePath', $Fixture.Archive,
        '-NugetPackageSource', $Fixture.NugetRoot)
    if ($StageCheckpoint -eq '5C-C') {
        # A real publish passes no launcher and the gate starts the product. Every fixture publish
        # has to pass one because the fixture product is a byte stub; the gate still runs its four
        # requests against it. Passing a launcher is not a way to skip the gate.
        $launcher = if ([string]::IsNullOrWhiteSpace($ProbeLauncher)) { [string] $Fixture.Launcher } else { $ProbeLauncher }
        if (-not [string]::IsNullOrWhiteSpace($launcher)) {
            $arguments += @('-ProductSmokeLauncher', $launcher)
        }
    }
    $arguments += $ExtraArguments

    $previousPath = $env:PATH
    $previousLocation = (Get-Location).Path
    $previousErrorAction = $ErrorActionPreference
    $journal = Join-Path $Fixture.Attempt ('dotnet-' + [guid]::NewGuid().ToString('N') + '.log')
    $previousEnvironment = @{}
    try {
        $env:PATH = $Fixture.Bin + ';' + $previousPath
        $env:TRACEFOLD_FIXTURE_DOTNET_JOURNAL = $journal
        $env:TRACEFOLD_FIXTURE_FFMPEG = $Fixture.FfmpegRoot
        foreach ($key in $Environment.Keys) {
            $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, [string] $Environment[$key], 'Process')
        }
        Set-Location -LiteralPath $Fixture.Repo
        # Windows PowerShell escalates a native writer's stderr to a terminating error while the
        # preference is Stop; the producer's own message is the evidence, so the call runs with
        # Continue and the real exit code is read immediately after.
        $ErrorActionPreference = 'Continue'
        $output = (& $hostExe @arguments 2>&1 | Out-String)
        $code = $LASTEXITCODE
        return [pscustomobject] @{ ExitCode = $code; Output = $output; Journal = $journal }
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        $env:PATH = $previousPath
        foreach ($key in $previousEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], 'Process')
        }
        Remove-Item Env:\TRACEFOLD_FIXTURE_DOTNET_JOURNAL -ErrorAction SilentlyContinue
        Remove-Item Env:\TRACEFOLD_FIXTURE_FFMPEG -ErrorAction SilentlyContinue
        Set-Location -LiteralPath $previousLocation
    }
}

function Read-PackagingDotNetCalls {
    param([Parameter(Mandatory)][string] $Path)
    return @(Get-Content -LiteralPath $Path | ForEach-Object { $_ | ConvertFrom-Json })
}

function Get-PackagingArgument {
    param([Parameter(Mandatory)] $Call, [Parameter(Mandatory)][string] $Name)
    $arguments = @($Call.arguments)
    for ($index = 0; $index -lt ($arguments.Count - 1); $index++) {
        if ($arguments[$index] -eq $Name) { return [string] $arguments[$index + 1] }
    }
    return ''
}

function Get-PackagingDirectoryFingerprint {
    param([Parameter(Mandatory)][string] $Path)
    $root = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $rows = foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($root.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
        $relative + '|' + $file.Length + '|' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return ($rows -join "`n")
}

function Get-PackagingZipEntries {
    param([Parameter(Mandatory)][string] $Path)
    try { $null = Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        return @($archive.Entries | ForEach-Object { ($_.FullName -replace '\\', '/') } | Sort-Object)
    }
    finally {
        $archive.Dispose()
    }
}

function New-PackagingArchive {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string[]] $EntryNames
    )
    if ([IO.File]::Exists($Path)) { Remove-Item -LiteralPath $Path -Force }
    try { $null = Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
    try { $null = Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in $EntryNames) {
                $entry = $archive.CreateEntry($name)
                $writer = $entry.Open()
                try {
                    $bytes = [Text.Encoding]::ASCII.GetBytes('fixture entry ' + $name)
                    $writer.Write($bytes, 0, $bytes.Length)
                }
                finally { $writer.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Set-PackagingDriverManifest {
    param(
        [Parameter(Mandatory)] $Fixture,
        [Parameter(Mandatory)][string] $ArchivePath,
        [string] $Version = '0.5.0'
    )
    $manifestPath = Join-Path $Fixture.Repo 'apps\media-desktop\tools\winapp\win-x64\manifest.json'
    $document = Read-PackagingJson -Path $manifestPath
    $document.version = $Version
    $document.archiveSize = [long] ((Get-Item -LiteralPath $ArchivePath).Length)
    $document.sha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    Write-PackagingText -Path $manifestPath -Text ($document | ConvertTo-Json -Depth 5)
    $add = Invoke-PackagingGit -Arguments @('-C', $Fixture.Repo, 'add', '-A')
    if ($add.ExitCode -ne 0) { throw ('the manifest fixture could not be staged: ' + $add.Output) }
    $commit = Invoke-PackagingGit -Arguments @('-C', $Fixture.Repo, '-c', 'user.name=fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-q', '-m', 'manifest fixture')
    if ($commit.ExitCode -ne 0) { throw ('the manifest fixture could not be committed: ' + $commit.Output) }
}

function Remove-PackagingFixtureFile {
    param(
        [Parameter(Mandatory)] $Fixture,
        [Parameter(Mandatory)][string] $Relative
    )
    $path = Join-Path $Fixture.Repo ($Relative -replace '/', '\')
    if ([IO.File]::Exists($path)) { Remove-Item -LiteralPath $path -Force }
    $add = Invoke-PackagingGit -Arguments @('-C', $Fixture.Repo, 'add', '-A')
    if ($add.ExitCode -ne 0) { throw ('the removal could not be staged: ' + $add.Output) }
    $commit = Invoke-PackagingGit -Arguments @('-C', $Fixture.Repo, '-c', 'user.name=fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-q', '-m', 'remove fixture file')
    if ($commit.ExitCode -ne 0) { throw ('the removal could not be committed: ' + $commit.Output) }
}

function Assert-PackagingRefused {
    param(
        [Parameter(Mandatory)] $Fixture,
        [Parameter(Mandatory)][string] $ExpectedText,
        [hashtable] $Environment = @{},
        [AllowEmptyCollection()][string[]] $ExtraArguments = @()
    )
    $result = Invoke-PackagingPublisher -Fixture $Fixture -Environment $Environment -ExtraArguments $ExtraArguments
    Assert-True ($result.ExitCode -ne 0) ('the producer should have refused; output: ' + $result.Output)
    Assert-Contains $result.Output $ExpectedText ('refusal reason; output: ' + $result.Output)
    Assert-True (-not [IO.Directory]::Exists($Fixture.Dist)) 'a refused publish must not create dist'
    return $result
}

# The pinned archive is the local v0.5.0 file; the suite refuses to run without it rather than
# substituting a look-alike. Keep downloads inside this workspace; the producer verifies the pin.
$script:packagingWinAppArchive = Join-Path $script:root '.cache/downloads/winappcli-x64.zip'

function Get-TestCases {
    return @(
        @{
            Name = 'the producer builds both packages, both archives and a consumable manifest'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $result = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $result.ExitCode ('the producer should publish; output: ' + $result.Output)
                    Assert-Contains $result.Output 'Product package gate: complete smoke passed' ('the complete product gate must run before promotion: ' + $result.Output)
                    Assert-Contains $result.Output 'real probe NotRun' ('the controlled run must record NotRun: ' + $result.Output)

                    $calls = @(Read-PackagingDotNetCalls -Path $result.Journal)
                    Assert-Equal 'restore,test,publish,restore,publish' (($calls | ForEach-Object { $_.arguments[0] }) -join ',') 'the Support RID restore must run after solution tests and before Support publish'
                    foreach ($call in $calls) {
                        $cache = @($call.arguments | Where-Object { $_ -like '-p:RestorePackagesPath=*' })
                        Assert-Equal 1 $cache.Count 'every SDK invocation must bind its package cache'
                        Assert-Equal ('-p:RestorePackagesPath=' + $fixture.NugetRoot) ([string] $cache[0]) 'SDK calls must use the fixture-owned equivalent of the workspace cache'
                    }
                    $supportProject = Join-Path $fixture.Repo 'apps\media-desktop\tools\TraceFold.Media.UiAcceptance.Support\TraceFold.Media.UiAcceptance.Support.csproj'
                    Assert-Equal $supportProject ([string] $calls[3].arguments[1]) 'RID restore must target Support'
                    Assert-Equal $supportProject ([string] $calls[4].arguments[1]) 'publish must consume that same project'
                    $stagedProductExe = Join-Path (Get-PackagingArgument -Call $calls[2] -Name '-o') 'TraceFold.Media.exe'
                    $stagedSupportExe = Join-Path (Get-PackagingArgument -Call $calls[4] -Name '-o') 'TraceFold.Media.UiAcceptance.Support.exe'
                    Assert-True ($stagedProductExe.Length -lt 260) ('staged product native path is too long: ' + $stagedProductExe)
                    Assert-True ($stagedSupportExe.Length -lt 260) ('staged support native path is too long: ' + $stagedSupportExe)
                    Assert-Equal 'win-x64' (Get-PackagingArgument -Call $calls[3] -Name '-r')
                    Assert-True ($calls[3].arguments -contains '-p:SelfContained=true') 'RID restore must acquire self-contained runtime packs'
                    Assert-Equal $fixture.NugetRoot (Get-PackagingArgument -Call $calls[3] -Name '--source') 'RID restore must stay offline'
                    $testResults = Get-PackagingArgument -Call $calls[1] -Name '--results-directory'
                    $evidenceParent = Join-Path $fixture.Repo 'artifacts/reports/publish-media-desktop'
                    Assert-Equal $evidenceParent ([IO.Path]::GetDirectoryName($testResults)) 'test evidence must stay in the fixture workspace'
                    Assert-True ([IO.Path]::GetFileName($testResults) -match '^[0-9a-f]{32}$') 'each invocation must own a fresh evidence leaf'
                    Assert-Equal 'trx' (Get-PackagingArgument -Call $calls[1] -Name '--logger')
                    Assert-True ([IO.File]::Exists((Join-Path $testResults 'fixture.trx'))) 'test evidence must survive successful staging cleanup'

                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $productZip = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'
                    $toolkitDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
                    $toolkitZip = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip'
                    foreach ($target in @($productDir, $productZip, $toolkitDir, $toolkitZip)) {
                        Assert-True (Test-Path -LiteralPath $target) ('missing artifact: ' + $target)
                    }

                    $commit = (& git -C $fixture.Repo rev-parse HEAD).Trim()
                    $version = Read-PackagingJson -Path (Join-Path $productDir 'package-version.json')
                    Assert-Equal ('4.0.0-stage5cc+' + $commit) ([string] $version.InformationalVersion) 'the product version must name the source commit'
                    Assert-Equal 'NotRun' ([string] $version.ProductLaunchProbe) 'the structural publish must not claim a launch probe'

                    $manifest = Read-PackagingJson -Path (Join-Path $toolkitDir 'toolkit.json')
                    Assert-Equal 'tracefold-ui-acceptance-toolkit' ([string] $manifest.schema) 'unexpected toolkit schema'
                    Assert-Equal ('4.0.0-stage5cc+' + $commit) ([string] $manifest.informationalVersion) 'the toolkit version must name the source commit'
                    Assert-True (@($manifest.members).Count -gt 30) 'the member table should cover the whole closure'
                    $packagedContract = Read-PackagingJson -Path (Join-Path $toolkitDir 'apps\media-desktop\tools\ui-acceptance-contract\stage5cc-uia-contract.json')
                    $declaredFixtures = @($packagedContract.fixtureAssets)
                    Assert-True ($declaredFixtures.Count -gt 0) 'the contract declares fixtures'
                    Assert-Equal $declaredFixtures.Count ([int] @($manifest.members | Where-Object { $_.source -eq 'fixture' }).Count) 'every declared fixture must be listed'
                    Assert-True ([string] $manifest.driver.archive.sha256 -ne [string] $manifest.driver.sha256) 'the archive digest must not be the executable digest'

                    # MainChain's declaration is a member the entry verifies, and its pins must be
                    # the binaries the product ships.
                    $mediaRelative = 'apps/media-desktop/tools/ffmpeg/win-x64/manifest.json'
                    Assert-Equal 1 ([int] @($manifest.members | Where-Object { $_.path -eq $mediaRelative }).Count) 'the media tools declaration must have one member row'
                    $packagedMedia = Read-PackagingJson -Path (Join-Path $toolkitDir ($mediaRelative -replace '/', '\'))
                    foreach ($toolName in @('ffmpeg.exe', 'ffprobe.exe')) {
                        $productTool = Join-Path $productDir ('tools\ffmpeg\' + $toolName)
                        Assert-Equal ([long] $packagedMedia.files.PSObject.Properties[$toolName].Value.size) ([long] (Get-Item -LiteralPath $productTool).Length) ('the packaged declaration must pin ' + $toolName)
                        Assert-Equal ([string] $packagedMedia.files.PSObject.Properties[$toolName].Value.sha256) ((Get-FileHash -LiteralPath $productTool -Algorithm SHA256).Hash) ('the packaged declaration digest must match ' + $toolName)
                    }

                    $supportFiles = @(Get-ChildItem -LiteralPath (Join-Path $toolkitDir 'runtime\support') -Recurse -File)
                    Assert-True ($supportFiles.Count -ge 5) 'the self-contained support payload must keep its dependencies'
                    foreach ($file in $supportFiles) {
                        Assert-True (@($manifest.members | Where-Object { $_.path -eq ('runtime/support/' + $file.Name) }).Count -eq 1) ('unlisted support file: ' + $file.Name)
                    }

                    $toolkitEntries = @(Get-PackagingZipEntries -Path $toolkitZip)
                    Assert-True ($toolkitEntries -contains 'toolkit.json') 'the toolkit archive must carry its manifest'
                    Assert-True ($toolkitEntries -contains 'scripts/run-stage5cc-ui-acceptance.ps1') 'the toolkit archive must carry the entry'
                    Assert-True ($toolkitEntries -contains 'Run-Stage5CC-Acceptance.cmd') 'the launcher must be at the package root'
                    Assert-True ($toolkitEntries -contains 'scripts/Run-Stage5CC-Acceptance.ps1') 'the launcher helper must ride next to the entry'
                    Assert-True (-not ($toolkitEntries -contains 'scripts/Run-Stage5CC-Acceptance.cmd')) 'the launcher must not stay under scripts'
                    Assert-True (-not ($toolkitEntries -contains 'winapp.pdb')) 'debug symbols must not be packaged'

                    # The manifest must list both launcher files with their real bytes, and the
                    # structure smoke consumes the same rows.
                    foreach ($launcherRelative in @('Run-Stage5CC-Acceptance.cmd', 'scripts/Run-Stage5CC-Acceptance.ps1')) {
                        $launcherRow = @($manifest.members | Where-Object { $_.path -eq $launcherRelative })
                        Assert-Equal 1 $launcherRow.Count ('the launcher needs one member row: ' + $launcherRelative)
                        $launcherDisk = Join-Path $toolkitDir ($launcherRelative -replace '/', '\')
                        Assert-Equal ([long] $launcherRow[0].bytes) ([long] (Get-Item -LiteralPath $launcherDisk).Length) ('the launcher row size must match: ' + $launcherRelative)
                        Assert-Equal ([string] $launcherRow[0].sha256) ((Get-FileHash -LiteralPath $launcherDisk -Algorithm SHA256).Hash) ('the launcher row hash must match: ' + $launcherRelative)
                    }
                    Assert-Equal `
                        (Get-FileHash -LiteralPath (Join-Path $script:root 'scripts\Run-Stage5CC-Acceptance.cmd') -Algorithm SHA256).Hash `
                        (Get-FileHash -LiteralPath (Join-Path $toolkitDir 'Run-Stage5CC-Acceptance.cmd') -Algorithm SHA256).Hash `
                        'the launcher at the package root must be the real one'

                    $productFiles = @(Get-ChildItem -LiteralPath $productDir -Recurse -File)
                    Assert-True (@($productFiles | Where-Object { $_.Name -eq 'winapp.exe' }).Count -eq 0) 'the product must not carry the driver'
                    Assert-True (@($productFiles | Where-Object { $_.Name -eq 'TraceFold.Media.UiAcceptance.Support.exe' }).Count -eq 0) 'the product must not carry the support CLI'
                    Assert-True (@($productFiles | Where-Object { $_.Name -eq 'toolkit.json' }).Count -eq 0) 'the product must not carry a toolkit manifest'
                    Assert-True (-not (Test-Path -LiteralPath (Join-Path $productDir 'scripts\ui-acceptance'))) 'the product must not carry acceptance scripts'

                    $staging = @(Get-ChildItem -LiteralPath $fixture.Dist -Directory | Where-Object { $_.Name -like '.tracefold-*' })
                    Assert-Equal 0 $staging.Count 'the staging and backup directories must be gone'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the toolkit smoke accepts the produced package without starting anything'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $publish = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $publish.ExitCode ('publish failed: ' + $publish.Output)
                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $toolkitDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'

                    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
                    $previousErrorAction = $ErrorActionPreference
                    try {
                        $ErrorActionPreference = 'Continue'
                        $output = (& $hostExe -NoProfile -File (Join-Path $fixture.Repo 'scripts\smoke-stage5cc-ui-acceptance-toolkit.ps1') -ToolkitDirectory $toolkitDir -ProductDirectory $productDir 2>&1 | Out-String)
                        $code = $LASTEXITCODE
                    }
                    finally { $ErrorActionPreference = $previousErrorAction }
                    Assert-Equal 0 $code ('toolkit smoke failed: ' + $output)
                    Assert-Contains $output 'packaged entry accepted the manifest' ('toolkit smoke output: ' + $output)
                    Assert-Contains $output 'real product launch probes: NotRun' 'the structure smoke must not claim a launch probe'

                    # A freshly promoted toolkit has no results directory: the entry creates one.
                    Assert-True (-not (Test-Path -LiteralPath (Join-Path $toolkitDir 'results'))) 'the smoke must not leave a results directory'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a second publish replaces both pairs and keeps exactly one of each'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $first = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $first.ExitCode ('first publish failed: ' + $first.Output)
                    $toolkitDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
                    $fixtureFile = Join-Path $fixture.Repo 'apps\media-api\tests\fixtures\media\photo.jpg'
                    $original = [IO.File]::ReadAllBytes($fixtureFile)
                    $updated = New-Object byte[] ($original.Length + 4)
                    [Array]::Copy($original, $updated, $original.Length)
                    $updated[$original.Length] = 0x2A
                    [IO.File]::WriteAllBytes($fixtureFile, $updated)
                    # The contract declares the fixture digest, so a changed fixture is refused.
                    $refused = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-True ($refused.ExitCode -ne 0) 'a changed fixture must be refused'
                    Assert-Contains $refused.Output 'fixture size changed' ('refusal reason: ' + $refused.Output)

                    [IO.File]::WriteAllBytes($fixtureFile, $original)
                    $second = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $second.ExitCode ('second publish failed: ' + $second.Output)
                    $directories = @(Get-ChildItem -LiteralPath $fixture.Dist -Directory | Where-Object { $_.Name -like 'TraceFold-MediaDesktop-Stage5C-C-*' })
                    Assert-Equal 2 $directories.Count 'exactly the product and toolkit directories must remain'
                    Assert-Equal 2 ([int] @(Get-ChildItem -LiteralPath $fixture.Dist -File -Filter '*.zip').Count) 'exactly two archives must remain'
                    Assert-True (Test-Path -LiteralPath (Join-Path $toolkitDir 'toolkit.json')) 'the toolkit must still be usable'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'SDK failures preserve earlier artifacts and each invocation keeps separate controlled TRX evidence'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $first = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $first.ExitCode ('initial publish failed: ' + $first.Output)
                    $before = Get-PackagingDirectoryFingerprint -Path $fixture.Dist
                    $firstCalls = @(Read-PackagingDotNetCalls -Path $first.Journal)
                    $firstResults = Get-PackagingArgument -Call $firstCalls[1] -Name '--results-directory'
                    $firstTrx = Join-Path $firstResults 'fixture.trx'
                    $firstHash = (Get-FileHash -LiteralPath $firstTrx -Algorithm SHA256).Hash
                    $seenResults = @($firstResults)
                    foreach ($failure in @(
                            @{ Environment = @{ TRACEFOLD_FIXTURE_FAIL_TEST = '1' }; Calls = 2; Text = 'dotnet failed with exit code 7'; Outcome = 'Failed' },
                            @{ Environment = @{ TRACEFOLD_FIXTURE_OMIT_TRX = '1' }; Calls = 2; Text = 'produced no TRX report'; Outcome = '' },
                            @{ Environment = @{ TRACEFOLD_FIXTURE_FAIL_SUPPORT_RESTORE = '1' }; Calls = 4; Text = 'dotnet failed with exit code 6'; Outcome = 'Passed' },
                            @{ Environment = @{ TRACEFOLD_FIXTURE_FAIL_SUPPORT = '1' }; Calls = 5; Text = 'dotnet failed with exit code 5'; Outcome = 'Passed' })) {
                        $failed = Invoke-PackagingPublisher -Fixture $fixture -Environment $failure.Environment
                        Assert-True ($failed.ExitCode -ne 0) 'an SDK or evidence failure must fail the producer'
                        Assert-Contains $failed.Output $failure.Text ('the first failure must remain visible: ' + $failed.Output)
                        $calls = @(Read-PackagingDotNetCalls -Path $failed.Journal)
                        Assert-Equal $failure.Calls $calls.Count 'no SDK step may run after the failing step'
                        $results = Get-PackagingArgument -Call $calls[1] -Name '--results-directory'
                        Assert-True ($seenResults -notcontains $results) 'a retry must never reuse an earlier evidence leaf'
                        $seenResults += $results
                        Assert-Equal (Join-Path $fixture.Repo 'artifacts/reports/publish-media-desktop') ([IO.Path]::GetDirectoryName($results))
                        $trx = Join-Path $results 'fixture.trx'
                        if ($failure.Outcome -eq '') {
                            Assert-True (-not [IO.File]::Exists($trx)) 'a missing report must not be fabricated'
                        }
                        else {
                            Assert-True ([IO.File]::Exists($trx)) 'staging cleanup must preserve the test report after failure'
                            $report = [xml] [IO.File]::ReadAllText($trx)
                            Assert-Equal $failure.Outcome ([string] $report.TestRun.ResultSummary.outcome)
                            Assert-Equal 'CONTROLLED PACKAGING SDK FIXTURE' ([string] $report.TestRun.name) 'fixture reports cannot claim real .NET execution'
                        }
                        Assert-Equal $firstHash ((Get-FileHash -LiteralPath $firstTrx -Algorithm SHA256).Hash) 'earlier evidence must remain unchanged'
                        Assert-Equal $before (Get-PackagingDirectoryFingerprint -Path $fixture.Dist) 'SDK failure must preserve the previous four artifacts without leftover staging'
                    }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'every promotion position restores the previous four artifacts'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $first = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $first.ExitCode ('first publish failed: ' + $first.Output)
                    $paths = [ordered] @{
                        ProductDirectory = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                        ProductArchive   = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'
                        ToolkitDirectory = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
                        ToolkitArchive   = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip'
                    }
                    $before = [ordered] @{
                        productDir = Get-PackagingDirectoryFingerprint -Path $paths.ProductDirectory
                        productZip = (Get-FileHash -LiteralPath $paths.ProductArchive -Algorithm SHA256).Hash
                        toolkitDir = Get-PackagingDirectoryFingerprint -Path $paths.ToolkitDirectory
                        toolkitZip = (Get-FileHash -LiteralPath $paths.ToolkitArchive -Algorithm SHA256).Hash
                    }

                    foreach ($position in @('AfterProductDirectory', 'AfterProductArchive', 'AfterToolkitDirectory', 'AfterToolkitArchive')) {
                        $faulted = Invoke-PackagingPublisher -Fixture $fixture -ExtraArguments @('-FaultPoint', $position)
                        Assert-True ($faulted.ExitCode -ne 0) ('the injected fault must fail the publish: ' + $position)
                        $kind = $position.Substring('After'.Length)
                        Assert-Contains $faulted.Output ('Injected promotion fault after ' + $kind) ('fault output: ' + $faulted.Output)
                        Assert-Equal $before.productDir (Get-PackagingDirectoryFingerprint -Path $paths.ProductDirectory) ('the product directory must be restored after ' + $position)
                        Assert-Equal $before.productZip ((Get-FileHash -LiteralPath $paths.ProductArchive -Algorithm SHA256).Hash) ('the product archive must be restored after ' + $position)
                        Assert-Equal $before.toolkitDir (Get-PackagingDirectoryFingerprint -Path $paths.ToolkitDirectory) ('the toolkit directory must be restored after ' + $position)
                        Assert-Equal $before.toolkitZip ((Get-FileHash -LiteralPath $paths.ToolkitArchive -Algorithm SHA256).Hash) ('the toolkit archive must be restored after ' + $position)
                        $staging = @(Get-ChildItem -LiteralPath $fixture.Dist -Directory | Where-Object { $_.Name -like '.tracefold-*' })
                        Assert-Equal 0 $staging.Count ('no staging or backup may remain after ' + $position)
                    }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a failure while backing up restores what already moved'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $first = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $first.ExitCode ('first publish failed: ' + $first.Output)
                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $productZip = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'
                    $toolkitDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
                    $toolkitZip = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip'
                    $before = [ordered] @{
                        productDir = Get-PackagingDirectoryFingerprint -Path $productDir
                        productZip = (Get-FileHash -LiteralPath $productZip -Algorithm SHA256).Hash
                        toolkitDir = Get-PackagingDirectoryFingerprint -Path $toolkitDir
                        toolkitZip = (Get-FileHash -LiteralPath $toolkitZip -Algorithm SHA256).Hash
                    }

                    $faulted = Invoke-PackagingPublisher -Fixture $fixture -ExtraArguments @('-FaultPoint', 'AfterBackupProductDirectory')
                    Assert-True ($faulted.ExitCode -ne 0) 'the injected backup fault must fail the publish'
                    Assert-Contains $faulted.Output 'Injected promotion fault after backing up ProductDirectory' ('fault output: ' + $faulted.Output)
                    Assert-Equal $before.productDir (Get-PackagingDirectoryFingerprint -Path $productDir) 'the product directory must be back from the backup'
                    Assert-Equal $before.productZip ((Get-FileHash -LiteralPath $productZip -Algorithm SHA256).Hash) 'the product archive must be untouched'
                    Assert-Equal $before.toolkitDir (Get-PackagingDirectoryFingerprint -Path $toolkitDir) 'the toolkit directory must be untouched'
                    Assert-Equal $before.toolkitZip ((Get-FileHash -LiteralPath $toolkitZip -Algorithm SHA256).Hash) 'the toolkit archive must be untouched'
                    Assert-Equal 0 ([int] @(Get-ChildItem -LiteralPath $fixture.Dist -Directory | Where-Object { $_.Name -like '.tracefold-*' }).Count) 'the backup must be removed after a complete restore'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a rollback that cannot finish keeps the backup and reports both failures'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $first = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $first.ExitCode ('first publish failed: ' + $first.Output)
                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $productZip = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'
                    $toolkitDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
                    $toolkitZip = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip'
                    $before = [ordered] @{
                        productDir = Get-PackagingDirectoryFingerprint -Path $productDir
                        productZip = (Get-FileHash -LiteralPath $productZip -Algorithm SHA256).Hash
                        toolkitDir = Get-PackagingDirectoryFingerprint -Path $toolkitDir
                        toolkitZip = (Get-FileHash -LiteralPath $toolkitZip -Algorithm SHA256).Hash
                    }

                    $faulted = Invoke-PackagingPublisher -Fixture $fixture -ExtraArguments @(
                        '-FaultPoint', 'AfterProductDirectory', '-RecoveryFaultPoint', 'ProductArchive')
                    Assert-True ($faulted.ExitCode -ne 0) 'the failed recovery must fail the publish'
                    Assert-Contains $faulted.Output 'Promotion failed: Injected promotion fault after ProductDirectory' ('the promotion error must be reported: ' + $faulted.Output)
                    Assert-Contains $faulted.Output 'Recovery failed: ProductArchive' ('the recovery error must be reported separately: ' + $faulted.Output)
                    Assert-Contains $faulted.Output 'remain in:' ('the remaining backup must be named: ' + $faulted.Output)

                    $backups = @(Get-ChildItem -LiteralPath $fixture.Dist -Directory | Where-Object { $_.Name -like '.tracefold-*-backup-*' })
                    Assert-Equal 1 $backups.Count 'exactly one backup must be kept'
                    $backupZip = Join-Path $backups[0].FullName 'TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'
                    Assert-True (Test-Path -LiteralPath $backupZip) 'the artifact that could not be restored must still be in the backup'
                    Assert-Equal $before.productZip ((Get-FileHash -LiteralPath $backupZip -Algorithm SHA256).Hash) 'the kept copy must be the previous archive, byte for byte'
                    Assert-True (-not (Test-Path -LiteralPath $productZip)) 'the archive that could not be restored must not be claimed in place'
                    Assert-Equal $before.productDir (Get-PackagingDirectoryFingerprint -Path $productDir) 'the restored targets must be back byte for byte'
                    Assert-Equal $before.toolkitDir (Get-PackagingDirectoryFingerprint -Path $toolkitDir) 'the toolkit directory must be back byte for byte'
                    Assert-Equal $before.toolkitZip ((Get-FileHash -LiteralPath $toolkitZip -Algorithm SHA256).Hash) 'the toolkit archive must be back byte for byte'
                    Assert-Equal 0 ([int] @(Get-ChildItem -LiteralPath $fixture.Dist -Directory | Where-Object { $_.Name -like '.tracefold-*-publish-*' }).Count) 'the staging directories must still be cleaned'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the earlier stage keeps the same rollback and leaves no backup'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $first = Invoke-PackagingPublisher -Fixture $fixture -StageCheckpoint '5C'
                    Assert-Equal 0 $first.ExitCode ('legacy publish failed: ' + $first.Output)
                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-win-x64'
                    $productZip = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-win-x64.zip'
                    $beforeDir = Get-PackagingDirectoryFingerprint -Path $productDir
                    $beforeZip = (Get-FileHash -LiteralPath $productZip -Algorithm SHA256).Hash

                    $faulted = Invoke-PackagingPublisher -Fixture $fixture -StageCheckpoint '5C' -ExtraArguments @('-FaultPoint', 'AfterProductArchive')
                    Assert-True ($faulted.ExitCode -ne 0) 'the injected fault must fail the legacy publish'
                    Assert-Contains $faulted.Output 'Injected promotion fault after ProductArchive' ('fault output: ' + $faulted.Output)
                    Assert-Equal $beforeDir (Get-PackagingDirectoryFingerprint -Path $productDir) 'the legacy product directory must be restored'
                    Assert-Equal $beforeZip ((Get-FileHash -LiteralPath $productZip -Algorithm SHA256).Hash) 'the legacy product archive must be restored'
                    Assert-Equal 0 ([int] @(Get-ChildItem -LiteralPath $fixture.Dist -Directory | Where-Object { $_.Name -like '.tracefold-*' }).Count) 'no backup may remain after a complete legacy restore'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an archive whose digest does not match the pin is refused before dist exists'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $bytes = [IO.File]::ReadAllBytes($fixture.Archive)
                    # Same length, different content: the size pin still holds, so the refusal has
                    # to come from the digest comparison itself.
                    $bytes[[int] ($bytes.Length / 2)] = [byte] ($bytes[[int] ($bytes.Length / 2)] -bxor 0xFF)
                    [IO.File]::WriteAllBytes($fixture.Archive, $bytes)
                    $null = Assert-PackagingRefused -Fixture $fixture -ExpectedText 'SHA-256 mismatch'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an archive with a traversing member is refused'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    New-PackagingArchive -Path $fixture.Archive -EntryNames @('winapp.exe', '../escape.exe')
                    Set-PackagingDriverManifest -Fixture $fixture -ArchivePath $fixture.Archive
                    $null = Assert-PackagingRefused -Fixture $fixture -ExpectedText 'traversal entry'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an archive with a duplicate or unexpected executable is refused'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    New-PackagingArchive -Path $fixture.Archive -EntryNames @('winapp.exe', 'winapp.exe')
                    Set-PackagingDriverManifest -Fixture $fixture -ArchivePath $fixture.Archive
                    $null = Assert-PackagingRefused -Fixture $fixture -ExpectedText 'duplicate entry'

                    New-PackagingArchive -Path $fixture.Archive -EntryNames @('winapp.exe', 'extra.exe')
                    Set-PackagingDriverManifest -Fixture $fixture -ArchivePath $fixture.Archive
                    $null = Assert-PackagingRefused -Fixture $fixture -ExpectedText 'exactly one executable'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a checkpoint without its launcher is reported as not ready before dist exists'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base -WithoutLauncher
                    $result = Assert-PackagingRefused -Fixture $fixture -ExpectedText 'stage5c_c_not_ready'
                    Assert-Contains $result.Output 'launcher' ('the missing piece must be named: ' + $result.Output)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a checkpoint without its launcher helper is reported as not ready before dist exists'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    Remove-PackagingFixtureFile -Fixture $fixture -Relative 'scripts/Run-Stage5CC-Acceptance.ps1'
                    $result = Assert-PackagingRefused -Fixture $fixture -ExpectedText 'stage5c_c_not_ready'
                    Assert-Contains $result.Output 'launcher-helper' ('the missing helper must be named: ' + $result.Output)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a product that carries acceptance payload is refused'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $result = Invoke-PackagingPublisher -Fixture $fixture -Environment @{ TRACEFOLD_FIXTURE_CONTAMINATE = '1' }
                    Assert-True ($result.ExitCode -ne 0) ('a contaminated product must be refused: ' + $result.Output)
                    Assert-Contains $result.Output 'acceptance' ('refusal reason: ' + $result.Output)
                    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'))) 'nothing may be promoted'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a flat support dependency in the product is refused'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $result = Invoke-PackagingPublisher -Fixture $fixture -Environment @{ TRACEFOLD_FIXTURE_CONTAMINATE = 'flat' }
                    Assert-True ($result.ExitCode -ne 0) ('a flat contaminated product must be refused: ' + $result.Output)
                    Assert-Contains $result.Output 'TraceFold.Media.UiAcceptance.Support.dll' ('the flat file must be named: ' + $result.Output)
                    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'))) 'nothing may be promoted'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'workspace private directories are refused before promotion and by the independent smoke'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    foreach ($variant in @(
                            @{ Directory = 'artifacts'; Hidden = '0' }, @{ Directory = 'artifacts'; Hidden = '1' },
                            @{ Directory = '.cache'; Hidden = '0' }, @{ Directory = '.cache'; Hidden = '1' },
                            @{ Directory = '.worktrees'; Hidden = '0' }, @{ Directory = '.worktrees'; Hidden = '1' })) {
                        $directory = [string] $variant.Directory
                        $refused = Invoke-PackagingPublisher -Fixture $fixture -Environment @{
                            TRACEFOLD_FIXTURE_CONTAMINATE = $directory
                            TRACEFOLD_FIXTURE_HIDE_PRIVATE = [string] $variant.Hidden
                        }
                        Assert-True ($refused.ExitCode -ne 0) ('private directory must fail publication: ' + $refused.Output)
                        Assert-Contains $refused.Output 'runtime/source directories'
                        Assert-Contains $refused.Output $directory
                        Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'))) 'nothing may be promoted'
                        Write-Host ('CONTROLLED producer privacy refused: directory=' + $directory + ' hidden=' + $variant.Hidden)
                    }
                    $published = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $published.ExitCode ('clean controlled publication failed: ' + $published.Output)
                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $toolkitDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
                    $smokeScript = Join-Path $fixture.Repo 'scripts/smoke-stage5cc-ui-acceptance-toolkit.ps1'
                    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
                    foreach ($variant in @(
                            @{ Directory = 'artifacts'; Hidden = '0' }, @{ Directory = 'artifacts'; Hidden = '1' },
                            @{ Directory = '.cache'; Hidden = '0' }, @{ Directory = '.cache'; Hidden = '1' },
                            @{ Directory = '.worktrees'; Hidden = '0' }, @{ Directory = '.worktrees'; Hidden = '1' })) {
                        $directory = [string] $variant.Directory
                        $privateRoot = Join-Path $productDir $directory
                        Write-PackagingText -Path (Join-Path $privateRoot 'private.txt') -Text 'CONTROLLED private workspace payload'
                        if ($variant.Hidden -eq '1') {
                            [IO.File]::SetAttributes($privateRoot, ([IO.File]::GetAttributes($privateRoot) -bor [IO.FileAttributes]::Hidden))
                            Assert-True ([bool] ([IO.File]::GetAttributes($privateRoot) -band [IO.FileAttributes]::Hidden)) 'Hidden fixture directory was actually created'
                        }
                        $previousErrorAction = $ErrorActionPreference
                        try {
                            $ErrorActionPreference = 'Continue'
                            $smokeOutput = (& $hostExe -NoProfile -File $smokeScript -ToolkitDirectory $toolkitDir -ProductDirectory $productDir 2>&1 | Out-String)
                            $smokeCode = $LASTEXITCODE
                        }
                        finally { $ErrorActionPreference = $previousErrorAction }
                        Assert-True ($smokeCode -ne 0) ('independent smoke must refuse ' + $directory)
                        Assert-Contains $smokeOutput 'the product carries an acceptance directory'
                        Write-Host ('CONTROLLED independent smoke privacy refused: directory=' + $directory + ' hidden=' + $variant.Hidden)
                        # Only this case's exact, newly created contamination is removed.
                        Remove-Item -LiteralPath $privateRoot -Recurse -Force
                    }
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the producer refuses to promote when the product gate cannot run'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    Remove-PackagingFixtureFile -Fixture $fixture -Relative 'scripts/smoke-media-desktop-package.ps1'
                    $result = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-True ($result.ExitCode -ne 0) ('a missing gate must fail the publish: ' + $result.Output)
                    Assert-Contains $result.Output 'product package smoke failed' ('the gate failure must be named: ' + $result.Output)
                    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'))) 'nothing may be promoted without the gate'
                    Assert-Equal 0 ([int] @(Get-ChildItem -LiteralPath $fixture.Dist -Directory | Where-Object { $_.Name -like '.tracefold-*' }).Count) 'the staging must still be cleaned'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the publish gate itself runs the four probe requests before promotion'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $record = Join-Path $base 'gate-requests.txt'
                    $launcher = Join-Path $base 'gate-launcher.cmd'
                    Write-PackagingText -Path $launcher -Text ("@echo off`r`necho %*>>`"$record`"`r`nexit /b 0`r`n")

                    $result = Invoke-PackagingPublisher -Fixture $fixture -ProbeLauncher $launcher
                    Assert-Equal 0 $result.ExitCode ('the controlled publish must pass: ' + $result.Output)
                    Assert-Contains $result.Output 'Product package gate: complete smoke passed' ('the complete smoke must be the gate: ' + $result.Output)
                    Assert-True ($result.Output.IndexOf('Product package gate: complete smoke passed', [StringComparison]::Ordinal) -lt
                        $result.Output.IndexOf('Published:', [StringComparison]::Ordinal)) 'the gate must run before anything is promoted'

                    $lines = @(Get-Content -LiteralPath $record)
                    Assert-Equal 4 $lines.Count ('the gate must issue the four requests itself: ' + ($lines -join ' | '))
                    foreach ($scenario in @('stage5a-batch', 'stage5b-library', 'stage5c-taxonomy', 'stage5cb-diagnostics')) {
                        Assert-Equal 1 ([int] @($lines | Where-Object { $_ -like ('*--package-probe-scenario ' + $scenario + '*') }).Count) ('the gate request for ' + $scenario + ' is missing: ' + ($lines -join ' | '))
                    }
                    foreach ($line in $lines) {
                        Assert-Contains $line '--package-probe-root' ('a gate request without a root: ' + $line)
                        Assert-Contains $line '--package-probe-token' ('a gate request without a token: ' + $line)
                    }

                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $toolkitDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
                    foreach ($target in @(
                            $productDir,
                            (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'),
                            $toolkitDir,
                            (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip'))) {
                        Assert-True (Test-Path -LiteralPath $target) ('the gate passed, so the artifact must exist: ' + $target)
                    }
                    $version = Read-PackagingJson -Path (Join-Path $productDir 'package-version.json')
                    Assert-Equal 'NotRun' ([string] $version.ProductLaunchProbe) 'a controlled launch must be recorded as NotRun'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a failing business gate leaves the previous four artifacts untouched'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $first = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $first.ExitCode ('first publish failed: ' + $first.Output)
                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $productZip = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'
                    $toolkitDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
                    $toolkitZip = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64.zip'
                    $before = [ordered] @{
                        productDir = Get-PackagingDirectoryFingerprint -Path $productDir
                        productZip = (Get-FileHash -LiteralPath $productZip -Algorithm SHA256).Hash
                        toolkitDir = Get-PackagingDirectoryFingerprint -Path $toolkitDir
                        toolkitZip = (Get-FileHash -LiteralPath $toolkitZip -Algorithm SHA256).Hash
                    }

                    $failing = Join-Path $base 'gate-failing.cmd'
                    Write-PackagingText -Path $failing -Text "@echo off`r`nexit /b 3`r`n"
                    $result = Invoke-PackagingPublisher -Fixture $fixture -ProbeLauncher $failing
                    Assert-True ($result.ExitCode -ne 0) ('a failing gate must fail the publish: ' + $result.Output)
                    Assert-Contains $result.Output 'product package smoke failed' ('the gate failure must be named: ' + $result.Output)
                    # The probe's real code is visible in the captured text, but the host wraps a
                    # native stderr line; the stable facts are the gate verdict and the probe name.
                    Assert-Contains $result.Output 'Stage 5A package probe failed' ('the failing probe must be named: ' + $result.Output)

                    Assert-Equal $before.productDir (Get-PackagingDirectoryFingerprint -Path $productDir) 'the product directory must be untouched'
                    Assert-Equal $before.productZip ((Get-FileHash -LiteralPath $productZip -Algorithm SHA256).Hash) 'the product archive must be untouched'
                    Assert-Equal $before.toolkitDir (Get-PackagingDirectoryFingerprint -Path $toolkitDir) 'the toolkit directory must be untouched'
                    Assert-Equal $before.toolkitZip ((Get-FileHash -LiteralPath $toolkitZip -Algorithm SHA256).Hash) 'the toolkit archive must be untouched'
                    Assert-Equal 0 ([int] @(Get-ChildItem -LiteralPath $fixture.Dist -Directory | Where-Object { $_.Name -like '.tracefold-*' }).Count) 'no staging or backup may be left behind'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a registry scenario missing from the source is refused before dist exists'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    Remove-PackagingFixtureFile -Fixture $fixture -Relative 'scripts/ui-acceptance/scenarios/10-main-chain.ps1'
                    $result = Assert-PackagingRefused -Fixture $fixture -ExpectedText 'stage5c_c_not_ready'
                    Assert-Contains $result.Output 'scenario:MainChain' ('the missing registry entry must be named: ' + $result.Output)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the toolkit smoke refuses a missing or tampered media declaration'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $publish = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $publish.ExitCode ('publish failed: ' + $publish.Output)
                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $toolkitDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64'
                    $mediaPath = Join-Path $toolkitDir 'apps\media-desktop\tools\ffmpeg\win-x64\manifest.json'
                    $smokeScript = Join-Path $fixture.Repo 'scripts\smoke-stage5cc-ui-acceptance-toolkit.ps1'
                    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }

                    $original = Read-PackagingJson -Path $mediaPath
                    $originalSha = [string] $original.files.'ffmpeg.exe'.sha256
                    $flipped = if ($originalSha.StartsWith('A')) { 'B' } else { 'A' }
                    $original.files.'ffmpeg.exe'.sha256 = $flipped + $originalSha.Substring(1)
                    Write-PackagingText -Path $mediaPath -Text ($original | ConvertTo-Json -Depth 5)
                    $previousErrorAction = $ErrorActionPreference
                    try {
                        $ErrorActionPreference = 'Continue'
                        $tamperedOutput = (& $hostExe -NoProfile -File $smokeScript -ToolkitDirectory $toolkitDir -ProductDirectory $productDir 2>&1 | Out-String)
                        $tamperedCode = $LASTEXITCODE
                    }
                    finally { $ErrorActionPreference = $previousErrorAction }
                    Assert-True ($tamperedCode -ne 0) 'a tampered declaration must be refused'
                    Assert-Contains $tamperedOutput 'member hash mismatch' ('the member hash must be the reason: ' + $tamperedOutput)

                    $republish = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $republish.ExitCode ('the republish failed: ' + $republish.Output)
                    Remove-Item -LiteralPath $mediaPath -Force
                    $previousErrorAction = $ErrorActionPreference
                    try {
                        $ErrorActionPreference = 'Continue'
                        $missingOutput = (& $hostExe -NoProfile -File $smokeScript -ToolkitDirectory $toolkitDir -ProductDirectory $productDir 2>&1 | Out-String)
                        $missingCode = $LASTEXITCODE
                    }
                    finally { $ErrorActionPreference = $previousErrorAction }
                    Assert-True ($missingCode -ne 0) 'a missing declaration must be refused'
                    Assert-Contains $missingOutput 'member file is missing' ('the missing member must be the reason: ' + $missingOutput)

                    # The structure smoke's own boundary gate: a flat support dependency at the
                    # product root must be caught there too, not only by the producer. The
                    # package is republished first so the member table is whole again.
                    $restored = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $restored.ExitCode ('the restore publish failed: ' + $restored.Output)
                    $flatPayload = Join-Path $productDir 'TraceFold.Media.UiAcceptance.Support.dll'
                    Write-PackagingText -Path $flatPayload -Text 'flat acceptance payload'
                    $previousErrorAction = $ErrorActionPreference
                    try {
                        $ErrorActionPreference = 'Continue'
                        $flatOutput = (& $hostExe -NoProfile -File $smokeScript -ToolkitDirectory $toolkitDir -ProductDirectory $productDir 2>&1 | Out-String)
                        $flatCode = $LASTEXITCODE
                    }
                    finally { $ErrorActionPreference = $previousErrorAction }
                    Assert-True ($flatCode -ne 0) 'the structure smoke must refuse a flat support dependency'
                    # The host wraps a native stderr line at 80 columns, which can split the file
                    # name itself; the gate's own verdict is the stable part.
                    Assert-Contains $flatOutput 'the product carries acceptance payload' ('the flat payload must be named: ' + $flatOutput)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the earlier stages still publish only the product pair'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $result = Invoke-PackagingPublisher -Fixture $fixture -StageCheckpoint '5C'
                    Assert-Equal 0 $result.ExitCode ('legacy publish failed: ' + $result.Output)
                    Assert-True (Test-Path -LiteralPath (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-win-x64')) 'the product directory is missing'
                    Assert-True (Test-Path -LiteralPath (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-win-x64.zip')) 'the product archive is missing'
                    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-UiAcceptance-win-x64'))) 'an earlier stage must not produce a toolkit'
                    $version = Read-PackagingJson -Path (Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-win-x64\package-version.json')
                    Assert-True ($null -eq $version.PSObject.Properties['ProductLaunchProbe']) 'the launch probe field belongs to Stage 5C-C only'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the product smoke stand-in checks the four probe requests and their failures'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $publish = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $publish.ExitCode ('publish failed: ' + $publish.Output)
                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $record = Join-Path $base 'probe-requests.txt'
                    $standIn = Join-Path $base 'stand-in.cmd'
                    Write-PackagingText -Path $standIn -Text ("@echo off`r`necho %*>>`"$record`"`r`nexit /b 0`r`n")

                    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
                    $smokeScript = Join-Path $fixture.Repo 'scripts\smoke-media-desktop-package.ps1'
                    $previousErrorAction = $ErrorActionPreference
                    try {
                        $ErrorActionPreference = 'Continue'
                        $output = (& $hostExe -NoProfile -File $smokeScript -StageCheckpoint '5C-C' -PackageDirectory $productDir -ProbeExecutable $standIn 2>&1 | Out-String)
                        $code = $LASTEXITCODE
                    }
                    finally { $ErrorActionPreference = $previousErrorAction }
                    Assert-Equal 0 $code ('the stand-in run should pass: ' + $output)
                    Assert-Contains $output 'realProbe=NotRun' 'the stand-in run must not claim a real probe'
                    $lines = @(Get-Content -LiteralPath $record)
                    Assert-Equal 4 $lines.Count 'the four probes must each build one request'
                    foreach ($scenario in @('stage5a-batch', 'stage5b-library', 'stage5c-taxonomy', 'stage5cb-diagnostics')) {
                        Assert-True (@($lines | Where-Object { $_ -like ('*--package-probe-scenario ' + $scenario + '*') }).Count -eq 1) ('the request for ' + $scenario + ' is missing: ' + ($lines -join ' | '))
                    }
                    foreach ($line in $lines) {
                        Assert-Contains $line '--package-probe-root' ('a request without a root: ' + $line)
                        Assert-Contains $line '--package-probe-token' ('a request without a token: ' + $line)
                    }
                    # The product refuses a root that is not named for its own scenario, so each
                    # request has to carry the contract's prefix, a temp-directory parent and one
                    # token - the same rules the real launch consumes.
                    $contractPrefixes = @{
                        'stage5a-batch'        = 'tracefold-media-desktop-stage5a-probe-'
                        'stage5b-library'      = 'tracefold-media-desktop-stage5b-probe-'
                        'stage5c-taxonomy'     = 'tracefold-media-desktop-stage5c-probe-'
                        'stage5cb-diagnostics' = 'tracefold-media-desktop-stage5cb-probe-'
                    }
                    $tempRootFull = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
                    foreach ($line in $lines) {
                        $rootMatch = [regex]::Match($line, '--package-probe-root\s+"([^"]+)"')
                        $tokenMatch = [regex]::Match($line, '--package-probe-token\s+([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})')
                        $scenarioMatch = [regex]::Match($line, '--package-probe-scenario\s+(\S+)')
                        Assert-True $rootMatch.Success ('the request root is not quoted: ' + $line)
                        Assert-True $tokenMatch.Success ('the request token is not a GUID: ' + $line)
                        Assert-True $scenarioMatch.Success ('the request names no scenario: ' + $line)
                        $rootPath = [IO.Path]::GetFullPath($rootMatch.Groups[1].Value)
                        $rootName = [IO.Path]::GetFileName($rootPath)
                        $parent = [IO.Path]::GetFullPath([IO.Path]::GetDirectoryName($rootPath)).TrimEnd([IO.Path]::DirectorySeparatorChar)
                        $scenario = $scenarioMatch.Groups[1].Value
                        Assert-True ($contractPrefixes.ContainsKey($scenario)) ('an unknown probe scenario: ' + $scenario)
                        Assert-True ($rootName.StartsWith($contractPrefixes[$scenario], [StringComparison]::OrdinalIgnoreCase)) ('the root does not carry the contract prefix for ' + $scenario + ': ' + $rootName)
                        Assert-Equal $tempRootFull $parent ('the probe root must be a direct child of the temp directory: ' + $rootPath)
                    }

                    $failing = Join-Path $base 'stand-in-failing.cmd'
                    Write-PackagingText -Path $failing -Text "@echo off`r`nexit /b 3`r`n"
                    $previousErrorAction = $ErrorActionPreference
                    try {
                        $ErrorActionPreference = 'Continue'
                        $failedOutput = (& $hostExe -NoProfile -File $smokeScript -StageCheckpoint '5C-C' -PackageDirectory $productDir -ProbeExecutable $failing 2>&1 | Out-String)
                        $failedCode = $LASTEXITCODE
                    }
                    finally { $ErrorActionPreference = $previousErrorAction }
                    Assert-True ($failedCode -ne 0) 'a failing probe must fail the smoke'
                    Assert-Contains $failedOutput 'failed with exit code 3' ('the real code must be reported: ' + $failedOutput)

                    $prefixes = @(
                        'tracefold-media-desktop-stage5a-probe-',
                        'tracefold-media-desktop-stage5b-probe-',
                        'tracefold-media-desktop-stage5c-probe-',
                        'tracefold-media-desktop-stage5cb-probe-')
                    $leftover = @([IO.Directory]::GetDirectories([IO.Path]::GetTempPath()) |
                        Where-Object { $name = [IO.Path]::GetFileName($_); @($prefixes | Where-Object { $name.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 })
                    Assert-Equal 0 $leftover.Count ('a failed probe must leave no root: ' + ($leftover -join ', '))
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a timed-out probe is stopped before its root is removed'
            Body = {
                $base = New-PackagingAttempt
                try {
                    $fixture = New-PackagingFixture -Attempt $base
                    $publish = Invoke-PackagingPublisher -Fixture $fixture
                    Assert-Equal 0 $publish.ExitCode ('publish failed: ' + $publish.Output)
                    $productDir = Join-Path $fixture.Dist 'TraceFold-MediaDesktop-Stage5C-C-win-x64'
                    $smokeScript = Join-Path $fixture.Repo 'scripts\smoke-media-desktop-package.ps1'
                    $sleeperName = 'probe-sleeper-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.cmd'
                    $sleeper = Join-Path $base $sleeperName
                    # A single cmd process that ignores its arguments and stays busy: no child
                    # process is left behind when the smoke stops it.
                    Write-PackagingText -Path $sleeper -Text "@echo off`r`nfor /l %%i in (1,1,400000000) do @rem`r`n"

                    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
                    $previousErrorAction = $ErrorActionPreference
                    try {
                        $ErrorActionPreference = 'Continue'
                        $output = (& $hostExe -NoProfile -File $smokeScript -StageCheckpoint '5C-C' -PackageDirectory $productDir -ProbeExecutable $sleeper -ProbeTimeoutSeconds 5 2>&1 | Out-String)
                        $code = $LASTEXITCODE
                    }
                    finally { $ErrorActionPreference = $previousErrorAction }
                    Assert-True ($code -ne 0) 'the timed-out probe must fail the smoke'
                    Assert-Contains $output 'did not exit within 5 seconds' ('the timeout must be reported: ' + $output)

                    $prefixes = @(
                        'tracefold-media-desktop-stage5a-probe-',
                        'tracefold-media-desktop-stage5b-probe-',
                        'tracefold-media-desktop-stage5c-probe-',
                        'tracefold-media-desktop-stage5cb-probe-')
                    $leftover = @([IO.Directory]::GetDirectories([IO.Path]::GetTempPath()) |
                        Where-Object { $name = [IO.Path]::GetFileName($_); @($prefixes | Where-Object { $name.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 })
                    Assert-Equal 0 $leftover.Count ('a timed-out probe must leave no root: ' + ($leftover -join ', '))
                    $owned = @(Get-CimInstance Win32_Process -Filter "Name = 'cmd.exe'" -ErrorAction SilentlyContinue |
                        Where-Object { $_.CommandLine -like ('*' + $sleeperName + '*') })
                    Assert-Equal 0 $owned.Count 'the timed-out probe process must not survive the smoke'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        }
    )
}
