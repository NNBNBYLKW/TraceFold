# TraceFold Stage 5C-C - offline acceptance toolkit structure smoke.
#
# What this verifies: the package that was produced is the package that will be consumed. The
# manifest, every member row, the complete support and driver payloads, the contract fixtures,
# the product/toolkit boundary and the version stamp are checked from the bytes on disk, and the
# toolkit's own entry is asked to verify itself with -ValidateOnly, which starts nothing.
#
# What it does NOT do by default: start the product, the support CLI or the pinned driver. The
# optional -RunStartupLayout delegates to the packaged entry and therefore starts the real
# product; it is an explicit operator choice and never the default.
#
# Usage:
#   pwsh -NoProfile -File scripts/smoke-stage5cc-ui-acceptance-toolkit.ps1 `
#       -ToolkitDirectory dist/TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64 `
#       -ProductDirectory dist/TraceFold-MediaDesktop-Stage5C-C-win-x64

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ToolkitDirectory,
    [Parameter(Mandatory)][string] $ProductDirectory,
    [string] $ExpectedVersion,
    [switch] $RunStartupLayout
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolkitRoot = [IO.Path]::GetFullPath($ToolkitDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$productRoot = [IO.Path]::GetFullPath($ProductDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$manifestPath = Join-Path $toolkitRoot 'toolkit.json'
$failures = New-Object System.Collections.ArrayList
$passes = New-Object System.Collections.ArrayList

function Add-Pass {
    param([Parameter(Mandatory)][string] $Message)
    [void] $passes.Add($Message)
}

function Assert-Smoke {
    param(
        [Parameter(Mandatory)][bool] $Condition,
        [Parameter(Mandatory)][string] $Message
    )
    if (-not $Condition) { throw $Message }
}

function Test-ReparsePoint {
    param([Parameter(Mandatory)][string] $Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { return $false }
    return (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Read-JsonDocument {
    # Windows PowerShell decodes a BOM-less UTF-8 file as ANSI, which corrupts every non-ASCII
    # string before ConvertFrom-Json sees it. ReadAllText detects the BOM and defaults to UTF-8.
    param([Parameter(Mandatory)][string] $Path)
    return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json)
}

function Assert-PathChain {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Path
    )
    $rootFull = [IO.Path]::GetFullPath($Root)
    if ($rootFull.Length -gt 3) { $rootFull = $rootFull.TrimEnd([IO.Path]::DirectorySeparatorChar) }
    $full = [IO.Path]::GetFullPath($Path)
    $boundary = $rootFull
    if (-not $boundary.EndsWith([string] [IO.Path]::DirectorySeparatorChar)) { $boundary += [IO.Path]::DirectorySeparatorChar }
    if (-not $full.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($full, $rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('path escapes the package: ' + $full)
    }
    $relative = $full.Substring($rootFull.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
    $current = $rootFull
    if (Test-ReparsePoint -Path $current) { throw ('package root is a reparse point: ' + $current) }
    foreach ($segment in @($relative -split '[\\/]')) {
        if ([string]::IsNullOrEmpty($segment)) { continue }
        $current = Join-Path $current $segment
        if (-not [IO.File]::Exists($current) -and -not [IO.Directory]::Exists($current)) { break }
        if (Test-ReparsePoint -Path $current) { throw ('package path is a reparse point: ' + $current) }
    }
}

function Resolve-PackageRelativePath {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Relative,
        [Parameter(Mandatory)][string] $Label
    )
    $text = [string] $Relative
    if ([string]::IsNullOrWhiteSpace($text)) { throw ('manifest path is empty: ' + $Label) }
    if ([IO.Path]::IsPathRooted($text)) { throw ('manifest path is rooted: ' + $Label) }
    foreach ($segment in @($text -split '[\\/]')) {
        if ([string]::IsNullOrEmpty($segment) -or $segment -ceq '.') { throw ('manifest path is not canonical: ' + $Label) }
        if ($segment -ceq '..') { throw ('manifest path traverses: ' + $Label) }
    }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull ($text -replace '/', '\')))
    if (-not $full.StartsWith(($rootFull + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw ('manifest path escapes the package: ' + $Label)
    }
    return $full
}

function Add-TreeFiles {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Directory,
        [Parameter(Mandatory)] $Sink
    )
    if (-not [IO.Directory]::Exists($Directory)) {
        throw ('required package directory is missing: ' + $Directory)
    }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push([IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar))
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        foreach ($entry in @([IO.Directory]::GetFileSystemEntries($current))) {
            if (Test-ReparsePoint -Path $entry) { throw ('package tree contains a reparse point: ' + $entry) }
            if ([IO.Directory]::Exists($entry)) { $pending.Push($entry); continue }
            [void] $Sink.Add($entry)
        }
    }
}

$hostExe = if ($PSVersionTable.PSEdition -eq 'Core') {
    Join-Path $PSHOME 'pwsh.exe'
}
else {
    Join-Path $PSHOME 'powershell.exe'
}

try {
    Assert-Smoke -Condition ([IO.Directory]::Exists($toolkitRoot)) -Message ('toolkit directory is missing: ' + $toolkitRoot)
    Assert-Smoke -Condition ([IO.Directory]::Exists($productRoot)) -Message ('product directory is missing: ' + $productRoot)
    Assert-PathChain -Root ([IO.Path]::GetPathRoot($toolkitRoot)) -Path $toolkitRoot
    Assert-PathChain -Root ([IO.Path]::GetPathRoot($productRoot)) -Path $productRoot
    Assert-Smoke -Condition ([IO.File]::Exists($manifestPath)) -Message 'toolkit.json is missing'

    $manifest = Read-JsonDocument -Path $manifestPath
    Assert-Smoke -Condition ([string] $manifest.schema -ceq 'tracefold-ui-acceptance-toolkit') -Message ('unexpected toolkit schema: ' + [string] $manifest.schema)
    Assert-Smoke -Condition ([int] $manifest.schemaVersion -eq 1) -Message ('unexpected toolkit schema version: ' + [string] $manifest.schemaVersion)
    foreach ($key in @('layout', 'driver', 'support', 'members')) {
        Assert-Smoke -Condition ($null -ne $manifest.PSObject.Properties[$key]) -Message ('toolkit.json is missing ' + $key)
    }
    foreach ($key in @('supportRoot', 'driverRoot', 'contractPath', 'resultsRoot')) {
        Assert-Smoke -Condition (-not [string]::IsNullOrWhiteSpace([string] $manifest.layout.$key)) -Message ('toolkit layout is missing ' + $key)
    }
    Assert-Smoke -Condition ([string] $manifest.support.executable -ceq 'TraceFold.Media.UiAcceptance.Support.exe') -Message 'the support executable name is not the consumed one'
    Assert-Smoke -Condition ([string] $manifest.driver.memberName -ceq 'winapp.exe') -Message 'the driver member name is not the consumed one'

    $supportRoot = Resolve-PackageRelativePath -Root $toolkitRoot -Relative ([string] $manifest.layout.supportRoot) -Label 'layout.supportRoot'
    $driverRoot = Resolve-PackageRelativePath -Root $toolkitRoot -Relative ([string] $manifest.layout.driverRoot) -Label 'layout.driverRoot'
    $contractPath = Resolve-PackageRelativePath -Root $toolkitRoot -Relative ([string] $manifest.layout.contractPath) -Label 'layout.contractPath'
    $resultsRoot = Resolve-PackageRelativePath -Root $toolkitRoot -Relative ([string] $manifest.layout.resultsRoot) -Label 'layout.resultsRoot'
    Assert-Smoke -Condition ([IO.Directory]::Exists($supportRoot)) -Message 'the support root is missing'
    Assert-Smoke -Condition ([IO.Directory]::Exists($driverRoot)) -Message 'the driver root is missing'
    Assert-Smoke -Condition ([IO.File]::Exists($contractPath)) -Message 'the contract is missing'
    Assert-Smoke -Condition (-not [IO.Directory]::Exists($resultsRoot)) -Message 'the package ships a results directory; a fresh toolkit has none'

    # --- member table -------------------------------------------------------------------
    $declared = @($manifest.members)
    Assert-Smoke -Condition ($declared.Count -gt 0) -Message 'the member table is empty'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $rows = @{}
    foreach ($member in $declared) {
        $relative = [string] $member.path
        $source = [string] $member.source
        Assert-Smoke -Condition (-not [string]::IsNullOrWhiteSpace($source)) -Message ('member row has no source: ' + $relative)
        $full = Resolve-PackageRelativePath -Root $toolkitRoot -Relative $relative -Label ('members[]=' + $relative)
        $key = $relative -replace '\\', '/'
        Assert-Smoke -Condition ($seen.Add($key)) -Message ('duplicate member row: ' + $key)
        Assert-Smoke -Condition ([IO.File]::Exists($full)) -Message ('member file is missing: ' + $key)
        Assert-PathChain -Root $toolkitRoot -Path $full
        Assert-Smoke -Condition ((Get-Item -LiteralPath $full).Length -eq [long] $member.bytes) -Message ('member size mismatch: ' + $key)
        Assert-Smoke -Condition ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ceq ([string] $member.sha256).ToUpperInvariant()) -Message ('member hash mismatch: ' + $key)
        $rows[$key] = $member
    }
    Add-Pass ('member table verified: ' + $declared.Count + ' rows')

    # --- coverage of what the entry consumes ---------------------------------------------
    $uiRoot = Join-Path $toolkitRoot 'scripts/ui-acceptance'
    $required = New-Object 'System.Collections.Generic.List[string]'
    $required.Add('scripts/run-stage5cc-ui-acceptance.ps1')
    $required.Add('Run-Stage5CC-Acceptance.cmd')
    $required.Add('scripts/Run-Stage5CC-Acceptance.ps1')
    foreach ($moduleName in @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner', 'Workflows', 'Metadata', 'VisualGate')) {
        $required.Add('scripts/ui-acceptance/TraceFold.UiAcceptance.' + $moduleName + '.psm1')
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $uiRoot -File -Filter '*.psm1' -ErrorAction SilentlyContinue)) {
        $required.Add(('scripts/ui-acceptance/' + $file.Name))
    }
    $required.Add('scripts/ui-acceptance/report-template/index.html')
    $required.Add('apps/media-desktop/tools/ffmpeg/win-x64/manifest.json')
    foreach ($relative in $required) {
        Assert-Smoke -Condition ($rows.ContainsKey($relative)) -Message ('consumed file has no member row: ' + $relative)
        Assert-Smoke -Condition ([IO.File]::Exists((Resolve-PackageRelativePath -Root $toolkitRoot -Relative $relative -Label 'required'))) -Message ('consumed file is missing: ' + $relative)
    }
    Assert-Smoke -Condition (-not $rows.ContainsKey('scripts/Run-Stage5CC-Acceptance.cmd')) -Message 'the launcher is under scripts; the package root must carry it'

    $covered = New-Object 'System.Collections.Generic.List[string]'
    Add-TreeFiles -Root $toolkitRoot -Directory (Join-Path $uiRoot 'report-template') -Sink $covered
    Add-TreeFiles -Root $toolkitRoot -Directory $supportRoot -Sink $covered
    Add-TreeFiles -Root $toolkitRoot -Directory $driverRoot -Sink $covered
    $rootFull = [IO.Path]::GetFullPath($toolkitRoot)
    foreach ($file in $covered) {
        $relative = ([IO.Path]::GetFullPath($file).Substring($rootFull.Length).TrimStart([IO.Path]::DirectorySeparatorChar)) -replace '\\', '/'
        Assert-Smoke -Condition ($rows.ContainsKey($relative)) -Message ('a packaged file has no member row: ' + $relative)
    }
    Add-Pass ('consumed trees are fully covered: ' + $covered.Count + ' files')

    # --- support and driver identity ------------------------------------------------------
    $supportExe = Join-Path $supportRoot 'TraceFold.Media.UiAcceptance.Support.exe'
    Assert-Smoke -Condition ([IO.File]::Exists($supportExe)) -Message 'the support executable is missing'

    $driverExe = Join-Path $driverRoot 'winapp.exe'
    Assert-Smoke -Condition ([IO.File]::Exists($driverExe)) -Message 'the packaged driver executable is missing'
    $driverVersion = [string] ((Get-Item -LiteralPath $driverExe).VersionInfo.FileVersion)
    if ([string]::IsNullOrWhiteSpace($driverVersion)) {
        # The Win32 version API stops at MAX_PATH. An unreadable value is a path problem, not a
        # wrong driver, and saying so is more useful than reporting an empty version.
        throw ('the packaged driver version is unreadable at this path length (' + $driverExe.Length + ' characters); move the package to a shorter path')
    }
    Assert-Smoke -Condition ($driverVersion -like '0.5.0*') -Message ('the packaged driver is not v0.5.0: ' + $driverVersion)
    if ($null -ne $manifest.driver.PSObject.Properties['archive']) {
        $archive = $manifest.driver.archive
        foreach ($key in @('name', 'bytes', 'sha256')) {
            Assert-Smoke -Condition ($null -ne $archive.PSObject.Properties[$key]) -Message ('the driver archive identity is missing ' + $key)
        }
        Assert-Smoke -Condition ([string] $archive.name -ceq 'winappcli-x64.zip') -Message 'the driver archive name is not the pinned one'
        Assert-Smoke -Condition ([string] $archive.sha256 -match '^[0-9A-F]{64}$') -Message 'the driver archive digest is malformed'
        Assert-Smoke -Condition ([long] $archive.bytes -gt 0) -Message 'the driver archive size is not recorded'
        # The archive digest is its own fact: it must never be the executable digest standing in
        # for the archive, which is what a copied value would look like.
        Assert-Smoke -Condition ([string] $archive.sha256 -cne ([string] $manifest.driver.sha256)) -Message 'the archive digest was copied from the executable digest'
    }
    else {
        throw 'the toolkit does not record the driver archive identity'
    }
    foreach ($excluded in @($manifest.driver.excludedMembers)) {
        Assert-Smoke -Condition ([string] $excluded -like '*.pdb') -Message ('an unexpected driver member was dropped: ' + [string] $excluded)
    }
    if ($null -ne $manifest.driver.PSObject.Properties['packageManifest']) {
        $packageManifestPath = Resolve-PackageRelativePath -Root $toolkitRoot -Relative ([string] $manifest.driver.packageManifest) -Label 'driver.packageManifest'
        Assert-Smoke -Condition ([IO.File]::Exists($packageManifestPath)) -Message 'the pinned driver manifest is missing from the package'
        $packageManifest = Read-JsonDocument -Path $packageManifestPath
        Assert-Smoke -Condition ([string] $packageManifest.version -ceq '0.5.0') -Message 'the packaged driver manifest is not v0.5.0'
        Assert-Smoke -Condition ([long] $packageManifest.archiveSize -eq [long] $manifest.driver.archive.bytes) -Message 'the packaged driver manifest disagrees about the archive size'
        Assert-Smoke -Condition ([string] $packageManifest.sha256 -ceq [string] $manifest.driver.archive.sha256) -Message 'the packaged driver manifest disagrees about the archive digest'
    }
    Add-Pass ('driver identity verified: v' + $driverVersion)

    # --- version stamp --------------------------------------------------------------------
    $version = [string] $manifest.informationalVersion
    Assert-Smoke -Condition ($version -match '^4\.0\.0-stage5cc\+[0-9a-f]{40}$') -Message ('unexpected toolkit version: ' + $version)
    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        Assert-Smoke -Condition ($version -ceq $ExpectedVersion) -Message ('toolkit version does not match the expected one: ' + $version)
    }
    $sourceCommit = [string] $manifest.sourceCommit
    Assert-Smoke -Condition ($sourceCommit -match '^[0-9a-f]{40}$') -Message 'the toolkit does not record a source commit'
    Assert-Smoke -Condition ($version.EndsWith($sourceCommit, [StringComparison]::Ordinal)) -Message 'the toolkit version and source commit disagree'
    Add-Pass ('version verified: ' + $version)

    # --- fixtures -------------------------------------------------------------------------
    $contract = Read-JsonDocument -Path $contractPath
    $fixtures = @($contract.fixtureAssets)
    foreach ($asset in $fixtures) {
        $relative = ([string] $asset.repositoryPath) -replace '\\', '/'
        $full = Resolve-PackageRelativePath -Root $toolkitRoot -Relative $relative -Label 'fixtureAssets'
        Assert-Smoke -Condition ([IO.File]::Exists($full)) -Message ('declared fixture is missing: ' + $relative)
        Assert-Smoke -Condition ((Get-Item -LiteralPath $full).Length -eq [long] $asset.sizeBytes) -Message ('fixture size mismatch: ' + $relative)
        Assert-Smoke -Condition ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ceq ([string] $asset.sha256).ToUpperInvariant()) -Message ('fixture hash mismatch: ' + $relative)
        Assert-Smoke -Condition ($rows.ContainsKey($relative)) -Message ('fixture has no member row: ' + $relative)
    }
    Add-Pass ('contract fixtures verified: ' + $fixtures.Count)

    # --- media tools declaration ----------------------------------------------------------
    #
    # MainChain reads this declaration from the toolkit root, and the entry refuses to drive a
    # package whose declaration has no verified member row. The pins it carries must also be the
    # binaries the product actually ships, so the two packages cannot disagree about FFmpeg.
    $mediaManifestRelative = 'apps/media-desktop/tools/ffmpeg/win-x64/manifest.json'
    $mediaManifestPath = Resolve-PackageRelativePath -Root $toolkitRoot -Relative $mediaManifestRelative -Label 'media-manifest'
    Assert-Smoke -Condition ([IO.File]::Exists($mediaManifestPath)) -Message 'the media tools declaration is missing from the package'
    Assert-Smoke -Condition ($rows.ContainsKey($mediaManifestRelative)) -Message 'the media tools declaration has no member row'
    $mediaManifest = Read-JsonDocument -Path $mediaManifestPath
    foreach ($toolName in @('ffmpeg.exe', 'ffprobe.exe')) {
        $pin = $mediaManifest.files.PSObject.Properties[$toolName]
        Assert-Smoke -Condition ($null -ne $pin) -Message ('the media tools declaration does not pin ' + $toolName)
        $productTool = Join-Path $productRoot ('tools/ffmpeg/' + $toolName)
        Assert-Smoke -Condition ([IO.File]::Exists($productTool)) -Message ('the product is missing ' + $toolName)
        Assert-Smoke -Condition ((Get-Item -LiteralPath $productTool).Length -eq [long] $pin.Value.size) -Message ('the product ' + $toolName + ' does not match the declared size')
        Assert-Smoke -Condition ((Get-FileHash -LiteralPath $productTool -Algorithm SHA256).Hash -ceq ([string] $pin.Value.sha256).ToUpperInvariant()) -Message ('the product ' + $toolName + ' does not match the declared digest')
    }
    Add-Pass 'media tools declaration verified against the product tools'

    # --- product / toolkit boundary -------------------------------------------------------
    $overlap = [string]::Equals($toolkitRoot, $productRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $toolkitRoot.StartsWith(($productRoot + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase) -or
        $productRoot.StartsWith(($toolkitRoot + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)
    Assert-Smoke -Condition (-not $overlap) -Message 'the product and the toolkit contain one another'
    Assert-Smoke -Condition ([IO.File]::Exists((Join-Path $productRoot 'TraceFold.Media.exe'))) -Message 'the product executable is missing'
    Assert-Smoke -Condition (-not [IO.File]::Exists((Join-Path $toolkitRoot 'TraceFold.Media.exe'))) -Message 'the toolkit contains a product executable'

    $forbiddenNames = @(
        'winapp.exe',
        'TraceFold.Media.UiAcceptance.Support.exe',
        'TraceFold.Media.UiAcceptance.Support.dll',
        'TraceFold.Media.UiAcceptance.Support.deps.json',
        'TraceFold.Media.UiAcceptance.Support.runtimeconfig.json',
        'toolkit.json',
        'stage5cc-uia-contract.json',
        'Run-Stage5CC-Acceptance.cmd',
        'run-stage5cc-ui-acceptance.ps1',
        'index.html',
        'media-desktop-stage5cc-visual-uia-test-guide.md',
        'media-desktop-stage5cc-visual-uia-acceptance.md',
        '70-visual-keyboard-matrix.ps1')
    $forbiddenDirectories = @('ui-acceptance', 'report-template', 'fixtures', '.superpowers', 'node_modules', '.git', 'artifacts', '.cache', '.worktrees')
    foreach ($file in @(Get-ChildItem -LiteralPath $productRoot -Recurse -File -Force -ErrorAction Stop)) {
        Assert-Smoke -Condition ($forbiddenNames -notcontains $file.Name) -Message ('the product carries acceptance payload: ' + $file.FullName)
        Assert-Smoke -Condition ($file.Name -notlike 'TraceFold.Media.UiAcceptance.Support.*') -Message ('the product carries a support dependency: ' + $file.FullName)
        Assert-Smoke -Condition ($file.Name -notlike 'TraceFold.UiAcceptance.*.psm1') -Message ('the product carries an acceptance module: ' + $file.FullName)
        Assert-Smoke -Condition ($file.Extension -ne '.ps1') -Message ('the product carries an acceptance script: ' + $file.FullName)
        Assert-Smoke -Condition ($file.Extension -notin @('.pdb')) -Message ('the product carries debug symbols: ' + $file.Name)
    }
    foreach ($directory in @(Get-ChildItem -LiteralPath $productRoot -Recurse -Directory -Force -ErrorAction Stop)) {
        Assert-Smoke -Condition ($forbiddenDirectories -notcontains $directory.Name.ToLowerInvariant()) -Message ('the product carries an acceptance directory: ' + $directory.Name)
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $toolkitRoot -Recurse -File -ErrorAction Stop)) {
        Assert-Smoke -Condition ($file.Extension -notin @('.pdb', '.zip')) -Message ('the toolkit carries a debug or archive file: ' + $file.FullName)
    }
    Add-Pass 'product and toolkit boundary verified'

    # --- the packaged entry verifies itself -----------------------------------------------
    $entry = Join-Path $toolkitRoot 'scripts/run-stage5cc-ui-acceptance.ps1'
    Assert-Smoke -Condition ([IO.File]::Exists($entry)) -Message 'the packaged entry is missing'
    $previousErrorAction = $ErrorActionPreference
    try {
        # Windows PowerShell turns a child's stderr into a terminating error while the preference
        # is Stop; the refusal text is evidence, so the call runs with Continue and the real exit
        # code is read immediately after.
        $ErrorActionPreference = 'Continue'
        $validationOutput = (& $hostExe -NoProfile -File $entry -ToolkitDirectory $toolkitRoot -ProductDirectory $productRoot -ValidateOnly 2>&1 | Out-String)
        $validationExit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    Assert-Smoke -Condition ($validationExit -eq 0) -Message ('the packaged entry refused its own package (exit ' + $validationExit + '): ' + $validationOutput.Trim())
    $validation = ($validationOutput -split "`n" | Where-Object { $_.Trim().StartsWith('{') } | Select-Object -Last 1)
    Assert-Smoke -Condition (-not [string]::IsNullOrWhiteSpace($validation)) -Message 'the packaged entry did not report a validation summary'
    $summary = $validation | ConvertFrom-Json
    Assert-Smoke -Condition ([string] $summary.schema -ceq 'tracefold-toolkit-validation') -Message 'the packaged entry reported an unexpected validation schema'
    Assert-Smoke -Condition ([int] $summary.memberCount -eq $declared.Count) -Message 'the packaged entry and this smoke disagree about the member count'
    Assert-Smoke -Condition ([int] $summary.fixtureCount -eq $fixtures.Count) -Message 'the packaged entry and this smoke disagree about the fixture count'
    Add-Pass ('packaged entry accepted the manifest: ' + [int] $summary.memberCount + ' members, ' + [int] $summary.fixtureCount + ' fixtures')

    if ($RunStartupLayout) {
        $outputDirectory = Join-Path $resultsRoot ('run-' + [guid]::NewGuid().ToString('N'))
        $previousErrorAction = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $runOutput = (& $hostExe -NoProfile -File $entry -ToolkitDirectory $toolkitRoot -ProductDirectory $productRoot -OutputDirectory $outputDirectory -Scenario StartupLayout 2>&1 | Out-String)
            $runExit = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorAction
        }
        Assert-Smoke -Condition ($runExit -eq 0) -Message ('the StartupLayout scenario failed (exit ' + $runExit + '): ' + $runOutput.Trim())
        Add-Pass 'StartupLayout scenario passed from the packaged toolkit'
    }
    else {
        Add-Pass 'real product launch probes: NotRun (this smoke starts nothing by default)'
    }
}
catch {
    [Console]::Error.WriteLine('FAIL: ' + $_.Exception.Message)
    exit 1
}

foreach ($message in $passes) { Write-Output ('PASS: ' + $message) }
Write-Output 'smoke-stage5cc-ui-acceptance-toolkit: structure verified without starting the product'
exit 0
