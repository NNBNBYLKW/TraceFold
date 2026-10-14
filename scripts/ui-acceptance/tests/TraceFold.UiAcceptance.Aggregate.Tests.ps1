# TraceFold Stage 5CC - adapted toolkit test for the 02/03 aggregate entries.
#
# This is an adaptation copy, not the candidate itself. The candidate is the standalone script at
# tests/test-aggregate-candidate.ps1 in the candidate root; this copy exists only to sit in
# scripts/ui-acceptance/tests and be dot-sourced by the toolkit's Get-TestCases contract.
#
# Three things differ from the candidate, and they are the only things that differ:
#   1. roots. The candidate reads -ModulesRoot/-SourceRoot/-WorkRoot off its own command line and
#      defaults them into the candidate directory. A dot-sourced file has no command line, and the
#      integrated layout is two directories, so this copy takes the module root from the toolkit's
#      $script:uiRoot and puts the entries in its scenarios sub-directory.
#   2. harness names. The two assertion helpers and the list of failures they add to are renamed
#      with an Aggregate prefix, because the toolkit defines the same three names for every other
#      suite and a dot-sourced file would silently replace them. The behaviour is unchanged:
#      assertions still accumulate instead of stopping the case at the first failure.
#   3. the tail. The candidate's self-run, its console report and its three exit statements are
#      gone. Get-TestCases returns one case per test function and the body raises, which is how the
#      toolkit decides a case failed.
#
# The cases, the assertions and the judging chain are the candidate's own, copied verbatim.
#
# What this runs for real:
#   * the real Invoke-UiAcceptanceRun -> Invoke-UiAcceptanceScenario -> candidate entry chain, so
#     the report contract, the status assertion, the argument filter, the artifact handling and
#     the cleanup bookkeeping are the shipped ones;
#   * the real candidate composition engine, so every judgement is the shipped code.
#
# What is substituted, and why:
#   * the child process spawn. A real child would be a real Conflict or Library run, which this
#     card does not authorise. The substitute is an injected scriptblock that declares itself as a
#     substitute (invokerKind='substitute'), and the composition record repeats that as
#     'substituteChildren'. A pass here is never a claim that the real aggregate passed, and never
#     a claim that the substitute ran the real public entry as a child process.
#
# The substitute still has to produce a child tree shaped exactly like the public entry's real
# output (summary.json next to the output root, report.json where the summary declares it, evidence
# files beside it), because the engine re-reads that tree and rejects a shape it does not know.



Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Integrated roots. The toolkit dot-sources this file, so there is no command line to read roots
# from and no candidate directory to fall back on. Two roots, not one: the engine module sits in
# the module root and the entries sit in the scenarios root. The candidate got away with a single
# root only because it kept both in one directory; here, one root would point one of the two at a
# directory that does not hold it and the suite would quietly test something else.
$uiRoot = ''
try { $uiRoot = [string] $script:uiRoot } catch { $uiRoot = '' }
if ([string]::IsNullOrWhiteSpace($uiRoot)) {
    # Standalone fallback: $PSScriptRoot is the tests directory, so its parent is the ui-acceptance
    # root. This keeps the file resolvable if it is ever executed directly instead of dot-sourced.
    $uiRoot = Split-Path -Parent $PSScriptRoot
}
$script:modulesRoot = $uiRoot
$script:moduleRoot = $uiRoot
$script:scenarioRoot = [IO.Path]::Combine($uiRoot, 'scenarios')
$script:workRoot = [IO.Path]::Combine([IO.Path]::GetTempPath(), ('task0203-aggregate-' + [guid]::NewGuid().ToString('N')))

Import-Module ([IO.Path]::Combine($script:modulesRoot, 'TraceFold.UiAcceptance.Contracts.psm1')) -Force -DisableNameChecking
Import-Module ([IO.Path]::Combine($script:modulesRoot, 'TraceFold.UiAcceptance.Evidence.psm1')) -Force -DisableNameChecking
Import-Module ([IO.Path]::Combine($script:modulesRoot, 'TraceFold.UiAcceptance.Processes.psm1')) -Force -DisableNameChecking
Import-Module ([IO.Path]::Combine($script:modulesRoot, 'TraceFold.UiAcceptance.Runner.psm1')) -Force -DisableNameChecking

# --- assertion harness ------------------------------------------------------------------------

$script:checks = New-Object 'System.Collections.Generic.List[object]'
$script:aggregateFailed = New-Object 'System.Collections.Generic.List[string]'

function Assert-AggregateTrue {
    param([Parameter(Mandatory)][bool] $Condition, [Parameter(Mandatory)][string] $Name)
    [void] $script:checks.Add($Name)
    if (-not $Condition) { [void] $script:aggregateFailed.Add($Name) }
}

function Assert-AggregateEqual {
    param([Parameter(Mandatory)] $Expected, [Parameter(Mandatory)] $Actual, [Parameter(Mandatory)][string] $Name)
    $ok = ([string] $Expected) -ceq ([string] $Actual)
    [void] $script:checks.Add($Name)
    if (-not $ok) { [void] $script:aggregateFailed.Add(($Name + ' (expected <' + ([string] $Expected) + '> got <' + ([string] $Actual) + '>)')) }
}

# --- shared fixtures --------------------------------------------------------------------------

$script:runToken = [guid]::NewGuid().ToString('N')

# The identity this run claims. Both the expectation and the substitute read it from here, so a
# 'matching' child really matches and a 'wrong' child really differs.
$script:currentSourceCommit = 'expected-source-commit'
$script:currentDriverVersion = '0.5.0'
$script:sourceCommitResolver = { return $script:currentSourceCommit }

function New-CandidateFixture {
    param([Parameter(Mandatory)][string] $Name)

    $work = [IO.Path]::Combine($script:workRoot, $Name)
    [void][IO.Directory]::CreateDirectory($work)

    # Stand-ins for the published product executable and the pinned driver archive. The engine only
    # needs them to exist, and to hash, because every child report is bound to both hashes.
    $product = [IO.Path]::Combine($work, 'product')
    [void][IO.Directory]::CreateDirectory($product)
    $executable = [IO.Path]::Combine($product, 'TraceFold.Media.exe')
    [IO.File]::WriteAllText($executable, 'candidate-stand-in', [Text.UTF8Encoding]::new($false))
    $archive = [IO.Path]::Combine($work, 'winappcli-x64.zip')
    [IO.File]::WriteAllText($archive, 'candidate-stand-in-archive', [Text.UTF8Encoding]::new($false))

    # A stand-in for the public entry: it only has to exist for the prerequisite check, because the
    # real spawn is substituted.
    $entry = [IO.Path]::Combine($work, 'entry-stub.ps1')
    [IO.File]::WriteAllText($entry, '# candidate stand-in for the public entry' + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

    return [ordered] @{
        Work        = $work
        Executable  = $executable
        Archive     = $archive
        Entry       = $entry
        ProductHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
        DriverHash  = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        Output      = [IO.Path]::Combine($work, 'output')
        OwnedRoot   = [IO.Path]::Combine($work, 'owned')
    }
}

<#
.SYNOPSIS
    Writes a child tree with the shape the public entry produces.

.DESCRIPTION
    Every field the engine re-reads is written here, and every option is a deliberate hole: a
    contradictory summary, a declared path that points somewhere else, an extra scenario, a later
    step that failed, evidence declared but absent, a wrong commit or driver version. The engine
    has to notice each one, and a matching tree has to pass.
#>
# What the generator actually wrote, keyed by the child output directory it wrote it into. A case
# reads this back to prove the tree it meant to hand the engine is the tree that is on disk,
# instead of trusting that the options it passed were honoured.
$script:fakeTreeLayout = [ordered] @{}

<#
.SYNOPSIS
    Reads one field of a generated step the way the node it really is.

.DESCRIPTION
    A step handed to the generator can be a Hashtable, an ordered dictionary or a decoded JSON
    object. PSObject.Properties only sees the last of them, so reading every step through it made
    EVERY generated step fall back to the default evidence file: the cases that were supposed to
    hand the engine a failing step, a missing evidence file or an escaping path were silently
    handing it a clean one and then recording a Passed that proved nothing.

    Presence is reported separately from the value, because an absent key, an explicit empty array
    and an explicit null are three different declarations and the generator has to keep them
    apart: the first falls back to the default, the other two are declarations of 'no evidence'
    that must reach the report exactly as declared.
#>
function Get-FakeStepValue {
    param(
        [Parameter(Mandatory)][AllowNull()] $Step,
        [Parameter(Mandatory)][string] $Name,
        [ref] $Found
    )

    if ($null -ne $Found) { $Found.Value = $false }
    if ($null -eq $Step) { return $null }

    if ($Step -is [System.Collections.IDictionary]) {
        if ($Step.Contains($Name)) {
            if ($null -ne $Found) { $Found.Value = $true }
            return $Step[$Name]
        }
        return $null
    }

    $property = $Step.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    if ($null -ne $Found) { $Found.Value = $true }
    return $property.Value
}

<#
.SYNOPSIS
    The generator's own copy of the relative-path rule, used only to decide where it may safely
    put a file on disk. The judgement about a declared path is the engine's, never this one's.
#>
function Test-FakeRelativePath {
    param([Parameter(Mandatory)][string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    if ([IO.Path]::IsPathRooted($Value)) { return $false }
    if ($Value -match '(^|[\\/])\.\.([\\/]|$)') { return $false }
    return $true
}

function Write-FakeChildTree {
    param(
        [Parameter(Mandatory)][string] $ChildOutput,
        [Parameter(Mandatory)][string] $ChildId,
        [Parameter(Mandatory)][string] $Slug,
        [Parameter(Mandatory)][string] $ProductHash,
        [Parameter(Mandatory)][string] $DriverHash,
        [hashtable] $Options = @{}
    )

    $entryStatus = 'Passed'
    if ($Options.ContainsKey('EntryStatus')) { $entryStatus = [string] $Options['EntryStatus'] }
    $reportStatus = $entryStatus
    if ($Options.ContainsKey('ReportStatus')) { $reportStatus = [string] $Options['ReportStatus'] }
    $executionStatus = $entryStatus
    if ($Options.ContainsKey('ExecutionStatus')) { $executionStatus = [string] $Options['ExecutionStatus'] }
    $summaryStatus = $entryStatus
    if ($Options.ContainsKey('SummaryStatus')) { $summaryStatus = [string] $Options['SummaryStatus'] }
    $summaryComplete = ($entryStatus -ceq 'Passed')
    if ($Options.ContainsKey('SummaryComplete')) { $summaryComplete = [bool] $Options['SummaryComplete'] }

    $cleanupStatus = 'Passed'
    if ($Options.ContainsKey('CleanupStatus')) { $cleanupStatus = [string] $Options['CleanupStatus'] }
    $sourceCommit = $script:currentSourceCommit
    if ($Options.ContainsKey('SourceCommit')) { $sourceCommit = [string] $Options['SourceCommit'] }
    $driverVersion = $script:currentDriverVersion
    if ($Options.ContainsKey('DriverVersion')) { $driverVersion = [string] $Options['DriverVersion'] }
    $startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    if ($Options.ContainsKey('StartedUtc')) { $startedUtc = [string] $Options['StartedUtc'] }
    $dispatched = 1
    if ($Options.ContainsKey('DispatchedCount')) { $dispatched = [int] $Options['DispatchedCount'] }
    $summaryEntryId = $ChildId
    if ($Options.ContainsKey('SummaryEntryId')) { $summaryEntryId = [string] $Options['SummaryEntryId'] }
    $reportSlug = $Slug
    if ($Options.ContainsKey('ReportSlug')) { $reportSlug = [string] $Options['ReportSlug'] }
    $declaredReportPath = ($ChildId + '/report.json')
    if ($Options.ContainsKey('DeclaredReportPath')) { $declaredReportPath = [string] $Options['DeclaredReportPath'] }
    # Where report.json is ACTUALLY written. It defaults to the same string the summary declares,
    # which is what a real child produces, but it is a separate knob on purpose: a case that
    # declares a wrong path still needs the real file to exist somewhere else, otherwise the
    # engine is being asked to distinguish 'missing' from 'declared elsewhere' with nothing to
    # compare against.
    $reportFileRelative = ($ChildId + '/report.json')
    if ($Options.ContainsKey('ReportFileRelative')) { $reportFileRelative = [string] $Options['ReportFileRelative'] }
    # The driver hash the report is really bound to. It has to be overridable, or a case that
    # claims 'this child was bound to another driver' writes the correct hash and proves nothing.
    $reportDriverHash = $DriverHash
    if ($Options.ContainsKey('DriverHash')) { $reportDriverHash = [string] $Options['DriverHash'] }
    $evidence = @('evidence.json')
    if ($Options.ContainsKey('Evidence')) { $evidence = @($Options['Evidence']) }
    $steps = @(@{ Status = 'Passed'; Evidence = $evidence })
    if ($Options.ContainsKey('Steps')) { $steps = @($Options['Steps']) }
    $scenarioStatus = 'Passed'
    if ($Options.ContainsKey('ScenarioStatus')) { $scenarioStatus = [string] $Options['ScenarioStatus'] }
    $writeSummary = $true
    if ($Options.ContainsKey('WriteSummary')) { $writeSummary = [bool] $Options['WriteSummary'] }
    $writeReport = $true
    if ($Options.ContainsKey('WriteReport')) { $writeReport = [bool] $Options['WriteReport'] }
    $invalidSummary = $false
    if ($Options.ContainsKey('InvalidSummary')) { $invalidSummary = [bool] $Options['InvalidSummary'] }
    $invalidReport = $false
    if ($Options.ContainsKey('InvalidReport')) { $invalidReport = [bool] $Options['InvalidReport'] }
    $extraScenario = $null
    if ($Options.ContainsKey('ExtraScenario')) { $extraScenario = $Options['ExtraScenario'] }

    [void][IO.Directory]::CreateDirectory($ChildOutput)
    # The report directory follows the file that is really written, not the path that is declared:
    # evidence lives next to the report, so the two have to agree or every evidence check becomes
    # a statement about the wrong directory.
    $reportDir = $ChildOutput
    if (Test-FakeRelativePath -Value $reportFileRelative) {
        $reportParent = [IO.Path]::GetDirectoryName($reportFileRelative)
        if (-not [string]::IsNullOrWhiteSpace($reportParent)) { $reportDir = [IO.Path]::Combine($ChildOutput, $reportParent) }
    }
    [void][IO.Directory]::CreateDirectory($reportDir)

    if ($writeSummary) {
        $summary = [ordered] @{
            checkpoint = '5CC'
            complete   = $summaryComplete
            status     = $summaryStatus
            reason     = ''
            entries    = @([ordered] @{
                id              = $summaryEntryId
                status          = $entryStatus
                reportStatus    = $reportStatus
                executionStatus = $executionStatus
                reason          = ''
                reportPath      = $declaredReportPath
                outputDirectory = $ChildId
                durationMs      = 1
                complete        = ($entryStatus -ceq 'Passed')
            })
            checks     = [ordered] @{
                scope                 = 'request'
                checkedCount          = 1
                checkedIds            = @($ChildId)
                missingCount          = 0
                missing               = @()
                blockedBeforeDispatch = $false
                dispatchedCount       = $dispatched
                checkedUtc            = [DateTimeOffset]::UtcNow.ToString('o')
            }
        }
        $summaryText = ConvertTo-Json -InputObject $summary -Depth 20
        if ($invalidSummary) { $summaryText = '{ this is not json' }
        [IO.File]::WriteAllText([IO.Path]::Combine($ChildOutput, 'summary.json'), $summaryText, [Text.UTF8Encoding]::new($false))
    }

    # Declared before the block that fills it, so the layout this generator reports is complete
    # even for a child that produced no report at all.
    $stepRecords = New-Object 'System.Collections.Generic.List[object]'
    if ($writeReport) {
        # Evidence for every step, so a later step with missing evidence is really a missing file
        # rather than an artifact of only the first step being written.
        foreach ($step in $steps) {
            $evidenceSeen = $false
            $declaredEvidence = Get-FakeStepValue -Step $step -Name 'Evidence' -Found ([ref] $evidenceSeen)
            $stepEvidence = @('evidence.json')
            if ($evidenceSeen) {
                if ($null -eq $declaredEvidence) { $stepEvidence = @() }
                else { $stepEvidence = @($declaredEvidence) }
            }
            foreach ($relative in $stepEvidence) {
                $target = [IO.Path]::Combine($reportDir, [string] $relative)
                $parent = [IO.Path]::GetDirectoryName($target)
                if (-not [string]::IsNullOrWhiteSpace($parent)) { [void][IO.Directory]::CreateDirectory($parent) }
                [IO.File]::WriteAllText($target, '{}', [Text.UTF8Encoding]::new($false))
            }
        }
        foreach ($missingRelative in @($Options['MissingEvidenceFiles'])) {
            if ([string]::IsNullOrWhiteSpace([string] $missingRelative)) { continue }
            $target = [IO.Path]::Combine($reportDir, [string] $missingRelative)
            if ([IO.File]::Exists($target)) { [IO.File]::Delete($target) }
        }

        $stepIndex = 0
        foreach ($step in $steps) {
            $stepIndex = $stepIndex + 1
            $stepStatus = 'Passed'
            $statusSeen = $false
            $declaredStatus = Get-FakeStepValue -Step $step -Name 'Status' -Found ([ref] $statusSeen)
            if ($statusSeen) { $stepStatus = [string] $declaredStatus }
            $evidenceSeen = $false
            $declaredEvidence = Get-FakeStepValue -Step $step -Name 'Evidence' -Found ([ref] $evidenceSeen)
            $stepEvidence = @('evidence.json')
            if ($evidenceSeen) {
                if ($null -eq $declaredEvidence) { $stepEvidence = @() }
                else { $stepEvidence = @($declaredEvidence) }
            }
            [void] $stepRecords.Add([ordered] @{
                id            = ('step-' + $stepIndex)
                status        = $stepStatus
                durationMs    = 1
                reason        = ''
                evidencePaths = @($stepEvidence)
            })
        }

        $scenarioList = New-Object 'System.Collections.Generic.List[object]'
        [void] $scenarioList.Add([ordered] @{
            id         = $reportSlug
            status     = $scenarioStatus
            durationMs = 1
            viewports  = @()
            steps      = $stepRecords.ToArray()
        })
        if ($null -ne $extraScenario) {
            [void] $scenarioList.Add($extraScenario)
        }

        $report = [ordered] @{
            acceptanceId   = ('run-' + [guid]::NewGuid().ToString('N'))
            checkpoint     = '5CC'
            sourceCommit   = $sourceCommit
            productHash    = $ProductHash
            productVersion = '0.0.0'
            driverVersion  = $driverVersion
            driverHash     = $reportDriverHash
            fixtureHashes  = @()
            hostOs         = 'candidate'
            dpi            = 96
            theme          = 'unknown'
            viewport       = [ordered] @{ width = 0; height = 0 }
            startedUtc     = $startedUtc
            endedUtc       = [DateTimeOffset]::UtcNow.ToString('o')
            durationMs     = 1
            scenarios      = $scenarioList.ToArray()
            cleanup        = [ordered] @{ status = $cleanupStatus }
            screenshotIndex = @()
            cleanVmStatus  = 'NotRun'
        }
        $reportText = ConvertTo-Json -InputObject $report -Depth 30
        if ($invalidReport) { $reportText = '{ this is not json' }
        $reportTarget = [IO.Path]::Combine($reportDir, 'report.json')
        [IO.File]::WriteAllText($reportTarget, $reportText, [Text.UTF8Encoding]::new($false))
    }

    # What this generator actually produced, recorded where a case can read it back. Nothing is
    # written to the pipeline on purpose: the substitute invoker's return value is captured by the
    # engine, so an extra output object here would become part of the child's result.
    $layout = [ordered] @{
        ChildOutput        = $ChildOutput
        ChildId            = $ChildId
        SummaryPath        = [IO.Path]::Combine($ChildOutput, 'summary.json')
        SummaryWritten     = [bool] $writeSummary
        ReportPath         = [IO.Path]::Combine($reportDir, 'report.json')
        ReportWritten      = [bool] $writeReport
        ReportDir          = $reportDir
        DeclaredReportPath = $declaredReportPath
        ReportFileRelative = $reportFileRelative
        DriverHash         = $reportDriverHash
        ProductHash        = $ProductHash
        Slug               = $reportSlug
        Steps              = $stepRecords.ToArray()
    }
    $script:fakeTreeLayout[$ChildOutput] = $layout
}

# The substitute child invoker. Script scoped state on purpose: the engine calls this scriptblock
# from inside a module function, so anything it needs has to live in the script scope.
$script:invokerBehavior = @{}
$script:invokerLog = New-Object 'System.Collections.Generic.List[object]'
$script:invokerProductHash = ''
$script:invokerDriverHash = ''

$script:invoker = {
    param($Request)

    [void] $script:invokerLog.Add($Request)
    $id = [string] $Request.id
    $options = @{}
    if ($script:invokerBehavior.ContainsKey($id)) { $options = [hashtable] $script:invokerBehavior[$id] }
    $mode = 'pass'
    if ($options.ContainsKey('Mode')) { $mode = [string] $options['Mode'] }

    # The substitute declares itself. Nothing downstream may read this as a real child process.
    $result = [ordered] @{
        started      = $true
        exitCode     = 0
        timedOut     = $false
        killed       = $false
        processId    = 0
        failure      = ''
        cleanupState = 'unconfirmed'
        invokerKind  = 'substitute'
        stdoutPath   = ''
        stderrPath   = ''
        engine       = 'substitute'
        arguments    = @()
    }

    if ($mode -ceq 'notstarted') { $result.started = $false; $result.exitCode = $null; return $result }
    if ($mode -ceq 'unknownexit') { $result.exitCode = $null; return $result }
    if ($mode -ceq 'exceptionafterstart') { $result.processId = 4242; $result.failure = 'simulated failure after spawn'; $result.exitCode = $null; return $result }
    if ($mode -ceq 'timeout') { $result.timedOut = $true; $result.killed = $true; $result.processId = 4243; $result.exitCode = $null; return $result }

    if ($options.ContainsKey('ExitCode')) { $result.exitCode = [int] $options['ExitCode'] }
    if ($mode -ceq 'skipoutput') { return $result }

    Write-FakeChildTree -ChildOutput ([string] $Request.outputDirectory) -ChildId $id `
        -Slug (ConvertTo-UiAcceptanceSlug -Value $id) -ProductHash $script:invokerProductHash `
        -DriverHash $script:invokerDriverHash -Options $options

    # Used by the "a later child must not inherit a stale directory" case: the child creates the
    # NEXT child's output directory behind the engine's back.
    if ($options.ContainsKey('ExtraDir')) {
        $extra = [string] $options['ExtraDir']
        if (-not [string]::IsNullOrWhiteSpace($extra)) {
            $parent = Split-Path -Parent ([string] $Request.outputDirectory)
            [void][IO.Directory]::CreateDirectory([IO.Path]::Combine($parent, $extra))
        }
    }

    return $result
}

<#
.SYNOPSIS
    Runs one candidate entry through the real runner with the substitute child invoker.
#>
function Invoke-CandidateAggregate {
    param(
        [Parameter(Mandatory)][string] $ScenarioFile,
        [Parameter(Mandatory)][string] $AggregateId,
        [Parameter(Mandatory)] $Fixture,
        [hashtable] $Behavior = @{},
        # Lets a case hand in its own child invoker instead of the shared substitute. Used where the
        # case is about the SHAPE or the PRODUCER of the result, not about the usual behaviour map.
        [scriptblock] $Invoker,
        # OMITTING this argument and handing in an empty value are two different requests, and the
        # difference used to be erased by a fallback on IsNullOrWhiteSpace: a case asking for an
        # UNBOUND archive silently got the fixture's real archive back and dispatched everything.
        # Omitted means 'use the fixture's real archive'; supplied and empty means 'the pinned
        # archive is not bound', which is the state the one-line entry patch has not been applied
        # in, and which the entry is supposed to refuse.
        [string] $ArchivePath = '',
        [string] $SourceCommit = 'expected-source-commit',
        [string] $DriverVersion = '0.5.0'
    )

    $script:invokerBehavior = $Behavior
    $script:invokerLog = New-Object 'System.Collections.Generic.List[object]'
    $script:invokerProductHash = [string] $Fixture.ProductHash
    $script:invokerDriverHash = [string] $Fixture.DriverHash
    $script:currentSourceCommit = $SourceCommit
    $script:currentDriverVersion = $DriverVersion

    $archive = [string] $Fixture.Archive
    if ($PSBoundParameters.ContainsKey('ArchivePath')) { $archive = [string] $ArchivePath }

    [void][IO.Directory]::CreateDirectory([string] $Fixture.OwnedRoot)

    $script:currentScenarioPath = [IO.Path]::Combine($script:scenarioRoot, $ScenarioFile)
    $script:currentArguments = @{
        ScenarioId           = (ConvertTo-UiAcceptanceSlug -Value $AggregateId)
        ExecutablePath       = [string] $Fixture.Executable
        ContractPath         = [IO.Path]::Combine([string] $Fixture.Work, 'contract.json')
        OwnedRoot            = [string] $Fixture.OwnedRoot
        RunToken             = $script:runToken
        OutputDirectory      = [string] $Fixture.Output
        SupportPath          = [IO.Path]::Combine([string] $Fixture.Work, 'support.exe')
        WinAppPath           = [IO.Path]::Combine([string] $Fixture.Work, 'winapp.exe')
        WinAppCliArchivePath = $archive
        EntryScriptPath      = [string] $Fixture.Entry
        InvokeChild          = $(if ($null -ne $Invoker) { $Invoker } else { $script:invoker })
        ExpectedSourceCommit = ''
        ResolveSourceCommit  = $script:sourceCommitResolver
        ExpectedDriverVersion = $DriverVersion
    }

    $metadata = @{
        sourceCommit   = $SourceCommit
        productHash    = [string] $Fixture.ProductHash
        productVersion = '0.0.0'
        driverVersion  = $DriverVersion
        driverHash     = [string] $Fixture.DriverHash
        fixtureHashes  = @()
        hostOs         = 'candidate'
        dpi            = 96
        theme          = 'unknown'
        viewport       = @{ width = 0; height = 0 }
    }

    $run = Invoke-UiAcceptanceRun -ScenarioId $AggregateId -Scenario {
        Invoke-UiAcceptanceScenario -Path $script:currentScenarioPath -Arguments $script:currentArguments
    } -Cleanup { } -OwnedRoot ([string] $Fixture.OwnedRoot) -OutputDirectory ([string] $Fixture.Output) `
        -Metadata $metadata -RunToken $script:runToken -SupportPath ''

    $aggregatePath = [IO.Path]::Combine([string] $Fixture.Output, 'aggregate.json')
    $document = $null
    if ([IO.File]::Exists($aggregatePath)) {
        $document = ([IO.File]::ReadAllText($aggregatePath) | ConvertFrom-Json)
    }

    return [ordered] @{
        Run       = $run
        Aggregate = $document
        Log       = $script:invokerLog.ToArray()
        Output    = [string] $Fixture.Output
    }
}

<#
.SYNOPSIS
    Returns one child record from the aggregate the engine actually wrote.

.DESCRIPTION
    A record that is not there is never returned as $null and never as an empty object. When the
    aggregate file itself is missing - the composition never reached its write - or when this child
    has no record inside it, the absence is named, recorded as a failed check and handed back as a
    record whose status says so. Reading a missing record as an empty string is how a case that
    never reached the engine still reported exactly the value it wanted to see, and it is also how
    a real defect in the composition used to be hidden behind a quiet '<>'.
#>
function Get-ChildRecord {
    param([Parameter(Mandatory)] $Result, [Parameter(Mandatory)][string] $ChildId)

    $aggregate = $Result.Aggregate
    if ($null -eq $aggregate) {
        $name = ('child ' + $ChildId + ': the aggregate file was written by the composition and read back')
        [void] $script:checks.Add($name)
        [void] $script:aggregateFailed.Add(($name + ' (the aggregate file is absent, so this case proved nothing about the engine)'))
        return [ordered] @{
            id     = $ChildId
            status = 'AggregateMissing'
            reason = 'aggregate_file_missing'
        }
    }

    foreach ($child in @($aggregate.children)) {
        if ($null -eq $child) { continue }
        if ([string] $child.id -ceq $ChildId) { return $child }
    }

    $name = ('child ' + $ChildId + ': this child has a record in the aggregate')
    [void] $script:checks.Add($name)
    [void] $script:aggregateFailed.Add(($name + ' (this child has no record, so this case proved nothing about this child)'))
    return [ordered] @{
        id     = $ChildId
        status = 'ChildRecordMissing'
        reason = 'child_record_missing'
    }
}

<#
.SYNOPSIS
    Writes one generated child tree into a scratch directory and returns what was written.

.DESCRIPTION
    The point is the order: the tree is written and asserted FIRST, on its own, and only then does
    the case hand the same options to the engine. A case whose input was silently defaulted used to
    pass while proving nothing, because the only thing it checked was the engine's verdict on a
    tree nobody had ever looked at.
#>
function New-PrecheckedChildTree {
    param(
        [Parameter(Mandatory)] $Fixture,
        [Parameter(Mandatory)][string] $ChildId,
        [Parameter(Mandatory)][hashtable] $Options
    )

    $root = [IO.Path]::Combine([string] $Fixture.Work, 'precheck', $ChildId)
    Write-FakeChildTree -ChildOutput $root -ChildId $ChildId `
        -Slug (ConvertTo-UiAcceptanceSlug -Value $ChildId) -ProductHash ([string] $Fixture.ProductHash) `
        -DriverHash ([string] $Fixture.DriverHash) -Options $Options
    return $script:fakeTreeLayout[$root]
}

<#
.SYNOPSIS
    Asserts that the tree on disk is the tree the case meant to hand the engine.

.DESCRIPTION
    This is the input side of a negative case and it is kept separate from the verdict: it proves
    the defect really exists in the generated summary and report - the failing step, the declared
    but absent file, the escaping path, the foreign driver hash - before any claim is made about
    what the engine did with it. Without it, a generator that ignores an option produces a clean
    tree, the engine correctly passes it, and the case reports a Passed that proves nothing.
#>
function Assert-GeneratedChildTree {
    param(
        [Parameter(Mandatory)] $Layout,
        [Parameter(Mandatory)][string] $Tag,
        [string] $ExpectedEntryStatus = 'Passed',
        [string] $ExpectedSummaryStatus = '',
        [string] $ExpectedDeclaredPath = '',
        [string] $ExpectedDriverHash = '',
        [string[]] $ExpectedStepStatuses = @(),
        [string[]] $ExpectedEvidencePaths = @(),
        [string[]] $ExpectedPresentFiles = @(),
        [string[]] $ExpectedAbsentFiles = @()
    )

    $expectedSummary = $ExpectedSummaryStatus
    if ([string]::IsNullOrWhiteSpace($expectedSummary)) { $expectedSummary = $ExpectedEntryStatus }
    $expectedDeclared = $ExpectedDeclaredPath
    if ([string]::IsNullOrWhiteSpace($expectedDeclared)) { $expectedDeclared = [string] $Layout.DeclaredReportPath }

    # Two different questions, so two different sets. The declared set is what the generated report
    # claims, and it is compared for exact equality. The present set is what must really be on disk.
    # They are the same by default so every existing call keeps its disk check, and a case that
    # declares a file which is deliberately absent states the present set itself instead of relying
    # on a set that would then contradict the absent set.
    # The two sets only share a default when the caller did not ask the second question at all. An
    # explicitly empty present set means nothing has to be on disk, which is a different statement
    # from not saying, and treating the two as the same would make a case that declares a deliberately
    # absent file also require that file to exist.
    $expectedPresent = @()
    if ($PSBoundParameters.ContainsKey('ExpectedPresentFiles')) { $expectedPresent = @($ExpectedPresentFiles) }
    else { $expectedPresent = @($ExpectedEvidencePaths) }

    $summaryPath = [string] $Layout.SummaryPath
    Assert-AggregateTrue ([IO.File]::Exists($summaryPath)) ($Tag + ' the generated summary is on disk')
    $summary = $null
    if ([IO.File]::Exists($summaryPath)) {
        $summary = ([IO.File]::ReadAllText($summaryPath) | ConvertFrom-Json)
    }
    Assert-AggregateTrue ($null -ne $summary) ($Tag + ' the generated summary parses')
    if ($null -ne $summary) {
        $entries = @($summary.entries)
        Assert-AggregateEqual 1 $entries.Count ($Tag + ' the generated summary carries exactly one entry')
        if ($entries.Count -eq 1) {
            $entry = $entries[0]
            Assert-AggregateEqual ([string] $Layout.ChildId) ([string] $entry.id) ($Tag + ' the generated entry carries the child id')
            Assert-AggregateEqual $ExpectedEntryStatus ([string] $entry.status) ($Tag + ' the generated entry carries the status the case declared')
            Assert-AggregateEqual $expectedDeclared ([string] $entry.reportPath) ($Tag + ' the generated summary declares the report path the case expects')
        }
        Assert-AggregateEqual $expectedSummary ([string] $summary.status) ($Tag + ' the generated summary status is the one the case declared')
    }

    Assert-AggregateTrue ([bool] $Layout.ReportWritten) ($Tag + ' this case expects a generated report')
    if ([bool] $Layout.ReportWritten) {
        $reportPath = [string] $Layout.ReportPath
        Assert-AggregateTrue ([IO.File]::Exists($reportPath)) ($Tag + ' the generated report is on disk')
        $report = $null
        if ([IO.File]::Exists($reportPath)) {
            $report = ([IO.File]::ReadAllText($reportPath) | ConvertFrom-Json)
        }
        Assert-AggregateTrue ($null -ne $report) ($Tag + ' the generated report parses')
        if ($null -ne $report) {
            if (-not [string]::IsNullOrWhiteSpace($ExpectedDriverHash)) {
                Assert-AggregateEqual $ExpectedDriverHash ([string] $report.driverHash) ($Tag + ' the generated report is bound to the driver hash the case declared')
            }
            $scenarioList = @($report.scenarios)
            Assert-AggregateEqual 1 $scenarioList.Count ($Tag + ' the generated report carries one scenario')
            $diskSteps = @()
            if ($scenarioList.Count -ge 1) { $diskSteps = @($scenarioList[0].steps) }
            Assert-AggregateEqual ([int] $ExpectedStepStatuses.Count) ([int] $diskSteps.Count) ($Tag + ' the generated report carries the number of steps the case declared')

            $flat = New-Object 'System.Collections.Generic.List[string]'
            $index = 0
            foreach ($diskStep in $diskSteps) {
                $index = $index + 1
                $expectedStatus = ''
                if ($index -le [int] $ExpectedStepStatuses.Count) { $expectedStatus = [string] $ExpectedStepStatuses[$index - 1] }
                Assert-AggregateEqual $expectedStatus ([string] $diskStep.status) ($Tag + ' generated step ' + $index + ' carries the status the case declared')
                foreach ($path in @($diskStep.evidencePaths)) { [void] $flat.Add([string] $path) }
            }
            Assert-AggregateEqual ([string] (@($ExpectedEvidencePaths) -join '|')) ([string] ($flat.ToArray() -join '|')) ($Tag + ' the generated report declares exactly the evidence paths the case declared')
        }
    }

    $reportDir = [string] $Layout.ReportDir
    foreach ($relative in @($expectedPresent)) {
        if ([string]::IsNullOrWhiteSpace([string] $relative)) { continue }
        Assert-AggregateTrue ([IO.File]::Exists([IO.Path]::Combine($reportDir, [string] $relative))) ($Tag + ' generated evidence ' + [string] $relative + ' is on disk')
    }
    foreach ($relative in @($ExpectedAbsentFiles)) {
        if ([string]::IsNullOrWhiteSpace([string] $relative)) { continue }
        Assert-AggregateTrue (-not [IO.File]::Exists([IO.Path]::Combine($reportDir, [string] $relative))) ($Tag + ' declared evidence ' + [string] $relative + ' really is absent from disk')
    }
}

# --- cases -------------------------------------------------------------------------------------

function Test-ConflictDispatchesEveryDecision {
    $fixture = New-CandidateFixture -Name 't01-pass'
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture

    Assert-AggregateEqual 'Passed' $result.Run.Status 'T01 aggregate status is Passed'
    Assert-AggregateEqual 3 @($result.Log).Count 'T01 three children were requested'
    Assert-AggregateEqual 3 ([int] $result.Aggregate.childCount) 'T01 aggregate records three children'
    Assert-AggregateEqual 'False' ([string] $result.Aggregate.sharedOwnedRoot) 'T01 aggregate declares no shared owned root'
    Assert-AggregateEqual 'False' ([string] $result.Aggregate.singleWorkflowClaim) 'T01 aggregate makes no single workflow claim'
    Assert-AggregateEqual 'True' ([string] $result.Aggregate.substituteChildren) 'T01 aggregate declares the children were substitutes'

    $ids = @($result.Log | ForEach-Object { [string] $_.id })
    Assert-AggregateEqual 'ConflictKeepBoth' @($ids)[0] 'T01 first child is ConflictKeepBoth'
    Assert-AggregateEqual 'ConflictReplace' @($ids)[1] 'T01 second child is ConflictReplace'
    Assert-AggregateEqual 'ConflictSkip' @($ids)[2] 'T01 third child is ConflictSkip'

    $outputs = @($result.Log | ForEach-Object { [string] $_.outputDirectory })
    Assert-AggregateEqual 3 @($outputs | Select-Object -Unique).Count 'T01 every child output directory is distinct'

    foreach ($request in @($result.Log)) {
        $argv = @($request.argv)
        $scenarioIndex = -1
        for ($i = 0; $i -lt $argv.Count; $i++) { if ([string] $argv[$i] -ceq '-Scenario') { $scenarioIndex = $i } }
        Assert-AggregateTrue ($scenarioIndex -ge 0) ('T01 argv carries -Scenario for ' + [string] $request.id)
        Assert-AggregateEqual ([string] $request.id) ([string] $argv[$scenarioIndex + 1]) ('T01 -Scenario is the single named value for ' + [string] $request.id)
        Assert-AggregateEqual 1 @($argv[$scenarioIndex + 1]).Count ('T01 -Scenario value is not an array for ' + [string] $request.id)
        Assert-AggregateEqual 1 @(@($argv) | Where-Object { $_ -ceq '-Scenario' }).Count ('T01 exactly one -Scenario switch for ' + [string] $request.id)
    }

    foreach ($child in @($result.Aggregate.children)) {
        # The substitute reported these facts; they must survive the read, not be replaced by a
        # default. A started child that the engine recorded as never started is precisely the
        # failure this loop has to keep catching.
        Assert-AggregateEqual 'True' ([string] $child.started) ('T01 child ' + [string] $child.id + ' is still recorded as started')
        Assert-AggregateEqual 0 ([int] $child.exitCode) ('T01 child ' + [string] $child.id + ' carries the real reconstructed exit code')
        Assert-AggregateEqual 'substitute' ([string] $child.invokerKind) ('T01 child ' + [string] $child.id + ' is recorded as a substitute')
        Assert-AggregateEqual 'True' ([string] $child.cleanupConfirmed) ('T01 child ' + [string] $child.id + ' has confirmed cleanup')
        Assert-AggregateEqual 'True' ([string] $child.reportBound) ('T01 child ' + [string] $child.id + ' read the declared report')
        Assert-AggregateEqual 'expected-source-commit' ([string] $child.expectedSourceCommit) ('T01 child ' + [string] $child.id + ' records the expected commit')
        Assert-AggregateEqual '0.5.0' ([string] $child.expectedDriverVersion) ('T01 child ' + [string] $child.id + ' records the expected driver version')
    }
}

function Test-LibraryDispatchesBothSlices {
    $fixture = New-CandidateFixture -Name 't02-pass'
    $result = Invoke-CandidateAggregate -ScenarioFile '03-library.ps1' -AggregateId 'Library' -Fixture $fixture

    Assert-AggregateEqual 'Passed' $result.Run.Status 'T02 library aggregate is Passed'
    $ids = @($result.Log | ForEach-Object { [string] $_.id })
    Assert-AggregateEqual 'BatchMetadataUndo' @($ids)[0] 'T02 first library slice is BatchMetadataUndo'
    Assert-AggregateEqual 'LibraryFilterNaming' @($ids)[1] 'T02 second library slice is LibraryFilterNaming'
    Assert-AggregateEqual 2 ([int] $result.Aggregate.childCount) 'T02 library records two children'
}

function Test-NonZeroExitFails {
    $fixture = New-CandidateFixture -Name 't03-nonzero'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; ExitCode = 1 } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T03 nonzero child exit fails the aggregate'
    Assert-AggregateEqual 'child_exit_nonzero' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T03 the failing child records the real code'
}

function Test-UnknownExitFails {
    $fixture = New-CandidateFixture -Name 't04-unknown'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'unknownexit' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T04 unknown child exit fails the aggregate'
    # One name for one fact. 'child_exit_unknown' was the revision 1 code for a null exit; the
    # engine has emitted 'child_exit_unconfirmed' for it since revision 2 (a child that started but
    # whose exit was never collected is not the same fact as zero), and this expectation was simply
    # never updated. Both cases now assert the same code, and neither guesses a zero.
    Assert-AggregateEqual 'child_exit_unconfirmed' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T04 unknown exit is recorded, not guessed'
}

function Test-NotStartedFails {
    $fixture = New-CandidateFixture -Name 't05-notstarted'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'notstarted' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T05 a child that never started fails the aggregate'
    Assert-AggregateEqual 'child_process_not_started' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T05 not-started is recorded'
}

function Test-TimeoutFailsAndLeavesCleanupUnconfirmed {
    $fixture = New-CandidateFixture -Name 't06-timeout'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'timeout' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    $child = Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth'
    Assert-AggregateEqual 'Failed' $result.Run.Status 'T06 a timed out child fails the aggregate'
    Assert-AggregateEqual 'child_exit_timeout' $child.reason 'T06 timeout is recorded'
    Assert-AggregateEqual 'True' ([string] $child.started) 'T06 the child is still recorded as started'
    Assert-AggregateEqual 'True' ([string] $child.killed) 'T06 the kill is recorded'
    Assert-AggregateEqual 'False' ([string] $child.cleanupConfirmed) 'T06 cleanup stays unconfirmed after a kill'
}

function Test-MissingSummaryFails {
    $fixture = New-CandidateFixture -Name 't07-nosummary'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; WriteSummary = $false } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T07 a child without a summary fails the aggregate'
    Assert-AggregateEqual 'child_summary_missing' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T07 missing summary is recorded'
}

function Test-InvalidSummaryFails {
    $fixture = New-CandidateFixture -Name 't08-badsummary'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; InvalidSummary = $true } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T08 an unreadable summary fails the aggregate'
    Assert-AggregateEqual 'child_summary_invalid' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T08 invalid summary is recorded'
}

function Test-WrongSummaryIdentityFails {
    $fixture = New-CandidateFixture -Name 't09-wrongid'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; SummaryEntryId = 'ConflictSkip' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T09 a child that ran another scenario fails the aggregate'
    Assert-AggregateEqual 'child_identity_mismatch' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T09 identity mismatch is recorded'
}

function Test-WrongReportSlugFails {
    $fixture = New-CandidateFixture -Name 't10-wrongslug'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; ReportSlug = 'conflict-skip' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T10 a report that belongs to another scenario fails the aggregate'
    Assert-AggregateEqual 'child_report_identity_mismatch' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T10 report identity mismatch is recorded'
}

function Test-MissingReportFails {
    $fixture = New-CandidateFixture -Name 't11-noreport'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; WriteReport = $false } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T11 a declared report that is absent fails the aggregate'
    Assert-AggregateEqual 'child_report_missing' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T11 missing report is recorded'
}

function Test-InvalidReportFails {
    $fixture = New-CandidateFixture -Name 't12-badreport'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; InvalidReport = $true } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T12 an unreadable report fails the aggregate'
    Assert-AggregateEqual 'child_report_invalid' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T12 invalid report is recorded'
}

function Test-BlockedChildIsAdopted {
    $fixture = New-CandidateFixture -Name 't13-blocked'
    $behavior = @{ ConflictReplace = @{ Mode = 'pass'; EntryStatus = 'Blocked'; ExitCode = 1 } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Blocked' $result.Run.Status 'T13 a blocked child makes the aggregate Blocked, not Passed'
    Assert-AggregateEqual 'Blocked' (Get-ChildRecord -Result $result -ChildId 'ConflictReplace').status 'T13 the blocked child is recorded as Blocked'
}

function Test-FailedChildIsAdopted {
    $fixture = New-CandidateFixture -Name 't14-failed'
    $behavior = @{ ConflictReplace = @{ Mode = 'pass'; EntryStatus = 'Failed'; ExitCode = 1 } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T14 a failed child fails the aggregate'
    Assert-AggregateEqual 'Failed' (Get-ChildRecord -Result $result -ChildId 'ConflictReplace').status 'T14 the failed child is recorded'
}

function Test-CleanupFailureFails {
    $fixture = New-CandidateFixture -Name 't15-cleanup'
    $behavior = @{ ConflictSkip = @{ Mode = 'pass'; CleanupStatus = 'Failed' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T15 a child whose cleanup failed fails the aggregate'
    Assert-AggregateEqual 'child_cleanup_failed' (Get-ChildRecord -Result $result -ChildId 'ConflictSkip').reason 'T15 cleanup failure is recorded'
}

function Test-MissingEvidenceFails {
    $fixture = New-CandidateFixture -Name 't16-noevidence'
    $options = @{ Mode = 'pass'; Evidence = @() }

    # Input first: the generated report really declares no evidence at all. An empty declaration is
    # not the same as an absent one - the generator must not refill it with a default file, or this
    # case is a control again.
    $layout = New-PrecheckedChildTree -Fixture $fixture -ChildId 'ConflictSkip' -Options $options
    Assert-GeneratedChildTree -Layout $layout -Tag 'T16 input:' -ExpectedStepStatuses @('Passed') -ExpectedEvidencePaths @()

    $behavior = @{ ConflictSkip = $options }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T16 a child with no business evidence fails the aggregate'
    Assert-AggregateEqual 'child_evidence_missing' (Get-ChildRecord -Result $result -ChildId 'ConflictSkip').reason 'T16 missing evidence is recorded'
}

function Test-DeclaredEvidenceFileAbsentFails {
    $fixture = New-CandidateFixture -Name 't17-evidencefile'
    $options = @{ Mode = 'pass'; Evidence = @('evidence/tree.json'); MissingEvidenceFiles = @('evidence/tree.json') }

    # Input first: the report declares the file and the file is really gone from disk, so what the
    # engine is being asked to judge is a declared-but-absent artifact and nothing else.
    $layout = New-PrecheckedChildTree -Fixture $fixture -ChildId 'ConflictSkip' -Options $options
    Assert-GeneratedChildTree -Layout $layout -Tag 'T17 input:' -ExpectedStepStatuses @('Passed') `
        -ExpectedEvidencePaths @('evidence/tree.json') -ExpectedPresentFiles @() -ExpectedAbsentFiles @('evidence/tree.json')

    $behavior = @{ ConflictSkip = $options }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T17 evidence that is declared but absent fails the aggregate'
    Assert-AggregateEqual 'child_evidence_file_missing' (Get-ChildRecord -Result $result -ChildId 'ConflictSkip').reason 'T17 absent evidence file is recorded'
}

function Test-StaleReportFails {
    $fixture = New-CandidateFixture -Name 't18-stale'
    $stale = ([DateTimeOffset]::UtcNow.AddDays(-1)).ToString('o')
    $behavior = @{ ConflictSkip = @{ Mode = 'pass'; StartedUtc = $stale } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T18 a report from before this run fails the aggregate'
    Assert-AggregateEqual 'child_report_stale' (Get-ChildRecord -Result $result -ChildId 'ConflictSkip').reason 'T18 stale report is recorded'
}

function Test-DriverBindingMismatchFails {
    $fixture = New-CandidateFixture -Name 't19-binding'
    $options = @{ Mode = 'pass'; DriverHash = '0000' }

    # Input first, and this is the whole point of the case: the wrong hash the case asked for has to
    # be the hash that is really written into the report. Until it was, the report carried the
    # correct hash, the engine correctly found no mismatch, and the case passed on a control.
    $layout = New-PrecheckedChildTree -Fixture $fixture -ChildId 'ConflictSkip' -Options $options
    Assert-GeneratedChildTree -Layout $layout -Tag 'T19 input:' -ExpectedStepStatuses @('Passed') `
        -ExpectedEvidencePaths @('evidence.json') -ExpectedDriverHash '0000'

    $behavior = @{ ConflictSkip = $options }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T19 a child bound to another driver fails the aggregate'
    Assert-AggregateEqual 'child_driver_binding_mismatch' (Get-ChildRecord -Result $result -ChildId 'ConflictSkip').reason 'T19 binding mismatch is recorded'
}

function Test-PreexistingChildOutputFails {
    $fixture = New-CandidateFixture -Name 't20-preexisting'
    # The first child creates the second child's output directory behind the engine's back, so the
    # engine meets an output that already exists instead of reusing it.
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; ExtraDir = '02-ConflictReplace' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T20 a child output that already exists fails the aggregate'
    Assert-AggregateEqual 'child_output_preexisting' (Get-ChildRecord -Result $result -ChildId 'ConflictReplace').reason 'T20 pre-existing output is recorded'
}

function Test-LaterSuccessDoesNotOverwriteFailure {
    $fixture = New-CandidateFixture -Name 't21-nooverride'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; ExitCode = 1 } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T21 a later pass does not clear an earlier failure'
    Assert-AggregateEqual 'Failed' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').status 'T21 the first failure survives two later passes'
    Assert-AggregateEqual 'Passed' (Get-ChildRecord -Result $result -ChildId 'ConflictReplace').status 'T21 the second child still reports its own Passed'
    Assert-AggregateEqual 'Passed' (Get-ChildRecord -Result $result -ChildId 'ConflictSkip').status 'T21 the third child still reports its own Passed'
    Assert-AggregateEqual 3 @($result.Log).Count 'T21 every child was still dispatched'
}

function Test-UnboundArchiveBlocksWithNotRunChildren {
    $fixture = New-CandidateFixture -Name 't22-unbound'
    # An EXPLICIT empty archive path, not an omitted one: this is the state before the one-line
    # entry patch is applied, and it has to reach the entry's own prerequisite chain unchanged.
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -ArchivePath ''

    Assert-AggregateEqual 'Blocked' $result.Run.Status 'T22 an unbound archive blocks the aggregate'
    Assert-AggregateEqual 'Blocked' ([string] $result.Aggregate.status) 'T22 the composition record says Blocked, not Passed'
    Assert-AggregateEqual 0 @($result.Log).Count 'T22 nothing was dispatched'
    Assert-AggregateEqual 3 ([int] $result.Aggregate.childCount) 'T22 every child is still recorded'
    foreach ($child in @($result.Aggregate.children)) {
        Assert-AggregateEqual 'NotRun' $child.status ('T22 child ' + [string] $child.id + ' is NotRun')
        Assert-AggregateEqual 'child_not_executed' $child.reason ('T22 child ' + [string] $child.id + ' says it was not executed')
    }
}

function Test-EntryArgumentBindingDeliversArchivePath {
    $fixture = New-CandidateFixture -Name 't23-binding-args'
    $scenarioPath = [IO.Path]::Combine($script:scenarioRoot, '02-conflict.ps1')
    $libraryPath = [IO.Path]::Combine($script:scenarioRoot, '03-library.ps1')
    $existingPath = [IO.Path]::Combine($script:scenarioRoot, '20-conflict-keep-both.ps1')

    $candidates = @{
        ScenarioId           = 'conflict'
        ExecutablePath       = [string] $fixture.Executable
        ContractPath         = [IO.Path]::Combine([string] $fixture.Work, 'contract.json')
        OwnedRoot            = [string] $fixture.OwnedRoot
        RunToken             = $script:runToken
        OutputDirectory      = [IO.Path]::Combine([string] $fixture.Work, 'never-created')
        SupportPath          = [IO.Path]::Combine([string] $fixture.Work, 'support.exe')
        WinAppPath           = [IO.Path]::Combine([string] $fixture.Work, 'winapp.exe')
        WinAppCliArchivePath = [string] $fixture.Archive
    }

    # Behavioural, not a parse: the shipped filter has to hand the key to the candidate entries.
    $filtered02 = New-UiAcceptanceEntryArguments -Path $scenarioPath -Candidates $candidates
    Assert-AggregateTrue ($filtered02.ContainsKey('WinAppCliArchivePath')) 'T23 the patched argument reaches 02-conflict.ps1'
    Assert-AggregateEqual ([string] $fixture.Archive) ([string] $filtered02['WinAppCliArchivePath']) 'T23 02-conflict.ps1 receives the real archive path'

    $filtered03 = New-UiAcceptanceEntryArguments -Path $libraryPath -Candidates $candidates
    Assert-AggregateTrue ($filtered03.ContainsKey('WinAppCliArchivePath')) 'T23 the patched argument reaches 03-library.ps1'

    # The declaration has to be a real parameter of the callee, not something the patch invents.
    $declared = @(Get-UiAcceptanceEntryParameter -Path $scenarioPath)
    Assert-AggregateTrue ($declared -ccontains 'WinAppCliArchivePath') 'T23 02-conflict.ps1 declares WinAppCliArchivePath'

    # And the patch must stay invisible to every scenario that does not ask for it.
    $filtered20 = New-UiAcceptanceEntryArguments -Path $existingPath -Candidates $candidates
    Assert-AggregateTrue (-not $filtered20.ContainsKey('WinAppCliArchivePath')) 'T23 an existing scenario is unaffected by the patch'
}

function Test-ChildRequestsAreSingleValuedAndNamed {
    $fixture = New-CandidateFixture -Name 't24-argv'
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture

    foreach ($request in @($result.Log)) {
        $argv = @($request.argv)
        # The real shape is twelve entries: three engine switches, -File with the entry, three
        # named path parameters, and one -Scenario with one value.
        Assert-AggregateEqual 12 $argv.Count ('T24 argv has twelve entries for ' + [string] $request.id)
        Assert-AggregateEqual '-NoProfile' ([string] $argv[0]) ('T24 argv[0] for ' + [string] $request.id)
        Assert-AggregateEqual '-NonInteractive' ([string] $argv[1]) ('T24 argv[1] for ' + [string] $request.id)
        Assert-AggregateEqual '-File' ([string] $argv[2]) ('T24 the child is started with -File for ' + [string] $request.id)
        Assert-AggregateEqual ([string] $fixture.Entry) ([string] $argv[3]) ('T24 the child targets the public entry for ' + [string] $request.id)
        Assert-AggregateEqual '-ProductDirectory' ([string] $argv[4]) ('T24 argv[4] for ' + [string] $request.id)
        Assert-AggregateTrue (-not [string]::IsNullOrWhiteSpace([string] $argv[5])) ('T24 product directory is populated for ' + [string] $request.id)
        Assert-AggregateEqual '-WinAppCliArchivePath' ([string] $argv[6]) ('T24 argv[6] for ' + [string] $request.id)
        Assert-AggregateEqual ([IO.Path]::GetFullPath([string] $fixture.Archive)) ([string] $argv[7]) ('T24 archive path is populated for ' + [string] $request.id)
        Assert-AggregateEqual '-OutputDirectory' ([string] $argv[8]) ('T24 argv[8] for ' + [string] $request.id)
        Assert-AggregateEqual ([string] $request.outputDirectory) ([string] $argv[9]) ('T24 child output directory for ' + [string] $request.id)
        Assert-AggregateEqual '-Scenario' ([string] $argv[10]) ('T24 -Scenario is at index 10 for ' + [string] $request.id)
        Assert-AggregateEqual ([string] $request.id) ([string] $argv[11]) ('T24 the scenario value is at index 11 for ' + [string] $request.id)
        Assert-AggregateTrue (-not ([string] $argv[11]).Contains(',')) ('T24 the scenario value is not a comma list for ' + [string] $request.id)
        Assert-AggregateTrue (-not ([string] $argv[11]).Contains(' ')) ('T24 the scenario value is not a space list for ' + [string] $request.id)
        Assert-AggregateEqual 1 @(@($argv) | Where-Object { $_ -ceq '-Scenario' }).Count ('T24 exactly one -Scenario switch for ' + [string] $request.id)
    }
}

function Test-SummaryStatusContradictionFails {
    $fixture = New-CandidateFixture -Name 't25-summarycontradiction'
    # entry says Passed, the summary top level says Failed: a Passed first entry must not hide it.
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; SummaryStatus = 'Failed' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T25 a summary that contradicts its entry fails the aggregate'
    Assert-AggregateEqual 'child_summary_status_inconsistent' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T25 the contradiction is recorded'
}

function Test-SummaryCompleteContradictionFails {
    $fixture = New-CandidateFixture -Name 't26-complete'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; SummaryComplete = $false } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T26 complete=false with a Passed entry fails the aggregate'
    Assert-AggregateEqual 'child_summary_complete_inconsistent' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T26 the contradiction is recorded'
}

function Test-ReportStatusFailedFails {
    $fixture = New-CandidateFixture -Name 't27-reportstatus'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; EntryStatus = 'Passed'; ReportStatus = 'Failed' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T27 reportStatus Failed fails the aggregate'
    Assert-AggregateEqual 'child_entry_status_contradiction' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T27 the contradiction is recorded'
}

function Test-ExecutionStatusFailedFails {
    $fixture = New-CandidateFixture -Name 't28-execstatus'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; EntryStatus = 'Passed'; ExecutionStatus = 'Failed' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T28 executionStatus Failed fails the aggregate'
    Assert-AggregateEqual 'child_entry_status_contradiction' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T28 the contradiction is recorded'
}

function Test-DeclaredReportPathWrongFails {
    $fixture = New-CandidateFixture -Name 't29-declaredwrong'
    # The report really exists at the default location; only the declared path is wrong. Reading a
    # hardcoded sibling instead of the declared path would pass here, so this is the regression.
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; DeclaredReportPath = 'other/report.json' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T29 a declared report path that resolves nowhere fails the aggregate'
    Assert-AggregateEqual 'child_report_missing' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T29 the declared path is what was resolved'
}

function Test-DeclaredReportPathRootedFails {
    $fixture = New-CandidateFixture -Name 't30-declaredrooted'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; DeclaredReportPath = 'C:\Windows\candidate-report.json' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T30 a rooted declared path fails the aggregate'
    Assert-AggregateEqual 'child_report_path_invalid' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T30 the rooted path is rejected'
}

function Test-DeclaredReportPathEscapesFails {
    $fixture = New-CandidateFixture -Name 't31-declaredescape'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; DeclaredReportPath = '../outside/report.json' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T31 a declared path that walks upwards fails the aggregate'
    Assert-AggregateEqual 'child_report_path_invalid' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T31 the escaping path is rejected'
}

function Test-ExtraFailingScenarioFails {
    $fixture = New-CandidateFixture -Name 't32-extrascenario'
    $extra = [ordered] @{ id = 'conflict-skip'; status = 'Failed'; durationMs = 1; viewports = @(); steps = @() }
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; ExtraScenario = $extra } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T32 an extra failing scenario fails the aggregate'
    Assert-AggregateEqual 'child_report_scenario_count' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T32 the extra scenario is not hidden by the first'
    Assert-AggregateEqual 2 ([int] (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').scenarioCount) 'T32 the scenario count is recorded'
}

function Test-LaterStepFailedFails {
    $fixture = New-CandidateFixture -Name 't33-laterstep'
    $steps = @(
        @{ Status = 'Passed'; Evidence = @('evidence/one.json') }
        @{ Status = 'Failed'; Evidence = @('evidence/two.json') }
    )
    $options = @{ Mode = 'pass'; Steps = $steps }

    # Input first: the second step really is written into the report as Failed. Reading the steps as
    # if every one of them were an object made both steps Passed with the default evidence file,
    # which is why this case used to be a control.
    $layout = New-PrecheckedChildTree -Fixture $fixture -ChildId 'ConflictKeepBoth' -Options $options
    Assert-GeneratedChildTree -Layout $layout -Tag 'T33 input:' -ExpectedStepStatuses @('Passed', 'Failed') `
        -ExpectedEvidencePaths @('evidence/one.json', 'evidence/two.json')

    $behavior = @{ ConflictKeepBoth = $options }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T33 a later failing step fails the aggregate'
    Assert-AggregateEqual 'child_step_failed:2:Failed' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T33 the later step is named'
}

function Test-LaterStepEvidenceMissingFails {
    $fixture = New-CandidateFixture -Name 't34-laterevidence'
    $steps = @(
        @{ Status = 'Passed'; Evidence = @('evidence/one.json') }
        @{ Status = 'Passed'; Evidence = @('evidence/two.json') }
    )
    $options = @{ Mode = 'pass'; Steps = $steps; MissingEvidenceFiles = @('evidence/two.json') }

    # Input first: both steps are declared Passed and the second one's evidence file is really gone,
    # so the only defect the engine can be judging is the missing file of a LATER step.
    $layout = New-PrecheckedChildTree -Fixture $fixture -ChildId 'ConflictKeepBoth' -Options $options
    Assert-GeneratedChildTree -Layout $layout -Tag 'T34 input:' -ExpectedStepStatuses @('Passed', 'Passed') `
        -ExpectedEvidencePaths @('evidence/one.json', 'evidence/two.json') -ExpectedPresentFiles @('evidence/one.json') -ExpectedAbsentFiles @('evidence/two.json')

    $behavior = @{ ConflictKeepBoth = $options }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T34 a later step with missing evidence fails the aggregate'
    Assert-AggregateEqual 'child_evidence_file_missing' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T34 the missing later evidence is recorded'
}

function Test-EvidencePathEscapesFails {
    $fixture = New-CandidateFixture -Name 't35-evidenceescape'
    $steps = @(@{ Status = 'Passed'; Evidence = @('../outside/evidence.json') })
    $options = @{ Mode = 'pass'; Steps = $steps }

    # Input first: the escaping path is really the declared path in the generated report. The file
    # is written where such a path points, so the engine is being asked about the PATH, not about a
    # file that happens to be missing.
    $layout = New-PrecheckedChildTree -Fixture $fixture -ChildId 'ConflictKeepBoth' -Options $options
    Assert-GeneratedChildTree -Layout $layout -Tag 'T35 input:' -ExpectedStepStatuses @('Passed') `
        -ExpectedEvidencePaths @('../outside/evidence.json')

    $behavior = @{ ConflictKeepBoth = $options }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T35 an evidence path that escapes the report fails the aggregate'
    Assert-AggregateEqual 'child_evidence_path_invalid' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T35 the escaping evidence path is rejected'
}

function Test-WrongSourceCommitFails {
    $fixture = New-CandidateFixture -Name 't36-wrongcommit'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; SourceCommit = 'another-commit' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T36 a child from another commit fails the aggregate'
    Assert-AggregateEqual 'child_source_commit_mismatch' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T36 the commit mismatch is recorded'
}

function Test-WrongDriverVersionFails {
    $fixture = New-CandidateFixture -Name 't37-wrongversion'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'pass'; DriverVersion = '0.4.0' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T37 a child bound to another driver version fails the aggregate'
    Assert-AggregateEqual 'child_driver_version_mismatch' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T37 the version mismatch is recorded'
}

function Test-UnverifiableSourceCommitFails {
    $fixture = New-CandidateFixture -Name 't38-nocommit'
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -SourceCommit ''

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T38 an unestablished source commit fails the aggregate'
    Assert-AggregateEqual 'child_source_commit_unverifiable' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T38 the unverifiable commit is recorded, not assumed'
}

function Test-UnverifiableDriverVersionFails {
    $fixture = New-CandidateFixture -Name 't39-noversion'
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -DriverVersion ''

    Assert-AggregateEqual 'Failed' $result.Run.Status 'T39 an unestablished driver version fails the aggregate'
    Assert-AggregateEqual 'child_driver_version_unverifiable' (Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth').reason 'T39 the unverifiable version is recorded, not assumed'
}

function Test-ExceptionAfterStartIsNotNeverStarted {
    $fixture = New-CandidateFixture -Name 't40-afterstart'
    $behavior = @{ ConflictKeepBoth = @{ Mode = 'exceptionafterstart' } }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    $child = Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth'
    Assert-AggregateEqual 'Failed' $result.Run.Status 'T40 a spawn followed by a failure fails the aggregate'
    Assert-AggregateEqual 'child_exit_unconfirmed' $child.reason 'T40 the code says unconfirmed, not never-started'
    Assert-AggregateEqual 'True' ([string] $child.started) 'T40 the child is still recorded as started'
}

function Test-RealisticMultiStepControlPasses {
    $fixture = New-CandidateFixture -Name 't41-control'
    $steps = @(
        @{ Status = 'Passed'; Evidence = @('evidence/tree.json', 'evidence/row.json') }
        @{ Status = 'Passed'; Evidence = @('evidence/conflict.json') }
        @{ Status = 'Passed'; Evidence = @('viewport-1280x800.png', 'evidence/undo.json') }
    )
    $options = @{ Mode = 'pass'; Steps = $steps }

    # Input first: three real steps and five real business files, asserted on disk before the engine
    # sees them. 'Five' is the count the case is entitled to expect only because the generator now
    # really writes all five; before, every step declared the single default file and the count was
    # three.
    $layout = New-PrecheckedChildTree -Fixture $fixture -ChildId 'ConflictKeepBoth' -Options $options
    Assert-GeneratedChildTree -Layout $layout -Tag 'T41 input:' -ExpectedStepStatuses @('Passed', 'Passed', 'Passed') `
        -ExpectedEvidencePaths @('evidence/tree.json', 'evidence/row.json', 'evidence/conflict.json', 'viewport-1280x800.png', 'evidence/undo.json')

    $behavior = @{
        ConflictKeepBoth = $options
        ConflictReplace  = $options
        ConflictSkip     = $options
    }
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Behavior $behavior

    Assert-AggregateEqual 'Passed' $result.Run.Status 'T41 a realistic multi-step child passes'
    $child = Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth'
    Assert-AggregateEqual 3 ([int] $child.stepCount) 'T41 all three steps were seen'
    Assert-AggregateEqual 5 ([int] $child.evidenceCount) 'T41 all five declared evidence files were counted'
    Assert-AggregateEqual 'True' ([string] $child.cleanupConfirmed) 'T41 cleanup is confirmed'
    # The declared path is the FROZEN public entry's own rule, not a slug guess. The entry writes
    # its index entry as reportPath = <requested scenario name> + '/report.json' and puts the file
    # in <output>/<requested scenario name> (baseline blob run-stage5cc-ui-acceptance.ps1, the
    # Write-Stage5CCIndexEntry call and the $runOutput line above it). A child dispatched with
    # '-Scenario ConflictKeepBoth' therefore declares ConflictKeepBoth/report.json. Lower-casing it
    # here would assert a path the real child never declares, which is exactly the kind of wording
    # that hides a declared path that does not exist or that walks out of the child's output.
    Assert-AggregateEqual 'ConflictKeepBoth/report.json' ([string] $child.reportDeclaredPath) 'T41 the declared path is the frozen rule for this child'
    Assert-AggregateEqual ([string] $child.reportDeclaredPath) ([string] $child.reportResolvedPath) 'T41 the declared path is the one that was resolved'
    Assert-AggregateTrue ([IO.File]::Exists([IO.Path]::Combine([string] $fixture.Output, '01-ConflictKeepBoth', 'ConflictKeepBoth', 'report.json'))) 'T41 the resolved report file exists inside the child output'
    Assert-AggregateEqual 'True' ([string] $child.reportBound) 'T41 the report was read from the declared path'
}

<#
.SYNOPSIS
    T42: the time normaliser has to agree with itself no matter which runtime handed it the value.

.DESCRIPTION
    The two runtimes do not hand the normaliser the same type, so this case exercises two
    separate pieces of evidence rather than treating them as one:

      Evidence A - the normaliser's own branches, fed a constructed String and a constructed
                   DateTime. This pins the two inputs types agree with each other.
      Evidence B - the real chain: a report field decoded by THIS runtime's ConvertFrom-Json, then
                   handed to the normaliser exactly as the engine hands it over. This is the only
                   part that says anything about JSON parsing feeding the helper; the earlier
                   AST-lifted probe built its DateTime with TryParse, so it never proved that chain.

    The observed runtime type and DateTimeKind are recorded rather than assumed, because they
    differ per runtime and this case must not be written against the assumption that one of them
    is faulty.
#>
function Test-TimeNormalisationAgreesAcrossInputTypes {
    $modulePath = [IO.Path]::Combine($script:moduleRoot, 'TraceFold.UiAcceptance.Aggregate.psm1')
    Import-Module $modulePath -Force -DisableNameChecking

    $expectedText = '2026-10-06T11:22:33.0000000+00:00'
    $base = [datetime]::new(2026, 10, 6, 11, 22, 33, [DateTimeKind]::Utc)
    $milliTicks = $base.AddTicks(4560000).Ticks
    $tickTicks = $base.AddTicks(1234567).Ticks

    # ---- Evidence A: the two constructed input types must agree -----------------------------
    $fromText = ConvertTo-UiAggregateDateTimeOffset -Value '2026-10-06T11:22:33Z'
    $fromUtc = ConvertTo-UiAggregateDateTimeOffset -Value $base
    Assert-AggregateEqual $expectedText $fromText.ToString('o') 'T42 constructed String input resolves to 11:22:33Z'
    Assert-AggregateEqual $expectedText $fromUtc.ToString('o') 'T42 constructed DateTime Kind=Utc input agrees with the String branch'

    $localStamp = $base.ToLocalTime()
    $fromLocal = ConvertTo-UiAggregateDateTimeOffset -Value $localStamp
    Assert-AggregateEqual $expectedText $fromLocal.ToUniversalTime().ToString('o') 'T42 constructed DateTime Kind=Local keeps its instant whatever the process zone is'

    $offsetText = ConvertTo-UiAggregateDateTimeOffset -Value '2026-10-06T19:22:33+08:00'
    Assert-AggregateEqual $expectedText $offsetText.ToString('o') 'T42 constructed +08:00 text resolves to 11:22:33Z'

    # The deliberate divergence: a zone-less stamp is read as UTC, not as the process local zone.
    $unzonedText = ConvertTo-UiAggregateDateTimeOffset -Value '2026-10-06T11:22:33'
    $unzonedDate = ConvertTo-UiAggregateDateTimeOffset -Value ([datetime]::new(2026, 10, 6, 11, 22, 33, [DateTimeKind]::Unspecified))
    Assert-AggregateEqual $expectedText $unzonedText.ToString('o') 'T42 constructed zone-less text is read as UTC'
    Assert-AggregateEqual $expectedText $unzonedDate.ToString('o') 'T42 constructed zone-less DateTime is read as UTC, not as local'
    # DateTimeOffset.Offset is a TimeSpan, so the zero-offset criterion has to be expressed as a
    # TimeSpan. Asking for the string '+00:00' would be asserting against the wrong type.
    Assert-AggregateEqual 'True' ([string] ([timespan]::Zero -eq $unzonedText.Offset)) 'T42 the zone-less stamp carries a zero TimeSpan offset'

    # ---- Evidence A2: an ALREADY TYPED DateTimeOffset ----------------------------------------
    # This is the branch that used to be handed straight back, so it could carry any offset while
    # the contract above claims every branch returns UTC: the comment and the code disagreed, and
    # nothing exercised a typed value with a non-zero offset, so nothing noticed. Same instant, zero
    # offset, both directions asserted - the offset is gone and the tick has not moved - plus a
    # sub-second typed value so the precision claim covers this branch too.
    $typedPlus = [DateTimeOffset]::new(2026, 10, 6, 13, 22, 33, [TimeSpan]::FromHours(2))
    $typedPlusNormalised = ConvertTo-UiAggregateDateTimeOffset -Value $typedPlus
    Assert-AggregateEqual 'True' ([string] ([timespan]::Zero -eq $typedPlusNormalised.Offset)) 'T42 a typed +02:00 DateTimeOffset normalises to a zero offset'
    Assert-AggregateEqual $expectedText $typedPlusNormalised.ToString('o') 'T42 a typed +02:00 DateTimeOffset names the same instant written as UTC'
    Assert-AggregateEqual ([string] $typedPlus.UtcDateTime.Ticks) ([string] $typedPlusNormalised.UtcDateTime.Ticks) 'T42 normalising a typed value leaves the instant on the same tick'
    Assert-AggregateEqual 'False' ([string] $typedPlusNormalised.ToString('o').EndsWith('+02:00')) 'T42 a typed value is not handed back with the offset it arrived with'

    $typedMinus = [DateTimeOffset]::new(2026, 10, 6, 9, 22, 33, [TimeSpan]::FromHours(-2))
    Assert-AggregateEqual $expectedText (ConvertTo-UiAggregateDateTimeOffset -Value $typedMinus).ToString('o') 'T42 a typed -02:00 DateTimeOffset names the same instant written as UTC'

    $typedMillis = $typedPlus.AddTicks(4560000)
    Assert-AggregateEqual ([string] ([long] $milliTicks)) ([string] (ConvertTo-UiAggregateDateTimeOffset -Value $typedMillis).UtcDateTime.Ticks) 'T42 a typed millisecond-precision value keeps its sub-second ticks'

    # ---- Evidence B: this runtime's real JSON decode, then straight into the normaliser -------
    [void][IO.Directory]::CreateDirectory($script:workRoot)
    $notes = New-Object System.Collections.Generic.List[string]
    $notes.Add('host=' + $PSVersionTable.PSVersion.ToString())
    $notes.Add('localZoneOffsetMinutes=' + [TimeSpan]::FromTicks(([datetime]::Now - [datetime]::UtcNow).Ticks).TotalMinutes)

    $samples = @(
        @{ Name = 'second';   Iso = '2026-10-06T11:22:33Z';            Ticks = $base.Ticks }
        @{ Name = 'millis';   Iso = '2026-10-06T11:22:33.456Z';        Ticks = $milliTicks }
        @{ Name = 'hundredns'; Iso = '2026-10-06T11:22:33.1234567Z';   Ticks = $tickTicks }
        @{ Name = 'offset';   Iso = '2026-10-06T19:22:33+08:00';       Ticks = $base.Ticks }
        @{ Name = 'nozone';   Iso = '2026-10-06T11:22:33';             Ticks = $base.Ticks }
    )

    foreach ($sample in $samples) {
        $name = [string] $sample.Name
        $iso = [string] $sample.Iso

        $decoded = ('{ "startedUtc": "' + $iso + '" }' | ConvertFrom-Json).startedUtc
        $observedType = $decoded.GetType().FullName
        $observedKind = '<not-a-datetime>'
        if ($decoded -is [datetime]) { $observedKind = $decoded.Kind.ToString() }
        $notes.Add($name + ' | iso=' + $iso + ' | decodedType=' + $observedType + ' | kind=' + $observedKind)

        $normalised = ConvertTo-UiAggregateDateTimeOffset -Value $decoded
        Assert-AggregateEqual 'False' ([string] ($null -eq $normalised)) ('T42 real JSON decode of ' + $name + ' normalises rather than refusing')

        # Tick equality is culture independent and proves the precision survived, not just the second.
        Assert-AggregateEqual ([string] ([long] $sample.Ticks)) ([string] $normalised.UtcDateTime.Ticks) ('T42 real JSON decode of ' + $name + ' lands on the same UTC tick as the constructed input')

        # The contract is a UTC instant AND a UTC representation. On a runtime that decodes an
        # offset-bearing stamp into Kind=Local, keeping the process offset would name the right
        # instant while writing '+01:00' into a field called startedUtc, so the offset must be zero
        # for every sample. This is the criterion that was previously unsatisfied.
        Assert-AggregateEqual 'True' ([string] ([timespan]::Zero -eq $normalised.Offset)) ('T42 real JSON decode of ' + $name + ' carries a zero TimeSpan offset')
        Assert-AggregateEqual 'True' ([string] $normalised.ToString('o').EndsWith('+00:00')) ('T42 real JSON decode of ' + $name + ' is written as UTC, not as a local offset')
    }

    # Normalising the representation must not move the instant. Positive and negative proof, both
    # expressed in ticks, so neither a zone nor a culture can make them pass by accident.
    $localSample = ([datetime]::new(2026, 10, 6, 11, 22, 33, [DateTimeKind]::Utc)).ToLocalTime()
    $normalisedLocal = ConvertTo-UiAggregateDateTimeOffset -Value $localSample
    Assert-AggregateEqual 'True' ([string] ([timespan]::Zero -eq $normalisedLocal.Offset)) 'T42 a Local DateTime normalises to a zero offset even when the process zone is not UTC'
    Assert-AggregateEqual ([string] $base.Ticks) ([string] $normalisedLocal.UtcDateTime.Ticks) 'T42 normalising the representation leaves the instant on the same tick'
    Assert-AggregateEqual ([string] $localSample.ToUniversalTime().Ticks) ([string] $normalisedLocal.UtcDateTime.Ticks) 'T42 the normalised instant equals the explicit universal conversion of the same input'

    [IO.File]::WriteAllLines([IO.Path]::Combine($script:workRoot, 't42-json-decode-observation.txt'), $notes, [Text.UTF8Encoding]::new($false))

    # Unparseable text is a refusal, not a guess.
    $garbage = ConvertTo-UiAggregateDateTimeOffset -Value 'not-a-time'
    Assert-AggregateEqual 'True' ([string] ($null -eq $garbage)) 'T42 unparseable text is refused rather than guessed'
}

<#
.SYNOPSIS
    T43: the node reader must read every shape the pipeline actually produces.

.DESCRIPTION
    The real spawn path returns an ordered dictionary, the report tree arrives from ConvertFrom-Json
    as objects, and a caller may hand over a hashtable. Measured, a dictionary key is NOT reachable
    through PSObject.Properties and an object property must NOT be read through an indexer, because
    the indexer answers with an empty value instead of failing. Before this was handled, a child
    that reported started=true was recorded as started=false and every real run would have been
    labelled child_process_not_started.

    The 'absent' / 'reported value' distinction is the other half: a reported false, zero or null
    must survive so that the reader's caller can tell them from a key that was never supplied.
#>
function Test-NodeReaderCoversEveryRealShape {
    $modulePath = [IO.Path]::Combine($script:moduleRoot, 'TraceFold.UiAcceptance.Aggregate.psm1')
    Import-Module $modulePath -Force -DisableNameChecking

    $ordered = [ordered] @{ started = $true; exitCode = 0; invokerKind = 'substitute'; nothing = $null }
    $hashtable = @{ started = $true; exitCode = 0; invokerKind = 'substitute'; nothing = $null }
    $fromJson = ('{"started":true,"exitCode":0,"invokerKind":"substitute","nothing":null}' | ConvertFrom-Json)

    foreach ($shape in @(
        @{ Name = 'OrderedDictionary'; Node = $ordered }
        @{ Name = 'Hashtable';        Node = $hashtable }
        @{ Name = 'JsonObject';       Node = $fromJson }
    )) {
        $label = [string] $shape.Name
        $node = $shape.Node
        $tag = 'T43 ' + $label + ': '

        $seenStart = $false
        $startValue = Get-UiAggregateNodeValue -Node $node -Name 'started' -Found ([ref] $seenStart)
        Assert-AggregateEqual 'True' ([string] $seenStart) ($tag + 'the started key is reported as present')
        Assert-AggregateEqual 'True' ([string] $startValue) ($tag + 'a reported true survives the read')

        # A reported zero must not be mistaken for 'nothing supplied'.
        $seenExit = $false
        $exitValue = Get-UiAggregateNodeValue -Node $node -Name 'exitCode' -Found ([ref] $seenExit)
        Assert-AggregateEqual 'True' ([string] $seenExit) ($tag + 'the exitCode key is reported as present')
        Assert-AggregateEqual '0' ([string] $exitValue) ($tag + 'a reported zero survives the read')

        Assert-AggregateEqual 'substitute' ([string] (Get-UiAggregateNodeValue -Node $node -Name 'invokerKind')) ($tag + 'the substitute label survives the read')

        # A present null is present, and still reads as null.
        $seenNull = $false
        $nullValue = Get-UiAggregateNodeValue -Node $node -Name 'nothing' -Found ([ref] $seenNull)
        Assert-AggregateEqual 'True' ([string] $seenNull) ($tag + 'a key whose value is null is reported as present')
        Assert-AggregateEqual 'True' ([string] ($null -eq $nullValue)) ($tag + 'a present null still reads as null')

        # A key that was never supplied is absent, and must say so.
        $seenMissing = $true
        $missingValue = Get-UiAggregateNodeValue -Node $node -Name 'neverSupplied' -Found ([ref] $seenMissing)
        Assert-AggregateEqual 'False' ([string] $seenMissing) ($tag + 'an absent key is reported as absent')
        Assert-AggregateEqual 'True' ([string] ($null -eq $missingValue)) ($tag + 'an absent key reads as null')

    }

    # Mandatory + explicit null are two different things, and both halves have to hold:
    #   * an explicit null node is accepted, reports Found=false and returns null, without throwing;
    #   * the parameter is still mandatory, so omitting it is still a binding error.
    # The mandatory half is asserted through the parameter attributes rather than by invoking the
    # function with the argument left out: a missing mandatory argument can prompt on an interactive
    # host, and a serial regression window must not be able to hang on a question.
    $seenNullNode = $true
    $nullNodeValue = Get-UiAggregateNodeValue -Node $null -Name 'started' -Found ([ref] $seenNullNode)
    Assert-AggregateEqual 'False' ([string] $seenNullNode) 'T43 an explicit null node reports the key as absent'
    Assert-AggregateEqual 'True' ([string] ($null -eq $nullNodeValue)) 'T43 an explicit null node returns null without throwing'

    $nodeParameter = (Get-Command Get-UiAggregateNodeValue).Parameters['Node']
    $isMandatory = $false
    $allowsNull = $false
    foreach ($attribute in @($nodeParameter.Attributes)) {
        if ($attribute -is [System.Management.Automation.ParameterAttribute]) {
            if ($attribute.Mandatory) { $isMandatory = $true }
        }
        if ($attribute -is [System.Management.Automation.AllowNullAttribute]) { $allowsNull = $true }
    }
    Assert-AggregateEqual 'True' ([string] $isMandatory) 'T43 the node parameter is still mandatory'
    Assert-AggregateEqual 'True' ([string] $allowsNull) 'T43 the node parameter explicitly allows null'
}

<#
.SYNOPSIS
    T44: the reported process facts must survive the fill and the verdict, in every shape.

.DESCRIPTION
    T43 proves the READER. This case proves the WIRING: the node is handed to the real
    Invoke-UiAggregateComposition, so the real conservative-default fill and the real verdict run
    on it, and what comes out of aggregate.json is what is asserted.

    Per shape (ordered dictionary, hashtable, JSON object):
      * a node with no 'started' key        -> explicit failure, not a swallowed exception
      * an explicit null exitCode           -> child_exit_unconfirmed, NOT an exit of zero
      * reported values present             -> preserved, not overwritten by a default
      * an invoker cleanup state that is not
        one of the two known states         -> child_cleanup_unconfirmed (this is what makes that
                                               branch reachable rather than dead)
#>
function Test-ProcessFactsSurviveTheFillAndJudgementChain {
    $modulePath = [IO.Path]::Combine($script:moduleRoot, 'TraceFold.UiAcceptance.Aggregate.psm1')
    Import-Module $modulePath -Force -DisableNameChecking

    $shapes = @(
        @{
            Name  = 'OrderedDictionary'
            Build = {
                param($values)
                $built = [ordered] @{}
                foreach ($key in @($values.Keys)) { $built[$key] = $values[$key] }
                $built
            }
        }
        @{
            Name  = 'Hashtable'
            Build = {
                param($values)
                $built = @{}
                foreach ($key in @($values.Keys)) { $built[$key] = $values[$key] }
                $built
            }
        }
        @{
            Name  = 'JsonObject'
            Build = {
                param($values)
                $values | ConvertTo-Json | ConvertFrom-Json
            }
        }
    )

    $scenarios = @(
        @{
            Name         = 'noStartedKey'
            Values       = @{ invokerKind = 'substitute'; cleanupState = 'unconfirmed'; processId = 0; timedOut = $false; killed = $false; exitCode = 0 }
            WriteTree    = $false
            ExpectStatus = 'Failed'
            ExpectReason = 'child_process_not_started'
        }
        @{
            Name         = 'explicitNullExit'
            Values       = @{ started = $true; invokerKind = 'substitute'; cleanupState = 'unconfirmed'; processId = 0; timedOut = $false; killed = $false; exitCode = $null }
            WriteTree    = $false
            ExpectStatus = 'Failed'
            ExpectReason = 'child_exit_unconfirmed'
        }
        @{
            Name         = 'unknownCleanupState'
            Values       = @{ started = $true; exitCode = 0; invokerKind = 'substitute'; cleanupState = 'leaked'; processId = 0; timedOut = $false; killed = $false }
            WriteTree    = $true
            ExpectStatus = 'Failed'
            ExpectReason = 'child_cleanup_unconfirmed'
        }
        @{
            Name         = 'reportedValuesPreserved'
            Values       = @{ started = $true; exitCode = 0; invokerKind = 'process'; cleanupState = 'unconfirmed'; processId = 4242; timedOut = $false; killed = $false }
            WriteTree    = $true
            ExpectStatus = 'Passed'
            ExpectReason = ''
        }
    )

    $shapeInvoker = {
        param($Request)

        if ($script:shapeWriteTree) {
            Write-FakeChildTree -ChildOutput ([string] $Request.outputDirectory) -ChildId ([string] $Request.id) `
                -Slug (ConvertTo-UiAcceptanceSlug -Value ([string] $Request.id)) `
                -ProductHash $script:invokerProductHash -DriverHash $script:invokerDriverHash
        }
        & $script:shapeBuild $script:shapeValues
    }

    foreach ($shape in $shapes) {
        foreach ($scenario in $scenarios) {
            $shapeName = [string] $shape.Name
            $scenarioName = [string] $scenario.Name
            $tag = 'T44 ' + $shapeName + '/' + $scenarioName + ': '

            $script:shapeBuild = $shape.Build
            $script:shapeValues = $scenario.Values
            $script:shapeWriteTree = [bool] $scenario.WriteTree

            $fixture = New-CandidateFixture -Name ('t44-' + $shapeName + '-' + $scenarioName)
            $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Invoker $shapeInvoker

            $child = Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth'
            Assert-AggregateEqual 'False' ([string] ($null -eq $child)) ($tag + 'the child is recorded')
            Assert-AggregateEqual ([string] $scenario.ExpectStatus) ([string] $child.status) ($tag + 'status')
            if ([string] $scenario.ExpectReason -cne '') {
                Assert-AggregateEqual ([string] $scenario.ExpectReason) ([string] $child.reason) ($tag + 'the reason names the actual cause')
            }

            if ($scenarioName -ceq 'noStartedKey') {
                # The composition's own fill manufactures an absent process fact for the two
                # dictionary shapes - that is what the fill exists for - and cannot manufacture one
                # for a decoded JSON object. Both still refuse to pass; only the presence bit
                # differs, so it is asserted as what actually happens per shape instead of as one
                # uniform guess. What must never differ is the value: false, not 'started'.
                $expectedStartedPresence = 'True'
                if ($shapeName -ceq 'JsonObject') { $expectedStartedPresence = 'False' }
                Assert-AggregateEqual $expectedStartedPresence ([string] $child.startedReported) ($tag + 'the presence bit says whether started was supplied')
                # The exit was supplied by every shape, so its presence bit is true in all of them and
                # is asserted on its own. Reusing the started expectation here is what let a JSON node
                # that did supply an exit look as if it had supplied nothing.
                Assert-AggregateEqual 'True' ([string] $child.exitCodeReported) ($tag + 'the presence bit says whether the exit was supplied')
                Assert-AggregateEqual 'False' ([string] $child.started) ($tag + 'a child that never reported started is recorded as not started')
                Assert-AggregateEqual '' ([string] $child.exitCode) ($tag + 'an exit that was never reported is recorded as nothing, not as zero')
            }

            if ($scenarioName -ceq 'explicitNullExit') {
                # The opposite distinction: the child DID supply these facts, and supplied them as
                # null. That is present-but-null, which is not the same as absent, and it is not
                # the same as zero either.
                Assert-AggregateEqual 'True' ([string] $child.startedReported) ($tag + 'the child supplied started')
                Assert-AggregateEqual 'True' ([string] $child.exitCodeReported) ($tag + 'an explicitly reported null exit is present, not absent')
                Assert-AggregateEqual 'True' ([string] $child.started) ($tag + 'the reported started survives')
                Assert-AggregateEqual '' ([string] $child.exitCode) ($tag + 'a reported null exit is recorded as null, never as zero')
            }

            if ($scenarioName -ceq 'reportedValuesPreserved') {
                Assert-AggregateEqual 'True' ([string] $child.startedReported) ($tag + 'the child supplied started')
                Assert-AggregateEqual 'True' ([string] $child.exitCodeReported) ($tag + 'the child supplied the exit')
                Assert-AggregateEqual 'True' ([string] $child.started) ($tag + 'a reported true is not replaced by a default')
                Assert-AggregateEqual '0' ([string] $child.exitCode) ($tag + 'a reported zero exit survives')
                Assert-AggregateEqual 'process' ([string] $child.invokerKind) ($tag + 'a reported process label survives')
                Assert-AggregateEqual '4242' ([string] $child.processId) ($tag + 'a reported process id survives')
                Assert-AggregateEqual 'unconfirmed' ([string] $child.cleanupState) ($tag + 'the invoker cleanup state is preserved as a fact')
                Assert-AggregateEqual 'True' ([string] $child.cleanupConfirmed) ($tag + 'cleanup is confirmed by the child proof')
            }
        }
    }
}

<#
.SYNOPSIS
    T45: the DEFAULT child invoker itself must run through the same chain.

.DESCRIPTION
    Every other case injects the substitute, so the default producer was never executed. This case
    runs Invoke-UiAggregateChildProcess for real and hands its ACTUAL return - whatever it is - to
    the same fill and verdict the substitute goes through.

    Only the spawn boundary is replaced: the call that would start a process. No process is
    started, no product is started, and the public entry is not run. Everything after the spawn -
    the started flag, the wait, the exit collection, the timeout kill, the returned facts - is the
    default invoker's own code.

    The default invoker returns an ordered dictionary and labels itself 'process'. That is asserted
    from what it actually returned, not from a hand-written stand-in shaped like it.
#>
function Test-DefaultChildInvokerBodyRunsThroughTheSameChain {
    $modulePath = [IO.Path]::Combine($script:moduleRoot, 'TraceFold.UiAcceptance.Aggregate.psm1')
    Import-Module $modulePath -Force -DisableNameChecking

    $engineName = 'powershell.exe'
    if ($PSVersionTable.PSVersion.Major -ge 6) { $engineName = 'pwsh.exe' }
    $script:defaultEngine = Join-Path $PSHOME $engineName

    $script:spawnArgumentCount = -1
    $script:spawnExit = 0
    $script:spawnOptions = @{}
    $script:spawnChildOutput = ''
    $script:spawnChildId = ''

    $script:fakeSpawn = {
        param($FilePath, $ArgumentList, $RedirectStandardOutput, $RedirectStandardError)

        # Stands in for the process that would run the public entry. It writes the child tree the
        # entry would have written, then answers like a process that already exited.
        $script:spawnArgumentCount = @($ArgumentList).Count
        Write-FakeChildTree -ChildOutput $script:spawnChildOutput -ChildId $script:spawnChildId `
            -Slug (ConvertTo-UiAcceptanceSlug -Value $script:spawnChildId) `
            -ProductHash $script:invokerProductHash -DriverHash $script:invokerDriverHash -Options $script:spawnOptions

        $fakeProcess = New-Object PSObject -Property @{ Id = 4711; ExitCode = $script:spawnExit }
        $fakeProcess | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($milliseconds) return $true } -Force
        $fakeProcess | Add-Member -MemberType ScriptMethod -Name Kill -Value { $script:spawnKilled = $true } -Force
        $fakeProcess
    }

    $defaultInvoker = {
        param($Request)
        $script:spawnChildOutput = [string] $Request.outputDirectory
        $script:spawnChildId = [string] $Request.id
        Invoke-UiAggregateChildProcess -Request $Request -TimeoutSeconds 60 `
            -SpawnProcess $script:fakeSpawn -EnginePath $script:defaultEngine
    }

    # Positive: the default invoker's real return, through the real chain.
    $fixture = New-CandidateFixture -Name 't45-default-invoker'
    $result = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture -Invoker $defaultInvoker

    Assert-AggregateEqual 12 $script:spawnArgumentCount 'T45 the default invoker built the twelve-entry argv'
    Assert-AggregateEqual 'Passed' $result.Run.Status 'T45 the default invoker path produces a passing aggregate'
    $child = Get-ChildRecord -Result $result -ChildId 'ConflictKeepBoth'
    Assert-AggregateEqual 'process' ([string] $child.invokerKind) 'T45 the default invoker labels itself process, not substitute'
    Assert-AggregateEqual 'True' ([string] $child.started) 'T45 the default invoker records the spawn'
    Assert-AggregateEqual '0' ([string] $child.exitCode) 'T45 the default invoker collected the real exit code'
    Assert-AggregateEqual 'unconfirmed' ([string] $child.cleanupState) 'T45 the default invoker keeps cleanup unconfirmed rather than claiming it'
    Assert-AggregateEqual 'False' ([string] $result.Aggregate.substituteChildren) 'T45 a real default-invoker run is not marked as substitutes'

    # Negative: cleanup that is not proven still fails, through the same default path.
    $script:spawnOptions = @{ CleanupStatus = 'NotRun' }
    $fixture2 = New-CandidateFixture -Name 't45-default-invoker-unproven-cleanup'
    $result2 = Invoke-CandidateAggregate -ScenarioFile '02-conflict.ps1' -AggregateId 'Conflict' -Fixture $fixture2 -Invoker $defaultInvoker

    $child2 = Get-ChildRecord -Result $result2 -ChildId 'ConflictKeepBoth'
    Assert-AggregateEqual 'Failed' $result2.Run.Status 'T45 unproven cleanup fails the aggregate on the default path'
    Assert-AggregateEqual 'child_cleanup_failed' ([string] $child2.reason) 'T45 the unproven cleanup is named'
    Assert-AggregateEqual 'False' ([string] $child2.cleanupConfirmed) 'T45 cleanup is not confirmed'
    Assert-AggregateEqual 'process' ([string] $child2.invokerKind) 'T45 the default invoker is still the producer that is recorded'
    $script:spawnOptions = @{}
}

# --- toolkit contract ---------------------------------------------------------------------------

<#
.SYNOPSIS
    Writes the case progress record.

.DESCRIPTION
    Bookkeeping only, and deliberately wrapped: if writing the record fails, that failure is kept
    in its own list and must never be mistaken for, or replace, the error a case produced.
#>
$script:progressWriteErrors = New-Object 'System.Collections.Generic.List[string]'
$script:caseProgress = New-Object 'System.Collections.Generic.List[string]'
# How many of those write errors have already been raised into a case verdict. A write error has to
# fail something, but it must not replace the error a case was already raising: the case that saw it
# re-raises its own business error unchanged, so without this watermark the write error would belong
# to no case at all - not to the one that hit it, and not to the next one, which only counts what it
# added itself. Everything after the watermark is therefore still owed, and the first case that has
# nothing else to report pays it.
$script:progressErrorsReported = 0

function Write-CaseProgress {
    try {
        if (-not [IO.Directory]::Exists($script:workRoot)) { [void][IO.Directory]::CreateDirectory($script:workRoot) }
        $target = [IO.Path]::Combine($script:workRoot, 'case-progress.txt')
        [IO.File]::WriteAllLines($target, $script:caseProgress.ToArray(), [Text.UTF8Encoding]::new($false))
    }
    catch {
        [void] $script:progressWriteErrors.Add([string] $_.Exception.Message)
    }
}

<#
.SYNOPSIS
    Runs one named case and turns what it found into either nothing or a raised error.

.DESCRIPTION
    The standalone script finished by counting the failures and choosing an exit code. A
    dot-sourced case has no exit code: the toolkit decides a case failed by whether its body
    raises. So all three ways a case can be unsuccessful are raised here, and each keeps the reason
    the standalone script would have printed:
      * the case did not finish. The record keeps what the ErrorRecord actually carries and no
        more: type, id, message, position, the stack the runtime had, which phase was running and
        how far the case got. It is deliberately NOT a deep serialisation of the ErrorRecord, its
        InvocationInfo or the session state - a whole error graph is not readable, it can carry
        paths and objects this record must not leak, and rebuilding one would be a second
        implementation of the runtime's own error rendering. The stack is the part that can
        legitimately be missing: an error that a case caught and re-threw inside its own try, or
        one raised inside a module, arrives wrapped with no usable stack of its own. Saying so is
        better than printing an empty line and letting the reader assume the case died before
        doing anything. The original error is then raised again so the toolkit records a failure.
      * the case finished but asserted something false. The harness accumulates instead of
        stopping at the first failure, which is what lets one case report every wrong field at
        once, so the failures this case added are raised together, by name.
      * the progress record could not be written. The standalone script counted those separately
        and exited non-zero for them on purpose: a run with no record of how far it got has no
        proof of anything. It is never folded into the business error either - when a case raises
        its own error, that error goes to the toolkit unchanged and the write failure is owed to
        whichever case reports next, so it cannot be swallowed by arriving second.
#>
function Invoke-AggregateTestCase {
    param([Parameter(Mandatory)][string] $Name)

    $checksBefore = [int] $script:checks.Count
    $failedBefore = [int] $script:aggregateFailed.Count

    [void] $script:caseProgress.Add(('STARTED ' + $Name))
    Write-CaseProgress

    try {
        & $Name
        $newChecks = ([int] $script:checks.Count) - $checksBefore
        $newFailed = ([int] $script:aggregateFailed.Count) - $failedBefore
        [void] $script:caseProgress.Add(('COMPLETED ' + $Name + ' checks=' + $newChecks + ' newFailures=' + $newFailed))
    }
    catch {
        $newChecks = ([int] $script:checks.Count) - $checksBefore
        $newFailed = ([int] $script:aggregateFailed.Count) - $failedBefore
        $stack = [string] $_.ScriptStackTrace
        if ([string]::IsNullOrWhiteSpace($stack)) { $stack = '(no stack on this record: the error was caught and re-thrown inside the case, so the runtime kept none)' }
        $stack = $stack -replace '(\r?\n)+', ' | '
        [void] $script:caseProgress.Add(('INCOMPLETE ' + $Name))
        [void] $script:caseProgress.Add(('  phase=case-body'))
        [void] $script:caseProgress.Add(('  exceptionType=' + [string] $_.Exception.GetType().FullName))
        [void] $script:caseProgress.Add(('  errorFqid=' + [string] $_.FullyQualifiedErrorId))
        [void] $script:caseProgress.Add(('  errorMessage=' + [string] $_.Exception.Message))
        [void] $script:caseProgress.Add(('  at=' + [string] $_.InvocationInfo.ScriptName + ':' + [string] $_.InvocationInfo.ScriptLineNumber))
        [void] $script:caseProgress.Add(('  scriptStackTrace=' + $stack))
        [void] $script:caseProgress.Add(('  checksReached=' + $newChecks + ' newFailures=' + $newFailed))
        Write-CaseProgress
        # Re-raised unchanged. If writing this record failed too, that failure stays owed (the
        # watermark below is deliberately not advanced here): this case's own error is what the
        # toolkit has to record, and the write error is then raised by the first later case that
        # has nothing else to report. A business error arriving first must never swallow it.
        throw
    }

    Write-CaseProgress

    # Two ways a case can be unsuccessful, and this has to cover every one of them. The standalone
    # script collected them and picked an exit code at the end; losing that tail lost the third of
    # the three with it, and a case whose progress record could not be written then passed.
    $newFailed = ([int] $script:aggregateFailed.Count) - $failedBefore
    $reasons = New-Object 'System.Collections.Generic.List[string]'
    if ($newFailed -gt 0) {
        $raised = New-Object 'System.Collections.Generic.List[string]'
        $index = $failedBefore
        while ($index -lt [int] $script:aggregateFailed.Count) {
            [void] $raised.Add([string] $script:aggregateFailed[$index])
            $index = $index + 1
        }
        [void] $reasons.Add(('assertions: ' + ($raised.ToArray() -join ' | ')))
    }

    # Everything after the watermark is still owed. It can have been left here by THIS case (its
    # own two writes above failed) or by an earlier case whose business error took precedence.
    $owedText = ''
    $owedIndex = [int] $script:progressErrorsReported
    while ($owedIndex -lt [int] $script:progressWriteErrors.Count) {
        $owedItem = [string] $script:progressWriteErrors[$owedIndex]
        if ($owedText -cne '') { $owedText = $owedText + ' | ' }
        $owedText = $owedText + $owedItem
        $owedIndex = $owedIndex + 1
    }
    if ($owedText -cne '') {
        [void] $reasons.Add(('the progress record could not be written: ' + $owedText))
    }

    if ($reasons.Count -gt 0) {
        $script:progressErrorsReported = [int] $script:progressWriteErrors.Count
        throw ('aggregate case failed: ' + ($reasons.ToArray() -join ' | '))
    }
}

<#
.SYNOPSIS
    The contract the toolkit calls: an array of @{ Name = <string>; Body = <scriptblock> }.
#>
function Get-TestCases {
    $names = @(
        'Test-ConflictDispatchesEveryDecision',
        'Test-LibraryDispatchesBothSlices',
        'Test-NonZeroExitFails',
        'Test-UnknownExitFails',
        'Test-NotStartedFails',
        'Test-TimeoutFailsAndLeavesCleanupUnconfirmed',
        'Test-MissingSummaryFails',
        'Test-InvalidSummaryFails',
        'Test-WrongSummaryIdentityFails',
        'Test-WrongReportSlugFails',
        'Test-MissingReportFails',
        'Test-InvalidReportFails',
        'Test-BlockedChildIsAdopted',
        'Test-FailedChildIsAdopted',
        'Test-CleanupFailureFails',
        'Test-MissingEvidenceFails',
        'Test-DeclaredEvidenceFileAbsentFails',
        'Test-StaleReportFails',
        'Test-DriverBindingMismatchFails',
        'Test-PreexistingChildOutputFails',
        'Test-LaterSuccessDoesNotOverwriteFailure',
        'Test-UnboundArchiveBlocksWithNotRunChildren',
        'Test-EntryArgumentBindingDeliversArchivePath',
        'Test-ChildRequestsAreSingleValuedAndNamed',
        'Test-SummaryStatusContradictionFails',
        'Test-SummaryCompleteContradictionFails',
        'Test-ReportStatusFailedFails',
        'Test-ExecutionStatusFailedFails',
        'Test-DeclaredReportPathWrongFails',
        'Test-DeclaredReportPathRootedFails',
        'Test-DeclaredReportPathEscapesFails',
        'Test-ExtraFailingScenarioFails',
        'Test-LaterStepFailedFails',
        'Test-LaterStepEvidenceMissingFails',
        'Test-EvidencePathEscapesFails',
        'Test-WrongSourceCommitFails',
        'Test-WrongDriverVersionFails',
        'Test-UnverifiableSourceCommitFails',
        'Test-UnverifiableDriverVersionFails',
        'Test-ExceptionAfterStartIsNotNeverStarted',
        'Test-RealisticMultiStepControlPasses',
        'Test-TimeNormalisationAgreesAcrossInputTypes',
        'Test-NodeReaderCoversEveryRealShape',
        'Test-ProcessFactsSurviveTheFillAndJudgementChain',
        'Test-DefaultChildInvokerBodyRunsThroughTheSameChain'
    )

    $cases = New-Object 'System.Collections.Generic.List[object]'
    foreach ($name in $names) {
        $caseName = [string] $name
        # The body is built from text so the name is fixed inside it. A script block closing over
        # the loop variable would hand every case the same name, and a block carrying a captured
        # copy of the session state would add its checks and its failures to lists that nobody
        # else can see, which would make a failing case look like a passing one.
        $body = [scriptblock]::Create('Invoke-AggregateTestCase -Name ''' + $caseName + '''')
        [void] $cases.Add(@{ Name = $caseName; Body = $body })
    }
    return $cases.ToArray()
}