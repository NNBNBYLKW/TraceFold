# TraceFold Stage 5CC - development behavior cases for the D0203 entry host-condition guard.
#
# Prepared out of the repository and revised after the static review. What this suite is:
#   * only cases that run without any real product evidence, so the toolkit 'All' run never
#     depends on runs that happen after the commit;
#   * the real-chain evidence claim is NOT a case here. It is the V1 post-run gate, executed
#     against the actual runs themselves; before those runs exist it is NotRun - never a pass,
#     never a substitute, never a pre-made report;
#   * every case lifts the shipped text out of the entry between the [D0203-E2 NAME] markers and
#     executes it in the case's own scope with the entry's own variable names, so what runs here
#     is the entry's own code, never a re-implementation.
#
# The entry under test resolves to the repository entry; $env:TRACEFOLD_E2_ENTRY_CANDIDATE may
# point at a prepared candidate file instead, for the out-of-repo regression window.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- helpers ----------------------------------------------------------------------------------

function Assert-HostConditionTrue {
    param([Parameter(Mandatory)][bool] $Condition, [Parameter(Mandatory)][string] $Name)
    if (-not $Condition) { throw ('host-condition case failed: ' + $Name) }
}

function Assert-HostConditionEqual {
    param([Parameter(Mandatory)] $Expected, [Parameter(Mandatory)] $Actual, [Parameter(Mandatory)][string] $Name)
    if ([string] $Expected -cne [string] $Actual) {
        throw ('host-condition case failed: ' + $Name + ' (expected <' + [string] $Expected + '> got <' + [string] $Actual + '>)')
    }
}

function Get-HostConditionEntryPath {
    if (-not [string]::IsNullOrWhiteSpace([string] $env:TRACEFOLD_E2_ENTRY_CANDIDATE)) { return [string] $env:TRACEFOLD_E2_ENTRY_CANDIDATE }
    $uiRoot = ''
    try { $uiRoot = [string] $script:uiRoot } catch { $uiRoot = '' }
    if ([string]::IsNullOrWhiteSpace($uiRoot)) { $uiRoot = Split-Path -Parent $PSScriptRoot }
    return (Join-Path (Split-Path -Parent $uiRoot) 'run-stage5cc-ui-acceptance.ps1')
}

function Get-HostConditionRegion {
    param([Parameter(Mandatory)][string] $Text, [Parameter(Mandatory)][string] $Name)
    $open = '# [D0203-E2 ' + $Name + ']'
    $close = '# [/D0203-E2 ' + $Name + ']'
    $a = $Text.IndexOf($open)
    $b = $Text.IndexOf($close)
    if ($a -lt 0 -or $b -le $a) { throw ('the entry text does not carry the ' + $Name + ' region.') }
    return $Text.Substring($a + $open.Length, $b - $a - $open.Length)
}

function Remove-HostConditionTree {
    param([Parameter(Mandatory)][string] $Path)
    $full = [IO.Path]::GetFullPath($Path)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($full) -notlike 'tracefold-e2-*') { throw 'cleanup target is outside its temporary scope.' }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

function Get-HostConditionZipSupport {
    # Windows PowerShell needs the compression assemblies loaded before their types resolve;
    # on PowerShell 7 both loads are no-ops. A missing assembly surfaces at the use site, which
    # is where the capture records its own error.
    try { Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
    try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }
    return $true
}

function New-HostConditionZipWithMember {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $MemberName,
        [Parameter(Mandatory)][byte[]] $MemberBytes
    )
    $null = Get-HostConditionZipSupport
    $fileStream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entry = $archive.CreateEntry($MemberName)
            $entryStream = $entry.Open()
            try { $entryStream.Write($MemberBytes, 0, $MemberBytes.Length) }
            finally { $entryStream.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    finally { $fileStream.Dispose() }
}

# --- cases ------------------------------------------------------------------------------------

function Test-GuardRegionsFamiliesAndParams {
    $entryPath = Get-HostConditionEntryPath
    Assert-HostConditionTrue ([IO.File]::Exists($entryPath)) 'the entry file exists'
    $text = [IO.File]::ReadAllText($entryPath)
    foreach ($region in @('HOST-FACTS', 'EVIDENCE-FUNCTION', 'CONDITION-SETUP', 'CONDITION-RESTORE', 'INSTANCE-FACTS', 'EVIDENCE-WRITE', 'FINALIZE')) {
        $body = Get-HostConditionRegion -Text $text -Name $region
        Assert-HostConditionTrue (-not [string]::IsNullOrWhiteSpace($body)) ('region ' + $region + ' is present and not empty')
    }
    Assert-HostConditionTrue ($text.IndexOf('chcp') -lt 0) 'no separate chcp process is mentioned anywhere in the entry'

    # the guarded families are exactly the three authorized registry names
    $familyMatch = [regex]::Match($text, '\$hostConditionFamilies\s*=\s*@\(\s*''Library'',\s*''BatchMetadataUndo'',\s*''LibraryFilterNaming''\s*\)')
    Assert-HostConditionTrue $familyMatch.Success 'the guard family list is exactly Library, BatchMetadataUndo, LibraryFilterNaming'

    # order: setup before the instance facts, both before the dispatch block, restore after them
    $setupAt = $text.IndexOf('# [D0203-E2 CONDITION-SETUP]')
    $instancesAt = $text.IndexOf('# [D0203-E2 INSTANCE-FACTS]')
    $restoreAt = $text.IndexOf('# [D0203-E2 CONDITION-RESTORE]')
    $dispatchAt = $text.IndexOf('$entryArguments = @{')
    $writeAt = $text.IndexOf('# [D0203-E2 EVIDENCE-WRITE]')
    $finalizeAt = $text.IndexOf('# [D0203-E2 FINALIZE]')
    Assert-HostConditionTrue ($setupAt -gt 0 -and $setupAt -lt $instancesAt) 'the condition is set before the instance facts'
    Assert-HostConditionTrue ($instancesAt -lt $dispatchAt) 'the instance facts are recorded before dispatch'
    Assert-HostConditionTrue ($instancesAt -lt $restoreAt) 'the restore region follows the instance facts'
    Assert-HostConditionTrue ($writeAt -gt $restoreAt -and $writeAt -lt $finalizeAt) 'the evidence write precedes the finalize merge'

    # The host version is persisted into the document, every identity field carries its state
    # and its error, and the instance facts bind members and the archive member explicitly.
    $hostFactsText = Get-HostConditionRegion -Text $text -Name 'HOST-FACTS'
    Assert-HostConditionTrue ($hostFactsText -match 'hostConditionVersion = \$hostConditionVersion') 'the host version is part of the evidence document'
    foreach ($marker in @('startUtcTicksState', 'startUtcTicksError', 'pathState', 'parentPidState', 'parentPidError', 'commandLineState', 'commandLineError', 'bytesState')) {
        Assert-HostConditionTrue ($hostFactsText.Contains($marker)) ('the host facts carry ' + $marker)
    }
    $instanceText = Get-HostConditionRegion -Text $text -Name 'INSTANCE-FACTS'
    foreach ($marker in @('memberCount', 'allMatchSource', 'matchesArchiveMember', 'archiveMemberSha256', 'winappExeFileVersion')) {
        Assert-HostConditionTrue ($instanceText.Contains($marker)) ('the instance facts carry ' + $marker)
    }

    # the public parameter surface is untouched: the same inputs and nothing more
    $paramAt = $text.IndexOf("param(`n")
    Assert-HostConditionTrue ($paramAt -gt 0) 'the entry param block is present'
    $paramEnd = $text.IndexOf("`n)", $paramAt)
    Assert-HostConditionTrue ($paramEnd -gt $paramAt) 'the entry param block closes'
    $paramBlock = $text.Substring($paramAt, $paramEnd - $paramAt)
    Assert-HostConditionEqual 2 (([regex]::Matches($paramBlock, '\[Parameter\(Mandatory\)\]')).Count) 'exactly the two original mandatory parameters remain'
    Assert-HostConditionTrue ($paramBlock -match '\$ProductDirectory') 'ProductDirectory is still an input'
    Assert-HostConditionTrue ($paramBlock -match '\$WinAppCliArchivePath') 'WinAppCliArchivePath is still an input'
    Assert-HostConditionTrue ($paramBlock -match '\$OutputDirectory') 'OutputDirectory is still an input'
    $scenarioSet = [regex]::Match($paramBlock, "\[ValidateSet\('All','StartupLayout','MainChain','Conflict','ConflictKeepBoth','ConflictReplace','ConflictSkip','ProcessingFailureRetry','BatchMetadataUndo','LibraryFilterNaming','Library','Taxonomy','Diagnostics','SingleInstance','Visual'\)\]")
    Assert-HostConditionTrue $scenarioSet.Success 'the scenario ValidateSet is unchanged'
}

function Test-ConditionIsSetVisibleAndRestored {
    $entryPath = Get-HostConditionEntryPath
    $text = [IO.File]::ReadAllText($entryPath)
    $setupText = Get-HostConditionRegion -Text $text -Name 'CONDITION-SETUP'
    $restoreText = Get-HostConditionRegion -Text $text -Name 'CONDITION-RESTORE'
    $caseSaved = [Console]::OutputEncoding
    try {
        # a non-guarded scenario never touches the console and records no claim
        $name = 'StartupLayout'
        $hostConditionApplies = $false
        $hostConditionAttempted = $false
        $hostConditionRestoreFailed = $false
        $savedOutputEncoding = $null
        $hostEvidence = [ordered] @{ evidence = 'host-condition'; version = 1; process = $null; scenarios = (New-Object System.Collections.ArrayList) }
        . ([scriptblock]::Create($setupText))
        Assert-HostConditionEqual $caseSaved.CodePage ([Console]::OutputEncoding.CodePage) 'a non-guarded scenario never touches the console'
        Assert-HostConditionTrue ($null -eq $hostEvidence.scenarios[0].condition) 'a non-guarded scenario records no condition claim'

        # a guarded scenario holds 936 while the body runs in this same process
        $name = 'Library'
        $hostConditionApplies = $true
        $hostConditionAttempted = $false
        $hostConditionRestoreFailed = $false
        $savedOutputEncoding = $null
        $hostEvidence = [ordered] @{ evidence = 'host-condition'; version = 1; process = $null; scenarios = (New-Object System.Collections.ArrayList) }
        . ([scriptblock]::Create($setupText))
        Assert-HostConditionTrue ([bool] $scenarioEvidence.condition.established) 'the condition was established'
        Assert-HostConditionEqual $caseSaved.CodePage $scenarioEvidence.condition.beforeCodePage 'before records the saved page'
        Assert-HostConditionEqual 936 $scenarioEvidence.condition.duringCodePage 'during is 936'
        Assert-HostConditionEqual 936 ([Console]::OutputEncoding.CodePage) 'the double-byte page is visible to whatever runs next in this process'
        . ([scriptblock]::Create($restoreText))
        Assert-HostConditionTrue ([bool] $scenarioEvidence.condition.restoreOk) 'the restore read back the saved page'
        Assert-HostConditionEqual $scenarioEvidence.condition.beforeCodePage $scenarioEvidence.condition.afterCodePage 'before equals after'
        Assert-HostConditionEqual $caseSaved.CodePage ([Console]::OutputEncoding.CodePage) 'the console is handed back'
        Assert-HostConditionTrue (-not $hostConditionRestoreFailed) 'no restore failure was recorded'
    }
    finally { [Console]::OutputEncoding = $caseSaved }
}

function Test-ConditionRestoredWhenBodyThrows {
    $entryPath = Get-HostConditionEntryPath
    $text = [IO.File]::ReadAllText($entryPath)
    $setupText = Get-HostConditionRegion -Text $text -Name 'CONDITION-SETUP'
    $restoreText = Get-HostConditionRegion -Text $text -Name 'CONDITION-RESTORE'
    $caseSaved = [Console]::OutputEncoding
    $caught = ''
    try {
        $name = 'LibraryFilterNaming'
        $hostConditionApplies = $true
        $hostConditionAttempted = $false
        $hostConditionRestoreFailed = $false
        $savedOutputEncoding = $null
        $hostEvidence = [ordered] @{ evidence = 'host-condition'; version = 1; process = $null; scenarios = (New-Object System.Collections.ArrayList) }
        try {
            . ([scriptblock]::Create($setupText))
            throw 'e2-sentinel-body-failure'
        }
        catch { $caught = [string] $_.Exception.Message }
        finally { . ([scriptblock]::Create($restoreText)) }
        Assert-HostConditionEqual 'e2-sentinel-body-failure' $caught 'a body failure is never swallowed'
        Assert-HostConditionTrue ([bool] $scenarioEvidence.condition.restoreOk) 'the finally restored and read the page back on the throwing path'
        Assert-HostConditionEqual $scenarioEvidence.condition.beforeCodePage ([Console]::OutputEncoding.CodePage) 'the console is handed back on the throwing path'
    }
    finally { [Console]::OutputEncoding = $caseSaved }
}

function Test-EvidenceWriteRefusesExistingFile {
    $entryPath = Get-HostConditionEntryPath
    $text = [IO.File]::ReadAllText($entryPath)
    $functionText = Get-HostConditionRegion -Text $text -Name 'EVIDENCE-FUNCTION'
    . ([scriptblock]::Create($functionText))
    $root = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-e2-evidence-' + [guid]::NewGuid().ToString('N'))
    try {
        [void][IO.Directory]::CreateDirectory($root)
        $document = [ordered] @{ evidence = 'host-condition'; version = 1; process = $null; scenarios = @() }

        # first write into a fresh directory is allowed
        $first = Write-Stage5CCHostEvidence -OutputDirectory $root -Document $document -ClaimWrite $false
        Assert-HostConditionTrue ([bool] $first.ok) 'the first write into a fresh directory succeeds'

        # a pre-existing file is never overwritten by a process that did not write it
        $decoy = 'pre-existing-evidence-bytes'
        [IO.File]::WriteAllText((Join-Path $root 'host-condition.json'), $decoy, [Text.UTF8Encoding]::new($false))
        $refused = Write-Stage5CCHostEvidence -OutputDirectory $root -Document $document -ClaimWrite $false
        Assert-HostConditionTrue (-not [bool] $refused.ok) 'a pre-existing evidence file is refused'
        Assert-HostConditionEqual 'evidence_already_present' $refused.reason 'the refusal is named'
        Assert-HostConditionEqual $decoy ([IO.File]::ReadAllText((Join-Path $root 'host-condition.json'))) 'the pre-existing bytes are untouched'

        # the same process may continue its own document
        $rewrite = Write-Stage5CCHostEvidence -OutputDirectory $root -Document $document -ClaimWrite $true
        Assert-HostConditionTrue ([bool] $rewrite.ok) 'the claiming process may continue its own document'
    }
    finally { Remove-HostConditionTree -Path $root }
}

function Test-EvidenceWriteFailureIsReturnedNotThrown {
    $entryPath = Get-HostConditionEntryPath
    $text = [IO.File]::ReadAllText($entryPath)
    $functionText = Get-HostConditionRegion -Text $text -Name 'EVIDENCE-FUNCTION'
    . ([scriptblock]::Create($functionText))
    $root = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-e2-blocked-' + [guid]::NewGuid().ToString('N'))
    try {
        $blocker = Join-Path $root 'blocker.txt'
        [void][IO.Directory]::CreateDirectory($root)
        [IO.File]::WriteAllText($blocker, 'x', [Text.UTF8Encoding]::new($false))
        $document = [ordered] @{ evidence = 'host-condition'; version = 1; process = $null; scenarios = @() }
        $result = Write-Stage5CCHostEvidence -OutputDirectory (Join-Path $blocker 'sub') -Document $document -ClaimWrite $false
        Assert-HostConditionTrue ($null -ne $result) 'the failure is returned as a result object, not thrown'
        Assert-HostConditionTrue (-not [bool] $result.ok) 'the write failure reports not-ok'
        Assert-HostConditionTrue (-not [string]::IsNullOrWhiteSpace([string] $result.reason)) 'the write failure is named'
    }
    finally { Remove-HostConditionTree -Path $root }
}

function Test-FinalizeFlagsNeverFakePass {
    $entryPath = Get-HostConditionEntryPath
    $text = [IO.File]::ReadAllText($entryPath)
    $finalizeText = Get-HostConditionRegion -Text $text -Name 'FINALIZE'

    # quiet flags change nothing
    $hostConditionRestoreFailed = $false
    $hostEvidenceWriteFailure = ''
    $executionStatus = 'Passed'
    $reason = ''
    . ([scriptblock]::Create($finalizeText))
    Assert-HostConditionEqual 'Passed' $executionStatus 'quiet flags leave a pass a pass'
    Assert-HostConditionEqual '' $reason 'quiet flags add no reason'

    # a failed restore upgrades a pass and names itself
    $hostConditionRestoreFailed = $true
    $hostEvidenceWriteFailure = ''
    $executionStatus = 'Passed'
    $reason = ''
    . ([scriptblock]::Create($finalizeText))
    Assert-HostConditionEqual 'Failed' $executionStatus 'a failed restore can never stay a pass'
    Assert-HostConditionTrue ($reason -like '*host_condition_restore_failed*') 'the restore failure is named in the reason'

    # a failed evidence write upgrades a pass and names itself
    $hostConditionRestoreFailed = $false
    $hostEvidenceWriteFailure = 'disk full'
    $executionStatus = 'Passed'
    $reason = ''
    . ([scriptblock]::Create($finalizeText))
    Assert-HostConditionEqual 'Failed' $executionStatus 'a failed evidence write can never stay a pass'
    Assert-HostConditionTrue ($reason -like '*host_evidence_write_failed*') 'the evidence failure is named in the reason'

    # an existing business failure or block is preserved and only gains the markers
    $hostConditionRestoreFailed = $true
    $hostEvidenceWriteFailure = 'disk full'
    $executionStatus = 'Blocked'
    $reason = 'the product refused: boom'
    . ([scriptblock]::Create($finalizeText))
    Assert-HostConditionEqual 'Blocked' $executionStatus 'a blocked business state is never rewritten'
    Assert-HostConditionTrue ($reason -like '*boom*') 'the original business reason survives'
    Assert-HostConditionTrue ($reason -like '*host_condition_restore_failed*') 'the restore marker is appended'
    Assert-HostConditionTrue ($reason -like '*host_evidence_write_failed*') 'the evidence marker is appended'
}

function Test-HostFactsCaptureIsPersisted {
    $entryPath = Get-HostConditionEntryPath
    $text = [IO.File]::ReadAllText($entryPath)
    $regionText = Get-HostConditionRegion -Text $text -Name 'HOST-FACTS'
    $caseRoot = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-e2-hostfacts-' + [guid]::NewGuid().ToString('N'))
    try {
        [void][IO.Directory]::CreateDirectory($caseRoot)
        # Synthetic inputs for the capture itself; they claim nothing about a real product.
        $productExecutable = Join-Path $caseRoot 'TraceFold.Media.exe'
        [IO.File]::WriteAllText($productExecutable, 'synthetic-product-placeholder', [Text.UTF8Encoding]::new($false))
        $productHash = 'SYNTHETIC-NOT-A-REAL-HASH'
        $productVersion = '0.0.0-synthetic'
        . ([scriptblock]::Create($regionText))
        Assert-HostConditionTrue ($null -ne $hostEvidence) 'the host facts build an evidence document'
        Assert-HostConditionTrue ($null -ne $hostEvidence.hostConditionVersion) 'the document carries the host version record'
        Assert-HostConditionEqual 'captured' $hostEvidence.hostConditionVersion.psVersionState 'the host version capture state is recorded'
        Assert-HostConditionTrue (-not [string]::IsNullOrWhiteSpace([string] $hostEvidence.hostConditionVersion.psVersion)) 'the host version was really read'
        Assert-HostConditionEqual 'captured' $hostEvidence.hostConditionVersion.psEditionState 'the edition capture state is recorded'
        Assert-HostConditionEqual 'captured' $hostEvidence.hostConditionVersion.clrState 'the clr capture state is recorded'
        Assert-HostConditionTrue ([int] $hostEvidence.process.pid -eq $PID) 'the document records this very process'
        Assert-HostConditionEqual 'captured' $hostEvidence.process.startUtcTicksState 'the start ticks state is recorded'
        Assert-HostConditionTrue ([long] $hostEvidence.process.startUtcTicks -gt 0) 'the real start ticks were read'
        Assert-HostConditionEqual 'captured' $hostEvidence.process.pathState 'the host path state is recorded'
        Assert-HostConditionTrue (-not [string]::IsNullOrWhiteSpace([string] $hostEvidence.process.path)) 'the host path was read'
        Assert-HostConditionEqual 'captured' $hostEvidence.process.parentPidState 'the parent pid state is recorded'
        Assert-HostConditionTrue ([int] $hostEvidence.process.parentPid -gt 0) 'the parent pid was read'
        Assert-HostConditionEqual 'captured' $hostEvidence.process.commandLineState 'the command line state is recorded'
        Assert-HostConditionTrue (-not [string]::IsNullOrWhiteSpace([string] $hostEvidence.process.commandLine)) 'the actual command line was read'
        Assert-HostConditionEqual 'captured' $hostEvidence.process.product.bytesState 'the product bytes state is recorded'
        Assert-HostConditionTrue ([long] $hostEvidence.process.product.bytes -gt 0) 'the product bytes were read'
        Assert-HostConditionEqual 'SYNTHETIC-NOT-A-REAL-HASH' $hostEvidence.process.product.sha256 'the recorded hash is exactly the one this process computed'
    }
    finally { Remove-HostConditionTree -Path $caseRoot }
}

function Test-InstanceFactsCaptureBindsMembers {
    $entryPath = Get-HostConditionEntryPath
    $text = [IO.File]::ReadAllText($entryPath)
    $regionText = Get-HostConditionRegion -Text $text -Name 'INSTANCE-FACTS'
    $caseRoot = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-e2-instances-' + [guid]::NewGuid().ToString('N'))
    try {
        # A synthetic support tree and a synthetic archive: they prove what the capture records
        # about members and mismatches, and they claim nothing about an unrun real chain. The
        # variable names match the entry's own names ($supportRoot / $runSupport / $runDriver),
        # because the region runs with the entry's own variable names.
        $supportRoot = Join-Path $caseRoot 'source'
        $supportInstance = Join-Path $caseRoot 'instance'
        $driverInstance = Join-Path $caseRoot 'driver'
        $nestedDir = Join-Path $supportRoot 'runtimes/win-x64/native'
        [void][IO.Directory]::CreateDirectory($nestedDir)
        [void][IO.Directory]::CreateDirectory($driverInstance)
        [IO.File]::WriteAllText((Join-Path $supportRoot 'TraceFold.Media.UiAcceptance.Support.exe'), 'synthetic-support-exe-payload', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $supportRoot 'TraceFold.Media.UiAcceptance.Support.dll'), 'synthetic-support-dll-payload', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $nestedDir 'e_sqlite3.dll'), 'synthetic-native-payload', [Text.UTF8Encoding]::new($false))
        Copy-Item -LiteralPath $supportRoot -Destination $supportInstance -Recurse
        $winappBytes = [Text.Encoding]::UTF8.GetBytes('synthetic-winapp-payload')
        [IO.File]::WriteAllBytes((Join-Path $driverInstance 'winapp.exe'), $winappBytes)
        $archivePath = Join-Path $caseRoot 'winappcli-synthetic.zip'
        New-HostConditionZipWithMember -Path $archivePath -MemberName 'winapp.exe' -MemberBytes $winappBytes
        $archiveSha = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
        $driverManifest = @{ sha256 = $archiveSha; archiveSize = [long] ((Get-Item -LiteralPath $archivePath).Length); version = '0.0.0-synthetic' }
        $runRoot = $caseRoot
        $runSupport = $supportInstance
        $runDriver = $driverInstance
        $runToken = 'synthetic-token'
        $scenarioEvidence = [ordered] @{ name = 'LibraryFilterNaming'; condition = $null; instances = $null }
        . ([scriptblock]::Create($regionText))
        $facts = $scenarioEvidence.instances
        Assert-HostConditionTrue ($null -ne $facts) 'the region records the instance facts'
        Assert-HostConditionEqual 3 $facts.support.memberCount 'every copied member is enumerated'
        Assert-HostConditionTrue ([bool] $facts.support.allMatchSource) 'every copied member matches its source bytes'
        Assert-HostConditionEqual 'captured' $facts.support.captureState 'the support capture state is recorded'
        $nested = @($facts.support.members | Where-Object { [string] $_.relative -ceq 'runtimes\win-x64\native\e_sqlite3.dll' })
        Assert-HostConditionEqual 1 $nested.Count 'the nested member is recorded with its relative path'
        Assert-HostConditionTrue ([bool] $nested[0].matchesSource) 'the nested member matches its source'
        Assert-HostConditionTrue ([bool] $facts.driver.matchesManifest) 'the archive matches the manifest values it was given'
        Assert-HostConditionEqual 'captured' $facts.driver.memberReadState 'the archive member read state is recorded'
        Assert-HostConditionTrue ([bool] $facts.driver.matchesArchiveMember) 'the extracted executable equals the archive member'
        Assert-HostConditionEqual ([string] $facts.driver.winappExeSha256) ([string] $facts.driver.archiveMemberSha256) 'the executable and the member carry the same hash'
        # A changed instance is recorded as a mismatch, never as a silent pass.
        [IO.File]::AppendAllText((Join-Path $driverInstance 'winapp.exe'), 'tampered')
        $scenarioEvidence = [ordered] @{ name = 'LibraryFilterNaming'; condition = $null; instances = $null }
        . ([scriptblock]::Create($regionText))
        $tampered = $scenarioEvidence.instances
        Assert-HostConditionTrue ($null -ne $tampered) 'the second capture also records facts'
        Assert-HostConditionTrue (-not [bool] $tampered.driver.matchesArchiveMember) 'a tampered executable no longer matches the member'
        Assert-HostConditionEqual 'captured' $tampered.driver.memberReadState 'the member read still succeeds on the tampered instance'
    }
    finally { Remove-HostConditionTree -Path $caseRoot }
}

# --- toolkit contract -------------------------------------------------------------------------

function Get-TestCases {
    $names = @(
        'Test-GuardRegionsFamiliesAndParams',
        'Test-ConditionIsSetVisibleAndRestored',
        'Test-ConditionRestoredWhenBodyThrows',
        'Test-EvidenceWriteRefusesExistingFile',
        'Test-EvidenceWriteFailureIsReturnedNotThrown',
        'Test-FinalizeFlagsNeverFakePass',
        'Test-HostFactsCaptureIsPersisted',
        'Test-InstanceFactsCaptureBindsMembers'
    )
    $cases = New-Object 'System.Collections.Generic.List[object]'
    foreach ($name in $names) {
        $caseName = [string] $name
        $body = [scriptblock]::Create('& ''' + $caseName + '''')
        [void] $cases.Add(@{ Name = $caseName; Body = $body })
    }
    return $cases.ToArray()
}
