# TraceFold UI acceptance - Task 13 safety gates.
#
# One implementation, two call sites:
#   * the public entry asks the journal gate before it runs any preflight, so an interrupted
#     theme change is resolved or refused before a product can start;
#   * the visual matrix uses the same command wrapper and the same response validators for its
#     apply and restore rounds.
#
# The reviewed WindowsAppThemeCoordinator (H13-UNIT-02 final) is the only producer. The support
# CLI turns a request into a coordinator call and returns an explicit JSON projection; this module
# validates that projection, and never invents a passing response. The hand-back receipt is a
# capability: it lives only on the round object and in the child's stdin, never in a decision, a
# report or a persisted command.
#
# Declared command surface:
#   theme inspect --journal <absolute-path> [--owner-*] [--*-binding options] [--handback-token -]
#   theme apply   --journal <absolute-path> --mode light|dark --owner-* --product-* --toolkit-*
#   theme restore --journal <absolute-path> [--disposition startup|handback] [--handback-token -]
#
# Declared response facts (required, typed):
#   inspect: journalState string in absent|pending|restore-required|foreign-live|unknown;
#            ownerState string in none|live|dead|pid-reused|unknown while a journal exists.
#   apply:   applied bool, mode string, journalState string, journalWritten bool,
#            registryWritten bool, broadcast.sent bool, original {...}, owner {...},
#            binding {...}, rejection string, adapters production|fixture,
#            handbackToken string (capability; never persisted by the consumer).
#   restore: restored bool, noAction bool, journalState string, journalPresent bool,
#            journalRemoved bool, journalChecksumValid bool, disposition string,
#            original { present, valueKind, bytes },
#            readback { present, valueKind, bytes, matchesOriginal bool },
#            broadcast.sent bool, ownerState string, bindingMatchesJournal bool|null.
#
# A boolean that arrives as the string 'false', a null, or a missing property is a refusal, not a
# false value: the three states stay distinct all the way into the evidence.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Workflows.psm1') -DisableNameChecking

$script:visualGateJournalStates = @('absent', 'pending', 'restore-required', 'foreign-live', 'unknown')
$script:visualGateOwnerStates = @('none', 'live', 'dead', 'pid-reused', 'unknown')

<#
.SYNOPSIS
    Decides whether the private-journal / fixture selection is a complete, legal combination.

.DESCRIPTION
    Two settings exist on this surface: the acceptance fixture adapters (a root plus an explicit
    per-call licence) and a private journal. Neither is a default; both are opt-in overrides of the
    production path. An override is only meaningful as a whole set, so a partial selection is
    refused before anything looks at the journal, runs a preflight, creates an adapter or starts a
    child: half a boundary is not a boundary.

      nothing selected              production default, allowed
      the canonical journal only    production default, allowed (the one path the plan fixes)
      any other journal only        incomplete: a private journal without the adapter boundary
                                    would still restore into the production registry
      fixture root without licence  adapters.fixture (the same refusal the command already gave)
      licence without a root        incomplete
      root and licence, no journal  incomplete, only where the caller distinguishes explicit input
      root and licence and journal  allowed when the journal sits directly in the root, the root is
                                    an absolute path under TEMP and carries the reviewed prefix

    The caller passes the values it was given, never a resolved default: a computed production
    journal is not an override. The gate and the command receive an already resolved journal, so
    they compare it with the canonical path instead of trusting the caller's word: only that one
    path is the production default, everything else is a private override that needs the boundary.
    Reparse points and the missing-root case are refused here, before any presence classification
    could read a root-wide "absent" and clear the gate; the C# boundary re-checks the same facts
    before it creates an adapter.

    With -RequireExplicitJournal the caller is the one that holds both a default and an override
    (the entry and the scenario), so "root and licence without a journal" is incomplete.
#>
function Test-UiAcceptanceThemeOverride {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string] $JournalPath = '',
        [AllowEmptyString()][string] $FixtureRoot = '',
        [switch] $AllowFixture,
        [switch] $RequireExplicitJournal
    )

    $decision = [ordered] @{ ok = $false; code = '' }
    $hasJournal = -not [string]::IsNullOrWhiteSpace($JournalPath)
    $hasRoot = -not [string]::IsNullOrWhiteSpace($FixtureRoot)

    if (-not $hasRoot -and -not $AllowFixture) {
        if ($hasJournal) {
            # The gate and the command are handed an already resolved journal, so the caller
            # cannot say whether it was an override. Only the canonical path is the production
            # default; anything else is a private journal and needs the fixture boundary with it.
            $isCanonical = $false
            try {
                $canonical = [IO.Path]::GetFullPath((Get-UiAcceptanceThemeJournalPath))
                $candidate = [IO.Path]::GetFullPath($JournalPath)
                $isCanonical = [string]::Equals(
                    $candidate.TrimEnd([IO.Path]::DirectorySeparatorChar),
                    $canonical.TrimEnd([IO.Path]::DirectorySeparatorChar),
                    [StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                # A path that cannot be normalised, or a machine without the per-user folder,
                # cannot be shown to be the default: treat it as an unproven private override.
                $isCanonical = $false
            }
            if (-not $isCanonical) {
                $decision.code = 'theme.override_incomplete'
                return [pscustomobject] $decision
            }
        }
        $decision.ok = $true
        return [pscustomobject] $decision
    }

    if ($hasRoot -and -not $AllowFixture) {
        $decision.code = 'adapters.fixture'
        return [pscustomobject] $decision
    }
    if ($AllowFixture -and -not $hasRoot) {
        $decision.code = 'theme.override_incomplete'
        return [pscustomobject] $decision
    }
    if ($RequireExplicitJournal -and -not $hasJournal) {
        $decision.code = 'theme.override_incomplete'
        return [pscustomobject] $decision
    }

    if ($FixtureRoot -notmatch '^(?:[A-Za-z]:[\\/]|\\\\)') {
        $decision.code = 'theme.override_fixture_root'
        return [pscustomobject] $decision
    }
    try {
        $root = [IO.Path]::GetFullPath($FixtureRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
        $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    }
    catch {
        $decision.code = 'theme.override_fixture_root'
        return [pscustomobject] $decision
    }
    $separator = [IO.Path]::DirectorySeparatorChar
    $leaf = [IO.Path]::GetFileName($root)
    if (-not $root.StartsWith($temp + $separator, [StringComparison]::OrdinalIgnoreCase) -or
        -not $leaf.StartsWith('tracefold-theme-fixture-', [StringComparison]::OrdinalIgnoreCase)) {
        $decision.code = 'theme.override_fixture_root'
        return [pscustomobject] $decision
    }
    # A reviewed prefix is a name, not a fixture root. A directory that is missing, unreadable or
    # reached through a reparse point would let the presence check answer "absent" and clear the
    # gate before the C# boundary ever ran, so the same facts are settled here first. The walk
    # stops at TEMP, exactly where the boundary comparison stops.
    try {
        if (-not [IO.Directory]::Exists($root)) {
            $decision.code = 'theme.override_fixture_root'
            return [pscustomobject] $decision
        }
        $cursor = $root
        while ($true) {
            if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                $decision.code = 'theme.override_fixture_root'
                return [pscustomobject] $decision
            }
            if ([string]::Equals($cursor, $temp, [StringComparison]::OrdinalIgnoreCase)) { break }
            $parentDirectory = [IO.Path]::GetDirectoryName($cursor)
            if ([string]::IsNullOrWhiteSpace($parentDirectory)) { break }
            $cursor = $parentDirectory.TrimEnd($separator)
        }
    }
    catch {
        $decision.code = 'theme.override_fixture_root'
        return [pscustomobject] $decision
    }
    if ($hasJournal) {
        try { $journalFull = [IO.Path]::GetFullPath($JournalPath) }
        catch {
            $decision.code = 'theme.override_incomplete'
            return [pscustomobject] $decision
        }
        $parent = ([IO.Path]::GetDirectoryName($journalFull)).TrimEnd($separator)
        if (-not [string]::Equals($parent, $root, [StringComparison]::OrdinalIgnoreCase)) {
            $decision.code = 'theme.override_journal_outside_fixture'
            return [pscustomobject] $decision
        }
        # An existing journal that is itself a reparse point is not the file the boundary would
        # read, so the selection is refused before a presence classification could trust it.
        try {
            if ([IO.File]::Exists($journalFull) -and
                (([IO.File]::GetAttributes($journalFull) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
                $decision.code = 'theme.override_fixture_root'
                return [pscustomobject] $decision
            }
        }
        catch {
            $decision.code = 'theme.override_fixture_root'
            return [pscustomobject] $decision
        }
    }

    $decision.ok = $true
    return [pscustomobject] $decision
}

<#
.SYNOPSIS
    Returns the one per-user recovery journal path the plan fixes.

.DESCRIPTION
    The journal belongs to the acceptance toolkit, not to the product data root, so a product
    reset can never delete the only evidence of an interrupted theme change.

    A caller that is bound to a private journal (an acceptance fixture run) may pass that journal
    explicitly. The override is a path, not a second rule: the same presence classification, the
    same gate and the same command surface run against it. Production callers pass nothing and
    keep the one per-user path.
#>
function Get-UiAcceptanceThemeJournalPath {
    [CmdletBinding()]
    param([AllowEmptyString()][string] $JournalPath = '')

    if (-not [string]::IsNullOrWhiteSpace($JournalPath)) {
        return [IO.Path]::GetFullPath($JournalPath)
    }
    $local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($local)) {
        throw 'The per-user local application data folder is unavailable.'
    }
    return [IO.Path]::GetFullPath((Join-Path $local 'TraceFold.UiAcceptance\theme-recovery.json'))
}

<#
.SYNOPSIS
    Captures the real persistent harness identity a theme request is made from.

.DESCRIPTION
    The owning process is this long-lived harness, not the short-lived support child. The identity
    is three typed facts: the PID, the process start time as Int64 UTC ticks, and a context token
    generated for this execution. If the start time cannot be read the identity stays 'unknown'
    with its error, and every caller must refuse rather than send a partial identity.
#>
function Get-UiAcceptanceHarnessIdentity {
    [CmdletBinding()]
    param([string] $Context = '')

    $identity = [ordered] @{
        state = 'unknown'
        error = ''
        pid = $null
        startUtcTicks = $null
        context = ''
    }
    try {
        $process = Get-Process -Id $PID -ErrorAction Stop
        $identity.pid = [int] $PID
        $identity.startUtcTicks = [long] $process.StartTime.ToUniversalTime().Ticks
        if ([string]::IsNullOrWhiteSpace($Context)) { $Context = [guid]::NewGuid().ToString('N') }
        $identity.context = [string] $Context
        $identity.state = 'captured'
    }
    catch {
        $identity.error = [string] $_.Exception.Message
    }
    return [pscustomobject] $identity
}

<#
.SYNOPSIS
    Digests one file for a binding fact, or returns null with a state.

.DESCRIPTION
    Only a regular, non-reparse file is digested. A missing, unreadable, directory or reparse
    target stays 'unknown' with its error; it is never replaced by an empty hash.
#>
function Get-UiAcceptanceBindingFileDigest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Path)

    $fact = [ordered] @{ state = 'unknown'; error = ''; bytes = $null; sha256 = '' }
    try {
        $full = [IO.Path]::GetFullPath($Path)
        if (-not [IO.File]::Exists($full)) { $fact.error = 'file_missing'; return [pscustomobject] $fact }
        $info = [IO.FileInfo]::new($full)
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $fact.error = 'file_reparse'
            return [pscustomobject] $fact
        }
        $fact.bytes = [long] $info.Length
        $hasher = [Security.Cryptography.SHA256]::Create()
        try {
            $stream = [IO.File]::OpenRead($full)
            try { $fact.sha256 = ([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-', '') }
            finally { $stream.Dispose() }
        }
        finally { $hasher.Dispose() }
        $fact.state = 'captured'
    }
    catch {
        $fact.error = [string] $_.Exception.Message
    }
    return [pscustomobject] $fact
}

<#
.SYNOPSIS
    Builds the product and toolkit binding the request states.

.DESCRIPTION
    Product binding is the published executable: one member, sha256, its exact bytes and digest.
    Toolkit binding covers the two Task 13 members that decide the theme behaviour: this gate
    module and the support CLI that will perform the write. The digest is sha256 over the
    canonical two-line member text, so a different member or a different byte changes it.
#>
function Get-UiAcceptanceThemeBinding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ProductExecutable,
        [Parameter(Mandatory)][string] $SupportExecutable,
        [Parameter(Mandatory)][string] $GateModulePath
    )

    $binding = [ordered] @{
        state = 'unknown'
        error = ''
        productHashKind = 'sha256'
        productMembers = $null
        productMemberNames = @()
        productSha256 = ''
        productBytes = $null
        toolkitHashKind = 'sha256'
        toolkitMembers = $null
        toolkitMemberNames = @()
        toolkitSha256 = ''
        members = @()
    }
    $product = Get-UiAcceptanceBindingFileDigest -Path $ProductExecutable
    $gate = Get-UiAcceptanceBindingFileDigest -Path $GateModulePath
    $support = Get-UiAcceptanceBindingFileDigest -Path $SupportExecutable
    if ($product.state -cne 'captured' -or $gate.state -cne 'captured' -or $support.state -cne 'captured') {
        $binding.error = ('product=' + $product.state + '/' + $product.error +
            '; gate=' + $gate.state + '/' + $gate.error +
            '; support=' + $support.state + '/' + $support.error)
        return [pscustomobject] $binding
    }

    $memberText = "visual-gate:$($gate.sha256)`nsupport:$($support.sha256)"
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $binding.toolkitSha256 = ([BitConverter]::ToString(
                $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($memberText)))).Replace('-', '')
    }
    finally { $hasher.Dispose() }

    $binding.productMembers = 1
    # The member name is the file the digest covers, not a table entry: the product digest is the
    # published executable's SHA-256, and the toolkit digest covers the two named members below.
    $binding.productMemberNames = @([IO.Path]::GetFileName($ProductExecutable))
    $binding.productSha256 = [string] $product.sha256
    $binding.productBytes = [long] $product.bytes
    $binding.toolkitMembers = 2
    $binding.toolkitMemberNames = @('visual-gate', 'support')
    $binding.members = @(
        'visual-gate|' + [string] $gate.bytes + '|' + [string] $gate.sha256,
        'support|' + [string] $support.bytes + '|' + [string] $support.sha256
    )
    $binding.state = 'captured'
    return [pscustomobject] $binding
}

<#
.SYNOPSIS
    Classifies the journal as present, definitely absent, or unknown.

.DESCRIPTION
    File.Exists returning false is not proof of absence: it also answers false for an unreadable
    path, a type error or a reparse target. This reader enumerates the exact parent instead. The
    grandparent is enumerated first so a missing parent directory is a definite absence; only a
    parent that exists and can be enumerated can produce 'absent' for the leaf. A reparse point,
    a directory leaf, a duplicate name, an unreadable entry or any exception stays 'unknown',
    and every caller must treat unknown as a refusal.
#>
function Get-UiAcceptanceJournalPresence {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $JournalPath)

    $presence = [ordered] @{
        state = 'unknown'
        entryKind = 'unknown'
        parentState = 'unknown'
        error = ''
        entryCount = $null
        checkedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    $journal = [IO.Path]::GetFullPath($JournalPath)
    $parent = [IO.Path]::GetDirectoryName($journal)
    $grand = [IO.Path]::GetDirectoryName($parent)
    if ([string]::IsNullOrWhiteSpace($parent) -or [string]::IsNullOrWhiteSpace($grand)) {
        $presence.error = 'path_not_absolute'
        return [pscustomobject] $presence
    }

    try {
        $grandEntries = @([IO.Directory]::EnumerateFileSystemEntries($grand))
        $parentName = [IO.Path]::GetFileName($parent)
        $parentMatches = @($grandEntries | Where-Object {
                [string]::Equals([IO.Path]::GetFileName([string] $_), $parentName, [StringComparison]::OrdinalIgnoreCase)
            })
        if ($parentMatches.Count -eq 0) {
            $presence.state = 'absent'
            $presence.entryKind = 'none'
            $presence.parentState = 'absent'
            $presence.entryCount = 0
            return [pscustomobject] $presence
        }
        if ($parentMatches.Count -gt 1) {
            $presence.error = 'duplicate_parent'
            return [pscustomobject] $presence
        }

        $parentInfo = [IO.DirectoryInfo]::new($parent)
        if (($parentInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $presence.error = 'parent_reparse'
            return [pscustomobject] $presence
        }
        if (-not $parentInfo.Exists) {
            $presence.error = 'parent_unreadable'
            return [pscustomobject] $presence
        }
        $parentInfo.Refresh()

        $entries = @([IO.Directory]::EnumerateFileSystemEntries($parent))
        $presence.parentState = 'enumerable'
        $presence.entryCount = [int] $entries.Count
        $leafName = [IO.Path]::GetFileName($journal)
        $leafMatches = @($entries | Where-Object {
                [string]::Equals([IO.Path]::GetFileName([string] $_), $leafName, [StringComparison]::OrdinalIgnoreCase)
            })
        if ($leafMatches.Count -eq 0) {
            $presence.state = 'absent'
            $presence.entryKind = 'none'
            return [pscustomobject] $presence
        }
        if ($leafMatches.Count -gt 1) {
            $presence.error = 'duplicate_leaf'
            return [pscustomobject] $presence
        }

        $leaf = [string] $leafMatches[0]
        $leafInfo = [IO.FileInfo]::new($leaf)
        if (($leafInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $presence.entryKind = 'reparse'
            $presence.error = 'leaf_reparse'
            return [pscustomobject] $presence
        }
        if ([IO.Directory]::Exists($leaf)) {
            $presence.entryKind = 'directory'
            $presence.error = 'leaf_directory'
            return [pscustomobject] $presence
        }
        if (-not [IO.File]::Exists($leaf)) {
            $presence.error = 'leaf_unreadable'
            return [pscustomobject] $presence
        }
        $presence.state = 'present'
        $presence.entryKind = 'file'
    }
    catch {
        $presence.error = [string] $_.Exception.Message
    }
    return [pscustomobject] $presence
}

<#
.SYNOPSIS
    Reads the producer's structured refusal code.

.DESCRIPTION
    Only the exact structured code 'theme_coordinator_not_integrated' is recognized, and only
    when it arrives as a string field. Prose or a substring is not a producer statement and is
    reported as 'unstructured' instead of being promoted to an environment limit.
#>
function Get-UiAcceptanceThemeProducerState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $Result)

    $state = [ordered] @{ state = 'unknown'; code = ''; notIntegrated = $false; error = '' }
    if ($null -eq $Result) { $state.error = 'no_result'; return [pscustomobject] $state }
    $data = Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'Data'
    if ($null -eq $data) { $state.error = 'no_data'; return [pscustomobject] $state }
    $codeField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'errorCode' -Kind 'string'
    if ($codeField.state -cne 'ok') {
        $state.error = 'error_code.' + $codeField.state
        return [pscustomobject] $state
    }
    $state.code = [string] $codeField.value
    if ($state.code -ceq 'theme_coordinator_not_integrated') {
        $state.state = 'not-integrated'
        $state.notIntegrated = $true
    }
    else {
        $state.state = 'declared'
    }
    return [pscustomobject] $state
}

<#
.SYNOPSIS
    Reads a property without assuming it exists, and reports its state separately from its value.

.DESCRIPTION
    'missing', 'null', 'wrong-type' and 'ok' are four different answers. Collapsing them is how a
    string 'false' or a null boolean used to be read as a real decision.
#>
function Get-UiAcceptanceVisualGateField {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $InputObject,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][ValidateSet('boolean', 'string', 'integer', 'object')][string] $Kind
    )

    $state = [ordered] @{
        name = $Name
        kind = $Kind
        state = 'missing'
        present = $false
        isNull = $false
        typeOk = $false
        value = $null
    }
    if ($null -eq $InputObject) { return [pscustomobject] $state }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return [pscustomobject] $state }
    $state.present = $true

    $value = $property.Value
    if ($null -eq $value) {
        $state.isNull = $true
        $state.state = 'null'
        return [pscustomobject] $state
    }

    $typeOk = $false
    switch ($Kind) {
        'boolean' { $typeOk = ($value -is [bool]) }
        'string' { $typeOk = ($value -is [string]) }
        'integer' {
            $typeOk = ($value -is [int]) -or ($value -is [long]) -or ($value -is [int16]) -or
                ($value -is [byte]) -or ($value -is [uint32]) -or ($value -is [uint16])
        }
        'object' {
            $typeOk = ($value -is [System.Management.Automation.PSCustomObject]) -or
                ($value -is [System.Collections.IDictionary])
        }
    }
    $state.typeOk = [bool] $typeOk
    $state.value = $value
    $state.state = if ($typeOk) { 'ok' } else { 'wrong-type' }
    return [pscustomobject] $state
}

<#
.SYNOPSIS
    Reads a property value with a caller supplied default, never throwing on absence.
#>
function Get-UiAcceptanceVisualGateValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $InputObject,
        [Parameter(Mandatory)][string] $Name,
        [AllowNull()] $Default = $null
    )

    if ($null -eq $InputObject) { return $Default }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}

<#
.SYNOPSIS
    Answers whether a value is an integer type, without converting it first.
#>
function Test-UiAcceptanceVisualGateInteger {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $Value)

    if ($null -eq $Value) { return $false }
    return ($Value -is [int]) -or ($Value -is [long]) -or ($Value -is [int16]) -or
        ($Value -is [byte]) -or ($Value -is [uint32]) -or ($Value -is [uint16])
}

<#
.SYNOPSIS
    Reads the exit facts of a theme command result without inventing a zero.
#>
function Get-UiAcceptanceVisualGateExit {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $Result)

    $known = $false
    $code = $null
    if ($null -ne $Result -and $null -ne $Result.PSObject.Properties['ExitKnown']) {
        $flag = $Result.PSObject.Properties['ExitKnown'].Value
        if ($flag -is [bool] -and $flag) {
            $exitProperty = $Result.PSObject.Properties['ExitCode']
            if ($null -ne $exitProperty -and (Test-UiAcceptanceVisualGateInteger -Value $exitProperty.Value)) {
                $known = $true
                $code = [int] $exitProperty.Value
            }
        }
    }
    return [pscustomobject] @{ Known = $known; Code = $code }
}

<#
.SYNOPSIS
    Reads the producer's structured refusal code.

.DESCRIPTION
    Replaces the former prose substring test. Only the exact structured code is a producer
    statement; an arbitrary message that happens to contain the words is not.
#>

<#
.SYNOPSIS
    Runs one theme command through the shared controlled support path.

.DESCRIPTION
    The wrapper never converts an unknown exit into zero: a command that could not be started and
    a command whose exit could not be read both keep ExitKnown = $false and ExitCode = $null. The
    stdout bytes and hash are the decoded text re-encoded for evidence, and are named that way;
    they are never described as the child's native bytes.

    The InvokeSupport seam exists so the behaviour tests can drive this exact function with a
    counted stub. Production callers never pass it.
#>
function Invoke-UiAcceptanceThemeCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string] $SupportPath,
        [Parameter(Mandatory)][ValidateSet('inspect', 'apply', 'restore')][string] $Action,
        [Parameter(Mandatory)][string] $JournalPath,
        [string] $Mode = '',
        [int] $TimeoutMs = 60000,
        [AllowNull()] $OwnerIdentity,
        [AllowNull()] $Binding,
        [ValidateSet('startup', 'handback')][string] $Disposition = 'startup',
        [AllowEmptyString()][string] $HandbackToken = '',
        [AllowEmptyString()][string] $FixtureRoot = '',
        [switch] $AllowFixture,
        [scriptblock] $InvokeSupport
    )

    $journal = [IO.Path]::GetFullPath($JournalPath)
    $arguments = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in @('theme', $Action, '--journal', $journal)) { [void] $arguments.Add([string] $item) }
    if (-not [string]::IsNullOrWhiteSpace($Mode)) {
        [void] $arguments.Add('--mode')
        [void] $arguments.Add([string] $Mode)
    }
    if ($Action -ceq 'restore') {
        [void] $arguments.Add('--disposition')
        [void] $arguments.Add([string] $Disposition)
    }
    # A partial override never reaches an identity check, a journal read or a child: the whole
    # selection is validated once, with the same rule the entry and the scenario apply.
    $override = Test-UiAcceptanceThemeOverride -JournalPath $journal -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture
    # Identity and binding are typed inputs, not echoes: an unknown identity is refused before
    # the child starts, so a partial request can never be mistaken for a real one.
    $identityRefused = if ($override.ok) { '' } else { [string] $override.code }
    if ([string]::IsNullOrWhiteSpace($identityRefused) -and $null -ne $OwnerIdentity) {
        if ([string] $OwnerIdentity.state -cne 'captured') {
            $identityRefused = 'owner_identity_unknown'
        }
        else {
            foreach ($pair in @(
                    @('--owner-pid', [string] $OwnerIdentity.pid),
                    @('--owner-start-utc-ticks', [string] $OwnerIdentity.startUtcTicks),
                    @('--owner-context', [string] $OwnerIdentity.context))) {
                [void] $arguments.Add([string] $pair[0])
                [void] $arguments.Add([string] $pair[1])
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($identityRefused) -and $null -ne $Binding) {
        if ([string] $Binding.state -cne 'captured') {
            $identityRefused = 'binding_unknown'
        }
        else {
            foreach ($pair in @(
                    @('--product-hash-kind', [string] $Binding.productHashKind),
                    @('--product-members', [string] $Binding.productMembers),
                    @('--product-member-names', (($Binding.productMemberNames | ForEach-Object { [string] $_ }) -join ';')),
                    @('--product-sha256', [string] $Binding.productSha256),
                    @('--toolkit-hash-kind', [string] $Binding.toolkitHashKind),
                    @('--toolkit-members', [string] $Binding.toolkitMembers),
                    @('--toolkit-member-names', (($Binding.toolkitMemberNames | ForEach-Object { [string] $_ }) -join ';')),
                    @('--toolkit-sha256', [string] $Binding.toolkitSha256))) {
                [void] $arguments.Add([string] $pair[0])
                [void] $arguments.Add([string] $pair[1])
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($identityRefused)) {
        if ($Disposition -ceq 'handback' -and $Action -ceq 'restore') {
            if ($HandbackToken -cnotmatch '^[0-9A-Fa-f]{64}$') {
                # Missing or malformed receipt is refused before the child starts. The value is
                # never placed in the error text or in the argument array.
                $identityRefused = 'handback_token_missing'
            }
            else {
                # The sentinel, never the value: argv is process-visible and is the first thing a
                # command record copies. The receipt itself travels on stdin.
                [void] $arguments.Add('--handback-token')
                [void] $arguments.Add('-')
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($FixtureRoot)) {
            # The process licence is checked again inside the CLI; it never substitutes for this
            # per-call permission. Without the explicit switch no argument is added and no child
            # is started at all.
            if (-not $AllowFixture) {
                $identityRefused = 'adapters.fixture'
            }
            else {
                [void] $arguments.Add('--fixture-root')
                [void] $arguments.Add([string] $FixtureRoot)
            }
        }
    }
    $argumentArray = $arguments.ToArray()
    if (-not [string]::IsNullOrEmpty($HandbackToken) -and $argumentArray -contains $HandbackToken) {
        throw 'visual.theme_token_in_argv'
    }

    # The receipt is the one thing that travels on stdin, and it travels the same way whichever
    # transport runs: computing it here keeps the default wrapper and the injected seam from
    # drifting into two different answers about what the child received.
    $standardInput = ''
    if ($Action -ceq 'restore' -and $Disposition -ceq 'handback') { $standardInput = $HandbackToken + "`n" }

    $ran = $false
    $result = $null
    $errorText = ''
    if (-not [string]::IsNullOrWhiteSpace($identityRefused)) {
        $errorText = $identityRefused
    }
    elseif ($null -eq $InvokeSupport) {
        if ([string]::IsNullOrWhiteSpace($SupportPath) -or -not [IO.File]::Exists($SupportPath)) {
            $errorText = 'The support CLI is missing.'
        }
        else {
            $ran = $true
            try {
                $result = Invoke-UiAcceptanceSupportCommand -SupportPath $SupportPath `
                    -Arguments $argumentArray -TimeoutMs $TimeoutMs `
                    -StandardInput $standardInput
            }
            catch {
                $errorText = [string] $_.Exception.Message
            }
        }
    }
    else {
        $ran = $true
        # The seam carries the same stdin payload as the default wrapper. A stub that declares
        # only two parameters keeps working (the third lands in its $args), and a witness that
        # forwards to the real wrapper proves the receipt reaches a real child instead of being
        # dropped at the seam.
        try { $result = & $InvokeSupport $SupportPath $argumentArray $standardInput }
        catch { $errorText = [string] $_.Exception.Message }
    }

    $exitKnown = $false
    $exitCode = $null
    $stdout = ''
    $stderr = ''
    if ($null -ne $result) {
        if ($null -ne $result.PSObject.Properties['ExitCode'] -and
            (Test-UiAcceptanceVisualGateInteger -Value $result.PSObject.Properties['ExitCode'].Value)) {
            $exitCode = [int] $result.PSObject.Properties['ExitCode'].Value
            $exitKnown = $true
        }
        $stdoutValue = Get-UiAcceptanceVisualGateValue -InputObject $result -Name 'Stdout' -Default ''
        if ($null -ne $stdoutValue) { $stdout = [string] $stdoutValue }
        $stderrValue = Get-UiAcceptanceVisualGateValue -InputObject $result -Name 'Stderr' -Default ''
        if ($null -ne $stderrValue) { $stderr = [string] $stderrValue }
    }

    $data = $null
    $parseState = 'not-run'
    if ($ran -and $null -ne $result) {
        if ([string]::IsNullOrWhiteSpace($stdout)) {
            $parseState = 'empty'
        }
        else {
            try {
                $data = $stdout | ConvertFrom-Json
                $parseState = 'parsed'
            }
            catch {
                $parseState = 'invalid'
                $data = $null
            }
        }
    }

    $stdoutBytes = 0
    $stdoutSha256 = ''
    if ($ran) {
        $encoded = [Text.Encoding]::UTF8.GetBytes($stdout)
        $stdoutBytes = [int] $encoded.Length
        $hasher = [Security.Cryptography.SHA256]::Create()
        try {
            $stdoutSha256 = ([BitConverter]::ToString($hasher.ComputeHash($encoded))).Replace('-', '')
        }
        finally { $hasher.Dispose() }
    }

    return [pscustomobject] @{
        Action = $Action
        Ran = $ran
        ExitKnown = $exitKnown
        ExitCode = $exitCode
        Stdout = $stdout
        Stderr = $stderr
        ErrorText = $errorText
        Data = $data
        ParseState = $parseState
        StdoutTextBytes = $stdoutBytes
        StdoutTextSha256 = $stdoutSha256
    }
}

<#
.SYNOPSIS
    Validates an inspect response: a state word, and an owner word while a journal exists.
#>
function Test-UiAcceptanceThemeAdapter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $Data,
        [switch] $AllowFixture,
        # The adapter the caller's own selection must produce. A fixture selection that receives a
        # production receipt (or the other way round) proves the command did not run where the
        # caller meant it to, so the answer is refused instead of accepted as a success.
        [ValidateSet('', 'production', 'fixture')][string] $Expected = ''
    )

    $field = Get-UiAcceptanceVisualGateField -InputObject $Data -Name 'adapters' -Kind 'string'
    if ($field.state -cne 'ok') {
        return [pscustomobject] @{ ok = $false; failure = 'adapters.' + $field.state; value = '' }
    }
    $value = [string] $field.value
    if ($value -ceq 'production') {
        if ($Expected -ceq 'fixture') {
            return [pscustomobject] @{ ok = $false; failure = 'adapters.mismatch'; value = $value }
        }
        return [pscustomobject] @{ ok = $true; failure = ''; value = $value }
    }
    if ($value -ceq 'fixture') {
        # The missing licence is the more specific refusal: a caller that never opened the
        # fixture boundary is told so before any selection comparison is made.
        if (-not $AllowFixture) {
            return [pscustomobject] @{ ok = $false; failure = 'adapters.fixture'; value = $value }
        }
        if ($Expected -ceq 'production') {
            return [pscustomobject] @{ ok = $false; failure = 'adapters.mismatch'; value = $value }
        }
        return [pscustomobject] @{ ok = $true; failure = ''; value = $value }
    }
    # Any other word is neither the production adapter nor the named fixture one.
    return [pscustomobject] @{ ok = $false; failure = 'adapters.unknown'; value = $value }
}

function Test-UiAcceptanceThemeInspectResponse {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Result,
        [switch] $AllowFixture,
        # What the caller's selection must produce; empty keeps the unconstrained call.
        [ValidateSet('', 'production', 'fixture')][string] $ExpectedAdapter = ''
    )

    $decision = [ordered] @{
        ok = $false
        failure = ''
        notIntegrated = $false
        exitKnown = $false
        exitCode = $null
        parseState = ''
        ran = $false
        adapters = ''
        journalState = ''
        ownerState = ''
        startupRecoveryAllowed = $false
        handbackAccepted = $false
        handbackRejection = ''
    }

    $decision.parseState = [string] (Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'ParseState' -Default '')
    $decision.ran = [bool] (Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'Ran' -Default $false)
    $producer = Get-UiAcceptanceThemeProducerState -Result $Result
    $decision.notIntegrated = [bool] $producer.notIntegrated
    $exit = Get-UiAcceptanceVisualGateExit -Result $Result
    $decision.exitKnown = $exit.Known
    $decision.exitCode = $exit.Code

    if (-not $decision.ran) { $decision.failure = 'not-run'; return [pscustomobject] $decision }
    if (-not $exit.Known) { $decision.failure = 'exit.unknown'; return [pscustomobject] $decision }
    if ($exit.Code -ne 0) { $decision.failure = 'exit.' + [string] $exit.Code; return [pscustomobject] $decision }

    $data = Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'Data'
    if ($null -eq $data) { $decision.failure = 'response.' + $decision.parseState; return [pscustomobject] $decision }

    $adapter = Test-UiAcceptanceThemeAdapter -Data $data -AllowFixture:$AllowFixture -Expected $ExpectedAdapter
    if (-not $adapter.ok) { $decision.failure = $adapter.failure; return [pscustomobject] $decision }
    $decision.adapters = [string] $adapter.value

    $journalField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'journalState' -Kind 'string'
    if ($journalField.state -cne 'ok') {
        $decision.failure = 'journal_state.' + $journalField.state
        return [pscustomobject] $decision
    }
    $decision.journalState = [string] $journalField.value
    if ($script:visualGateJournalStates -cnotcontains $decision.journalState) {
        $decision.failure = 'journal_state.unknown-value'
        return [pscustomobject] $decision
    }

    $ownerField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'ownerState' -Kind 'string'
    if ($decision.journalState -cne 'absent') {
        if ($ownerField.state -cne 'ok') {
            $decision.failure = 'owner_state.' + $ownerField.state
            return [pscustomobject] $decision
        }
        $decision.ownerState = [string] $ownerField.value
        if ($script:visualGateOwnerStates -cnotcontains $decision.ownerState) {
            $decision.failure = 'owner_state.unknown-value'
            return [pscustomobject] $decision
        }
    }
    elseif ($ownerField.state -ceq 'ok') {
        $decision.ownerState = [string] $ownerField.value
    }

    # These two permissions are evidence, not a pass condition here: the journal gate decides
    # whether it may restore, and a missing value stays false.
    foreach ($pair in @(
            @('startupRecoveryAllowed', 'startupRecoveryAllowed'),
            @('handbackAccepted', 'handbackAccepted'))) {
        $field = Get-UiAcceptanceVisualGateField -InputObject $data -Name $pair[0] -Kind 'boolean'
        if ($field.state -ceq 'ok') { $decision[$pair[1]] = [bool] $field.value }
    }
    $rejectionField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'handbackRejection' -Kind 'string'
    if ($rejectionField.state -ceq 'ok') { $decision.handbackRejection = [string] $rejectionField.value }

    $decision.ok = $true
    return [pscustomobject] $decision
}

<#
.SYNOPSIS
    Reads the exact original-state triple a theme response must report before a write can be
    undone: presence, registry value type and value bytes.

.DESCRIPTION
    Presence is a boolean; the type and bytes are strings when a value existed and may be null
    when it did not. A present value without its type or bytes is refused, because 'restored
    exactly' would otherwise be unverifiable.
#>
function Get-UiAcceptanceVisualGateOriginalFacts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $InputObject,
        [Parameter(Mandatory)][string] $Prefix
    )

    $facts = [ordered] @{ ok = $false; failure = ''; present = $null; valueKind = $null; bytes = $null }
    $objectField = Get-UiAcceptanceVisualGateField -InputObject $InputObject -Name $Prefix -Kind 'object'
    if ($objectField.state -cne 'ok') {
        $facts.failure = $Prefix + '.' + $objectField.state
        return [pscustomobject] $facts
    }
    $presentField = Get-UiAcceptanceVisualGateField -InputObject $objectField.value -Name 'present' -Kind 'boolean'
    if ($presentField.state -cne 'ok') {
        $facts.failure = $Prefix + '.present.' + $presentField.state
        return [pscustomobject] $facts
    }
    $kindField = Get-UiAcceptanceVisualGateField -InputObject $objectField.value -Name 'valueKind' -Kind 'string'
    if ($kindField.state -cnotin @('ok', 'null')) {
        $facts.failure = $Prefix + '.value_kind.' + $kindField.state
        return [pscustomobject] $facts
    }
    $bytesField = Get-UiAcceptanceVisualGateField -InputObject $objectField.value -Name 'bytes' -Kind 'string'
    if ($bytesField.state -cnotin @('ok', 'null')) {
        $facts.failure = $Prefix + '.bytes.' + $bytesField.state
        return [pscustomobject] $facts
    }
    $facts.present = [bool] $presentField.value
    $facts.valueKind = $kindField.value
    $facts.bytes = $bytesField.value
    if ($facts.present -and $kindField.state -cne 'ok') {
        $facts.failure = $Prefix + '.value_kind.missing'
        return [pscustomobject] $facts
    }
    if ($facts.present -and $bytesField.state -cne 'ok') {
        $facts.failure = $Prefix + '.bytes.missing'
        return [pscustomobject] $facts
    }
    $facts.ok = $true
    return [pscustomobject] $facts
}

<#
.SYNOPSIS
    Reads the owner echo a theme response must carry: pid, Int64 start ticks and context.

.DESCRIPTION
    All three are required and typed. This is the response side of the comparison; equality is
    decided by the caller against the identity it actually captured.
#>
function Get-UiAcceptanceVisualGateOwnerEcho {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $InputObject)

    $echo = [ordered] @{ ok = $false; failure = ''; pid = $null; startUtcTicks = $null; context = '' }
    $owner = Get-UiAcceptanceVisualGateField -InputObject $InputObject -Name 'owner' -Kind 'object'
    if ($owner.state -cne 'ok') {
        $echo.failure = 'owner.' + $owner.state
        return [pscustomobject] $echo
    }
    $pidField = Get-UiAcceptanceVisualGateField -InputObject $owner.value -Name 'pid' -Kind 'integer'
    if ($pidField.state -cne 'ok') { $echo.failure = 'owner.pid.' + $pidField.state; return [pscustomobject] $echo }
    $ticksField = Get-UiAcceptanceVisualGateField -InputObject $owner.value -Name 'startUtcTicks' -Kind 'integer'
    if ($ticksField.state -cne 'ok') { $echo.failure = 'owner.start_ticks.' + $ticksField.state; return [pscustomobject] $echo }
    $contextField = Get-UiAcceptanceVisualGateField -InputObject $owner.value -Name 'context' -Kind 'string'
    if ($contextField.state -cne 'ok') { $echo.failure = 'owner.context.' + $contextField.state; return [pscustomobject] $echo }
    $echo.pid = [int] $pidField.value
    $echo.startUtcTicks = [long] $ticksField.value
    $echo.context = [string] $contextField.value
    if ($echo.pid -le 0) { $echo.failure = 'owner.pid.range'; return [pscustomobject] $echo }
    if ($echo.startUtcTicks -le 0) { $echo.failure = 'owner.start_ticks.range'; return [pscustomobject] $echo }
    if ([string]::IsNullOrWhiteSpace($echo.context)) { $echo.failure = 'owner.context.empty'; return [pscustomobject] $echo }
    $echo.ok = $true
    return [pscustomobject] $echo
}

<#
.SYNOPSIS
    Reads the product/toolkit binding echo a theme response must carry.
#>
function Get-UiAcceptanceVisualGateBindingEcho {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $InputObject)

    $echo = [ordered] @{
        ok = $false; failure = ''
        productHashKind = ''; productMembers = $null; productSha256 = ''
        toolkitHashKind = ''; toolkitMembers = $null; toolkitSha256 = ''
    }
    $binding = Get-UiAcceptanceVisualGateField -InputObject $InputObject -Name 'binding' -Kind 'object'
    if ($binding.state -cne 'ok') {
        $echo.failure = 'binding.' + $binding.state
        return [pscustomobject] $echo
    }
    foreach ($pair in @(
            @('productHashKind', 'string'), @('productMembers', 'integer'), @('productSha256', 'string'),
            @('toolkitHashKind', 'string'), @('toolkitMembers', 'integer'), @('toolkitSha256', 'string'))) {
        $field = Get-UiAcceptanceVisualGateField -InputObject $binding.value -Name $pair[0] -Kind $pair[1]
        if ($field.state -cne 'ok') {
            $echo.failure = 'binding.' + $pair[0] + '.' + $field.state
            return [pscustomobject] $echo
        }
        $echo[$pair[0]] = $field.value
    }
    $echo.productMembers = [int] $echo.productMembers
    $echo.toolkitMembers = [int] $echo.toolkitMembers
    $echo.productSha256 = [string] $echo.productSha256
    $echo.toolkitSha256 = [string] $echo.toolkitSha256
    $echo.productHashKind = [string] $echo.productHashKind
    $echo.toolkitHashKind = [string] $echo.toolkitHashKind
    foreach ($name in @('productSha256', 'toolkitSha256')) {
        if ($echo[$name] -cnotmatch '^[0-9A-Fa-f]{64}$') { $echo.failure = 'binding.' + $name + '.shape'; return [pscustomobject] $echo }
    }
    if ($echo.productMembers -lt 1 -or $echo.toolkitMembers -lt 1) {
        $echo.failure = 'binding.members.range'
        return [pscustomobject] $echo
    }
    $echo.ok = $true
    return [pscustomobject] $echo
}

<#
.SYNOPSIS
    Validates an apply response and reports how much of the round may have been written.

.DESCRIPTION
    'writesPossible' is deliberately not a boolean: a non-zero exit or an unreadable response is
    'unknown', never 'none'. Only a parsed response that explicitly reports no journal, no
    registry write and no broadcast can be read as a clean refusal. A successful apply requires
    its ownership receipt, because a journal the round cannot prove it owns may not be restored.
#>
function Test-UiAcceptanceThemeApplyResponse {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)][string] $Mode,
        [AllowNull()] $OwnerIdentity,
        [AllowNull()] $Binding,
        [AllowNull()] $Round,
        [switch] $AllowFixture,
        # What the caller's selection must produce; empty keeps the unconstrained call.
        [ValidateSet('', 'production', 'fixture')][string] $ExpectedAdapter = ''
    )

    $decision = [ordered] @{
        ok = $false
        failure = ''
        notIntegrated = $false
        exitKnown = $false
        exitCode = $null
        parseState = ''
        ran = $false
        adapters = ''
        applied = $null
        mode = ''
        journalState = ''
        journalWritten = $false
        registryWritten = $null
        broadcastSent = $null
        originalPresent = $null
        originalValueKind = $null
        originalBytes = $null
        identityMatch = $false
        bindingMatch = $false
        handbackTokenPresent = $false
        ownershipConfirmed = $false
        writesPossible = 'unknown'
    }

    $decision.parseState = [string] (Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'ParseState' -Default '')
    $decision.ran = [bool] (Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'Ran' -Default $false)
    $producer = Get-UiAcceptanceThemeProducerState -Result $Result
    $decision.notIntegrated = [bool] $producer.notIntegrated
    $exit = Get-UiAcceptanceVisualGateExit -Result $Result
    $decision.exitKnown = $exit.Known
    $decision.exitCode = $exit.Code

    if (-not $decision.ran) {
        $decision.failure = 'not-run'
        $decision.writesPossible = 'none'
        return [pscustomobject] $decision
    }
    if (-not $exit.Known) {
        $decision.failure = 'exit.unknown'
        return [pscustomobject] $decision
    }
    if ($exit.Code -ne 0) {
        # A structured not-integrated answer is an environment limit, never proof that nothing
        # was written: the round stays 'unknown' and must not be reported as a clean refusal.
        $decision.failure = 'exit.' + [string] $exit.Code
        return [pscustomobject] $decision
    }

    $data = Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'Data'
    if ($null -eq $data) {
        $decision.failure = 'response.' + $decision.parseState
        return [pscustomobject] $decision
    }

    $adapter = Test-UiAcceptanceThemeAdapter -Data $data -AllowFixture:$AllowFixture -Expected $ExpectedAdapter
    if (-not $adapter.ok) { $decision.failure = $adapter.failure; return [pscustomobject] $decision }
    $decision.adapters = [string] $adapter.value

    $appliedField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'applied' -Kind 'boolean'
    if ($appliedField.state -cne 'ok') {
        $decision.failure = 'applied.' + $appliedField.state
        return [pscustomobject] $decision
    }
    $decision.applied = [bool] $appliedField.value

    $modeField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'mode' -Kind 'string'
    if ($modeField.state -cne 'ok') {
        $decision.failure = 'mode.' + $modeField.state
        return [pscustomobject] $decision
    }
    $decision.mode = [string] $modeField.value
    if ($decision.mode -cne $Mode) {
        $decision.failure = 'mode.mismatch'
        return [pscustomobject] $decision
    }

    $journalField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'journalState' -Kind 'string'
    if ($journalField.state -cne 'ok') {
        $decision.failure = 'journal_state.' + $journalField.state
        return [pscustomobject] $decision
    }
    $decision.journalState = [string] $journalField.value
    if ($script:visualGateJournalStates -cnotcontains $decision.journalState) {
        $decision.failure = 'journal_state.unknown-value'
        return [pscustomobject] $decision
    }

    # A pending journal is a claim that a journal was persisted. The two facts must agree;
    # otherwise the response is self-contradictory and the round cannot know what it owes.
    $journalWrittenField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'journalWritten' -Kind 'boolean'
    if ($journalWrittenField.state -cne 'ok') {
        $decision.failure = 'journal_written.' + $journalWrittenField.state
        return [pscustomobject] $decision
    }
    $decision.journalWritten = [bool] $journalWrittenField.value
    if ($decision.journalState -ceq 'pending' -and -not $decision.journalWritten) {
        $decision.failure = 'journal_state.pending-without-write'
        return [pscustomobject] $decision
    }

    $registryField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'registryWritten' -Kind 'boolean'
    if ($registryField.state -cne 'ok') {
        $decision.failure = 'registry_written.' + $registryField.state
        return [pscustomobject] $decision
    }
    $decision.registryWritten = [bool] $registryField.value

    $broadcastField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'broadcast' -Kind 'object'
    if ($broadcastField.state -cne 'ok') {
        $decision.failure = 'broadcast.' + $broadcastField.state
        return [pscustomobject] $decision
    }
    $sentField = Get-UiAcceptanceVisualGateField -InputObject $broadcastField.value -Name 'sent' -Kind 'boolean'
    if ($sentField.state -cne 'ok') {
        $decision.failure = 'broadcast.sent.' + $sentField.state
        return [pscustomobject] $decision
    }
    $decision.broadcastSent = [bool] $sentField.value

    # The owner and binding echoes are compared against the facts this round captured. The echo
    # is a consistency check, not identity proof: the coordinator's probe and the journal are the
    # authority. Only the comparison result is kept; no process identity enters the decision.
    $identityEcho = Get-UiAcceptanceVisualGateOwnerEcho -InputObject $data
    if ($null -eq $OwnerIdentity -or [string] $OwnerIdentity.state -cne 'captured') {
        $decision.failure = 'ownership.expected_identity_missing'
        if ($decision.applied) { $decision.writesPossible = 'registry' }
        return [pscustomobject] $decision
    }
    if (-not $identityEcho.ok) {
        $decision.failure = 'ownership.' + $identityEcho.failure
        if ($decision.applied) { $decision.writesPossible = 'registry' }
        return [pscustomobject] $decision
    }
    $decision.identityMatch = [bool] (
        $identityEcho.pid -eq [int] $OwnerIdentity.pid -and
        $identityEcho.startUtcTicks -eq [long] $OwnerIdentity.startUtcTicks -and
        $identityEcho.context -ceq [string] $OwnerIdentity.context)
    if (-not $decision.identityMatch) {
        $decision.failure = 'ownership.identity_mismatch'
        if ($decision.applied) { $decision.writesPossible = 'registry' }
        return [pscustomobject] $decision
    }

    $bindingEcho = Get-UiAcceptanceVisualGateBindingEcho -InputObject $data
    if ($null -eq $Binding -or [string] $Binding.state -cne 'captured') {
        $decision.failure = 'ownership.expected_binding_missing'
        if ($decision.applied) { $decision.writesPossible = 'registry' }
        return [pscustomobject] $decision
    }
    if (-not $bindingEcho.ok) {
        $decision.failure = 'ownership.' + $bindingEcho.failure
        if ($decision.applied) { $decision.writesPossible = 'registry' }
        return [pscustomobject] $decision
    }
    $decision.bindingMatch = [bool] (
        $bindingEcho.productHashKind -ceq [string] $Binding.productHashKind -and
        $bindingEcho.productMembers -eq [int] $Binding.productMembers -and
        $bindingEcho.productSha256 -ceq [string] $Binding.productSha256 -and
        $bindingEcho.toolkitHashKind -ceq [string] $Binding.toolkitHashKind -and
        $bindingEcho.toolkitMembers -eq [int] $Binding.toolkitMembers -and
        $bindingEcho.toolkitSha256 -ceq [string] $Binding.toolkitSha256)
    if (-not $decision.bindingMatch) {
        $decision.failure = 'ownership.binding_mismatch'
        if ($decision.applied) { $decision.writesPossible = 'registry' }
        return [pscustomobject] $decision
    }

    # The receipt is a capability, not an identifier. It leaves the parsed response here and
    # lives only on the round; the returned decision and every projection of it are tokenless.
    $tokenField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'handbackToken' -Kind 'string'
    if ($tokenField.state -cnotin @('ok', 'missing', 'null')) {
        $decision.failure = 'handback_token.' + $tokenField.state
        return [pscustomobject] $decision
    }
    $token = ''
    if ($tokenField.state -ceq 'ok') { $token = [string] $tokenField.value }
    if ($token -cne '' -and $token -cnotmatch '^[0-9A-Fa-f]{64}$') {
        $decision.failure = 'handback_token.shape'
        return [pscustomobject] $decision
    }
    $decision.handbackTokenPresent = ($token -cne '')
    if ($decision.handbackTokenPresent -and $null -ne $Round) { $Round.HandbackToken = $token }
    $decision.ownershipConfirmed = ($decision.identityMatch -and $decision.bindingMatch -and
        $decision.handbackTokenPresent)

    if (-not $decision.applied) {
        if ($decision.registryWritten) { $decision.writesPossible = 'registry' }
        elseif ($decision.journalState -ceq 'pending') { $decision.writesPossible = 'journal' }
        elseif ($decision.journalState -ceq 'absent' -and -not $decision.broadcastSent -and
            $decision.identityMatch -and $decision.bindingMatch) { $decision.writesPossible = 'none' }
        else { $decision.writesPossible = 'unknown' }
        # A clean refusal says nothing was written and needs no original state. Every other
        # refusal may have written, so the original state must still be on record.
        if ($decision.writesPossible -cne 'none') {
            $original = Get-UiAcceptanceVisualGateOriginalFacts -InputObject $data -Prefix 'original'
            if (-not $original.ok) {
                $decision.failure = $original.failure
                return [pscustomobject] $decision
            }
            $decision.originalPresent = $original.present
            $decision.originalValueKind = $original.valueKind
            $decision.originalBytes = $original.bytes
        }
        $decision.failure = 'applied.false'
        return [pscustomobject] $decision
    }

    if ($decision.journalState -cne 'pending') {
        $decision.failure = 'journal_state.apply-without-pending'
        return [pscustomobject] $decision
    }
    if (-not $decision.registryWritten) {
        $decision.failure = 'registry.not-written'
        return [pscustomobject] $decision
    }
    if (-not $decision.broadcastSent) {
        $decision.failure = 'broadcast.unsent'
        return [pscustomobject] $decision
    }
    $original = Get-UiAcceptanceVisualGateOriginalFacts -InputObject $data -Prefix 'original'
    if (-not $original.ok) {
        $decision.failure = $original.failure
        return [pscustomobject] $decision
    }
    $decision.originalPresent = $original.present
    $decision.originalValueKind = $original.valueKind
    $decision.originalBytes = $original.bytes
    if (-not $decision.ownershipConfirmed) {
        # The registry may already carry the new value: the round owes a restore it cannot prove
        # is its own, so it must stop with the journal preserved.
        $decision.writesPossible = 'registry'
        $decision.failure = 'ownership.missing'
        return [pscustomobject] $decision
    }
    $decision.writesPossible = 'registry'
    $decision.ok = $true
    return [pscustomobject] $decision
}

<#
.SYNOPSIS
    Validates a restore response that is claimed to have closed a journal it was given.

.DESCRIPTION
    A restore only passes when it says it restored, says the journal is gone, reports the exact
    original presence/type/bytes it restored, reports a readback that matches that original, and
    reports the broadcast. 'noAction' is a contradiction when a journal existed, and journal
    absence alone is never accepted as restoration.
#>
function Test-UiAcceptanceThemeRestoreResponse {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Result,
        [bool] $JournalExpected = $true,
        [AllowNull()] $ExpectedOwnerIdentity,
        [ValidateSet('', 'startup', 'handback')][string] $ExpectedDisposition = '',
        [switch] $RequireBindingMatch,
        [switch] $AllowFixture,
        # What the caller's selection must produce; empty keeps the unconstrained call.
        [ValidateSet('', 'production', 'fixture')][string] $ExpectedAdapter = ''
    )

    $decision = [ordered] @{
        ok = $false
        failure = ''
        notIntegrated = $false
        exitKnown = $false
        exitCode = $null
        parseState = ''
        ran = $false
        adapters = ''
        disposition = ''
        restored = $null
        noAction = $null
        journalState = ''
        journalPresent = $null
        journalRemoved = $false
        journalChecksumValid = $null
        ownerState = ''
        identityMatch = $false
        originalPresent = $null
        originalValueKind = $null
        originalBytes = $null
        readbackPresent = $null
        readbackValueKind = $null
        readbackBytes = $null
        readbackMatches = $null
        broadcastSent = $null
        registryRestored = $null
        bindingMatchesJournal = $null
    }

    $decision.parseState = [string] (Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'ParseState' -Default '')
    $decision.ran = [bool] (Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'Ran' -Default $false)
    $producer = Get-UiAcceptanceThemeProducerState -Result $Result
    $decision.notIntegrated = [bool] $producer.notIntegrated
    $exit = Get-UiAcceptanceVisualGateExit -Result $Result
    $decision.exitKnown = $exit.Known
    $decision.exitCode = $exit.Code

    if (-not $decision.ran) { $decision.failure = 'not-run'; return [pscustomobject] $decision }
    if (-not $exit.Known) { $decision.failure = 'exit.unknown'; return [pscustomobject] $decision }
    if ($exit.Code -ne 0) { $decision.failure = 'exit.' + [string] $exit.Code; return [pscustomobject] $decision }

    $data = Get-UiAcceptanceVisualGateValue -InputObject $Result -Name 'Data'
    if ($null -eq $data) { $decision.failure = 'response.' + $decision.parseState; return [pscustomobject] $decision }

    $adapter = Test-UiAcceptanceThemeAdapter -Data $data -AllowFixture:$AllowFixture -Expected $ExpectedAdapter
    if (-not $adapter.ok) { $decision.failure = $adapter.failure; return [pscustomobject] $decision }
    $decision.adapters = [string] $adapter.value

    $restoredField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'restored' -Kind 'boolean'
    if ($restoredField.state -cne 'ok') {
        $decision.failure = 'restored.' + $restoredField.state
        return [pscustomobject] $decision
    }
    $decision.restored = [bool] $restoredField.value

    $noActionField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'noAction' -Kind 'boolean'
    if ($noActionField.state -cne 'ok') {
        $decision.failure = 'no_action.' + $noActionField.state
        return [pscustomobject] $decision
    }
    $decision.noAction = [bool] $noActionField.value

    $journalField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'journalState' -Kind 'string'
    if ($journalField.state -cne 'ok') {
        $decision.failure = 'journal_state.' + $journalField.state
        return [pscustomobject] $decision
    }
    $decision.journalState = [string] $journalField.value

    $removedField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'journalRemoved' -Kind 'boolean'
    if ($removedField.state -cne 'ok') {
        $decision.failure = 'journal_removed.' + $removedField.state
        return [pscustomobject] $decision
    }
    $decision.journalRemoved = [bool] $removedField.value

    # "Restored" is a summary; it is never accepted on its own. The journal that was consumed has
    # to be reported as present and checksum-valid, and its closure is confirmed separately by
    # journalState/journalRemoved and by the physical absence read in the caller.
    $journalPresentField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'journalPresent' -Kind 'boolean'
    if ($journalPresentField.state -cne 'ok') {
        $decision.failure = 'journal_present.' + $journalPresentField.state
        return [pscustomobject] $decision
    }
    $decision.journalPresent = [bool] $journalPresentField.value

    $journalChecksumField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'journalChecksumValid' -Kind 'boolean'
    if ($journalChecksumField.state -cne 'ok') {
        $decision.failure = 'journal_checksum.' + $journalChecksumField.state
        return [pscustomobject] $decision
    }
    $decision.journalChecksumValid = [bool] $journalChecksumField.value

    # The disposition is part of the answer: a startup recovery and a hand-back are decided by
    # different rules, and a response that does not say which one it was is refused.
    $dispositionField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'disposition' -Kind 'string'
    if ($dispositionField.state -cne 'ok') {
        $decision.failure = 'disposition.' + $dispositionField.state
        return [pscustomobject] $decision
    }
    $decision.disposition = [string] $dispositionField.value
    if ($decision.disposition -cnotin @('startup', 'handback')) {
        $decision.failure = 'disposition.unknown-value'
        return [pscustomobject] $decision
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedDisposition) -and
        $decision.disposition -cne $ExpectedDisposition) {
        $decision.failure = 'disposition.mismatch'
        return [pscustomobject] $decision
    }

    # Startup recovery adopts a journal whose owner is provably dead; a hand-back is only issued
    # by the still-live owner. The probe state is checked against the disposition, so a response
    # cannot call itself a hand-back while reporting a dead owner (or the reverse).
    $ownerStateField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'ownerState' -Kind 'string'
    if ($ownerStateField.state -cne 'ok') {
        $decision.failure = 'owner_state.' + $ownerStateField.state
        return [pscustomobject] $decision
    }
    $decision.ownerState = [string] $ownerStateField.value
    if ($script:visualGateOwnerStates -cnotcontains $decision.ownerState) {
        $decision.failure = 'owner_state.unknown-value'
        return [pscustomobject] $decision
    }
    if (-not $decision.noAction) {
        $requiredOwnerState = $(if ($decision.disposition -ceq 'handback') { 'live' } else { 'dead' })
        if ($decision.ownerState -cne $requiredOwnerState) {
            $decision.failure = 'owner_state.disposition_mismatch'
            return [pscustomobject] $decision
        }
    }

    # A same-round restore must echo the identity that owes it. The dead-owner recovery in the
    # gate passes no expected identity: it adopts a journal whose owner is gone.
    if ($null -ne $ExpectedOwnerIdentity) {
        $identityEcho = Get-UiAcceptanceVisualGateOwnerEcho -InputObject $data
        if ([string] $ExpectedOwnerIdentity.state -cne 'captured') {
            $decision.failure = 'ownership.expected_identity_missing'
            return [pscustomobject] $decision
        }
        if (-not $identityEcho.ok) {
            $decision.failure = 'ownership.' + $identityEcho.failure
            return [pscustomobject] $decision
        }
        $decision.identityMatch = [bool] (
            $identityEcho.pid -eq [int] $ExpectedOwnerIdentity.pid -and
            $identityEcho.startUtcTicks -eq [long] $ExpectedOwnerIdentity.startUtcTicks -and
            $identityEcho.context -ceq [string] $ExpectedOwnerIdentity.context)
        if (-not $decision.identityMatch) {
            $decision.failure = 'ownership.identity_mismatch'
            return [pscustomobject] $decision
        }
    }

    $original = Get-UiAcceptanceVisualGateOriginalFacts -InputObject $data -Prefix 'original'
    if (-not $original.ok) {
        $decision.failure = $original.failure
        return [pscustomobject] $decision
    }
    $decision.originalPresent = $original.present
    $decision.originalValueKind = $original.valueKind
    $decision.originalBytes = $original.bytes

    $readbackField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'readback' -Kind 'object'
    if ($readbackField.state -cne 'ok') {
        $decision.failure = 'readback.' + $readbackField.state
        return [pscustomobject] $decision
    }
    $readbackPresentField = Get-UiAcceptanceVisualGateField -InputObject $readbackField.value -Name 'present' -Kind 'boolean'
    if ($readbackPresentField.state -cne 'ok') {
        $decision.failure = 'readback.present.' + $readbackPresentField.state
        return [pscustomobject] $decision
    }
    $readbackKindField = Get-UiAcceptanceVisualGateField -InputObject $readbackField.value -Name 'valueKind' -Kind 'string'
    if ($readbackKindField.state -cnotin @('ok', 'null')) {
        $decision.failure = 'readback.value_kind.' + $readbackKindField.state
        return [pscustomobject] $decision
    }
    $readbackBytesField = Get-UiAcceptanceVisualGateField -InputObject $readbackField.value -Name 'bytes' -Kind 'string'
    if ($readbackBytesField.state -cnotin @('ok', 'null')) {
        $decision.failure = 'readback.bytes.' + $readbackBytesField.state
        return [pscustomobject] $decision
    }
    $matchesField = Get-UiAcceptanceVisualGateField -InputObject $readbackField.value -Name 'matchesOriginal' -Kind 'boolean'
    if ($matchesField.state -cne 'ok') {
        $decision.failure = 'readback.matches_original.' + $matchesField.state
        return [pscustomobject] $decision
    }
    $decision.readbackPresent = [bool] $readbackPresentField.value
    $decision.readbackValueKind = $readbackKindField.value
    $decision.readbackBytes = $readbackBytesField.value
    $decision.readbackMatches = [bool] $matchesField.value

    $broadcastField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'broadcast' -Kind 'object'
    if ($broadcastField.state -cne 'ok') {
        $decision.failure = 'broadcast.' + $broadcastField.state
        return [pscustomobject] $decision
    }
    $sentField = Get-UiAcceptanceVisualGateField -InputObject $broadcastField.value -Name 'sent' -Kind 'boolean'
    if ($sentField.state -cne 'ok') {
        $decision.failure = 'broadcast.sent.' + $sentField.state
        return [pscustomobject] $decision
    }
    $decision.broadcastSent = [bool] $sentField.value

    $registryRestoredField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'registryRestored' -Kind 'boolean'
    if ($registryRestoredField.state -cne 'ok') {
        $decision.failure = 'registry_restored.' + $registryRestoredField.state
        return [pscustomobject] $decision
    }
    $decision.registryRestored = [bool] $registryRestoredField.value

    $bindingMatchField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'bindingMatchesJournal' -Kind 'boolean'
    if ($bindingMatchField.state -ceq 'ok') {
        $decision.bindingMatchesJournal = [bool] $bindingMatchField.value
    }
    if ($RequireBindingMatch -and $decision.bindingMatchesJournal -ne $true) {
        $decision.failure = 'binding.not-matched-to-journal'
        return [pscustomobject] $decision
    }

    # The component never echoes the receipt; possession is proven by the restore succeeding. A
    # response that volunteers a receipt field is refused rather than trusted.
    $tokenField = Get-UiAcceptanceVisualGateField -InputObject $data -Name 'handbackToken' -Kind 'string'
    if ($tokenField.state -ceq 'ok') {
        $decision.failure = 'handback_token.unexpected'
        return [pscustomobject] $decision
    }

    if ($JournalExpected -and $decision.noAction) {
        $decision.failure = 'no_action.for-existing-journal'
        return [pscustomobject] $decision
    }
    if ($JournalExpected -and -not $decision.journalPresent) {
        $decision.failure = 'journal_present.false'
        return [pscustomobject] $decision
    }
    if ($JournalExpected -and -not $decision.journalChecksumValid) {
        $decision.failure = 'journal_checksum.invalid'
        return [pscustomobject] $decision
    }
    if (-not $decision.restored) {
        $decision.failure = 'restored.false'
        return [pscustomobject] $decision
    }
    if ($decision.journalState -cne 'absent') {
        $decision.failure = 'journal_state.' + $decision.journalState
        return [pscustomobject] $decision
    }
    if (-not $decision.journalRemoved) {
        $decision.failure = 'journal.not-removed'
        return [pscustomobject] $decision
    }
    # The readback is compared field by field, not trusted through its own summary boolean.
    if ($decision.readbackPresent -ne $decision.originalPresent) {
        $decision.failure = 'readback.presence_mismatch'
        return [pscustomobject] $decision
    }
    if ($decision.originalPresent) {
        if ([string] $decision.readbackValueKind -cne [string] $decision.originalValueKind) {
            $decision.failure = 'readback.value_kind_mismatch'
            return [pscustomobject] $decision
        }
        if ([string] $decision.readbackBytes -cne [string] $decision.originalBytes) {
            $decision.failure = 'readback.bytes_mismatch'
            return [pscustomobject] $decision
        }
    }
    if (-not $decision.readbackMatches) {
        $decision.failure = 'readback.mismatch'
        return [pscustomobject] $decision
    }
    if (-not $decision.broadcastSent) {
        $decision.failure = 'broadcast.unsent'
        return [pscustomobject] $decision
    }
    if (-not $decision.registryRestored) {
        $decision.failure = 'registry.not-restored'
        return [pscustomobject] $decision
    }

    $decision.ok = $true
    return [pscustomobject] $decision
}

<#
.SYNOPSIS
    Creates the per-theme round state the visual matrix keeps across apply, observation and
    restore.

.DESCRIPTION
    The state separates what was attempted from what was written and from what was proven. A
    round that never started cannot owe a restore, and a round whose apply response was lost owes
    one but may not perform it.
#>
function New-UiAcceptanceThemeRoundState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Mode,
        [AllowNull()] $OwnerIdentity,
        [AllowNull()] $Binding
    )

    return [pscustomobject] @{
        Mode = $Mode
        OwnerIdentity = $OwnerIdentity
        Binding = $Binding
        Attempted = $false
        ApplySucceeded = $false
        WritesPossible = 'none'
        OwnershipConfirmed = $false
        # Capability, only in memory, cleared before the scenario returns. Never exported.
        HandbackToken = ''
        HandbackTokenPresent = $false
        RestoreOwed = $false
        RestoreAttempted = $false
        RestoreConfirmed = $false
        JournalClosed = $false
        EvidenceWritten = $false
        StopReason = ''
        ApplyDecision = $null
        RestoreDecision = $null
    }
}

<#
.SYNOPSIS
    Runs the apply phase of one round and records its facts before the outcome is judged.
#>
function Invoke-UiAcceptanceThemeApplyPhase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Round,
        [Parameter(Mandatory)][string] $JournalPath,
        [Parameter(Mandatory)][string] $SupportPath,
        [Parameter(Mandatory)][string] $Mode,
        [AllowNull()] $OwnerIdentity,
        [AllowNull()] $Binding,
        [scriptblock] $InvokeSupport,
        [int] $TimeoutMs = 60000,
        [AllowEmptyString()][string] $FixtureRoot = '',
        [switch] $AllowFixture,
        [switch] $EnforceAdapterSelection
    )

    $expectedAdapter = ''
    if ($EnforceAdapterSelection) {
        $expectedAdapter = if ($AllowFixture -and -not [string]::IsNullOrWhiteSpace($FixtureRoot)) { 'fixture' } else { 'production' }
    }
    if ($null -eq $OwnerIdentity) { $OwnerIdentity = $Round.OwnerIdentity }
    if ($null -eq $Binding) { $Binding = $Round.Binding }
    $Round.OwnerIdentity = $OwnerIdentity
    $Round.Binding = $Binding

    # Recorded before the command runs: from this point the round may have written a journal.
    $Round.Attempted = $true
    $Round.RestoreOwed = $true
    $result = Invoke-UiAcceptanceThemeCommand -SupportPath $SupportPath -Action 'apply' `
        -JournalPath $JournalPath -Mode $Mode -TimeoutMs $TimeoutMs `
        -OwnerIdentity $OwnerIdentity -Binding $Binding -InvokeSupport $InvokeSupport `
        -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture
    $decision = Test-UiAcceptanceThemeApplyResponse -Result $result -Mode $Mode `
        -OwnerIdentity $OwnerIdentity -Binding $Binding -Round $Round -AllowFixture:$AllowFixture `
        -ExpectedAdapter $expectedAdapter

    $Round.ApplyDecision = $decision
    $Round.ApplySucceeded = [bool] $decision.ok
    $Round.WritesPossible = [string] $decision.writesPossible
    $Round.OwnershipConfirmed = [bool] $decision.ownershipConfirmed
    $Round.HandbackTokenPresent = [bool] $decision.handbackTokenPresent
    $Round.RestoreOwed = ($Round.WritesPossible -cne 'none')

    if (-not $Round.ApplySucceeded) {
        if (-not $Round.RestoreOwed) { $Round.StopReason = 'apply_refused_cleanly' }
        elseif (-not $Round.OwnershipConfirmed) { $Round.StopReason = 'ownership_unconfirmed' }
    }
    return $decision
}

<#
.SYNOPSIS
    Answers whether the round may restore: it owes one and it proved the journal is its own.
#>
function Test-UiAcceptanceThemeRoundMayRestore {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $State)

    return [bool] ($State.RestoreOwed -and $State.OwnershipConfirmed -and -not $State.RestoreConfirmed)
}

<#
.SYNOPSIS
    Answers whether another theme round may start.

.DESCRIPTION
    This is the stop gate after a failed or uncertain round: an owed restore that was not
    confirmed, or an explicit stop reason, refuses the next theme instead of letting its
    CreateNew journal collide with the preserved one.
#>
function Test-UiAcceptanceThemeRoundMayStart {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $State)

    $allowed = $true
    $reason = ''
    if (-not [string]::IsNullOrWhiteSpace([string] $State.StopReason)) {
        $allowed = $false
        $reason = [string] $State.StopReason
    }
    elseif ($State.RestoreOwed -and -not $State.RestoreConfirmed) {
        $allowed = $false
        $reason = 'restore_owed'
    }
    return [pscustomobject] @{ Allowed = $allowed; Reason = $reason }
}

<#
.SYNOPSIS
    Answers whether a restore closed the same original state the apply had read.

.DESCRIPTION
    The receipt binds the restore to the apply instance inside the component, but the consumer
    still has to tie the reported original to the round it observed. The apply decision and the
    restore decision both carry the exact presence/type/bytes triple, so they are compared here;
    a successful restore whose triple does not match the apply is not this round's restore.
#>
function Test-UiAcceptanceThemeOriginalMatches {
    [CmdletBinding()]
    param([AllowNull()] $Apply, [AllowNull()] $Restore)

    if ($null -eq $Apply -or $null -eq $Restore) { return $false }
    if ($Apply.originalPresent -isnot [bool] -or $Restore.originalPresent -isnot [bool]) { return $false }
    if ([bool] $Apply.originalPresent -ne [bool] $Restore.originalPresent) { return $false }
    if (-not [bool] $Apply.originalPresent) { return $true }
    return ([string] $Apply.originalValueKind -ceq [string] $Restore.originalValueKind -and
        [string] $Apply.originalBytes -ceq [string] $Restore.originalBytes)
}

<#
.SYNOPSIS
    Runs the restore phase of one round: ownership gate, restore, readback, journal closure,
    evidence.

.DESCRIPTION
    A round that cannot prove it owns the journal makes no restore call at all: the journal is
    preserved for explicit recovery and the round is refused. The evidence write is part of the
    confirmation, because an unrecorded restore cannot later be distinguished from a silent one.
#>
function Invoke-UiAcceptanceThemeRestorePhase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Round,
        [Parameter(Mandatory)][string] $JournalPath,
        [Parameter(Mandatory)][string] $SupportPath,
        [scriptblock] $InvokeSupport,
        [int] $TimeoutMs = 60000,
        [AllowEmptyString()][string] $FixtureRoot = '',
        [switch] $AllowFixture,
        [switch] $EnforceAdapterSelection,
        [scriptblock] $WriteEvidence
    )

    $expectedAdapter = ''
    if ($EnforceAdapterSelection) {
        $expectedAdapter = if ($AllowFixture -and -not [string]::IsNullOrWhiteSpace($FixtureRoot)) { 'fixture' } else { 'production' }
    }

    $summary = [ordered] @{
        ok = $false
        failure = ''
        calls = 0
        journalClosed = $false
        evidenceWritten = $false
        decision = $null
        inspect = $null
    }

    if (-not $Round.RestoreOwed) {
        $summary.failure = 'not-owed'
        return [pscustomobject] $summary
    }
    if (-not (Test-UiAcceptanceThemeRoundMayRestore -State $Round)) {
        $Round.RestoreAttempted = $false
        $Round.RestoreConfirmed = $false
        $Round.StopReason = 'ownership_unconfirmed'
        $summary.failure = 'ownership.unconfirmed'
        return [pscustomobject] $summary
    }

    $Round.RestoreAttempted = $true
    $restoreResult = Invoke-UiAcceptanceThemeCommand -SupportPath $SupportPath -Action 'restore' `
        -JournalPath $JournalPath -TimeoutMs $TimeoutMs -Disposition 'handback' `
        -HandbackToken ([string] $Round.HandbackToken) `
        -OwnerIdentity $Round.OwnerIdentity -Binding $Round.Binding -InvokeSupport $InvokeSupport `
        -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture
    $summary.calls = 1
    $decision = Test-UiAcceptanceThemeRestoreResponse -Result $restoreResult -JournalExpected $true `
        -ExpectedOwnerIdentity $Round.OwnerIdentity -ExpectedDisposition 'handback' `
        -RequireBindingMatch -AllowFixture:$AllowFixture -ExpectedAdapter $expectedAdapter
    $summary.decision = $decision

    # The restore succeeded against a journal whose original must be the one this round's apply
    # read. Without this cross-check a successful restore could belong to another journal.
    if ($decision.ok -and -not (Test-UiAcceptanceThemeOriginalMatches `
            -Apply $Round.ApplyDecision -Restore $decision)) {
        $decision.ok = $false
        $decision.failure = 'original.apply_restore_mismatch'
    }

    $inspectResult = Invoke-UiAcceptanceThemeCommand -SupportPath $SupportPath -Action 'inspect' `
        -JournalPath $JournalPath -TimeoutMs $TimeoutMs -InvokeSupport $InvokeSupport `
        -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture
    $summary.calls = 2
    $inspect = Test-UiAcceptanceThemeInspectResponse -Result $inspectResult -AllowFixture:$AllowFixture -ExpectedAdapter $expectedAdapter
    $summary.inspect = $inspect

    $journalClosed = [bool] ($decision.ok -and $inspect.ok -and
        $inspect.journalState -ceq 'absent' -and
        -not [IO.File]::Exists([IO.Path]::GetFullPath($JournalPath)))
    $summary.journalClosed = $journalClosed
    # The round sees the closure before the evidence callback runs, so the document it writes
    # reports the same facts the confirmation is about to use.
    $Round.RestoreDecision = $decision
    $Round.JournalClosed = $journalClosed

    $evidenceWritten = $false
    $evidenceError = $null
    if ($null -ne $WriteEvidence) {
        try {
            & $WriteEvidence $decision $inspect
            $evidenceWritten = $true
        }
        catch {
            # The callback failed; the reason is the only thing that can explain the phase. Keep
            # the record itself in memory (the round carries it) and expose a type/message pair
            # that every existing writer already projects.
            $evidenceWritten = $false
            $evidenceError = $_
            # The round is a plain object, not a dictionary: add the slot when it is not declared
            # instead of assigning a property that does not exist.
            if ($null -ne $Round.PSObject.Properties['EvidenceError']) { $Round.EvidenceError = $_ }
            else { $Round | Add-Member -NotePropertyName EvidenceError -NotePropertyValue $_ }
            $summary.evidenceErrorType = [string] $_.Exception.GetType().Name
            $summary.evidenceErrorText = [string] $_.Exception.Message
        }
    }
    $summary.evidenceWritten = $evidenceWritten

    $Round.EvidenceWritten = $evidenceWritten
    $Round.RestoreConfirmed = [bool] ($decision.ok -and $journalClosed -and $evidenceWritten)
    if (-not $Round.RestoreConfirmed) {
        if (-not $decision.ok) { $Round.StopReason = 'restore_failed' }
        else { $Round.StopReason = 'restore_unconfirmed' }
    }

    $summary.ok = $Round.RestoreConfirmed
    if (-not $decision.ok) { $summary.failure = $decision.failure }
    elseif (-not $journalClosed) { $summary.failure = 'journal.open' }
    elseif (-not $evidenceWritten) { $summary.failure = 'evidence.unwritten' }
    return [pscustomobject] $summary
}

<#
.SYNOPSIS
    The pre-preflight journal gate: inspect a leftover journal and refuse unless it is provably
    closed.

.DESCRIPTION
    The gate never writes the registry and never implements a second recovery mechanism: it drives
    the same support command surface the coordinator will own. Outcomes:
      clear      no journal file exists; no support call is made at all
      recovered  a journal with a provably dead owner was restored and re-read as absent
      blocked    anything else: no trustworthy capability, unreadable response, an inconsistent
                 state, a live or unknown owner, a failed restore, or a journal that survived

    A blocked gate is a refusal to start, not a completed recovery. 'recovered' is only produced
    by a restore whose own response and a fresh readback both closed the journal.
#>
function Invoke-UiAcceptanceThemeJournalGate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $JournalPath,
        [string] $SupportPath = '',
        [int] $TimeoutMs = 60000,
        [scriptblock] $InvokeSupport,
        # A fixture-bound caller passes its private root and the explicit per-call licence. Both
        # travel to every command this gate issues and to both response validators, so a gate that
        # runs inside an acceptance fixture is the same gate, not a looser one. Without the switch
        # no fixture argument is added and no child is started with one.
        [AllowEmptyString()][string] $FixtureRoot = '',
        [switch] $AllowFixture,
        # A read-only start (CurrentOnly) still inspects and classifies, but a journal that would
        # need a restore is refused and kept instead of being restored and closed behind the
        # caller's back. Both keeps the original recovery contract.
        [bool] $AllowRestore = $true,
        # The caller that owns the selection asks every answer to match it. A caller that only
        # drives one validator (a test, a diagnostic) keeps the unconstrained surface.
        [switch] $EnforceAdapterSelection
    )

    $journal = [IO.Path]::GetFullPath($JournalPath)
    # The adapter the caller's own selection must produce, checked on every answer this gate
    # accepts: a fixture-bound gate that hears 'production' was not run where it claimed to run.
    $expectedAdapter = ''
    if ($EnforceAdapterSelection) {
        $expectedAdapter = if ($AllowFixture -and -not [string]::IsNullOrWhiteSpace($FixtureRoot)) { 'fixture' } else { 'production' }
    }
    $gate = [ordered] @{
        state = 'clear'
        reason = ''
        journalPresent = $null
        presenceState = 'unknown'
        entryKind = 'unknown'
        presenceError = ''
        journalState = ''
        ownerState = ''
        supportUsed = $false
        calls = @()
        checkedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        inspect = $null
        restore = $null
        readback = $null
    }

    # A partial override is refused before the journal is even classified: an absent journal must
    # never turn a broken selection into a silent 'clear'.
    $override = Test-UiAcceptanceThemeOverride -JournalPath $journal -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture
    if (-not $override.ok) {
        $gate.state = 'blocked'
        $gate.reason = [string] $override.code
        return [pscustomobject] $gate
    }

    # Three states, not a boolean: absent is only reported after the exact parent enumerated
    # without the leaf; an unreadable path or a wrong type is unknown and blocks the start.
    $presence = Get-UiAcceptanceJournalPresence -JournalPath $journal
    $gate.presenceState = [string] $presence.state
    $gate.entryKind = [string] $presence.entryKind
    $gate.presenceError = [string] $presence.error
    if ($presence.state -ceq 'absent') {
        $gate.journalPresent = $false
        return [pscustomobject] $gate
    }
    if ($presence.state -cne 'present') {
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_presence_unknown'
        return [pscustomobject] $gate
    }
    $gate.journalPresent = $true

    $supportAvailable = $false
    if (-not [string]::IsNullOrWhiteSpace($SupportPath)) {
        $supportAvailable = [IO.File]::Exists($SupportPath)
    }
    if (-not $supportAvailable -and $null -eq $InvokeSupport) {
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_gate_unavailable'
        return [pscustomobject] $gate
    }

    $gate.supportUsed = $true
    $gate.calls = @($gate.calls) + @('inspect')
    $inspectResult = Invoke-UiAcceptanceThemeCommand -SupportPath $SupportPath -Action 'inspect' `
        -JournalPath $journal -TimeoutMs $TimeoutMs -InvokeSupport $InvokeSupport `
        -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture
    $inspect = Test-UiAcceptanceThemeInspectResponse -Result $inspectResult -AllowFixture:$AllowFixture -ExpectedAdapter $expectedAdapter
    $gate.inspect = $inspect
    $gate.journalState = [string] $inspect.journalState
    $gate.ownerState = [string] $inspect.ownerState

    if (-not $inspect.ok) {
        $gate.state = 'blocked'
        if ($inspect.notIntegrated) { $gate.reason = 'theme_journal_gate_unavailable' }
        else { $gate.reason = 'theme_journal_gate_inspect_failed' }
        return [pscustomobject] $gate
    }
    if ($inspect.journalState -ceq 'absent') {
        # The file exists but the command reports no journal: neither answer can be trusted.
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_gate_inconsistent'
        return [pscustomobject] $gate
    }
    if ($inspect.journalState -ceq 'foreign-live') {
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_foreign_live'
        return [pscustomobject] $gate
    }
    if ($inspect.journalState -cnotin @('pending', 'restore-required')) {
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_gate_unknown'
        return [pscustomobject] $gate
    }
    if ($inspect.ownerState -ceq 'live') {
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_owner_live'
        return [pscustomobject] $gate
    }
    if ($inspect.ownerState -cne 'dead') {
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_owner_unknown'
        return [pscustomobject] $gate
    }
    if (-not $AllowRestore) {
        # A dead owner would normally be restored here. A read-only start must not write the
        # registry, so the journal stays exactly as it is and the run is blocked with its reason.
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_restore_not_allowed'
        return [pscustomobject] $gate
    }

    $gate.calls = @($gate.calls) + @('restore')
    $restoreResult = Invoke-UiAcceptanceThemeCommand -SupportPath $SupportPath -Action 'restore' `
        -JournalPath $journal -TimeoutMs $TimeoutMs -InvokeSupport $InvokeSupport `
        -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture
    $restore = Test-UiAcceptanceThemeRestoreResponse -Result $restoreResult -JournalExpected $true `
        -ExpectedDisposition 'startup' -AllowFixture:$AllowFixture -ExpectedAdapter $expectedAdapter
    $gate.restore = $restore
    if (-not $restore.ok) {
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_recovery_failed'
        return [pscustomobject] $gate
    }

    $gate.calls = @($gate.calls) + @('inspect')
    $readbackResult = Invoke-UiAcceptanceThemeCommand -SupportPath $SupportPath -Action 'inspect' `
        -JournalPath $journal -TimeoutMs $TimeoutMs -InvokeSupport $InvokeSupport `
        -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture
    $readback = Test-UiAcceptanceThemeInspectResponse -Result $readbackResult -AllowFixture:$AllowFixture `
        -ExpectedAdapter $expectedAdapter
    $gate.readback = $readback
    $gate.journalState = [string] $readback.journalState
    $gate.ownerState = [string] $readback.ownerState

    if (-not $readback.ok -or $readback.journalState -cne 'absent') {
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_recovery_unconfirmed'
        return [pscustomobject] $gate
    }
    $afterPresence = Get-UiAcceptanceJournalPresence -JournalPath $journal
    if ($afterPresence.state -cne 'absent') {
        # The response says gone; the path is still present or cannot be classified. The
        # physical classification wins over the response.
        $gate.presenceState = [string] $afterPresence.state
        $gate.entryKind = [string] $afterPresence.entryKind
        $gate.presenceError = [string] $afterPresence.error
        $gate.state = 'blocked'
        $gate.reason = 'theme_journal_recovery_unconfirmed'
        return [pscustomobject] $gate
    }
    $gate.presenceState = 'absent'
    $gate.entryKind = 'none'

    $gate.state = 'recovered'
    return [pscustomobject] $gate
}

<#
.SYNOPSIS
    Produces the summary shape of a gate result. It carries no machine paths.
#>
function Export-UiAcceptanceThemeJournalGate {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Gate)

    $export = [ordered] @{
        state = [string] $Gate.state
        reason = [string] $Gate.reason
        journalPresent = $Gate.journalPresent
        presenceState = [string] $Gate.presenceState
        entryKind = [string] $Gate.entryKind
        presenceError = [string] $Gate.presenceError
        journalState = [string] $Gate.journalState
        ownerState = [string] $Gate.ownerState
        supportUsed = [bool] $Gate.supportUsed
        calls = @($Gate.calls | ForEach-Object { [string] $_ })
        checkedUtc = [string] $Gate.checkedUtc
        inspect = $null
        restore = $null
        readback = $null
    }
    foreach ($name in @('inspect', 'restore', 'readback')) {
        # A PSCustomObject has no string indexer on every host; read the property through the
        # shared accessor instead so the export works wherever the gate result came from.
        $decision = Get-UiAcceptanceVisualGateValue -InputObject $Gate -Name $name
        if ($null -ne $decision) {
            # Not every decision carries every field (a restore has no ownerState), so the export
            # reads each one defensively instead of assuming one common shape.
            $export[$name] = [ordered] @{
                ok = [bool] (Get-UiAcceptanceVisualGateValue -InputObject $decision -Name 'ok' -Default $false)
                failure = [string] (Get-UiAcceptanceVisualGateValue -InputObject $decision -Name 'failure' -Default '')
                exitKnown = [bool] (Get-UiAcceptanceVisualGateValue -InputObject $decision -Name 'exitKnown' -Default $false)
                exitCode = Get-UiAcceptanceVisualGateValue -InputObject $decision -Name 'exitCode'
                parseState = [string] (Get-UiAcceptanceVisualGateValue -InputObject $decision -Name 'parseState' -Default '')
                notIntegrated = [bool] (Get-UiAcceptanceVisualGateValue -InputObject $decision -Name 'notIntegrated' -Default $false)
                journalState = [string] (Get-UiAcceptanceVisualGateValue -InputObject $decision -Name 'journalState' -Default '')
                ownerState = [string] (Get-UiAcceptanceVisualGateValue -InputObject $decision -Name 'ownerState' -Default '')
            }
        }
    }
    return $export
}

<#
.SYNOPSIS
    Checks a captured matrix against the declared set: no duplicates, the full base grid, and
    every required state/dialog present.

.DESCRIPTION
    Structural defects (missing grid cells, duplicate keys, missing states) are returned in
    'Defects' and must stop the scenario. Evidence that is present but not passing is returned in
    'NotPassed' and can only make the scenario NotRun, never Failed.
#>
function Test-UiAcceptanceVisualMatrixSet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Entries,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Themes,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Pages,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Viewports,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $RequiredStates,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]] $DialogStates
    )

    $defects = New-Object 'System.Collections.Generic.List[string]'
    $notPassed = New-Object 'System.Collections.Generic.List[string]'
    $seen = @{}

    foreach ($entry in @($Entries)) {
        $key = [string] $entry.theme + '|' + [string] $entry.viewport + '|' +
            [string] $entry.page + '|' + [string] $entry.state
        if ($seen.ContainsKey($key)) { [void] $defects.Add('duplicate:' + $key) }
        else { $seen[$key] = 1 }
    }

    foreach ($theme in @($Themes)) {
        foreach ($viewport in @($Viewports)) {
            foreach ($page in @($Pages)) {
                $matches = @($Entries | Where-Object {
                    [string] $_.theme -ceq [string] $theme -and
                    [string] $_.viewport -ceq [string] $viewport -and
                    [string] $_.page -ceq [string] $page -and
                    [string] $_.state -ceq 'matrix'
                })
                if ($matches.Count -ne 1) {
                    [void] $defects.Add('base:' + $theme + '|' + $viewport + '|' + $page + ':' + $matches.Count)
                }
            }
        }
        foreach ($required in @($RequiredStates)) {
            $matches = @($Entries | Where-Object {
                [string] $_.theme -ceq [string] $theme -and
                [string] $_.page -ceq [string] $required.Page -and
                [string] $_.state -ceq [string] $required.State
            })
            if ($matches.Count -eq 0) {
                [void] $defects.Add('state:' + [string] $required.State + ':missing')
            }
            elseif (@($matches | Where-Object { [string] $_.status -cne 'Passed' }).Count -gt 0) {
                [void] $notPassed.Add('state:' + [string] $required.State)
            }
        }
        foreach ($dialog in @($DialogStates)) {
            $matches = @($Entries | Where-Object {
                [string] $_.theme -ceq [string] $theme -and
                [string] $_.state -ceq [string] $dialog.State
            })
            if ($matches.Count -eq 0) {
                [void] $defects.Add('dialog:' + [string] $dialog.State + ':missing')
            }
            elseif (@($matches | Where-Object { [string] $_.status -cne 'Passed' }).Count -gt 0) {
                [void] $notPassed.Add('dialog:' + [string] $dialog.State)
            }
        }
    }

    return [pscustomobject] @{
        ok = ($defects.Count -eq 0)
        defects = $defects.ToArray()
        notPassed = $notPassed.ToArray()
        entryCount = @($Entries).Count
    }
}

<#
.SYNOPSIS
    Projects a theme decision onto an explicit field allowlist.

.DESCRIPTION
    A report is not a decision dump. Only the named non-secret fields are copied; a field added to
    a decision later cannot reach the matrix, and a capability value has no field to travel in.
#>
function Export-UiAcceptanceThemeDecision {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $Decision,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Fields
    )

    if ($null -eq $Decision) { return $null }
    $export = [ordered] @{}
    foreach ($name in @($Fields)) {
        if ($Decision -is [System.Collections.IDictionary]) {
            if ($Decision.Contains([string] $name)) { $export[[string] $name] = $Decision[[string] $name] }
            continue
        }
        $property = $Decision.PSObject.Properties[[string] $name]
        if ($null -ne $property) { $export[[string] $name] = $property.Value }
    }
    return [pscustomobject] $export
}

Export-ModuleMember -Function @(
    'Get-UiAcceptanceThemeJournalPath',
    'Test-UiAcceptanceThemeOverride',
    'Get-UiAcceptanceHarnessIdentity',
    'Get-UiAcceptanceBindingFileDigest',
    'Get-UiAcceptanceThemeBinding',
    'Get-UiAcceptanceJournalPresence',
    'Get-UiAcceptanceThemeProducerState',
    'Get-UiAcceptanceVisualGateField',
    'Get-UiAcceptanceVisualGateValue',
    'Test-UiAcceptanceVisualGateInteger',
    'Get-UiAcceptanceVisualGateExit',
    'Get-UiAcceptanceVisualGateOwnerEcho',
    'Get-UiAcceptanceVisualGateBindingEcho',
    'Get-UiAcceptanceVisualGateOriginalFacts',
    'Invoke-UiAcceptanceThemeCommand',
    'Test-UiAcceptanceThemeInspectResponse',
    'Test-UiAcceptanceThemeApplyResponse',
    'Test-UiAcceptanceThemeRestoreResponse',
    'New-UiAcceptanceThemeRoundState',
    'Invoke-UiAcceptanceThemeApplyPhase',
    'Invoke-UiAcceptanceThemeRestorePhase',
    'Test-UiAcceptanceThemeRoundMayStart',
    'Test-UiAcceptanceThemeRoundMayRestore',
    'Test-UiAcceptanceThemeOriginalMatches',
    'Invoke-UiAcceptanceThemeJournalGate',
    'Export-UiAcceptanceThemeJournalGate',
    'Export-UiAcceptanceThemeDecision',
    'Test-UiAcceptanceVisualMatrixSet'
)
