# TraceFold Stage 5CC - real window UI acceptance entry point.
#
# Proves the pinned winapp driver (v0.5.0) against a published TraceFold.Media.exe: every
# scenario gets its own run id, token, owned root, output directory and support/driver copy,
# and a scenario failure is contained so the remaining scenarios still run.
#
# Nothing is installed globally: the driver archive is verified against the repository
# manifest, extracted into this run's own directory, and the support CLI is published into the
# same place. A missing or mismatched archive is reported as Blocked, never as a product
# failure, and never triggers a download.
#
# Two invocation shapes:
#
#   source tree    the entry sits in a checkout. The pinned driver archive is verified against the
#                  repository manifest and unpacked into this run's directory, and the support CLI
#                  is published from its project, so the SDK is required.
#
#   moved toolkit  the package was unpacked somewhere else and carries its own support payload,
#                  unpacked driver, modules, templates, contract and declared fixtures. Nothing is
#                  compiled, downloaded or unpacked: toolkit.json is the package data contract and
#                  every member is verified before the product, driver or support is started.
#
# Usage:
#   pwsh -NoProfile -File scripts/run-stage5cc-ui-acceptance.ps1 `
#       -ProductDirectory .superpowers/stage5cc-product `
#       -WinAppCliArchivePath D:\downloads\winappcli-x64.zip `
#       -OutputDirectory .superpowers/stage5cc-uia-gate `
#       -Scenario StartupLayout
#
#   pwsh -NoProfile -File <toolkit>/scripts/run-stage5cc-ui-acceptance.ps1 `
#       -ToolkitDirectory <toolkit> -ProductDirectory <product> -Scenario StartupLayout

[CmdletBinding()]
param(
    # The first four parameters keep their original positions: an old call that binds them
    # positionally (-File entry <product> <archive> <out> StartupLayout) must keep working, so the
    # package switch is appended after them instead of inserted into the sequence.
    [Parameter(Mandatory)][string] $ProductDirectory,
    [string] $WinAppCliArchivePath,
    [string] $OutputDirectory,
    [ValidateSet('All','StartupLayout','MainChain','Conflict','ConflictKeepBoth','ConflictReplace','ConflictSkip','ProcessingFailureRetry','BatchMetadataUndo','LibraryFilterNaming','Library','Taxonomy','Diagnostics','SingleInstance','Visual')]
    [string[]] $Scenario = @('All'),
    [string] $ToolkitDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitInvalidArguments = 2
$ExitBlocked = 1

$sourceMode = -not [string]::IsNullOrWhiteSpace($WinAppCliArchivePath)
$packageMode = -not [string]::IsNullOrWhiteSpace($ToolkitDirectory)
if ($sourceMode -eq $packageMode) {
    [Console]::Error.WriteLine('Specify exactly one of -WinAppCliArchivePath (source tree) or -ToolkitDirectory (moved package).')
    exit $ExitInvalidArguments
}

<#
.SYNOPSIS
    Resolves one package-relative member path and refuses anything that leaves the package.

.DESCRIPTION
    The package manifest is data, so every path in it is treated as untrusted input: a rooted
    path, a traversal segment, an empty or dot segment and a control character are refused
    before anything is opened, and the resolved path must still sit under the package root.
#>
function Resolve-Stage5CCPackageRelativePath {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Relative,
        [Parameter(Mandatory)][string] $Label
    )

    $text = [string] $Relative
    if ([string]::IsNullOrWhiteSpace($text)) { throw ('toolkit.path_empty: ' + $Label) }
    if ([IO.Path]::IsPathRooted($text)) { throw ('toolkit.path_rooted: ' + $Label + '=' + $text) }
    $segments = @($text -split '[\\/]')
    foreach ($segment in $segments) {
        if ([string]::IsNullOrEmpty($segment) -or $segment -ceq '.') { throw ('toolkit.path_not_canonical: ' + $Label + '=' + $text) }
        if ($segment -ceq '..') { throw ('toolkit.path_traversal: ' + $Label + '=' + $text) }
        foreach ($character in $segment.ToCharArray()) {
            if ([int] [char] $character -lt 32) { throw ('toolkit.path_control_character: ' + $Label + '=' + $text) }
        }
    }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull ($segments -join [IO.Path]::DirectorySeparatorChar)))
    if (-not $full.StartsWith(($rootFull + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw ('toolkit.path_outside_root: ' + $Label + '=' + $text)
    }
    return $full
}

<#
.SYNOPSIS
    Walks every component of one member path and refuses a reparse point.
#>
function Assert-Stage5CCPackagePathChain {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Path
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $relative = [IO.Path]::GetFullPath($Path).Substring($rootFull.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
    $current = $rootFull
    foreach ($segment in @($relative -split '[\\/]')) {
        if ([string]::IsNullOrEmpty($segment)) { continue }
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw ('toolkit.path_reparse: ' + ($relative -replace '\\', '/'))
        }
    }
}

<#
.SYNOPSIS
    Refuses a product directory and a toolkit that contain one another.
#>
function Assert-Stage5CCPathsDisjoint {
    param(
        [Parameter(Mandatory)][string] $Product,
        [Parameter(Mandatory)][string] $Toolkit
    )

    $productFull = [IO.Path]::GetFullPath($Product).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $toolkitFull = [IO.Path]::GetFullPath($Toolkit).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $productBoundary = $productFull + [IO.Path]::DirectorySeparatorChar
    $toolkitBoundary = $toolkitFull + [IO.Path]::DirectorySeparatorChar
    $same = [string]::Equals($productFull, $toolkitFull, [StringComparison]::OrdinalIgnoreCase)
    if ($same -or
        $productFull.StartsWith($toolkitBoundary, [StringComparison]::OrdinalIgnoreCase) -or
        $toolkitFull.StartsWith($productBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'toolkit.product_overlap: the product directory and the toolkit must not contain one another.'
    }
}

<#
.SYNOPSIS
    Requires one consumed file to have a verified member row.

.DESCRIPTION
    The member table is only a contract if it covers what the entry actually loads, copies or
    passes on. A file that exists in the package but has no row would never be hashed, so this
    refuses it instead of letting the unverified file be imported or copied.
#>
function Assert-Stage5CCToolkitFileCovered {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)] $Members,
        [Parameter(Mandatory)][string] $Label
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $relative = [IO.Path]::GetFullPath($Path).Substring($rootFull.Length).TrimStart([IO.Path]::DirectorySeparatorChar) -replace '\\', '/'
    if (-not $Members.Contains($relative)) {
        throw ('toolkit.member_unlisted: ' + $Label + '=' + $relative)
    }
}

<#
.SYNOPSIS
    Requires every file in one consumed directory to have a verified member row.

.DESCRIPTION
    Support is copied recursively and the templates are read as a tree, so a file that is present
    but absent from the table would reach a scenario without ever being hashed. The walk refuses a
    reparse point on sight and reports the first file that carries no row.
#>
function Assert-Stage5CCToolkitDirectoryCovered {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Directory,
        [Parameter(Mandatory)] $Members,
        [Parameter(Mandatory)][string] $Label
    )

    if (-not [IO.Directory]::Exists($Directory)) { throw ('toolkit.tree_missing: ' + $Label) }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push([IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar))
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        foreach ($entry in @([IO.Directory]::GetFileSystemEntries($current))) {
            $item = Get-Item -LiteralPath $entry -Force
            $relative = $entry.Substring($rootFull.Length).TrimStart([IO.Path]::DirectorySeparatorChar) -replace '\\', '/'
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('toolkit.path_reparse: ' + $relative) }
            if ([IO.Directory]::Exists($entry)) { $pending.Push($entry); continue }
            if (-not $Members.Contains($relative)) { throw ('toolkit.member_unlisted: ' + $Label + '=' + $relative) }
        }
    }
}

<#
.SYNOPSIS
    Reads and verifies the package data contract before anything is started.

.DESCRIPTION
    The manifest names the layout, the support and driver members that will actually run, and every
    file this entry consumes. Each member is re-hashed here; the contract's declared fixture assets
    are re-hashed too. A missing, duplicated, escaping, reparse-pointed or changed member stops the
    invocation with an invalid-argument answer, before any support, driver or product is started.
#>
function Read-Stage5CCToolkitPackage {
    param([Parameter(Mandatory)][string] $Root)

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not [IO.Directory]::Exists($rootFull)) { throw ('toolkit.root_missing: ' + $Root) }
    if (((Get-Item -LiteralPath $rootFull -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw ('toolkit.root_reparse: ' + $Root)
    }
    $manifestPath = Join-Path $rootFull 'toolkit.json'
    if (-not [IO.File]::Exists($manifestPath)) { throw 'toolkit.manifest_missing: toolkit.json' }
    Assert-Stage5CCPackagePathChain -Root $rootFull -Path $manifestPath

    $document = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string] $document.schema -cne 'tracefold-ui-acceptance-toolkit') { throw 'toolkit.schema_mismatch: expected tracefold-ui-acceptance-toolkit' }
    if ([int] $document.schemaVersion -ne 1) { throw ('toolkit.schema_version_unsupported: ' + [string] $document.schemaVersion) }
    foreach ($key in @('layout', 'driver', 'support', 'members')) {
        if ($null -eq $document.PSObject.Properties[$key]) { throw ('toolkit.manifest_key_missing: ' + $key) }
    }
    $layout = $document.layout
    foreach ($key in @('supportRoot', 'driverRoot', 'contractPath', 'resultsRoot')) {
        if ($null -eq $layout.PSObject.Properties[$key] -or [string]::IsNullOrWhiteSpace([string] $layout.$key)) {
            throw ('toolkit.layout_key_missing: ' + $key)
        }
    }
    $supportRoot = Resolve-Stage5CCPackageRelativePath -Root $rootFull -Relative ([string] $layout.supportRoot) -Label 'layout.supportRoot'
    $driverRoot = Resolve-Stage5CCPackageRelativePath -Root $rootFull -Relative ([string] $layout.driverRoot) -Label 'layout.driverRoot'
    $contractPath = Resolve-Stage5CCPackageRelativePath -Root $rootFull -Relative ([string] $layout.contractPath) -Label 'layout.contractPath'
    $resultsRoot = Resolve-Stage5CCPackageRelativePath -Root $rootFull -Relative ([string] $layout.resultsRoot) -Label 'layout.resultsRoot'
    if (-not [IO.Directory]::Exists($supportRoot)) { throw ('toolkit.support_root_missing: ' + [string] $layout.supportRoot) }
    if (-not [IO.Directory]::Exists($driverRoot)) { throw ('toolkit.driver_root_missing: ' + [string] $layout.driverRoot) }
    if (-not [IO.File]::Exists($contractPath)) { throw ('toolkit.contract_missing: ' + [string] $layout.contractPath) }

    if ($null -eq $document.support.PSObject.Properties['executable']) { throw 'toolkit.support_executable_missing' }
    $supportName = [string] $document.support.executable
    if ([string]::IsNullOrWhiteSpace($supportName) -or $supportName -match '[\\/]') { throw 'toolkit.support_executable_invalid' }
    # The entry, the Runner and the report all name this executable. A package that declares some
    # other name would be verified under one name and consumed under another, so the other name is
    # refused here, before anything starts, instead of being silently ignored later.
    if ($supportName -cne 'TraceFold.Media.UiAcceptance.Support.exe') {
        throw ('toolkit.support_executable_unsupported: this entry consumes TraceFold.Media.UiAcceptance.Support.exe, not ' + $supportName)
    }
    $supportExecutable = Join-Path $supportRoot $supportName
    $driverName = 'winapp.exe'
    if ($null -ne $document.driver.PSObject.Properties['memberName'] -and -not [string]::IsNullOrWhiteSpace([string] $document.driver.memberName)) {
        $driverName = [string] $document.driver.memberName
    }
    if ($driverName -match '[\\/]') { throw 'toolkit.driver_member_invalid' }
    # The run root, the scenario argument and the identity measurement all say winapp.exe.
    if ($driverName -cne 'winapp.exe') {
        throw ('toolkit.driver_member_unsupported: this entry consumes winapp.exe, not ' + $driverName)
    }
    $driverExecutable = Join-Path $driverRoot $driverName

    $declaredRows = @($document.members)
    if ($declaredRows.Count -eq 0) { throw 'toolkit.members_empty' }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $rows = New-Object 'System.Collections.Generic.List[object]'
    foreach ($member in $declaredRows) {
        if ($null -eq $member) { throw 'toolkit.member_invalid: null entry' }
        $relative = [string] $member.path
        $source = [string] $member.source
        if ([string]::IsNullOrWhiteSpace($source)) { throw ('toolkit.member_source_missing: ' + $relative) }
        $full = Resolve-Stage5CCPackageRelativePath -Root $rootFull -Relative $relative -Label 'members[].path'
        $key = $relative -replace '\\', '/'
        if (-not $seen.Add($key)) { throw ('toolkit.member_duplicate: ' + $key) }
        if (-not [IO.File]::Exists($full)) { throw ('toolkit.member_missing: ' + $key) }
        Assert-Stage5CCPackagePathChain -Root $rootFull -Path $full
        if ($null -eq $member.PSObject.Properties['bytes']) { throw ('toolkit.member_size_invalid: ' + $key) }
        $declaredBytes = [long] $member.bytes
        $declaredSha = ([string] $member.sha256).ToUpperInvariant()
        if ($declaredSha -notmatch '^[0-9A-F]{64}$') { throw ('toolkit.member_hash_invalid: ' + $key) }
        $actualBytes = [long] ((Get-Item -LiteralPath $full).Length)
        if ($actualBytes -ne $declaredBytes) { throw ('toolkit.member_size_mismatch: ' + $key) }
        $actualSha = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
        if ($actualSha -cne $declaredSha) { throw ('toolkit.member_hash_mismatch: ' + $key) }
        [void] $rows.Add([pscustomobject] @{ Key = $key; Bytes = $actualBytes; Sha256 = $actualSha; Source = $source })
    }

    $requiredMembers = @(
        @{ Path = $supportExecutable; Label = 'support executable' },
        @{ Path = $driverExecutable; Label = 'driver executable' },
        @{ Path = $contractPath; Label = 'contract' }
    )
    $requiredRows = @{}
    foreach ($item in $requiredMembers) {
        $relative = [IO.Path]::GetFullPath($item.Path).Substring($rootFull.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
        $key = $relative -replace '\\', '/'
        if (-not $seen.Contains($key)) { throw ('toolkit.required_member_absent: ' + $item.Label + '=' + $key) }
        $requiredRows[$item.Label] = @($rows | Where-Object { $_.Key -ceq $key })[0]
    }
    $supportRow = $requiredRows['support executable']
    $driverRow = $requiredRows['driver executable']
    if ($null -ne $document.support.PSObject.Properties['sha256']) {
        $declaredSupportSha = ([string] $document.support.sha256).ToUpperInvariant()
        if ($declaredSupportSha -cne $supportRow.Sha256) { throw 'toolkit.support_declaration_mismatch: the support digest does not match its member' }
    }
    if ($null -ne $document.driver.PSObject.Properties['sha256']) {
        $declaredDriverSha = ([string] $document.driver.sha256).ToUpperInvariant()
        if ($declaredDriverSha -cne $driverRow.Sha256) { throw 'toolkit.driver_declaration_mismatch: the driver digest does not match its member' }
    }

    # What the entry actually consumes must be exactly what was verified: the entry file itself,
    # the module set it imports, the template tree the report writer reads, and the complete
    # support and driver payloads that are copied into the run root. A file that is present but
    # unlisted would otherwise be imported or copied without ever being hashed.
    $uiRootFull = Join-Path $rootFull 'scripts/ui-acceptance'
    $moduleNames = @('Contracts', 'Driver', 'Processes', 'Evidence', 'Runner', 'Workflows', 'Metadata')
    $memberIndex = @{}
    foreach ($row in $rows) { $memberIndex[[string] $row.Key] = $row }
    Assert-Stage5CCToolkitFileCovered -Root $rootFull -Path (Join-Path $rootFull 'scripts/run-stage5cc-ui-acceptance.ps1') -Members $seen -Label 'entry'
    foreach ($moduleName in $moduleNames) {
        $modulePath = Join-Path $uiRootFull ('TraceFold.UiAcceptance.' + $moduleName + '.psm1')
        if (-not [IO.File]::Exists($modulePath)) { throw ('toolkit.module_missing: ' + $moduleName) }
        Assert-Stage5CCToolkitFileCovered -Root $rootFull -Path $modulePath -Members $seen -Label 'module'
    }
    foreach ($presentModule in @(Get-ChildItem -LiteralPath $uiRootFull -File -Filter '*.psm1')) {
        Assert-Stage5CCToolkitFileCovered -Root $rootFull -Path $presentModule.FullName -Members $seen -Label 'module'
    }
    Assert-Stage5CCToolkitDirectoryCovered -Root $rootFull -Directory (Join-Path $uiRootFull 'report-template') -Members $seen -Label 'template'
    # The report writer reads this file by name whenever an index is produced. A tree guard alone
    # would accept an empty template directory, so the file is required and bound to its row.
    $indexTemplatePath = Join-Path $uiRootFull 'report-template/index.html'
    if (-not [IO.File]::Exists($indexTemplatePath)) { throw 'toolkit.template_missing: scripts/ui-acceptance/report-template/index.html' }
    Assert-Stage5CCToolkitFileCovered -Root $rootFull -Path $indexTemplatePath -Members $seen -Label 'template'
    Assert-Stage5CCToolkitDirectoryCovered -Root $rootFull -Directory $supportRoot -Members $seen -Label 'support'
    Assert-Stage5CCToolkitDirectoryCovered -Root $rootFull -Directory $driverRoot -Members $seen -Label 'driver'

    # The contract declares the fixture assets this toolkit owes. They are re-hashed here, from the
    # package itself, so a moved toolkit never reaches a scenario with a stale or missing fixture.
    $contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
    $fixtureAssets = @()
    if ($null -ne $contract.PSObject.Properties['fixtureAssets']) { $fixtureAssets = @($contract.fixtureAssets) }
    foreach ($asset in $fixtureAssets) {
        if ($null -eq $asset) { throw 'toolkit.fixture_invalid: null entry' }
        $relative = [string] $asset.repositoryPath
        $full = Resolve-Stage5CCPackageRelativePath -Root $rootFull -Relative $relative -Label 'fixtureAssets[].repositoryPath'
        $key = $relative -replace '\\', '/'
        if (-not [IO.File]::Exists($full)) { throw ('toolkit.fixture_missing: ' + $key) }
        Assert-Stage5CCPackagePathChain -Root $rootFull -Path $full
        if ($null -eq $asset.PSObject.Properties['sizeBytes'] -or $null -eq $asset.PSObject.Properties['sha256']) {
            throw ('toolkit.fixture_declaration_incomplete: ' + $key)
        }
        if ([long] ((Get-Item -LiteralPath $full).Length) -ne [long] $asset.sizeBytes) { throw ('toolkit.fixture_size_mismatch: ' + $key) }
        if ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -cne ([string] $asset.sha256).ToUpperInvariant()) { throw ('toolkit.fixture_hash_mismatch: ' + $key) }
    }

    return [pscustomobject] @{
        Root             = $rootFull
        ManifestPath     = $manifestPath
        SupportRoot      = $supportRoot
        SupportExecutable = $supportExecutable
        SupportSha256    = [string] $supportRow.Sha256
        DriverRoot       = $driverRoot
        DriverExecutable = $driverExecutable
        DriverMemberName = $driverName
        DriverSha256     = [string] $driverRow.Sha256
        ContractPath     = $contractPath
        ResultsRoot      = $resultsRoot
        MemberCount      = $rows.Count
        MemberRows       = $memberIndex
        ModuleNames      = $moduleNames
        FixtureCount     = $fixtureAssets.Count
    }
}

$script:toolkitPackage = $null
if ($packageMode) {
    try { $script:toolkitPackage = Read-Stage5CCToolkitPackage -Root $ToolkitDirectory }
    catch {
        [Console]::Error.WriteLine($_.Exception.Message)
        exit $ExitInvalidArguments
    }
    $entryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not [string]::Equals($entryRoot, [string] $script:toolkitPackage.Root, [StringComparison]::OrdinalIgnoreCase)) {
        [Console]::Error.WriteLine('toolkit.entry_root_mismatch: -ToolkitDirectory and the running entry file name different packages.')
        exit $ExitInvalidArguments
    }
    $script:repositoryRoot = [string] $script:toolkitPackage.Root
}
else {
    $script:repositoryRoot = Split-Path -Parent $PSScriptRoot
}
$script:uiRoot = Join-Path $script:repositoryRoot 'scripts/ui-acceptance'
Import-Module (Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Contracts.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Processes.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Runner.psm1') -Force -DisableNameChecking
# Metadata and provenance producers: one implementation per field family, consumed below.
Import-Module (Join-Path $script:uiRoot 'TraceFold.UiAcceptance.Metadata.psm1') -Force -DisableNameChecking

# Registry entries that load the aggregate engine themselves. Each one resolves the sibling copy
# under scenarios/ first and falls back to the parent directory, so the package check below reads
# the same two candidates in the same order.
$script:scenarioEngineModule = [ordered] @{
    'Conflict' = 'TraceFold.UiAcceptance.Aggregate.psm1'
    'Library'  = 'TraceFold.UiAcceptance.Aggregate.psm1'
}

<#
.SYNOPSIS
    Requires the module a request's scenario will import to be a verified package member.

.DESCRIPTION
    Two registry entries load the aggregate engine at dispatch time, choosing the sibling copy
    first and the parent copy second. A package that carries the engine without a row, or carries
    neither, would otherwise be discovered only after the support tool has already created the run
    root. The same candidate order is used here, and the file that would be imported is the file
    that is checked.
#>
function Assert-Stage5CCToolkitScenarioDependencies {
    param(
        [Parameter(Mandatory)] $Package,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Names
    )

    foreach ($name in $Names) {
        if (-not $script:scenarioEngineModule.Contains($name)) { continue }
        # An entry that is not there is the completeness gate's refusal; this check only binds the
        # dependency of a scenario that will really be loaded.
        $scenarioFile = Get-UiAcceptanceScenarioFile -Name $name
        if (-not [IO.File]::Exists((Join-Path ([string] $Package.Root) ('scripts/ui-acceptance/scenarios/' + $scenarioFile)))) { continue }
        $moduleName = [string] $script:scenarioEngineModule[$name]
        $candidates = @(
            ('scripts/ui-acceptance/scenarios/' + $moduleName),
            ('scripts/ui-acceptance/' + $moduleName)
        )
        $chosen = $null
        foreach ($relative in $candidates) {
            $candidatePath = Join-Path ([string] $Package.Root) ($relative -replace '/', '\')
            if ([IO.File]::Exists($candidatePath)) { $chosen = $relative; break }
        }
        if ($null -eq $chosen) { throw ('toolkit.scenario_dependency_missing: ' + $name + '=' + $moduleName) }
        $row = $Package.MemberRows[$chosen]
        if ($null -eq $row) { throw ('toolkit.member_unlisted: dependency=' + $chosen) }
        $chosenPath = Join-Path ([string] $Package.Root) ($chosen -replace '/', '\')
        if ([long] ((Get-Item -LiteralPath $chosenPath).Length) -ne [long] $row.Bytes) { throw ('toolkit.member_size_mismatch: ' + $chosen) }
        if ((Get-FileHash -LiteralPath $chosenPath -Algorithm SHA256).Hash -cne [string] $row.Sha256) { throw ('toolkit.member_hash_mismatch: ' + $chosen) }
    }
}

<#
.SYNOPSIS
    Refuses an existing path whose components include a reparse point.

.DESCRIPTION
    A junction inside an existing part of a path redirects everything created below it, so a fresh
    leaf name says nothing about where the run actually writes. The walk stops at the first
    component that does not exist yet: those are the ones this run would create itself.
#>
function Assert-Stage5CCExistingPathNotAliased {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Code
    )

    $full = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($full)
    if ([string]::IsNullOrEmpty($pathRoot)) { return }
    $current = $pathRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
    foreach ($segment in @($full.Substring($pathRoot.Length) -split '[\\/]')) {
        if ([string]::IsNullOrEmpty($segment)) { continue }
        $current = Join-Path $current $segment
        if (-not [IO.File]::Exists($current) -and -not [IO.Directory]::Exists($current)) { break }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ($Code + ': ' + $full) }
    }
}

<#
.SYNOPSIS
    Requires every scenario entry this request will load to be a verified package member.

.DESCRIPTION
    A scenario file is dispatched by path, so the file that runs must be the file the manifest
    hashed. The row is looked up in the same table the package validator built, and the bytes are
    read again here because this check runs after the request is resolved and immediately before
    the scenario could be dispatched.
#>
function Assert-Stage5CCToolkitScenarioMembers {
    param(
        [Parameter(Mandatory)] $Package,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Names
    )

    foreach ($name in $Names) {
        $file = Get-UiAcceptanceScenarioFile -Name $name
        $relative = 'scripts/ui-acceptance/scenarios/' + $file
        $full = Join-Path ([string] $Package.Root) ($relative -replace '/', '\')
        # An entry that is not there is the completeness gate's refusal (scenario_entry_missing),
        # not this one: 'All' must still answer Blocked rather than an invalid package.
        if (-not [IO.File]::Exists($full)) { continue }
        $row = $Package.MemberRows[$relative]
        if ($null -eq $row) { throw ('toolkit.member_unlisted: scenario=' + $relative) }
        if ([long] ((Get-Item -LiteralPath $full).Length) -ne [long] $row.Bytes) { throw ('toolkit.member_size_mismatch: ' + $relative) }
        if ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -cne [string] $row.Sha256) { throw ('toolkit.member_hash_mismatch: ' + $relative) }
    }
}

function Write-Stage5CCIndexEntry {    param([Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.ArrayList] $Index, [Parameter(Mandatory)][hashtable] $Entry)
    foreach ($field in @('reportPath','outputDirectory')) {
        $value = [string] $Entry[$field]
        if ([IO.Path]::IsPathRooted($value) -or $value -match '(^|[\\/])\.\.([\\/]|$)') {
            throw ('Summary path must be relative: ' + $field)
        }
    }
    [void] $Index.Add([ordered] @{
        id               = [string] $Entry.id
        status           = [string] $Entry.status
        reportStatus     = [string] $Entry.reportStatus
        executionStatus  = [string] $Entry.executionStatus
        reason           = Protect-UiAcceptanceText -Value ([string] $Entry.reason)
        reportPath       = [string] $Entry.reportPath
        outputDirectory  = [string] $Entry.outputDirectory
        durationMs       = [int] $Entry.durationMs
        complete         = [bool] $Entry.complete
    })
}

function Get-Stage5CCEntryEffectiveStatus {
    param(
        [Parameter(Mandatory)][string] $ReportStatus,
        [Parameter(Mandatory)][string] $ExecutionStatus
    )

    if ($ExecutionStatus -ceq 'Failed' -or $ReportStatus -ceq 'Failed') { return 'Failed' }
    if ($ExecutionStatus -ceq 'Blocked' -or $ReportStatus -ceq 'Blocked') { return 'Blocked' }
    if ($ReportStatus -ceq 'NotRun' -or $ExecutionStatus -ceq 'NotRun') { return 'NotRun' }
    return 'Passed'
}

function Get-Stage5CCOverallStatus {
    param([Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.ArrayList] $Index)

    $statuses = @($Index | ForEach-Object { [string] $_.status })
    if ($statuses.Count -eq 0) { return 'NotRun' }
    if (@($statuses | Where-Object { $_ -ceq 'Failed' }).Count -gt 0) { return 'Failed' }
    if (@($statuses | Where-Object { $_ -ceq 'Blocked' }).Count -gt 0) { return 'Blocked' }
    if (@($statuses | Where-Object { $_ -ceq 'NotRun' }).Count -gt 0) { return 'NotRun' }
    return 'Passed'
}

<#
.SYNOPSIS
    Turns a precheck observation into the persisted 'checks' node of the summary.

.DESCRIPTION
    The node carries what this run checked, what it found missing and whether the outcome
    stopped dispatch, so a reader can verify the gate from disk. It never carries a path:
    summaries are shared artifacts and must stay free of machine specific locations. The
    observation time comes from the check itself, not from the moment the summary is written.

    'blockedBeforeDispatch' has exactly one meaning: the completeness precheck stopped this
    batch, so no scenario was dispatched at all. A later stage refusing to run (the product,
    the pinned driver, the interactive desktop) is not this case and must stay false.
    'dispatchedCount' counts scenarios that really reached their invocation, so a root or
    driver that never came up is never counted.
#>
function New-Stage5CCCompletenessNode {
    param(
        [Parameter(Mandatory)] $Completeness,
        [Parameter(Mandatory)][bool] $BlockedBeforeDispatch,
        [Parameter(Mandatory)][int] $DispatchedCount
    )

    $missing = @()
    foreach ($item in @($Completeness.Missing)) {
        $missing += [ordered]@{
            id        = [string] $item.id
            entryFile = [string] $item.entryFile
            reason    = [string] $item.reason
        }
    }

    return ,[ordered]@{
        scope                 = [string] $Completeness.Scope
        checkedCount          = [int] $Completeness.CheckedCount
        checkedIds            = @($Completeness.Checked | ForEach-Object { [string] $_ })
        missingCount          = [int] $Completeness.MissingCount
        missing               = $missing
        blockedBeforeDispatch = [bool] $BlockedBeforeDispatch
        dispatchedCount       = [int] $DispatchedCount
        checkedUtc            = [string] $Completeness.CheckedUtc
    }
}

function New-Stage5CCSummary {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.ArrayList] $Index,
        [Parameter(Mandatory)][string] $Status,
        [string] $Reason = '',
        $Checks
    )

    $summary = [ordered]@{
        checkpoint = '5CC'
        complete   = ($Status -ceq 'Passed')
        status     = $Status
        reason     = $Reason
        entries    = @($Index.ToArray())
    }
    if ($null -ne $Checks) { $summary['checks'] = $Checks }

    return ,$summary
}

function Write-Stage5CCSummary {
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)] $Summary
    )

    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    $reportPath = Join-Path $OutputDirectory 'summary.json'
    [IO.File]::WriteAllText($reportPath, (ConvertTo-Json -InputObject $Summary -Depth 20), [Text.UTF8Encoding]::new($false))
    return $reportPath
}

function Get-Stage5CCOwnedLedger {
    param([Parameter(Mandatory)][string] $OwnedRoot)

    $ledgerPath = Join-Path $OwnedRoot 'owned-processes.txt'
    $records = @()
    if (-not [IO.File]::Exists($ledgerPath)) { return ,$records }
    foreach ($line in [IO.File]::ReadAllLines($ledgerPath)) {
        $parts = $line.Split('|')
        if ($parts.Count -ne 3) { continue }
        $processId = 0
        [long]$ticks = 0
        if (-not [int]::TryParse($parts[0], [ref] $processId) -or $processId -le 0) { continue }
        if (-not [long]::TryParse($parts[1], [ref] $ticks) -or $ticks -le 0) { continue }
        if (-not [IO.Path]::IsPathRooted($parts[2])) { continue }
        $records += [pscustomobject] @{ ProcessId = $processId; StartUtcTicks = $ticks; ExecutablePath = $parts[2] }
    }
    return ,$records
}

# [D0203-E2 EVIDENCE-FUNCTION]
<#
.SYNOPSIS
    Writes the D0203-E2 host evidence file: one independent document per entry process.

.DESCRIPTION
    The file lands under this run's own fresh output directory. Nothing that already exists is
    ever overwritten by a process that has not written it itself, and every failure is returned
    instead of thrown, so the caller folds it into its own outcome bookkeeping and an original
    business error is never replaced by an evidence problem.
#>
function Write-Stage5CCHostEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)] $Document,
        [string] $RunToken = '',
        [string] $OwnedRoot = '',
        [Parameter(Mandatory)][bool] $ClaimWrite
    )

    $path = Join-Path $OutputDirectory 'host-condition.json'
    try {
        if ([IO.File]::Exists($path) -and -not $ClaimWrite) {
            return [pscustomobject] @{ ok = $false; reason = 'evidence_already_present'; path = $path }
        }
        $json = ConvertTo-Json -InputObject $Document -Depth 12
        if (-not [string]::IsNullOrWhiteSpace($RunToken) -or -not [string]::IsNullOrWhiteSpace($OwnedRoot)) {
            $json = Protect-UiAcceptanceText -Value $json -RunToken $RunToken -OwnedRoot $OwnedRoot
        }
        [void][IO.Directory]::CreateDirectory($OutputDirectory)
        [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
        return [pscustomobject] @{ ok = $true; reason = ''; path = $path }
    }
    catch {
        return [pscustomobject] @{ ok = $false; reason = [string] $_.Exception.Message; path = $path }
    }
}
# [/D0203-E2 EVIDENCE-FUNCTION]

# --- inputs ----------------------------------------------------------------------------

$product = [IO.Path]::GetFullPath($ProductDirectory)
$productExecutable = Join-Path $product 'TraceFold.Media.exe'
if ($packageMode) {
    # A product that carries the toolkit, or a toolkit inside the product, is one tree pretending to
    # be two: the boundary is refused before anything is read from either side. A junction on the
    # product path makes the lexical answer meaningless, so it is refused first.
    try {
        Assert-Stage5CCExistingPathNotAliased -Path $product -Code 'toolkit.product_reparse'
        Assert-Stage5CCPathsDisjoint -Product $product -Toolkit ([string] $script:toolkitPackage.Root)
    }
    catch {
        [Console]::Error.WriteLine($_.Exception.Message)
        exit $ExitInvalidArguments
    }
}
$driverManifest = $null
if (-not $packageMode) {
    $driverManifestPath = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/winapp/win-x64/manifest.json'
    $driverManifest = Get-Content -LiteralPath $driverManifestPath -Raw | ConvertFrom-Json
}
$archivePath = $WinAppCliArchivePath
$contractPath = if ($packageMode) {
    [string] $script:toolkitPackage.ContractPath
}
else {
    Join-Path $script:repositoryRoot 'apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    # A moved toolkit writes into the results directory its own manifest declares; a source tree
    # keeps its .superpowers gate directory. Neither falls back to the other's location.
    $defaultParent = if ($packageMode) { [string] $script:toolkitPackage.ResultsRoot } else { Join-Path $script:repositoryRoot '.superpowers/stage5cc-uia-gate' }
    if ($packageMode) {
        try { Assert-Stage5CCExistingPathNotAliased -Path $defaultParent -Code 'toolkit.results_reparse' }
        catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit $ExitInvalidArguments
        }
    }
    [void][IO.Directory]::CreateDirectory($defaultParent)
    $OutputDirectory = Join-Path $defaultParent ('run-' + [guid]::NewGuid().ToString('N'))
}
else {
    # An explicit destination keeps its old rules and gains one: an existing junction inside it
    # would move the whole run somewhere the caller did not name.
    try { Assert-Stage5CCExistingPathNotAliased -Path $OutputDirectory -Code 'toolkit.output_reparse' }
    catch {
        [Console]::Error.WriteLine($_.Exception.Message)
        exit $ExitInvalidArguments
    }
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Directory]::Exists($output) -or [IO.File]::Exists($output)) {
    [Console]::Error.WriteLine('The output directory already exists; choose a fresh destination.')
    exit $ExitInvalidArguments
}

$requested = @()
try { $requested = @(Resolve-UiAcceptanceScenarioRequest -Requested $Scenario) }
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit $ExitInvalidArguments
}

$index = New-Object System.Collections.ArrayList
$dispatched = New-Object System.Collections.ArrayList
$blockedReason = ''

# --- request precheck ------------------------------------------------------------------
#
# Runs before anything else: no support publish, no owned root and no scenario is created when
# the registry owes an entry that does not exist. A named request owes only the entries it
# named, so verifying one slice is never blocked by a missing unrelated one; a request that
# contains 'All' owes the whole registry, and one missing entry stops the batch here.

$completeness = $null
try {
    $completeness = Test-UiAcceptanceRequestCompleteness -ScenariosRoot (Join-Path $script:uiRoot 'scenarios') -Requested $Scenario
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit $ExitInvalidArguments
}

if ($completeness.IncludesAll -and -not $completeness.Complete) {
    # 'All' asks for the checkpoint, so every registry entry must exist. Nothing is dispatched:
    # the walk stops here, before the product, driver, desktop or process checks and long before
    # a support publish or an owned root.
    foreach ($name in @($completeness.Checked)) {
        Write-Stage5CCIndexEntry -Index $index -Entry @{
            id = $name; status = 'Blocked'; reportStatus = 'Blocked'; executionStatus = 'Blocked'
            reason = 'scenario_entry_missing'; reportPath = ''; outputDirectory = ''
            durationMs = 0; complete = $false
        }
    }
    $checks = New-Stage5CCCompletenessNode -Completeness $completeness -BlockedBeforeDispatch $true -DispatchedCount 0
    $precheckStatus = Get-Stage5CCOverallStatus -Index $index
    $null = Write-Stage5CCSummary -OutputDirectory $output -Summary (New-Stage5CCSummary -Index $index -Status $precheckStatus -Reason 'scenario_entry_missing' -Checks $checks)
    [Console]::Out.WriteLine(('Stage 5CC: ' + $precheckStatus + ' (scenario_entry_missing)'))
    exit $ExitBlocked
}

if ($packageMode) {
    # The selected entries are the scenario files that can actually be loaded, and the aggregate
    # engine two of them import is a real conditional dependency. Both are checked here: after the
    # completeness gate has had its say, and before any support, driver or product is started.
    try {
        Assert-Stage5CCToolkitScenarioMembers -Package $script:toolkitPackage -Names $requested
        Assert-Stage5CCToolkitScenarioDependencies -Package $script:toolkitPackage -Names $requested
    }
    catch {
        [Console]::Error.WriteLine($_.Exception.Message)
        exit $ExitInvalidArguments
    }
}

$missingIds = @($completeness.Missing | ForEach-Object { [string] $_.id })
$missingRecorded = @()

if ($missingIds.Count -gt 0 -and -not $completeness.IncludesAll) {
    # A named request owes only the entries it named. Each missing one is refused here, before
    # anything is dispatched for it, and the rest of the request keeps running: verifying one
    # slice must never be blocked by an unrelated unimplemented scenario.
    foreach ($name in $missingIds) {
        Write-Stage5CCIndexEntry -Index $index -Entry @{
            id = $name; status = 'Blocked'; reportStatus = 'Blocked'; executionStatus = 'Blocked'
            reason = 'scenario_entry_missing'; reportPath = ''; outputDirectory = ''
            durationMs = 0; complete = $false
        }
        $missingRecorded += $name
    }
}

# Only the entries that still owe the later phases. Entries refused above are not walked again.
$pending = @($requested | Where-Object { $missingRecorded -cnotcontains $_ })

# Nothing left to run means no product, driver, desktop or process check needs answering: the
# run stops with the entries it already refused, and nothing was dispatched.
if ($pending.Count -gt 0) {
    if (-not [IO.File]::Exists($productExecutable)) {
        $blockedReason = 'published_product_unavailable'
    }
    elseif ($packageMode) {
        # The package validator already proved this member; the second read keeps a tree that
        # changed after validation from being driven as if it were the verified one.
        if (-not [IO.File]::Exists([string] $script:toolkitPackage.DriverExecutable)) {
            $blockedReason = 'toolkit_driver_unavailable'
        }
    }
    elseif (-not [IO.File]::Exists($archivePath)) {
        $blockedReason = 'pinned_driver_archive_unavailable'
    }
    else {
        $archive = Get-Item -LiteralPath $archivePath
        if ($archive.Length -ne [long] $driverManifest.archiveSize -or
            (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -cne [string] $driverManifest.sha256) {
            $blockedReason = 'pinned_driver_archive_mismatch'
        }
    }
    if ([string]::IsNullOrWhiteSpace($blockedReason) -and $pending -contains 'MainChain') {
        $mediaManifestPath = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/ffmpeg/win-x64/manifest.json'
        if (-not [IO.File]::Exists($mediaManifestPath)) {
            # A moved toolkit that does not carry the media-tools declaration cannot promise the
            # MainChain digest check; it says so instead of reading a path that is not there.
            $blockedReason = 'pinned_media_tools_declaration_unavailable'
        }
        else {
            # This declaration is consumed only by MainChain, and in a package it has to be one of
            # the members that were verified: a file that exists but carries no row would otherwise
            # decide the media-tool gate without ever being hashed.
            if ($packageMode) {
                try {
                    $mediaRelative = 'apps/media-desktop/tools/ffmpeg/win-x64/manifest.json'
                    $mediaRow = $script:toolkitPackage.MemberRows[$mediaRelative]
                    if ($null -eq $mediaRow) { throw ('toolkit.member_unlisted: media-manifest=' + $mediaRelative) }
                    if ([long] ((Get-Item -LiteralPath $mediaManifestPath).Length) -ne [long] $mediaRow.Bytes) { throw ('toolkit.member_size_mismatch: ' + $mediaRelative) }
                    if ((Get-FileHash -LiteralPath $mediaManifestPath -Algorithm SHA256).Hash -cne [string] $mediaRow.Sha256) { throw ('toolkit.member_hash_mismatch: ' + $mediaRelative) }
                }
                catch {
                    [Console]::Error.WriteLine($_.Exception.Message)
                    $blockedReason = 'pinned_media_tools_declaration_unverified'
                }
            }
            if ([string]::IsNullOrWhiteSpace($blockedReason)) {
                $mediaManifest = [IO.File]::ReadAllText($mediaManifestPath) | ConvertFrom-Json
                foreach ($fileName in @('ffmpeg.exe', 'ffprobe.exe')) {
                    $toolPath = Join-Path $product ('tools/ffmpeg/' + $fileName)
                    if (-not [IO.File]::Exists($toolPath)) {
                        $blockedReason = 'pinned_media_tools_unavailable'
                        break
                    }
                    $expected = $mediaManifest.files.PSObject.Properties[$fileName].Value
                    if ((Get-Item -LiteralPath $toolPath).Length -ne [long] $expected.size -or
                        (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash -cne [string] $expected.sha256) {
                        $blockedReason = 'pinned_media_tools_mismatch'
                        break
                    }
                }
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($blockedReason)) {
        if (-not (Test-UiInteractiveDesktop)) {
            $blockedReason = 'interactive_desktop_unavailable'
        }
        elseif (@(Get-Process -Name 'TraceFold.Media' -ErrorAction SilentlyContinue).Count -gt 0) {
            $blockedReason = 'ordinary_product_process_running'
        }
    }
}

if ($pending.Count -eq 0) {
    # Every requested entry was refused above, so there is nothing left to publish, own or run:
    # the precheck is what stopped this batch, and that is what the flag reports.
    $checks = New-Stage5CCCompletenessNode -Completeness $completeness -BlockedBeforeDispatch $true -DispatchedCount 0
    $refusedStatus = Get-Stage5CCOverallStatus -Index $index
    $null = Write-Stage5CCSummary -OutputDirectory $output -Summary (New-Stage5CCSummary -Index $index -Status $refusedStatus -Reason 'scenario_entry_missing' -Checks $checks)
    [Console]::Out.WriteLine(('Stage 5CC: ' + $refusedStatus + ' (scenario_entry_missing)'))
    exit $ExitBlocked
}

if (-not [string]::IsNullOrWhiteSpace($blockedReason)) {
    foreach ($name in $pending) {
        Write-Stage5CCIndexEntry -Index $index -Entry @{
            id = $name; status = 'Blocked'; reportStatus = 'Blocked'; executionStatus = 'Blocked'
            reason = $blockedReason; reportPath = ''; outputDirectory = ''
            durationMs = 0; complete = $false
        }
    }
    # The precheck ran and passed for this request, and that observation is recorded here too, so
    # every summary says which entries were checked and how many reached dispatch.
    $checks = New-Stage5CCCompletenessNode -Completeness $completeness -BlockedBeforeDispatch $false -DispatchedCount 0
    $preflightStatus = Get-Stage5CCOverallStatus -Index $index
    $null = Write-Stage5CCSummary -OutputDirectory $output -Summary (New-Stage5CCSummary -Index $index -Status $preflightStatus -Reason $blockedReason -Checks $checks)
    [Console]::Out.WriteLine(('Stage 5CC: ' + $preflightStatus + ' (' + $blockedReason + ')'))
    exit $ExitBlocked
}

# --- per scenario ----------------------------------------------------------------------

[void][IO.Directory]::CreateDirectory($output)
if ($packageMode) {
    # The package already carries the published support payload. Compiling here would need the SDK
    # the moved toolkit is supposed to run without, so the verified payload is used as it is.
    $supportRoot = [string] $script:toolkitPackage.SupportRoot
    $supportExecutable = [string] $script:toolkitPackage.SupportExecutable
}
else {
    $supportProject = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/TraceFold.Media.UiAcceptance.Support.csproj'
    $supportRoot = Join-Path $script:repositoryRoot 'apps/media-desktop/tools/TraceFold.Media.UiAcceptance.Support/bin/stage5cc-bootstrap'
    $packageCache = Join-Path $script:repositoryRoot 'apps/media-desktop/.packages'
    & dotnet publish $supportProject -c Release -o $supportRoot -p:RestorePackagesPath=$packageCache | Out-Null
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine('The support CLI could not be published.')
        exit $ExitBlocked
    }
    $supportExecutable = Join-Path $supportRoot 'TraceFold.Media.UiAcceptance.Support.exe'
}
if (-not [IO.File]::Exists($supportExecutable)) {
    [Console]::Error.WriteLine($(if ($packageMode) { 'The packaged support CLI is missing.' } else { 'The support CLI is missing after publish.' }))
    exit $ExitBlocked
}

$productHash = (Get-FileHash -LiteralPath $productExecutable -Algorithm SHA256).Hash
$productVersion = [string] (Get-Item -LiteralPath $productExecutable).VersionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($productVersion)) { $productVersion = 'unknown' }

# [D0203-E2 HOST-FACTS]
# The three registry names whose verification runs under the double-byte console condition in
# the process that actually executes them; each host records its own facts and no host reads
# another host's setting.
$hostConditionFamilies = @('Library', 'BatchMetadataUndo', 'LibraryFilterNaming')
# Every captured fact carries its own state and its own error text. A field that could not be
# read keeps state 'unknown' with the original error beside it, and is never replaced by an
# empty placeholder that later reads like a real answer; the host version is part of the
# evidence document itself instead of a construction that is discarded before it is written.
$hostConditionVersion = [ordered] @{
    psVersion = ''; psVersionState = 'unknown'; psVersionError = ''
    psEdition = ''; psEditionState = 'unknown'; psEditionError = ''
    clr = ''; clrState = 'unknown'; clrError = ''
}
try { $hostConditionVersion.psVersion = $PSVersionTable.PSVersion.ToString(); $hostConditionVersion.psVersionState = 'captured' }
catch { $hostConditionVersion.psVersionError = [string] $_.Exception.Message }
try { $hostConditionVersion.psEdition = [string] $PSVersionTable.PSEdition; $hostConditionVersion.psEditionState = 'captured' }
catch { $hostConditionVersion.psEditionError = [string] $_.Exception.Message }
try { $hostConditionVersion.clr = [Environment]::Version.ToString(); $hostConditionVersion.clrState = 'captured' }
catch { $hostConditionVersion.clrError = [string] $_.Exception.Message }
$hostConditionProcess = [ordered] @{
    pid = $PID
    startUtcTicks = $null; startUtcTicksState = 'unknown'; startUtcTicksError = ''
    path = ''; pathState = 'unknown'; pathError = ''
    parentPid = $null; parentPidState = 'unknown'; parentPidError = ''
    commandLine = ''; commandLineState = 'unknown'; commandLineError = ''
    product = [ordered] @{
        path = $productExecutable
        bytes = $null; bytesState = 'unknown'; bytesError = ''
        sha256 = $productHash; fileVersion = $productVersion
    }
}
try { $hostConditionProcess.startUtcTicks = [long] (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks; $hostConditionProcess.startUtcTicksState = 'captured' }
catch { $hostConditionProcess.startUtcTicksError = [string] $_.Exception.Message }
try { $hostConditionProcess.path = [string] (Get-Process -Id $PID).Path; $hostConditionProcess.pathState = 'captured' }
catch { $hostConditionProcess.pathError = [string] $_.Exception.Message }
try {
    $parentRecord = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $PID) -ErrorAction Stop
    if ($null -eq $parentRecord) { throw 'the process table answered no record for this process id' }
    $hostConditionProcess.parentPid = [int] $parentRecord.ParentProcessId
    $hostConditionProcess.parentPidState = 'captured'
}
catch { $hostConditionProcess.parentPidError = [string] $_.Exception.Message }
try { $hostConditionProcess.commandLine = [string] [Environment]::CommandLine; $hostConditionProcess.commandLineState = 'captured' }
catch { $hostConditionProcess.commandLineError = [string] $_.Exception.Message }
try { $hostConditionProcess.product.bytes = [long] ((Get-Item -LiteralPath $productExecutable).Length); $hostConditionProcess.product.bytesState = 'captured' }
catch { $hostConditionProcess.product.bytesError = [string] $_.Exception.Message }
$hostEvidence = [ordered] @{ evidence = 'host-condition'; version = 1; hostConditionVersion = $hostConditionVersion; process = $hostConditionProcess; scenarios = (New-Object System.Collections.ArrayList) }
$hostEvidenceClaimed = $false
$hostEvidenceWriteFailure = ''
# [/D0203-E2 HOST-FACTS]

foreach ($name in $pending) {
    $started = [DateTimeOffset]::UtcNow
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $entryFile = Get-UiAcceptanceScenarioFile -Name $name
    $scenarioPath = Join-Path $script:uiRoot ('scenarios/' + $entryFile)
    $runId = [guid]::NewGuid()
    $runToken = [guid]::NewGuid().ToString('N')
    $runOutput = Join-Path $output $name
    $runRoot = ''
    $reportPath = ''
    $reportStatus = 'NotRun'
    $executionStatus = 'NotRun'
    $reason = ''
    $hostConditionApplies = ($hostConditionFamilies -ccontains $name)
    $hostConditionAttempted = $false
    $hostConditionRestoreFailed = $false
    $savedOutputEncoding = $null

    # Checked again at dispatch time: an entry file may have appeared or vanished since the
    # request precheck, and this is the check that actually decides whether anything runs. The
    # reason stays 'scenario_not_implemented', which is an implementation debt, never a product
    # failure, so it must never be reported as one.
    if (-not [IO.File]::Exists($scenarioPath)) {
        $executionStatus = 'Blocked'
        $reason = 'scenario_not_implemented'
        $clock.Stop()
        Write-Stage5CCIndexEntry -Index $index -Entry @{
            id = $name; status = (Get-Stage5CCEntryEffectiveStatus -ReportStatus $reportStatus -ExecutionStatus $executionStatus)
            reportStatus = $reportStatus; executionStatus = $executionStatus; reason = $reason
            reportPath = ''; outputDirectory = $name
            durationMs = [int] $clock.ElapsedMilliseconds; complete = $false
        }
        continue
    }

    try {
        # [D0203-E2 CONDITION-SETUP]
        # Same family as the WorkflowScenario cases: save the process console encoding, set and
        # measure the double-byte code page, and run this scenario's body here while it holds.
        # The restore is the finally on this same try; both paths run it.
        $scenarioEvidence = [ordered] @{ name = $name; condition = $null; instances = $null }
        [void] $hostEvidence.scenarios.Add($scenarioEvidence)
        if ($hostConditionApplies) {
            $condition = [ordered] @{ applies = $true; established = $false; beforeCodePage = $null; duringCodePage = $null; afterCodePage = $null; restoreOk = $false; setupError = ''; restoreError = '' }
            $scenarioEvidence.condition = $condition
            try {
                $savedOutputEncoding = [Console]::OutputEncoding
                $condition.beforeCodePage = [int] $savedOutputEncoding.CodePage
                $hostConditionAttempted = $true
                [Console]::OutputEncoding = [Text.Encoding]::GetEncoding(936)
                $condition.duringCodePage = [int] [Console]::OutputEncoding.CodePage
                if ($condition.duringCodePage -ne 936) { throw 'the double-byte host condition was not established' }
                $condition.established = $true
            }
            catch {
                $condition.setupError = [string] $_.Exception.Message
                throw ('host_condition_not_established: ' + [string] $_.Exception.Message)
            }
        }
        # [/D0203-E2 CONDITION-SETUP]
        # Preparation stays in this scope, never in a helper script block: the catch below has to
        # know the root it was given so it can stop whatever the root owns and hand it back to the
        # support tool, and every later step reads the paths produced here. Moving this away to
        # make counting easier made the walk forget where its own scaffold was.
        $runRoot = New-UiAcceptanceRunRoot -SupportPath $supportExecutable `
            -ParentDirectory ([IO.Path]::GetTempPath()) -RunId $runId -RunToken $runToken
        $runSupport = Join-Path $runRoot 'support'
        $runDriver = Join-Path $runRoot 'winapp'
        Copy-Item -LiteralPath $supportRoot -Destination $runSupport -Recurse -Force
        [void][IO.Directory]::CreateDirectory($runDriver)
        if ($packageMode) {
            # The package ships the driver unpacked: this is a copy into the owned run root, not an
            # extraction, and it stays byte-identical to the member the package validator verified.
            foreach ($driverItem in @(Get-ChildItem -LiteralPath ([string] $script:toolkitPackage.DriverRoot) -Force)) {
                Copy-Item -LiteralPath $driverItem.FullName -Destination $runDriver -Recurse -Force
            }
        }
        else {
            Expand-Archive -LiteralPath $archivePath -DestinationPath $runDriver
        }
        if (-not [IO.File]::Exists((Join-Path $runDriver 'winapp.exe'))) { throw 'pinned_driver_executable_missing' }

        # [D0203-E2 INSTANCE-FACTS]
        # The instances that are about to be used are bound per member, from this run's own records,
        # after the copy and the extraction and before anything is dispatched; errors are stored beside
        # the fields instead of being collapsed into one string. The support copy is compared member by
        # member against the published source it was copied from; the extracted driver executable is
        # compared against the member bytes inside the verified archive itself, because the archive hash
        # alone says nothing about the file that will actually run. Cleanup later deletes these
        # instances; the facts recorded here stay readable without them.
        $instanceFacts = [ordered] @{
            support = [ordered] @{
                sourceRoot = ''; instanceRoot = ''
                members = @(); memberCount = 0; allMatchSource = $false; allMatchManifest = $null; captureState = 'unknown'; error = ''
            }
            driver = [ordered] @{
                archivePath = $null; archiveBytes = $null; archiveSha256 = $null
                manifestSha256 = $null; manifestArchiveSize = $null; matchesManifest = $null
                winappExePath = ''; winappExeBytes = $null; winappExeSha256 = $null; winappExeFileVersion = ''
                archiveMemberName = $null; archiveMemberBytes = $null; archiveMemberSha256 = $null
                matchesArchiveMember = $null; memberReadState = 'unknown'; error = ''
            }
        }
        try {
            $supportSourceFull = [IO.Path]::GetFullPath($supportRoot)
            $supportInstanceFull = [IO.Path]::GetFullPath($runSupport)
            $instanceFacts.support.sourceRoot = Protect-UiAcceptanceText -Value $supportSourceFull -RunToken $runToken -OwnedRoot $runRoot
            $instanceFacts.support.instanceRoot = Protect-UiAcceptanceText -Value $supportInstanceFull -RunToken $runToken -OwnedRoot $runRoot
            $memberRows = New-Object System.Collections.ArrayList
            $supportPrefix = ''
            if ($packageMode) {
                $supportPrefix = ([string] $script:toolkitPackage.SupportRoot).Substring(([string] $script:toolkitPackage.Root).Length).TrimStart([char] 92) -replace '\\', '/'
            }
            foreach ($memberFile in @(Get-ChildItem -LiteralPath $supportInstanceFull -Recurse -File | Sort-Object FullName)) {
                $relative = $memberFile.FullName.Substring($supportInstanceFull.Length).TrimStart([char] 92)
                $memberRow = [ordered] @{ relative = $relative; instanceBytes = $null; instanceSha256 = ''; sourceBytes = $null; sourceSha256 = ''; matchesSource = $false; manifestSha256 = $null; matchesManifestMember = $null; state = 'unknown'; error = '' }
                try {
                    $memberRow.instanceBytes = [long] $memberFile.Length
                    $memberRow.instanceSha256 = (Get-FileHash -LiteralPath $memberFile.FullName -Algorithm SHA256).Hash
                    $sourceMember = Join-Path $supportSourceFull $relative
                    if (-not [IO.File]::Exists($sourceMember)) { throw ('the published source member is missing: ' + $relative) }
                    $memberRow.sourceBytes = [long] ((Get-Item -LiteralPath $sourceMember).Length)
                    $memberRow.sourceSha256 = (Get-FileHash -LiteralPath $sourceMember -Algorithm SHA256).Hash
                    $memberRow.matchesSource = ($memberRow.instanceSha256 -ceq $memberRow.sourceSha256 -and $memberRow.instanceBytes -eq $memberRow.sourceBytes)
                    if ($packageMode) {
                        # The copy that will run is compared with the member the manifest bound, not
                        # only with the directory it was copied from: a package whose source tree
                        # changed after validation cannot launder the change through the copy.
                        $manifestKey = $supportPrefix + '/' + ($relative -replace '\\', '/')
                        $manifestRow = $script:toolkitPackage.MemberRows[$manifestKey]
                        if ($null -eq $manifestRow) { throw ('the run copy carries a member the package does not declare: ' + $manifestKey) }
                        $memberRow.manifestSha256 = [string] $manifestRow.Sha256
                        $memberRow.matchesManifestMember = ($memberRow.instanceSha256 -ceq [string] $manifestRow.Sha256 -and $memberRow.instanceBytes -eq [long] $manifestRow.Bytes)
                    }
                    $memberRow.state = 'captured'
                }
                catch { $memberRow.error = [string] $_.Exception.Message }
                [void] $memberRows.Add($memberRow)
            }
            $instanceFacts.support.members = $memberRows.ToArray()
            $instanceFacts.support.memberCount = $memberRows.Count
            $instanceFacts.support.allMatchSource = ($memberRows.Count -gt 0 -and @($memberRows | Where-Object { -not [bool] $_.matchesSource }).Count -eq 0)
            if ($packageMode) {
                $instanceFacts.support.allMatchManifest = ($memberRows.Count -gt 0 -and @($memberRows | Where-Object { -not [bool] $_.matchesManifestMember }).Count -eq 0)
            }
            $instanceFacts.support.captureState = 'captured'
        }
        catch { $instanceFacts.support.error = [string] $_.Exception.Message }
        try {
            $winappInstance = Join-Path $runDriver 'winapp.exe'
            $instanceFacts.driver.winappExePath = Protect-UiAcceptanceText -Value $winappInstance -RunToken $runToken -OwnedRoot $runRoot
            $instanceFacts.driver.winappExeBytes = [long] ((Get-Item -LiteralPath $winappInstance).Length)
            $instanceFacts.driver.winappExeSha256 = (Get-FileHash -LiteralPath $winappInstance -Algorithm SHA256).Hash
            $instanceFacts.driver.winappExeFileVersion = [string] ((Get-Item -LiteralPath $winappInstance).VersionInfo.FileVersion)
            if ($packageMode) {
                # A moved toolkit has no driver archive. Every archive class stays null - a false
                # comparison or an empty string would read as an observation that was never made -
                # and the copy that will actually run is compared with the verified package member.
                $instanceFacts.driver.archivePath = $null
                $instanceFacts.driver.archiveBytes = $null
                $instanceFacts.driver.archiveSha256 = $null
                $instanceFacts.driver.manifestSha256 = $null
                $instanceFacts.driver.manifestArchiveSize = $null
                $instanceFacts.driver.matchesManifest = $null
                $instanceFacts.driver.archiveMemberName = $null
                $instanceFacts.driver.archiveMemberBytes = $null
                $instanceFacts.driver.archiveMemberSha256 = $null
                $instanceFacts.driver.matchesArchiveMember = $null
                $instanceFacts.driver.memberReadState = 'not-packaged'
                $instanceFacts.driver['archiveState'] = 'not-packaged'
                $instanceFacts.driver['packageMemberName'] = [string] $script:toolkitPackage.DriverMemberName
                $instanceFacts.driver['packageMemberSha256'] = [string] $script:toolkitPackage.DriverSha256
                $instanceFacts.driver['matchesPackageMember'] = [bool] ($instanceFacts.driver.winappExeSha256 -ceq [string] $script:toolkitPackage.DriverSha256)
            }
            else {
                $instanceFacts.driver.archivePath = Protect-UiAcceptanceText -Value $archivePath -RunToken $runToken -OwnedRoot $runRoot
                $instanceFacts.driver.archiveBytes = [long] ((Get-Item -LiteralPath $archivePath).Length)
                $instanceFacts.driver.archiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
                $instanceFacts.driver.manifestSha256 = [string] $driverManifest.sha256
                $instanceFacts.driver.manifestArchiveSize = [long] $driverManifest.archiveSize
                $instanceFacts.driver.matchesManifest = ($instanceFacts.driver.archiveSha256 -ceq [string] $driverManifest.sha256 -and $instanceFacts.driver.archiveBytes -eq [long] $driverManifest.archiveSize)
                # Both loader names are attempted so the same text runs on both hosts: Windows PowerShell
                # needs the compression assemblies loaded before their types can be used.
                try { Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
                try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }
                $driverZip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($archivePath))
                try {
                    $driverMember = @($driverZip.Entries | Where-Object { $_.FullName -ceq 'winapp.exe' })
                    if ($driverMember.Count -ne 1) { throw ('the archive carries ' + $driverMember.Count + ' winapp.exe members') }
                    $memberStream = $driverMember[0].Open()
                    try {
                        $memberBuffer = New-Object IO.MemoryStream
                        try { $memberStream.CopyTo($memberBuffer); $memberBytes = $memberBuffer.ToArray() }
                        finally { $memberBuffer.Dispose() }
                    }
                    finally { $memberStream.Dispose() }
                    $memberHasher = [Security.Cryptography.SHA256]::Create()
                    try { $memberHash = ([BitConverter]::ToString($memberHasher.ComputeHash($memberBytes))).Replace('-', '') }
                    finally { $memberHasher.Dispose() }
                    $instanceFacts.driver.archiveMemberName = 'winapp.exe'
                    $instanceFacts.driver.archiveMemberBytes = [long] $memberBytes.Length
                    $instanceFacts.driver.archiveMemberSha256 = $memberHash
                    $instanceFacts.driver.matchesArchiveMember = ($instanceFacts.driver.winappExeSha256 -ceq $memberHash -and $instanceFacts.driver.winappExeBytes -eq [long] $memberBytes.Length)
                    $instanceFacts.driver.memberReadState = 'captured'
                }
                finally { $driverZip.Dispose() }
            }
        }
        catch { $instanceFacts.driver.error = [string] $_.Exception.Message }
        $scenarioEvidence.instances = $instanceFacts
        # [/D0203-E2 INSTANCE-FACTS]

        $entryArguments = @{
            ScenarioId        = (ConvertTo-UiAcceptanceSlug -Value $name)
            ExecutablePath    = $productExecutable
            ContractPath      = $contractPath
            OwnedRoot         = $runRoot
            RunToken          = $runToken
            OutputDirectory   = $runOutput
            SupportPath       = (Join-Path $runSupport 'TraceFold.Media.UiAcceptance.Support.exe')
            WinAppCliArchivePath = $WinAppCliArchivePath
            WinAppPath        = (Join-Path $runDriver 'winapp.exe')
        }

        $cleanupBody = {
            $ledger = Get-Stage5CCOwnedLedger -OwnedRoot $runRoot
            if ($ledger.Count -gt 0) {
                $null = Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($product)
            }
        }
        # Provenance, product, driver, fixture and host facts now come from the metadata module:
        # each field family has one producer, and a value that could not be read stays null beside
        # its own state. The legacy ten-key hashtable the Runner requires is projected from the
        # block below; the Runner keeps the values' types instead of collapsing them to text.
        if ($packageMode) {
            # A moved toolkit is not a checkout, and it ships the driver unpacked: no repository
            # commit is probed and no archive version/hash is claimed, while the executable that
            # will run is still measured below.
            $metadataSourceProvenance = Get-UiMetadataSourceProvenance -ProductExecutable $productExecutable -RepositoryRoot $script:repositoryRoot -CandidateSha256 $null -NoRepositoryProbe
            $metadataProductIdentity = Get-UiMetadataProductIdentity -ProductExecutable $productExecutable -ProductDirectory (Split-Path -Parent $productExecutable)
            $metadataDriverIdentity = Get-UiMetadataDriverIdentity -ArchivePath '' -ExtractedExePath (Join-Path $runDriver 'winapp.exe') -ManifestVersion $null -ManifestSha256 $null -ManifestArchiveSize $null -MemberName ([string] $script:toolkitPackage.DriverMemberName)
        }
        else {
            $metadataSourceProvenance = Get-UiMetadataSourceProvenance -ProductExecutable $productExecutable -RepositoryRoot $script:repositoryRoot -CandidateSha256 $null
            $metadataProductIdentity = Get-UiMetadataProductIdentity -ProductExecutable $productExecutable -ProductDirectory (Split-Path -Parent $productExecutable)
            $metadataDriverIdentity = Get-UiMetadataDriverIdentity -ArchivePath $WinAppCliArchivePath -ExtractedExePath (Join-Path $runDriver 'winapp.exe') -ManifestVersion ([string] $driverManifest.version) -ManifestSha256 ([string] $driverManifest.sha256) -ManifestArchiveSize ([long] $driverManifest.archiveSize)
        }
        $declaredFixtureAssets = @()
        if ([IO.File]::Exists($contractPath)) {
            $contractDocument = Read-UiMetadataJsonDocument -Path $contractPath
            $contractValue = $contractDocument['value']
            if ($contractDocument['state'] -ceq 'captured' -and $null -ne $contractValue -and $null -ne $contractValue.PSObject.Properties['fixtureAssets']) {
                $declaredFixtureAssets = @($contractValue.fixtureAssets)
            }
        }
        $metadataFixtures = Get-UiMetadataFixtureManifest -DeclaredAssets $declaredFixtureAssets -RepositoryRoot $script:repositoryRoot
        $metadataHostObservation = Get-UiMetadataHostObservation
        $metadataBlock = New-UiMetadataReportBlock -SourceProvenance $metadataSourceProvenance -ProductIdentity $metadataProductIdentity -DriverIdentity $metadataDriverIdentity -FixtureManifest $metadataFixtures -HostObservation $metadataHostObservation -HostOs ([Environment]::OSVersion.VersionString) -Redactor { param([AllowNull()] $Value) if ($null -eq $Value) { return $null }; return (Protect-UiAcceptanceText -Value $Value -RunToken $runToken -OwnedRoot $runRoot) }
        # The legacy ten-key hashtable the Runner requires, now sourced from the block through its
        # indexer: a known value keeps its text, an unknown stays null instead of collapsing to an
        # empty string or to a placeholder. The Runner projection and the report writer decide the
        # legacy shape.
        $metadata = @{
            sourceCommit  = $metadataBlock['sourceCommit']
            productHash   = $metadataBlock['productHash']
            productVersion = $metadataBlock['productVersion']
            driverVersion = $metadataBlock['driverVersion']
            driverHash    = $metadataBlock['driverHash']
            fixtureHashes = $metadataBlock['fixtureHashes']
            hostOs        = $metadataBlock['hostOs']
            dpi           = $metadataBlock['dpi']
            theme         = $metadataBlock['theme']
            viewport      = $metadataBlock['viewport']
            metadataEvidence = $metadataBlock['metadataEvidence']
        }

        $result = Invoke-UiAcceptanceRun -ScenarioId $name `
            -Scenario {
                # Counted on the line where the scenario really runs and nowhere else: preparation
                # that failed above never reaches it, so 'dispatchedCount' cannot inflate, and a
                # scenario that does run is counted exactly once.
                Register-UiAcceptanceDispatch -Name $name -Ledger $dispatched
                Invoke-UiAcceptanceScenario -Path $scenarioPath -Arguments $entryArguments
            } `
            -Cleanup $cleanupBody `
            -OwnedRoot $runRoot -OutputDirectory $runOutput -Metadata $metadata `
            -RunToken $runToken -SupportPath $supportExecutable `
            -ApprovedDirectories @($product)
        $reportPath = [string] $result.ReportPath
        if ([IO.File]::Exists($reportPath)) {
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
            $reportStatus = [string] @($report.scenarios)[0].status
        }
        $executionStatus = if ($result.Status -ceq 'Passed') { 'Passed' } else { [string] $result.Status }
        if ($result.CleanupStatus -cne 'Passed') { $executionStatus = 'Failed' }
    }
    catch {
        $executionStatus = 'Blocked'
        $reason = Protect-UiAcceptanceText -Value $_.Exception.Message -RunToken $runToken -OwnedRoot $runRoot
        if (-not [string]::IsNullOrWhiteSpace($runRoot) -and [IO.Directory]::Exists($runRoot)) {
            try {
                $ledger = Get-Stage5CCOwnedLedger -OwnedRoot $runRoot
                if ($ledger.Count -gt 0) {
                    $null = Stop-OwnedProcessTree -Ledger $ledger -ApprovedDirectories @($product)
                }
                $null = & $supportExecutable root delete --root $runRoot --token $runToken
                if ($LASTEXITCODE -ne 0) { throw 'owned_root_delete_refused' }
            }
            catch {
                $executionStatus = 'Failed'
                $reason += '; setup_cleanup_failed'
            }
        }
    }
    finally {
        # [D0203-E2 CONDITION-RESTORE]
        # Runs whether the body succeeded, failed or threw: hand the console back and read it.
        if ($hostConditionApplies -and $hostConditionAttempted) {
            try {
                [Console]::OutputEncoding = $savedOutputEncoding
                $afterCodePage = [int] [Console]::OutputEncoding.CodePage
                $scenarioEvidence.condition.afterCodePage = $afterCodePage
                $scenarioEvidence.condition.restoreOk = ($afterCodePage -eq [int] $savedOutputEncoding.CodePage)
                if (-not $scenarioEvidence.condition.restoreOk) {
                    $hostConditionRestoreFailed = $true
                    try { [Console]::Error.WriteLine('e2-host-condition-restore-mismatch') } catch { }
                }
            }
            catch {
                $hostConditionRestoreFailed = $true
                try {
                    $scenarioEvidence.condition.restoreError = [string] $_.Exception.Message
                    $scenarioEvidence.condition.restoreOk = $false
                }
                catch { }
                try { [Console]::Error.WriteLine('e2-host-condition-restore-failed') } catch { }
            }
        }
        # [/D0203-E2 CONDITION-RESTORE]
    }

    $clock.Stop()

    # [D0203-E2 EVIDENCE-WRITE]
    # The independent evidence document is written here, before the outcome is booked, so an
    # evidence problem is visible in this entry's own status without touching the report schema.
    try {
        $evidenceWrite = Write-Stage5CCHostEvidence -OutputDirectory $output -Document $hostEvidence -RunToken $runToken -OwnedRoot $runRoot -ClaimWrite $hostEvidenceClaimed
        if ([bool] $evidenceWrite.ok) { $hostEvidenceClaimed = $true }
        else {
            $hostEvidenceWriteFailure = [string] $evidenceWrite.reason
            try { [Console]::Error.WriteLine('e2-host-evidence-write-failed: ' + [string] $evidenceWrite.reason) } catch { }
        }
    }
    catch {
        $hostEvidenceWriteFailure = [string] $_.Exception.Message
        try { [Console]::Error.WriteLine('e2-host-evidence-write-failed') } catch { }
    }
    # [/D0203-E2 EVIDENCE-WRITE]

    # [D0203-E2 FINALIZE]
    # A failed host condition, a failed restore or a failed evidence write never reads as a
    # pass, and an existing business failure or block is never overwritten by these markers.
    if ($hostConditionRestoreFailed -or $hostEvidenceWriteFailure -ne '') {
        if ($executionStatus -cne 'Failed' -and $executionStatus -cne 'Blocked') { $executionStatus = 'Failed' }
        if ($hostConditionRestoreFailed -and $reason -notlike '*host_condition_restore_failed*') { $reason += '; host_condition_restore_failed' }
        if ($hostEvidenceWriteFailure -ne '' -and $reason -notlike '*host_evidence_write_failed*') { $reason += '; host_evidence_write_failed' }
    }
    # [/D0203-E2 FINALIZE]

    $status = Get-Stage5CCEntryEffectiveStatus -ReportStatus $reportStatus -ExecutionStatus $executionStatus
    Write-Stage5CCIndexEntry -Index $index -Entry @{
        id = $name; status = $status; reportStatus = $reportStatus; executionStatus = $executionStatus
        reason = $reason; reportPath = $(if ([IO.File]::Exists($reportPath)) { $name + '/report.json' } else { '' }); outputDirectory = $name
        durationMs = [int] $clock.ElapsedMilliseconds
        complete = ($status -ceq 'Passed' -and -not [string]::IsNullOrWhiteSpace($reportPath))
    }
}

$overall = Get-Stage5CCOverallStatus -Index $index
$checks = New-Stage5CCCompletenessNode -Completeness $completeness -BlockedBeforeDispatch $false -DispatchedCount $dispatched.Count
$null = Write-Stage5CCSummary -OutputDirectory $output -Summary (New-Stage5CCSummary -Index $index -Status $overall -Checks $checks)
[Console]::Out.WriteLine('Stage 5CC: ' + $overall)

if ($overall -ceq 'Passed') { exit 0 }
exit $ExitBlocked
