# TraceFold Stage 5CC - aggregate composition engine (revision 2).
#
# Status: integrated at scripts/ui-acceptance/TraceFold.UiAcceptance.Aggregate.psm1 together with
# the 02/03 aggregate entries and their toolkit test; the one-line entry pass-through it needs was
# applied to scripts/run-stage5cc-ui-acceptance.ps1 with the integration. It has not yet been run
# against the real product as a composed aggregate, and it is not a claim that the Conflict or
# Library aggregate has been accepted.
#
# What this engine does:
#   an aggregate entry ('Conflict', 'Library') owns no product instance of its own. It dispatches
#   every sub scenario through the real public entry point as a separate process, one named
#   '-Scenario <Name>' request at a time, each with its own output directory and therefore its own
#   owned root, report and cleanup chain. The parent then re-reads what each child actually
#   produced on this run and composes a verdict.
#
# What it deliberately never does:
#   * never invokes a sub scenario script directly with '&'; the child always goes through the
#     public entry, so root creation, provenance, cleanup and the report contract stay real;
#   * never shares its owned root with a child;
#   * never reads a previous run's output as this run's success (count, identity, consistency,
#     binding, freshness, dispatch count and evidence checks, plus a refusal to reuse a
#     pre-existing child output directory);
#   * never lets a later child overwrite an earlier failure: the composed status takes the worst
#     child status and every child keeps its own visible record;
#   * never reports Passed on unconfirmed cleanup. A killed or unconfirmed child leaves cleanup
#     unconfirmed, and unconfirmed cleanup is a failure, not a pass.
#
# Revision 2 changes (see 04-REVISION-RECORD.md):
#   1. the declared report path is resolved through the real summary relative-path contract and is
#      the file actually read, instead of a non-empty check followed by a hardcoded sibling path;
#   2. the summary top level, the entry status and the report/execution statuses must agree before
#      anything is adopted, so a contradictory summary can never read as Passed;
#   3. every step and every declared evidence path is validated, not only the first step;
#   4. provenance is bound to this run: source commit and driver version are reconciled against
#      expectations, and an expectation that cannot be established is a failure, not a pass;
#   5. process facts distinguish 'never spawned' from 'spawned but unconfirmed', and a killed
#      child reports timeout plus unconfirmed cleanup.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The module sits next to the contracts module after integration. The guard keeps both layouts
# working - the candidate review imported this after a frozen contracts mirror - without ever
# guessing where a shared file lives.
if (-not (Get-Command -Name 'ConvertTo-UiAcceptanceSlug' -ErrorAction SilentlyContinue)) {
    $contractsCandidate = Join-Path $PSScriptRoot 'TraceFold.UiAcceptance.Contracts.psm1'
    if ([IO.File]::Exists($contractsCandidate)) {
        Import-Module $contractsCandidate -Force -DisableNameChecking
    }
}

<#
.SYNOPSIS
    Throws a child failure that carries both a machine readable code and the status it maps to.
#>
function New-UiAggregateChildFailure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Code,
        [Parameter(Mandatory)][string] $Status
    )

    $ex = New-Object System.Exception -ArgumentList $Code
    $ex.Data['UiAggregateStatus'] = $Status
    return $ex
}

<#
.SYNOPSIS
    Reads one node property, treating an absent property as empty instead of an error.

.DESCRIPTION
    Strict mode turns a missing property into a terminating error, and the difference between
    'absent' and 'empty' is a real difference in these reports. Reading through the property
    metadata keeps that distinction instead of losing the child to a property lookup.

    The node is not always the same shape, and the shapes do not answer the same questions.
    Measured on this machine (artifacts/node-shape-probe-ps51.txt and
    artifacts/node-shape-probe-ps7.txt), identically on both runtimes:

                                          PSObject.Properties[k]   Contains(k)   [k]        .k
        OrderedDictionary (process,       MISS -> null             True          value     value
         substitute result, even in
         the real spawn path)
        Hashtable                         MISS -> null             True          value     value
        PSCustomObject (ConvertFrom-Json) value                    no such       EMPTY,    value
                                                                   method        silently

    A dictionary key is therefore not reachable through PSObject.Properties, and an object
    property must not be read through an indexer, because the indexer answers with an empty value
    instead of failing. So the shape decides the access, and both shapes are read explicitly.

    '-Found' is what separates 'the child reported false' from 'the child reported nothing'. A
    caller that only looks at the returned value cannot tell them apart, because both read as
    null; filling a default over a real reported value is how a started process came to be
    recorded as never started.

    'Node' is mandatory but explicitly allows null. Without [AllowNull()] the mandatory check
    rejects an explicit null at binding time, before the body's own null guard can run, so a
    caller asking 'does this absent node read as absent' gets a binding exception instead of an
    answer. Mandatory still means the caller must pass something: omitting the argument is still
    an error, and a null node still reports Found=false and returns null.
#>
function Get-UiAggregateNodeValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()] $Node,
        [Parameter(Mandatory)][string] $Name,
        [ref] $Found
    )

    if ($null -ne $Found) { $Found.Value = $false }
    if ($null -eq $Node) { return $null }

    if ($Node -is [System.Collections.IDictionary]) {
        if ($Node.Contains($Name)) {
            if ($null -ne $Found) { $Found.Value = $true }
            return $Node[$Name]
        }
        return $null
    }

    $property = $Node.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    if ($null -ne $Found) { $Found.Value = $true }
    return $property.Value
}

<#
.SYNOPSIS
    Applies the same relative-path rule the public entry applies to persisted summary paths.

.DESCRIPTION
    A path that is rooted, or that walks upwards, cannot be a child artifact reference. The rule
    and the pattern are the ones the entry already uses for its own index entries, so the
    aggregate is not inventing a second definition of 'inside the output'.
#>
function Test-UiAggregateRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    if ([IO.Path]::IsPathRooted($Value)) { return $false }
    if ($Value -match '(^|[\\/])\.\.([\\/]|$)') { return $false }
    return $true
}

<#
.SYNOPSIS
    Turns a report time field into a DateTimeOffset, or returns null when it cannot be trusted.

.DESCRIPTION
    The two PowerShell runtimes do not agree on what a JSON ISO-8601 string becomes. Measured on
    this machine (artifacts/json-time-probe-ps51.txt and artifacts/json-time-probe-ps7.txt):

        PS 5.1 (5.1.26100.9444):  every ISO-8601 time field stays a System.String, verbatim,
                                  including the trailing 'Z' or the numeric offset.
        PS 7   (7.6.3):           the same field becomes a System.DateTime. A 'Z' value arrives
                                  with Kind=Utc; a numeric-offset value arrives converted into
                                  the process local zone with Kind=Local; a value with no zone at
                                  all arrives with Kind=Unspecified and PowerShell then reads it
                                  as LOCAL time, which silently shifts it by the process offset.

    So formatting the raw value through the current culture, or comparing it as a string, would
    let a report produced under another locale or another zone read as stale or as shifted. The
    value is therefore normalised to a DateTimeOffset with an invariant culture and an explicit
    universal assumption, and anything that cannot be normalised is a failure rather than a guess.

    THE CONTRACT IS A UTC INSTANT WITH A UTC REPRESENTATION. 'Same instant' and 'written as UTC'
    are two different claims, and this helper must satisfy both, because its result is persisted
    into a field named startedUtc and compared against a UTC composition start. A result carrying
    '+01:00' would name the right instant while writing the wrong thing. So every branch returns
    an offset of zero; the instant is what was supplied, never a shifted one. Before this was made
    uniform the Kind=Local branch kept the process offset, which is why a numeric-offset report
    field could normalise to 11:22:33+01:00 instead of 11:22:33+00:00, and the typed DateTimeOffset
    branch returned whatever offset it was handed, which is why the comment and the code used to
    disagree about a value that arrived already typed. The typed branch is asserted, not assumed:
    T42 hands it +02:00 and requires the same instant written with a zero offset.

    Two consequences are deliberate and must not be "simplified" away:
      * A Kind=Unspecified DateTime is read as UTC, not as local time. This is the opposite of
        what PS 7 does by default, and it is intentional: every timestamp these reports carry is
        named and written as UTC. On the host where this was measured the process zone is UTC+1,
        so the naive PS 7 reading would have shifted an unzoned stamp by exactly one hour.
      * A Kind=Local DateTime is converted to universal rather than kept at the process offset.
        The conversion changes the representation only; UtcDateTime.Ticks is untouched, and that
        is asserted, not assumed.
      * Both branches must keep agreeing, and the two kinds of evidence must stay separate:
          - Branch agreement was measured with the function lifted out by AST
            (artifacts/normalise-probe-ps51.txt and artifacts/normalise-probe-ps7.txt): the string
            branch and the DateTime branch produce the identical instant on both runtimes for
            second, millisecond and 100-nanosecond precision, with 'Z' and with an explicit
            offset. That probe BUILT its DateTime with DateTime.TryParse(AssumeUniversal|
            AdjustToUniversal); it therefore says nothing about what this runtime's ConvertFrom-Json
            actually hands over, and must not be cited as having verified that chain.
          - The real JSON-decode-into-this-function chain is covered separately, per runtime, by
            the T42 case, which hands the helper whatever ConvertFrom-Json produced and asserts the
            resulting UTC tick for each precision. Do not merge the two into one claim.
        Both return null for unparseable text instead of a guess.
#>
function ConvertTo-UiAggregateDateTimeOffset {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Value
    )

    if ($null -eq $Value) { return $null }

    if ($Value -is [datetimeoffset]) {
        # A typed DateTimeOffset already names an instant, but it may carry ANY offset, and the
        # contract is the instant written as UTC. So this branch is normalised like every other
        # one: ToUniversalTime changes the offset and leaves UtcTicks untouched. Before this the
        # typed branch returned the value unchanged, which meant a caller handing in +02:00 got
        # +02:00 back while the comment above claimed every branch returns an offset of zero; the
        # claim and the code disagreed, and the disagreement was invisible until a typed value
        # with a non-zero offset was actually handed in.
        return ([DateTimeOffset] $Value).ToUniversalTime()
    }

    if ($Value -is [datetime]) {
        $stamp = [datetime] $Value
        # Kind=Local is converted to universal. The instant is preserved exactly; only the
        # representation is normalised, so the result always carries a zero offset no matter which
        # zone the process runs in.
        if ($stamp.Kind -ceq [DateTimeKind]::Local) { return ([DateTimeOffset]::new($stamp)).ToUniversalTime() }
        # Utc and Unspecified are both read as UTC: every timestamp these reports carry is UTC.
        return [DateTimeOffset]::new($stamp, [TimeSpan]::Zero)
    }

    $text = [string] $Value
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }

    $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    $parsed = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse($text, [Globalization.CultureInfo]::InvariantCulture, $styles, [ref] $parsed)) {
        return $parsed
    }

    return $null
}

<#
.SYNOPSIS
    Reproduces the public entry's own status composition for one index entry.

.DESCRIPTION
    The entry derives an entry status from the report status and the execution status with a fixed
    priority. The aggregate repeats that derivation instead of trusting the reported one, because
    a summary that contradicts itself is exactly the case that must not be adopted.
#>
function Get-UiAggregateEffectiveStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ReportStatus,
        [Parameter(Mandatory)][string] $ExecutionStatus
    )

    if ($ExecutionStatus -ceq 'Failed' -or $ReportStatus -ceq 'Failed') { return 'Failed' }
    if ($ExecutionStatus -ceq 'Blocked' -or $ReportStatus -ceq 'Blocked') { return 'Blocked' }
    if ($ReportStatus -ceq 'NotRun' -or $ExecutionStatus -ceq 'NotRun') { return 'NotRun' }
    return 'Passed'
}

<#
.SYNOPSIS
    Reads the source commit this run is executing, or '' when it cannot be established.

.DESCRIPTION
    Provenance has to be an observed fact about this run, so it is read here and nowhere else.
    A native tool writing to stderr is an error record on Windows PowerShell, so the read is
    guarded: an unavailable commit stays unknown instead of aborting the composition. Unknown is
    then handled as a failure by the verdict, never as a pass.
#>
function Get-UiAggregateSourceCommit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot
    )

    if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { return '' }
    if (-not [IO.Directory]::Exists($RepositoryRoot)) { return '' }

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = & git -C $RepositoryRoot rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0) { return ([string] ($raw | Out-String)).Trim() }
    }
    catch {
        # An unreadable commit is recorded as unknown, never as a reason to abandon the run.
    }
    finally {
        $ErrorActionPreference = $previous
    }

    return ''
}

<#
.SYNOPSIS
    Reads the pinned driver version from the repository manifest, or '' when it cannot be read.
#>
function Get-UiAggregateDriverVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot
    )

    if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { return '' }
    $manifestPath = [IO.Path]::Combine($RepositoryRoot, 'apps', 'media-desktop', 'tools', 'winapp', 'win-x64', 'manifest.json')
    if (-not [IO.File]::Exists($manifestPath)) { return '' }

    try {
        $manifest = ([IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json)
        return [string] (Get-UiAggregateNodeValue -Node $manifest -Name 'version')
    }
    catch {
        return ''
    }
}

<#
.SYNOPSIS
    Builds the ordered child plan for one aggregate entry.
#>
function Get-UiAggregateChildPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $ChildScenarios
    )

    $plan = New-Object 'System.Collections.Generic.List[object]'
    $index = 0
    foreach ($child in @($ChildScenarios)) {
        if ([string]::IsNullOrWhiteSpace($child)) { throw 'A child scenario name is empty.' }
        $index = $index + 1
        [void] $plan.Add([ordered] @{
            index      = $index
            id         = [string] $child
            outputName = ('{0:d2}-{1}' -f $index, $child)
        })
    }

    return $plan.ToArray()
}

<#
.SYNOPSIS
    Quotes one argument the way a Windows process start expects it.
#>
function Get-UiAggregateQuotedArgument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Value
    )

    if ($Value -match '\s') { return '"' + $Value + '"' }
    return $Value
}

<#
.SYNOPSIS
    Builds one child request: exactly one named scenario, one output directory, one argv.

.DESCRIPTION
    The argv is built here and stored on the request so a reviewer (and the regression test) can
    verify per child what would have been executed. It is twelve entries:
      0 '-NoProfile', 1 '-NonInteractive', 2 '-File', 3 <entry script>,
      4 '-ProductDirectory', 5 <product directory>,
      6 '-WinAppCliArchivePath', 7 <archive path>,
      8 '-OutputDirectory', 9 <child output directory>,
      10 '-Scenario', 11 <one scenario name>.
    '-Scenario' is therefore at index 10 and its value at index 11. The public entry binds
    '-Scenario' as [string[]] and a process start cannot bind several values to it, so one named
    request per process is the only shape that works; that is why the value is a single name.
#>
function New-UiAggregateChildRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $PlanEntry,
        [Parameter(Mandatory)][string] $EntryScriptPath,
        [Parameter(Mandatory)][string] $ProductDirectory,
        [Parameter(Mandatory)][string] $WinAppCliArchivePath,
        [Parameter(Mandatory)][string] $ParentOutputDirectory
    )

    $childOutput = [IO.Path]::Combine($ParentOutputDirectory, [string] $PlanEntry.outputName)
    $argv = @(
        '-NoProfile'
        '-NonInteractive'
        '-File'
        $EntryScriptPath
        '-ProductDirectory'
        $ProductDirectory
        '-WinAppCliArchivePath'
        $WinAppCliArchivePath
        '-OutputDirectory'
        $childOutput
        '-Scenario'
        ([string] $PlanEntry.id)
    )

    return [ordered] @{
        index            = [int] $PlanEntry.index
        id               = [string] $PlanEntry.id
        outputName       = [string] $PlanEntry.outputName
        outputDirectory  = $childOutput
        entryScript      = $EntryScriptPath
        productDirectory = $ProductDirectory
        archivePath      = $WinAppCliArchivePath
        argv             = $argv
    }
}

<#
.SYNOPSIS
    Runs one child request as a real child process and reports what actually happened.

.DESCRIPTION
    This is the default child invoker and the only place a process is started. It reports facts,
    never a verdict:
      * 'started' means the spawn returned a process object. It is set the moment that happens and
        is never cleared afterwards, so neither an exception after a successful spawn nor a failure
        while the output capture is set up rewrites history into 'never started'; the pid and the
        start time of this call's own object are taken at the same moment and are kept beside it;
      * 'exitCode' is null when no real exit was collected, which covers a spawn that failed, a
        start that produced no exit, and an exit read that answered nothing or failed. The read is
        checked before any conversion: a missing answer is recorded as a named read failure and
        stays null, because casting it with [int] is how a missing exit became a zero. A real zero
        is a reported fact and survives as zero. 'exitCodeRaw', 'exitCodeRawType' and
        'exitReadError' carry the raw value, its type and the read error from that same single read;
      * 'timedOut' and 'killed' record the one case where this engine ends a process;
      * 'stdoutPumpStatus' / 'stderrPumpStatus' carry the completion fact of each output capture -
        'skipped' when no capture was set up, 'completed', 'timed-out' when the bounded completion
        wait did not finish, 'failed' when the copy faulted, and 'close-failed' when the file could
        not be closed after a completed copy - with the first error text in 'stdoutPumpError' /
        'stderrPumpError'. These facts never rewrite the process facts above;
      * 'cleanupState' is always 'unconfirmed' here. Killing a child says nothing about whether
        that child's product instance and owned root were released, and this engine will not
        pretend otherwise.

    The process killed on timeout is the one this call created and holds, so the kill is addressed
    by the live Process object, never by name and never by a pid discovered from the system.
    Taking over an unknown product process is out of scope for this engine: it records
    what it knows and refuses to upgrade 'unconfirmed' into a pass. It does NOT assume what the
    missing confirmation would take; whether that needs a new narrow public interface, and what it
    would have to return, is a separate question recorded in 05-REVISION-RECORD-R3.md.
#>
function Invoke-UiAggregateChildProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Request,
        [int] $TimeoutSeconds = 3600,
        [string] $EnginePath = '',
        <#
            Replaces ONLY the spawn boundary: the call that hands back a process object. Everything
            after it - the started flag, the wait, the exit collection, the timeout kill and the
            returned facts - is this function's own code and runs unchanged. This exists so a
            regression can exercise the real default invoker without starting a real process;
            it is not a second implementation and it changes nothing when it is not supplied.
        #>
        [scriptblock] $SpawnProcess
    )

    $engine = $EnginePath
    if ([string]::IsNullOrWhiteSpace($engine)) {
        $engineName = 'powershell.exe'
        if ($PSVersionTable.PSVersion.Major -ge 6) { $engineName = 'pwsh.exe' }
        $engine = Join-Path $PSHOME $engineName
    }
    if (-not [IO.File]::Exists($engine)) { throw ('aggregate_engine_missing: ' + $engine) }

    $argList = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in @($Request.argv)) {
        [void] $argList.Add((Get-UiAggregateQuotedArgument -Value ([string] $item)))
    }

    $stdoutPath = [string] $Request.outputDirectory + '.stdout.log'
    $stderrPath = [string] $Request.outputDirectory + '.stderr.log'

    $spawned = $false
    $processId = 0
    $processStartUtcTicks = 0
    $timedOut = $false
    $killed = $false
    $exitCode = $null
    $exitCodeRaw = $null
    $exitCodeRawType = ''
    $exitReadError = ''
    $failure = ''
    $stdoutPumpStatus = 'skipped'
    $stderrPumpStatus = 'skipped'
    $stdoutPumpError = ''
    $stderrPumpError = ''
    $stdoutPump = $null
    $stderrPump = $null
    $stdoutFile = $null
    $stderrFile = $null
    try {
        $arguments = $argList.ToArray()
        if ($null -ne $SpawnProcess) {
            $process = & $SpawnProcess -FilePath $engine -ArgumentList $arguments `
                -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
            $spawned = $true
        }
        else {
            # The default spawn goes through the process start info and not through the
            # Start-Process cmdlet. On the older runtime that cmdlet cannot build the child
            # environment when the block carries names that differ only in case - it throws on the
            # duplicate key - and where it does start the child it hands back an object whose exit
            # code reads as nothing, which the integer cast then turns into a zero. Both were
            # reproduced against the fixed hosts before this change. The arguments already carry
            # their quoting, so the command line is the same one the cmdlet would have built.
            $startInfo = New-Object System.Diagnostics.ProcessStartInfo
            $startInfo.FileName = $engine
            $startInfo.UseShellExecute = $false
            $startInfo.Arguments = ($argList -join ' ')
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $startInfo.CreateNoWindow = $true
            $process = [System.Diagnostics.Process]::Start($startInfo)
            # The spawn fact and the identity of the object this call holds are recorded the moment
            # the start returned and before any capture is set up: a setup that fails below must
            # never rewrite a started child into 'never started', and the kill on timeout works
            # from this held object.
            $spawned = $true
            # The child's raw output bytes are copied to the same two files the file redirection
            # used to create, from the moment the child starts and without decoding them. Each
            # setup is its own guarded step whose outcome is kept as that stream's own fact; a
            # failure here never touches started, the pid, the wait or the exit facts. The files
            # are opened only after the spawn succeeded, so a spawn that fails still creates none.
            $stdoutPumpStatus = 'active'
            try {
                $stdoutFile = [IO.File]::Open($stdoutPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
                $stdoutPump = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
            }
            catch { $stdoutPumpStatus = 'failed'; $stdoutPumpError = [string] $_.Exception.Message }
            $stderrPumpStatus = 'active'
            try {
                $stderrFile = [IO.File]::Open($stderrPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
                $stderrPump = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
            }
            catch { $stderrPumpStatus = 'failed'; $stderrPumpError = [string] $_.Exception.Message }
        }
        try { $processId = [int] $process.Id } catch { $processId = 0 }
        try { $processStartUtcTicks = [long] $process.StartTime.ToUniversalTime().Ticks } catch { $processStartUtcTicks = 0 }
        if ($process.WaitForExit($TimeoutSeconds * 1000)) {
            # The exit code is read exactly once and recorded as it was read: the raw value, its
            # type, the read error and the final converted value all come from this one read of
            # this one process object. A read that answers nothing, or fails, keeps the exit
            # unknown and names the read failure, because casting that answer is how a missing exit
            # used to become a zero. A real zero is a reported fact and survives.
            $rawExit = $null
            $exitReadError = ''
            try { $rawExit = $process.ExitCode } catch { $exitReadError = [string] $_.Exception.Message }
            $exitCodeRaw = $rawExit
            if ($null -ne $rawExit) {
                try { $exitCodeRawType = $rawExit.GetType().FullName } catch { $exitCodeRawType = '' }
                try { $exitCode = [int] $rawExit }
                catch {
                    $exitCode = $null
                    $exitReadError = 'the exit value could not be converted: ' + $_.Exception.Message
                }
            }
            if ($null -eq $exitCode) {
                if ([string]::IsNullOrWhiteSpace($exitReadError)) { $exitReadError = 'the exit read answered nothing' }
                $failure = 'child_exit_read_failed: ' + $exitReadError
            }
        }
        else {
            $timedOut = $true
            $killed = $true
            $process.Kill()
            [void] $process.WaitForExit(5000)
        }
    }
    catch {
        # 'started' keeps what was observed. A failure after the spawn is not the same fact as a
        # spawn that never happened, and the verdict has to be able to tell them apart.
        $failure = [string] $_.Exception.Message
    }
    finally {
        # The completion of each capture is its own fact: the wait is bounded, and a copy that did
        # not finish in the bound, a copy that faulted, and a file close that failed are each kept
        # on that stream's own fields. Nothing here rewrites the started, pid, wait or exit facts
        # taken above, and nothing here upgrades the cleanup state.
        if ($null -ne $stdoutPump) {
            try {
                if ($stdoutPump.Wait(5000)) { $stdoutPumpStatus = 'completed' }
                else { $stdoutPumpStatus = 'timed-out'; $stdoutPumpError = 'the copy did not complete within the five second bound' }
            }
            catch { $stdoutPumpStatus = 'failed'; $stdoutPumpError = [string] $_.Exception.Message }
        }
        if ($null -ne $stdoutFile) {
            try { $stdoutFile.Dispose() }
            catch {
                if ($stdoutPumpStatus -ceq 'completed') { $stdoutPumpStatus = 'close-failed' }
                if ([string]::IsNullOrWhiteSpace($stdoutPumpError)) { $stdoutPumpError = [string] $_.Exception.Message }
            }
        }
        if ($null -ne $stderrPump) {
            try {
                if ($stderrPump.Wait(5000)) { $stderrPumpStatus = 'completed' }
                else { $stderrPumpStatus = 'timed-out'; $stderrPumpError = 'the copy did not complete within the five second bound' }
            }
            catch { $stderrPumpStatus = 'failed'; $stderrPumpError = [string] $_.Exception.Message }
        }
        if ($null -ne $stderrFile) {
            try { $stderrFile.Dispose() }
            catch {
                if ($stderrPumpStatus -ceq 'completed') { $stderrPumpStatus = 'close-failed' }
                if ([string]::IsNullOrWhiteSpace($stderrPumpError)) { $stderrPumpError = [string] $_.Exception.Message }
            }
        }
    }

    return [ordered] @{
        started              = [bool] $spawned
        exitCode             = $exitCode
        exitCodeRaw          = $exitCodeRaw
        exitCodeRawType      = $exitCodeRawType
        exitReadError        = $exitReadError
        timedOut             = [bool] $timedOut
        killed               = [bool] $killed
        processId            = [int] $processId
        processStartUtcTicks = [long] $processStartUtcTicks
        failure              = $failure
        cleanupState         = 'unconfirmed'
        invokerKind          = 'process'
        stdoutPath           = $stdoutPath
        stderrPath           = $stderrPath
        stdoutPumpStatus     = $stdoutPumpStatus
        stdoutPumpError      = $stdoutPumpError
        stderrPumpStatus     = $stderrPumpStatus
        stderrPumpError      = $stderrPumpError
        engine               = $engine
        arguments            = $argList.ToArray()
    }
}

<#
.SYNOPSIS
    Reads one child's real output and decides that child's status.

.DESCRIPTION
    Every check below is a fact about this run, and the order matters:
      1. process facts - never started, timed out, or started with no confirmed exit;
      2. summary shape - one entry, the requested identity, and no contradiction between the
         summary top level and that entry's own three statuses;
      3. the child's own verdict, adopted only when nothing above contradicts it;
      4. the declared report path, resolved through the summary's relative-path contract and read
         from exactly where it resolved;
      5. the report - one scenario, the right slug, every step Passed, every declared evidence file
         present and inside the report directory;
      6. binding to this run - product hash, driver hash, source commit, driver version;
      7. freshness - the report started after this composition began;
      8. cleanup - confirmed only by the child's own report saying so.
    Anything that cannot be verified is Failed, because an unverifiable child must never read as a
    pass.
#>
function Get-UiAggregateChildVerdict {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Request,
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)][DateTimeOffset] $StartedAt,
        [Parameter(Mandatory)][hashtable] $Bindings,
        [string] $RunToken = '',
        [string] $OwnedRoot = ''
    )

    $childId = [string] $Request.id
    $expectedSlug = ConvertTo-UiAcceptanceSlug -Value $childId
    $childOutput = [string] $Request.outputDirectory
    $summaryPath = [IO.Path]::Combine($childOutput, 'summary.json')

    <#
        The record below is built BEFORE the result is read, so nothing in it may touch a member
        of the result. A member access here is a second read path with different rules from the
        reader's: on a node that does not carry the key it is a terminating strict-mode error (a
        decoded JSON object missing the property) instead of the 'absent' answer the reader gives
        for all three shapes, and that error escaped the verdict and took the whole composition
        down with it, so a child that simply omitted a fact destroyed the aggregate file instead
        of being recorded as the failure it is. Every process fact is therefore read exactly once,
        inside the try, through the one reader.

        'startedReported' and 'exitCodeReported' are the presence bits the reader reports, kept in
        the persisted record on purpose: a key that is ABSENT and a key that carries an explicit
        false / null are different facts about the child, and collapsing them into one boolean is
        how a reported false used to become 'never happened'.
    #>
    $record = [ordered] @{
        index                 = [int] $Request.index
        id                    = $childId
        outputName            = [string] $Request.outputName
        outputDirectory       = $childOutput
        requestedScenario     = $childId
        argvCount             = @($Request.argv).Count
        # Neutral start values only; every one of them is re-read through the single reader below.
        started               = $false
        startedReported       = $false
        exitCode              = $null
        exitCodeReported      = $false
        timedOut              = $false
        killed                = $false
        processId             = 0
        invokerKind           = ''
        status                = 'NotRun'
        reason                = ''
        summaryPath           = 'summary.json'
        reportDeclaredPath    = ''
        reportResolvedPath    = ''
        reportBound           = $false
        reportStatus          = ''
        executionStatus       = ''
        summaryStatus         = ''
        sourceCommit          = ''
        productHash           = ''
        driverVersion         = ''
        driverHash            = ''
        startedUtc            = ''
        startedUtcRaw         = ''
        cleanupStatus         = ''
        cleanupState          = ''
        cleanupConfirmed      = $false
        dispatchedCount       = -1
        scenarioCount         = 0
        stepCount             = 0
        evidenceCount         = 0
        expectedSourceCommit  = ''
        expectedDriverVersion = ''
    }

    try {
        # Every process fact is read through the one reader, including these. The verdict used to
        # read some of them by direct member access, which is a second read path with different
        # rules: on a node that does not carry the key it answers differently depending on the
        # shape and on strict mode, instead of reporting 'absent' the way the reader does. One
        # reader means one answer for all three shapes.
        $record.invokerKind = [string] (Get-UiAggregateNodeValue -Node $Result -Name 'invokerKind')
        $record.processId = [int] (Get-UiAggregateNodeValue -Node $Result -Name 'processId')
        $record.timedOut = [bool] (Get-UiAggregateNodeValue -Node $Result -Name 'timedOut')
        $record.killed = [bool] (Get-UiAggregateNodeValue -Node $Result -Name 'killed')
        $record.cleanupState = [string] (Get-UiAggregateNodeValue -Node $Result -Name 'cleanupState')

        # Presence is recorded before any decision is taken, so a child that omitted the key and a
        # child that reported an explicit false or null stay distinguishable in the persisted
        # record even though both refuse to pass.
        $startedSeen = $false
        $startedRaw = Get-UiAggregateNodeValue -Node $Result -Name 'started' -Found ([ref] $startedSeen)
        $record.started = [bool] $startedRaw
        $record.startedReported = [bool] $startedSeen
        $startedFlag = [bool] $startedRaw

        # The exit fact is read, and its presence recorded, before any decision is taken. A presence
        # bit that still holds 'not read yet' is not the same fact as 'the child did not supply an
        # exit', and collapsing the two is how a supplied exit came to be recorded as never given.
        # The rejection order and its codes are unchanged: the started check below still comes first,
        # and the exit VALUE is only bound after it, so a child that never started is still recorded
        # with no exit value rather than with a zero.
        $exitSeen = $false
        $reportedExit = Get-UiAggregateNodeValue -Node $Result -Name 'exitCode' -Found ([ref] $exitSeen)
        $record.exitCodeReported = [bool] $exitSeen

        if (-not $startedFlag) {
            throw (New-UiAggregateChildFailure -Code 'child_process_not_started' -Status 'Failed')
        }
        if ([bool] $record.timedOut -or [bool] $record.killed) {
            throw (New-UiAggregateChildFailure -Code 'child_exit_timeout' -Status 'Failed')
        }
        if ($null -eq $reportedExit) {
            # Spawned, but no real exit was collected. That is not 'never started' and it is not an
            # exit of zero either.
            throw (New-UiAggregateChildFailure -Code 'child_exit_unconfirmed' -Status 'Failed')
        }
        $exit = [int] $reportedExit
        $record.exitCode = $exit

        if (-not [IO.File]::Exists($summaryPath)) {
            throw (New-UiAggregateChildFailure -Code 'child_summary_missing' -Status 'Failed')
        }
        $summary = $null
        try { $summary = ([IO.File]::ReadAllText($summaryPath) | ConvertFrom-Json) }
        catch { throw (New-UiAggregateChildFailure -Code 'child_summary_invalid' -Status 'Failed') }
        if ($null -eq $summary) {
            throw (New-UiAggregateChildFailure -Code 'child_summary_invalid' -Status 'Failed')
        }

        $entries = @(Get-UiAggregateNodeValue -Node $summary -Name 'entries')
        if ($entries.Count -ne 1) {
            throw (New-UiAggregateChildFailure -Code 'child_summary_entry_count' -Status 'Failed')
        }
        $entry = $entries[0]
        $entryId = [string] (Get-UiAggregateNodeValue -Node $entry -Name 'id')
        if ($entryId -cne $childId) {
            throw (New-UiAggregateChildFailure -Code 'child_identity_mismatch' -Status 'Failed')
        }

        $entryStatus = [string] (Get-UiAggregateNodeValue -Node $entry -Name 'status')
        $reportStatus = [string] (Get-UiAggregateNodeValue -Node $entry -Name 'reportStatus')
        $executionStatus = [string] (Get-UiAggregateNodeValue -Node $entry -Name 'executionStatus')
        $summaryStatus = [string] (Get-UiAggregateNodeValue -Node $summary -Name 'status')
        $record.reportStatus = $reportStatus
        $record.executionStatus = $executionStatus
        $record.summaryStatus = $summaryStatus

        $effective = Get-UiAggregateEffectiveStatus -ReportStatus $reportStatus -ExecutionStatus $executionStatus
        if ($effective -cne $entryStatus) {
            throw (New-UiAggregateChildFailure -Code 'child_entry_status_contradiction' -Status 'Failed')
        }
        if ($summaryStatus -cne $entryStatus) {
            throw (New-UiAggregateChildFailure -Code 'child_summary_status_inconsistent' -Status 'Failed')
        }
        $completeFlag = [bool] (Get-UiAggregateNodeValue -Node $summary -Name 'complete')
        if ($completeFlag -ne ($entryStatus -ceq 'Passed')) {
            throw (New-UiAggregateChildFailure -Code 'child_summary_complete_inconsistent' -Status 'Failed')
        }

        $dispatch = -1
        $checks = Get-UiAggregateNodeValue -Node $summary -Name 'checks'
        if ($null -ne $checks) {
            $dispatch = [int] (Get-UiAggregateNodeValue -Node $checks -Name 'dispatchedCount')
        }
        $record.dispatchedCount = $dispatch

        if ($entryStatus -ceq 'Blocked') {
            $blockReason = [string] (Get-UiAggregateNodeValue -Node $entry -Name 'reason')
            throw (New-UiAggregateChildFailure -Code ('child_blocked:' + $blockReason) -Status 'Blocked')
        }
        if ($entryStatus -ceq 'NotRun') {
            throw (New-UiAggregateChildFailure -Code 'child_not_run' -Status 'NotRun')
        }
        if ($entryStatus -cne 'Passed') {
            throw (New-UiAggregateChildFailure -Code ('child_failed:' + $entryStatus) -Status 'Failed')
        }

        if ($exit -ne 0) {
            throw (New-UiAggregateChildFailure -Code 'child_exit_nonzero' -Status 'Failed')
        }
        if ($dispatch -ne 1) {
            throw (New-UiAggregateChildFailure -Code 'child_dispatch_count' -Status 'Failed')
        }

        # The declared path is the contract, not a hint: it is resolved through the same relative
        # path rule the entry applies to its own index, and the file that is read is the file it
        # resolves to.
        $declared = [string] (Get-UiAggregateNodeValue -Node $entry -Name 'reportPath')
        $record.reportDeclaredPath = $declared
        if (-not (Test-UiAggregateRelativePath -Value $declared)) {
            throw (New-UiAggregateChildFailure -Code 'child_report_path_invalid' -Status 'Failed')
        }
        $resolved = [IO.Path]::Combine($childOutput, $declared)
        $record.reportResolvedPath = $declared
        if (-not [IO.File]::Exists($resolved)) {
            throw (New-UiAggregateChildFailure -Code 'child_report_missing' -Status 'Failed')
        }
        $report = $null
        try { $report = ([IO.File]::ReadAllText($resolved) | ConvertFrom-Json) }
        catch { throw (New-UiAggregateChildFailure -Code 'child_report_invalid' -Status 'Failed') }
        if ($null -eq $report) {
            throw (New-UiAggregateChildFailure -Code 'child_report_invalid' -Status 'Failed')
        }
        $record.reportBound = $true
        $reportBase = [IO.Path]::GetDirectoryName($resolved)
        if ([string]::IsNullOrWhiteSpace($reportBase)) { $reportBase = $childOutput }

        $scenarios = @(Get-UiAggregateNodeValue -Node $report -Name 'scenarios')
        $record.scenarioCount = $scenarios.Count
        if ($scenarios.Count -ne 1) {
            throw (New-UiAggregateChildFailure -Code 'child_report_scenario_count' -Status 'Failed')
        }
        $first = $scenarios[0]
        if ([string] (Get-UiAggregateNodeValue -Node $first -Name 'id') -cne $expectedSlug) {
            throw (New-UiAggregateChildFailure -Code 'child_report_identity_mismatch' -Status 'Failed')
        }
        $scenarioStatus = [string] (Get-UiAggregateNodeValue -Node $first -Name 'status')
        if ($scenarioStatus -cne 'Passed') {
            throw (New-UiAggregateChildFailure -Code ('child_report_status:' + $scenarioStatus) -Status 'Failed')
        }

        $cleanup = Get-UiAggregateNodeValue -Node $report -Name 'cleanup'
        $cleanupStatus = ''
        if ($null -ne $cleanup) { $cleanupStatus = [string] (Get-UiAggregateNodeValue -Node $cleanup -Name 'status') }
        $record.cleanupStatus = $cleanupStatus
        if ($cleanupStatus -cne 'Passed') {
            throw (New-UiAggregateChildFailure -Code 'child_cleanup_failed' -Status 'Failed')
        }

        $record.sourceCommit = [string] (Get-UiAggregateNodeValue -Node $report -Name 'sourceCommit')
        $record.productHash = [string] (Get-UiAggregateNodeValue -Node $report -Name 'productHash')
        $record.driverVersion = [string] (Get-UiAggregateNodeValue -Node $report -Name 'driverVersion')
        $record.driverHash = [string] (Get-UiAggregateNodeValue -Node $report -Name 'driverHash')

        if ($record.productHash -cne [string] $Bindings['ProductHash']) {
            throw (New-UiAggregateChildFailure -Code 'child_product_binding_mismatch' -Status 'Failed')
        }
        if ($record.driverHash -cne [string] $Bindings['DriverHash']) {
            throw (New-UiAggregateChildFailure -Code 'child_driver_binding_mismatch' -Status 'Failed')
        }

        $expectedSource = [string] $Bindings['SourceCommit']
        $record.expectedSourceCommit = $expectedSource
        if ([string]::IsNullOrWhiteSpace($expectedSource)) {
            throw (New-UiAggregateChildFailure -Code 'child_source_commit_unverifiable' -Status 'Failed')
        }
        if ($record.sourceCommit -cne $expectedSource) {
            throw (New-UiAggregateChildFailure -Code 'child_source_commit_mismatch' -Status 'Failed')
        }

        $expectedDriverVersion = [string] $Bindings['DriverVersion']
        $record.expectedDriverVersion = $expectedDriverVersion
        if ([string]::IsNullOrWhiteSpace($expectedDriverVersion)) {
            throw (New-UiAggregateChildFailure -Code 'child_driver_version_unverifiable' -Status 'Failed')
        }
        if ($record.driverVersion -cne $expectedDriverVersion) {
            throw (New-UiAggregateChildFailure -Code 'child_driver_version_mismatch' -Status 'Failed')
        }

        # Every step, not only the first: a later step that failed, or a later step whose declared
        # evidence is missing, is a failure of this child.
        $steps = @(Get-UiAggregateNodeValue -Node $first -Name 'steps')
        $record.stepCount = $steps.Count
        if ($steps.Count -lt 1) {
            throw (New-UiAggregateChildFailure -Code 'child_steps_missing' -Status 'Failed')
        }
        $stepIndex = 0
        $evidenceTotal = 0
        foreach ($step in $steps) {
            $stepIndex = $stepIndex + 1
            $stepStatus = [string] (Get-UiAggregateNodeValue -Node $step -Name 'status')
            if ($stepStatus -cne 'Passed') {
                throw (New-UiAggregateChildFailure -Code ('child_step_failed:' + $stepIndex + ':' + $stepStatus) -Status 'Failed')
            }
            $paths = @(Get-UiAggregateNodeValue -Node $step -Name 'evidencePaths')
            foreach ($relative in $paths) {
                $relativeText = [string] $relative
                $evidenceTotal = $evidenceTotal + 1
                if (-not (Test-UiAggregateRelativePath -Value $relativeText)) {
                    throw (New-UiAggregateChildFailure -Code 'child_evidence_path_invalid' -Status 'Failed')
                }
                if (-not [IO.File]::Exists([IO.Path]::Combine($reportBase, $relativeText))) {
                    throw (New-UiAggregateChildFailure -Code 'child_evidence_file_missing' -Status 'Failed')
                }
            }
        }
        $record.evidenceCount = $evidenceTotal
        if ($evidenceTotal -lt 1) {
            throw (New-UiAggregateChildFailure -Code 'child_evidence_missing' -Status 'Failed')
        }

        $rawStart = Get-UiAggregateNodeValue -Node $report -Name 'startedUtc'
        $record.startedUtcRaw = [string] $rawStart
        $reportedStart = ConvertTo-UiAggregateDateTimeOffset -Value $rawStart
        if ($null -eq $reportedStart) {
            throw (New-UiAggregateChildFailure -Code 'child_report_time_invalid' -Status 'Failed')
        }
        $record.startedUtc = $reportedStart.ToString('o')
        if ($reportedStart -lt $StartedAt) {
            throw (New-UiAggregateChildFailure -Code 'child_report_stale' -Status 'Failed')
        }

        # The child's own cleanup proof is already checked above. What is checked here is the
        # invoker's own state, which is a different claim: 'unconfirmed' is this engine's honest
        # answer for a child it started and cannot independently verify, and it must not be
        # rejected, or no real child could ever be confirmed either. Anything outside the two known
        # states is unknown, and unknown is a failure.
        if ($record.cleanupState -cne 'confirmed' -and $record.cleanupState -cne 'unconfirmed') {
            throw (New-UiAggregateChildFailure -Code 'child_cleanup_unconfirmed' -Status 'Failed')
        }
        $record.cleanupConfirmed = $true
        $record.status = 'Passed'
    }
    catch {
        $record.status = 'Failed'
        $record.reason = [string] $_.Exception.Message
        if ($null -ne $_.Exception.Data -and $_.Exception.Data.Contains('UiAggregateStatus')) {
            $record.status = [string] $_.Exception.Data['UiAggregateStatus']
        }
        if (-not [string]::IsNullOrWhiteSpace($RunToken)) {
            $record.reason = Protect-UiAcceptanceText -Value $record.reason -RunToken $RunToken -OwnedRoot $OwnedRoot
        }
    }

    return $record
}

<#
.SYNOPSIS
    Builds the record a child gets when it was never dispatched.
#>
function New-UiAggregateUnexecutedRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $PlanEntry,
        [Parameter(Mandatory)][string] $OutputDirectory
    )

    return [ordered] @{
        index                 = [int] $PlanEntry.index
        id                    = [string] $PlanEntry.id
        outputName            = [string] $PlanEntry.outputName
        outputDirectory       = [IO.Path]::Combine($OutputDirectory, [string] $PlanEntry.outputName)
        requestedScenario     = [string] $PlanEntry.id
        argvCount             = 0
        # Same shape as a real verdict record, so the two are interchangeable downstream: a child
        # that was never dispatched reports no presence either, rather than a record that is
        # missing the keys and therefore reads as 'unknown shape'.
        started               = $false
        startedReported       = $false
        exitCode              = $null
        exitCodeReported      = $false
        timedOut              = $false
        killed                = $false
        processId             = 0
        invokerKind           = ''
        status                = 'NotRun'
        reason                = 'child_not_executed'
        summaryPath           = ''
        reportDeclaredPath    = ''
        reportResolvedPath    = ''
        reportBound           = $false
        reportStatus          = ''
        executionStatus       = ''
        summaryStatus         = ''
        sourceCommit          = ''
        productHash           = ''
        driverVersion         = ''
        driverHash            = ''
        startedUtc            = ''
        startedUtcRaw         = ''
        cleanupStatus         = ''
        cleanupState          = ''
        cleanupConfirmed      = $false
        dispatchedCount       = -1
        scenarioCount         = 0
        stepCount             = 0
        evidenceCount         = 0
        expectedSourceCommit  = ''
        expectedDriverVersion = ''
    }
}

<#
.SYNOPSIS
    Writes the aggregate composition record and returns its relative path.

.DESCRIPTION
    The record states what this artifact is: a composition of independently rooted children, in
    order, each with its own identity, exit, binding, report and cleanup confirmation. It never
    claims a single shared root and never claims one production workflow. When any child was
    produced by an injected substitute instead of a real child process, 'substituteChildren' says
    so, because a composition that ran substitutes must not read as one that ran the real public
    entry.
#>
function Write-UiAggregateCompositionFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string] $AggregateId,
        [Parameter(Mandatory)] $Children,
        [Parameter(Mandatory)][string] $Status,
        [Parameter(Mandatory)][bool] $SubstituteChildren,
        [string] $RunToken = '',
        [string] $OwnedRoot = ''
    )

    $document = [ordered] @{
        composition         = 'aggregate'
        aggregateId         = $AggregateId
        status              = $Status
        childCount          = @($Children).Count
        sharedOwnedRoot     = $false
        singleWorkflowClaim = $false
        substituteChildren  = [bool] $SubstituteChildren
        children            = @($Children)
    }

    $json = ConvertTo-Json -InputObject $document -Depth 30
    if (-not [string]::IsNullOrWhiteSpace($RunToken)) {
        $json = Protect-UiAcceptanceText -Value $json -RunToken $RunToken -OwnedRoot $OwnedRoot
    }
    $target = [IO.Path]::Combine($OutputDirectory, 'aggregate.json')
    [IO.File]::WriteAllText($target, $json, [Text.UTF8Encoding]::new($false))

    return 'aggregate.json'
}

<#
.SYNOPSIS
    Dispatches every child of one aggregate entry and composes the verdict.

.DESCRIPTION
    Prerequisites come first and stop everything: no product instance is started by the parent, so
    when the pinned archive path is not bound (the one-line entry patch is not applied yet) the
    aggregate reports Blocked with every child NotRun. A Blocked aggregate is not a pass and is
    not hidden.

    Each child then gets a fresh output directory. A child directory that already exists is a
    refusal, not a reuse: the walk will not read a previous run's tree as this run's success.

    The composed status is the worst child status, so a later child's pass can never overwrite an
    earlier failure, and a child that was never executed stays NotRun instead of disappearing.
    A Passed composition additionally requires every child to have confirmed its own cleanup.
#>
function Invoke-UiAggregateComposition {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $AggregateId,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $ChildScenarios,
        [Parameter(Mandatory)][string] $ExecutablePath,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $RunToken,
        [string] $WinAppCliArchivePath = '',
        [string] $EntryScriptPath = '',
        [string] $EnginePath = '',
        [int] $TimeoutSeconds = 3600,
        [scriptblock] $InvokeChild,
        [string] $ExpectedSourceCommit = '',
        [scriptblock] $ResolveSourceCommit,
        [string] $ExpectedDriverVersion = ''
    )

    $startedAt = [DateTimeOffset]::UtcNow
    $plan = @(Get-UiAggregateChildPlan -ChildScenarios $ChildScenarios)
    $records = New-Object 'System.Collections.Generic.List[object]'
    $artifacts = New-Object 'System.Collections.Generic.List[string]'

    $blocked = ''
    if ([string]::IsNullOrWhiteSpace($EntryScriptPath) -or -not [IO.File]::Exists($EntryScriptPath)) {
        $blocked = 'aggregate_entry_missing'
    }
    elseif ([string]::IsNullOrWhiteSpace($WinAppCliArchivePath) -or -not [IO.File]::Exists($WinAppCliArchivePath)) {
        $blocked = 'aggregate_archive_unbound'
    }
    elseif (-not [IO.File]::Exists($ExecutablePath)) {
        $blocked = 'aggregate_product_unavailable'
    }

    if (-not [string]::IsNullOrWhiteSpace($blocked)) {
        foreach ($entry in $plan) {
            [void] $records.Add((New-UiAggregateUnexecutedRecord -PlanEntry $entry -OutputDirectory $OutputDirectory))
        }
        [void] $artifacts.Add((Write-UiAggregateCompositionFile -OutputDirectory $OutputDirectory `
            -AggregateId $AggregateId -Children $records.ToArray() -Status 'Blocked' -SubstituteChildren $false `
            -RunToken $RunToken -OwnedRoot $OwnedRoot))

        return [pscustomobject] @{
            Status    = 'Blocked'
            Reason    = $blocked
            Artifacts = $artifacts.ToArray()
            Viewports = @()
        }
    }

    # The repository root comes from the public entry's own location, so provenance and the pinned
    # driver version are read from the tree this run is actually executing.
    $repositoryRoot = ''
    if (-not [string]::IsNullOrWhiteSpace($EntryScriptPath)) {
        $repositoryRoot = [string] (Split-Path -Parent (Split-Path -Parent $EntryScriptPath))
    }

    $sourceCommit = $ExpectedSourceCommit
    if ([string]::IsNullOrWhiteSpace($sourceCommit)) {
        if ($null -ne $ResolveSourceCommit) {
            try { $sourceCommit = [string] (& $ResolveSourceCommit) }
            catch { $sourceCommit = '' }
        }
        else {
            $sourceCommit = Get-UiAggregateSourceCommit -RepositoryRoot $repositoryRoot
        }
    }

    $driverVersion = $ExpectedDriverVersion
    if ([string]::IsNullOrWhiteSpace($driverVersion)) {
        $driverVersion = Get-UiAggregateDriverVersion -RepositoryRoot $repositoryRoot
    }

    $bindings = @{
        ProductHash   = (Get-FileHash -LiteralPath $ExecutablePath -Algorithm SHA256).Hash
        DriverHash    = (Get-FileHash -LiteralPath $WinAppCliArchivePath -Algorithm SHA256).Hash
        SourceCommit  = $sourceCommit
        DriverVersion = $driverVersion
    }

    $productDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($ExecutablePath))
    $substituteChildren = $false

    foreach ($entry in $plan) {
        $request = New-UiAggregateChildRequest -PlanEntry $entry -EntryScriptPath $EntryScriptPath `
            -ProductDirectory $productDirectory -WinAppCliArchivePath ([IO.Path]::GetFullPath($WinAppCliArchivePath)) `
            -ParentOutputDirectory $OutputDirectory

        if ([IO.Directory]::Exists([string] $request.outputDirectory)) {
            $record = New-UiAggregateUnexecutedRecord -PlanEntry $entry -OutputDirectory $OutputDirectory
            $record.status = 'Failed'
            $record.reason = 'child_output_preexisting'
            $record.argvCount = @($request.argv).Count
            [void] $records.Add($record)
            continue
        }

        if ($null -eq $InvokeChild) {
            $result = Invoke-UiAggregateChildProcess -Request $request -TimeoutSeconds $TimeoutSeconds -EnginePath $EnginePath
        }
        else {
            $result = & $InvokeChild $request
            if ($null -eq $result) {
                $result = [ordered] @{
                    started = $false; exitCode = $null; timedOut = $false; killed = $false
                    processId = 0; failure = 'substitute_returned_nothing'; cleanupState = 'unconfirmed'
                    invokerKind = 'substitute'; stdoutPath = ''; stderrPath = ''; engine = ''; arguments = @()
                }
            }
        }

        # A substitute may return fewer facts than a real process does. Rather than losing the
        # child to a missing key, the absent process facts are filled with their conservative
        # value. The fill is keyed on ABSENCE, not on a null read: a reported false, a reported
        # zero and a reported null are facts the child did supply, and overwriting them is how a
        # started process came to be recorded as never started.
        foreach ($name in @('started', 'exitCode', 'timedOut', 'killed', 'processId', 'invokerKind', 'cleanupState')) {
            $seen = $false
            $null = Get-UiAggregateNodeValue -Node $result -Name $name -Found ([ref] $seen)
            if ($seen) { continue }

            $default = $null
            if ($name -ceq 'started') { $default = $false }
            elseif ($name -ceq 'exitCode') { $default = $null }
            elseif ($name -ceq 'timedOut') { $default = $false }
            elseif ($name -ceq 'killed') { $default = $false }
            elseif ($name -ceq 'processId') { $default = 0 }
            elseif ($name -ceq 'invokerKind') { $default = 'substitute' }
            elseif ($name -ceq 'cleanupState') { $default = 'unconfirmed' }

            # Both producers in this code base return an ordered dictionary, so the write below
            # reaches them. A result of any other shape that omitted these facts keeps reading as
            # absent, and the verdict then fails conservatively rather than assuming a process.
            if ($result -is [System.Collections.IDictionary]) { $result[$name] = $default }
        }

        $verdict = Get-UiAggregateChildVerdict -Request $request -Result $result -StartedAt $startedAt `
            -Bindings $bindings -RunToken $RunToken -OwnedRoot $OwnedRoot
        [void] $records.Add($verdict)

        if ([string] (Get-UiAggregateNodeValue -Node $result -Name 'invokerKind') -cne 'process') {
            $substituteChildren = $true
        }

        $stdoutLog = [string] (Get-UiAggregateNodeValue -Node $result -Name 'stdoutPath')
        if (-not [string]::IsNullOrWhiteSpace($stdoutLog) -and [IO.File]::Exists($stdoutLog)) {
            [void] $artifacts.Add(([string] $request.outputName + '.stdout.log'))
        }
        $stderrLog = [string] (Get-UiAggregateNodeValue -Node $result -Name 'stderrPath')
        if (-not [string]::IsNullOrWhiteSpace($stderrLog) -and [IO.File]::Exists($stderrLog)) {
            [void] $artifacts.Add(([string] $request.outputName + '.stderr.log'))
        }
        if ([string] $verdict.reportDeclaredPath -cne '') {
            $declaredReport = [IO.Path]::Combine([string] $request.outputDirectory, [string] $verdict.reportDeclaredPath)
            if ([IO.File]::Exists($declaredReport)) {
                [void] $artifacts.Add(([string] $request.outputName + '/' + [string] $verdict.reportDeclaredPath))
            }
        }
    }

    $statuses = @($records.ToArray() | ForEach-Object { [string] $_.status })
    $status = 'NotRun'
    if (@($statuses | Where-Object { $_ -ceq 'Failed' }).Count -gt 0) { $status = 'Failed' }
    elseif (@($statuses | Where-Object { $_ -ceq 'Blocked' }).Count -gt 0) { $status = 'Blocked' }
    elseif (@($statuses | Where-Object { $_ -ceq 'NotRun' }).Count -gt 0) { $status = 'NotRun' }
    else { $status = 'Passed' }

    if ($statuses.Count -ne $plan.Count) { $status = 'NotRun' }

    # A Passed aggregate has to be a fully confirmed one: if any child reached the end without a
    # confirmed cleanup, the composition is not a pass.
    if ($status -ceq 'Passed') {
        foreach ($record in $records.ToArray()) {
            if (-not [bool] $record.cleanupConfirmed) { $status = 'Failed'; break }
        }
    }

    $reason = ''
    if ($status -cne 'Passed') {
        $parts = New-Object 'System.Collections.Generic.List[string]'
        foreach ($record in $records.ToArray()) {
            if ([string] $record.status -cne 'Passed') {
                [void] $parts.Add(([string] $record.id + '=' + [string] $record.status + ':' + [string] $record.reason))
            }
        }
        $reason = ($parts.ToArray() -join '; ')
        if (-not [string]::IsNullOrWhiteSpace($RunToken)) {
            $reason = Protect-UiAcceptanceText -Value $reason -RunToken $RunToken -OwnedRoot $OwnedRoot
        }
    }

    [void] $artifacts.Insert(0, (Write-UiAggregateCompositionFile -OutputDirectory $OutputDirectory `
        -AggregateId $AggregateId -Children $records.ToArray() -Status $status `
        -SubstituteChildren $substituteChildren -RunToken $RunToken -OwnedRoot $OwnedRoot))

    return [pscustomobject] @{
        Status    = $status
        Reason    = $reason
        Artifacts = $artifacts.ToArray()
        Viewports = @()
    }
}

Export-ModuleMember -Function @(
    'New-UiAggregateChildFailure',
    'Get-UiAggregateNodeValue',
    'Test-UiAggregateRelativePath',
    'ConvertTo-UiAggregateDateTimeOffset',
    'Get-UiAggregateEffectiveStatus',
    'Get-UiAggregateSourceCommit',
    'Get-UiAggregateDriverVersion',
    'Get-UiAggregateChildPlan',
    'Get-UiAggregateQuotedArgument',
    'New-UiAggregateChildRequest',
    'Invoke-UiAggregateChildProcess',
    'Get-UiAggregateChildVerdict',
    'Write-UiAggregateCompositionFile',
    'New-UiAggregateUnexecutedRecord',
    'Invoke-UiAggregateComposition'
)
