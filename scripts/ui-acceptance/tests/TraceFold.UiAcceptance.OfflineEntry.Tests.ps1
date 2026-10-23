# TraceFold Stage 5CC - offline entry behaviour cases (moved toolkit).
#
# What this suite is:
#   * the real public entry (scripts/run-stage5cc-ui-acceptance.ps1) is always started as a child
#     process through -File, from a working directory outside both the source tree and the package;
#   * the package is a real moved toolkit: its own support payload, unpacked driver, modules,
#     scenarios, template, contract and declared fixture, described by toolkit.json;
#   * every rejection case asserts that nothing was started, not merely that a message appeared;
#   * the archive (source tree) shape is exercised once so the new mode is proven compatible.
#
# What it is not: the real product, the real pinned driver, a release archive, or a claim that the
# whole Task 14 packaging pipeline works. The support executable is a test double published by this
# suite before the package is built; the package itself never compiles anything.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- helpers ----------------------------------------------------------------------------------

function New-OfflineBase {
    # The prefix stays short on purpose: the archive shape publishes the support CLI under
    # apps/media-desktop/tools/<project>/bin/stage5cc-bootstrap, and a deep temp root would push
    # that executable past the Windows path limit, where it cannot be started at all.
    $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-o-' + [guid]::NewGuid().ToString('N').Substring(0, 12))
    if ([IO.Directory]::Exists($base)) { throw 'The offline test root already exists.' }
    [void][IO.Directory]::CreateDirectory($base)
    return $base
}

function New-OfflineProduct {
    param([Parameter(Mandatory)][string] $Path)
    [void][IO.Directory]::CreateDirectory($Path)
    [IO.File]::WriteAllBytes((Join-Path $Path 'TraceFold.Media.exe'), [byte[]] @(9, 8, 7, 6))
    return $Path
}

<#
.SYNOPSIS
    Writes the support test double's project once and publishes it.

.DESCRIPTION
    The package mode is not allowed to compile anything, so the executable it carries is built
    here, before the package exists. It answers the two commands the Runner needs and journals
    every create and delete, so the case can prove the owned root was handed back instead of
    assuming it.
#>
function New-OfflineSupportPayload {
    $cache = Join-Path ([IO.Path]::GetTempPath()) 'tracefold-offline-support-payload'
    $executable = Join-Path $cache 'TraceFold.Media.UiAcceptance.Support.exe'
    $projectRoot = Join-Path ([IO.Path]::GetTempPath()) 'tracefold-offline-support-project'
    $projectPath = Write-OfflineSupportProject -ProjectRoot $projectRoot
    # The cache is keyed by the source it was built from: a stub that gained a behaviour must not be
    # served from an older build, or a case would silently exercise the previous double.
    $stamp = (Get-FileHash -LiteralPath (Join-Path $projectRoot 'Program.cs') -Algorithm SHA256).Hash
    $stampPath = Join-Path $cache 'build.stamp'
    if ([IO.File]::Exists($executable) -and [IO.File]::Exists($stampPath) -and ([IO.File]::ReadAllText($stampPath)).Trim() -ceq $stamp) {
        return $cache
    }

    [void][IO.Directory]::CreateDirectory($cache)
    # The build runs in its own child process: this suite starts entries as child processes too,
    # and a dotnet run inside the suite process changes console state the children then inherit.
    # The package entry itself never compiles anything - that is proven by its own sentinel case.
    $buildScript = Join-Path ([IO.Path]::GetTempPath()) 'tracefold-offline-build-support.ps1'
    $buildText = @'
param([Parameter(Mandatory)][string] $Project, [Parameter(Mandatory)][string] $Out)
& dotnet publish $Project -c Release -o $Out --nologo | Out-Null
exit $LASTEXITCODE
'@
    [IO.File]::WriteAllText($buildScript, $buildText, [Text.UTF8Encoding]::new($false))
    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
    $null = & $hostExe -NoProfile -File $buildScript -Project $projectPath -Out $cache 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw ('the support test double could not be published, exit ' + $code) }
    if (-not [IO.File]::Exists($executable)) { throw 'the support test double was not produced' }
    [IO.File]::WriteAllText($stampPath, $stamp, [Text.UTF8Encoding]::new($false))
    return $cache
}

<#
.SYNOPSIS
    Writes the support test double's C# project into the given directory.
#>
function Write-OfflineSupportProject {
    param([Parameter(Mandatory)][string] $ProjectRoot)

    [void][IO.Directory]::CreateDirectory($ProjectRoot)

    $source = @'
using System;
using System.IO;
using System.Text;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "root", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("unsupported support command");
            return 3;
        }

        string parent = ReadOption(args, "--parent");
        string runId = ReadOption(args, "--run-id");
        string token = ReadOption(args, "--token");
        string root = ReadOption(args, "--root");

        if (string.Equals(args[1], "create", StringComparison.Ordinal))
        {
            if (parent.Length == 0 || runId.Length == 0 || token.Length == 0)
            {
                Console.Error.WriteLine("root create needs a parent, a run id and a token");
                return 3;
            }
            string created = Path.Combine(parent, "TraceFold-UIA-" + runId.Replace("-", string.Empty));
            Directory.CreateDirectory(created);
            File.WriteAllText(Path.Combine(created, ".owned-token"), token, new UTF8Encoding(false));
            if (string.Equals(Environment.GetEnvironmentVariable("TRACEFOLD_STUB_SUPPORT_POISON"), "1", StringComparison.Ordinal))
            {
                // Occupies the name the entry copies the support payload into, so preparation fails
                // after the owned root exists - the window this case needs to observe cleanup.
                File.WriteAllText(Path.Combine(created, "support"), "poison", new UTF8Encoding(false));
            }
            Journal("create", created);
            Console.Out.WriteLine("{\"root\":" + Quote(created) + "}");
            return 0;
        }

        if (string.Equals(args[1], "delete", StringComparison.Ordinal))
        {
            if (root.Length == 0 || token.Length == 0)
            {
                Console.Error.WriteLine("root delete needs a root and a token");
                return 3;
            }
            string marker = Path.Combine(root, ".owned-token");
            string stored = File.Exists(marker) ? File.ReadAllText(marker) : string.Empty;
            if (!string.Equals(stored, token, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("root ownership token mismatch");
                return 3;
            }
            if (Directory.Exists(root)) { Directory.Delete(root, true); }
            Journal("delete", root);
            return 0;
        }

        Console.Error.WriteLine("unsupported support command");
        return 3;
    }

    private static string ReadOption(string[] args, string name)
    {
        for (int index = 2; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal)) { return args[index + 1]; }
        }
        return string.Empty;
    }

    private static void Journal(string action, string path)
    {
        string journal = Environment.GetEnvironmentVariable("TRACEFOLD_STUB_SUPPORT_JOURNAL");
        if (string.IsNullOrEmpty(journal)) { return; }
        File.AppendAllText(journal, action + "|" + path + Environment.NewLine, new UTF8Encoding(false));
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
'@
    [IO.File]::WriteAllText((Join-Path $projectRoot 'Program.cs'), $source, [Text.UTF8Encoding]::new($false))

    $project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>TraceFold.Media.UiAcceptance.Support</AssemblyName>
    <RootNamespace>TraceFold.UIAcceptance.OfflineStubSupport</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
  </ItemGroup>
</Project>
'@
    $projectPath = Join-Path $projectRoot 'TraceFold.Media.UiAcceptance.Support.csproj'
    [IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))
    return $projectPath
}

<#
.SYNOPSIS
    Writes a scenario entry that records what the entry actually handed it.
#>
function New-OfflineScenarioStub {
    param(
        [Parameter(Mandatory)][string] $Path,
        [ValidateSet('Passed', 'Failed')][string] $Outcome = 'Passed'
    )

    $template = @'
param(
    [string] $ScenarioId,
    [string] $ExecutablePath,
    [string] $ContractPath,
    [string] $OwnedRoot,
    [string] $RunToken,
    [string] $OutputDirectory,
    [string] $SupportPath,
    [string] $WinAppPath,
    [string] $WinAppCliArchivePath
)

$journal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
if (-not [string]::IsNullOrWhiteSpace($journal)) {
    $driverDirectory = Split-Path -Parent $WinAppPath
    $driverFiles = @()
    if ([IO.Directory]::Exists($driverDirectory)) {
        $driverFiles = @([IO.Directory]::GetFiles($driverDirectory) | ForEach-Object { [IO.Path]::GetFileName($_) })
    }
    $record = [ordered]@{
        scenarioId           = $ScenarioId
        executablePath       = $ExecutablePath
        contractPath         = $ContractPath
        ownedRoot            = $OwnedRoot
        runTokenPresent      = (-not [string]::IsNullOrWhiteSpace($RunToken))
        outputDirectory      = $OutputDirectory
        supportPath          = $SupportPath
        winAppPath           = $WinAppPath
        winAppCliArchivePath = $WinAppCliArchivePath
        supportExists        = [bool] [IO.File]::Exists($SupportPath)
        driverExists         = [bool] [IO.File]::Exists($WinAppPath)
        rootExists           = [bool] [IO.Directory]::Exists($OwnedRoot)
        driverFiles          = $driverFiles
        driverSha256         = $(if ([IO.File]::Exists($WinAppPath)) { (Get-FileHash -LiteralPath $WinAppPath -Algorithm SHA256).Hash } else { '' })
    }
    [IO.File]::AppendAllText($journal, ($record | ConvertTo-Json -Compress) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

return [pscustomobject]@{ Status = '__OUTCOME__'; Reason = '__REASON__' }
'@

    $reason = if ($Outcome -ceq 'Failed') { 'scenario_refused_on_purpose' } else { '' }
    $text = $template.Replace('__OUTCOME__', $Outcome).Replace('__REASON__', $reason)
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
    return $Path
}

function Get-OfflineMemberSource {
    param([Parameter(Mandatory)][string] $Relative)

    if ($Relative -ceq 'scripts/run-stage5cc-ui-acceptance.ps1') { return 'entry' }
    if ($Relative -like 'scripts/ui-acceptance/scenarios/*') { return 'scenario' }
    if ($Relative -like 'scripts/ui-acceptance/report-template/*') { return 'template' }
    if ($Relative -like 'scripts/ui-acceptance/*.psm1') { return 'module' }
    if ($Relative -like 'runtime/support/*') { return 'support' }
    if ($Relative -like 'runtime/driver/*') { return 'driver' }
    if ($Relative -like 'apps/media-desktop/tools/ui-acceptance-contract/*') { return 'contract' }
    if ($Relative -like 'apps/media-api/tests/fixtures/*') { return 'fixture' }
    return 'other'
}

<#
.SYNOPSIS
    Writes the aggregate engine double the Conflict and Library scenarios import.

.DESCRIPTION
    The real scenario files are used unchanged; only the engine they import is a double. It records
    which file was imported, so the case can prove the sibling copy is preferred over the parent
    copy exactly as the scenario's own resolution does, and answers one outcome.
#>
function Write-OfflineEngineStub {
    param([Parameter(Mandatory)][string] $Path)

    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    $module = @'
$script:enginePath = $PSCommandPath

function Invoke-UiAggregateComposition {
    [CmdletBinding()]
    param(
        $AggregateId,
        $ChildScenarios,
        $ExecutablePath,
        $OutputDirectory,
        $OwnedRoot,
        $RunToken,
        $WinAppCliArchivePath,
        $EntryScriptPath,
        $EnginePath,
        $TimeoutSeconds,
        $InvokeChild,
        $ExpectedSourceCommit,
        $ResolveSourceCommit,
        $ExpectedDriverVersion
    )

    $journal = $env:TRACEFOLD_STUB_ENGINE_JOURNAL
    if (-not [string]::IsNullOrWhiteSpace($journal)) {
        [IO.File]::AppendAllText($journal, ($script:enginePath + '|' + [string] $AggregateId + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    }
    return [pscustomobject]@{ Status = 'Passed'; Reason = '' }
}

Export-ModuleMember -Function Invoke-UiAggregateComposition
'@
    [IO.File]::WriteAllText($Path, $module, [Text.UTF8Encoding]::new($false))
}

<#
.SYNOPSIS
    Writes a module directory that shadows Expand-Archive with a recording failure.

.DESCRIPTION
    The entry's unpack step is a cmdlet, not an executable, so a PATH shim cannot see it. A module
    with the archive module's own name, placed first on PSModulePath, is the interception point the
    command is actually resolved through: the real name is found, the call is recorded, and it
    fails. The same interception is used for the positive control.
#>
function New-OfflineUnpackSentinelModule {
    param([Parameter(Mandatory)][string] $Root)

    $moduleRoot = Join-Path $Root 'Microsoft.PowerShell.Archive'
    [void][IO.Directory]::CreateDirectory($moduleRoot)
    $module = @'
$script:sentinelRecord = $env:TRACEFOLD_UNPACK_SENTINEL_RECORD

function Expand-Archive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $LiteralPath,
        [Parameter(Mandatory)][string] $DestinationPath,
        [switch] $Force
    )

    if (-not [string]::IsNullOrWhiteSpace($script:sentinelRecord)) {
        [IO.File]::AppendAllText($script:sentinelRecord, ($LiteralPath + '|' + $DestinationPath + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    }
    throw 'unpack_sentinel: Expand-Archive was invoked'
}

Export-ModuleMember -Function Expand-Archive
'@
    [IO.File]::WriteAllText((Join-Path $moduleRoot 'Microsoft.PowerShell.Archive.psm1'), $module, [Text.UTF8Encoding]::new($false))
    $manifest = @'
@{
    RootModule = 'Microsoft.PowerShell.Archive.psm1'
    ModuleVersion = '1.0.0'
    GUID = 'f0c1a2b3-c4d5-e6f7-8901-23456789abcd'
    FunctionsToExport = @('Expand-Archive')
    CmdletsToExport = @()
    AliasesToExport = @()
}
'@
    [IO.File]::WriteAllText((Join-Path $moduleRoot 'Microsoft.PowerShell.Archive.psd1'), $manifest, [Text.UTF8Encoding]::new($false))
    return $Root
}

<#
.SYNOPSIS
    Builds one moved toolkit: the real entry and modules, a published support payload, an already
    unpacked driver, the contract and its declared fixture, and the manifest that binds them.
#>
function New-OfflinePackage {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $SupportPayload,
        [string] $Scenario = 'StartupLayout',
        [ValidateSet('Passed', 'Failed')][string] $Outcome = 'Passed',
        [string[]] $WithoutScenario = @(),
        # Real scenario files run unchanged against a controlled engine double; the rest stay
        # markers or the recording stub.
        [string[]] $RealScenario = @(),
        # The Visual integration reuses this same package builder against a test-owned source
        # tree and the real contract/assets. Defaults retain the original synthetic package.
        [string] $SourceRoot = $script:root,
        [AllowEmptyString()][string] $ContractSource = '',
        [AllowEmptyString()][string] $FixtureSourceRoot = ''
    )

    $sourceRootExact = if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $script:root } else { $SourceRoot }
    $sourceUiRoot = Join-Path $sourceRootExact 'scripts/ui-acceptance'
    $scriptsRoot = Join-Path $Root 'scripts'
    $uiRoot = Join-Path $scriptsRoot 'ui-acceptance'
    $scenarioRoot = Join-Path $uiRoot 'scenarios'
    [void][IO.Directory]::CreateDirectory($scenarioRoot)
    Copy-Item -LiteralPath (Join-Path $sourceRootExact 'scripts/run-stage5cc-ui-acceptance.ps1') -Destination (Join-Path $scriptsRoot 'run-stage5cc-ui-acceptance.ps1')
    foreach ($module in @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner', 'Workflows', 'Metadata', 'VisualGate')) {
        Copy-Item -LiteralPath (Join-Path $sourceUiRoot ('TraceFold.UiAcceptance.' + $module + '.psm1')) -Destination (Join-Path $uiRoot ('TraceFold.UiAcceptance.' + $module + '.psm1'))
    }
    Copy-Item -LiteralPath (Join-Path $sourceUiRoot 'report-template') -Destination (Join-Path $uiRoot 'report-template') -Recurse
    foreach ($name in @(Get-UiAcceptanceScenarioName)) {
        if ($WithoutScenario -ccontains $name) { continue }
        $entryPath = Join-Path $scenarioRoot (Get-UiAcceptanceScenarioFile -Name $name)
        if ($RealScenario -ccontains $name) {
            Copy-Item -LiteralPath (Join-Path $sourceUiRoot ('scenarios/' + (Get-UiAcceptanceScenarioFile -Name $name))) -Destination $entryPath
        }
        elseif ($name -ceq $Scenario) {
            $null = New-OfflineScenarioStub -Path $entryPath -Outcome $Outcome
        }
        else {
            [IO.File]::WriteAllText($entryPath, ('# Marker for the ' + $name + ' entry.' + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
        }
    }

    $supportRoot = Join-Path $Root 'runtime/support'
    [void][IO.Directory]::CreateDirectory($supportRoot)
    Copy-Item -Path (Join-Path $SupportPayload '*') -Destination $supportRoot -Recurse -Force
    $driverRoot = Join-Path $Root 'runtime/driver'
    [void][IO.Directory]::CreateDirectory($driverRoot)
    [IO.File]::WriteAllBytes((Join-Path $driverRoot 'winapp.exe'), [byte[]] @(1, 2, 3))

    $contractDirectory = Join-Path $Root 'apps/media-desktop/tools/ui-acceptance-contract'
    [void][IO.Directory]::CreateDirectory($contractDirectory)
    $contractPath = Join-Path $contractDirectory 'stage5cc-uia-contract.json'
    $fixturePath = ''
    if ([string]::IsNullOrWhiteSpace($ContractSource)) {
        $fixtureRelative = 'apps/media-api/tests/fixtures/media/paired.mp4'
        $fixturePath = Join-Path $Root $fixtureRelative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $fixturePath))
        $fixtureBytes = [Text.UTF8Encoding]::new($false).GetBytes('offline-fixture-bytes')
        [IO.File]::WriteAllBytes($fixturePath, $fixtureBytes)
        $contract = [ordered] @{
            schemaVersion = 1
            entries       = @()
            fixtureAssets = @([ordered] @{
                fixedFilename  = 'paired.mp4'
                repositoryPath = $fixtureRelative
                sha256         = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash
                sizeBytes      = [long] $fixtureBytes.Length
                seedDirectory  = 'inbox'
            })
        }
        [IO.File]::WriteAllText($contractPath, ($contract | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    }
    else {
        Copy-Item -LiteralPath $ContractSource -Destination $contractPath -Force
        $sourceAssets = if ([string]::IsNullOrWhiteSpace($FixtureSourceRoot)) { $sourceRootExact } else { $FixtureSourceRoot }
        $contractDocument = [IO.File]::ReadAllText($contractPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        foreach ($asset in @($contractDocument.fixtureAssets)) {
            $relative = [string] $asset.repositoryPath
            $destination = Join-Path $Root $relative
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
            Copy-Item -LiteralPath (Join-Path $sourceAssets $relative) -Destination $destination -Force
            if ([string]::IsNullOrWhiteSpace($fixturePath)) { $fixturePath = $destination }
        }
    }

    $members = New-Object 'System.Collections.Generic.List[object]'
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($Root.Length).TrimStart([char] 92) -replace '\\', '/'
        [void] $members.Add([ordered] @{
            path   = $relative
            bytes  = [long] $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            source = (Get-OfflineMemberSource -Relative $relative)
        })
    }
    $supportExecutable = Join-Path $supportRoot 'TraceFold.Media.UiAcceptance.Support.exe'
    $driverExecutable = Join-Path $driverRoot 'winapp.exe'
    $manifest = [ordered] @{
        schema        = 'tracefold-ui-acceptance-toolkit'
        schemaVersion = 1
        layout        = [ordered] @{
            supportRoot  = 'runtime/support'
            driverRoot   = 'runtime/driver'
            contractPath = 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
            resultsRoot  = 'results'
        }
        driver        = [ordered] @{
            memberName = 'winapp.exe'
            bytes      = [long] (Get-Item -LiteralPath $driverExecutable).Length
            sha256     = (Get-FileHash -LiteralPath $driverExecutable -Algorithm SHA256).Hash
            source     = 'unpacked-driver-archive'
        }
        support       = [ordered] @{
            executable = 'TraceFold.Media.UiAcceptance.Support.exe'
            bytes      = [long] (Get-Item -LiteralPath $supportExecutable).Length
            sha256     = (Get-FileHash -LiteralPath $supportExecutable -Algorithm SHA256).Hash
        }
        members       = $members.ToArray()
    }
    [IO.File]::WriteAllText((Join-Path $Root 'toolkit.json'), ($manifest | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))

    return [pscustomobject] @{
        Root              = $Root
        EntryPath         = Join-Path $scriptsRoot 'run-stage5cc-ui-acceptance.ps1'
        SupportExecutable = $supportExecutable
        DriverExecutable  = $driverExecutable
        DriverSha256      = (Get-FileHash -LiteralPath $driverExecutable -Algorithm SHA256).Hash
        ContractPath      = $contractPath
        FixturePath       = $fixturePath
        ManifestPath      = Join-Path $Root 'toolkit.json'
    }
}

function Read-OfflineManifest {
    param([Parameter(Mandatory)][string] $Path)
    return ([IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json)
}

function Write-OfflineManifest {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)] $Document)
    [IO.File]::WriteAllText($Path, ($Document | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}

<#
.SYNOPSIS
    Drops one row from the package manifest while leaving the file in place.

.DESCRIPTION
    This is the shape the member-closure cases need: the file still exists and would still be
    imported or copied, but the table no longer describes it. The file must stay byte-identical so
    the refusal is about the missing row, not about a changed member.
#>
function Remove-OfflineMemberRow {
    param(
        [Parameter(Mandatory)][string] $ManifestPath,
        [Parameter(Mandatory)][string] $Relative
    )

    $document = Read-OfflineManifest -Path $ManifestPath
    $key = $Relative -replace '\\', '/'
    $before = @($document.members).Count
    $kept = @($document.members | Where-Object { (([string] $_.path) -replace '\\', '/') -cne $key })
    if ($kept.Count -eq $before) { throw ('the member row to remove is not present: ' + $key) }
    $document.members = $kept
    Write-OfflineManifest -Path $ManifestPath -Document $document
}

<#
.SYNOPSIS
    Starts the real entry as a child process and answers its exit code.

.DESCRIPTION
    The entry answers with 'exit', so it can only be observed from a child process. The child runs
    with the same host that runs this suite, from the caller's working directory, and an optional
    directory is prepended to PATH so a sentinel can prove whether a tool was invoked.
#>
function Invoke-OfflineEntry {
    param(
        [Parameter(Mandatory)][string] $EntryPath,
        $Parameters = $null,
        [Parameter(Mandatory)][string] $WorkingDirectory,
        [string] $PathPrefix = '',
        # Positional invocations are passed through unchanged: the point of that case is what the
        # entry binds from a bare argv, so nothing may be reordered or named here.
        [AllowEmptyCollection()][string[]] $RawArguments = @()
    )

    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
    if ($RawArguments.Count -eq 0 -and $null -eq $Parameters) { throw 'either named parameters or raw arguments are required' }
    $arguments = @('-NoProfile', '-File', $EntryPath)
    if ($RawArguments.Count -gt 0) { $arguments += $RawArguments }
    else { foreach ($key in $Parameters.Keys) { $arguments += @(('-' + $key), [string] $Parameters[$key]) } }
    $previousPath = $env:PATH
    $previousLocation = (Get-Location).Path
    $previousErrorAction = $ErrorActionPreference
    try {
        if (-not [string]::IsNullOrWhiteSpace($PathPrefix)) { $env:PATH = $PathPrefix + ';' + $previousPath }
        Set-Location -LiteralPath $WorkingDirectory
        # Windows PowerShell turns a native command's stderr into a terminating error while the
        # preference is Stop, which would hide the child's exit code and its own message. The
        # refusal text is the evidence here, so the merge runs with Continue and the real code is
        # read immediately after.
        $ErrorActionPreference = 'Continue'
        $output = (& $hostExe @arguments 2>&1 | Out-String)
        $code = $LASTEXITCODE
        return [pscustomobject] @{ ExitCode = $code; Output = $output }
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        $env:PATH = $previousPath
        Set-Location -LiteralPath $previousLocation
    }
}

function Find-OfflineResultLeaf {
    param([Parameter(Mandatory)][string] $ResultsRoot)
    if (-not [IO.Directory]::Exists($ResultsRoot)) { return $null }
    $leaves = @(Get-ChildItem -LiteralPath $ResultsRoot -Directory | Sort-Object LastWriteTime -Descending)
    if ($leaves.Count -eq 0) { return $null }
    return $leaves[0].FullName
}

function Read-OfflineJson {
    param([Parameter(Mandatory)][string] $Path)
    # The entry writes UTF-8 without a BOM; Windows PowerShell would decode that as ANSI and
    # corrupt any non-ASCII text in a reason field, so the bytes are decoded as UTF-8 here.
    return ([IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json)
}

# --- cases ------------------------------------------------------------------------------------

function Get-TestCases {
    return @(
        @{
            Name = 'a moved toolkit reaches the real runner, writes its report and hands the root back'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload -Scenario 'StartupLayout'
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    $working = Join-Path $base 'cwd'
                    [void][IO.Directory]::CreateDirectory($working)
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal

                    # No -OutputDirectory and no archive: the default leaf and the package mode are
                    # both exercised by this one call.
                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory $working -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 0 $result.ExitCode ('entry exit code; output: ' + $result.Output)

                    $leaf = Find-OfflineResultLeaf -ResultsRoot (Join-Path $package.Root 'results')
                    Assert-True ($null -ne $leaf) 'the default output leaf is under toolkit/results'
                    $summary = Read-OfflineJson -Path (Join-Path $leaf 'summary.json')
                    Assert-Equal 'Passed' ([string] $summary.status)
                    Assert-Equal 1 @($summary.entries).Count
                    Assert-Equal 1 ([int] $summary.checks.dispatchedCount)
                    Assert-Equal $false ([bool] $summary.checks.blockedBeforeDispatch)

                    $records = @(Get-Content -LiteralPath $scenarioJournal | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
                    Assert-Equal 1 $records.Count
                    $record = $records[0]
                    Assert-Equal $package.ContractPath ([string] $record.contractPath) 'the packaged contract is what the scenario received'
                    Assert-Equal '' ([string] $record.winAppCliArchivePath) 'no archive path is handed to the scenario in package mode'
                    Assert-Equal $true ([bool] $record.supportExists) 'the packaged support reached the run root'
                    Assert-Equal $true ([bool] $record.driverExists) 'the packaged driver reached the run root'
                    Assert-Equal $package.DriverSha256 ([string] $record.driverSha256) 'the run-root driver is the verified package member'
                    Assert-True ([string] $record.supportPath -like '*\support\*') 'the support copy lives in the owned root'
                    Assert-True ([string] $record.winAppPath -like '*\winapp\winapp.exe') 'the driver copy lives in the owned root'

                    $supportLines = @([IO.File]::ReadAllLines($supportJournal) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                    Assert-Equal 2 $supportLines.Count 'one create and one delete'
                    Assert-Equal ('create|' + [string] $record.ownedRoot) $supportLines[0]
                    Assert-Equal ('delete|' + [string] $record.ownedRoot) $supportLines[1]
                    Assert-True (-not [IO.Directory]::Exists([string] $record.ownedRoot)) 'the owned root was handed back'

                    $report = Read-OfflineJson -Path (Join-Path $leaf 'StartupLayout/report.json')
                    Assert-Equal 'Passed' ([string] @($report.scenarios)[0].status)
                    Assert-Equal 'Passed' ([string] $report.cleanup.status)
                    # Identity domains: no archive and no git were observed, and neither was faked.
                    Assert-True ($null -eq $report.sourceCommit) 'a package without git keeps sourceCommit null'
                    Assert-Equal 'unknown' ([string] $report.metadataEvidence.sourceCommitDomain.state)
                    Assert-True ($null -eq $report.metadataEvidence.driverClasses.archiveSha256) 'no archive digest is claimed'
                    Assert-Equal 'not-packaged' ([string] $report.metadataEvidence.driverClasses.archiveState)
                    Assert-Equal $package.DriverSha256 ([string] $report.metadataEvidence.driverClasses.extractedExeSha256)
                    # The package has no archive member either: the state says so instead of the
                    # comparison collapsing to null with no explanation.
                    Assert-Equal 'not-packaged' ([string] $report.metadataEvidence.driverClasses.archiveMemberState)
                    Assert-True ($null -eq $report.metadataEvidence.driverClasses.matchesManifest) 'an unobserved archive comparison stays null'
                    Assert-True ($null -eq $report.metadataEvidence.driverClasses.matchesArchiveMember) 'an unobserved member comparison stays null'

                    # The second persisted evidence document must agree with the report: null ZIP
                    # values, explicit not-packaged states, and the package member comparison that
                    # really was measured.
                    $hostCondition = Read-OfflineJson -Path (Join-Path $leaf 'host-condition.json')
                    $scenarioEvidence = @($hostCondition.scenarios | Where-Object { [string] $_.name -ceq 'StartupLayout' })[0]
                    Assert-True ($null -ne $scenarioEvidence) 'the host evidence carries this scenario'
                    $driverFacts = $scenarioEvidence.instances.driver
                    Assert-True ($null -eq $driverFacts.archivePath) 'no archive path in the host evidence'
                    Assert-True ($null -eq $driverFacts.archiveSha256) 'no archive digest in the host evidence'
                    Assert-True ($null -eq $driverFacts.matchesManifest) 'the host evidence does not claim an archive comparison'
                    Assert-True ($null -eq $driverFacts.matchesArchiveMember) 'the host evidence does not claim a member comparison'
                    Assert-Equal 'not-packaged' ([string] $driverFacts.memberReadState)
                    Assert-Equal 'not-packaged' ([string] $driverFacts.archiveState)
                    Assert-Equal $true ([bool] $driverFacts.matchesPackageMember)
                    Assert-Equal $true ([bool] $scenarioEvidence.instances.support.allMatchManifest) 'the copied support members match the manifest, not only the source directory'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a scenario that fails in package mode answers blocked and keeps its report'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload -Outcome 'Failed'
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 1 $result.ExitCode ('a failed scenario answers exit 1; output: ' + $result.Output)
                    $leaf = Find-OfflineResultLeaf -ResultsRoot (Join-Path $package.Root 'results')
                    $summary = Read-OfflineJson -Path (Join-Path $leaf 'summary.json')
                    Assert-Equal 'Failed' ([string] $summary.status)
                    Assert-Equal 'Failed' ([string] @($summary.entries)[0].status)
                    $report = Read-OfflineJson -Path (Join-Path $leaf 'StartupLayout/report.json')
                    Assert-Equal 'Failed' ([string] @($report.scenarios)[0].status)
                    Assert-Equal 'Passed' ([string] $report.cleanup.status)

                    # The cleanup field is the Runner's own report of what happened; the owned root
                    # itself is witnessed by the support tool's journal, which must name the same
                    # root the scenario was given for both the create and the delete.
                    $records = @(Get-Content -LiteralPath $scenarioJournal | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
                    Assert-Equal 1 $records.Count
                    $ownedRoot = [string] $records[0].ownedRoot
                    $supportLines = @([IO.File]::ReadAllLines($supportJournal) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                    Assert-Equal 2 $supportLines.Count 'one create and one delete for a failed scenario'
                    Assert-Equal ('create|' + $ownedRoot) $supportLines[0]
                    Assert-Equal ('delete|' + $ownedRoot) $supportLines[1]
                    Assert-True (-not [IO.Directory]::Exists($ownedRoot)) 'the failed scenario still handed its root back'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a missing member stops the invocation before any support, driver or scenario starts'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    [IO.File]::Delete((Join-Path $package.Root 'scripts/ui-acceptance/TraceFold.UiAcceptance.Runner.psm1'))

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode ('a broken package answers exit 2; output: ' + $result.Output)
                    Assert-Contains $result.Output 'toolkit.member_missing'
                    Assert-True (-not [IO.File]::Exists($scenarioJournal)) 'no scenario ran'
                    Assert-True (-not [IO.File]::Exists($supportJournal)) 'the support tool was never started'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'no results leaf was created'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a changed member is refused by its recorded hash'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $modulePath = Join-Path $package.Root 'scripts/ui-acceptance/TraceFold.UiAcceptance.Evidence.psm1'
                    [IO.File]::AppendAllText($modulePath, [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode
                    Assert-Contains $result.Output 'toolkit.member_size_mismatch'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'nothing was written'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a member path that leaves the package is refused'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $document = Read-OfflineManifest -Path $package.ManifestPath
                    $document.members = @($document.members) + @([pscustomobject] @{
                        path = '../outside.ps1'; bytes = [long] 3; sha256 = ('A' * 64); source = 'module'
                    })
                    Write-OfflineManifest -Path $package.ManifestPath -Document $document

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode
                    Assert-Contains $result.Output 'toolkit.path_traversal'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a duplicated member is refused instead of being verified twice'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $document = Read-OfflineManifest -Path $package.ManifestPath
                    $first = @($document.members)[0]
                    $document.members = @($document.members) + @([pscustomobject] @{
                        path = [string] $first.path; bytes = [long] $first.bytes; sha256 = [string] $first.sha256; source = [string] $first.source
                    })
                    Write-OfflineManifest -Path $package.ManifestPath -Document $document

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode
                    Assert-Contains $result.Output 'toolkit.member_duplicate'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a reparse point inside the package is refused before the member is read'
            Body = {
                $base = New-OfflineBase
                $junction = $null
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $outside = Join-Path $base 'outside'
                    [void][IO.Directory]::CreateDirectory($outside)
                    $linkedBytes = [Text.UTF8Encoding]::new($false).GetBytes('linked-member')
                    [IO.File]::WriteAllBytes((Join-Path $outside 'linked.bin'), $linkedBytes)
                    $junction = Join-Path $package.Root 'linked'
                    $null = New-Item -ItemType Junction -Path $junction -Target $outside
                    $document = Read-OfflineManifest -Path $package.ManifestPath
                    $document.members = @($document.members) + @([pscustomobject] @{
                        path   = 'linked/linked.bin'
                        bytes  = [long] $linkedBytes.Length
                        sha256 = (Get-FileHash -LiteralPath (Join-Path $outside 'linked.bin') -Algorithm SHA256).Hash
                        source = 'module'
                    })
                    Write-OfflineManifest -Path $package.ManifestPath -Document $document

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode
                    Assert-Contains $result.Output 'toolkit.path_reparse'
                }
                finally {
                    if ($null -ne $junction -and [IO.Directory]::Exists($junction)) { [IO.Directory]::Delete($junction) }
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'the two invocation shapes are mutually exclusive and one is required'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $archive = Join-Path $base 'driver.zip'
                    [IO.File]::WriteAllBytes($archive, [byte[]] @(1, 2, 3))

                    $both = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory     = $package.Root
                        WinAppCliArchivePath = $archive
                        ProductDirectory     = $product
                        Scenario             = 'StartupLayout'
                    })
                    Assert-Equal 2 $both.ExitCode 'both shapes answer exit 2'
                    Assert-Contains $both.Output 'exactly one of'

                    $neither = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $neither.ExitCode 'no shape answers exit 2'
                    Assert-Contains $neither.Output 'exactly one of'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a product directory inside the toolkit is refused'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $product = New-OfflineProduct -Path (Join-Path $package.Root 'product')
                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode
                    Assert-Contains $result.Output 'toolkit.product_overlap'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'nothing was written'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'package mode never invokes dotnet or git, and the sentinels are live'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $sentinelDirectory = Join-Path $base 'sentinels'
                    [void][IO.Directory]::CreateDirectory($sentinelDirectory)
                    $dotnetRecord = Join-Path $base 'dotnet-was-invoked.txt'
                    $gitRecord = Join-Path $base 'git-was-invoked.txt'
                    foreach ($sentinel in @(
                        @{ Name = 'dotnet.cmd'; Record = $dotnetRecord },
                        @{ Name = 'git.cmd'; Record = $gitRecord }
                    )) {
                        $shim = '@echo off' + [Environment]::NewLine + 'echo invoked>>"' + $sentinel.Record + '"' + [Environment]::NewLine + 'exit /b 1' + [Environment]::NewLine
                        [IO.File]::WriteAllText((Join-Path $sentinelDirectory $sentinel.Name), $shim, [Text.UTF8Encoding]::new($false))
                    }

                    # With both failing shims first on PATH, a compile or a repository probe would
                    # fail the run. It passes, so neither tool was started.
                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -PathPrefix $sentinelDirectory -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 0 $result.ExitCode ('package mode does not need the SDK or git; output: ' + $result.Output)
                    Assert-True (-not [IO.File]::Exists($dotnetRecord)) 'dotnet was never invoked'
                    Assert-True (-not [IO.File]::Exists($gitRecord)) 'git was never invoked'

                    # Positive control: the same shims do answer when something calls them, so the
                    # two absences above are a result of not calling, not of dead sentinels.
                    & (Join-Path $sentinelDirectory 'git.cmd') | Out-Null
                    & (Join-Path $sentinelDirectory 'dotnet.cmd') | Out-Null
                    Assert-True ([IO.File]::Exists($gitRecord)) 'the git sentinel is live'
                    Assert-True ([IO.File]::Exists($dotnetRecord)) 'the dotnet sentinel is live'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'an archive placed beside the package is never unpacked'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal
                    # A real archive at the source-tree location, carrying a marker the packaged
                    # driver does not have. If the entry unpacked it into the run root, the marker
                    # would show up in the directory the scenario actually received.
                    $legacyDirectory = Join-Path $package.Root 'apps/media-desktop/tools/winapp/win-x64'
                    [void][IO.Directory]::CreateDirectory($legacyDirectory)
                    $staging = Join-Path $base 'archive-sentinel'
                    [void][IO.Directory]::CreateDirectory($staging)
                    [IO.File]::WriteAllText((Join-Path $staging 'unpack-marker.txt'), 'unpacked', [Text.UTF8Encoding]::new($false))
                    [IO.File]::WriteAllBytes((Join-Path $staging 'winapp.exe'), [byte[]] @(5, 5, 5))
                    $sentinelArchive = Join-Path $legacyDirectory 'winappcli-x64.zip'
                    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $sentinelArchive -Force

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 0 $result.ExitCode ('the packaged driver is used, not the archive beside it; output: ' + $result.Output)
                    $records = @(Get-Content -LiteralPath $scenarioJournal | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
                    $driverFiles = @($records[0].driverFiles | ForEach-Object { [string] $_ })
                    Assert-True ($driverFiles -ccontains 'winapp.exe') 'the packaged driver reached the scenario'
                    Assert-True ($driverFiles -cnotcontains 'unpack-marker.txt') 'the archive beside the package was not unpacked into the run root'

                    # Positive control: the sentinel archive really does carry the marker, so the
                    # absence above is a result of not extracting it, not of an empty zip.
                    $control = Join-Path $base 'control-extract'
                    Expand-Archive -LiteralPath $sentinelArchive -DestinationPath $control
                    Assert-True ([IO.File]::Exists((Join-Path $control 'unpack-marker.txt'))) 'the sentinel archive is a real archive'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'All in package mode still refuses a registry entry that is not there'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload -WithoutScenario 'Visual'
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'All'
                    })
                    Assert-Equal 1 $result.ExitCode ('All with a missing entry answers Blocked; output: ' + $result.Output)
                    $leaf = Find-OfflineResultLeaf -ResultsRoot (Join-Path $package.Root 'results')
                    Assert-True ($null -ne $leaf) 'the refusal is recorded'
                    $summary = Read-OfflineJson -Path (Join-Path $leaf 'summary.json')
                    Assert-Equal 'Blocked' ([string] $summary.status)
                    Assert-Equal 'scenario_entry_missing' ([string] $summary.reason)
                    Assert-Equal $true ([bool] $summary.checks.blockedBeforeDispatch)
                    Assert-Equal 0 ([int] $summary.checks.dispatchedCount)
                    Assert-True (-not [IO.File]::Exists($scenarioJournal)) 'nothing was dispatched'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'the archive shape still runs the same entry'
            Body = {
                $base = New-OfflineBase
                try {
                    $repository = Join-Path $base 'repo'
                    $scripts = Join-Path $repository 'scripts'
                    $fakeUi = Join-Path $scripts 'ui-acceptance'
                    [void][IO.Directory]::CreateDirectory((Join-Path $fakeUi 'scenarios'))
                    foreach ($module in @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner', 'Workflows', 'Metadata', 'VisualGate')) {
                        Copy-Item -LiteralPath (Join-Path $script:uiRoot ('TraceFold.UiAcceptance.' + $module + '.psm1')) -Destination (Join-Path $fakeUi ('TraceFold.UiAcceptance.' + $module + '.psm1'))
                    }
                    Copy-Item -LiteralPath (Join-Path $script:root 'scripts/run-stage5cc-ui-acceptance.ps1') -Destination (Join-Path $scripts 'run-stage5cc-ui-acceptance.ps1')
                    $manifestDirectory = Join-Path $repository 'apps/media-desktop/tools/winapp/win-x64'
                    [void][IO.Directory]::CreateDirectory($manifestDirectory)
                    foreach ($name in @(Get-UiAcceptanceScenarioName)) {
                        $entryPath = Join-Path $fakeUi ('scenarios/' + (Get-UiAcceptanceScenarioFile -Name $name))
                        if ($name -ceq 'StartupLayout') { $null = New-OfflineScenarioStub -Path $entryPath } else { [IO.File]::WriteAllText($entryPath, ('# Marker for the ' + $name + ' entry.'), [Text.UTF8Encoding]::new($false)) }
                    }
                    $supportProjectRoot = Join-Path $repository 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support'
                    # The archive shape publishes the project it finds, exactly as before this change.
                    $null = Write-OfflineSupportProject -ProjectRoot $supportProjectRoot
                    $staging = Join-Path $base 'driver-staging'
                    [void][IO.Directory]::CreateDirectory($staging)
                    [IO.File]::WriteAllBytes((Join-Path $staging 'winapp.exe'), [byte[]] @(1, 2, 3))
                    $archive = Join-Path $base 'driver.zip'
                    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive -Force
                    $archiveBytes = [IO.File]::ReadAllBytes($archive)
                    $driverManifest = [ordered] @{ version = '0.5.0'; archiveSize = [long] $archiveBytes.Length; sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash }
                    [IO.File]::WriteAllText((Join-Path $manifestDirectory 'manifest.json'), ($driverManifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))

                    $result = Invoke-OfflineEntry -EntryPath (Join-Path $scripts 'run-stage5cc-ui-acceptance.ps1') -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        WinAppCliArchivePath = $archive
                        ProductDirectory     = $product
                        OutputDirectory      = (Join-Path $base 'out')
                        Scenario             = 'StartupLayout'
                    })
                    $summaryPath = Join-Path $base 'out/summary.json'
                    $reasonText = 'no summary'
                    if ([IO.File]::Exists($summaryPath)) {
                        $probeSummary = Read-OfflineJson -Path $summaryPath
                        $reasonText = [string] @($probeSummary.entries)[0].reason
                    }
                    Assert-Equal 0 $result.ExitCode ('the archive shape still passes; output: ' + $result.Output + '; reason: ' + $reasonText)
                    $summary = Read-OfflineJson -Path $summaryPath
                    Assert-Equal 'Passed' ([string] $summary.status) ('archive shape status; reason: ' + [string] @($summary.entries)[0].reason)

                    # The original positional call binds product, archive, output and scenario in
                    # that order; the package switch was appended after them for exactly this case.
                    $positionalOut = Join-Path $base 'out-positional'
                    $positional = Invoke-OfflineEntry -EntryPath (Join-Path $scripts 'run-stage5cc-ui-acceptance.ps1') -WorkingDirectory (Join-Path $base 'cwd') -RawArguments @($product, $archive, $positionalOut, 'StartupLayout')
                    Assert-Equal 0 $positional.ExitCode ('the positional call still binds the old parameters; output: ' + $positional.Output)
                    $positionalSummary = Read-OfflineJson -Path (Join-Path $positionalOut 'summary.json')
                    Assert-Equal 'Passed' ([string] $positionalSummary.status) 'the positional call really ran'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a module that exists but has no member row is refused'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    # The module file stays exactly as built; only its row disappears. The entry
                    # would import it, so the table has to be the thing that refuses it.
                    Remove-OfflineMemberRow -ManifestPath $package.ManifestPath -Relative 'scripts/ui-acceptance/TraceFold.UiAcceptance.Runner.psm1'

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode ('an unlisted module answers exit 2; output: ' + $result.Output)
                    Assert-Contains $result.Output 'toolkit.member_unlisted'
                    Assert-True (-not [IO.File]::Exists($scenarioJournal)) 'no scenario ran'
                    Assert-True (-not [IO.File]::Exists($supportJournal)) 'the support tool was never started'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'nothing was written'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a support dependency that exists but has no member row is refused'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    # A dependency the copied payload carries: the copy would run it, so its row
                    # cannot be optional.
                    Remove-OfflineMemberRow -ManifestPath $package.ManifestPath -Relative 'runtime/support/TraceFold.Media.UiAcceptance.Support.dll'

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode ('an unlisted support dependency answers exit 2; output: ' + $result.Output)
                    Assert-Contains $result.Output 'toolkit.member_unlisted'
                    Assert-True (-not [IO.File]::Exists($supportJournal)) 'the support tool was never started'
                    Assert-True (-not [IO.File]::Exists($scenarioJournal)) 'no scenario ran'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'an equal-length change to a member is caught by its hash'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    # One byte changes and the length does not, so only the hash can notice.
                    $modulePath = Join-Path $package.Root 'scripts/ui-acceptance/TraceFold.UiAcceptance.Evidence.psm1'
                    $bytes = [IO.File]::ReadAllBytes($modulePath)
                    $index = [int] [Math]::Floor($bytes.Length / 2)
                    $bytes[$index] = [byte] (($bytes[$index] + 1) % 256)
                    [IO.File]::WriteAllBytes($modulePath, $bytes)

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode
                    Assert-Contains $result.Output 'toolkit.member_hash_mismatch'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'nothing was written'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a package that declares another support executable name is refused'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    # Both names exist on disk: the standard one the entry consumes and the declared
                    # one it would never use. The mismatch is refused before anything starts.
                    $other = Join-Path $package.Root 'runtime/support/OtherSupport.exe'
                    [IO.File]::WriteAllBytes($other, [byte[]] @(4, 5, 6))
                    $document = Read-OfflineManifest -Path $package.ManifestPath
                    $document.members = @($document.members) + @([pscustomobject] @{
                        path = 'runtime/support/OtherSupport.exe'; bytes = [long] 3
                        sha256 = (Get-FileHash -LiteralPath $other -Algorithm SHA256).Hash; source = 'support'
                    })
                    $document.support.executable = 'OtherSupport.exe'
                    Write-OfflineManifest -Path $package.ManifestPath -Document $document

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode
                    Assert-Contains $result.Output 'toolkit.support_executable_unsupported'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'nothing was written'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a package that declares another driver member name is refused'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $other = Join-Path $package.Root 'runtime/driver/winapp2.exe'
                    [IO.File]::WriteAllBytes($other, [byte[]] @(7, 7, 7))
                    $document = Read-OfflineManifest -Path $package.ManifestPath
                    $document.members = @($document.members) + @([pscustomobject] @{
                        path = 'runtime/driver/winapp2.exe'; bytes = [long] 3
                        sha256 = (Get-FileHash -LiteralPath $other -Algorithm SHA256).Hash; source = 'driver'
                    })
                    $document.driver.memberName = 'winapp2.exe'
                    Write-OfflineManifest -Path $package.ManifestPath -Document $document

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode
                    Assert-Contains $result.Output 'toolkit.driver_member_unsupported'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'nothing was written'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a junction where the results directory would be is refused and the outside stays untouched'
            Body = {
                $base = New-OfflineBase
                $junction = $null
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    $outside = Join-Path $base 'outside'
                    [void][IO.Directory]::CreateDirectory($outside)
                    [IO.File]::WriteAllText((Join-Path $outside 'sentinel.txt'), 'outside', [Text.UTF8Encoding]::new($false))
                    $junction = Join-Path $package.Root 'results'
                    $null = New-Item -ItemType Junction -Path $junction -Target $outside

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode ('a junctioned results directory answers exit 2; output: ' + $result.Output)
                    Assert-Contains $result.Output 'toolkit.results_reparse'
                    $outsideFiles = @(Get-ChildItem -LiteralPath $outside -Recurse -File)
                    Assert-Equal 1 $outsideFiles.Count 'the package wrote nothing outside its root'
                    Assert-Equal 'sentinel.txt' $outsideFiles[0].Name
                    Assert-Equal 'outside' ([IO.File]::ReadAllText($outsideFiles[0].FullName))
                    Assert-True (-not [IO.File]::Exists($supportJournal)) 'the support tool was never started'
                }
                finally {
                    if ($null -ne $junction -and [IO.Directory]::Exists($junction)) { [IO.Directory]::Delete($junction) }
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a product path that aliases into the toolkit is refused'
            Body = {
                $base = New-OfflineBase
                $junction = $null
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $inside = New-OfflineProduct -Path (Join-Path $package.Root 'product-inside')
                    $junction = Join-Path $base 'product-alias'
                    $null = New-Item -ItemType Junction -Path $junction -Target $inside

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $junction
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode ('a product alias answers exit 2; output: ' + $result.Output)
                    Assert-Contains $result.Output 'toolkit.product_reparse'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'nothing was written'
                }
                finally {
                    if ($null -ne $junction -and [IO.Directory]::Exists($junction)) { [IO.Directory]::Delete($junction) }
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'the declared results root is where the default output lands'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $document = Read-OfflineManifest -Path $package.ManifestPath
                    $document.layout.resultsRoot = 'out-results'
                    Write-OfflineManifest -Path $package.ManifestPath -Document $document

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 0 $result.ExitCode ('the declared results root is usable; output: ' + $result.Output)
                    $leaf = Find-OfflineResultLeaf -ResultsRoot (Join-Path $package.Root 'out-results')
                    Assert-True ($null -ne $leaf) 'the default output is under the declared results root'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'the hardcoded name is not used'
                    $summary = Read-OfflineJson -Path (Join-Path $leaf 'summary.json')
                    Assert-Equal 'Passed' ([string] $summary.status)
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'a preparation failure after the root exists dispatches nothing and hands the same root back'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                $previousPoison = $env:TRACEFOLD_STUB_SUPPORT_POISON
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    # The support double occupies the directory name the payload is copied into, so
                    # preparation fails after the owned root exists but before any scenario starts.
                    $env:TRACEFOLD_STUB_SUPPORT_POISON = '1'

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 1 $result.ExitCode ('a preparation failure answers Blocked; output: ' + $result.Output)
                    $leaf = Find-OfflineResultLeaf -ResultsRoot (Join-Path $package.Root 'results')
                    Assert-True ($null -ne $leaf) 'the summary is still written'
                    $summary = Read-OfflineJson -Path (Join-Path $leaf 'summary.json')
                    Assert-Equal 'Blocked' ([string] $summary.status)
                    Assert-Equal 'Blocked' ([string] @($summary.entries)[0].executionStatus)
                    Assert-Equal 0 ([int] $summary.checks.dispatchedCount) 'no scenario was dispatched'
                    Assert-Equal $false ([bool] $summary.checks.blockedBeforeDispatch) 'the precheck was not what stopped it'
                    Assert-Equal '' ([string] @($summary.entries)[0].reportPath) 'no report was produced'
                    Assert-True (-not [IO.File]::Exists($scenarioJournal)) 'the scenario was never entered'
                    Assert-True (-not [IO.File]::Exists((Join-Path $leaf 'StartupLayout/report.json'))) 'no report file either'

                    # The owned root is witnessed by the support journal, not by the cleanup field:
                    # the same root that was created is the one that was deleted, and it is gone.
                    $supportLines = @([IO.File]::ReadAllLines($supportJournal) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                    Assert-Equal 2 $supportLines.Count 'one create and one delete'
                    Assert-True ($supportLines[0].StartsWith('create|')) 'the root was created first'
                    Assert-True ($supportLines[1].StartsWith('delete|')) 'the root was deleted by the failure path'
                    $createdRoot = $supportLines[0].Substring('create|'.Length)
                    $deletedRoot = $supportLines[1].Substring('delete|'.Length)
                    Assert-Equal $createdRoot $deletedRoot 'the same root was handed back'
                    Assert-True (-not [IO.Directory]::Exists($createdRoot)) 'the root is gone'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    $env:TRACEFOLD_STUB_SUPPORT_POISON = $previousPoison
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a migrated package runs without the old source layout or caches'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $built = New-OfflinePackage -Root (Join-Path $base 'build/moved-toolkit') -SupportPayload $payload
                    # Move the package after it was built: nothing may still point at where it was
                    # produced, and the run has to work from the new location alone.
                    $moved = Join-Path $base 'elsewhere/toolkit'
                    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $moved))
                    Move-Item -LiteralPath $built.Root -Destination $moved
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal

                    $result = Invoke-OfflineEntry -EntryPath (Join-Path $moved 'scripts/run-stage5cc-ui-acceptance.ps1') -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $moved
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 0 $result.ExitCode ('a moved package still runs; output: ' + $result.Output)
                    $summary = Read-OfflineJson -Path (Join-Path (Find-OfflineResultLeaf -ResultsRoot (Join-Path $moved 'results')) 'summary.json')
                    Assert-Equal 'Passed' ([string] $summary.status)

                    # The source-tree construction locations are simply not in the package, so the
                    # run above cannot have fallen back to a build directory, a cache or an archive.
                    foreach ($probe in @(
                        'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/TraceFold.Media.UiAcceptance.Support.csproj',
                        'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap',
                        'apps/media-desktop/.packages',
                        'apps/media-desktop/tools/winapp/win-x64/winappcli-x64.zip'
                    )) {
                        Assert-True (-not (Test-Path -LiteralPath (Join-Path $moved $probe))) ('the package does not carry ' + $probe)
                    }
                    $records = @(Get-Content -LiteralPath $scenarioJournal | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
                    Assert-Equal 1 $records.Count
                    Assert-True ([string] $records[0].contractPath -like ($moved + '\*')) 'the contract came from the moved package'
                    Assert-Equal $true ([bool] $records[0].supportExists) 'the support copy came from the moved package'
                    Assert-Equal $true ([bool] $records[0].driverExists) 'the driver copy came from the moved package'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'an empty template directory without the index file is refused'
            Body = {
                $base = New-OfflineBase
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    # The directory still exists, so a tree-only guard would accept it; the report
                    # writer would then fail on the missing index after the support root existed.
                    [IO.File]::Delete((Join-Path $package.Root 'scripts/ui-acceptance/report-template/index.html'))
                    Remove-OfflineMemberRow -ManifestPath $package.ManifestPath -Relative 'scripts/ui-acceptance/report-template/index.html'

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 2 $result.ExitCode ('a missing index template answers exit 2; output: ' + $result.Output)
                    Assert-Contains $result.Output 'toolkit.template_missing'
                    Assert-True (-not [IO.Directory]::Exists((Join-Path $package.Root 'results'))) 'nothing was written'
                }
                finally { Remove-UiTestTree -Path $base }
            }
        },
        @{
            Name = 'the aggregate engine is taken from the sibling copy the scenario uses'
            Body = {
                $base = New-OfflineBase
                $previousEngineJournal = $env:TRACEFOLD_STUB_ENGINE_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    # The real Conflict scenario runs; only the engine it imports is a double.
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload -RealScenario @('Conflict')
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $enginePath = Join-Path $package.Root 'scripts/ui-acceptance/scenarios/TraceFold.UiAcceptance.Aggregate.psm1'
                    Write-OfflineEngineStub -Path $enginePath
                    $document = Read-OfflineManifest -Path $package.ManifestPath
                    $document.members = @($document.members) + @([pscustomobject] @{
                        path = 'scripts/ui-acceptance/scenarios/TraceFold.UiAcceptance.Aggregate.psm1'
                        bytes = [long] ((Get-Item -LiteralPath $enginePath).Length)
                        sha256 = (Get-FileHash -LiteralPath $enginePath -Algorithm SHA256).Hash
                        source = 'module'
                    })
                    Write-OfflineManifest -Path $package.ManifestPath -Document $document
                    $engineJournal = Join-Path $base 'engine.txt'
                    $env:TRACEFOLD_STUB_ENGINE_JOURNAL = $engineJournal

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'Conflict'
                    })
                    Assert-Equal 0 $result.ExitCode ('the sibling engine runs the real scenario; output: ' + $result.Output)
                    $lines = @([IO.File]::ReadAllLines($engineJournal) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                    Assert-Equal 1 $lines.Count
                    Assert-Equal ($enginePath + '|Conflict') $lines[0] 'the scenario imported the sibling copy'
                }
                finally {
                    $env:TRACEFOLD_STUB_ENGINE_JOURNAL = $previousEngineJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'the aggregate engine falls back to the parent copy when the sibling is absent'
            Body = {
                $base = New-OfflineBase
                $previousEngineJournal = $env:TRACEFOLD_STUB_ENGINE_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload -RealScenario @('Conflict')
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $enginePath = Join-Path $package.Root 'scripts/ui-acceptance/TraceFold.UiAcceptance.Aggregate.psm1'
                    Write-OfflineEngineStub -Path $enginePath
                    $document = Read-OfflineManifest -Path $package.ManifestPath
                    $document.members = @($document.members) + @([pscustomobject] @{
                        path = 'scripts/ui-acceptance/TraceFold.UiAcceptance.Aggregate.psm1'
                        bytes = [long] ((Get-Item -LiteralPath $enginePath).Length)
                        sha256 = (Get-FileHash -LiteralPath $enginePath -Algorithm SHA256).Hash
                        source = 'module'
                    })
                    Write-OfflineManifest -Path $package.ManifestPath -Document $document
                    $engineJournal = Join-Path $base 'engine.txt'
                    $env:TRACEFOLD_STUB_ENGINE_JOURNAL = $engineJournal

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'Conflict'
                    })
                    Assert-Equal 0 $result.ExitCode ('the parent engine runs the real scenario; output: ' + $result.Output)
                    $lines = @([IO.File]::ReadAllLines($engineJournal) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                    Assert-Equal 1 $lines.Count
                    Assert-Equal ($enginePath + '|Conflict') $lines[0] 'the scenario fell back to the parent copy'
                }
                finally {
                    $env:TRACEFOLD_STUB_ENGINE_JOURNAL = $previousEngineJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'an aggregate engine present without a member row is refused'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload -RealScenario @('Conflict')
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    # The engine file exists at the position the scenario would import, but the
                    # table does not describe it.
                    Write-OfflineEngineStub -Path (Join-Path $package.Root 'scripts/ui-acceptance/scenarios/TraceFold.UiAcceptance.Aggregate.psm1')

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'Conflict'
                    })
                    Assert-Equal 2 $result.ExitCode ('an unlisted engine answers exit 2; output: ' + $result.Output)
                    Assert-Contains $result.Output 'toolkit.member_unlisted'
                    Assert-True (-not [IO.File]::Exists($supportJournal)) 'the support tool was never started'
                    Assert-True (-not [IO.File]::Exists($scenarioJournal)) 'the scenario was never entered'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a request whose aggregate engine is absent is refused before anything starts'
            Body = {
                $base = New-OfflineBase
                $previousScenarioJournal = $env:TRACEFOLD_STUB_SCENARIO_JOURNAL
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload -RealScenario @('Conflict')
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $scenarioJournal = Join-Path $base 'scenario.jsonl'
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $scenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'Conflict'
                    })
                    Assert-Equal 2 $result.ExitCode ('a missing engine answers exit 2; output: ' + $result.Output)
                    Assert-Contains $result.Output 'toolkit.scenario_dependency_missing'
                    Assert-True (-not [IO.File]::Exists($supportJournal)) 'the support tool was never started'
                    Assert-True (-not [IO.File]::Exists($scenarioJournal)) 'the scenario was never entered'
                    Assert-True ($null -eq (Find-OfflineResultLeaf -ResultsRoot (Join-Path $package.Root 'results'))) 'no run leaf was created'
                }
                finally {
                    $env:TRACEFOLD_STUB_SCENARIO_JOURNAL = $previousScenarioJournal
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'the MainChain media declaration is checked against the member table before it is read'
            Body = {
                $base = New-OfflineBase
                $previousSupportJournal = $env:TRACEFOLD_STUB_SUPPORT_JOURNAL
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $supportJournal = Join-Path $base 'support.txt'
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $supportJournal
                    $mediaRelative = 'apps/media-desktop/tools/ffmpeg/win-x64/manifest.json'
                    $mediaPath = Join-Path $package.Root ($mediaRelative -replace '/', '\')
                    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $mediaPath))
                    $media = [ordered] @{
                        version = 'test'
                        files   = [ordered] @{
                            'ffmpeg.exe'  = [ordered] @{ size = 3; sha256 = ('A' * 64) }
                            'ffprobe.exe' = [ordered] @{ size = 3; sha256 = ('B' * 64) }
                        }
                    }
                    [IO.File]::WriteAllText($mediaPath, ($media | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))

                    # Present but unlisted: the declaration decides a gate, so it must be verified.
                    $unlisted = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'MainChain'
                        OutputDirectory  = (Join-Path $base 'out-unlisted')
                    })
                    Assert-Equal 1 $unlisted.ExitCode ('an unlisted media declaration answers Blocked; output: ' + $unlisted.Output)
                    $unlistedSummary = Read-OfflineJson -Path (Join-Path $base 'out-unlisted/summary.json')
                    Assert-Equal 'Blocked' ([string] $unlistedSummary.status)
                    Assert-Equal 'pinned_media_tools_declaration_unverified' ([string] $unlistedSummary.reason)
                    Assert-True (-not [IO.File]::Exists($supportJournal)) 'the support tool was never started'

                    # Positive control: the same file, now a member, passes the table and reaches
                    # the next gate (the product does not carry the media tools).
                    $document = Read-OfflineManifest -Path $package.ManifestPath
                    $document.members = @($document.members) + @([pscustomobject] @{
                        path = $mediaRelative; bytes = [long] ((Get-Item -LiteralPath $mediaPath).Length)
                        sha256 = (Get-FileHash -LiteralPath $mediaPath -Algorithm SHA256).Hash; source = 'other'
                    })
                    Write-OfflineManifest -Path $package.ManifestPath -Document $document
                    $listed = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'MainChain'
                        OutputDirectory  = (Join-Path $base 'out-listed')
                    })
                    Assert-Equal 1 $listed.ExitCode
                    $listedSummary = Read-OfflineJson -Path (Join-Path $base 'out-listed/summary.json')
                    Assert-Equal 'pinned_media_tools_unavailable' ([string] $listedSummary.reason) 'the verified declaration passed and the next gate answered'
                    Assert-True (-not [IO.File]::Exists($supportJournal)) 'the support tool was still never started'
                }
                finally {
                    $env:TRACEFOLD_STUB_SUPPORT_JOURNAL = $previousSupportJournal
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'package mode never calls the unpack command, and the interception is live'
            Body = {
                $base = New-OfflineBase
                $previousModulePath = $env:PSModulePath
                $previousRecord = $env:TRACEFOLD_UNPACK_SENTINEL_RECORD
                try {
                    $payload = New-OfflineSupportPayload
                    $package = New-OfflinePackage -Root (Join-Path $base 'moved-toolkit') -SupportPayload $payload
                    $product = New-OfflineProduct -Path (Join-Path $base 'product')
                    [void][IO.Directory]::CreateDirectory((Join-Path $base 'cwd'))
                    $sentinelModules = New-OfflineUnpackSentinelModule -Root (Join-Path $base 'modules')
                    $unpackRecord = Join-Path $base 'unpack-invoked.txt'
                    $env:PSModulePath = $sentinelModules + ';' + $previousModulePath
                    $env:TRACEFOLD_UNPACK_SENTINEL_RECORD = $unpackRecord

                    # Positive control: the same interception path the entry would resolve through
                    # really answers when something calls the unpack command.
                    $probe = Join-Path $base 'call-unpack.ps1'
                    [IO.File]::WriteAllText($probe, "Expand-Archive -LiteralPath 'x.zip' -DestinationPath 'y'", [Text.UTF8Encoding]::new($false))
                    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
                    # The control is supposed to fail; on Windows PowerShell a native command's stderr
                    # becomes a terminating error under Stop, so the call runs with Continue.
                    $previousErrorAction = $ErrorActionPreference
                    try {
                        $ErrorActionPreference = 'Continue'
                        $null = (& $hostExe -NoProfile -File $probe 2>&1 | Out-String)
                    }
                    finally { $ErrorActionPreference = $previousErrorAction }
                    Assert-True ([IO.File]::Exists($unpackRecord)) 'the unpack interception is live'
                    [IO.File]::Delete($unpackRecord)

                    $result = Invoke-OfflineEntry -EntryPath $package.EntryPath -WorkingDirectory (Join-Path $base 'cwd') -Parameters ([ordered] @{
                        ToolkitDirectory = $package.Root
                        ProductDirectory = $product
                        Scenario         = 'StartupLayout'
                    })
                    Assert-Equal 0 $result.ExitCode ('package mode does not unpack; output: ' + $result.Output)
                    Assert-True (-not [IO.File]::Exists($unpackRecord)) 'the unpack command was never invoked'
                }
                finally {
                    $env:PSModulePath = $previousModulePath
                    $env:TRACEFOLD_UNPACK_SENTINEL_RECORD = $previousRecord
                    Remove-UiTestTree -Path $base
                }
            }
        }
    )
}
