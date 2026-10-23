# TraceFold Stage 5CC - report metadata evidence slice tests (RMETA-R2 corrected companion).
#
# Status: corrected inside the RMETA-R2 exclusive root under card RMETA-S-02 / RMETA-PS-01.
# This file is executed for real on the fixed PS7 host and then the fixed PS5.1 host; per-run
# raw evidence lives in the run records of that window, not in this file. A Parser pass is not
# a claim about Windows PowerShell 5.1 semantics.
#
# RMETA-R2 corrections over the RMETA-S-01 candidate (kept as failures where they were real):
#   M-NULL  the report block no longer casts before Protect-UiMetadataText, so a missing value
#           stays null beside its state instead of collapsing to an empty string;
#   M-DATE  Read-UiMetadataJsonDocument guards the host capability and keeps ISO original text
#           on both fixed hosts, and text without a zone or an Unspecified stamp is unknown
#           (null) rather than assumed UTC;
#   M-TEST  M11's typed value carries the same 9003395 fractional ticks as the raw text, the
#           read-only $host automatic variable is no longer shadowed, the redaction state is
#           read structurally, and HostOs travels through the injected redactor;
#   M-FIXTURE every object this suite creates is registered, junction links are removed by
#           their exact path without following the target, deliberately retained evidence is
#           separated from residue, and cleanup is proven by a per-object readback.
#
# Encoding: pure ASCII, no byte order mark, so the N1 failure mode cannot arise here.
#
# What this suite drives:
#   the actual helper module text through Import-Module, against real controlled file I/O in a
#   registered test-owned root. It covers missing files, locked reads, reparse refusal, byte
#   and rename change detection, creation-order and culture invariance, the three separate
#   provenance domains, product/driver/fixture identity classes, host observation unknowns,
#   date-safe JSON reading, and the assembled report block null semantics.
#
# What it deliberately never does:
#   * never starts the product, the driver, the support tool, a real registry, theme or UI;
#   * never fabricates a report as a real-chain connection: the suite asserts helper behavior
#     and block shape only;
#   * never treats a synthetic stub as a real observation: stubs are labelled and the cases
#     assert the unknown states beside them;
#   * never deletes anything outside its own registered fixtures.

# Entry modes (RMETA-REG-02, static addition; the 13 case bodies are unchanged):
#   standalone : -RunRoot and -OutputDirectory given. The 13 cases run here, in declaration
#                order, into that exact output directory, and summary.json is written. This is
#                the entry every earlier result used, and it stays available.
#   registered : the same file dot-sourced by scripts/test-stage5cc-ui-acceptance-toolkit.ps1,
#                which then calls Get-TestCases. Loading this file defines the case table and
#                runs nothing: no case body, no fixture, no module import. Each case body then
#                imports the helper and creates its own fresh attempt directory when invoked.
# Both modes execute the same scriptblocks through Invoke-MetadataCaseById; the case text is
# never duplicated. A registered case that fails keeps its attempt directory as evidence; a
# passing case proves the attempt directory gone before it returns.
param(
    [string] $RunRoot = '',
    [string] $OutputDirectory = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:standaloneRun = (-not [string]::IsNullOrWhiteSpace($RunRoot)) -and
    (-not [string]::IsNullOrWhiteSpace($OutputDirectory))
$script:outputRoot = ''
$script:metadataInitialized = $false
$script:caseTable = New-Object 'System.Collections.Generic.List[object]'
$script:dispatchFacts = New-Object 'System.Collections.Generic.List[object]'
$script:fixtureOutputDirectory = ''
$script:fixtureRegistry = @{}
$script:results = New-Object 'System.Collections.Generic.List[object]'
$script:checks = 0
$script:cleanupFacts = New-Object 'System.Collections.Generic.List[object]'
$script:expectedCases = 13
$script:helperPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'TraceFold.UiAcceptance.Metadata.psm1'

function Write-TestJson {
    param([string]$Path, $Value)
    [IO.File]::WriteAllText($Path, (ConvertTo-TestAsciiJson -Value $Value), [Text.UTF8Encoding]::new($false))
}

# A localized runtime error message is not ASCII. Written raw into a BOM-less file, a PS 5.1
# reader decodes it with the console code page and the JSON stops parsing. Escaping every
# character above 127 as \uXXXX keeps the text, keeps the JSON valid on both hosts, and keeps
# the file ASCII like every other file here. Pure-ASCII content is written unchanged.
function ConvertTo-TestAsciiJson {
    param($Value)
    $json = ConvertTo-Json -InputObject $Value -Depth 50
    $builder = New-Object 'System.Text.StringBuilder'
    foreach ($ch in $json.ToCharArray()) {
        if ([int] $ch -lt 128) { [void] $builder.Append($ch) }
        else { [void] $builder.Append(('\u{0:x4}' -f [int] $ch)) }
    }
    return $builder.ToString()
}

function Assert-Test {
    param([bool]$Condition, [string]$Message)
    $script:checks++
    if (-not $Condition) { throw ('ASSERT: ' + $Message) }
}

function Assert-TestNull {
    param($Value, [string]$Message)
    $script:checks++
    if ($null -ne $Value) { throw ('ASSERT: ' + $Message) }
}

function Hash-TestBytes {
    param([byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '') }
    finally { $sha.Dispose() }
}

# Identity of a case is its id plus the exact text of its body. The dispatch record compares the
# text that actually ran with the text registered under that id, so a label that points at the
# wrong case, or a body that silently changed, is visible instead of passing as "13 tests ran".
function Get-MetadataTextSha256 {
    param([AllowNull()][string] $Text)
    if ($null -eq $Text) { return '' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '') }
    finally { $sha.Dispose() }
}

# ---------------------------------------------------------------------------
# Fixture model: every object this suite creates is registered with its exact
# full path, kind and note. Cleanup deletes only registered objects, removes
# junction links by their exact path (never following the target), separates
# deliberately retained evidence from residue, and proves closure by reading
# every registration back. An unregistered deletion is refused.
# ---------------------------------------------------------------------------

function New-TestFixture {
    param([string]$Id)
    $base = Join-Path $script:outputRoot $Id
    [void][IO.Directory]::CreateDirectory($base)
    $owned = Join-Path $base 'owned'
    [void][IO.Directory]::CreateDirectory($owned)
    $token = 'TEST-ONLY-' + [Guid]::NewGuid().ToString('N')
    $record = [ordered]@{
        id = $Id
        base = $base
        token = $token
        created = $true
        removed = $false
        createdObjects = New-Object 'System.Collections.Generic.List[object]'
        residue = New-Object 'System.Collections.Generic.List[object]'
    }
    $record.createdObjects.Add([ordered]@{
        path = $base; kind = 'directory'; note = 'fixture case root'
        retained = $false; retainedReason = $null; disposition = 'pending'
    })
    $record.createdObjects.Add([ordered]@{
        path = $owned; kind = 'directory'; note = 'fixture owned root'
        retained = $false; retainedReason = $null; disposition = 'pending'
    })
    $script:fixtureRegistry[$base] = $record
    return [pscustomobject]@{
        id = $Id
        base = $base
        owned = $owned
        token = $token
        logs = (Join-Path $owned 'logs')
        product = (Join-Path $base 'product')
        tools = (Join-Path $base 'tools')
        registryKey = $base
    }
}

function Register-TestCreation {
    param(
        [Parameter(Mandatory)] $Fixture,
        [Parameter(Mandatory)][string] $Path,
        [string] $Kind = 'file',
        [string] $Note = ''
    )
    $exact = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not $script:fixtureRegistry.ContainsKey($Fixture.registryKey)) {
        throw 'A creation was attempted outside a registered fixture.'
    }
    if (-not $exact.StartsWith($Fixture.registryKey + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A creation was attempted outside its own fixture root.'
    }
    $record = $script:fixtureRegistry[$Fixture.registryKey]
    $record.createdObjects.Add([ordered]@{
        path = $exact; kind = $Kind; note = $Note
        retained = $false; retainedReason = $null; disposition = 'pending'
    })
    return $exact
}

function Retain-TestCreation {
    param(
        [Parameter(Mandatory)] $Fixture,
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Reason
    )
    $exact = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $record = $script:fixtureRegistry[$Fixture.registryKey]
    if ($null -eq $record) { throw 'Retention outside a registered fixture.' }
    $marked = 0
    foreach ($entry in $record.createdObjects) {
        if ($entry.path -ceq $exact) {
            $entry.retained = $true
            $entry.retainedReason = $Reason
            $marked = $marked + 1
        }
    }
    if ($marked -eq 0) { throw 'Retention names an unregistered creation.' }
    return $exact
}

function New-TestDirectory {
    param([Parameter(Mandatory)] $Fixture, [Parameter(Mandatory)][string] $Path, [string] $Note = '')
    $exact = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    [void][IO.Directory]::CreateDirectory($exact)
    Register-TestCreation -Fixture $Fixture -Path $exact -Kind 'directory' -Note $Note | Out-Null
    return $exact
}

function New-TestFile {
    param([Parameter(Mandatory)] $Fixture, [Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][AllowEmptyCollection()][byte[]] $Bytes, [string] $Note = '')
    $exact = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    [IO.File]::WriteAllBytes($exact, $Bytes)
    Register-TestCreation -Fixture $Fixture -Path $exact -Kind 'file' -Note $Note | Out-Null
    return $exact
}

function New-TestText {
    param([Parameter(Mandatory)] $Fixture, [Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Text, [string] $Note = '')
    $exact = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    [IO.File]::WriteAllText($exact, $Text, [Text.UTF8Encoding]::new($false))
    Register-TestCreation -Fixture $Fixture -Path $exact -Kind 'file' -Note $Note | Out-Null
    return $exact
}

function New-TestJunction {
    param([Parameter(Mandatory)] $Fixture, [Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Target, [string] $Note = '')
    $exact = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $null = New-Item -ItemType Junction -Path $exact -Value $Target -ErrorAction Stop
    Register-TestCreation -Fixture $Fixture -Path $exact -Kind 'junction' -Note $Note | Out-Null
    return $exact
}

function Remove-TestFixture {
    param([Parameter(Mandatory)][string]$FixtureRoot)
    $exact = [IO.Path]::GetFullPath($FixtureRoot).TrimEnd('\')
    if (-not $script:fixtureRegistry.ContainsKey($exact)) { throw 'Refused unregistered test fixture deletion.' }
    $record = $script:fixtureRegistry[$exact]
    if (-not $exact.StartsWith($script:fixtureOutputDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refused test deletion outside exact test output.'
    }
    if ([IO.Directory]::Exists($exact) -or [IO.File]::Exists($exact)) {
        if ((Get-Item -LiteralPath $exact -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Refused a reparse fixture root.'
        }

        # Junction and symlink links created by this run are removed by their exact path
        # before any recursive delete: the link goes, its target is never followed and
        # never deleted. Only links this run registered are touched.
        foreach ($entry in $record.createdObjects.ToArray()) {
            if ($entry.kind -cne 'junction') { continue }
            $link = $entry.path
            if ([IO.Directory]::Exists($link)) {
                [IO.Directory]::Delete($link)
            }
            elseif ([IO.File]::Exists($link)) {
                [IO.File]::Delete($link)
            }
        }

        $retained = @($record.createdObjects | Where-Object { $_.retained } | ForEach-Object { $_.path })
        if (@($retained).Count -gt 0) {
            # Deliberate evidence is kept, so the base is not deleted wholesale: every
            # non-retained creation is removed leaf-first and the ancestors of retained
            # evidence are skipped so the evidence survives.
            $paths = @($record.createdObjects | ForEach-Object { $_.path } | Sort-Object -Unique)
            $ordered = $paths | Sort-Object { ($_ -split '\\').Count } -Descending
            foreach ($candidate in $ordered) {
                if (@($retained | Where-Object { $_ -ceq $candidate }).Count -gt 0) { continue }
                $prefix = $candidate.TrimEnd('\') + '\'
                $keepsChild = @($retained | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
                if ($keepsChild) { continue }
                if ([IO.Directory]::Exists($candidate)) { [IO.Directory]::Delete($candidate, $true) }
                elseif ([IO.File]::Exists($candidate)) { [IO.File]::Delete($candidate) }
            }
        }
        else {
            Remove-Item -LiteralPath $exact -Recurse -Force -ErrorAction Stop
        }
    }

    # Closure is decided by the per-object dispositions below, not by the base directory
    # alone: deliberate evidence legitimately keeps its ancestor directories alive, so a
    # surviving base directory alone is not residue and a vanished one alone is not proof.
    foreach ($entry in $record.createdObjects) {
        $present = ([IO.Directory]::Exists($entry.path) -or [IO.File]::Exists($entry.path))
        $keepsRetained = $false
        if ($present -and -not $entry.retained) {
            $ancestorPrefix = $entry.path.TrimEnd('\') + '\'
            $keepsRetained = @($record.createdObjects | Where-Object {
                $_.retained -and $_.path.StartsWith($ancestorPrefix, [StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
        }
        if ($entry.retained) {
            $entry.disposition = $(if ($present) { 'retained-as-evidence' } else { 'retention-lost' })
            if (-not $present) { $record.residue.Add($entry.path + ' (retention lost)') }
        }
        elseif ($keepsRetained) {
            $entry.disposition = 'kept-alive-by-retained-evidence'
        }
        elseif ($present) {
            $entry.disposition = 'residue'
            $record.residue.Add($entry.path)
        }
        else {
            $entry.disposition = 'removed'
        }
    }
    $unresolved = @($record.createdObjects | Where-Object {
        $_.disposition -ceq 'residue' -or $_.disposition -ceq 'retention-lost'
    }).Count
    $gone = ($unresolved -eq 0)
    $record.removed = $gone
    [void]$script:cleanupFacts.Add([ordered]@{
        path = $exact
        registered = $true
        utc = [DateTime]::UtcNow.ToString('o')
        directoryExists = [IO.Directory]::Exists($exact)
        fileExists = [IO.File]::Exists($exact)
        gone = $gone
        createdObjectCount = $record.createdObjects.Count
        residueCount = $record.residue.Count
        residue = $record.residue.ToArray()
        meaning = 'only self-created fixture deletion, not production release protocol; junction links removed by exact path, targets never followed'
    })
    if (-not $gone) { throw 'Registered test fixture remains.' }
    if ($record.residue.Count -gt 0) { throw ('A registered creation survived cleanup: ' + ($record.residue -join '; ')) }
}

# Registration only: this fills the case table and runs nothing. The 13 call sites below are
# unchanged; only the verb changed, because a function named Invoke- would now be a lie.
function Add-MetadataCase {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][scriptblock] $Body)
    foreach ($existing in $script:caseTable) {
        if ($existing.Id -ceq $Id) { throw ('Duplicate metadata case id: ' + $Id) }
    }
    [void] $script:caseTable.Add([pscustomobject] @{
        Id = $Id
        Body = $Body
        BodySha256 = (Get-MetadataTextSha256 -Text ([string] $Body.ToString()))
    })
}

# One import, once per host, performed by the first case rather than at load time.
function Initialize-MetadataSuite {
    if ($script:metadataInitialized) { return }
    if ($script:caseTable.Count -ne $script:expectedCases) {
        throw ('The metadata case table holds ' + $script:caseTable.Count + ' cases, expected ' + $script:expectedCases + '.')
    }
    Import-Module $script:helperPath -Force -DisableNameChecking
    Assert-Test ($null -ne (Get-Command -Name 'Get-UiMetadataTreeDigest' -ErrorAction SilentlyContinue)) 'actual helper module loaded'
    $script:metadataInitialized = $true
}

function Get-MetadataCaseEntry {
    param([Parameter(Mandatory)][string] $Id)
    foreach ($entry in $script:caseTable) {
        if ($entry.Id -ceq $Id) { return $entry }
    }
    throw ('Unknown metadata case: ' + $Id)
}

# Executes one registered case and records its result. This is the only execution path; the
# standalone driver and the toolkit both reach it through Invoke-MetadataCaseById.
function Invoke-MetadataCase {
    param([string]$Id, [scriptblock]$Body)
    # Taken before the body runs: this is the text that is about to be invoked, not a re-read
    # afterwards, so a body that mutates state cannot change what the record says was run.
    $invokedSha = Get-MetadataTextSha256 -Text ([string] $Body.ToString())
    $caseClock = [Diagnostics.Stopwatch]::StartNew()
    $started = [DateTime]::UtcNow.ToString('o')
    $status = 'Passed'
    $errorFact = $null
    $facts = $null
    try { $facts = & $Body }
    catch {
        $status = 'Failed'
        $errorFact = [ordered]@{
            type = $_.Exception.GetType().FullName
            message = $_.Exception.Message
            position = $_.InvocationInfo.PositionMessage
            stack = $_.ScriptStackTrace
        }
        if ($_.Exception.Message.StartsWith('NOT_ESTABLISHED:')) { $status = 'NotEstablished' }
    }
    $caseClock.Stop()
    [void]$script:results.Add([ordered]@{
        id = $Id
        status = $status
        startedUtc = $started
        endedUtc = [DateTime]::UtcNow.ToString('o')
        durationMs = $caseClock.Elapsed.TotalMilliseconds
        facts = $facts
        error = $errorFact
    })
    Write-TestJson -Path (Join-Path $script:outputRoot ($Id + '-result.json')) -Value $script:results[$script:results.Count - 1]
    if ($script:standaloneRun) { [Console]::Out.WriteLine($Id + ' ' + $status) }
    return [pscustomobject] @{ Id = $Id; Status = $status; Facts = $facts; Error = $errorFact; BodySha256 = $invokedSha }
}

# Removes one registered attempt directory the caller created and proves it gone. It refuses
# anything outside the temporary directory or not named as a metadata attempt, and it never
# walks into a reparse point.
function Remove-MetadataAttemptRoot {
    param([Parameter(Mandatory)][string] $Path)
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $full.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refused to remove an attempt directory outside the temporary directory.'
    }
    if (-not ([IO.Path]::GetFileName($full) -like 'tracefold-metadata-*')) {
        throw 'Refused to remove a directory that is not a metadata attempt.'
    }
    if ((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Refused to remove a reparse attempt directory.'
    }
    Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
    if ([IO.Directory]::Exists($full) -or [IO.File]::Exists($full)) {
        throw ('The attempt directory survived its removal: ' + $full)
    }
}

# The single per-case entry used by both modes. In standalone mode the output root is the
# caller's directory and stays. In registered mode every case gets its own fresh attempt
# directory under the temporary directory: a pass must leave it gone, a failure keeps it and
# names it, so a later reader can tell evidence from residue.
function Invoke-MetadataCaseById {
    param([Parameter(Mandatory)][string] $Id)
    Initialize-MetadataSuite
    $entry = Get-MetadataCaseEntry -Id $Id
    $attemptRoot = ''
    if (-not $script:standaloneRun) {
        $attemptRoot = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-metadata-' + $Id + '-' + [Guid]::NewGuid().ToString('N'))
        if ([IO.Directory]::Exists($attemptRoot) -or [IO.File]::Exists($attemptRoot)) {
            throw ('Metadata attempt directory already exists: ' + $attemptRoot)
        }
        [void][IO.Directory]::CreateDirectory($attemptRoot)
        $script:outputRoot = $attemptRoot
        $script:fixtureOutputDirectory = $attemptRoot
    }
    $result = Invoke-MetadataCase -Id $entry.Id -Body $entry.Body
    # The dispatch record is the registration gate's evidence: which id was asked for, which
    # entry answered it, whether the text that ran is the text registered under that id, and
    # whether this case's own attempt directory was proven closed afterwards.
    $dispatch = [ordered] @{
        requestedId = $Id
        entryId = [string] $entry.Id
        registeredBodySha256 = [string] $entry.BodySha256
        invokedBodySha256 = [string] $result.BodySha256
        bodyMatches = [bool] ($entry.BodySha256 -ceq $result.BodySha256)
        mode = $(if ($script:standaloneRun) { 'standalone' } else { 'registered' })
        status = [string] $result.Status
        attemptDirectory = $attemptRoot
        attemptClosed = $false
        attemptAbsent = $null
        attemptRemovalError = ''
        utc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    [void] $script:dispatchFacts.Add($dispatch)
    if ($result.Status -cne 'Passed') {
        $detail = [string] $result.Status
        if ($null -ne $result.Error) { $detail = [string] $result.Error.message }
        $where = ''
        if (-not [string]::IsNullOrEmpty($attemptRoot)) { $where = ' (attempt directory retained: ' + $attemptRoot + ')' }
        throw ('metadata ' + $Id + ' ' + $detail + $where)
    }
    if (-not [string]::IsNullOrEmpty($attemptRoot)) {
        try {
            Remove-MetadataAttemptRoot -Path $attemptRoot
            $dispatch.attemptClosed = $true
            $dispatch.attemptAbsent = -not ([IO.Directory]::Exists($attemptRoot) -or [IO.File]::Exists($attemptRoot))
        }
        catch {
            $dispatch.attemptRemovalError = [string] $_.Exception.Message
            throw
        }
        $script:outputRoot = ''
        $script:fixtureOutputDirectory = ''
    }
    return $result.Facts
}

function Get-TestField {
    param($Node, [string]$Name)
    if ($null -eq $Node) { return $null }
    if ($Node -is [System.Collections.IDictionary]) {
        if ($Node.Contains($Name)) { return $Node[$Name] }
        return $null
    }
    $property = $Node.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

# M01: one file digest class. Captured bytes and sha match an independent hash, a missing file
# is Absent with null values, a locked file is an Error with its message, and a zero-byte file
# is Captured with the empty-input sha rather than being confused with absence.
Add-MetadataCase 'M01' {
    $f = New-TestFixture 'M01'
    try {
        [byte[]]$bytes = @(1, 2, 3, 250, 0, 255)
        $path = New-TestFile -Fixture $f -Path (Join-Path $f.base 'sample.bin') -Bytes $bytes -Note 'captured digest sample'
        $digest = Get-UiMetadataFileDigest -Path $path
        Assert-Test ((Get-TestField $digest 'state') -ceq 'captured') 'a present file digests as captured'
        Assert-Test ((Get-TestField $digest 'bytes') -eq [long]$bytes.Length) 'captured bytes equal the file length'
        Assert-Test ((Get-TestField $digest 'sha256') -ceq (Hash-TestBytes $bytes)) 'captured sha equals the independent hash'

        $missing = Get-UiMetadataFileDigest -Path (Join-Path $f.base 'not-here.bin')
        Assert-Test ((Get-TestField $missing 'state') -ceq 'absent') 'a missing file is absent'
        Assert-TestNull (Get-TestField $missing 'bytes') 'an absent file keeps bytes null'
        Assert-TestNull (Get-TestField $missing 'sha256') 'an absent file keeps sha256 null'

        $emptyPath = New-TestFile -Fixture $f -Path (Join-Path $f.base 'empty.bin') -Bytes ([byte[]]::new(0)) -Note 'zero-byte digest sample'
        $empty = Get-UiMetadataFileDigest -Path $emptyPath
        Assert-Test ((Get-TestField $empty 'state') -ceq 'captured') 'a zero-byte file is captured'
        Assert-Test ((Get-TestField $empty 'bytes') -eq 0) 'a zero-byte file records zero bytes'
        Assert-Test ((Get-TestField $empty 'sha256') -ceq (Hash-TestBytes ([byte[]]::new(0)))) 'a zero-byte file records the empty-input sha'

        $lockPath = New-TestFile -Fixture $f -Path (Join-Path $f.base 'locked.bin') -Bytes ([byte[]](9, 9, 9)) -Note 'locked read sample'
        $lock = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try {
            $locked = Get-UiMetadataFileDigest -Path $lockPath
            Assert-Test ((Get-TestField $locked 'state') -ceq 'error') 'a locked read is an explicit error'
            Assert-Test (-not [string]::IsNullOrWhiteSpace([string](Get-TestField $locked 'error'))) 'a locked read names its error'
            Assert-TestNull (Get-TestField $locked 'sha256') 'a locked read keeps sha256 null'
        }
        finally { if ($null -ne $lock) { $lock.Dispose() } }

        return [ordered]@{
            sampleSha256 = (Get-TestField $digest 'sha256')
            missingState = (Get-TestField $missing 'state')
            emptySha256 = (Get-TestField $empty 'sha256')
            lockedState = (Get-TestField $locked 'state')
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M02: ordinal tree digest stability. Two roots with identical content created in opposite
# order produce the same tree sha, because the manifest sort is ordinal and not a culture
# collation; the same digest must hold under zh-SG and en-US current cultures.
Add-MetadataCase 'M02' {
    $f = New-TestFixture 'M02'
    try {
        $rootA = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'tree-a') -Note 'first creation order root'
        $rootB = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'tree-b') -Note 'second creation order root'

        [byte[]]$one = [Text.UTF8Encoding]::new($false).GetBytes('alpha')
        [byte[]]$two = [Text.UTF8Encoding]::new($false).GetBytes('beta')
        [byte[]]$three = [Text.UTF8Encoding]::new($false).GetBytes('gamma')

        New-TestFile -Fixture $f -Path (Join-Path $rootA 'B.txt') -Bytes $one -Note 'case-ordered member' | Out-Null
        New-TestFile -Fixture $f -Path (Join-Path $rootA 'a.txt') -Bytes $two -Note 'case-ordered member' | Out-Null
        New-TestDirectory -Fixture $f -Path (Join-Path $rootA 'sub') -Note 'nested member holder' | Out-Null
        New-TestFile -Fixture $f -Path ((Join-Path (Join-Path $rootA 'sub') 'z.bin')) -Bytes $three -Note 'nested member' | Out-Null

        New-TestFile -Fixture $f -Path (Join-Path $rootB 'a.txt') -Bytes $two -Note 'reverse creation order member' | Out-Null
        New-TestDirectory -Fixture $f -Path (Join-Path $rootB 'sub') -Note 'nested member holder' | Out-Null
        New-TestFile -Fixture $f -Path ((Join-Path (Join-Path $rootB 'sub') 'z.bin')) -Bytes $three -Note 'nested member' | Out-Null
        New-TestFile -Fixture $f -Path (Join-Path $rootB 'B.txt') -Bytes $one -Note 'reverse creation order member' | Out-Null

        $digestA = Get-UiMetadataTreeDigest -Root $rootA
        $digestB = Get-UiMetadataTreeDigest -Root $rootB
        Assert-Test ((Get-TestField $digestA 'state') -ceq 'captured') 'the first tree digests as captured'
        Assert-Test ((Get-TestField $digestB 'state') -ceq 'captured') 'the second tree digests as captured'
        Assert-Test ((Get-TestField $digestA 'treeSha256') -ceq (Get-TestField $digestB 'treeSha256')) 'creation order does not change the ordinal digest'
        Assert-Test ((Get-TestField $digestA 'fileCount') -eq 3) 'the manifest counts every regular file once'

        $previousCulture = [System.Threading.Thread]::CurrentThread.CurrentCulture
        $previousUiCulture = [System.Threading.Thread]::CurrentThread.CurrentUICulture
        $culturesApplied = New-Object 'System.Collections.Generic.List[string]'
        try {
            foreach ($name in @('zh-SG', 'en-US')) {
                $culture = $null
                try { $culture = [Globalization.CultureInfo]::GetCultureInfo($name) }
                catch { $culture = $null }
                if ($null -eq $culture) {
                    $culturesApplied.Add($name + ':not-available')
                    continue
                }
                [System.Threading.Thread]::CurrentThread.CurrentCulture = $culture
                [System.Threading.Thread]::CurrentThread.CurrentUICulture = $culture
                $culturesApplied.Add($name)
                $underCulture = Get-UiMetadataTreeDigest -Root $rootA
                Assert-Test ((Get-TestField $underCulture 'treeSha256') -ceq (Get-TestField $digestA 'treeSha256')) ('the ordinal digest is culture invariant under ' + $name)
            }
        }
        finally {
            [System.Threading.Thread]::CurrentThread.CurrentCulture = $previousCulture
            [System.Threading.Thread]::CurrentThread.CurrentUICulture = $previousUiCulture
        }

        return [ordered]@{
            treeSha256 = (Get-TestField $digestA 'treeSha256')
            fileCount = (Get-TestField $digestA 'fileCount')
            culturesApplied = $culturesApplied.ToArray()
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M03: change detection. One changed byte, one renamed member and one changed length each
# produce a different tree sha, so the digest is a real change detector and not a constant.
Add-MetadataCase 'M03' {
    $f = New-TestFixture 'M03'
    try {
        $root = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'tree') -Note 'change detection root'
        $onePath = New-TestFile -Fixture $f -Path (Join-Path $root 'one.bin') -Bytes ([byte[]](1, 2, 3)) -Note 'first change member'
        New-TestFile -Fixture $f -Path (Join-Path $root 'two.bin') -Bytes ([byte[]](4, 5, 6)) -Note 'second change member' | Out-Null
        $before = Get-UiMetadataTreeDigest -Root $root
        Assert-Test ((Get-TestField $before 'state') -ceq 'captured') 'the baseline tree digests as captured'

        [IO.File]::WriteAllBytes($onePath, [byte[]](1, 2, 4))
        $changedByte = Get-UiMetadataTreeDigest -Root $root
        Assert-Test ((Get-TestField $changedByte 'treeSha256') -cne (Get-TestField $before 'treeSha256')) 'one changed byte changes the digest'

        [IO.File]::WriteAllBytes($onePath, [byte[]](1, 2, 3))
        [IO.File]::Move($onePath, (Join-Path $root 'renamed.bin'))
        Register-TestCreation -Fixture $f -Path (Join-Path $root 'renamed.bin') -Kind 'file' -Note 'renamed member' | Out-Null
        $renamed = Get-UiMetadataTreeDigest -Root $root
        Assert-Test ((Get-TestField $renamed 'treeSha256') -cne (Get-TestField $before 'treeSha256')) 'one renamed member changes the digest'

        New-TestFile -Fixture $f -Path (Join-Path $root 'two.bin') -Bytes ([byte[]](4, 5, 6, 7)) -Note 'relengthened member' | Out-Null
        $changedLength = Get-UiMetadataTreeDigest -Root $root
        Assert-Test ((Get-TestField $changedLength 'treeSha256') -cne (Get-TestField $before 'treeSha256')) 'one changed length changes the digest'
        Assert-Test ((Get-TestField $changedLength 'totalBytes') -eq [long]7) 'the manifest total bytes equal the member sum'

        return [ordered]@{
            baselineSha256 = (Get-TestField $before 'treeSha256')
            changedByteSha256 = (Get-TestField $changedByte 'treeSha256')
            renamedSha256 = (Get-TestField $renamed 'treeSha256')
            changedLengthSha256 = (Get-TestField $changedLength 'treeSha256')
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M04: reparse refusal. A junction inside the tree is refused before any member is read, the
# state is Refused with the reason preserved, and no digest is produced. The junction target
# bytes are proven untouched, the link is removed by its exact path, and the target sentinel
# is deliberately retained as evidence of that proof.
Add-MetadataCase 'M04' {
    $f = New-TestFixture 'M04'
    try {
        $root = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'tree') -Note 'junction tree root'
        $outside = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'outside') -Note 'junction target directory'
        $sentinel = New-TestFile -Fixture $f -Path (Join-Path $outside 'not-authorized.log') -Bytes ([byte[]](43, 44, 45)) -Note 'junction target sentinel'
        $before = Hash-TestBytes ([IO.File]::ReadAllBytes($sentinel))

        New-TestFile -Fixture $f -Path (Join-Path $root 'plain.bin') -Bytes ([byte[]](7)) -Note 'plain member beside the link' | Out-Null
        $link = New-TestJunction -Fixture $f -Path (Join-Path $root 'link') -Target $outside -Note 'junction under test'

        $digest = Get-UiMetadataTreeDigest -Root $root
        Assert-Test ((Get-TestField $digest 'state') -ceq 'refused') 'a junction member is refused'
        Assert-Test ((Get-TestField $digest 'error') -match 'Reparse') 'the refusal names the reparse reason'
        Assert-TestNull (Get-TestField $digest 'treeSha256') 'a refused tree keeps treeSha256 null'
        Assert-Test ((Hash-TestBytes ([IO.File]::ReadAllBytes($sentinel))) -ceq $before) 'the junction target bytes are untouched'

        # The link itself is removed by its exact path; the target is never followed.
        [IO.Directory]::Delete($link)
        Assert-Test (-not [IO.Directory]::Exists($link)) 'the exact junction link is gone'
        Assert-Test ([IO.File]::Exists($sentinel)) 'the junction target still exists after the link removal'
        Assert-Test ((Hash-TestBytes ([IO.File]::ReadAllBytes($sentinel))) -ceq $before) 'the junction target bytes are still untouched'

        Retain-TestCreation -Fixture $f -Path $sentinel -Reason 'evidence that the junction target survived the link removal'

        return [ordered]@{
            state = (Get-TestField $digest 'state')
            error = [string](Get-TestField $digest 'error')
            sentinelRetained = $true
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M05: build commit domain. A ProductVersion carrying +<commit> yields the commit; a plain
# four-part version, an empty value and null all stay null, because FileVersion never
# identifies a commit and nothing is guessed.
Add-MetadataCase 'M05' {
    $f = New-TestFixture 'M05'
    try {
        $withCommit = Read-UiMetadataBuildCommit -ProductVersionText '1.0.0+04d727f7214fc2f552242b7eb23618f82da87648'
        Assert-Test ($withCommit -ceq '04d727f7214fc2f552242b7eb23618f82da87648') 'a ProductVersion commit is extracted exactly'

        $plain = Read-UiMetadataBuildCommit -ProductVersionText '1.0.0.0'
        Assert-TestNull $plain 'a plain four-part version carries no commit'

        $empty = Read-UiMetadataBuildCommit -ProductVersionText ''
        Assert-TestNull $empty 'an empty ProductVersion carries no commit'

        $missing = Read-UiMetadataBuildCommit -ProductVersionText $null
        Assert-TestNull $missing 'a null ProductVersion carries no commit'

        $short = Read-UiMetadataBuildCommit -ProductVersionText '1.0.0+zz'
        Assert-TestNull $short 'a non-hex build suffix is not a commit'

        return [ordered]@{ withCommit = $withCommit; plainIsNull = ($null -eq $plain) }
    }
    finally { Remove-TestFixture $f.base }
}

# M06: source provenance domains. Without a repository root the script HEAD stays null with a
# state and an error, never an empty string and never another domain value; the candidate SHA
# domain is recorded separately; domains are never required to be equal.
Add-MetadataCase 'M06' {
    $f = New-TestFixture 'M06'
    try {
        $provenance = Get-UiMetadataSourceProvenance -ProductExecutable (Join-Path $f.base 'missing-product.exe') -RepositoryRoot (Join-Path $f.base 'not-a-repo') -CandidateSha256 $null
        Assert-TestNull (Get-TestField $provenance 'scriptRepositoryHead') 'no repository root keeps the script HEAD null'
        Assert-Test ((Get-TestField $provenance 'scriptRepositoryHeadState') -ceq 'unknown') 'no repository root keeps the HEAD state unknown'
        Assert-Test (-not [string]::IsNullOrWhiteSpace([string](Get-TestField $provenance 'scriptRepositoryHeadError'))) 'no repository root names its error'
        Assert-Test ((Get-TestField $provenance 'candidateSha256State') -ceq 'not-supplied') 'a missing candidate SHA is not-supplied, not a fabricated hash'
        Assert-TestNull (Get-TestField $provenance 'candidateSha256') 'a missing candidate SHA stays null'
        Assert-TestNull (Get-TestField $provenance 'productBuildCommit') 'a missing product keeps the build commit null'
        Assert-Test ((Get-TestField $provenance 'domainsSeparate') -eq $true) 'the provenance domains are declared separate'

        $withCandidate = Get-UiMetadataSourceProvenance -ProductExecutable (Join-Path $f.base 'missing-product.exe') -RepositoryRoot (Join-Path $f.base 'not-a-repo') -CandidateSha256 ('A' * 64)
        Assert-Test ((Get-TestField $withCandidate 'candidateSha256State') -ceq 'captured') 'a supplied candidate SHA is captured'
        Assert-TestNull (Get-TestField $withCandidate 'scriptRepositoryHead') 'the script HEAD is unaffected by a candidate SHA'

        return [ordered]@{
            headState = (Get-TestField $provenance 'scriptRepositoryHeadState')
            headError = [string](Get-TestField $provenance 'scriptRepositoryHeadError')
            candidateState = (Get-TestField $withCandidate 'candidateSha256State')
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M07: product identity classes. The exe digest is its own class, the tree digest its own, the
# Task 14 package class stays not-generated, and FileVersion-less bytes keep their version
# fields unknown rather than being filled with placeholders.
Add-MetadataCase 'M07' {
    $f = New-TestFixture 'M07'
    try {
        $product = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'product') -Note 'synthetic product directory'
        [byte[]]$exeBytes = [Text.UTF8Encoding]::new($false).GetBytes('TEST_ONLY_NOT_A_PRODUCT')
        New-TestFile -Fixture $f -Path (Join-Path $product 'TraceFold.Media.exe') -Bytes $exeBytes -Note 'synthetic exe' | Out-Null
        New-TestFile -Fixture $f -Path (Join-Path $product 'TraceFold.Media.dll') -Bytes ([byte[]](1, 2)) -Note 'synthetic companion' | Out-Null

        $identity = Get-UiMetadataProductIdentity -ProductExecutable (Join-Path $product 'TraceFold.Media.exe') -ProductDirectory $product
        $exeDigest = Get-TestField $identity 'executableDigest'
        Assert-Test ((Get-TestField $exeDigest 'state') -ceq 'captured') 'the exe digest is captured'
        Assert-Test ((Get-TestField $exeDigest 'sha256') -ceq (Hash-TestBytes $exeBytes)) 'the exe digest equals the independent hash'
        $treeDigest = Get-TestField $identity 'treeDigest'
        Assert-Test ((Get-TestField $treeDigest 'state') -ceq 'captured') 'the product tree digest is captured'
        Assert-Test ((Get-TestField $treeDigest 'fileCount') -eq 2) 'the product tree counts both members'
        Assert-Test ((Get-TestField $identity 'packageZipState') -ceq 'not-generated') 'the package class stays not-generated'
        Assert-TestNull (Get-TestField $identity 'packageZipDigest') 'the package class keeps its digest null'
        Assert-Test ((Get-TestField $identity 'fileVersionState') -ceq 'unknown') 'a FileVersion-less exe keeps its file version unknown'
        Assert-TestNull (Get-TestField $identity 'fileVersion') 'a FileVersion-less exe keeps its file version null'
        Assert-TestNull (Get-TestField $identity 'buildCommit') 'a FileVersion-less exe carries no build commit'

        return [ordered]@{
            exeSha256 = (Get-TestField $exeDigest 'sha256')
            treeSha256 = (Get-TestField $treeDigest 'treeSha256')
            packageZipState = (Get-TestField $identity 'packageZipState')
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M08: driver identity classes. A real zip archive, its extracted member and the manifest
# values stay in separate classes, the member and manifest booleans are computed only when
# both sides were read, and a wrong manifest hash records false instead of failing silently.
Add-MetadataCase 'M08' {
    $f = New-TestFixture 'M08'
    try {
        $memberSource = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'member-source') -Note 'zip member source'
        [byte[]]$memberBytes = [Text.UTF8Encoding]::new($false).GetBytes('TEST_ONLY_NOT_WINAPP')
        New-TestFile -Fixture $f -Path (Join-Path $memberSource 'winapp.exe') -Bytes $memberBytes -Note 'archive member source' | Out-Null
        $archivePath = Join-Path $f.base 'winappcli-x64.zip'
        try { $null = Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
        try { $null = Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }
        [IO.Compression.ZipFile]::CreateFromDirectory($memberSource, $archivePath)
        Register-TestCreation -Fixture $f -Path $archivePath -Kind 'file' -Note 'created zip archive' | Out-Null

        $extractedPath = Join-Path $f.base 'extracted-winapp.exe'
        $zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($archivePath))
        try {
            $member = @($zip.Entries | Where-Object { $_.FullName -ceq 'winapp.exe' })
            Assert-Test ($member.Count -eq 1) 'the archive carries exactly one winapp.exe member'
            $stream = $member[0].Open()
            try {
                $target = [IO.File]::Create($extractedPath)
                try { $stream.CopyTo($target) }
                finally { $target.Dispose() }
            }
            finally { $stream.Dispose() }
        }
        finally { $zip.Dispose() }
        Register-TestCreation -Fixture $f -Path $extractedPath -Kind 'file' -Note 'extracted archive member' | Out-Null

        $archiveBytes = [IO.File]::ReadAllBytes($archivePath)
        $archiveSha = Hash-TestBytes $archiveBytes

        $identity = Get-UiMetadataDriverIdentity -ArchivePath $archivePath -ExtractedExePath $extractedPath -ManifestVersion '0.5.0' -ManifestSha256 $archiveSha -ManifestArchiveSize ([long]$archiveBytes.Length)
        $archiveDigest = Get-TestField $identity 'archiveDigest'
        Assert-Test ((Get-TestField $archiveDigest 'state') -ceq 'captured') 'the archive digest is captured'
        Assert-Test ((Get-TestField $archiveDigest 'sha256') -ceq $archiveSha) 'the archive digest equals the independent hash'
        Assert-Test ((Get-TestField $identity 'matchesManifest') -eq $true) 'a matching manifest records true'
        Assert-Test ((Get-TestField $identity 'matchesArchiveMember') -eq $true) 'the extracted member matches the archive member'
        Assert-Test ((Get-TestField $identity 'manifestVersion') -ceq '0.5.0') 'the manifest version class is preserved'
        Assert-TestNull (Get-TestField $identity 'peFileVersion') 'a version-less member keeps the PE version null'
        Assert-Test ((Get-TestField $identity 'peFileVersionState') -ceq 'unknown') 'a version-less member keeps the PE version state unknown'

        $wrong = Get-UiMetadataDriverIdentity -ArchivePath $archivePath -ExtractedExePath $extractedPath -ManifestVersion '0.5.0' -ManifestSha256 ('0' * 64) -ManifestArchiveSize 1
        Assert-Test ((Get-TestField $wrong 'matchesManifest') -eq $false) 'a wrong manifest hash records false'

        $noManifest = Get-UiMetadataDriverIdentity -ArchivePath $archivePath -ExtractedExePath $extractedPath
        Assert-TestNull (Get-TestField $noManifest 'matchesManifest') 'without manifest values the boolean stays null'

        return [ordered]@{
            archiveSha256 = (Get-TestField $archiveDigest 'sha256')
            memberSha256 = (Get-TestField (Get-TestField $identity 'archiveMemberDigest') 'sha256')
            matchesManifest = (Get-TestField $identity 'matchesManifest')
            matchesArchiveMember = (Get-TestField $identity 'matchesArchiveMember')
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M09: fixture manifest. A declared asset is re-digested and confirmed, a changed declared
# hash is contradictory, a missing asset is absent, a path escape is refused and a missing
# declared hash is an error; none of these is dropped silently.
Add-MetadataCase 'M09' {
    $f = New-TestFixture 'M09'
    try {
        $repository = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'repo') -Note 'synthetic repository root'
        $media = New-TestDirectory -Fixture $f -Path ((Join-Path (Join-Path $repository 'fixtures') 'media')) -Note 'synthetic media folder'
        [byte[]]$bytes = [Text.UTF8Encoding]::new($false).GetBytes('TEST_ONLY_FIXTURE')
        New-TestFile -Fixture $f -Path (Join-Path $media 'photo.jpg') -Bytes $bytes -Note 'synthetic fixture asset' | Out-Null
        $declaredSha = Hash-TestBytes $bytes

        $good = Get-UiMetadataFixtureManifest -DeclaredAssets @([pscustomobject]@{ fixedFilename = 'photo.jpg'; repositoryPath = 'fixtures/media/photo.jpg'; sha256 = $declaredSha; sizeBytes = [long]$bytes.Length }) -RepositoryRoot $repository
        Assert-Test ((Get-TestField $good 'state') -ceq 'captured') 'a matching declared asset is captured'
        Assert-Test ((Get-TestField $good 'verifiedCount') -eq 1) 'the manifest counts the verified asset'
        $row = @(Get-TestField $good 'assets')[0]
        Assert-Test ((Get-TestField $row 'actualSha256') -ceq $declaredSha) 'the recomputed asset hash is recorded'

        $wrong = Get-UiMetadataFixtureManifest -DeclaredAssets @([pscustomobject]@{ fixedFilename = 'photo.jpg'; repositoryPath = 'fixtures/media/photo.jpg'; sha256 = ('A' * 64); sizeBytes = [long]$bytes.Length }) -RepositoryRoot $repository
        Assert-Test ((Get-TestField $wrong 'state') -ceq 'unverified') 'a changed declared hash is unverified'
        Assert-Test ((Get-TestField @(Get-TestField $wrong 'assets')[0] 'state') -ceq 'contradictory') 'the changed row is contradictory'

        $missing = Get-UiMetadataFixtureManifest -DeclaredAssets @([pscustomobject]@{ fixedFilename = 'gone.jpg'; repositoryPath = 'fixtures/media/gone.jpg'; sha256 = ('B' * 64); sizeBytes = 3 }) -RepositoryRoot $repository
        Assert-Test ((Get-TestField @(Get-TestField $missing 'assets')[0] 'state') -ceq 'absent') 'a missing asset is absent'

        $escape = Get-UiMetadataFixtureManifest -DeclaredAssets @([pscustomobject]@{ fixedFilename = 'x.jpg'; repositoryPath = '../../outside.jpg'; sha256 = ('C' * 64); sizeBytes = 3 }) -RepositoryRoot $repository
        Assert-Test ((Get-TestField @(Get-TestField $escape 'assets')[0] 'state') -ceq 'refused') 'an escaping fixture path is refused'

        $noHash = Get-UiMetadataFixtureManifest -DeclaredAssets @([pscustomobject]@{ fixedFilename = 'photo.jpg'; repositoryPath = 'fixtures/media/photo.jpg'; sizeBytes = [long]$bytes.Length }) -RepositoryRoot $repository
        Assert-Test ((Get-TestField @(Get-TestField $noHash 'assets')[0] 'state') -ceq 'error') 'a missing declared hash is an error'

        $emptyManifest = Get-UiMetadataFixtureManifest -DeclaredAssets @() -RepositoryRoot $repository
        Assert-Test ((Get-TestField $emptyManifest 'state') -ceq 'empty') 'no declared assets is empty'

        return [ordered]@{
            goodState = (Get-TestField $good 'state')
            wrongState = (Get-TestField $wrong 'state')
            missingState = (Get-TestField @(Get-TestField $missing 'assets')[0] 'state')
            escapeState = (Get-TestField @(Get-TestField $escape 'assets')[0] 'state')
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M10: host observation. Without readers the values stay null with states and the old
# hardcoded 96 / 'unknown' / 0x0 is never reintroduced; a reader that throws records its own
# error state; a labelled stub reader proves the captured path.
Add-MetadataCase 'M10' {
    $f = New-TestFixture 'M10'
    try {
        $none = Get-UiMetadataHostObservation
        Assert-TestNull (Get-TestField $none 'dpi') 'without a reader the dpi stays null'
        Assert-Test ((Get-TestField $none 'dpiState') -ceq 'unknown') 'without a reader the dpi state is unknown'
        Assert-TestNull (Get-TestField $none 'theme') 'without a reader the theme stays null'
        Assert-Test ((Get-TestField $none 'themeState') -ceq 'unknown') 'without a reader the theme state is unknown'
        Assert-TestNull (Get-TestField $none 'viewport') 'without a reader the viewport stays null'
        Assert-Test ((Get-TestField $none 'viewportState') -ceq 'not-run') 'without a reader the viewport state is not-run'

        $throwing = Get-UiMetadataHostObservation -ThemeReader { throw 'TEST_ONLY theme read failed' }
        Assert-Test ((Get-TestField $throwing 'themeState') -ceq 'error') 'a throwing reader records an error state'
        Assert-Test ((Get-TestField $throwing 'themeError') -match 'TEST_ONLY') 'a throwing reader names its error'
        Assert-TestNull (Get-TestField $throwing 'theme') 'a throwing reader keeps the value null'

        $captured = Get-UiMetadataHostObservation -DpiReader { 144 } -ThemeReader { 'Dark' } -ViewportReader { [ordered]@{ widthDip = 1024; heightDip = 720 } }
        Assert-Test ((Get-TestField $captured 'dpi') -eq 144) 'a labelled stub reader value is captured'
        Assert-Test ((Get-TestField $captured 'dpiState') -ceq 'captured') 'a labelled stub reader state is captured'
        Assert-Test ((Get-TestField $captured 'theme') -ceq 'Dark') 'a labelled stub theme reader value is captured'
        $viewport = Get-TestField $captured 'viewport'
        Assert-Test ($null -ne $viewport) 'a labelled stub viewport reader value is captured'
        Assert-Test ((Get-TestField $viewport 'widthDip') -eq 1024) 'the captured viewport keeps its DIP width'
        Assert-Test ((Get-TestField $captured 'viewportState') -ceq 'captured') 'the captured viewport state is captured'

        return [ordered]@{ dpiState = (Get-TestField $none 'dpiState'); themeState = (Get-TestField $none 'themeState'); viewportState = (Get-TestField $none 'viewportState') }
    }
    finally { Remove-TestFixture $f.base }
}

# M11: date-safe JSON reading. The original offset and fraction survive in the text, the
# moment helper normalizes to the same UTC instant from text and from typed values of every
# kind, text without a zone and an Unspecified stamp stay unknown instead of being assumed
# UTC, and unparseable text is null rather than a guess.
Add-MetadataCase 'M11' {
    $f = New-TestFixture 'M11'
    try {
        $path = New-TestText -Fixture $f -Path (Join-Path $f.base 'moment.json') -Text ('{ ' + [char]34 + 'startedUtc' + [char]34 + ': ' + [char]34 + '2026-10-16T11:12:09.9003395+02:00' + [char]34 + ' }') -Note 'date-safe JSON document'

        $document = Read-UiMetadataJsonDocument -Path $path
        Assert-Test ((Get-TestField $document 'state') -ceq 'captured') 'the JSON document reads as captured'
        Assert-Test ((Get-TestField $document 'text') -ceq ([IO.File]::ReadAllText($path, [Text.Encoding]::UTF8))) 'the raw text is preserved verbatim'
        Assert-Test (-not [string]::IsNullOrWhiteSpace([string](Get-TestField $document 'dateReadStrategy'))) 'the read records its host strategy'
        $dateReadStrategy = [string](Get-TestField $document 'dateReadStrategy')

        $rawMoment = Read-UiMetadataUtcMoment -Value '2026-10-16T11:12:09.9003395+02:00'
        Assert-Test ($null -ne $rawMoment) 'a textual offset moment parses'
        Assert-Test ($rawMoment.UtcTicks -eq ([DateTimeOffset]::new(2026, 10, 16, 9, 12, 9, [TimeSpan]::Zero).Add([TimeSpan]::FromTicks(9003395))).UtcTicks) 'the textual offset moment normalizes to the same UTC instant'
        Assert-Test ($rawMoment.Offset -eq [TimeSpan]::Zero) 'the normalized moment carries a zero offset'

        # The typed value must carry the same fractional ticks as the raw text; dropping the
        # 9003395 fraction here is the M11 defect this suite is forbidden from relaxing.
        $typed = [DateTimeOffset]::new(([DateTime]::new(2026, 10, 16, 11, 12, 9).Add([TimeSpan]::FromTicks(9003395))), [TimeSpan]::FromHours(2))
        $typedMoment = Read-UiMetadataUtcMoment -Value $typed
        Assert-Test ($null -ne $typedMoment) 'a typed offset moment normalizes'
        Assert-Test ($typedMoment.UtcTicks -eq $rawMoment.UtcTicks) 'a typed offset moment normalizes to the same UTC instant'

        $utcStamp = [DateTime]::new(2026, 10, 16, 9, 12, 9, [DateTimeKind]::Utc).Add([TimeSpan]::FromTicks(9003395))
        $utcMoment = Read-UiMetadataUtcMoment -Value $utcStamp
        Assert-Test ($null -ne $utcMoment) 'a Utc-kind stamp normalizes'
        Assert-Test ($utcMoment.UtcTicks -eq $rawMoment.UtcTicks) 'a Utc-kind stamp normalizes to the same UTC instant'

        $localStamp = [DateTime]::new(2026, 10, 16, 11, 12, 9, [DateTimeKind]::Local).Add([TimeSpan]::FromTicks(9003395))
        $localMoment = Read-UiMetadataUtcMoment -Value $localStamp
        $expectedLocal = ([DateTimeOffset]::new($localStamp)).ToUniversalTime()
        Assert-Test ($null -ne $localMoment) 'a Local-kind stamp normalizes'
        Assert-Test ($localMoment.UtcTicks -eq $expectedLocal.UtcTicks) 'a Local-kind stamp keeps its own instant through the host offset'

        $zMoment = Read-UiMetadataUtcMoment -Value '2026-10-16T09:12:09.9003395Z'
        Assert-Test ($null -ne $zMoment) 'a textual Z moment parses'
        Assert-Test ($zMoment.UtcTicks -eq $rawMoment.UtcTicks) 'a textual Z moment normalizes to the same UTC instant'

        # Whatever this host's ConvertFrom-Json produced (the capability guard keeps ISO text
        # on both fixed hosts) must normalize to the same instant.
        $parsedValue = $null
        if ($null -ne (Get-TestField $document 'value')) { $parsedValue = (Get-TestField (Get-TestField $document 'value') 'startedUtc') }
        if ($parsedValue -is [string]) {
            Assert-Test ($parsedValue -ceq '2026-10-16T11:12:09.9003395+02:00') 'the parsed value keeps the ISO original string'
        }
        if ($null -ne $parsedValue) {
            $parsedMoment = Read-UiMetadataUtcMoment -Value $parsedValue
            Assert-Test ($null -ne $parsedMoment) 'the parsed document value yields a moment'
            Assert-Test ($parsedMoment.UtcTicks -eq $rawMoment.UtcTicks) 'the parsed document value normalizes to the same UTC instant'
        }

        $unzoneable = Read-UiMetadataUtcMoment -Value '2026-10-16T09:12:09.9003395'
        Assert-TestNull $unzoneable 'text without a zone stays unknown instead of being assumed UTC'

        $unspecified = Read-UiMetadataUtcMoment -Value ([DateTime]::new(2026, 10, 16, 9, 12, 9, [DateTimeKind]::Unspecified))
        Assert-TestNull $unspecified 'an Unspecified-kind stamp stays unknown instead of being assumed UTC'

        $unparseable = Read-UiMetadataUtcMoment -Value 'not-a-moment'
        Assert-TestNull $unparseable 'unparseable text returns null instead of a guess'

        $empty = Read-UiMetadataUtcMoment -Value ''
        Assert-TestNull $empty 'empty text returns null instead of a guess'

        $absent = Read-UiMetadataJsonDocument -Path (Join-Path $f.base 'not-here.json')
        Assert-Test ((Get-TestField $absent 'state') -ceq 'absent') 'a missing document is absent'

        return [ordered]@{
            rawUtcTicks = $rawMoment.UtcTicks
            typedUtcTicks = $typedMoment.UtcTicks
            dateReadStrategy = $dateReadStrategy
            parsedValueType = $(if ($null -eq $parsedValue) { 'null' } else { $parsedValue.GetType().FullName })
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M12: the assembled report block. The ten Runner-required keys exist, an unestablished value
# stays null beside its state, the viewport keeps its object type, the injected redactor is
# applied to HostOs as well as the evidence strings, and the block never claims a business
# status or a redaction it did not perform.
Add-MetadataCase 'M12' {
    $f = New-TestFixture 'M12'
    try {
        $product = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'product') -Note 'synthetic product directory'
        [byte[]]$exeBytes = [Text.UTF8Encoding]::new($false).GetBytes('TEST_ONLY_NOT_A_PRODUCT')
        New-TestFile -Fixture $f -Path (Join-Path $product 'TraceFold.Media.exe') -Bytes $exeBytes -Note 'synthetic exe' | Out-Null

        $provenance = Get-UiMetadataSourceProvenance -ProductExecutable (Join-Path $product 'TraceFold.Media.exe') -RepositoryRoot (Join-Path $f.base 'not-a-repo') -CandidateSha256 $null
        $identity = Get-UiMetadataProductIdentity -ProductExecutable (Join-Path $product 'TraceFold.Media.exe') -ProductDirectory $product
        $driver = Get-UiMetadataDriverIdentity -ArchivePath (Join-Path $f.base 'not-here.zip') -ExtractedExePath (Join-Path $f.base 'not-here.exe')
        $fixtures = Get-UiMetadataFixtureManifest -DeclaredAssets @() -RepositoryRoot $f.base
        $hostObservation = Get-UiMetadataHostObservation

        $block = New-UiMetadataReportBlock -SourceProvenance $provenance -ProductIdentity $identity -DriverIdentity $driver -FixtureManifest $fixtures -HostObservation $hostObservation -HostOs 'TEST_ONLY host'

        $required = @('sourceCommit', 'productHash', 'productVersion', 'driverVersion', 'driverHash', 'fixtureHashes', 'hostOs', 'dpi', 'theme', 'viewport')
        foreach ($key in $required) {
            Assert-Test ($block.Contains($key)) ('the block carries the Runner-required key ' + $key)
        }

        Assert-TestNull (Get-TestField $block 'sourceCommit') 'without git the block sourceCommit stays null'
        Assert-TestNull (Get-TestField $block 'driverVersion') 'without a manifest the block driverVersion stays null'
        Assert-TestNull (Get-TestField $block 'driverHash') 'without a manifest the block driverHash stays null'
        Assert-TestNull (Get-TestField $block 'dpi') 'without an observation the block dpi stays null'
        Assert-TestNull (Get-TestField $block 'theme') 'without an observation the block theme stays null'
        Assert-TestNull (Get-TestField $block 'viewport') 'without an observation the block viewport stays null'
        Assert-Test ((Get-TestField $block 'productHash') -ceq (Hash-TestBytes $exeBytes)) 'the block productHash carries the exe digest class'
        Assert-Test ((Get-TestField $block 'hostOs') -ceq 'TEST_ONLY host') 'the block hostOs carries the supplied value'

        $evidence = Get-TestField $block 'metadataEvidence'
        Assert-Test ($null -ne $evidence) 'the block carries a separate metadata evidence object'
        $redaction = Get-TestField $evidence 'redaction'
        Assert-Test ($null -ne $redaction) 'the evidence records its redaction state'
        Assert-Test ((Get-TestField $redaction 'state') -ceq 'not-applied-here') 'without a redactor the evidence says so'
        Assert-TestNull (Get-TestField (Get-TestField $evidence 'businessSeparation') 'status') 'the block never carries a business status'

        $redactedBlock = New-UiMetadataReportBlock -SourceProvenance $provenance -ProductIdentity $identity -DriverIdentity $driver -FixtureManifest $fixtures -HostObservation $hostObservation -HostOs 'TEST_ONLY host' -Redactor { param([AllowNull()][string]$Value) if ($null -eq $Value) { return $null }; return ($Value -replace 'TEST_ONLY', '[redacted-test]') }
        Assert-Test ((Get-TestField $redactedBlock 'hostOs') -ceq '[redacted-test] host') 'the injected redactor is applied to evidence strings'
        $redactedRedaction = Get-TestField (Get-TestField $redactedBlock 'metadataEvidence') 'redaction'
        Assert-Test ((Get-TestField $redactedRedaction 'state') -ceq 'applied-to-evidence-strings') 'the evidence records the applied redaction state'

        return [ordered]@{
            sourceCommitIsNull = ($null -eq (Get-TestField $block 'sourceCommit'))
            productHash = (Get-TestField $block 'productHash')
            keysPresent = $required.Count
        }
    }
    finally { Remove-TestFixture $f.base }
}

# M13: locale sweep. The current culture and UI culture are switched between zh-SG and en-US
# and restored; the ordinal digest and the normalized moment are identical across both.
Add-MetadataCase 'M13' {
    $f = New-TestFixture 'M13'
    try {
        $root = New-TestDirectory -Fixture $f -Path (Join-Path $f.base 'tree') -Note 'culture sweep root'
        New-TestFile -Fixture $f -Path (Join-Path $root 'B.txt') -Bytes ([byte[]](1)) -Note 'culture sweep member' | Out-Null
        New-TestFile -Fixture $f -Path (Join-Path $root 'a.txt') -Bytes ([byte[]](2)) -Note 'culture sweep member' | Out-Null

        $previousCulture = [System.Threading.Thread]::CurrentThread.CurrentCulture
        $previousUiCulture = [System.Threading.Thread]::CurrentThread.CurrentUICulture
        $applied = New-Object 'System.Collections.Generic.List[string]'
        $digests = New-Object 'System.Collections.Generic.List[string]'
        try {
            foreach ($name in @('zh-SG', 'en-US')) {
                $culture = $null
                try { $culture = [Globalization.CultureInfo]::GetCultureInfo($name) }
                catch { $culture = $null }
                if ($null -eq $culture) { $applied.Add($name + ':not-available'); continue }
                [System.Threading.Thread]::CurrentThread.CurrentCulture = $culture
                [System.Threading.Thread]::CurrentThread.CurrentUICulture = $culture
                $applied.Add($name)
                $digest = Get-UiMetadataTreeDigest -Root $root
                $digests.Add([string](Get-TestField $digest 'treeSha256'))
                $moment = Read-UiMetadataUtcMoment -Value '2026-10-16T11:12:09.9003395+02:00'
                Assert-Test ($moment.UtcTicks -eq ([DateTimeOffset]::new(2026, 10, 16, 9, 12, 9, [TimeSpan]::Zero).Add([TimeSpan]::FromTicks(9003395))).UtcTicks) ('the moment normalizes invariantly under ' + $name)
            }
        }
        finally {
            [System.Threading.Thread]::CurrentThread.CurrentCulture = $previousCulture
            [System.Threading.Thread]::CurrentThread.CurrentUICulture = $previousUiCulture
        }

        Assert-Test ($digests.Count -ge 1) 'at least one culture digest was taken'
        $first = $digests[0]
        foreach ($value in $digests) {
            Assert-Test ($value -ceq $first) 'the ordinal digest is identical across cultures'
        }

        return [ordered]@{ culturesApplied = $applied.ToArray(); digest = $first }
    }
    finally { Remove-TestFixture $f.base }
}

# Toolkit registration. Each entry is id plus label only; the body calls the single registered
# case, so the 13 case texts are not copied here. Running 'Metadata' alone and running it inside
# 'All' therefore execute the same scriptblocks, and a lost or misspelled id fails as a case
# failure instead of a silent pass.
function Get-TestCases {
    return @(
        @{ Name = 'metadata M01: one file digest class'; Body = { [void] (Invoke-MetadataCaseById -Id 'M01') } }
        @{ Name = 'metadata M02: ordinal tree digest stability'; Body = { [void] (Invoke-MetadataCaseById -Id 'M02') } }
        @{ Name = 'metadata M03: change detection'; Body = { [void] (Invoke-MetadataCaseById -Id 'M03') } }
        @{ Name = 'metadata M04: reparse refusal'; Body = { [void] (Invoke-MetadataCaseById -Id 'M04') } }
        @{ Name = 'metadata M05: build commit domain'; Body = { [void] (Invoke-MetadataCaseById -Id 'M05') } }
        @{ Name = 'metadata M06: source provenance domains'; Body = { [void] (Invoke-MetadataCaseById -Id 'M06') } }
        @{ Name = 'metadata M07: product identity classes'; Body = { [void] (Invoke-MetadataCaseById -Id 'M07') } }
        @{ Name = 'metadata M08: driver identity classes'; Body = { [void] (Invoke-MetadataCaseById -Id 'M08') } }
        @{ Name = 'metadata M09: fixture manifest'; Body = { [void] (Invoke-MetadataCaseById -Id 'M09') } }
        @{ Name = 'metadata M10: host observation'; Body = { [void] (Invoke-MetadataCaseById -Id 'M10') } }
        @{ Name = 'metadata M11: date-safe JSON reading'; Body = { [void] (Invoke-MetadataCaseById -Id 'M11') } }
        @{ Name = 'metadata M12: assembled report block'; Body = { [void] (Invoke-MetadataCaseById -Id 'M12') } }
        @{ Name = 'metadata M13: locale sweep'; Body = { [void] (Invoke-MetadataCaseById -Id 'M13') } }
    )
}

# --- standalone driver -------------------------------------------------------------------------
# The block below is the original one, unchanged except for the output root variable and the
# wrapper: it runs only when -RunRoot and -OutputDirectory were both supplied. Under the toolkit
# this branch is skipped, so dot-sourcing the file has no execution side effect.
if ($script:standaloneRun) {
$resolvedRunRoot = [IO.Path]::GetFullPath($RunRoot).TrimEnd('\')
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if (-not $resolvedOutput.StartsWith($resolvedRunRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test output must be inside the exact authorized private root.'
}
if (Test-Path -LiteralPath $resolvedOutput) { throw 'Test output must be fresh.' }
[void][IO.Directory]::CreateDirectory($resolvedOutput)
$script:outputRoot = $resolvedOutput
$script:fixtureOutputDirectory = $resolvedOutput

foreach ($registeredCase in $script:caseTable.ToArray()) { [void] (Invoke-MetadataCaseById -Id $registeredCase.Id) }

$unremoved = @($script:fixtureRegistry.Values | Where-Object { -not $_.removed })
$allCreated = @($script:fixtureRegistry.Values | ForEach-Object { $_.createdObjects })
$residueTotal = @($script:fixtureRegistry.Values | ForEach-Object { $_.residue } | Where-Object { $null -ne $_ }).Count
$fixtureClosure = @($script:fixtureRegistry.Values | ForEach-Object {
    [ordered]@{
        id = $_.id
        base = $_.base
        createdObjectCount = $_.createdObjects.Count
        retainedCount = @($_.createdObjects | Where-Object { $_.retained }).Count
        residueCount = $_.residue.Count
        removed = $_.removed
        objects = @($_.createdObjects | ForEach-Object {
            [ordered]@{
                path = $_.path
                kind = $_.kind
                note = $_.note
                retained = [bool]$_.retained
                retainedReason = $_.retainedReason
                disposition = $_.disposition
            }
        })
    }
})
$summary = [ordered]@{
    schemaVersion = 1
    utc = [DateTime]::UtcNow.ToString('o')
    helperPath = $script:helperPath
    cases = $script:results.ToArray()
    assertions = $script:checks
    passed = @($script:results | Where-Object { $_.status -ceq 'Passed' }).Count
    failed = @($script:results | Where-Object { $_.status -ceq 'Failed' }).Count
    notEstablished = @($script:results | Where-Object { $_.status -ceq 'NotEstablished' }).Count
    cleanup = $script:cleanupFacts.ToArray()
    dispatch = $script:dispatchFacts.ToArray()
    registeredFixtureCount = $script:fixtureRegistry.Count
    registeredCreationCount = @($allCreated).Count
    residueTotal = $residueTotal
    fixtureClosure = $fixtureClosure
    unremovedFixtures = $unremoved
    runtimeCounts = [ordered]@{ testHost = 1; product = 0; driver = 0; support = 0; realExperiments = 0 }
    limitations = @(
        'No product, driver, support, registry, theme or UI execution; every value is a controlled test-owned fact.',
        'The suite proves helper behavior and block shape only; it is not a real acceptance-chain connection.',
        'Culture cases record which cultures the host actually applied; the invariance expectation is fixed.',
        'Fixture closure is proven per registered creation: removed, deliberately retained as evidence, kept alive by retained evidence, or residue; a vanished owned directory alone is never the proof of closure.'
    )
}
Write-TestJson -Path (Join-Path $script:outputRoot 'summary.json') -Value $summary
[Console]::Out.WriteLine(('RMETA-R2 checks={0} passed={1} failed={2} notEstablished={3} residue={4}' -f $summary.assertions, $summary.passed, $summary.failed, $summary.notEstablished, $residueTotal))
if ($summary.failed -gt 0 -or $summary.notEstablished -gt 0 -or $unremoved.Count -gt 0 -or $residueTotal -gt 0 -or $summary.passed -ne $script:expectedCases) { exit 1 }
exit 0
}
