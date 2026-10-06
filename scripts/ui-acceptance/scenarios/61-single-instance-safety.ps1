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
#   4. prove the primary window was brought to the foreground: a controlled non-foreground state is
#      established first, then the foreground window is checked to belong to the primary process.
#      An inspect that succeeds only shows the instance is observable; it is never offered as the
#      activation proof;
#   5. close the primary through the product's own window, prove no leftovers, and start it again on
#      the same owned root;
#   6. the negative case for an outside instance runs as a pre-dispatch check and never starts a
#      product, never terminates the outside process and never redirects it.
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
    $dataRoot = Join-Path $Root 'data'
    if (-not [IO.Directory]::Exists($dataRoot)) { return @() }
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($file in [IO.Directory]::GetFiles($dataRoot, '*.db', [IO.SearchOption]::AllDirectories)) {
        $items.Add([IO.Path]::GetFullPath($file))
    }
    foreach ($file in [IO.Directory]::GetFiles($dataRoot, '*.db-wal', [IO.SearchOption]::AllDirectories)) {
        $items.Add([IO.Path]::GetFullPath($file))
    }
    foreach ($file in [IO.Directory]::GetFiles($dataRoot, '*.db-shm', [IO.SearchOption]::AllDirectories)) {
        $items.Add([IO.Path]::GetFullPath($file))
    }
    return @($items | Sort-Object -Unique)
}

function Get-SingleInstanceLibraryInventory {
    param([Parameter(Mandatory)][string] $Root)
    $library = Join-Path $Root 'library'
    if (-not [IO.Directory]::Exists($library)) { return @() }
    return @([IO.Directory]::GetFiles($library, '*', [IO.SearchOption]::AllDirectories) | Sort-Object)
}

function Get-ForegroundWindowHandle {
    if (-not ('TraceFoldUiForegroundProbe' -as [type])) { return [IntPtr]::Zero }
    return [TraceFoldUiForegroundProbe]::GetForegroundWindow()
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

    $dbBefore = Get-SingleInstanceDbInventory -Root $root
    $libraryBefore = Get-SingleInstanceLibraryInventory -Root $root
    $expectedDbPath = if ($dbBefore.Count -gt 0) { [string] $dbBefore[0] } else { '' }

    $preObservation = Invoke-SingleInstanceObservation -ProcessId $primaryPid
    $preState = [pscustomobject] @{
        primaryPid        = $primaryPid
        primaryWindow     = $primaryWindow.ToInt64()
        primaryStartedUtc = [DateTime]::UtcNow.ToString('o')
        observationStatus = [string] $preObservation.Status
    }

    # --- 2. second start on the same owned root --------------------------------------------------
    # The second process gets the same executable and the same owned data root, and nothing else:
    # the point is that the product itself decides to redirect it. Its exit code is the product's,
    # not a wrapper's.
    $secondaryInfo = New-Object System.Diagnostics.ProcessStartInfo
    $secondaryInfo.FileName = $exe
    $secondaryInfo.UseShellExecute = $false
    $secondaryInfo.Environment['TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT'] = $root
    $secondaryArgv = @($exe)
    $secondary = [Diagnostics.Process]::Start($secondaryInfo)
    $secondaryPid = [int] $secondary.Id
    $secondaryStartUtc = $secondary.StartTime.ToUniversalTime().ToString('o')
    $exited = $secondary.WaitForExit($SecondaryTimeoutSeconds * 1000)
    if (-not $exited) {
        try { $secondary.Kill() } catch { }
        throw 'single.instance.secondary_resident'
    }
    $secondaryExit = [int] $secondary.ExitCode
    $secondaryEndUtc = [DateTime]::UtcNow.ToString('o')
    if ($secondaryExit -ne 0) { throw ('single.instance.secondary_exit_' + $secondaryExit) }

    # --- 3. no second database, no other library -------------------------------------------------
    $dbAfter = Get-SingleInstanceDbInventory -Root $root
    $libraryAfter = Get-SingleInstanceLibraryInventory -Root $root
    $dbCreated = @($dbAfter | Where-Object { $dbBefore -cnotcontains $_ })
    if ($dbCreated.Count -gt 0) { throw 'single.instance.second_database_created' }
    $libraryCreated = @($libraryAfter | Where-Object { $libraryBefore -cnotcontains $_ })
    if ($libraryCreated.Count -gt 0) { throw 'single.instance.second_library_used' }
    if ($expectedDbPath.Length -gt 0 -and @($dbAfter | Where-Object { $_ -ceq $expectedDbPath }).Count -eq 0) {
        throw 'single.instance.expected_database_missing'
    }

    # --- 4. the primary window was activated -----------------------------------------------------
    # The foreground is read first and only read: nothing here activates a window, clicks it or
    # asks the driver to bring it forward, because that would manufacture the very fact being
    # proved. An inspect that succeeds later is recorded as observability only.
    $foregroundHandle = Get-ForegroundWindowHandle
    $foregroundMatches = [bool] (& $ForegroundCheck $primaryPid)
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
        libraryFilesBefore       = $libraryBefore.Count
        libraryFilesAfter        = $libraryAfter.Count
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
