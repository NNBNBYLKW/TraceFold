[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceProductArchive,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $DocumentationRef,

    [Parameter(Mandatory)]
    [string] $WorkingDirectory,

    [string] $RepositoryRoot,

    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$previousGitOptionalLocks = $env:GIT_OPTIONAL_LOCKS

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$ExpectedProductSourceCommit = '01264b7a086aebfcfa2f546d294def5076113e3e'
$ExpectedProductArchiveName = 'TraceFold-MediaDesktop-Stage5C-C-win-x64.zip'
$ExpectedProductArchiveBytes = 225879347L
$ExpectedProductArchiveSha256 = '19B82302D00916B9B74143DC5B72B9D48D84DF0A8A8CECBAA52BEAFD839EFBAF'
$ExpectedProductMemberCount = 584
$BundleFileName = 'TraceFold-MediaDesktop-Stage5C-C-win-x64-docs-20261025.zip'
$ChecksumFileName = 'SHA256SUMS-docs-20261025.txt'
$PublicManifestFileName = 'TraceFold-MediaDesktop-Stage5C-C-win-x64-docs-20261025.public-manifest.json'
$PrivateReceiptFileName = 'TraceFold-MediaDesktop-Stage5C-C-win-x64-docs-20261025.private-receipt.json'
$ReleaseTag = 'v4.0.0-stage5cc'
$ReleasePageUrl = 'https://github.com/NNBNBYLKW/TraceFold/releases/tag/' + $ReleaseTag
$ReleaseDownloadBaseUrl = 'https://github.com/NNBNBYLKW/TraceFold/releases/download/' + $ReleaseTag + '/'
$ManifestFileName = 'DOCS-ENHANCED-MANIFEST.json'
$DocumentationMembers = @(
    'README.md',
    'README.en.md',
    'docs/capability-matrix.md',
    'docs/capability-matrix.en.md',
    'docs/offline-quick-start.md',
    'docs/offline-quick-start.en.md',
    'docs/product-capabilities.json',
    'SUPPORT.md',
    'docs/getting-started.md',
    'docs/troubleshooting.md',
    'docs/images/library.png'
)
$GeneratedDocumentationMembers = @(
    'START-HERE.txt',
    'docs/README.md',
    'docs/development/README.md',
    'docs/development/getting-started.md',
    $ManifestFileName
)

function Assert-FullyQualifiedPath {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Label)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Label must be a fully qualified path."
    }

    $isDriveAbsolute = $Path -match '^[A-Za-z]:[\\/]'
    $isUncAbsolute = $Path -match '^\\\\[^\\/]+[\\/][^\\/]+'
    $isExtendedAbsolute = $Path -match '^\\\\\?\\[A-Za-z]:[\\/]'
    if (-not ($isDriveAbsolute -or $isUncAbsolute -or $isExtendedAbsolute)) {
        throw "$Label must be a fully qualified path."
    }

    try {
        [void][IO.Path]::GetFullPath($Path)
    }
    catch {
        throw "$Label must be a valid fully qualified path."
    }
}

function Assert-ExistingPathHasNoReparsePoint {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Label)

    $full = [IO.Path]::GetFullPath($Path)
    $current = [IO.Path]::GetPathRoot($full)
    $relative = $full.Substring($current.Length)
    foreach ($segment in $relative.Split(
        [IO.Path]::DirectorySeparatorChar,
        [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $attributes = [IO.File]::GetAttributes($current)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Label contains a reparse point: $current"
            }
        }
    }
}

function Get-FileSha256 {
    param([Parameter(Mandatory)][string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-NormalizedZipMemberName {
    param([Parameter(Mandatory)][string] $Name)

    $normalized = $Name.Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith('/') -or
        $normalized.Contains(':')) {
        throw "Unsafe ZIP member name: $Name"
    }

    $parts = $normalized.Split('/')
    foreach ($part in $parts) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -eq '.' -or $part -eq '..') {
            throw "Unsafe ZIP member name: $Name"
        }
    }

    return ($parts -join '/')
}

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Label
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $fullRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label escapes its owned root: $fullPath"
    }
}

function Get-RelativeFilePath {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Path
    )

    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath($Path)
    Assert-PathUnderRoot -Path $fullPath -Root $fullRoot -Label 'File'
    return $fullPath.Substring($fullRoot.Length + 1).Replace('\', '/')
}

function Get-FileInventory {
    param([Parameter(Mandatory)][string] $Root)

    $caseNames = @{}
    $inventory = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
            Sort-Object FullName)) {
        $relative = Get-RelativeFilePath -Root $Root -Path $file.FullName
        $key = $relative.ToLowerInvariant()
        if ($caseNames.ContainsKey($key)) {
            throw "Case-insensitive duplicate member: $relative"
        }

        $caseNames[$key] = $true
        $inventory += [pscustomobject][ordered]@{
            path = $relative
            bytes = [long]$file.Length
            sha256 = Get-FileSha256 -Path $file.FullName
        }
    }

    return $inventory
}

function Get-InventoryBindingSha256 {
    param([Parameter(Mandatory)][object[]] $Inventory)

    $lines = @($Inventory | Sort-Object path | ForEach-Object {
        $_.path + '|' + $_.bytes + '|' + $_.sha256
    })
    $text = $lines -join "`n"
    $encoding = [System.Text.UTF8Encoding]::new($false)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha.ComputeHash($encoding.GetBytes($text)))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Assert-NoFileDirectoryConflicts {
    param(
        [Parameter(Mandatory)][string[]] $Paths,
        [Parameter(Mandatory)][string] $Label
    )

    $files = @{}
    $directories = @{}
    foreach ($path in $Paths) {
        $normalized = Get-NormalizedZipMemberName -Name $path
        $fileKey = $normalized.ToLowerInvariant()
        if ($files.ContainsKey($fileKey)) {
            throw "$Label contains a case-insensitive duplicate: $normalized"
        }

        $files[$fileKey] = $normalized
        $segments = $normalized.Split('/')
        for ($index = 1; $index -lt $segments.Length; $index++) {
            $directoryKey = (($segments[0..($index - 1)]) -join '/').ToLowerInvariant()
            $directories[$directoryKey] = $true
        }
    }

    foreach ($fileKey in $files.Keys) {
        if ($directories.ContainsKey($fileKey)) {
            throw "$Label uses the same path as a file and directory: $($files[$fileKey])"
        }
    }
}

function Assert-RelativeMarkdownReferences {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string[]] $RelativePaths
    )

    $externalLinks = New-Object System.Collections.Generic.List[string]
    foreach ($relative in $RelativePaths) {
        $source = Join-Path $Root ($relative.Replace('/', '\'))
        $text = [IO.File]::ReadAllText($source)
        $matches = [regex]::Matches($text, '!?\[[^\]]*\]\(([^)]+)\)')
        foreach ($match in $matches) {
            $rawTarget = $match.Groups[1].Value.Trim()
            if ($rawTarget.StartsWith('<') -and $rawTarget.EndsWith('>')) {
                $rawTarget = $rawTarget.Substring(1, $rawTarget.Length - 2)
            }

            if ($rawTarget -match '^(?i:https?://|mailto:)') {
                [void]$externalLinks.Add($rawTarget)
                continue
            }

            if ($rawTarget.StartsWith('#')) {
                continue
            }

            $pathOnly = $rawTarget.Split('#')[0].Split('?')[0]
            if ([string]::IsNullOrWhiteSpace($pathOnly)) {
                continue
            }

            $decoded = [Uri]::UnescapeDataString($pathOnly).Replace('/', '\')
            $candidate = [IO.Path]::GetFullPath(
                (Join-Path (Split-Path -Parent $source) $decoded))
            Assert-PathUnderRoot -Path $candidate -Root $Root -Label 'Markdown link'
            if (-not (Test-Path -LiteralPath $candidate)) {
                throw "Unresolved relative Markdown link in ${relative}: $rawTarget"
            }
        }
    }

    return @($externalLinks | Sort-Object -Unique)
}

function Extract-ArchiveSafely {
    param(
        [Parameter(Mandatory)][string] $ArchivePath,
        [Parameter(Mandatory)][string] $Destination
    )

    [void][IO.Directory]::CreateDirectory($Destination)
    $destinationRoot = [IO.Path]::GetFullPath($Destination)
    $caseNames = @{}
    $fileCount = 0
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.Name.Length -eq 0) {
                continue
            }

            $relative = Get-NormalizedZipMemberName -Name $entry.FullName
            $key = $relative.ToLowerInvariant()
            if ($caseNames.ContainsKey($key)) {
                throw "Case-insensitive duplicate ZIP member: $relative"
            }

            $caseNames[$key] = $true
            $target = Join-Path $destinationRoot ($relative.Replace('/', '\'))
            Assert-PathUnderRoot -Path $target -Root $destinationRoot -Label 'ZIP member'
            $parent = Split-Path -Parent $target
            [void][IO.Directory]::CreateDirectory($parent)
            $input = $entry.Open()
            try {
                $output = [IO.File]::Open(
                    $target,
                    [IO.FileMode]::CreateNew,
                    [IO.FileAccess]::Write,
                    [IO.FileShare]::None)
                try {
                    $input.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                }
            }
            finally {
                $input.Dispose()
            }

            $fileCount++
        }
    }
    finally {
        $archive.Dispose()
    }

    return $fileCount
}

function Get-DocumentationBlobId {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Ref,
        [Parameter(Mandatory)][string] $RelativePath
    )

    $env:GIT_OPTIONAL_LOCKS = '0'
    & git -C $Root -c core.fsmonitor=false -c diff.autoRefreshIndex=false cat-file -e (
        $Ref + ':' + $RelativePath) 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Documentation source is not present at docsRef: $RelativePath"
    }

    return (& git -C $Root -c core.fsmonitor=false -c diff.autoRefreshIndex=false rev-parse (
        $Ref + ':' + $RelativePath)).Trim()
}

function Copy-GitBlobToPath {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $BlobId,
        [Parameter(Mandatory)][string] $Destination
    )

    $parent = Split-Path -Parent $Destination
    [void][IO.Directory]::CreateDirectory($parent)
    $git = (Get-Command git.exe -ErrorAction Stop).Source
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $git
    $startInfo.Arguments = '-C "' + $Root.Replace('"', '\"') + '" cat-file blob ' + $BlobId
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Unable to start git cat-file.'
    }

    $output = [IO.File]::Open(
        $Destination,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None)
    try {
        $process.StandardOutput.BaseStream.CopyTo($output)
    }
    finally {
        $output.Dispose()
    }

    $errorText = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Unable to read documentation blob $BlobId`: $errorText"
    }
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Text
    )

    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Write-ZipFromDirectory {
    param(
        [Parameter(Mandatory)][string] $SourceDirectory,
        [Parameter(Mandatory)][string] $ArchivePath
    )

    $stream = [IO.File]::Open(
        $ArchivePath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($file in @(Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File -Force |
                    Sort-Object FullName)) {
                $relative = Get-RelativeFilePath -Root $SourceDirectory -Path $file.FullName
                $entry = $archive.CreateEntry(
                    $relative,
                    [IO.Compression.CompressionLevel]::Optimal)
                $output = $entry.Open()
                try {
                    $input = [IO.File]::OpenRead($file.FullName)
                    try {
                        $input.CopyTo($output)
                    }
                    finally {
                        $input.Dispose()
                    }
                }
                finally {
                    $output.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Validate-EnhancedArchive {
    param(
        [Parameter(Mandatory)][string] $ArchivePath,
        [Parameter(Mandatory)][object[]] $SourceInventory,
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Ref,
        [Parameter(Mandatory)][string] $TemporaryRoot
    )

    $verificationRoot = Join-Path $TemporaryRoot 'verify-bundle'
    if (Test-Path -LiteralPath $verificationRoot) {
        throw "Verification directory already exists: $verificationRoot"
    }

    [void](Extract-ArchiveSafely -ArchivePath $ArchivePath -Destination $verificationRoot)
    $bundleInventory = @(Get-FileInventory -Root $verificationRoot)
    $bundleByPath = @{}
    foreach ($member in $bundleInventory) {
        $bundleByPath[$member.path.ToLowerInvariant()] = $member
    }

    $manifestPath = Join-Path $verificationRoot $ManifestFileName
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'Documentation manifest is missing from the enhanced ZIP.'
    }

    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ($manifest.schema -ne 'tracefold-documentation-enhanced-package') {
        throw 'Documentation manifest schema is unsupported.'
    }

    if ($manifest.productSourceCommit -ne $ExpectedProductSourceCommit) {
        throw 'Documentation manifest product source is wrong.'
    }

    if ($manifest.documentationRef -ne $Ref.ToLowerInvariant()) {
        throw 'Documentation manifest docsRef is wrong.'
    }

    $expectedAdded = @($GeneratedDocumentationMembers + $DocumentationMembers)
    $expectedAddedSet = @{}
    foreach ($relative in $expectedAdded) {
        $expectedAddedSet[$relative.ToLowerInvariant()] = $true
    }

    $recordedDocumentation = @($manifest.documentationMembers)
    if ($recordedDocumentation.Count -ne $DocumentationMembers.Count) {
        throw 'Documentation manifest has an unexpected documentation member count.'
    }

    foreach ($relative in $DocumentationMembers) {
        if (-not (@($recordedDocumentation) -contains $relative)) {
            throw "Documentation manifest is missing documentation member: $relative"
        }
    }

    $referenceDocumentationRoot = Join-Path $TemporaryRoot 'referenced-documentation'
    foreach ($relative in $DocumentationMembers) {
        $blobId = Get-DocumentationBlobId -Root $RepositoryRoot -Ref $Ref -RelativePath $relative
        $referencePath = Join-Path $referenceDocumentationRoot ($relative.Replace('/', '\'))
        Copy-GitBlobToPath -Root $RepositoryRoot -BlobId $blobId -Destination $referencePath
        $referenceInventory = Get-FileInventory -Root $referenceDocumentationRoot
        $referencePathKey = $relative.ToLowerInvariant()
        $matchingReference = @($referenceInventory | Where-Object {
            $_.path.ToLowerInvariant() -eq $referencePathKey
        })
        if ($matchingReference.Count -ne 1) {
            throw "Unable to inventory referenced documentation: $relative"
        }

        $bundleKey = $relative.ToLowerInvariant()
        if (-not $bundleByPath.ContainsKey($bundleKey) -or
            $bundleByPath[$bundleKey].bytes -ne $matchingReference[0].bytes -or
            $bundleByPath[$bundleKey].sha256 -ne $matchingReference[0].sha256) {
            throw "Enhanced ZIP documentation differs from docsRef: $relative"
        }
    }

    $recordedAdded = @($manifest.addedMembers)
    if ($recordedAdded.Count -ne ($expectedAdded.Count - 1)) {
        throw 'Documentation manifest has an unexpected added-member count.'
    }

    $recordedAddedSet = @{}
    foreach ($member in $recordedAdded) {
        $key = $member.path.ToLowerInvariant()
        if ($key -eq $ManifestFileName.ToLowerInvariant() -or
            -not $expectedAddedSet.ContainsKey($key) -or
            $recordedAddedSet.ContainsKey($key)) {
            throw "Documentation manifest contains an unapproved member: $($member.path)"
        }

        if (-not $bundleByPath.ContainsKey($key)) {
            throw "Enhanced ZIP is missing recorded documentation member: $($member.path)"
        }

        $actual = $bundleByPath[$key]
        if ($actual.bytes -ne [long]$member.bytes -or $actual.sha256 -ne $member.sha256) {
            throw "Enhanced ZIP changed documentation member: $($member.path)"
        }

        $recordedAddedSet[$key] = $true
    }

    foreach ($relative in $expectedAdded) {
        if ($relative -ne $ManifestFileName -and
            -not $recordedAddedSet.ContainsKey($relative.ToLowerInvariant())) {
            throw "Documentation manifest did not record expected member: $relative"
        }
    }

    $expectedSet = @{}
    foreach ($member in $SourceInventory) {
        $expectedSet[$member.path.ToLowerInvariant()] = $true
        if (-not $bundleByPath.ContainsKey($member.path.ToLowerInvariant())) {
            throw "Enhanced ZIP is missing product member: $($member.path)"
        }

        $actual = $bundleByPath[$member.path.ToLowerInvariant()]
        if ($actual.bytes -ne $member.bytes -or $actual.sha256 -ne $member.sha256) {
            throw "Enhanced ZIP changed product member: $($member.path)"
        }
    }

    foreach ($relative in $expectedAdded) {
        $key = $relative.ToLowerInvariant()
        if ($expectedSet.ContainsKey($key)) {
            throw "Added documentation member collides with product payload: $relative"
        }

        if (-not $bundleByPath.ContainsKey($key)) {
            throw "Enhanced ZIP is missing documentation member: $relative"
        }

        $expectedSet[$key] = $true
    }

    if ($bundleInventory.Count -ne $expectedSet.Count) {
        $unexpected = @($bundleInventory | Where-Object {
            -not $expectedSet.ContainsKey($_.path.ToLowerInvariant())
        })
        throw "Enhanced ZIP has unexpected members: $($unexpected.path -join ', ')"
    }

    $recordedProductMembers = @($manifest.productMembers)
    if ($recordedProductMembers.Count -ne $SourceInventory.Count) {
        throw 'Documentation manifest product member count is wrong.'
    }

    $manifestProductByPath = @{}
    foreach ($member in $recordedProductMembers) {
        $key = $member.path.ToLowerInvariant()
        if ($manifestProductByPath.ContainsKey($key)) {
            throw "Documentation manifest has a duplicate product member: $($member.path)"
        }

        $manifestProductByPath[$key] = $member
    }

    foreach ($member in $SourceInventory) {
        $key = $member.path.ToLowerInvariant()
        if (-not $manifestProductByPath.ContainsKey($key) -or
            [long]$manifestProductByPath[$key].bytes -ne $member.bytes -or
            $manifestProductByPath[$key].sha256 -ne $member.sha256) {
            throw "Documentation manifest changed product member: $($member.path)"
        }
    }

    [void](Assert-RelativeMarkdownReferences `
        -Root $verificationRoot `
        -RelativePaths @($GeneratedDocumentationMembers + $DocumentationMembers |
            Where-Object { $_ -ne $ManifestFileName }))

    return [pscustomobject][ordered]@{
        productMemberCount = $SourceInventory.Count
        documentationMemberCount = $expectedAdded.Count
        totalMemberCount = $bundleInventory.Count
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
}

Assert-FullyQualifiedPath -Path $RepositoryRoot -Label 'RepositoryRoot'
Assert-FullyQualifiedPath -Path $SourceProductArchive -Label 'SourceProductArchive'
Assert-FullyQualifiedPath -Path $OutputDirectory -Label 'OutputDirectory'
Assert-FullyQualifiedPath -Path $WorkingDirectory -Label 'WorkingDirectory'
Assert-ExistingPathHasNoReparsePoint -Path $RepositoryRoot -Label 'RepositoryRoot'
Assert-ExistingPathHasNoReparsePoint -Path $SourceProductArchive -Label 'SourceProductArchive'
Assert-ExistingPathHasNoReparsePoint -Path (Split-Path -Parent $OutputDirectory) -Label 'OutputDirectory parent'
Assert-ExistingPathHasNoReparsePoint -Path (Split-Path -Parent $WorkingDirectory) -Label 'WorkingDirectory parent'

$sourceItem = Get-Item -LiteralPath $SourceProductArchive -Force
if ($sourceItem.Name -ne $ExpectedProductArchiveName -or
    $sourceItem.Length -ne $ExpectedProductArchiveBytes) {
    throw 'Source product archive name or size does not match the published P payload.'
}

$sourceHash = Get-FileSha256 -Path $sourceItem.FullName
if ($sourceHash -ne $ExpectedProductArchiveSha256) {
    throw "Source product archive SHA-256 mismatch: $sourceHash"
}

$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot '.cache/tmp'))
Assert-PathUnderRoot -Path $WorkingDirectory -Root $temporaryRoot -Label 'WorkingDirectory'
if (Test-Path -LiteralPath $WorkingDirectory) {
    throw 'WorkingDirectory must be a new, owned leaf.'
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if ($ValidateOnly -and -not (Test-Path -LiteralPath $outputRoot -PathType Container)) {
    throw 'ValidateOnly requires an existing OutputDirectory.'
}

if (-not $ValidateOnly -and (Test-Path -LiteralPath $outputRoot)) {
    throw 'OutputDirectory must be a new, owned leaf.'
}

if ($ValidateOnly) {
    Assert-ExistingPathHasNoReparsePoint -Path $outputRoot -Label 'OutputDirectory'
}

$env:GIT_OPTIONAL_LOCKS = '0'
& git -C $RepositoryRoot -c core.fsmonitor=false -c diff.autoRefreshIndex=false cat-file -e (
    $DocumentationRef + '^{commit}') 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'DocumentationRef does not identify a commit.'
}

& git -C $RepositoryRoot -c core.fsmonitor=false -c diff.autoRefreshIndex=false merge-base --is-ancestor (
    $DocumentationRef) HEAD
if ($LASTEXITCODE -ne 0) {
    throw 'DocumentationRef must be an ancestor of HEAD.'
}

if ($DocumentationRef -eq $ExpectedProductSourceCommit) {
    throw 'DocumentationRef must be separate from the product source commit.'
}

foreach ($relative in $DocumentationMembers) {
    [void](Get-DocumentationBlobId -Root $RepositoryRoot -Ref $DocumentationRef -RelativePath $relative)
}

Assert-NoFileDirectoryConflicts `
    -Paths @($DocumentationMembers + $GeneratedDocumentationMembers) `
    -Label 'Documentation whitelist'

[void][IO.Directory]::CreateDirectory($WorkingDirectory)
Assert-ExistingPathHasNoReparsePoint -Path $WorkingDirectory -Label 'WorkingDirectory'
$sourceExtract = Join-Path $WorkingDirectory 'source-product'
$verificationRoot = Join-Path $WorkingDirectory 'verify-bundle'
$succeeded = $false
try {
    $sourceFileCount = Extract-ArchiveSafely -ArchivePath $sourceItem.FullName -Destination $sourceExtract
    $productInventory = @(Get-FileInventory -Root $sourceExtract)
    if ($productInventory.Count -ne $ExpectedProductMemberCount) {
        throw "Source product member count mismatch: $($productInventory.Count)"
    }

    if ($productInventory.Count -ne $sourceFileCount) {
        throw 'Source product inventory count does not match extracted file count.'
    }

    $versionPath = Join-Path $sourceExtract 'package-version.json'
    if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
        throw 'Source product does not contain package-version.json.'
    }

    $versionDocument = [IO.File]::ReadAllText($versionPath) | ConvertFrom-Json
    if ([string]$versionDocument.SourceCommit -ne $ExpectedProductSourceCommit) {
        throw 'Source product version does not identify the published product source.'
    }

    if ($ValidateOnly) {
        $bundlePath = Join-Path $outputRoot $BundleFileName
        if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
            throw 'ValidateOnly bundle is missing.'
        }

        $verification = Validate-EnhancedArchive `
            -ArchivePath $bundlePath `
            -SourceInventory $productInventory `
            -RepositoryRoot $RepositoryRoot `
            -Ref $DocumentationRef `
            -TemporaryRoot $WorkingDirectory
        $bundleHash = Get-FileSha256 -Path $bundlePath
        $checksumPath = Join-Path $outputRoot $ChecksumFileName
        if (Test-Path -LiteralPath $checksumPath -PathType Leaf) {
            $actualChecksum = [IO.File]::ReadAllText($checksumPath)
            if ($actualChecksum -notmatch ('(?m)^' + [regex]::Escape($bundleHash) + '  ' +
                    [regex]::Escape($BundleFileName) + '$')) {
                throw 'Documentation-enhanced package checksum sidecar is wrong.'
            }
        }

        $publicManifestPath = Join-Path $outputRoot $PublicManifestFileName
        if (-not (Test-Path -LiteralPath $publicManifestPath -PathType Leaf)) {
            throw 'Public documentation manifest is missing.'
        }

        $publicManifest = [IO.File]::ReadAllText($publicManifestPath) | ConvertFrom-Json
        if ($publicManifest.schema -ne 'tracefold-documentation-enhanced-release-manifest' -or
            $publicManifest.releaseAsset.name -ne $BundleFileName -or
            $publicManifest.releaseAsset.bytes -ne [long](Get-Item -LiteralPath $bundlePath).Length -or
            $publicManifest.releaseAsset.sha256 -ne $bundleHash -or
            $publicManifest.originalProduct.sourceCommit -ne $ExpectedProductSourceCommit -or
            $publicManifest.documentationRef -ne $DocumentationRef.ToLowerInvariant()) {
            throw 'Public documentation manifest identity does not match the bundle.'
        }

        [pscustomobject][ordered]@{
            status = 'StaticVerified'
            bundle = $bundlePath
            sha256 = $bundleHash
            productMemberCount = $verification.productMemberCount
            documentationMemberCount = $verification.documentationMemberCount
            totalMemberCount = $verification.totalMemberCount
            productSmoke = 'NotRun'
            gui = 'NotRun'
            productExecution = 'NotRun'
        } | ConvertTo-Json
        $succeeded = $true
        return
    }

    $productCollisions = @{}
    foreach ($member in $productInventory) {
        $productCollisions[$member.path.ToLowerInvariant()] = $true
    }

    foreach ($relative in @($DocumentationMembers + $GeneratedDocumentationMembers)) {
        if ($productCollisions.ContainsKey($relative.ToLowerInvariant())) {
            throw "Documentation member collides with product payload: $relative"
        }
    }

    $documentationSources = @()
    foreach ($relative in $DocumentationMembers) {
        $blobId = Get-DocumentationBlobId -Root $RepositoryRoot -Ref $DocumentationRef -RelativePath $relative
        $destination = Join-Path $sourceExtract ($relative.Replace('/', '\'))
        Copy-GitBlobToPath -Root $RepositoryRoot -BlobId $blobId -Destination $destination
        $item = Get-Item -LiteralPath $destination -Force
        $documentationSources += [pscustomobject][ordered]@{
            path = $relative
            gitBlob = $blobId.ToLowerInvariant()
            bytes = [long]$item.Length
            sha256 = Get-FileSha256 -Path $destination
        }
    }

    $startHere = @'
TraceFold - Windows photo and video organizer
TraceFold - Windows 照片与视频整理工具

START / 开始
1. Run TraceFold.Media.exe from this fully extracted folder. Do not run it from inside the ZIP.
   请完整解压本目录后运行 TraceFold.Media.exe，不要从 ZIP 预览中运行。
2. The product UI is currently in Chinese. Open Chinese or English offline guidance:
   产品界面当前为中文。离线指南：
   docs/offline-quick-start.md
   docs/offline-quick-start.en.md
3. Use three separate, non-overlapping folders for Inbox, Library, and Work.
   在“设置 → 文件夹与扫描”配置互不包含的待入库、正式文件库和工作目录。
4. Start with copies of a few media files. A successful commit removes the corresponding
   inbox sources; committing a prepared pair also removes the source/output files.
   首次使用请保留媒体副本。成功入库会清理对应待入库源文件；配对素材入库也会清理对应源文件和处理输出。
5. GitHub links require network access. This package adds documentation only; it does not
   add a public API, CLI, MCP integration, or new runtime capability.
   GitHub 链接需要联网。本包仅增加文档，不新增公开 API、CLI、MCP 集成或运行时能力。
'@
    $docsIndex = @'
# TraceFold Documentation

## Offline package

- [简体中文离线快速指南](offline-quick-start.md)
- [English offline quick start](offline-quick-start.en.md)
- [简体中文能力矩阵](capability-matrix.md)
- [English capability matrix](capability-matrix.en.md)
- [使用指南 (Chinese)](getting-started.md)
- [Troubleshooting (Chinese)](troubleshooting.md)
- [Support](https://github.com/NNBNBYLKW/TraceFold/issues)

The package root contains the bilingual product README and
`DOCS-ENHANCED-MANIFEST.json`. Product source and documentation source are recorded
separately in that manifest. External GitHub links require network access.
'@
    $developmentIndex = @'
# TraceFold Development Documentation

This documentation-enhanced product package is for ordinary installation and offline
use. Source development, build, test and release instructions require the online
repository:

- [Online source getting-started guide](https://github.com/NNBNBYLKW/TraceFold/blob/main/docs/development/getting-started.md)
- https://github.com/NNBNBYLKW/TraceFold/tree/main/docs/development

The bundled product payload is unchanged from the published original product archive.
This package adds documentation only and does not add a public automation interface.
'@
    $developmentGettingStarted = @'
# TraceFold Source Development

Source build and test instructions require the online repository and are not part of
the ordinary offline product package:

https://github.com/NNBNBYLKW/TraceFold/blob/main/docs/development/getting-started.md

Use the product-level offline quick start for installation. The bundled product
payload is unchanged from the published original product archive.
'@
    Write-Utf8File -Path (Join-Path $sourceExtract 'docs/README.md') -Text $docsIndex
    Write-Utf8File -Path (Join-Path $sourceExtract 'docs/development/README.md') -Text $developmentIndex
    Write-Utf8File -Path (Join-Path $sourceExtract 'docs/development/getting-started.md') -Text $developmentGettingStarted
    Write-Utf8File -Path (Join-Path $sourceExtract 'START-HERE.txt') -Text $startHere

    $externalLinks = @(Assert-RelativeMarkdownReferences `
        -Root $sourceExtract `
        -RelativePaths @($GeneratedDocumentationMembers + $DocumentationMembers |
            Where-Object { $_ -ne $ManifestFileName }))

    $addedInventory = @()
    foreach ($relative in @($GeneratedDocumentationMembers + $DocumentationMembers)) {
        if ($relative -eq $ManifestFileName) {
            continue
        }

        $path = Join-Path $sourceExtract ($relative.Replace('/', '\'))
        $item = Get-Item -LiteralPath $path -Force
        $addedInventory += [pscustomobject][ordered]@{
            path = $relative
            bytes = [long]$item.Length
            sha256 = Get-FileSha256 -Path $path
        }
    }

    $manifest = [pscustomobject][ordered]@{
        schema = 'tracefold-documentation-enhanced-package'
        schemaVersion = 1
        packageRole = 'published-product-payload-with-bilingual-documentation'
        productSourceCommit = $ExpectedProductSourceCommit
        productInformationalVersion = [string]$versionDocument.InformationalVersion
        productLaunchProbe = [string]$versionDocument.ProductLaunchProbe
        sourceProductArchive = [ordered]@{
            name = $ExpectedProductArchiveName
            bytes = $ExpectedProductArchiveBytes
            sha256 = $ExpectedProductArchiveSha256
        }
        documentationRef = $DocumentationRef.ToLowerInvariant()
        documentationMembers = $DocumentationMembers
        documentationSources = $documentationSources
        generatedMembers = $GeneratedDocumentationMembers
        externalLinks = $externalLinks
        externalLinkPolicy = 'Only HTTP(S) and mailto links require network access; every relative Markdown link resolves inside this package.'
        payloadPolicy = 'All source product file members are copied byte-for-byte by their normalized relative path. No recompilation, smoke, GUI, database access, or product execution is part of this package.'
        productMembers = $productInventory
        addedMembers = $addedInventory
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 12
    Write-Utf8File -Path (Join-Path $sourceExtract $ManifestFileName) -Text ($manifestJson + "`n")

    [void][IO.Directory]::CreateDirectory($outputRoot)
    $bundlePath = Join-Path $outputRoot $BundleFileName
    $checksumPath = Join-Path $outputRoot $ChecksumFileName
    $publicManifestPath = Join-Path $outputRoot $PublicManifestFileName
    $receiptPath = Join-Path $outputRoot $PrivateReceiptFileName
    Write-ZipFromDirectory -SourceDirectory $sourceExtract -ArchivePath $bundlePath

    $verification = Validate-EnhancedArchive `
        -ArchivePath $bundlePath `
        -SourceInventory $productInventory `
        -RepositoryRoot $RepositoryRoot `
        -Ref $DocumentationRef `
        -TemporaryRoot $WorkingDirectory

    $bundleItem = Get-Item -LiteralPath $bundlePath -Force
    $bundleHash = Get-FileSha256 -Path $bundlePath
    $packageManifestHash = Get-FileSha256 -Path (Join-Path $sourceExtract $ManifestFileName)
    $publicManifest = [pscustomobject][ordered]@{
        schema = 'tracefold-documentation-enhanced-release-manifest'
        schemaVersion = 1
        releaseTarget = [ordered]@{
            tag = $ReleaseTag
            releasePageUrl = $ReleasePageUrl
            assetBaseUrl = $ReleaseDownloadBaseUrl
        }
        originalProduct = [ordered]@{
            sourceCommit = $ExpectedProductSourceCommit
            name = $ExpectedProductArchiveName
            bytes = $ExpectedProductArchiveBytes
            sha256 = $ExpectedProductArchiveSha256
        }
        documentationRef = $DocumentationRef.ToLowerInvariant()
        releaseAsset = [ordered]@{
            name = $BundleFileName
            bytes = [long]$bundleItem.Length
            sha256 = $bundleHash
            url = $ReleaseDownloadBaseUrl + $BundleFileName
        }
        publicManifestAsset = [ordered]@{
            name = $PublicManifestFileName
            url = $ReleaseDownloadBaseUrl + $PublicManifestFileName
        }
        checksumAsset = [ordered]@{
            name = $ChecksumFileName
            url = $ReleaseDownloadBaseUrl + $ChecksumFileName
        }
        memberBinding = [ordered]@{
            originalProductMemberCount = $productInventory.Count
            originalProductMembersSha256 = Get-InventoryBindingSha256 -Inventory $productInventory
            documentationSourceCount = $documentationSources.Count
            documentationSourcesSha256 = Get-InventoryBindingSha256 -Inventory $documentationSources
            embeddedManifestName = $ManifestFileName
            embeddedManifestSha256 = $packageManifestHash
        }
        scope = [ordered]@{
            payload = 'published-product-payload'
            change = 'documentation-only'
            recompilation = $false
            productSmoke = 'NotRun'
            gui = 'NotRun'
            productExecution = 'NotRun'
        }
    }
    Write-Utf8File -Path $publicManifestPath -Text (
        ($publicManifest | ConvertTo-Json -Depth 12) + "`n")
    $publicManifestHash = Get-FileSha256 -Path $publicManifestPath
    $checksumText = $bundleHash + '  ' + $BundleFileName + "`n" +
        $publicManifestHash + '  ' + $PublicManifestFileName + "`n"
    Write-Utf8File -Path $checksumPath -Text $checksumText

    $sourceHashAfter = Get-FileSha256 -Path $sourceItem.FullName
    if ($sourceHashAfter -ne $ExpectedProductArchiveSha256) {
        throw 'Source product archive changed during packaging.'
    }

    $receipt = [pscustomobject][ordered]@{
        schema = 'tracefold-documentation-enhanced-package-receipt'
        schemaVersion = 1
        status = 'StaticVerified'
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        sourceProduct = [ordered]@{
            path = $sourceItem.FullName
            name = $ExpectedProductArchiveName
            bytes = $ExpectedProductArchiveBytes
            sha256 = $ExpectedProductArchiveSha256
            sourceCommit = $ExpectedProductSourceCommit
        }
        documentation = [ordered]@{
            ref = $DocumentationRef.ToLowerInvariant()
            members = $DocumentationMembers
            sources = $documentationSources
        }
        bundle = [ordered]@{
            path = $bundlePath
            name = $BundleFileName
            bytes = [long]$bundleItem.Length
            sha256 = $bundleHash
            checksumPath = $checksumPath
            publicManifestPath = $publicManifestPath
        }
        publicationStatus = 'PendingPublication'
        verification = [ordered]@{
            mode = 'static-member-byte-comparison'
            productMemberCount = $verification.productMemberCount
            documentationMemberCount = $verification.documentationMemberCount
            totalMemberCount = $verification.totalMemberCount
            caseInsensitiveCollisionCheck = 'Passed'
            sourceProductHashStable = 'Passed'
            productMemberNamesAndBytesUnchanged = 'Passed'
            whitelistExact = 'Passed'
            relativeLinks = 'Passed'
            productSmoke = 'NotRun'
            gui = 'NotRun'
            productExecution = 'NotRun'
        }
    }
    Write-Utf8File -Path $receiptPath -Text (($receipt | ConvertTo-Json -Depth 12) + "`n")

    Write-Output ('Documentation-enhanced bundle: ' + $bundlePath)
    Write-Output ('Bundle SHA-256: ' + $bundleHash)
    Write-Output ('Checksum: ' + $checksumPath)
    Write-Output ('Public manifest: ' + $publicManifestPath)
    Write-Output ('Receipt: ' + $receiptPath)
    Write-Output (
        'Static member verification: product=' + $verification.productMemberCount +
        ' documentation=' + $verification.documentationMemberCount +
        ' total=' + $verification.totalMemberCount)
    $succeeded = $true
}
finally {
    $env:GIT_OPTIONAL_LOCKS = $previousGitOptionalLocks
    if ($succeeded -and (Test-Path -LiteralPath $WorkingDirectory)) {
        $resolvedWorking = [IO.Path]::GetFullPath($WorkingDirectory)
        Assert-PathUnderRoot -Path $resolvedWorking -Root $temporaryRoot -Label 'WorkingDirectory'
        Assert-ExistingPathHasNoReparsePoint -Path $resolvedWorking -Label 'WorkingDirectory'
        Remove-Item -LiteralPath $resolvedWorking -Recurse -Force
    }
}
