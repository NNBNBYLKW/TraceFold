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
#   4. read the foreground after the second start and require the foreground window's owner to be
#      the primary process with the primary window handle unchanged. An inspect that succeeds only
#      shows the instance is observable; it is never offered as the activation proof;
#   5. close the primary through the product's own window, prove no leftovers, and start it again on
#      the same owned root.
#
# NOT yet implemented, and therefore not claimed by this file:
#   * establishing a controlled non-foreground pre-state before the second start. The foreground is
#     currently only read, so the activation step proves the post-state without having set up the
#     pre-state, and the counter-examples for that (already foreground, another window of the same
#     process, non-foreground and not activated) are not covered here;
#   * the negative case for an outside instance. It is not implemented in this file at all: proving
#     Blocked with zero dispatches and zero new starts needs a separate controlled fixture with its
#     own ownership ledger, which lives outside this scenario.
#
# Bans: no coordinates, no fixed sleeps, no termination by process name, no direct database access,
# no AppActivate-style foreground coaxing, and no reuse of another scenario's root.
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
    [scriptblock] $LaunchProcess,
    [scriptblock] $DesktopCheck = { Test-UiInteractiveDesktop },
    [scriptblock] $ForegroundCheck = { param($id) Test-UiForegroundProcess -ProcessId $id }
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
    # The comma keeps a single result a one-element array instead of unrolling it to a scalar, so
    # every consumer can rely on a stable collection shape.
    return ,@($items | Sort-Object -Unique)
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
    # The comma keeps a single result a one-element array instead of unrolling it to a scalar, so
    # every consumer can rely on a stable collection shape.
    return ,@($items | Sort-Object -Unique)
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
    return ,@([IO.Directory]::GetFiles($library, '*', [IO.SearchOption]::AllDirectories) | Sort-Object)
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

$artifacts = New-Object System.Collections.Generic.List[string]

try {
    if (-not [IO.File]::Exists($ExecutablePath)) { throw 'single.instance.executable_missing' }
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if ([IO.Path]::GetFileName($exe) -ine 'TraceFold.Media.exe') { throw 'single.instance.executable_missing' }
    $root = [IO.Path]::GetFullPath($OwnedRoot)

    # A host without an interactive desktop cannot exercise activation at all.
    if (-not (& $DesktopCheck)) { throw 'single.instance.desktop_unavailable' }

    # --- 1. primary instance ---------------------------------------------------------------------
    $primary = Start-TraceFoldScenario -ExecutablePath $exe -OwnedRoot $root -RunToken $RunToken `
        -SupportPath $SupportPath -StartProcess $LaunchProcess
    $primaryPid = [int] $primary.ProcessId
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
    if (@($dbBefore).Count -ne 1) { throw ('single.instance.expected_database_count_' + [string] @($dbBefore).Count) }
    if (@($dbBefore) -cnotcontains $expectedDbPath) { throw 'single.instance.expected_database_missing' }

    # The identity ledger already carries the launched process's real creation time, so the scenario
    # reports that value rather than the moment it sampled its own clock.
    $primaryStartedUtc = [DateTime]::new([long] $primary.StartUtcTicks, [DateTimeKind]::Utc).ToString('o')

    $preObservation = Invoke-SingleInstanceObservation -ProcessId $primaryPid
    $preState = [pscustomobject] @{
        primaryPid        = $primaryPid
        primaryWindow     = $primaryWindow.ToInt64()
        primaryStartedUtc = $primaryStartedUtc
        observationStatus = [string] $preObservation.Status
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
        foreach ($key in $launchEnv.Keys) { $info.Environment[$key] = [string] $launchEnv[$key] }
        $proc = [Diagnostics.Process]::Start($info)
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
    $secondaryExit = [int] $secondaryProc.ExitCode
    $secondaryEndUtc = [DateTime]::UtcNow.ToString('o')
    if ($secondaryExit -ne 0) { throw ('single.instance.secondary_exit_' + $secondaryExit) }

    # --- 3. no second database, no other library -------------------------------------------------
    $dbAfter = @(Get-SingleInstanceDbInventory -Root $root)
    $dbSidecarAfter = @(Get-SingleInstanceDbSidecarInventory -Root $root)
    $libraryAfter = @(Get-SingleInstanceLibraryInventory -Root $root)
    # Still exactly the one expected database: a second file, or a different one, is the failure.
    if (@($dbAfter).Count -ne 1) { throw ('single.instance.expected_database_count_' + [string] @($dbAfter).Count) }
    if (@($dbAfter) -cnotcontains $expectedDbPath) { throw 'single.instance.second_database_used' }
    $dbCreated = @($dbAfter | Where-Object { @($dbBefore) -cnotcontains $_ })
    if ($dbCreated.Count -gt 0) { throw 'single.instance.second_database_created' }
    $libraryCreated = @($libraryAfter | Where-Object { @($libraryBefore) -cnotcontains $_ })
    if ($libraryCreated.Count -gt 0) { throw 'single.instance.second_library_used' }

    # --- 4. the primary window was activated -----------------------------------------------------
    # The foreground is read first and only read: nothing here activates a window, clicks it or
    # asks the driver to bring it forward, because that would manufacture the very fact being
    # proved. An inspect that succeeds later is recorded as observability only.
    # The foreground check runs first: it is the read-only call that also makes the probe type
    # available. Nothing here brings a window forward, clicks it or asks the driver to activate it,
    # because that would manufacture the very fact being proved. The handle is then read from the
    # same foreground window the check just measured.
    $foregroundMatches = [bool] (& $ForegroundCheck $primaryPid)
    $foregroundHandle = Get-ForegroundWindowHandle
    $primaryWindowAfter = Get-ProcessWindowHandle -ProcessId $primaryPid
    if (-not $foregroundMatches) { throw 'single.instance.primary_not_activated' }
    if ($primaryWindowAfter -eq [IntPtr]::Zero) { throw 'single.instance.primary_window_missing' }
    if ($primaryWindowAfter.ToInt64() -ne $primaryWindow.ToInt64()) {
        throw 'single.instance.primary_window_changed'
    }

    $postObservation = Invoke-SingleInstanceObservation -ProcessId $primaryPid
    if ([string] $postObservation.Status -cne 'Passed') { throw 'single.instance.primary_unresponsive' }

    # --- 5. legal close, then restart on the same owned root -------------------------------------
    $closeAccepted = $false
    $primaryProcess = Get-Process -Id $primaryPid -ErrorAction SilentlyContinue
    if ($null -ne $primaryProcess) {
        try { $closeAccepted = $primaryProcess.CloseMainWindow() } catch { $closeAccepted = $false }
    }
    if (-not $closeAccepted) { throw 'single.instance.primary_close_rejected' }
    $closed = $false
    $deadline = [DateTime]::UtcNow.AddSeconds($WaitTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $still = Get-Process -Id $primaryPid -ErrorAction SilentlyContinue
        if ($null -eq $still) { $closed = $true; break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $closed) { throw 'single.instance.primary_close_timeout' }
    $residue = @(Get-Process -Name 'TraceFold.Media' -ErrorAction SilentlyContinue)
    if ($residue.Count -gt 0) { throw 'single.instance.residue_after_close' }

    $restart = Start-TraceFoldScenario -ExecutablePath $exe -OwnedRoot $root -RunToken $RunToken `
        -SupportPath $SupportPath -StartProcess $LaunchProcess
    $restartPid = [int] $restart.ProcessId
    $restartWindow = Wait-ForProcessWindow -ProcessId $restartPid -TimeoutSeconds $InteractiveTimeoutSeconds
    if ($restartWindow -eq [IntPtr]::Zero) { throw 'single.instance.restart_window_missing' }
    $restartObservation = Invoke-SingleInstanceObservation -ProcessId $restartPid
    if ([string] $restartObservation.Status -cne 'Passed') { throw 'single.instance.restart_unresponsive' }

    # The restart is a new process on the same root: a brand new window is expected here, and the
    # earlier window identity is never reused to stand in for it.
    $restartProcess = Get-Process -Id $restartPid -ErrorAction SilentlyContinue
    if ($null -ne $restartProcess) {
        try { [void] $restartProcess.CloseMainWindow() } catch { }
    }

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
        residueAfterClose        = $residue.Count
        restartPid               = $restartPid
        restartWindowHandle      = $restartWindow.ToInt64()
        restartObservationStatus = [string] $restartObservation.Status
        activationProof          = 'controlled foreground read after the second start; the foreground window owner is the primary pid and the primary window handle is unchanged'
        observabilityNote        = 'the inspect calls show the instance is observable and are not offered as the activation proof'
        databaseScopeNote        = 'only the one expected database and the absence of a second one are asserted; no byte-freeze of the main database, WAL or SHM'
        singleInstanceKeyScope   = 'the product key is a fixed constant without the data root; no machine-wide or cross-session claim is made here'
    }
    $evidenceJson = ConvertTo-Json -InputObject $evidence -Depth 10
    $evidenceName = 'single-instance-safety.json'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $evidenceName), $evidenceJson, [Text.UTF8Encoding]::new($false))
    $artifacts.Add($evidenceName)

    return [pscustomobject] @{
        ScenarioId        = $ScenarioId
        Status            = 'Passed'
        Reason            = 'single.instance.verified'
        PrimaryPid        = $primaryPid
        SecondaryExitCode = $secondaryExit
        RestartPid        = $restartPid
        Artifacts         = @($artifacts)
    }
}
catch {
    $code = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($code) -or $code -notlike 'single.instance.*') {
        $cause = ConvertTo-UiAcceptanceSlug -Value ([string] $code)
        if ($cause.Length -gt 120) { $cause = $cause.Substring(0, 120) }
        $code = 'single.instance.unexpected'
        if (-not [string]::IsNullOrWhiteSpace($cause)) { $code = $code + '.' + $cause }
    }
    $status = if ($blockedFailureCodes -ccontains $code) { 'Blocked' } else { 'Failed' }
    return [pscustomobject] @{ ScenarioId = $ScenarioId; Status = $status; Reason = $code; Artifacts = @($artifacts) }
}
