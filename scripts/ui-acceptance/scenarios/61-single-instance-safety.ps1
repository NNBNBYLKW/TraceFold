# TraceFold UI acceptance - single instance safety (Task 12, Step 4).
#
# This scenario owns a fresh owned root and proves what happens when the product is started a
# second time against the same owned data root while the first instance is running.
#
# Shape:
#   1. start the primary instance on a fresh owned root through the product's own launch path;
#   2. start a second process with the same executable and the same owned data root, and read its
#      real exit code - the product's single instance coordinator returns 0 when it redirected the
#      activation and 1 when it failed or timed out, so 0 is required and 1 is a failure;
#   3. prove the second start created no second database and used no other library;
#   4. establish a controlled non-foreground pre-state with a window this harness owns, then read the
#      foreground once after the second start and require that handle to be the primary window and
#      that handle's owner to be the primary process. An inspect that succeeds only shows the
#      instance is observable; it is not offered as the activation proof;
#   5. close the primary through the product's own window, prove no leftovers, and start it again on
#      the same owned root.
#
# NOT yet implemented, and therefore not claimed by this file:
#   * the negative case for an outside instance. It is not implemented in this file at all: proving
#     Blocked with zero dispatches and zero new starts needs a separate controlled fixture with its
#     own ownership ledger, which lives outside this scenario.
#
# Bans: no coordinates, no fixed sleeps, no termination by process name, no direct database access,
# no coaxing the primary window to the foreground, and no reuse of another scenario's root.
#
# The single instance key is a fixed constant in the product and does not include the data root.
# This scenario therefore does not claim a machine-wide or cross-session scope for that key; it only
# exercises two starts against the same owned root with the same executable.

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ScenarioId,
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $ContractPath,
    [Parameter(Mandatory)][string] $OwnedRoot,
    [Parameter(Mandatory)][string] $RunToken,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][string] $SupportPath,
    [Parameter(Mandatory)][string] $WinAppPath,
    [int] $WaitTimeoutSeconds = 60,
    [int] $InteractiveTimeoutSeconds = 60,
    [int] $SecondaryTimeoutSeconds = 45,
    [int] $ObservationTimeoutSeconds = 20,
    [int] $InspectDepth = 20,
    # The default launch seam: the primary and the restart still go through Start-TraceFoldScenario,
    # which validates the owned root and writes the identity ledger, and only the process start
    # itself is supplied here. The environment it is handed reaches the child through this process's
    # own block for the duration of the start and is restored immediately afterwards: the process
    # start info's Environment member does not exist on the older runtime and its EnvironmentVariables
    # member reads back as null on a first access there. A dispatcher that supplies its own seam
    # replaces exactly this step and nothing else.
    [scriptblock] $LaunchProcess = {
        param($path, $childEnvironment)
        $info = New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName = $path
        $info.UseShellExecute = $false
        $restore = @{}
        foreach ($key in $childEnvironment.Keys) {
            $restore[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, [string] $childEnvironment[$key], 'Process')
        }
        try {
            $proc = [Diagnostics.Process]::Start($info)
        }
        finally {
            foreach ($key in $restore.Keys) {
                [Environment]::SetEnvironmentVariable($key, $restore[$key], 'Process')
            }
        }
        $record = [pscustomobject] @{
            ProcessId      = $proc.Id
            StartUtcTicks  = $proc.StartTime.ToUniversalTime().Ticks
            ExecutablePath = $path
        }
        $proc.Dispose()
        return $record
    },
    [scriptblock] $DesktopCheck = { Test-UiInteractiveDesktop },
    [scriptblock] $ForegroundCheck = { param($id) Test-UiForegroundProcess -ProcessId $id },
    # The controlled object that takes the foreground away from the primary before the second start.
    # It is a seam so a dispatcher can substitute its own controlled window, and the default is a
    # window this harness owns and closes again.
    [scriptblock] $EstablishNonForeground = { New-NonForegroundControlWindow }
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Contracts.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Processes.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Driver.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Evidence.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.Workflows.psm1') -DisableNameChecking

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))

# Conditions this scenario cannot create are Blocked, never Failed: a host without an interactive
# desktop says nothing about the product.
$blockedFailureCodes = @('single.instance.desktop_unavailable')

function Get-SingleInstanceDbInventory {
    param([Parameter(Mandatory)][string] $Root)
    # The product resolves its database as <canonical root>/media-desktop.db
    # (LocalAppDataPathProvider.GetPaths: Path.Combine(canonicalRoot, "media-desktop.db")), so the
    # root itself is the data directory. There is no 'data' subdirectory in that contract, and this
    # function does not go looking for one.
    $canonical = [IO.Path]::GetFullPath($Root)
    if (-not [IO.Directory]::Exists($canonical)) { return @() }
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($file in [IO.Directory]::GetFiles($canonical, '*.db', [IO.SearchOption]::AllDirectories)) {
        $items.Add([IO.Path]::GetFullPath($file))
    }
    # One array boundary, applied once: the helper flattens its paths onto the pipeline (a missing
    # directory and an empty directory both produce nothing), and the caller materialises the result
    # with @() exactly once. Nothing wraps a second time.
    return @($items | Sort-Object -Unique)
}

# WAL and SHM are the main database's own sidecars, listed separately so they are never mistaken for
# a second database, and never frozen: the product writes them while it runs.
function Get-SingleInstanceDbSidecarInventory {
    param([Parameter(Mandatory)][string] $Root)
    $canonical = [IO.Path]::GetFullPath($Root)
    if (-not [IO.Directory]::Exists($canonical)) { return @() }
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($pattern in @('*.db-wal', '*.db-shm')) {
        foreach ($file in [IO.Directory]::GetFiles($canonical, $pattern, [IO.SearchOption]::AllDirectories)) {
            $items.Add([IO.Path]::GetFullPath($file))
        }
    }
    # One array boundary, applied once: the helper flattens its paths onto the pipeline (a missing
    # directory and an empty directory both produce nothing), and the caller materialises the result
    # with @() exactly once. Nothing wraps a second time.
    return @($items | Sort-Object -Unique)
}

function Get-SingleInstanceExpectedDbPath {
    param([Parameter(Mandatory)][string] $Root)
    # Derived from the same contract the product uses, never guessed from whatever the inventory
    # happened to return first.
    return [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetFullPath($Root)) 'media-desktop.db'))
}

function Get-SingleInstanceLibraryInventory {
    param([Parameter(Mandatory)][string] $Root)
    $library = Join-Path ([IO.Path]::GetFullPath($Root)) 'library'
    if (-not [IO.Directory]::Exists($library)) { return @() }
    return @([IO.Directory]::GetFiles($library, '*', [IO.SearchOption]::AllDirectories) | Sort-Object)
}

# The freshness rule as its own unit: exactly the one database the product's path contract names,
# and nothing else. It is driven with real inventories taken by the real helper over real
# temporary files, so the rule is exercised instead of being restated here.
function Assert-SingleInstanceDatabaseState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ExpectedDatabasePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Databases
    )
    if (@($Databases).Count -ne 1) { throw ('single.instance.expected_database_count_' + [string] @($Databases).Count) }
    if (@($Databases) -cnotcontains $ExpectedDatabasePath) { throw 'single.instance.expected_database_missing' }
}

# The second start must reuse the very database the first one created: still exactly one, still
# the expected one, and nothing new in the root or in the library. The differences are computed
# from the inventories that were really taken and returned, so the evidence carries what the
# rule actually saw.
function Assert-SingleInstanceSecondStartDatabase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ExpectedDatabasePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $DatabasesBefore,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $DatabasesAfter,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $LibraryBefore,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]] $LibraryAfter
    )
    # Still exactly the one expected database: a second file, or a different one, is the failure.
    if (@($DatabasesAfter).Count -ne 1) { throw ('single.instance.expected_database_count_' + [string] @($DatabasesAfter).Count) }
    if (@($DatabasesAfter) -cnotcontains $ExpectedDatabasePath) { throw 'single.instance.second_database_used' }
    $databasesCreated = @($DatabasesAfter | Where-Object { @($DatabasesBefore) -cnotcontains $_ })
    if ($databasesCreated.Count -gt 0) { throw 'single.instance.second_database_created' }
    $libraryCreated = @($LibraryAfter | Where-Object { @($LibraryBefore) -cnotcontains $_ })
    if ($libraryCreated.Count -gt 0) { throw 'single.instance.second_library_used' }
    return [pscustomobject] @{ DatabasesCreated = @($databasesCreated); LibraryCreated = @($libraryCreated) }
}

function Get-ForegroundWindowHandle {
    # The probe type is created by the driver's foreground check on first use. The caller therefore
    # runs that check before reading a handle; if the type is still absent the handle is reported as
    # unavailable instead of being returned as a zero that would look like a measurement.
    if (-not ('TraceFoldUiForegroundProbe' -as [type])) { throw 'single.instance.foreground_probe_unavailable' }
    $handle = [TraceFoldUiForegroundProbe]::GetForegroundWindow()
    if ($handle -eq [IntPtr]::Zero) { throw 'single.instance.no_foreground_window' }
    return $handle
}

# The owner of a specific window handle. The activation proof asks this about the one handle it
# sampled, so the process behind that handle is the process the foreground really belongs to.
function Get-WindowOwnerProcessId {
    param([Parameter(Mandatory)][IntPtr] $Handle)
    if (-not ('TraceFoldUiForegroundProbe' -as [type])) { throw 'single.instance.foreground_probe_unavailable' }
    if ($Handle -eq [IntPtr]::Zero) { throw 'single.instance.window_handle_missing' }
    [uint32] $owner = 0
    [void] [TraceFoldUiForegroundProbe]::GetWindowThreadProcessId($Handle, [ref] $owner)
    if ($owner -le 0) { throw 'single.instance.window_owner_unknown' }
    return [int] $owner
}

# The foreground probe type is created by the driver's own check when that check runs, so the check
# really has to run before any handle is sampled. The failure that matters is "the check had no
# effect": when the type is still missing afterwards the scenario is refused, and a check that
# throws leaves with its own exception unchanged. This read produces nothing and is never offered
# as the pre-state or as the activation proof.
function Initialize-SingleInstanceForegroundProbe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][scriptblock] $ForegroundCheck,
        # The lookup is a seam of its own, so the refusal can be driven without depending on whether
        # an earlier case in the same session already caused the type to exist.
        [scriptblock] $ResolveProbeType = { return ('TraceFoldUiForegroundProbe' -as [type]) }
    )
    $null = & $ForegroundCheck $ProcessId
    if ($null -eq (& $ResolveProbeType)) { throw 'single.instance.foreground_probe_unavailable' }
}

# The controlled object used to take the foreground away from the primary before the second start.
# It belongs to this harness process, its identity is recorded, and it is always closed again. The
# primary window is never activated, clicked or asked to come forward.
function New-NonForegroundControlWindow {
    [CmdletBinding()]
    param(
        # Both steps are seams so the failure paths can be driven without a live desktop: a caller can
        # supply a form that cannot be built, or a show action that throws. The defaults are the
        # previous logic verbatim, so the behaviour of every existing call is unchanged.
        [scriptblock] $CreateForm = { New-Object System.Windows.Forms.Form },
        [scriptblock] $ShowAction = { param($f) $f.Show(); $f.Activate() }
    )
    if (-not ('System.Windows.Forms.Form' -as [type])) { Add-Type -AssemblyName System.Windows.Forms }
    $form = $null
    try {
        $form = & $CreateForm
        $form.Text = 'TraceFold UI acceptance control window'
        $form.Width = 220
        $form.Height = 120
        $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
        $form.Location = New-Object System.Drawing.Point(-4000, -4000)
        $form.ShowInTaskbar = $false
        & $ShowAction $form
    }
    catch {
        # Two failures that must stay apart. If no object was ever obtained there is nothing to
        # release, and that is recorded as exactly that. If the object exists it belongs to the same
        # lifecycle owner as every other path - the shared release unit - so it is released through
        # that unit, and the release action and read back facts are handed to the caller beside the
        # original error instead of being lost in an empty catch. The failure itself is rethrown
        # unchanged rather than replaced by a release error.
        $factoryError = $_
        $factoryReport = [pscustomobject] @{
            objectObtained   = ($null -ne $form)
            releaseAttempted = $false
            attempt          = $null
        }
        if ($null -ne $form) {
            $release = Complete-SingleInstanceControlRelease -ControlWindow $form
            $factoryReport.releaseAttempted = $true
            $factoryReport.attempt = New-SingleInstanceReleaseAttempt -Source 'factory' -Stage 'factory' -Release $release
        }
        $factoryError.Exception.Data['TraceFold.ControlWindowRelease'] = $factoryReport
        throw
    }
    return $form
}

# The pre-state decision as its own unit, so it can be driven with facts instead of a live desktop.
# It takes the handles and owner pids that were sampled and decides whether a controlled
# non-foreground state really exists. The counter-examples all reduce to different failures here: a
# state that was never established, another window of the same process, and a non-control foreground.
function Assert-SingleInstancePreState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int] $PrimaryPid,
        [Parameter(Mandatory)][long] $ControlHandle,
        [Parameter(Mandatory)][int] $ControlOwnerPid,
        [Parameter(Mandatory)][long] $PreForegroundHandle,
        [Parameter(Mandatory)][int] $PreForegroundOwnerPid
    )
    if ($PreForegroundHandle -ne $ControlHandle) { throw 'single.instance.pre_state_not_controlled' }
    if ($PreForegroundOwnerPid -ne $ControlOwnerPid) { throw 'single.instance.pre_state_owner_mismatch' }
    if ($PreForegroundOwnerPid -eq $PrimaryPid) { throw 'single.instance.pre_state_still_foreground' }
}

# The activation decision as its own unit, driven by the ONE sample that was taken: the foreground
# handle and the owner of that very handle. Two independent samples can never be substituted here,
# and an inspect that succeeded plays no part in it.
function Assert-SingleInstanceActivated {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int] $PrimaryPid,
        [Parameter(Mandatory)][long] $PrimaryWindowHandle,
        [Parameter(Mandatory)][long] $PrimaryWindowAfterHandle,
        [Parameter(Mandatory)][long] $ForegroundHandle,
        [Parameter(Mandatory)][int] $ForegroundOwnerPid
    )
    if ($PrimaryWindowAfterHandle -le 0) { throw 'single.instance.primary_window_missing' }
    if ($ForegroundHandle -ne $PrimaryWindowHandle) { throw 'single.instance.foreground_window_not_primary' }
    if ($ForegroundOwnerPid -ne $PrimaryPid) { throw 'single.instance.primary_not_activated' }
}

# The release of the controlled window as one unit, called by both the success path and the safety
# net so the two can never drift apart. It uses only what the object already offers and it reads the
# result back rather than treating a call that did not throw as proof: an object that is not disposed
# afterwards is reported as a failure, not as an unknown that still counts as released.
function Complete-SingleInstanceControlRelease {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $ControlWindow)
    # Close, Dispose and the single disposed read back are three separate facts with their own
    # guards. A Close that throws must not skip the Dispose that could still release the object, so
    # Dispose is attempted on its own; each action keeps its own error, and an action error can
    # never be reported as a clean release even when the object turns out to be disposed. Released
    # means all of them: neither call failed and the object really is disposed.
    $closeError = ''
    $closeErrorRecord = $null
    try {
        $ControlWindow.Close()
    }
    catch {
        # The record is kept beside the code, so the failure can later be materialised as its own
        # lightweight fact with its type, id, position and stack.
        $closeErrorRecord = $_
        $closeError = 'single.instance.control_window_not_closed: ' + $_.Exception.GetType().Name
    }

    # Attempted whether or not Close threw: releasing the object is the point of this unit.
    $disposeError = ''
    $disposeErrorRecord = $null
    try {
        $ControlWindow.Dispose()
    }
    catch {
        $disposeErrorRecord = $_
        $disposeError = 'single.instance.control_window_not_disposed: ' + $_.Exception.GetType().Name
    }

    # The read back has its own guard, so an object that refuses to answer is reported as unreadable
    # rather than letting the failure escape the unit.
    $readBackError = ''
    $readBackErrorRecord = $null
    $isDisposed = $false
    $releaseReadBack = 'unknown'
    try {
        $isDisposed = [bool] $ControlWindow.IsDisposed
        if ($isDisposed) { $releaseReadBack = 'disposed' } else { $releaseReadBack = 'not-disposed' }
    }
    catch {
        $readBackErrorRecord = $_
        $readBackError = 'single.instance.control_window_read_back_failed: ' + $_.Exception.GetType().Name
    }

    # The standing action error is the first action that failed; the specific ones stay beside it.
    $actionError = ''
    $actionErrorRecord = $null
    if (-not [string]::IsNullOrWhiteSpace($closeError)) {
        $actionError = $closeError
        $actionErrorRecord = $closeErrorRecord
    }
    elseif (-not [string]::IsNullOrWhiteSpace($disposeError)) {
        $actionError = $disposeError
        $actionErrorRecord = $disposeErrorRecord
    }

    $closed = ($isDisposed -and [string]::IsNullOrWhiteSpace($closeError) -and
        [string]::IsNullOrWhiteSpace($disposeError) -and [string]::IsNullOrWhiteSpace($readBackError))
    $releaseFailure = ''
    if (-not [string]::IsNullOrWhiteSpace($closeError)) { $releaseFailure = $closeError }
    elseif (-not [string]::IsNullOrWhiteSpace($disposeError)) { $releaseFailure = $disposeError }
    elseif (-not [string]::IsNullOrWhiteSpace($readBackError)) { $releaseFailure = $readBackError }
    elseif (-not $isDisposed) { $releaseFailure = 'single.instance.control_window_not_released' }

    return [pscustomobject] @{
        closed              = $closed
        releaseReadBack     = $releaseReadBack
        releaseFailure      = $releaseFailure
        actionError         = $actionError
        closeError          = $closeError
        disposeError        = $disposeError
        readBackError       = $readBackError
        isDisposedReadBack  = $isDisposed
        # The raw records travel beside the codes; they are never serialised directly and are what
        # the exit evidence turns into lightweight scalar facts.
        actionErrorRecord   = $actionErrorRecord
        closeErrorRecord    = $closeErrorRecord
        disposeErrorRecord  = $disposeErrorRecord
        readBackErrorRecord = $readBackErrorRecord
    }
}

# One release attempt as its own record: the action error and the read back error stay beside each
# other, so no caller has to flatten them into a single flag. The raw records are kept for the exit
# evidence, which materialises them as facts; nothing else reads them.
function New-SingleInstanceReleaseAttempt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Stage,
        [Parameter(Mandatory)] $Release
    )
    return [pscustomobject] @{
        source              = $Source
        stage               = $Stage
        closed              = [bool] $Release.closed
        releaseReadBack     = [string] $Release.releaseReadBack
        releaseFailure      = [string] $Release.releaseFailure
        actionError         = [string] $Release.actionError
        closeError          = [string] $Release.closeError
        disposeError        = [string] $Release.disposeError
        readBackError       = [string] $Release.readBackError
        isDisposedReadBack  = [bool] $Release.isDisposedReadBack
        actionErrorRecord   = $Release.actionErrorRecord
        closeErrorRecord    = $Release.closeErrorRecord
        disposeErrorRecord  = $Release.disposeErrorRecord
        readBackErrorRecord = $Release.readBackErrorRecord
    }
}

# One error materialised as a lightweight fact: scalars only, taken from the ErrorRecord that is
# actually in flight, with the existing redaction applied. It is deliberately not the ErrorRecord:
# no CLIXML, no session state and no deep serialisation of InvocationInfo ever reach the evidence.
function ConvertTo-SingleInstanceErrorFact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $ErrorRecord,
        [Parameter(Mandatory)][string] $Stage,
        [string] $RunToken = '',
        [string] $OwnedRoot = ''
    )
    $fact = [pscustomobject] [ordered] @{
        stage     = [string] $Stage
        errorType = [string] $ErrorRecord.Exception.GetType().FullName
        errorId   = [string] $ErrorRecord.FullyQualifiedErrorId
        category  = [string] $ErrorRecord.CategoryInfo.Category
        position  = [string] $ErrorRecord.InvocationInfo.PositionMessage
        stack     = [string] $ErrorRecord.ScriptStackTrace
        message   = [string] $ErrorRecord.Exception.Message
    }
    foreach ($property in @('stage', 'errorType', 'errorId', 'category', 'position', 'stack', 'message')) {
        $fact.$property = Protect-UiAcceptanceText -Value ([string] $fact.$property) -RunToken $RunToken -OwnedRoot $OwnedRoot
    }
    return $fact
}

# The last resort for a failure fact: no record machinery, no conversion and no second attempt at
# the converter, just fixed scalar fields with the message redacted through the existing redaction.
# A redaction that is itself unavailable is never an excuse to publish the raw text, and an empty
# message is legal: it is simply left out instead of being smuggled in as an empty suffix.
function New-SingleInstanceFallbackErrorFact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Stage,
        # An exception message may legally be empty, so the empty string is an accepted input here;
        # refusing it would make this last resort itself a source of new errors.
        [Parameter(Mandatory)][AllowEmptyString()][string] $Message,
        [string] $RunToken = '',
        [string] $OwnedRoot = ''
    )
    $redacted = ''
    try { $redacted = Protect-UiAcceptanceText -Value $Message -RunToken $RunToken -OwnedRoot $OwnedRoot }
    catch { $redacted = '[redaction-unavailable]' }
    $summary = 'fact-conversion-failed'
    if (-not [string]::IsNullOrWhiteSpace($redacted) -and $redacted -cne '[redaction-unavailable]') {
        $summary = $summary + ': ' + $redacted
    }
    elseif ($redacted -ceq '[redaction-unavailable]') {
        $summary = $summary + ': [redaction-unavailable]'
    }
    return [pscustomobject] [ordered] @{
        stage     = $Stage
        errorType = 'unavailable'
        errorId   = 'fact-conversion-failed'
        category  = ''
        position  = ''
        stack     = ''
        message   = $summary
    }
}

# The conversion used inside the failure paths. The conversion itself is a seam, so a failure of the
# fact materialisation can be driven once; whatever happens, this function never throws, and its
# fallback never recurses into the same converter that just failed. Both the message sampling and
# the fallback call are bounded, so even an empty message or an unreadable exception can only ever
# produce a static scalar fact.
function New-SingleInstanceErrorFactSafe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $ErrorRecord,
        [Parameter(Mandatory)][string] $Stage,
        [string] $RunToken = '',
        [string] $OwnedRoot = '',
        [scriptblock] $Convert = $null
    )
    try {
        if ($null -ne $Convert) { return & $Convert $ErrorRecord $Stage $RunToken $OwnedRoot }
        return ConvertTo-SingleInstanceErrorFact -ErrorRecord $ErrorRecord -Stage $Stage -RunToken $RunToken -OwnedRoot $OwnedRoot
    }
    catch {
        $message = ''
        try { $message = [string] $ErrorRecord.Exception.Message } catch { $message = '' }
        try {
            return New-SingleInstanceFallbackErrorFact -Stage $Stage -Message $message -RunToken $RunToken -OwnedRoot $OwnedRoot
        }
        catch { }
        # The static literal is the end of the line: it cannot fail, cannot recurse and cannot leak.
        return [pscustomobject] [ordered] @{
            stage     = $Stage
            errorType = 'unavailable'
            errorId   = 'fact-conversion-failed'
            category  = ''
            position  = ''
            stack     = ''
            message   = 'fact-conversion-failed'
        }
    }
}

# The hand over a failing factory leaves beside its original error, read as its own unit: the report
# is validated by shape, its release attempt joins the attempt list, and a hand over that cannot be
# read is recorded rather than thrown. The caller stays responsible for its own business error.
function Import-SingleInstanceFactoryReleaseReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $ErrorRecord,
        [Parameter(Mandatory)] $ReleaseAttempts,
        [Parameter(Mandatory)] $Failures,
        [string] $RunToken = '',
        [string] $OwnedRoot = ''
    )
    $received = [pscustomobject] @{ report = $null; attempt = $null }
    try {
        $handedOver = $ErrorRecord.Exception.Data['TraceFold.ControlWindowRelease']
        if ($null -ne $handedOver) {
            # A hand over has to look like a report before anything is claimed from it; anything else
            # is an unreadable hand over, recorded rather than read.
            if ($null -eq $handedOver.PSObject.Properties['releaseAttempted'] -or
                $null -eq $handedOver.PSObject.Properties['attempt']) {
                throw 'single.instance.factory_handover_unreadable'
            }
            $received.report = $handedOver
            if ([bool] $handedOver.releaseAttempted -and $null -ne $handedOver.attempt) {
                $received.attempt = $handedOver.attempt
                [void] $ReleaseAttempts.Add($handedOver.attempt)
            }
        }
    }
    catch {
        $Failures.Add((New-SingleInstanceErrorFactSafe -ErrorRecord $_ -Stage 'factory-release-report-read' -RunToken $RunToken -OwnedRoot $OwnedRoot))
    }
    return $received
}

# The safety net as its own unit, so the paths that never reached the release step are drivable as
# they really run. It takes an object only when it was not already confirmed released - a confirmed
# release is never repeated - and every call is guarded: a failure here is recorded as its own fact
# and never replaces the business error.
function Invoke-SingleInstanceSafetyNet {
    [CmdletBinding()]
    param(
        $ControlWindow,
        [Parameter(Mandatory)][bool] $AlreadyClosed,
        [Parameter(Mandatory)] $ReleaseAttempts,
        [Parameter(Mandatory)] $Failures,
        [string] $RunToken = '',
        [string] $OwnedRoot = ''
    )
    $outcome = [pscustomobject] @{ closed = [bool] $AlreadyClosed; attempted = $false; readBack = ''; failure = '' }
    if ($null -eq $ControlWindow -or $AlreadyClosed) { return $outcome }
    try {
        $safety = Complete-SingleInstanceControlRelease -ControlWindow $ControlWindow
        [void] $ReleaseAttempts.Add((New-SingleInstanceReleaseAttempt -Source 'safety-net' -Stage 'release-safety-net' -Release $safety))
        $outcome.attempted = $true
        $outcome.readBack = [string] $safety.releaseReadBack
        $outcome.failure = [string] $safety.releaseFailure
        if ([bool] $safety.closed) { $outcome.closed = $true }
    }
    catch {
        $Failures.Add((New-SingleInstanceErrorFactSafe -ErrorRecord $_ -Stage 'release-safety-net-guard' -RunToken $RunToken -OwnedRoot $OwnedRoot))
    }
    return $outcome
}

# The second start's exit decision as one unit. A zero exit is the redirect the product promises; a
# non zero exit is its own failure code; an exit that cannot be read is unknown, and the cast that
# would turn a missing value into a zero never happens.
function Assert-SingleInstanceSecondaryExit {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()] $ExitCode)
    if ($null -eq $ExitCode) { throw 'single.instance.secondary_exit_unknown' }
    $value = 0
    try { $value = [int] $ExitCode } catch { throw 'single.instance.secondary_exit_unknown' }
    if ($value -ne 0) { throw ('single.instance.secondary_exit_' + $value) }
}

# The final decision as its own unit, taken only after the release and the required forensics have
# completed: a business pass stays a pass only when the release was confirmed and every required
# document was really written. The business outcome and its code are never rewritten; a release or
# evidence problem turns a pass into its own named failure instead.
function Get-SingleInstanceFinalVerdict {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $BusinessOutcome,
        [Parameter(Mandatory)][string] $BusinessReason,
        [Parameter(Mandatory)][bool] $ReleaseConfirmed,
        [Parameter(Mandatory)][bool] $RequiredEvidenceComplete,
        $ReleaseAttempts
    )
    # A generic object list is never wrapped in @(): on both engines the array subexpression over a
    # List[object] fails with "Argument types do not match", so the collection is used exactly as it
    # was handed over, with a null fallback, and a caller may pass an array, a list or nothing.
    $attempts = $ReleaseAttempts
    if ($null -eq $attempts) { $attempts = @() }
    $status = $BusinessOutcome
    $reason = $BusinessReason
    if ($BusinessOutcome -ceq 'Passed') {
        if (-not $ReleaseConfirmed) {
            $standing = ''
            foreach ($attempt in $attempts) {
                if ($null -eq $attempt) { continue }
                if (-not [string]::IsNullOrWhiteSpace([string] $attempt.releaseFailure)) { $standing = [string] $attempt.releaseFailure }
            }
            if ([string]::IsNullOrWhiteSpace($standing)) { $standing = 'single.instance.control_window_not_released' }
            $status = 'Failed'
            $reason = $standing
        }
        elseif (-not $RequiredEvidenceComplete) {
            $status = 'Failed'
            $reason = 'single.instance.required_evidence_write_failed'
        }
    }
    return [pscustomobject] @{ Status = $status; Reason = $reason }
}

# The exit evidence, written after the release and only on the way out. Each document is guarded on
# its own: a write that fails becomes its own lightweight fact and leaves the required state
# incomplete instead of being swallowed, and there is no retry, no substitute file and no second
# observation that could turn a failed write into a pass. The failure forensics keep the original
# error at the top level and list the release action error, the read back error and every exit
# failure as their own facts.
function Write-SingleInstanceExitEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)] $Artifacts,
        [Parameter(Mandatory)] $Failures,
        [string] $RunToken = '',
        [string] $OwnedRoot = '',
        $SuccessEvidence,
        $BusinessErrorRecord,
        [string] $BusinessStage = '',
        $ReleaseAttempts,
        $FactoryReleaseReport
    )
    $outcome = [pscustomobject] @{
        controlReleaseWritten  = $false
        stageErrorWritten      = $false
        successEvidenceWritten = $false
        requiredComplete       = $true
    }
    # Handed over as it is, never wrapped in @(): the array subexpression over a List[object] fails
    # with "Argument types do not match" on both engines. An array, a list or nothing all work.
    $attempts = $ReleaseAttempts
    if ($null -eq $attempts) { $attempts = @() }

    # 1. the release record, whenever a release really was attempted. Every attempt keeps its own
    # action and read back facts, so a later attempt never erases an earlier failure.
    if ($attempts.Count -gt 0) {
        $attemptDocuments = @()
        foreach ($attempt in $attempts) {
            $closeFact = $null
            if ($null -ne $attempt.closeErrorRecord) {
                $closeFact = ConvertTo-SingleInstanceErrorFact -ErrorRecord $attempt.closeErrorRecord -Stage ([string] $attempt.stage) -RunToken $RunToken -OwnedRoot $OwnedRoot
            }
            $disposeFact = $null
            if ($null -ne $attempt.disposeErrorRecord) {
                $disposeFact = ConvertTo-SingleInstanceErrorFact -ErrorRecord $attempt.disposeErrorRecord -Stage ([string] $attempt.stage) -RunToken $RunToken -OwnedRoot $OwnedRoot
            }
            $readBackFact = $null
            if ($null -ne $attempt.readBackErrorRecord) {
                $readBackFact = ConvertTo-SingleInstanceErrorFact -ErrorRecord $attempt.readBackErrorRecord -Stage ([string] $attempt.stage) -RunToken $RunToken -OwnedRoot $OwnedRoot
            }
            $attemptDocuments += [ordered] @{
                source              = [string] $attempt.source
                stage               = [string] $attempt.stage
                closed              = [bool] $attempt.closed
                releaseReadBack     = [string] $attempt.releaseReadBack
                releaseFailure      = [string] $attempt.releaseFailure
                actionError         = [string] $attempt.actionError
                closeError          = [string] $attempt.closeError
                disposeError        = [string] $attempt.disposeError
                readBackError       = [string] $attempt.readBackError
                isDisposedReadBack  = [bool] $attempt.isDisposedReadBack
                closeErrorFact      = $closeFact
                disposeErrorFact    = $disposeFact
                readBackErrorFact   = $readBackFact
            }
        }
        $lastAttempt = $attempts[$attempts.Count - 1]
        $record = [ordered] @{
            stage                  = [string] $lastAttempt.stage
            # The confirmed fact every other reader derives its answer from: the disposition of the
            # last attempt, which is what the final result's ReleaseConfirmed carries too.
            confirmed              = [bool] $lastAttempt.closed
            releaseAttempted       = $true
            releaseReadBack        = [string] $lastAttempt.releaseReadBack
            releaseFailure         = [string] $lastAttempt.releaseFailure
            isDisposedReadBack     = [bool] $lastAttempt.isDisposedReadBack
            finalStatusFromRelease = $(if ([bool] $lastAttempt.closed) { 'Passed' } else { 'Failed' })
            attemptCount           = $attempts.Count
            attempts               = $attemptDocuments
        }
        try {
            [IO.File]::WriteAllText((Join-Path $OutputDirectory 'single-instance-control-release.json'),
                (ConvertTo-Json -InputObject $record -Depth 6), [Text.UTF8Encoding]::new($false))
            $outcome.controlReleaseWritten = $true
            $Artifacts.Add('single-instance-control-release.json')
        }
        catch {
            $Failures.Add((New-SingleInstanceErrorFactSafe -ErrorRecord $_ -Stage 'control-release-write' -RunToken $RunToken -OwnedRoot $OwnedRoot))
            $outcome.requiredComplete = $false
        }
    }

    # 2. the failure forensics, whenever the run did not pass.
    if ($null -ne $BusinessErrorRecord) {
        $business = ConvertTo-SingleInstanceErrorFact -ErrorRecord $BusinessErrorRecord -Stage $BusinessStage -RunToken $RunToken -OwnedRoot $OwnedRoot
        $document = [ordered] @{
            stage                = [string] $business.stage
            errorType            = [string] $business.errorType
            errorId              = [string] $business.errorId
            category             = [string] $business.category
            position             = [string] $business.position
            stack                = [string] $business.stack
            message              = [string] $business.message
            release              = $null
            factoryRelease       = $null
            releaseActionError   = $null
            releaseReadBackError = $null
            closeObservations    = $BusinessErrorRecord.Exception.Data['TraceFold.SingleInstanceClose']
            # Cast, not wrapped: the shared failure list is a generic object list and @() over one
            # fails on both engines.
            exitFailures         = [object[]] $Failures
        }
        if ($attempts.Count -gt 0) {
            $lastAttempt = $attempts[$attempts.Count - 1]
            $document.release = [ordered] @{
                attempted    = $true
                attemptCount = $attempts.Count
                confirmed    = [bool] $lastAttempt.closed
                lastStage    = [string] $lastAttempt.stage
                lastReadBack = [string] $lastAttempt.releaseReadBack
                lastFailure  = [string] $lastAttempt.releaseFailure
            }
            if ($null -ne $lastAttempt.actionErrorRecord) {
                $document.releaseActionError = ConvertTo-SingleInstanceErrorFact -ErrorRecord $lastAttempt.actionErrorRecord -Stage ([string] $lastAttempt.stage) -RunToken $RunToken -OwnedRoot $OwnedRoot
            }
            if ($null -ne $lastAttempt.readBackErrorRecord) {
                $document.releaseReadBackError = ConvertTo-SingleInstanceErrorFact -ErrorRecord $lastAttempt.readBackErrorRecord -Stage ([string] $lastAttempt.stage) -RunToken $RunToken -OwnedRoot $OwnedRoot
            }
        }
        if ($null -ne $FactoryReleaseReport) {
            $document.factoryRelease = [ordered] @{
                objectObtained   = [bool] $FactoryReleaseReport.objectObtained
                releaseAttempted = [bool] $FactoryReleaseReport.releaseAttempted
            }
        }
        try {
            [IO.File]::WriteAllText((Join-Path $OutputDirectory 'single-instance-stage-error.json'),
                (ConvertTo-Json -InputObject $document -Depth 8), [Text.UTF8Encoding]::new($false))
            $outcome.stageErrorWritten = $true
            $Artifacts.Add('single-instance-stage-error.json')
        }
        catch {
            $Failures.Add((New-SingleInstanceErrorFactSafe -ErrorRecord $_ -Stage 'stage-error-write' -RunToken $RunToken -OwnedRoot $OwnedRoot))
            $outcome.requiredComplete = $false
        }
    }
    elseif ($null -ne $SuccessEvidence) {
        # 3. the success evidence. A failure here is the difference between a run that proved
        # something and a run that did not, so it is named as its own required document.
        try {
            [IO.File]::WriteAllText((Join-Path $OutputDirectory 'single-instance-safety.json'),
                (ConvertTo-Json -InputObject $SuccessEvidence -Depth 10), [Text.UTF8Encoding]::new($false))
            $outcome.successEvidenceWritten = $true
            $Artifacts.Add('single-instance-safety.json')
        }
        catch {
            $Failures.Add((New-SingleInstanceErrorFactSafe -ErrorRecord $_ -Stage 'success-evidence-write' -RunToken $RunToken -OwnedRoot $OwnedRoot))
            $outcome.requiredComplete = $false
        }
    }

    return $outcome
}

function Get-ProcessWindowHandle {
    param([Parameter(Mandatory)][int] $ProcessId)
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $p) { return [IntPtr]::Zero }
    try { return [IntPtr] $p.MainWindowHandle } catch { return [IntPtr]::Zero }
}

function Wait-ForProcessWindow {
    param(
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][int] $TimeoutSeconds
    )
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $handle = Get-ProcessWindowHandle -ProcessId $ProcessId
        if ($handle -ne [IntPtr]::Zero) { return $handle }
        Start-Sleep -Milliseconds 250
    }
    return [IntPtr]::Zero
}

function Invoke-SingleInstanceObservation {
    param([Parameter(Mandatory)][int] $ProcessId)
    return Invoke-WinAppObservation -Arguments @('ui', 'inspect', '--depth', ([string] $InspectDepth)) `
        -ProcessId $ProcessId -TimeoutSeconds $ObservationTimeoutSeconds -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot
}

# The legal close and the same-root restart as one unit, so every branch of it can be driven without
# a live desktop while the scenario still runs this very unit. It closes the primary through its own
# window, observes its exit on the same held process object, checks only registered identities,
# starts the restart on the same root, observes its window, and waits for its graceful exit too.
# Neither the order nor the arguments are decided anywhere else.
function Invoke-SingleInstanceCloseAndRestart {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int] $PrimaryPid,
        [Parameter(Mandatory)][string] $ExecutablePath,
        [Parameter(Mandatory)][string] $OwnedRoot,
        [Parameter(Mandatory)][string] $RunToken,
        [Parameter(Mandatory)][string] $SupportPath,
        [Parameter(Mandatory)][int] $WaitTimeoutSeconds,
        [Parameter(Mandatory)][int] $InteractiveTimeoutSeconds,
        # Every step is a seam so the refusals can be driven; the defaults are the real calls.
        [scriptblock] $FindProcess = { param($id) Get-Process -Id $id -ErrorAction SilentlyContinue },
        # An injected inventory returns identity records; unrelated records are ignored. The real
        # inventory queries only the registered PIDs, never every process with the product name.
        [scriptblock] $ProcessInventory,
        [scriptblock] $StartScenario = { param($exe, $root, $token, $support) Start-TraceFoldScenario -ExecutablePath $exe -OwnedRoot $root -RunToken $token -SupportPath $support },
        [scriptblock] $WaitWindow = { param($id, $timeout) Wait-ForProcessWindow -ProcessId $id -TimeoutSeconds $timeout },
        [scriptblock] $Observe = { param($id) Invoke-SingleInstanceObservation -ProcessId $id },
        [scriptblock] $Sleep = { param($milliseconds) Start-Sleep -Milliseconds $milliseconds },
        [Parameter(Mandatory)] $PrimaryIdentity,
        [object[]] $OwnedProcessLedger = @()
    )
    function Test-RegisteredIdentity {
        param($Actual, $Expected)
        return ([int] $Actual.ProcessId -eq [int] $Expected.ProcessId -and
            [long] $Actual.StartUtcTicks -eq [long] $Expected.StartUtcTicks -and
            [IO.Path]::GetFullPath([string] $Actual.ExecutablePath) -ieq
                [IO.Path]::GetFullPath([string] $Expected.ExecutablePath))
    }

    function Read-HeldIdentity {
        param($Process)
        # Acquire the handle before reading identity. Closing, waiting and reading the exit code
        # then all use this object; a later PID lookup cannot substitute a different process.
        $null = $Process.Handle
        return [pscustomobject] @{
            ProcessId = [int] $Process.Id
            StartUtcTicks = $Process.StartTime.ToUniversalTime().Ticks
            ExecutablePath = [string] $Process.MainModule.FileName
        }
    }

    function Close-OwnedInstance {
        param($Identity, [string] $Role)
        $prefix = 'single.instance.' + $Role
        if ([int] $Identity.ProcessId -le 0 -or [long] $Identity.StartUtcTicks -le 0 -or
            -not [IO.Path]::IsPathRooted([string] $Identity.ExecutablePath) -or
            [IO.Path]::GetFullPath([string] $Identity.ExecutablePath) -ine [IO.Path]::GetFullPath($ExecutablePath)) {
            throw ($prefix + '_identity_invalid')
        }
        $facts = [pscustomobject] @{
            role = $Role; processId = [int] $Identity.ProcessId; startUtcTicks = [long] $Identity.StartUtcTicks
            identityMatched = $false; closeAccepted = $false; exitObserved = $false; exitCode = $null
        }
        [void] $closeFacts.Add($facts)
        $process = $null
        try {
            $process = & $FindProcess ([int] $Identity.ProcessId)
            if ($null -eq $process) { throw ($prefix + '_close_rejected') }
            if (-not (Test-RegisteredIdentity -Actual (Read-HeldIdentity $process) -Expected $Identity)) {
                throw ($prefix + '_identity_mismatch')
            }
            $facts.identityMatched = $true
            if ($process.HasExited) { throw ($prefix + '_exited_before_close') }
            $facts.closeAccepted = [bool] $process.CloseMainWindow()
            if (-not $facts.closeAccepted) { throw ($prefix + '_close_rejected') }
            $clock = [Diagnostics.Stopwatch]::StartNew()
            while (-not $process.WaitForExit(0)) {
                if ($clock.Elapsed.TotalSeconds -ge $WaitTimeoutSeconds) { throw ($prefix + '_close_timeout') }
                & $Sleep 250
            }
            if (-not $process.HasExited) { throw ($prefix + '_exit_unobserved') }
            $facts.exitObserved = $true
            $exitValue = $null
            try { $exitValue = $process.ExitCode } catch { $exitValue = $null }
            if ($exitValue -isnot [int]) { throw ($prefix + '_exit_unknown') }
            $facts.exitCode = $exitValue
            if ($exitValue -ne 0) { throw ($prefix + '_exit_' + $exitValue) }
            return $facts
        }
        finally { if ($null -ne $process) { $process.Dispose() } }
    }

    function Read-OwnedResidue {
        param([object[]] $Ledger)
        $observed = @()
        if ($null -ne $ProcessInventory) {
            $observed = @(& $ProcessInventory $Ledger)
        }
        else {
            foreach ($identity in $Ledger) {
                $live = & $FindProcess ([int] $identity.ProcessId)
                if ($null -eq $live) { continue }
                try {
                    if (-not $live.HasExited) { $observed += Read-HeldIdentity $live }
                }
                finally { $live.Dispose() }
            }
        }
        foreach ($actual in $observed) {
            foreach ($identity in $Ledger) {
                if (Test-RegisteredIdentity -Actual $actual -Expected $identity) { $actual; break }
            }
        }
    }

    $closeFacts = New-Object 'System.Collections.Generic.List[object]'
    try {
        if ([int] $PrimaryIdentity.ProcessId -ne $PrimaryPid) { throw 'single.instance.primary_identity_mismatch' }
        $owned = @($PrimaryIdentity) + @($OwnedProcessLedger)
        $primaryClose = Close-OwnedInstance -Identity $PrimaryIdentity -Role 'primary'
        $residue = @(Read-OwnedResidue -Ledger $owned)
        if ($residue.Count -gt 0) { throw 'single.instance.residue_after_close' }

        $restart = & $StartScenario $ExecutablePath $OwnedRoot $RunToken $SupportPath
        $restartPid = [int] $restart.ProcessId
        $restartWindow = & $WaitWindow $restartPid $InteractiveTimeoutSeconds
        if ([IntPtr] $restartWindow -eq [IntPtr]::Zero) { throw 'single.instance.restart_window_missing' }
        $restartObservation = & $Observe $restartPid
        if ([string] $restartObservation.Status -cne 'Passed') { throw 'single.instance.restart_unresponsive' }
        $restartClose = Close-OwnedInstance -Identity $restart -Role 'restart'
        $restartResidue = @(Read-OwnedResidue -Ledger ($owned + @($restart)))
        if ($restartResidue.Count -gt 0) { throw 'single.instance.residue_after_restart_close' }
        return [pscustomobject] @{
            closeAccepted            = $primaryClose.closeAccepted
            primaryClose             = $primaryClose
            residueCount             = $residue.Count
            restartPid               = $restartPid
            restartWindowHandle      = ([IntPtr] $restartWindow).ToInt64()
            restartObservationStatus = [string] $restartObservation.Status
            restartExecutable        = $ExecutablePath
            restartOwnedRoot         = $OwnedRoot
            restartClose             = $restartClose
            restartResidueCount       = $restartResidue.Count
        }
    }
    catch {
        $_.Exception.Data['TraceFold.SingleInstanceClose'] = [object[]] $closeFacts
        throw
    }
}

$artifacts = New-Object System.Collections.Generic.List[string]
# Declared before the try so the finally can always see whatever this run owns.
$controlWindow = $null
$controlWindowClosed = $false
# The last stage the business flow reached. It is assigned as the run advances and can only be read,
# never decided, by the failure path, so a failure can say where it happened without changing what
# failed. The cleanup sequence records its own stages inside the release attempts; the two are never
# mixed, and no stage name is used to claim an action that has not happened.
$scenarioStage = 'start'
# The business outcome and the original ErrorRecord are saved by the catch and only read afterwards,
# so neither the release nor the forensics can overwrite them.
$businessOutcome = 'NotRun'
$businessReason = ''
$businessErrorRecord = $null
# Every release attempt that really ran, in order. The record and the evidence are built from this
# list rather than from a single value that a later attempt would overwrite, and a confirmed release
# is never repeated.
$releaseAttempts = New-Object System.Collections.Generic.List[object]
$releaseReadBack = ''
$releaseFailure = ''
# The release facts a failing factory hands over beside its own original error.
$factoryReleaseReport = $null
# The failures of the exit evidence, each recorded as its own lightweight fact. This is what makes a
# failed write visible: it is listed in the returned record and it decides that the run cannot pass.
$exitFailures = New-Object System.Collections.Generic.List[object]
# What the exit evidence step really achieved. A missing outcome leaves the required state
# incomplete rather than assumed.
$exitEvidence = $null
$successEvidence = $null
$primaryPidOut = $null
$secondaryExitOut = $null
$restartPidOut = $null

try {
    if (-not [IO.File]::Exists($ExecutablePath)) { throw 'single.instance.executable_missing' }
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if ([IO.Path]::GetFileName($exe) -ine 'TraceFold.Media.exe') { throw 'single.instance.executable_missing' }
    $root = [IO.Path]::GetFullPath($OwnedRoot)

    # A host without an interactive desktop cannot exercise activation at all.
    if (-not (& $DesktopCheck)) { throw 'single.instance.desktop_unavailable' }
    $scenarioStage = 'desktop-verified'

    # --- 1. primary instance ---------------------------------------------------------------------
    $primary = Start-TraceFoldScenario -ExecutablePath $exe -OwnedRoot $root -RunToken $RunToken `
        -SupportPath $SupportPath -StartProcess $LaunchProcess
    $primaryPid = [int] $primary.ProcessId
    $primaryPidOut = $primaryPid
    $scenarioStage = 'primary-started'
    $primaryWindow = Wait-ForProcessWindow -ProcessId $primaryPid -TimeoutSeconds $InteractiveTimeoutSeconds
    if ($primaryWindow -eq [IntPtr]::Zero) { throw 'single.instance.primary_window_missing' }

    # The expected database comes from the product's own path contract, not from whatever the
    # inventory happened to return first, and the primary instance has to have created it.
    $expectedDbPath = Get-SingleInstanceExpectedDbPath -Root $root
    # Each inventory is wrapped, so a single-element result keeps a stable collection shape and the
    # counts below are safe to read under strict mode.
    $dbBefore = @(Get-SingleInstanceDbInventory -Root $root)
    $dbSidecarBefore = @(Get-SingleInstanceDbSidecarInventory -Root $root)
    $libraryBefore = @(Get-SingleInstanceLibraryInventory -Root $root)
    Assert-SingleInstanceDatabaseState -ExpectedDatabasePath $expectedDbPath -Databases $dbBefore

    # The identity ledger already carries the launched process's real creation time, so the scenario
    # reports that value rather than the moment it sampled its own clock.
    $primaryStartedUtc = [DateTime]::new([long] $primary.StartUtcTicks, [DateTimeKind]::Utc).ToString('o')

    $preObservation = Invoke-SingleInstanceObservation -ProcessId $primaryPid

    # --- controlled non-foreground pre-state ----------------------------------------------------
    # The probe type is created by the driver's foreground check when that check runs, so the check
    # is driven before any handle is sampled. This initialisation is not the pre-state and not the
    # activation evidence, and it produces nothing.
    Initialize-SingleInstanceForegroundProbe -ProcessId $primaryPid -ForegroundCheck $ForegroundCheck

    # The pre-state is positively bound, not merely "not the primary": the foreground handle must be
    # exactly the control window that this scenario owns, and that handle's owner must be the control
    # window's own verified owner. Any other window, including one of the primary's own, is rejected.
    $controlWindow = & $EstablishNonForeground
    $controlHandle = [IntPtr] $controlWindow.Handle
    if ($controlHandle -eq [IntPtr]::Zero) { throw 'single.instance.control_window_missing' }
    $controlOwnerPid = Get-WindowOwnerProcessId -Handle $controlHandle
    # The controlled object's ownership is checked against an independent reference, not against a
    # second read of the same handle: a window this scenario owns has to belong to this very harness
    # process, and the recorded owner is therefore cross checked against the running process id.
    if ($controlOwnerPid -ne $PID) { throw 'single.instance.control_window_not_harness_owned' }
    $preForegroundHandle = Get-ForegroundWindowHandle
    $preForegroundPid = Get-WindowOwnerProcessId -Handle $preForegroundHandle
    Assert-SingleInstancePreState -PrimaryPid $primaryPid -ControlHandle $controlHandle.ToInt64() `
        -ControlOwnerPid $controlOwnerPid -PreForegroundHandle $preForegroundHandle.ToInt64() `
        -PreForegroundOwnerPid $preForegroundPid
    $scenarioStage = 'pre-state-verified'

    $preState = [pscustomobject] @{
        primaryPid            = $primaryPid
        primaryWindow         = $primaryWindow.ToInt64()
        primaryStartedUtc     = $primaryStartedUtc
        observationStatus     = [string] $preObservation.Status
        controlWindowHandle   = $controlHandle.ToInt64()
        controlWindowOwnerPid = $controlOwnerPid
        preForegroundHandle   = $preForegroundHandle.ToInt64()
        preForegroundOwnerPid = $preForegroundPid
    }

    # --- 2. second start on the same owned root --------------------------------------------------
    # The second process gets the same executable and the same owned data root, and nothing else:
    # the point is that the product itself decides to redirect it. Its exit code is the product's,
    # not a wrapper's.
    # The second start goes through the same ownership path as the first: it is registered in the
    # ledger with its pid, creation time and executable path, so a second process that fails to exit
    # on its own is owned by the existing verified cleanup chain. Nothing is killed directly here and
    # an unverified pid is never terminated.
    $script:singleInstanceSecondary = $null
    $secondaryStartBlock = {
        param($launchExe, $launchEnv)
        $info = New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName = $launchExe
        $info.UseShellExecute = $false
        # The environment this block was handed reaches the child through this process's own block
        # for the duration of the start and is restored immediately afterwards. The process start
        # info's Environment member does not exist on the older runtime and its EnvironmentVariables
        # member reads back as null on a first access there, so the child inherits the values
        # instead, and nothing is left modified after the start.
        $restore = @{}
        foreach ($key in $launchEnv.Keys) {
            $restore[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, [string] $launchEnv[$key], 'Process')
        }
        try {
            $proc = [Diagnostics.Process]::Start($info)
        }
        finally {
            foreach ($key in $restore.Keys) {
                [Environment]::SetEnvironmentVariable($key, $restore[$key], 'Process')
            }
        }
        $script:singleInstanceSecondary = $proc
        return [pscustomobject] @{
            ProcessId      = $proc.Id
            StartUtcTicks  = $proc.StartTime.ToUniversalTime().Ticks
            ExecutablePath = $launchExe
        }
    }
    $secondaryArgv = @($exe)
    $secondary = Start-TraceFoldScenario -ExecutablePath $exe -OwnedRoot $root -RunToken $RunToken `
        -SupportPath $SupportPath -StartProcess $secondaryStartBlock
    $secondaryPid = [int] $secondary.ProcessId
    $secondaryStartUtc = [DateTime]::new([long] $secondary.StartUtcTicks, [DateTimeKind]::Utc).ToString('o')
    $secondaryProc = $script:singleInstanceSecondary
    $exited = $false
    if ($null -ne $secondaryProc) {
        try { $exited = $secondaryProc.WaitForExit($SecondaryTimeoutSeconds * 1000) } catch { $exited = $false }
    }
    if (-not $exited) {
        # The process is in the ledger, so the scenario does not stop it itself: the verified cleanup
        # chain re-checks pid, creation time and path before anything is stopped.
        throw 'single.instance.secondary_resident'
    }
    # The exit value is taken as a fact first and only turned into a number once it really is one: a
    # value that is missing or unreadable is unknown, never a zero produced by a cast.
    $secondaryExitValue = $null
    try { $secondaryExitValue = $secondaryProc.ExitCode } catch { $secondaryExitValue = $null }
    Assert-SingleInstanceSecondaryExit -ExitCode $secondaryExitValue
    $secondaryExit = [int] $secondaryExitValue
    $secondaryExitOut = $secondaryExit
    $secondaryEndUtc = [DateTime]::UtcNow.ToString('o')
    $scenarioStage = 'secondary-exited'

    # --- 3. no second database, no other library -------------------------------------------------
    $dbAfter = @(Get-SingleInstanceDbInventory -Root $root)
    $dbSidecarAfter = @(Get-SingleInstanceDbSidecarInventory -Root $root)
    $libraryAfter = @(Get-SingleInstanceLibraryInventory -Root $root)
    $secondStartDatabase = Assert-SingleInstanceSecondStartDatabase -ExpectedDatabasePath $expectedDbPath `
        -DatabasesBefore $dbBefore -DatabasesAfter $dbAfter -LibraryBefore $libraryBefore -LibraryAfter $libraryAfter
    $dbCreated = @($secondStartDatabase.DatabasesCreated)
    $libraryCreated = @($secondStartDatabase.LibraryCreated)

    # --- 4. the primary window was activated -----------------------------------------------------
    # The foreground is read first and only read: nothing here activates a window, clicks it or
    # asks the driver to bring it forward, because that would manufacture the very fact being
    # proved. An inspect that succeeds later is recorded as observability only.
    # One sample, not two: the foreground handle is read once and the process behind that very handle
    # is asked for. The primary is the foreground only if that handle is the primary's own window and
    # that handle's owner is the primary process. Nothing here brings any window forward, clicks it
    # or asks the driver to activate it, because that would manufacture the fact being proved.
    $foregroundHandle = Get-ForegroundWindowHandle
    $foregroundOwnerPid = Get-WindowOwnerProcessId -Handle $foregroundHandle
    $primaryWindowAfter = Get-ProcessWindowHandle -ProcessId $primaryPid
    Assert-SingleInstanceActivated -PrimaryPid $primaryPid -PrimaryWindowHandle $primaryWindow.ToInt64() `
        -PrimaryWindowAfterHandle $primaryWindowAfter.ToInt64() `
        -ForegroundHandle $foregroundHandle.ToInt64() -ForegroundOwnerPid $foregroundOwnerPid
    if ($primaryWindowAfter.ToInt64() -ne $primaryWindow.ToInt64()) {
        throw 'single.instance.primary_window_changed'
    }
    $foregroundMatches = ($foregroundOwnerPid -eq $primaryPid)
    $scenarioStage = 'activated'

    $postObservation = Invoke-SingleInstanceObservation -ProcessId $primaryPid
    if ([string] $postObservation.Status -cne 'Passed') { throw 'single.instance.primary_unresponsive' }

    # The controlled window has done its job. Its release is performed through the same unit every
    # other path uses and its result is READ BACK, not assumed from the absence of an exception: the
    # object is asked whether it is really disposed. A release that did not take effect is unknown
    # and this scenario cannot pass. The attempt is recorded here; the record file itself is written
    # on the way out, after the release, together with the other exit evidence.
    # The stage name belongs to the cleanup sequence and must not claim the restart: the restart has
    # not been verified yet at this point. The business stage is tracked separately and is only moved
    # once the restart really has been verified.
    $release = Complete-SingleInstanceControlRelease -ControlWindow $controlWindow
    [void] $releaseAttempts.Add((New-SingleInstanceReleaseAttempt -Source 'success-path' -Stage 'release-after-second-start' -Release $release))
    $controlWindowClosed = [bool] $release.closed
    $releaseReadBack = [string] $release.releaseReadBack
    $releaseFailure = [string] $release.releaseFailure
    if (-not $controlWindowClosed) { throw $releaseFailure }
    $scenarioStage = 'released'

    # --- 5. legal close, then restart on the same owned root -------------------------------------
    # The whole sequence runs as its own unit, so every branch of it can be driven; the unit keeps
    # the real calls as its defaults and the restart still starts through this scenario's own launch
    # seam, on the same executable and the same owned root.
    $restartFacts = Invoke-SingleInstanceCloseAndRestart -PrimaryPid $primaryPid -ExecutablePath $exe -OwnedRoot $root `
        -RunToken $RunToken -SupportPath $SupportPath -WaitTimeoutSeconds $WaitTimeoutSeconds `
        -InteractiveTimeoutSeconds $InteractiveTimeoutSeconds `
        -PrimaryIdentity $primary -OwnedProcessLedger @($secondary) `
        -StartScenario { param($restartExe, $restartRoot, $restartToken, $restartSupport) Start-TraceFoldScenario -ExecutablePath $restartExe -OwnedRoot $restartRoot -RunToken $restartToken -SupportPath $restartSupport -StartProcess $LaunchProcess }
    $closeAccepted = [bool] $restartFacts.closeAccepted
    $restartPid = [int] $restartFacts.restartPid
    $restartWindow = [IntPtr] $restartFacts.restartWindowHandle
    $restartObservationStatus = [string] $restartFacts.restartObservationStatus
    # The unit returns only after both the restart observation and its graceful exit succeeded.
    # Runner cleanup remains a failure safety net; it cannot supply this successful close fact.
    $scenarioStage = 'restart-closed'

    # --- evidence --------------------------------------------------------------------------------
    $evidence = [ordered] @{
        scenarioId              = $ScenarioId
        runToken                = $RunToken
        ownedRoot               = $root
        executablePath          = $exe
        primaryPid              = $primaryPid
        primaryWindowHandle     = $primaryWindow.ToInt64()
        primaryWindowAfter      = $primaryWindowAfter.ToInt64()
        primaryStartedUtc       = $preState.primaryStartedUtc
        preObservationStatus    = [string] $preObservation.Status
        postObservationStatus   = [string] $postObservation.Status
        secondaryArgv            = $secondaryArgv
        secondaryDataRootEnv    = $root
        secondaryPid             = $secondaryPid
        secondaryStartUtc        = $secondaryStartUtc
        secondaryEndUtc          = $secondaryEndUtc
        secondaryExitCode        = $secondaryExit
        foregroundWindowHandle   = $foregroundHandle.ToInt64()
        foregroundMatchesPrimary = $foregroundMatches
        foregroundOwnerPid       = $foregroundOwnerPid
        controlWindowHandle      = $preState.controlWindowHandle
        controlWindowOwnerPid    = $preState.controlWindowOwnerPid
        controlWindowClosed      = $controlWindowClosed
        controlReleaseReadBack   = $releaseReadBack
        controlReleaseFailure    = $releaseFailure
        # The stage the release itself ran at; the business stage is a separate fact.
        controlReleaseStage      = [string] $releaseAttempts[$releaseAttempts.Count - 1].stage
        businessStage            = $scenarioStage
        preForegroundHandle      = $preState.preForegroundHandle
        preForegroundOwnerPid    = $preState.preForegroundOwnerPid
        preStateNote             = 'a window this harness owns was brought forward first; the primary was required to be out of the foreground before the second start'
        expectedDatabasePath     = $expectedDbPath
        databasesBefore          = @($dbBefore)
        databasesAfter           = @($dbAfter)
        databasesCreated         = @($dbCreated)
        dbSidecarsBefore         = @($dbSidecarBefore)
        dbSidecarsAfter          = @($dbSidecarAfter)
        dbSidecarNote            = 'the wal and shm files are the main database sidecars; they are listed separately, are not counted as a second database, and are not frozen'
        libraryFilesBefore       = @($libraryBefore).Count
        libraryFilesAfter        = @($libraryAfter).Count
        libraryFilesCreated      = @($libraryCreated)
        closeAccepted            = $closeAccepted
        primaryClose             = $restartFacts.primaryClose
        residueAfterClose        = [int] $restartFacts.residueCount
        restartPid               = $restartPid
        restartWindowHandle      = ([IntPtr] $restartWindow).ToInt64()
        restartObservationStatus = $restartObservationStatus
        restartClose             = $restartFacts.restartClose
        residueAfterRestartClose = [int] $restartFacts.restartResidueCount
        activationProof          = 'a controlled window owned by this harness took the foreground before the second start; afterwards the foreground handle was read once, that handle owner is the primary pid and the handle is the primary window'
        observabilityNote        = 'the inspect calls show the instance is observable and are not offered as the activation proof'
        databaseScopeNote        = 'only the one expected database and the absence of a second one are asserted; no byte-freeze of the main database, WAL or SHM'
        singleInstanceKeyScope   = 'the product key is a fixed constant without the data root; no machine-wide or cross-session claim is made here'
    }
    # The document is prepared here and written on the way out, after the release, beside the other
    # exit evidence: the result is only built once every required write really happened.
    $successEvidence = $evidence
    $primaryPidOut = $primaryPid
    $secondaryExitOut = $secondaryExit
    $restartPidOut = $restartPid
    $businessOutcome = 'Passed'
    $businessReason = 'single.instance.verified'
}
catch {
    # The business outcome and the original ErrorRecord are saved; nothing is written and no result
    # is built here. The release and the exit evidence happen in the finally, each guarded on its
    # own, and the single final result is only built after they really completed.
    $businessErrorRecord = $_
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or $code -notlike 'single.instance.*') {
        $cause = ConvertTo-UiAcceptanceSlug -Value ([string] $code)
        if ($cause.Length -gt 120) { $cause = $cause.Substring(0, 120) }
        $code = 'single.instance.unexpected'
        if (-not [string]::IsNullOrWhiteSpace($cause)) { $code = $code + '.' + $cause }
    }
    $businessReason = $code
    $businessOutcome = if ($blockedFailureCodes -ccontains $code) { 'Blocked' } else { 'Failed' }

    # The factory hands its release facts over beside the original failure. They are read through
    # their own unit: the attempt joins the list, a release the factory confirmed is carried into
    # the same field every other path uses - so the final field, the persisted record and the
    # evidence all agree about it - and a hand over that cannot be read is recorded rather than
    # swallowed. None of this can change the business error, and a confirmed release never turns a
    # business failure green.
    $received = Import-SingleInstanceFactoryReleaseReport -ErrorRecord $_ -ReleaseAttempts $releaseAttempts `
        -Failures $exitFailures -RunToken $RunToken -OwnedRoot $OwnedRoot
    $factoryReleaseReport = $received.report
    if ($null -ne $received.attempt) {
        $releaseReadBack = [string] $received.attempt.releaseReadBack
        $releaseFailure = [string] $received.attempt.releaseFailure
        if ([bool] $received.attempt.closed) { $controlWindowClosed = $true }
    }
}
finally {
    # The safety net: whatever this run owns is released on every path, including a failure of the
    # pre-state, of the second start or of an observation. It runs only when the object exists and
    # was not already confirmed released, so a confirmed release is never repeated, and it reads the
    # released fact back rather than assuming the call was enough. Every call in the finally carries
    # its own guard: a failure here must never replace the business error saved above.
    $safety = Invoke-SingleInstanceSafetyNet -ControlWindow $controlWindow -AlreadyClosed $controlWindowClosed `
        -ReleaseAttempts $releaseAttempts -Failures $exitFailures -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ([bool] $safety.attempted) {
        $releaseReadBack = [string] $safety.readBack
        $releaseFailure = [string] $safety.failure
    }
    $controlWindowClosed = [bool] $safety.closed
    # The required exit evidence is written after the release and before the result is built. This
    # call is guarded as well; a failure here is recorded as a fact and leaves the required state
    # incomplete, so the final result cannot claim a pass.
    try {
        $exitEvidence = Write-SingleInstanceExitEvidence -OutputDirectory $OutputDirectory -Artifacts $artifacts `
            -Failures $exitFailures -RunToken $RunToken -OwnedRoot $OwnedRoot `
            -SuccessEvidence $successEvidence -BusinessErrorRecord $businessErrorRecord `
            -BusinessStage $scenarioStage -ReleaseAttempts $releaseAttempts -FactoryReleaseReport $factoryReleaseReport
    }
    catch {
        $exitFailures.Add((New-SingleInstanceErrorFactSafe -ErrorRecord $_ -Stage 'exit-evidence' -RunToken $RunToken -OwnedRoot $OwnedRoot))
    }
}

# --- the single final result ---------------------------------------------------------------------
# Built only here, after the release and the required forensics really completed: the saved business
# outcome, the release facts and the write failures are decided together, and the release and exit
# facts are announced beside the business code instead of replacing it.
$requiredEvidenceComplete = $false
if ($null -ne $exitEvidence) { $requiredEvidenceComplete = [bool] $exitEvidence.requiredComplete }
$verdict = Get-SingleInstanceFinalVerdict -BusinessOutcome $businessOutcome -BusinessReason $businessReason `
    -ReleaseConfirmed $controlWindowClosed -RequiredEvidenceComplete $requiredEvidenceComplete `
    -ReleaseAttempts $releaseAttempts
return [pscustomobject] @{
    ScenarioId        = $ScenarioId
    Status            = [string] $verdict.Status
    Reason            = [string] $verdict.Reason
    Artifacts         = @($artifacts)
    PrimaryPid        = $primaryPidOut
    SecondaryExitCode = $secondaryExitOut
    RestartPid        = $restartPidOut
    ReleaseAttempted  = ($releaseAttempts.Count -gt 0)
    ReleaseConfirmed  = [bool] $controlWindowClosed
    ReleaseFailure    = [string] $releaseFailure
    EvidenceComplete  = $requiredEvidenceComplete
    # Cast, not wrapped: this is a generic object list and @() over one fails on both engines.
    ExitFailures      = [object[]] $exitFailures
}
