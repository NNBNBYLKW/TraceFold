# TraceFold Stage 5CC - report metadata and provenance evidence slice (RMETA-S-01 candidate).
#
# Status: static development candidate inside the RMETA-S-01 exclusive root. It has NOT been
# run: no Parser, no behavior test, no product, driver, support or UI execution happened while
# it was authored. The queued RMETA-PS-01 window owns the first real execution, and a Parser
# pass is not a claim about Windows PowerShell 5.1 semantics.
#
# Encoding: pure ASCII, no byte order mark. The N1 lesson is that a Chinese character in a
# BOM-less UTF-8 file is decoded as cp936 by Windows PowerShell 5.1 and swallows quotes; this
# file carries no non-ASCII text at all, so that failure mode cannot arise here.
#
# What this module does:
#   it produces the metadata and provenance evidence block for the EXISTING report.json fields,
#   one implementation per field family:
#     - a file digest class (bytes, sha256, state, error),
#     - an ordinal tree manifest and digest class, deterministic across runtimes and cultures,
#     - product identity classes (exe digest vs tree ordinal digest vs package zip),
#     - source provenance domains (product build commit vs script repository HEAD vs candidate SHA),
#     - driver identity classes (archive hash/version vs extracted member hash vs PE version),
#     - a fixture manifest built from the declared contract assets with independently recomputed digests,
#     - host observation (dpi/theme/viewport) from injected real observations or explicit unknown/NotRun,
#     - null-preserving, date-safe JSON reading,
#     - one assembler that fills the ten Runner-required metadata fields plus a separate
#       metadata evidence status object.
#
# What it deliberately never does:
#   * never synthesizes an observation: an unread DPI, theme or viewport stays null with a state;
#   * never coerces an unknown into 0, an empty string, or a real-looking value;
#   * never treats the product build commit, the script repository HEAD and the candidate SHA
#     as one identity domain, and never requires them to be equal;
#   * never follows a reparse point while digesting a tree;
#   * never rewrites a business status, an acceptance criterion or a report pass/fail rule;
#   * never claims redaction by itself; redaction is an explicitly injected consumer step and
#     the existing report writer applies the shared redaction patterns on its own.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

<#
.SYNOPSIS
    Internal: reads one named value from a fact object, or null.

.DESCRIPTION
    The same guard the aggregate engine uses: a dictionary is read through its indexer and an
    object through its property adapter, a missing key or property is null rather than a
    strict-mode error, and a null node is null. This keeps every consumer of a metadata fact
    independent of whether the producer returned an ordered dictionary or a PSCustomObject.
#>
function Get-UiMetadataNodeValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $Node,
        [Parameter(Mandatory)][string] $Name
    )

    if ($null -eq $Node) { return $null }

    if ($Node -is [System.Collections.IDictionary]) {
        if ($Node.Contains($Name)) { return $Node[$Name] }
        return $null
    }

    $property = $Node.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

<#
.SYNOPSIS
    Digests one regular file, or records why it could not be digested.

.DESCRIPTION
    States are 'captured', 'absent', 'refused' (reparse point) and 'error'. bytes and sha256
    stay null unless the digest was actually captured, so a missing or unread file can never
    read as a zero-byte file with a real hash.
#>
function Get-UiMetadataFileDigest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path
    )

    $fact = [ordered]@{
        path     = $Path
        bytes    = $null
        sha256   = $null
        state    = 'unknown'
        error    = $null
    }

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $fact.state = 'unknown'
        $fact.error = 'A digest path was not supplied.'
        return $fact
    }

    try {
        if (-not [IO.File]::Exists($Path)) {
            $fact.state = 'absent'
            return $fact
        }

        $attributes = [IO.File]::GetAttributes($Path)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $fact.state = 'refused'
            $fact.error = 'Reparse point refused without following it: ' + $Path
            return $fact
        }

        $item = Get-Item -LiteralPath $Path -Force
        if ($null -ne $item -and $null -ne $item.LinkType) {
            $fact.state = 'refused'
            $fact.error = 'Link target refused without following it: ' + $Path
            return $fact
        }

        $fact.bytes = [long] $item.Length

        $hasher = [Security.Cryptography.SHA256]::Create()
        try {
            $stream = [IO.File]::OpenRead($Path)
            try { $hash = $hasher.ComputeHash($stream) }
            finally { $stream.Dispose() }
        }
        finally { $hasher.Dispose() }

        $fact.sha256 = ([BitConverter]::ToString($hash)).Replace('-', '')
        $fact.state = 'captured'
    }
    catch {
        $fact.bytes = $null
        $fact.sha256 = $null
        $fact.state = 'error'
        $fact.error = $_.Exception.Message
    }

    return $fact
}

<#
.SYNOPSIS
    Digests one directory tree with a deterministic ordinal manifest.

.DESCRIPTION
    Entries are enumerated with an explicit stack, every entry is checked for the reparse
    attribute before it is opened, and subdirectories are traversed only when they are plain
    directories. The manifest line for each file is
    '<relative-path>|<bytes>|<sha256>' and the lines are joined with a line feed; the joined
    text is encoded as UTF-8 without a BOM and hashed. Files are sorted with an ordinal
    comparer, so neither the Windows PowerShell 5.1 NLS collation nor the PowerShell 7 ICU
    collation can reorder the same bytes and produce a different digest.

    States are 'Captured', 'Absent' (root missing), 'Empty' (root exists, no regular file),
    'Refused' (a reparse entry) and 'Error'. fileCount, totalBytes and treeSha256 stay null
    unless the manifest was actually captured.
#>
function Get-UiMetadataTreeDigest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Root
    )

    $fact = [ordered]@{
        root       = $Root
        state      = 'unknown'
        fileCount  = $null
        totalBytes = $null
        treeSha256 = $null
        files      = @()
        error      = $null
    }

    if ([string]::IsNullOrWhiteSpace($Root)) {
        $fact.state = 'unknown'
        $fact.error = 'A tree root was not supplied.'
        return $fact
    }

    if (-not [IO.Directory]::Exists($Root)) {
        $fact.state = 'absent'
        return $fact
    }

    try {
        $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')

        $pending = New-Object 'System.Collections.Generic.List[string]'
        $pending.Add($fullRoot)
        $collected = New-Object 'System.Collections.Generic.List[string]'
        $refused = $null

        while ($pending.Count -gt 0) {
            $current = $pending[$pending.Count - 1]
            $pending.RemoveAt($pending.Count - 1)
            foreach ($entry in @([IO.Directory]::GetFileSystemEntries($current))) {
                $attributes = [IO.File]::GetAttributes($entry)
                if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    $refused = $entry
                    break
                }
                if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                    $pending.Add($entry)
                }
                else {
                    $collected.Add($entry)
                }
            }
            if ($null -ne $refused) { break }
        }

        if ($null -ne $refused) {
            $fact.state = 'refused'
            $fact.error = 'Reparse entry refused without following it: ' + $refused
            return $fact
        }

        if ($collected.Count -eq 0) {
            $fact.state = 'empty'
            $fact.fileCount = 0
            $fact.totalBytes = 0
            return $fact
        }

        $sorted = $collected.ToArray()
        [Array]::Sort($sorted, [StringComparer]::Ordinal)

        $rows = New-Object 'System.Collections.Generic.List[object]'
        $total = [long]0
        foreach ($path in $sorted) {
            $digest = Get-UiMetadataFileDigest -Path $path
            if ($digest.state -cne 'captured') {
                $fact.state = 'error'
                $fact.error = 'A tree member could not be digested: ' + $path + ' (' + $digest.state + ')'
                return $fact
            }
            $relative = $path.Substring($fullRoot.Length).TrimStart('\', '/')
            $row = [ordered]@{
                relativePath = $relative
                bytes        = $digest.bytes
                sha256       = $digest.sha256
            }
            $rows.Add($row)
            $total = $total + [long]$digest.bytes
        }

        $builder = New-Object 'System.Text.StringBuilder'
        foreach ($row in $rows) {
            [void]$builder.Append([string]$row.relativePath).Append('|').Append([string]$row.bytes).Append('|').Append([string]$row.sha256).Append("`n")
        }

        $manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes($builder.ToString())
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $treeHash = $hasher.ComputeHash($manifestBytes) }
        finally { $hasher.Dispose() }

        $fact.state = 'captured'
        $fact.fileCount = $rows.Count
        $fact.totalBytes = $total
        $fact.treeSha256 = ([BitConverter]::ToString($treeHash)).Replace('-', '')
        $fact.files = $rows.ToArray()
    }
    catch {
        $fact.fileCount = $null
        $fact.totalBytes = $null
        $fact.treeSha256 = $null
        $fact.files = @()
        $fact.state = 'error'
        $fact.error = $_.Exception.Message
    }

    return $fact
}

<#
.SYNOPSIS
    Reads the build commit carried by a product version string, or null.

.DESCRIPTION
    A ProductVersion such as '1.0.0+04d727f7...' carries the build commit after the plus sign;
    a plain '1.0.0.0' carries nothing and must stay unknown rather than be guessed. FileVersion
    and AssemblyVersion do not identify a commit and are never substituted for one.
#>
function Read-UiMetadataBuildCommit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][AllowEmptyString()][string] $ProductVersionText
    )

    if ($null -eq $ProductVersionText) { return $null }
    if ([string]::IsNullOrWhiteSpace($ProductVersionText)) { return $null }

    $text = $ProductVersionText.Trim()
    $plus = $text.IndexOf('+')
    if ($plus -lt 0 -or $plus -eq ($text.Length - 1)) { return $null }

    $commit = $text.Substring($plus + 1).Trim()
    if ($commit -notmatch '^[0-9a-fA-F]{7,64}$') { return $null }

    return $commit
}

<#
.SYNOPSIS
    Reads the three separate source provenance domains for one run.

.DESCRIPTION
    Domains: the product build commit (from the product binary's ProductVersion), the script
    repository HEAD (from git, for the tree the running tools came from), and the candidate
    SHA-256 of the actual scenario script bytes. They are recorded side by side and are never
    required to be equal; a missing git, a missing ProductVersion or a missing candidate SHA
    keeps its own domain null with its own state and error text, never an empty string and
    never another domain's value.
#>
function Get-UiMetadataSourceProvenance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ProductExecutable,
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [AllowNull()][string] $CandidateSha256 = $null
    )

    $fact = [ordered]@{
        productBuildCommit    = $null
        productBuildCommitState = 'unknown'
        productBuildCommitError = $null
        productVersion        = $null
        scriptRepositoryHead  = $null
        scriptRepositoryHeadState = 'unknown'
        scriptRepositoryHeadError = $null
        candidateSha256       = $null
        candidateSha256State  = 'unknown'
        candidateSha256Error  = $null
        domainsSeparate       = $true
        meaning               = 'Product build commit, script repository HEAD and candidate SHA are separate identity domains and are never required to be equal.'
    }

    try {
        if (-not [IO.File]::Exists($ProductExecutable)) {
            $fact.productBuildCommitError = 'The product executable was not found, so no ProductVersion could be read.'
        }
        else {
            $versionInfo = (Get-Item -LiteralPath $ProductExecutable).VersionInfo
            $productVersion = $null
            if ($null -ne $versionInfo) { $productVersion = [string]$versionInfo.ProductVersion }
            if ([string]::IsNullOrWhiteSpace($productVersion)) {
                $fact.productBuildCommitError = 'ProductVersion was empty or absent; a build commit cannot be derived and is not guessed.'
            }
            else {
                $fact.productVersion = $productVersion
                $commit = Read-UiMetadataBuildCommit -ProductVersionText $productVersion
                if ($null -eq $commit) {
                    $fact.productBuildCommitError = 'ProductVersion does not carry a build commit: ' + $productVersion
                }
                else {
                    $fact.productBuildCommit = $commit
                    $fact.productBuildCommitState = 'captured'
                }
            }
        }
    }
    catch {
        $fact.productBuildCommitState = 'error'
        $fact.productBuildCommitError = $_.Exception.Message
    }

    if ([string]::IsNullOrWhiteSpace($RepositoryRoot) -or -not [IO.Directory]::Exists($RepositoryRoot)) {
        $fact.scriptRepositoryHeadError = 'The script repository root was not supplied or does not exist.'
    }
    else {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $raw = & git -C $RepositoryRoot rev-parse HEAD 2>$null
            if ($LASTEXITCODE -eq 0) { $fact.scriptRepositoryHead = ([string]($raw | Out-String)).Trim() }
            if ([string]::IsNullOrWhiteSpace($fact.scriptRepositoryHead)) {
                $fact.scriptRepositoryHeadError = 'git did not answer a commit for this repository root.'
            }
            else {
                $fact.scriptRepositoryHeadState = 'captured'
            }
        }
        catch {
            $fact.scriptRepositoryHeadError = $_.Exception.Message
        }
        finally {
            $ErrorActionPreference = $previous
        }
    }

    if ([string]::IsNullOrWhiteSpace($CandidateSha256)) {
        $fact.candidateSha256Error = 'No candidate SHA-256 was supplied for the actual scenario script bytes.'
        $fact.candidateSha256State = 'not-supplied'
    }
    else {
        $fact.candidateSha256 = $CandidateSha256
        $fact.candidateSha256State = 'captured'
    }

    return $fact
}

<#
.SYNOPSIS
    Reads the product identity classes for one run.

.DESCRIPTION
    Classes are kept apart on purpose: the executable file digest (the legacy productHash
    meaning), the product directory tree ordinal digest, and the Task 14 package zip digest.
    The package class stays 'not-generated' until a real package exists; it is never filled
    with the exe digest or the tree digest to make the field look answered.
#>
function Get-UiMetadataProductIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ProductExecutable,
        [Parameter(Mandatory)][string] $ProductDirectory,
        [AllowNull()][string] $PackageZipPath = $null
    )

    $fact = [ordered]@{
        executableDigest = $null
        treeDigest       = $null
        packageZipDigest = $null
        packageZipState  = 'not-generated'
        fileVersion      = $null
        fileVersionState = 'unknown'
        productVersion   = $null
        productVersionState = 'unknown'
        buildCommit      = $null
        classes          = [ordered]@{
            executableFileSha256 = 'legacy productHash class; a single exe file digest'
            treeOrdinalSha256   = 'ordinal manifest digest over the whole product directory'
            packageZipSha256    = 'Task 14 package zip digest; not generated yet'
        }
        errors           = @()
    }

    try {
        $fact.executableDigest = Get-UiMetadataFileDigest -Path $ProductExecutable
        if ($fact.executableDigest.state -cne 'captured') {
            $fact.errors += ,([ordered]@{ stage='executable-digest'; message = $fact.executableDigest.error })
        }
    }
    catch {
        $fact.executableDigest = [ordered]@{ path = $ProductExecutable; bytes = $null; sha256 = $null; state = 'error'; error = $_.Exception.Message }
        $fact.errors += ,([ordered]@{ stage='executable-digest'; message = $_.Exception.Message })
    }

    try {
        $fact.treeDigest = Get-UiMetadataTreeDigest -Root $ProductDirectory
    }
    catch {
        $fact.treeDigest = [ordered]@{ root = $ProductDirectory; state = 'error'; error = $_.Exception.Message }
        $fact.errors += ,([ordered]@{ stage='tree-digest'; message = $_.Exception.Message })
    }

    if ([string]::IsNullOrWhiteSpace($PackageZipPath)) {
        $fact.packageZipState = 'not-generated'
    }
    else {
        try {
            $fact.packageZipDigest = Get-UiMetadataFileDigest -Path $PackageZipPath
            if ($fact.packageZipDigest.state -cne 'captured') { $fact.packageZipState = $fact.packageZipDigest.state }
            else { $fact.packageZipState = 'captured' }
        }
        catch {
            $fact.packageZipState = 'error'
            $fact.errors += ,([ordered]@{ stage='package-zip-digest'; message = $_.Exception.Message })
        }
    }

    try {
        if (-not [IO.File]::Exists($ProductExecutable)) {
            $fact.fileVersionState = 'absent'
        }
        else {
            $versionInfo = (Get-Item -LiteralPath $ProductExecutable).VersionInfo
            if ($null -eq $versionInfo) {
                $fact.fileVersionState = 'unknown'
            }
            else {
                $fileVersion = [string]$versionInfo.FileVersion
                $productVersion = [string]$versionInfo.ProductVersion
                if ([string]::IsNullOrWhiteSpace($fileVersion)) {
                    $fact.fileVersionState = 'unknown'
                }
                else {
                    $fact.fileVersion = $fileVersion
                    $fact.fileVersionState = 'captured'
                }
                if ([string]::IsNullOrWhiteSpace($productVersion)) {
                    $fact.productVersionState = 'unknown'
                }
                else {
                    $fact.productVersion = $productVersion
                    $fact.productVersionState = 'captured'
                    $commit = Read-UiMetadataBuildCommit -ProductVersionText $productVersion
                    if ($null -ne $commit) { $fact.buildCommit = $commit }
                }
            }
        }
    }
    catch {
        $fact.errors += ,([ordered]@{ stage='version-info'; message = $_.Exception.Message })
    }

    return $fact
}

<#
.SYNOPSIS
    Reads the driver identity classes for one run.

.DESCRIPTION
    The archive class (manifest version, manifest sha256, archive bytes) and the extracted
    member class (winapp.exe bytes, sha256, PE FileVersion, and the archive member digest)
    stay apart: the manifest version is the released tool version and the PE FileVersion is a
    different fact, so '0.5.0' and '0.5.0.1' never collapse into one field. matchesArchiveMember
    and matchesManifest are recorded as booleans only when both sides could be read.
#>
function Get-UiMetadataDriverIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ArchivePath,
        [Parameter(Mandatory)][string] $ExtractedExePath,
        [AllowNull()][string] $ManifestVersion = $null,
        [AllowNull()][string] $ManifestSha256 = $null,
        [AllowNull()][long] $ManifestArchiveSize = $null,
        [string] $MemberName = 'winapp.exe'
    )

    $fact = [ordered]@{
        archiveDigest      = $null
        manifestVersion    = $null
        manifestSha256     = $null
        manifestArchiveSize = $null
        extractedExeDigest = $null
        archiveMemberDigest = $null
        archiveMemberName  = $MemberName
        peFileVersion      = $null
        peFileVersionState = 'unknown'
        matchesManifest    = $null
        matchesArchiveMember = $null
        errors             = @()
    }

    try {
        $fact.archiveDigest = Get-UiMetadataFileDigest -Path $ArchivePath
        if ($fact.archiveDigest.state -cne 'captured') {
            $fact.errors += ,([ordered]@{ stage='archive-digest'; message = $fact.archiveDigest.error })
        }
    }
    catch {
        $fact.errors += ,([ordered]@{ stage='archive-digest'; message = $_.Exception.Message })
    }

    try {
        $fact.extractedExeDigest = Get-UiMetadataFileDigest -Path $ExtractedExePath
        if ($fact.extractedExeDigest.state -cne 'captured') {
            $fact.errors += ,([ordered]@{ stage='extracted-exe-digest'; message = $fact.extractedExeDigest.error })
        }
    }
    catch {
        $fact.errors += ,([ordered]@{ stage='extracted-exe-digest'; message = $_.Exception.Message })
    }

    try {
        if (-not [string]::IsNullOrWhiteSpace($ManifestVersion)) { $fact.manifestVersion = $ManifestVersion }
        if (-not [string]::IsNullOrWhiteSpace($ManifestSha256)) { $fact.manifestSha256 = $ManifestSha256 }
        if ($null -ne $ManifestArchiveSize) { $fact.manifestArchiveSize = [long]$ManifestArchiveSize }
    }
    catch {
        $fact.errors += ,([ordered]@{ stage='manifest-values'; message = $_.Exception.Message })
    }

    try {
        if (-not [IO.File]::Exists($ExtractedExePath)) {
            $fact.peFileVersionState = 'absent'
        }
        else {
            $versionInfo = (Get-Item -LiteralPath $ExtractedExePath).VersionInfo
            $peVersion = $null
            if ($null -ne $versionInfo) { $peVersion = [string]$versionInfo.FileVersion }
            if ([string]::IsNullOrWhiteSpace($peVersion)) {
                $fact.peFileVersionState = 'unknown'
            }
            else {
                $fact.peFileVersion = $peVersion
                $fact.peFileVersionState = 'captured'
            }
        }
    }
    catch {
        $fact.errors += ,([ordered]@{ stage='pe-version'; message = $_.Exception.Message })
    }

    try {
        $null = Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop
    }
    catch {
        # Already loaded, or a host where the type resolves without the explicit load.
    }
    try {
        $null = Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
    }
    catch {
        # Windows PowerShell needs the compression assemblies before ZipFile resolves; a
        # second load of the same assembly is harmless.
    }

    try {
        if (-not [IO.File]::Exists($ArchivePath)) {
            $fact.errors += ,([ordered]@{ stage='archive-member'; message = 'The driver archive was not found, so no member digest could be read.' })
        }
        else {
            $zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
            try {
                $members = @($zip.Entries | Where-Object { $_.FullName -ceq $MemberName })
                if ($members.Count -ne 1) {
                    $fact.errors += ,([ordered]@{ stage='archive-member'; message = ('The archive carries ' + $members.Count + ' ' + $MemberName + ' members.') })
                }
                else {
                    $memberStream = $members[0].Open()
                    try {
                        $buffer = New-Object IO.MemoryStream
                        try { $memberStream.CopyTo($buffer); $memberBytes = $buffer.ToArray() }
                        finally { $buffer.Dispose() }
                    }
                    finally { $memberStream.Dispose() }

                    $hasher = [Security.Cryptography.SHA256]::Create()
                    try { $memberHash = ([BitConverter]::ToString($hasher.ComputeHash($memberBytes))).Replace('-', '') }
                    finally { $hasher.Dispose() }

                    $fact.archiveMemberDigest = [ordered]@{
                        memberName = $MemberName
                        bytes      = [long]$memberBytes.Length
                        sha256     = $memberHash
                        state      = 'captured'
                        error      = $null
                    }

                    if ($fact.extractedExeDigest.state -cne 'captured') {
                        $fact.matchesArchiveMember = $null
                    }
                    else {
                        $fact.matchesArchiveMember = [bool](
                            $fact.extractedExeDigest.sha256 -ceq $memberHash -and
                            $fact.extractedExeDigest.bytes -eq [long]$memberBytes.Length
                        )
                    }
                }
            }
            finally { $zip.Dispose() }
        }
    }
    catch {
        $fact.errors += ,([ordered]@{ stage='archive-member'; message = $_.Exception.Message })
    }

    if ($fact.archiveDigest.state -cne 'captured' -or
        [string]::IsNullOrWhiteSpace($fact.manifestSha256) -or
        $null -eq $fact.manifestArchiveSize) {
        $fact.matchesManifest = $null
    }
    else {
        $fact.matchesManifest = [bool](
            $fact.archiveDigest.sha256 -ceq $fact.manifestSha256 -and
            $fact.archiveDigest.bytes -eq [long]$fact.manifestArchiveSize
        )
    }

    return $fact
}

<#
.SYNOPSIS
    Builds the fixture manifest from the declared contract assets.

.DESCRIPTION
    Every declared fixture asset is re-digested from its repository path at read time and
    compared with the declared bytes and sha256; a declared hash is never copied into the
    report without an independent recomputation, and a missing or changed fixture keeps its
    own row with its own state instead of being dropped.
#>
function Get-UiMetadataFixtureManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $DeclaredAssets,
        [Parameter(Mandatory)][string] $RepositoryRoot
    )

    $fact = [ordered]@{
        repositoryRoot = $RepositoryRoot
        state         = 'unknown'
        assetCount    = 0
        verifiedCount = 0
        assets        = @()
        errors        = @()
    }

    $assets = @($DeclaredAssets)
    $fact.assetCount = $assets.Count
    if ($assets.Count -eq 0) {
        $fact.state = 'empty'
        return $fact
    }

    $rows = New-Object 'System.Collections.Generic.List[object]'
    $verified = 0

    foreach ($asset in $assets) {
        $row = [ordered]@{
            fixedFilename = $null
            repositoryPath = $null
            declaredBytes = $null
            declaredSha256 = $null
            actualBytes    = $null
            actualSha256   = $null
            state          = 'unknown'
            error          = $null
        }
        try {
            $row.fixedFilename = [string]$asset.fixedFilename
            $row.repositoryPath = [string]$asset.repositoryPath
            if ($null -eq $asset.PSObject.Properties['sizeBytes'] -or $null -eq $asset.sizeBytes) {
                $row.state = 'error'
                $row.error = 'A declared fixture asset has no size.'
            }
            elseif ([string]::IsNullOrWhiteSpace([string]$asset.sha256)) {
                $row.state = 'error'
                $row.error = 'A declared fixture asset has no declared hash.'
            }
            else {
                $row.declaredBytes = [long]$asset.sizeBytes
                $row.declaredSha256 = ([string]$asset.sha256).ToUpperInvariant()
                if ([string]::IsNullOrWhiteSpace($row.repositoryPath)) {
                    $row.state = 'error'
                    $row.error = 'A declared fixture asset has no repository path.'
                }
                else {
                    $source = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $row.repositoryPath))
                    $boundary = $RepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
                    if (-not $source.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
                        $row.state = 'refused'
                        $row.error = 'A declared fixture path escaped the repository root.'
                    }
                    else {
                        $digest = Get-UiMetadataFileDigest -Path $source
                        if ($digest.state -cne 'captured') {
                            $row.state = $digest.state
                            $row.error = $digest.error
                        }
                        else {
                            $row.actualBytes = $digest.bytes
                            $row.actualSha256 = $digest.sha256
                            if ($digest.bytes -ne $row.declaredBytes -or $digest.sha256 -cne $row.declaredSha256) {
                                $row.state = 'contradictory'
                                $row.error = 'The recomputed fixture digest does not match the declared digest.'
                            }
                            else {
                                $row.state = 'captured'
                                $verified = $verified + 1
                            }
                        }
                    }
                }
            }
        }
        catch {
            $row.state = 'error'
            $row.error = $_.Exception.Message
        }
        $rows.Add($row)
    }

    $fact.verifiedCount = $verified
    $fact.assets = $rows.ToArray()
    if ($verified -eq $rows.Count) { $fact.state = 'captured' }
    elseif ($verified -eq 0) { $fact.state = 'unverified' }
    else { $fact.state = 'partial' }

    return $fact
}

<#
.SYNOPSIS
    Records host observation from injected real observation delegates, or explicit unknowns.

.DESCRIPTION
    DPI, theme and viewport are reported facts about a real, identified, time-stamped window
    observation, so this module never measures or synthesizes them. Without a supplied reader
    the values stay null with a state, and the previous hardcoded 96 / 'unknown' / 0x0 is
    never reintroduced. A reader that throws records its own error state without aborting the
    metadata block.
#>
function Get-UiMetadataHostObservation {
    [CmdletBinding()]
    param(
        [AllowNull()][scriptblock] $DpiReader = $null,
        [AllowNull()][scriptblock] $ThemeReader = $null,
        [AllowNull()][scriptblock] $ViewportReader = $null
    )

    $fact = [ordered]@{
        dpi            = $null
        dpiState       = 'unknown'
        dpiError       = $null
        theme          = $null
        themeState     = 'unknown'
        themeError     = $null
        viewport       = $null
        viewportState  = 'not-run'
        viewportError  = $null
        meaning        = 'Without a real identity- and time-stamped window observation these values stay unknown/NotRun; nothing here synthesizes 96, a theme name or a 0x0 viewport.'
    }

    if ($null -ne $DpiReader) {
        try {
            $value = & $DpiReader
            if ($null -eq $value) {
                $fact.dpiState = 'unknown'
                $fact.dpiError = 'The DPI reader returned no value.'
            }
            else {
                $fact.dpi = $value
                $fact.dpiState = 'captured'
            }
        }
        catch {
            $fact.dpiState = 'error'
            $fact.dpiError = $_.Exception.Message
        }
    }

    if ($null -ne $ThemeReader) {
        try {
            $value = & $ThemeReader
            if ($null -eq $value) {
                $fact.themeState = 'unknown'
                $fact.themeError = 'The theme reader returned no value.'
            }
            else {
                $fact.theme = $value
                $fact.themeState = 'captured'
            }
        }
        catch {
            $fact.themeState = 'error'
            $fact.themeError = $_.Exception.Message
        }
    }

    if ($null -ne $ViewportReader) {
        try {
            $value = & $ViewportReader
            if ($null -eq $value) {
                $fact.viewportState = 'unknown'
                $fact.viewportError = 'The viewport reader returned no value.'
            }
            else {
                $fact.viewport = $value
                $fact.viewportState = 'captured'
            }
        }
        catch {
            $fact.viewportState = 'error'
            $fact.viewportError = $_.Exception.Message
        }
    }

    return $fact
}

<#
.SYNOPSIS
    Reads one JSON document as verbatim text and as a value, with a host capability guard.

.DESCRIPTION
    The text is read explicitly as UTF-8 and stays the authoritative source for every date
    field: it is never rewritten and keeps the original offset and fractional seconds. The
    parsed value is produced with a host capability guard so both fixed hosts end up with
    the same shape: when the host's ConvertFrom-Json exposes a JsonDateKind enum with a
    String member, the parse passes -DateKind String and every ISO 8601 string stays text;
    when it does not (Windows PowerShell 5.1 has no DateKind parameter at all) the parse
    passes nothing and that host keeps ISO strings as text by itself. The applied strategy
    is recorded in the fact, so a host that materialized dates could never be mistaken for
    one that preserved them. Date fields are consumed through Read-UiMetadataUtcMoment,
    which normalizes typed or textual values by invariant rules and returns null instead
    of a guess.
#>
function Read-UiMetadataJsonDocument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path
    )

    $fact = [ordered]@{
        path  = $Path
        state = 'unknown'
        text  = $null
        value = $null
        error = $null
        dateReadStrategy = 'unknown'
    }

    try {
        if (-not [IO.File]::Exists($Path)) {
            $fact.state = 'absent'
            return $fact
        }

        $text = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
        $fact.text = $text

        $convert = Get-Command ConvertFrom-Json -ErrorAction SilentlyContinue
        $strategy = 'unknown'
        if ($null -ne $convert -and $convert.Parameters.ContainsKey('DateKind')) {
            $kindType = $convert.Parameters['DateKind'].ParameterType
            if ($kindType.IsEnum -and [Enum]::GetNames($kindType) -contains 'String') {
                $strategy = 'iso-text-preserved-by-date-kind-string'
            }
            else {
                $strategy = 'date-kind-without-string-member'
            }
        }
        else {
            $strategy = 'no-date-kind-parameter'
        }

        if ($strategy -ceq 'iso-text-preserved-by-date-kind-string') {
            $fact.value = ($text | ConvertFrom-Json -DateKind String)
        }
        else {
            $fact.value = ($text | ConvertFrom-Json)
        }
        $fact.dateReadStrategy = $strategy
        $fact.state = 'captured'
    }
    catch {
        $fact.state = 'error'
        $fact.error = $_.Exception.Message
    }

    return $fact
}

<#
.SYNOPSIS
    Normalizes one typed or textual moment to a UTC instant, or null when it cannot.

.DESCRIPTION
    Only values that carry their own time meaning are normalized. A DateTimeOffset uses its
    own offset. A DateTime is resolved from its own Kind: Utc stays UTC and Local resolves
    through the host offset, which is a property of the typed value itself rather than a
    guess about an ambiguous text. A DateTime of Unspecified kind stays unknown, and so does
    text that does not name its zone: an explicit offset or a trailing Z is required, and
    anything else returns null instead of being assumed UTC. No field in the Runner contract
    or in the metadata block carries a documented "Unspecified means UTC" convention, so
    nothing here needs one; if such a convention were ever established for one specific
    field it would have to be annotated at that field and tested there, not assumed here.
    Parsing uses the invariant culture, so the host display culture and host time zone
    display cannot change the result. The original offset and fractional seconds remain
    visible in the raw text, which this module never rewrites.
#>
function Read-UiMetadataUtcMoment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $Value
    )

    if ($null -eq $Value) { return $null }

    if ($Value -is [System.DateTimeOffset]) {
        return ([DateTimeOffset]$Value).ToUniversalTime()
    }

    if ($Value -is [System.DateTime]) {
        $stamp = [DateTime]$Value
        if ($stamp.Kind -ceq [DateTimeKind]::Local) {
            return ([DateTimeOffset]::new($stamp)).ToUniversalTime()
        }
        if ($stamp.Kind -ceq [DateTimeKind]::Utc) {
            return ([DateTimeOffset]::new($stamp, [TimeSpan]::Zero))
        }
        return $null
    }

    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }

    # A moment must name its zone: an explicit offset or a trailing Z. Text that does not
    # cannot be resolved without a host guess, so it stays unknown (null).
    if ($text -notmatch '(?:[Zz]|[+-]\d{2}:?\d{2})\s*$') {
        return $null
    }

    $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    $parsed = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse($text, [Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

<#
.SYNOPSIS
    Assembles the report metadata block for the existing Runner contract.

.DESCRIPTION
    The block carries the ten Runner-required keys with null-preserving, type-preserving
    values, plus a separate metadataEvidence object holding every state, error and class note.
    An unestablished value is null beside its state; it is never 0, an empty string, or a
    real-looking placeholder. The optional redactor is applied to evidence strings only; the
    shared report writer applies the established redaction patterns on its own, so this
    module never claims that it performed the final redaction.
#>
function New-UiMetadataReportBlock {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $SourceProvenance,
        [Parameter(Mandatory)] $ProductIdentity,
        [Parameter(Mandatory)] $DriverIdentity,
        [Parameter(Mandatory)] $FixtureManifest,
        [Parameter(Mandatory)] $HostObservation,
        [Parameter(Mandatory)][AllowEmptyString()] $HostOs,
        [AllowNull()][scriptblock] $Redactor = $null
    )

    function Protect-UiMetadataText {
        param([AllowNull()] $Value)
        if ($null -eq $Value) { return $null }
        if ($null -eq $Redactor) { return $Value }
        if ($Value -isnot [string]) { return $Value }
        return [string](& $Redactor $Value)
    }

    $fixtureRows = @()
    $fixtureAssets = @(Get-UiMetadataNodeValue -Node $FixtureManifest -Name 'assets')
    foreach ($asset in @($fixtureAssets)) {
        $fixtureRows += ,([ordered]@{
            fixedFilename  = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $asset -Name 'fixedFilename'))
            repositoryPath = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $asset -Name 'repositoryPath'))
            declaredBytes  = (Get-UiMetadataNodeValue -Node $asset -Name 'declaredBytes')
            declaredSha256 = (Get-UiMetadataNodeValue -Node $asset -Name 'declaredSha256')
            actualBytes    = (Get-UiMetadataNodeValue -Node $asset -Name 'actualBytes')
            actualSha256   = (Get-UiMetadataNodeValue -Node $asset -Name 'actualSha256')
            state          = (Get-UiMetadataNodeValue -Node $asset -Name 'state')
            error          = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $asset -Name 'error'))
        })
    }

    $exeDigest = Get-UiMetadataNodeValue -Node $ProductIdentity -Name 'executableDigest'
    $treeDigest = Get-UiMetadataNodeValue -Node $ProductIdentity -Name 'treeDigest'
    $packageZipDigest = Get-UiMetadataNodeValue -Node $ProductIdentity -Name 'packageZipDigest'
    $archiveDigest = Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'archiveDigest'
    $extractedExeDigest = Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'extractedExeDigest'
    $archiveMemberDigest = Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'archiveMemberDigest'

    $block = [ordered]@{
        sourceCommit   = $null
        productHash    = $null
        productVersion = $null
        driverVersion  = $null
        driverHash     = $null
        fixtureHashes  = $fixtureRows
        hostOs         = (Protect-UiMetadataText $HostOs)
        dpi            = $null
        theme          = $null
        viewport       = $null
        metadataEvidence = [ordered]@{
            generatedUtc = [DateTime]::UtcNow.ToString('o')
            timebase     = 'metadata evidence block; times are UTC ISO text, not a scene clock'
            sourceCommitDomain = [ordered]@{
                field   = 'sourceCommit'
                state   = (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'scriptRepositoryHeadState')
                error   = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'scriptRepositoryHeadError'))
                meaning = 'The script repository HEAD of the running tools; a null value with a state is an unknown, never an empty answer.'
            }
            provenance = [ordered]@{
                productBuildCommit      = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'productBuildCommit'))
                productBuildCommitState = (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'productBuildCommitState')
                productBuildCommitError = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'productBuildCommitError'))
                productVersion          = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'productVersion'))
                candidateSha256         = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'candidateSha256'))
                candidateSha256State    = (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'candidateSha256State')
                domainsSeparate         = $true
                meaning                 = 'Product build commit, script repository HEAD and candidate SHA are separate identity domains and are never required to be equal.'
            }
            productClasses = [ordered]@{
                executableFileSha256 = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $exeDigest -Name 'sha256'))
                executableFileState  = (Get-UiMetadataNodeValue -Node $exeDigest -Name 'state')
                executableBytes      = (Get-UiMetadataNodeValue -Node $exeDigest -Name 'bytes')
                executableError      = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $exeDigest -Name 'error'))
                treeOrdinalSha256    = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $treeDigest -Name 'treeSha256'))
                treeState            = (Get-UiMetadataNodeValue -Node $treeDigest -Name 'state')
                treeFileCount        = (Get-UiMetadataNodeValue -Node $treeDigest -Name 'fileCount')
                packageZipSha256     = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $packageZipDigest -Name 'sha256'))
                packageZipState      = (Get-UiMetadataNodeValue -Node $ProductIdentity -Name 'packageZipState')
                fileVersion          = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $ProductIdentity -Name 'fileVersion'))
                fileVersionState     = (Get-UiMetadataNodeValue -Node $ProductIdentity -Name 'fileVersionState')
                classes              = 'exe digest, tree ordinal digest and Task 14 package zip are separate classes and are never substituted for one another.'
            }
            driverClasses = [ordered]@{
                archiveSha256        = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $archiveDigest -Name 'sha256'))
                archiveState         = (Get-UiMetadataNodeValue -Node $archiveDigest -Name 'state')
                archiveBytes         = (Get-UiMetadataNodeValue -Node $archiveDigest -Name 'bytes')
                manifestVersion      = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'manifestVersion'))
                manifestSha256       = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'manifestSha256'))
                extractedExeSha256   = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $extractedExeDigest -Name 'sha256'))
                extractedExeState    = (Get-UiMetadataNodeValue -Node $extractedExeDigest -Name 'state')
                archiveMemberSha256  = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $archiveMemberDigest -Name 'sha256'))
                archiveMemberState   = (Get-UiMetadataNodeValue -Node $archiveMemberDigest -Name 'state')
                peFileVersion        = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'peFileVersion'))
                peFileVersionState   = (Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'peFileVersionState')
                matchesManifest      = (Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'matchesManifest')
                matchesArchiveMember = (Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'matchesArchiveMember')
                classes              = 'Archive version/hash and extracted member hash and PE version are separate facts; 0.5.0 and 0.5.0.1 do not collapse.'
            }
            fixtureManifest = [ordered]@{
                state         = (Get-UiMetadataNodeValue -Node $FixtureManifest -Name 'state')
                assetCount    = (Get-UiMetadataNodeValue -Node $FixtureManifest -Name 'assetCount')
                verifiedCount = (Get-UiMetadataNodeValue -Node $FixtureManifest -Name 'verifiedCount')
                errors        = @(@(Get-UiMetadataNodeValue -Node $FixtureManifest -Name 'errors') | ForEach-Object { (Protect-UiMetadataText $_) })
                meaning       = 'Fixture hashes are independently recomputed at read time; a declared hash alone is never reported as evidence.'
            }
            hostObservation = [ordered]@{
                dpi           = (Get-UiMetadataNodeValue -Node $HostObservation -Name 'dpi')
                dpiState      = (Get-UiMetadataNodeValue -Node $HostObservation -Name 'dpiState')
                dpiError      = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $HostObservation -Name 'dpiError'))
                theme         = (Get-UiMetadataNodeValue -Node $HostObservation -Name 'theme')
                themeState    = (Get-UiMetadataNodeValue -Node $HostObservation -Name 'themeState')
                themeError    = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $HostObservation -Name 'themeError'))
                viewport      = (Get-UiMetadataNodeValue -Node $HostObservation -Name 'viewport')
                viewportState = (Get-UiMetadataNodeValue -Node $HostObservation -Name 'viewportState')
                viewportError = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $HostObservation -Name 'viewportError'))
                meaning       = 'Without a real identity- and time-stamped window observation these values stay unknown/NotRun; nothing here synthesizes 96, a theme name or a 0x0 viewport.'
            }
            businessSeparation = [ordered]@{
                meaning = 'This block records metadata evidence only. It never changes a business Passed/Failed/Blocked/NotRun status and never adds or relaxes an acceptance criterion.'
            }
            redaction = [ordered]@{
                state   = $(if ($null -eq $Redactor) { 'not-applied-here' } else { 'applied-to-evidence-strings' })
                meaning = 'Evidence strings pass through the injected redactor when one is supplied; the shared report writer applies the established redaction patterns itself, so no field here claims to be the final redaction.'
            }
        }
    }

    $block.sourceCommit = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'scriptRepositoryHead'))
    $block.productHash = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $exeDigest -Name 'sha256'))
    $block.productVersion = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $SourceProvenance -Name 'productVersion'))
    $block.driverVersion = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'manifestVersion'))
    $block.driverHash = (Protect-UiMetadataText (Get-UiMetadataNodeValue -Node $DriverIdentity -Name 'manifestSha256'))
    $block.dpi = (Get-UiMetadataNodeValue -Node $HostObservation -Name 'dpi')
    $block.theme = (Get-UiMetadataNodeValue -Node $HostObservation -Name 'theme')
    $block.viewport = (Get-UiMetadataNodeValue -Node $HostObservation -Name 'viewport')

    return $block
}

Export-ModuleMember -Function @(
    'Get-UiMetadataFileDigest',
    'Get-UiMetadataTreeDigest',
    'Read-UiMetadataBuildCommit',
    'Get-UiMetadataSourceProvenance',
    'Get-UiMetadataProductIdentity',
    'Get-UiMetadataDriverIdentity',
    'Get-UiMetadataFixtureManifest',
    'Get-UiMetadataHostObservation',
    'Read-UiMetadataJsonDocument',
    'Read-UiMetadataUtcMoment',
    'New-UiMetadataReportBlock'
)
