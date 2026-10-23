# TraceFold Stage 5CC - Task 13 visual, viewport, keyboard, drawer and dialog matrix.
#
# The matrix is the Task 13 contract, not a screenshot tour:
#   * Both themes (light and dark), all four pages, the three DIP viewports every gate must prove,
#     and every state/dialog the plan names.
#   * Every required fact is read from the live UIA tree; a screenshot never makes a state pass.
#   * The compact drawer is opened by its own button, Escape closes it, and focus is read from UIA
#     after every keyboard injection.
#   * The Windows app theme is switched only through the support CLI's theme command. The command
#     surface is fixed here; the reviewed coordinator that owns journaling and the registry write
#     is a separate integration point. When it is absent the matrix blocks instead of guessing.
#
# Static-stage limits, stated so a later run does not read them as results:
#   * No product, driver, support CLI, registry or UI call is made by the static review; every
#     behavioural claim below is NotRun until a scheduled real visual reservation.
#   * CurrentOnly never writes the registry and returns NotRun, because one mode cannot be a full
#     visual pass.
#
# Bans: no coordinates, no sleeps, no process termination by name, no shell folder picker, no
# direct database or service access, and no reuse of another scenario's root.

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
    [ValidateSet('Both', 'CurrentOnly')][string] $ThemeMatrix = 'Both',
    # A fixture-bound run binds its own journal and its own adapter root; both default to the
    # production values. The licence is a separate switch: a fixture root without it is refused
    # before any child starts, so neither parameter alone can move the run off the real journal.
    [AllowEmptyString()][string] $ThemeJournalPath = '',
    [AllowEmptyString()][string] $FixtureRoot = '',
    [switch] $AllowFixture,
    # The caller states that the product and window under observation are a controlled stand-in.
    # Absent, the scenario makes no claim either way: the report keeps the provenance unknown
    # rather than dressing a stand-in run up as a real visual pass.
    [switch] $Synthetic,
    [int] $WaitTimeoutSeconds = 30,
    [int] $RowWaitTimeoutSeconds = 45,
    [int] $ProcessingWaitSeconds = 180,
    [int] $DiagnosticsWaitSeconds = 120,
    [int] $ObservationTimeoutSeconds = 20,
    [int] $InspectDepth = 24,
    [int] $MutationTimeoutMs = 5000,
    [int] $PixelTolerance = 2,
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
Import-Module (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.VisualGate.psm1') -DisableNameChecking

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
# The journal path, the command wrapper and the response validators come from the shared gate
# module: the entry's pre-preflight gate and this scenario must never drift into two answers.
# A partial override is refused here, before the journal is resolved, classified or restored.
$themeOverride = Test-UiAcceptanceThemeOverride -JournalPath $ThemeJournalPath -FixtureRoot $FixtureRoot `
    -AllowFixture:$AllowFixture -RequireExplicitJournal
if (-not $themeOverride.ok) { throw [string] $themeOverride.code }
$journalPath = Get-UiAcceptanceThemeJournalPath -JournalPath $ThemeJournalPath
# CurrentOnly is a read-only theme observation: the shared gate still inspects and classifies a
# leftover journal, but it may not restore one.
$allowThemeRestore = $ThemeMatrix -ceq 'Both'
$syntheticFlag = if ($PSBoundParameters.ContainsKey('Synthetic')) { [bool] $Synthetic } else { $null }

$blockedFailureCodes = @(
    'visual.desktop_unavailable',
    'visual.foreground_unavailable',
    'visual.fixture_missing',
    'visual.fixture_mismatch',
    'visual.fixture_target',
    'visual.contract_missing',
    'visual.contract_empty',
    'visual.tooling_missing',
    'visual.product_unavailable',
    'visual.window_unavailable',
    'visual.inspect_unavailable',
    'visual.native_probe_unavailable',
    'visual.resize_refused',
    'visual.theme_coordinator_unavailable',
    'visual.theme_override_incomplete',
    'theme.override_incomplete',
    'theme.override_fixture_root',
    'theme.override_journal_outside_fixture',
    'visual.theme_owner_identity_unavailable',
    'visual.theme_binding_unavailable',
    'visual.theme_journal_blocked',
    'visual.pixel_sample_unavailable',
    'visual.focus_observation_unavailable',
    'visual.observation_unavailable'
)

$pageSpecs = @{
    inbox = @{
        Nav = 'NavInbox'; Wait = 'InboxGrid'; Root = 'InboxPageRoot'
        Keys = @('InboxPageRoot','InboxTitle','InboxSearchBox','InboxStateFilter','InboxRefreshButton','InboxGrid')
        Inspector = 'InspectorPane'; Open = 'InboxInspectorOpenButton'
    }
    library = @{
        Nav = 'NavLibrary'; Wait = 'LibraryGrid'; Root = 'LibraryPageRoot'
        Keys = @('LibraryPageRoot','LibrarySearchBox','LibrarySortField','LibraryFilterButton','LibraryGrid')
        Inspector = 'LibraryInspectorPane'; Open = 'LibraryInspectorOpenButton'
    }
    taxonomy = @{
        Nav = 'NavTaxonomy'; Wait = 'TaxonomyTermList'; Root = 'TaxonomyPageRoot'
        Keys = @('TaxonomyPageRoot','TaxonomyKindList','TaxonomySearchBox','TaxonomyRefreshButton','TaxonomyTermList')
        Inspector = 'TaxonomyInspectorPane'; Open = 'TaxonomyInspectorOpenButton'
    }
    settings = @{
        Nav = 'NavSettings'; Wait = 'SettingsTabs'; Root = 'SettingsPageRoot'
        # The folder fields and the save action can sit below the fold at 1024x720 and are reached
        # by scrolling; the page chrome and tab strip are the controls that must fit as rendered.
        Keys = @('SettingsPageRoot','SettingsTabs','FolderSettingsTab')
        Inspector = ''; Open = ''
    }
}

$requiredStates = @(
    @{ Page = 'inbox'; State = 'empty-selection' },
    @{ Page = 'inbox'; State = 'selected' },
    @{ Page = 'inbox'; State = 'processing-queued' },
    @{ Page = 'inbox'; State = 'processing-ready' },
    @{ Page = 'inbox'; State = 'disabled-primary-action' },
    @{ Page = 'inbox'; State = 'drawer-open' },
    @{ Page = 'inbox'; State = 'drawer-closed' },
    @{ Page = 'library'; State = 'selected' },
    @{ Page = 'taxonomy'; State = 'selection' },
    @{ Page = 'settings'; State = 'folder-form' },
    @{ Page = 'settings'; State = 'validation-error' },
    @{ Page = 'settings'; State = 'diagnostics-running' },
    @{ Page = 'settings'; State = 'diagnostics-complete' }
)

$dialogStates = @(
    @{ Page = 'inbox'; State = 'batch-metadata-dialog'; Dialog = 'BatchMetadataDialog'; Scroll = 'BatchMetadataScrollRoot'; Close = '' },
    @{ Page = 'inbox'; State = 'conflict-dialog'; Dialog = 'ConflictComparisonDialog'; Scroll = 'ConflictScrollRoot'; Close = '' },
    @{ Page = 'library'; State = 'library-batch-edit-dialog'; Dialog = 'LibraryBatchEditDialog'; Scroll = 'LibraryBatchEditScrollRoot'; Close = 'LibraryBatchCancelButton' },
    @{ Page = 'taxonomy'; State = 'taxonomy-merge-dialog'; Dialog = 'TaxonomyMergeDialog'; Scroll = 'TaxonomyMergeScrollRoot'; Close = 'TaxonomyMergeCancelButton' }
)

$script:productId = 0
$script:productIdentity = $null
$script:contract = $null
$script:folders = @{}
$script:entries = New-Object 'System.Collections.Generic.List[object]'
$script:themeFacts = New-Object 'System.Collections.Generic.List[object]'
$script:keyboardFacts = New-Object 'System.Collections.Generic.List[object]'
$script:focusPropertySeen = $false
$script:themeRestoreFailure = ''
$script:themePendingRestore = $false
# The round the scenario is in, and the explicit stop reason that refuses a following theme.
$script:themeRound = $null
$script:themeStopReason = ''
# The direct-invocation journal gate result, recorded in the matrix document when it ran.
$journalGateFacts = $null
$script:artifacts = New-Object 'System.Collections.Generic.List[string]'
$script:productHash = ''
$script:screenshotRoot = Join-Path $OwnedRoot 'screenshots'
$accepted = @()
$windowId = ''

function Get-VisualIndex {
    param([object[]] $Nodes)
    $index = @{}
    foreach ($node in @($Nodes)) {
        $id = [string] (Get-UiAcceptanceNodeProperty -Node $node -Name 'automationId')
        if ([string]::IsNullOrWhiteSpace($id)) { continue }
        if ($index.ContainsKey($id)) { $index[$id] = @($index[$id]) + @($node) }
        else { $index[$id] = @($node) }
    }
    return ,$index
}

function Read-VisualNodes {
    return @(Read-UiAcceptanceTree -ProcessId $script:productId -WinAppPath $WinAppPath `
        -RunToken $RunToken -OwnedRoot $OwnedRoot -FailureCode 'visual.inspect_unavailable' `
        -Depth $InspectDepth -TimeoutSeconds $ObservationTimeoutSeconds)
}

function Find-VisualId {
    param([Parameter(Mandatory)][object[]] $Nodes, [Parameter(Mandatory)][string] $Id,
        [Parameter(Mandatory)][string] $Code)
    $node = Find-UiAcceptanceUniqueNode -Nodes $Nodes -AutomationId $Id -FailureCode $Code
    if ($null -eq $node) { throw $Code }
    return $node
}

function Assert-VisualOnscreen {
    param([Parameter(Mandatory)] $Node, [Parameter(Mandatory)][string] $Code)
    if ((Get-UiAcceptanceNodeProperty -Node $Node -Name 'isOffscreen') -cne 'False') { throw $Code }
}

function Invoke-VisualAction {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $Code)
    $result = Invoke-WinAppMutation -StepId $Step -Arguments $Arguments -ProcessId $script:productId `
        -WinAppPath $WinAppPath -TimeoutMs $MutationTimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($result.Status -cne 'Passed') {
        throw ($Code + '.' + (ConvertTo-UiAcceptanceSlug -Value $Step))
    }
    return $result
}

function Invoke-VisualKey {
    param([Parameter(Mandatory)][string] $Step, [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $Code)
    $result = Invoke-WinAppKeyboard -StepId $Step -Arguments $Arguments -ProcessId $script:productId `
        -WinAppPath $WinAppPath -ForegroundCheck $ForegroundCheck -DesktopCheck $DesktopCheck `
        -TimeoutMs $MutationTimeoutMs -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($result.Status -ceq 'Blocked') { throw 'visual.foreground_unavailable' }
    if ($result.Status -cne 'Passed') { throw ($Code + '.' + (ConvertTo-UiAcceptanceSlug -Value $Step)) }
    return $result
}

function Wait-Visual {
    param([Parameter(Mandatory)][string[]] $Arguments, [Parameter(Mandatory)][int] $Seconds)
    $observation = Invoke-WinAppObservation -Arguments $Arguments -ProcessId $script:productId `
        -TimeoutSeconds $Seconds -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.TimedOut) { return $false }
    if ($observation.Status -cne 'Passed') { throw 'visual.observation_unavailable' }
    if ($null -ne $observation.Data -and
        $null -ne $observation.Data.PSObject.Properties['found'] -and
        $observation.Data.found -ne $true) {
        # The driver completed the query in time, but its observed answer was "not present".
        # Returning that as a successful wait would let the caller enter a state it never saw.
        return $false
    }
    return $true
}

function Read-VisualValue {
    param([Parameter(Mandatory)][string] $AutomationId, [Parameter(Mandatory)][string] $Code)
    $observation = Invoke-WinAppObservation -Arguments @('ui','get-value',$AutomationId) `
        -ProcessId $script:productId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($observation.Status -cne 'Passed' -or $null -eq $observation.Data) { throw $Code }
    if ($null -eq $observation.Data.PSObject.Properties['text']) { throw $Code }
    return [string] $observation.Data.text
}

function Resize-VisualWindow {
    param([Parameter(Mandatory)][int] $WidthDip, [Parameter(Mandatory)][int] $HeightDip)
    $raw = & $SupportPath window resize --process-id $script:productId --width-dip $WidthDip `
        --height-dip $HeightDip --root $OwnedRoot --token $RunToken
    if ($LASTEXITCODE -ne 0) { throw 'visual.resize_refused' }
    $fact = ($raw | Out-String | ConvertFrom-Json)
    Assert-UiAcceptanceFields -InputObject $fact -Required @(
        'widthPixels','heightPixels','clientWidthPixels','clientHeightPixels')
    $widthPixels = [int] $fact.widthPixels
    $heightPixels = [int] $fact.heightPixels
    if ($widthPixels -le 0 -or $heightPixels -le 0) { throw 'visual.resize_refused' }
    if ([Math]::Abs(($widthPixels / $WidthDip) - ($heightPixels / $HeightDip)) -gt 0.02) {
        throw 'visual.resize_refused'
    }
    if ([Math]::Abs([int] $fact.clientWidthPixels - $widthPixels) -gt $PixelTolerance -or
        [Math]::Abs([int] $fact.clientHeightPixels - $heightPixels) -gt $PixelTolerance) {
        throw 'visual.resize_refused'
    }
    $scale = $widthPixels / [double] $WidthDip
    return [pscustomobject] @{
        ClientWidthPixels = [int] $fact.clientWidthPixels
        ClientHeightPixels = [int] $fact.clientHeightPixels
        Scale = $scale
        Dpi = [Math]::Round(96.0 * $scale, 2)
    }
}

function Get-VisualPngFacts {
    param([Parameter(Mandatory)][string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    Assert-UiPngBytes -Bytes $bytes
    [uint32]$width = ([uint32]$bytes[16] -shl 24) -bor ([uint32]$bytes[17] -shl 16) -bor
        ([uint32]$bytes[18] -shl 8) -bor [uint32]$bytes[19]
    [uint32]$height = ([uint32]$bytes[20] -shl 24) -bor ([uint32]$bytes[21] -shl 16) -bor
        ([uint32]$bytes[22] -shl 8) -bor [uint32]$bytes[23]
    return [pscustomobject] @{
        Width = [int] $width; Height = [int] $height
        BitDepth = [int] $bytes[24]; ColorType = [int] $bytes[25]
        Bytes = [long] $bytes.Length
    }
}

<#
.SYNOPSIS
    Samples the screenshot with a dependency-free PNG decoder.

.DESCRIPTION
    Pixel evidence must not depend on System.Drawing being present in the PowerShell host, because
    the toolkit runs on both hosts. The decoder reads one IHDR/IDAT image (8-bit grey, RGB or RGBA,
    no interlace), unfilters it and averages a 5x5 grid of the centre region. Any unsupported or
    corrupt input comes back as state 'unknown' rather than as a made-up colour.
#>
function Get-VisualPixelSample {
    param([Parameter(Mandatory)][string] $Path)

    if (-not ('TraceFoldVisualPixelSampler' -as [type])) {
        try {
            try { Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
            Add-Type -TypeDefinition @'
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class TraceFoldVisualPixelSampler
{
    public static string Sample(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 33 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 ||
                bytes[3] != 71)
            {
                return "unknown|not-png";
            }

            int offset = 8;
            int width = 0;
            int height = 0;
            int bitDepth = 0;
            int colorType = 0;
            int interlace = 0;
            MemoryStream idat = new MemoryStream();
            while (offset + 12 <= bytes.Length)
            {
                int length = (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
                    (bytes[offset + 2] << 8) | bytes[offset + 3];
                string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                if (length < 0 || offset + 12 + length > bytes.Length)
                {
                    return "unknown|bad-chunk";
                }
                if (type == "IHDR")
                {
                    width = (bytes[offset + 8] << 24) | (bytes[offset + 9] << 16) |
                        (bytes[offset + 10] << 8) | bytes[offset + 11];
                    height = (bytes[offset + 12] << 24) | (bytes[offset + 13] << 16) |
                        (bytes[offset + 14] << 8) | bytes[offset + 15];
                    bitDepth = bytes[offset + 16];
                    colorType = bytes[offset + 17];
                    interlace = bytes[offset + 20];
                }
                else if (type == "IDAT")
                {
                    idat.Write(bytes, offset + 8, length);
                }
                else if (type == "IEND")
                {
                    break;
                }
                offset += length + 12;
            }

            if (width <= 0 || height <= 0 || bitDepth != 8 || interlace != 0 ||
                (colorType != 0 && colorType != 2 && colorType != 6))
            {
                return "unknown|unsupported";
            }

            byte[] compressed = idat.ToArray();
            if (compressed.Length < 6)
            {
                return "unknown|no-idat";
            }

            byte[] data = null;
            using (MemoryStream input = new MemoryStream(compressed, 2, compressed.Length - 6))
            using (DeflateStream deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                deflate.CopyTo(output);
                data = output.ToArray();
            }
            if (data == null)
            {
                return "unknown|no-data";
            }

            int bytesPerPixel = colorType == 6 ? 4 : (colorType == 2 ? 3 : 1);
            int stride = width * bytesPerPixel;
            if (data.Length < height * (stride + 1))
            {
                return "unknown|short-data";
            }

            byte[] pixels = new byte[height * stride];
            int position = 0;
            for (int row = 0; row < height; row++)
            {
                int filter = data[position++];
                int rowStart = row * stride;
                for (int column = 0; column < stride; column++)
                {
                    int left = column >= bytesPerPixel ? pixels[rowStart + column - bytesPerPixel] : 0;
                    int up = row > 0 ? pixels[rowStart - stride + column] : 0;
                    int upLeft = row > 0 && column >= bytesPerPixel
                        ? pixels[rowStart - stride + column - bytesPerPixel] : 0;
                    int value = data[position++];
                    switch (filter)
                    {
                        case 0: break;
                        case 1: value = (value + left) & 255; break;
                        case 2: value = (value + up) & 255; break;
                        case 3: value = (value + ((left + up) >> 1)) & 255; break;
                        case 4:
                            int estimate = left + up - upLeft;
                            int leftDistance = Math.Abs(estimate - left);
                            int upDistance = Math.Abs(estimate - up);
                            int upLeftDistance = Math.Abs(estimate - upLeft);
                            int predictor = leftDistance <= upDistance && leftDistance <= upLeftDistance
                                ? left : (upDistance <= upLeftDistance ? up : upLeft);
                            value = (value + predictor) & 255;
                            break;
                        default:
                            return "unknown|bad-filter";
                    }
                    pixels[rowStart + column] = (byte) value;
                }
            }

            double total = 0;
            int count = 0;
            for (int gridY = 1; gridY <= 5; gridY++)
            {
                int y = (height - 1) * gridY / 6;
                for (int gridX = 1; gridX <= 5; gridX++)
                {
                    int x = (width - 1) * gridX / 6;
                    int index = y * stride + x * bytesPerPixel;
                    double red;
                    double green;
                    double blue;
                    if (colorType == 0)
                    {
                        red = green = blue = pixels[index];
                    }
                    else
                    {
                        red = pixels[index];
                        green = pixels[index + 1];
                        blue = pixels[index + 2];
                    }
                    total += (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255.0;
                    count++;
                }
            }

            return "captured|" + (total / count).ToString("0.####", CultureInfo.InvariantCulture) +
                "|" + width.ToString(CultureInfo.InvariantCulture) +
                "|" + height.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception error)
        {
            return "unknown|" + error.GetType().Name;
        }
    }
}
'@
        }
        catch {
            return [ordered] @{
                state = 'unknown'
                averageLuminance = $null
                width = 0
                height = 0
                reason = 'add-type-failed'
            }
        }
    }

    $sample = [TraceFoldVisualPixelSampler]::Sample($Path)
    $parts = @(([string] $sample).Split('|'))
    if ($parts.Count -ge 2 -and $parts[0] -ceq 'captured' -and $parts.Count -eq 4) {
        return [ordered] @{
            state = 'captured'
            averageLuminance = [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
            width = [int]::Parse($parts[2], [Globalization.CultureInfo]::InvariantCulture)
            height = [int]::Parse($parts[3], [Globalization.CultureInfo]::InvariantCulture)
        }
    }
    return [ordered] @{
        state = 'unknown'
        averageLuminance = $null
        width = 0
        height = 0
        reason = $(if ($parts.Count -ge 2) { [string] $parts[1] } else { 'unknown' })
    }
}

function Get-VisualEvidenceNodes {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Nodes)
    $rows = @()
    foreach ($node in @($Nodes)) {
        $rows += [pscustomobject] @{
            automationId = [string] (Get-UiAcceptanceNodeProperty -Node $node -Name 'automationId')
            controlType = [string] (Get-UiAcceptanceNodeProperty -Node $node -Name 'type')
            name = [string] (Get-UiAcceptanceNodeProperty -Node $node -Name 'name')
            enabled = ([string] (Get-UiAcceptanceNodeProperty -Node $node -Name 'isEnabled')) -ceq 'True'
            offscreen = ([string] (Get-UiAcceptanceNodeProperty -Node $node -Name 'isOffscreen')) -ceq 'True'
            bounds = [pscustomobject] @{
                x = [int] (Get-UiAcceptanceNodeProperty -Node $node -Name 'x')
                y = [int] (Get-UiAcceptanceNodeProperty -Node $node -Name 'y')
                width = [int] (Get-UiAcceptanceNodeProperty -Node $node -Name 'width')
                height = [int] (Get-UiAcceptanceNodeProperty -Node $node -Name 'height')
            }
        }
    }
    return ,$rows
}

function Add-VisualEntry {
    param(
        [Parameter(Mandatory)][string] $Theme,
        [Parameter(Mandatory)][string] $Viewport,
        [Parameter(Mandatory)][string] $Page,
        [Parameter(Mandatory)][string] $State,
        [Parameter(Mandatory)][string] $Status,
        [string] $Reason = '',
        [string] $Image = '',
        [string] $Uia = '',
        [bool] $Dialog = $false,
        [int] $ClientWidth = 0,
        [int] $ClientHeight = 0,
        [double] $Dpi = 0,
        [int] $PngWidth = 0,
        [int] $PngHeight = 0,
        [int] $PngBitDepth = 0,
        [int] $PngColorType = 0,
        $Pixel = $null
    )
    $entry = [ordered] @{
        theme = $Theme
        viewport = $Viewport
        page = $Page
        state = $State
        dialog = $Dialog
        status = $Status
        synthetic = $syntheticFlag
        reason = $Reason
        image = $Image
        uia = $Uia
        clientWidth = $ClientWidth
        clientHeight = $ClientHeight
        dpi = $Dpi
        pngWidth = $PngWidth
        pngHeight = $PngHeight
        pngBitDepth = $PngBitDepth
        pngColorType = $PngColorType
        pixelState = $(if ($null -ne $Pixel) { [string] $Pixel.state } else { 'not-sampled' })
        averageLuminance = $(if ($null -ne $Pixel) { $Pixel.averageLuminance } else { $null })
        windowAutomationId = 'TraceFoldMainWindow'
        windowStartUtcTicks = $(if ($null -ne $script:productIdentity) { $script:productIdentity.startUtcTicks } else { $null })
        productHash = $script:productHash
        capturedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    $script:entries.Add($entry)
    return $entry
}

function Capture-VisualState {
    param(
        [Parameter(Mandatory)][string] $Prefix,
        [Parameter(Mandatory)][string] $Theme,
        [Parameter(Mandatory)][string] $Viewport,
        [Parameter(Mandatory)][string] $Page,
        [Parameter(Mandatory)][string] $State,
        [Parameter(Mandatory)][string] $Selector,
        [Parameter(Mandatory)][int] $ClientWidth,
        [Parameter(Mandatory)][int] $ClientHeight,
        [Parameter(Mandatory)][double] $Dpi,
        [switch] $Dialog
    )

    $nodes = Read-VisualNodes
    $slug = ConvertTo-UiAcceptanceSlug -Value ($Theme + '-' + $Viewport + '-' + $Page + '-' + $State)
    $uia = Write-UiRedactedTreeEvidence -OutputDirectory $OutputDirectory -EvidenceId $slug `
        -Nodes (Get-VisualEvidenceNodes -Nodes $nodes) -RunToken $RunToken -OwnedRoot $OwnedRoot
    $source = Join-Path $script:screenshotRoot ($slug + '.png')
    [void][IO.Directory]::CreateDirectory($script:screenshotRoot)
    $captured = Invoke-WinAppObservation -Arguments @('ui','screenshot',$Selector,'--output',$source) `
        -ProcessId $script:productId -TimeoutSeconds $ObservationTimeoutSeconds `
        -WinAppPath $WinAppPath -RunToken $RunToken -OwnedRoot $OwnedRoot
    if ($captured.Status -cne 'Passed' -or -not [IO.File]::Exists($source)) {
        throw 'visual.screenshot_unavailable'
    }
    Assert-UiOwnedEvidenceFile -Path $source -OwnedRoot $OwnedRoot
    Assert-UiPngBytes -Bytes ([IO.File]::ReadAllBytes($source))
    $relative = $slug + '.png'
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory $relative), [IO.File]::ReadAllBytes($source))
    $png = Get-VisualPngFacts -Path $source
    $pixel = Get-VisualPixelSample -Path $source
    $entry = Add-VisualEntry -Theme $Theme -Viewport $Viewport -Page $Page -State $State `
        -Status 'Passed' -Image $relative -Uia $uia -Dialog ([bool] $Dialog) `
        -ClientWidth $ClientWidth -ClientHeight $ClientHeight -Dpi $Dpi `
        -PngWidth ([int] $png.Width) -PngHeight ([int] $png.Height) `
        -PngBitDepth ([int] $png.BitDepth) -PngColorType ([int] $png.ColorType) -Pixel $pixel
    if (-not $script:artifacts.Contains($relative)) { $script:artifacts.Add($relative) }
    if (-not $script:artifacts.Contains($uia)) { $script:artifacts.Add($uia) }
    return [pscustomobject] @{ Entry = $entry; Pixel = $pixel; Nodes = $nodes }
}

function Add-VisualNotRun {
    param([string] $Theme, [string] $Viewport, [string] $Page, [string] $State,
        [string] $Reason, [int] $ClientWidth, [int] $ClientHeight, [double] $Dpi, [bool] $Dialog = $false)
    [void] (Add-VisualEntry -Theme $Theme -Viewport $Viewport -Page $Page -State $State `
        -Status 'NotRun' -Reason $Reason -Dialog $Dialog -ClientWidth $ClientWidth `
        -ClientHeight $ClientHeight -Dpi $Dpi)
}

function Assert-VisualBounds {
    param([Parameter(Mandatory)] $Node, [Parameter(Mandatory)][string] $Code,
        [Parameter(Mandatory)][int] $ClientWidth, [Parameter(Mandatory)][int] $ClientHeight)
    $width = [int] (Get-UiAcceptanceNodeProperty -Node $Node -Name 'width')
    $height = [int] (Get-UiAcceptanceNodeProperty -Node $Node -Name 'height')
    if ($width -le 0 -or $height -le 0) { throw $Code }
    if ($width -gt ($ClientWidth + $PixelTolerance) -or
        $height -gt ($ClientHeight + $PixelTolerance)) { throw $Code }
}

<#
.SYNOPSIS
    Requires an element and the window to share one screen-coordinate frame.

.DESCRIPTION
    The UIA rectangle is in screen coordinates, so a control is inside the window only when all
    four edges fall inside the window rectangle. The window rectangle includes the non-client
    area, and the check tolerates the documented pixel slack; it never substitutes a screenshot
    for the observation.
#>
function Assert-VisualInsideWindow {
    param([Parameter(Mandatory)] $Node, [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)][string] $Code)
    $x = [int] (Get-UiAcceptanceNodeProperty -Node $Node -Name 'x')
    $y = [int] (Get-UiAcceptanceNodeProperty -Node $Node -Name 'y')
    $width = [int] (Get-UiAcceptanceNodeProperty -Node $Node -Name 'width')
    $height = [int] (Get-UiAcceptanceNodeProperty -Node $Node -Name 'height')
    $windowX = [int] (Get-UiAcceptanceNodeProperty -Node $Window -Name 'x')
    $windowY = [int] (Get-UiAcceptanceNodeProperty -Node $Window -Name 'y')
    $windowWidth = [int] (Get-UiAcceptanceNodeProperty -Node $Window -Name 'width')
    $windowHeight = [int] (Get-UiAcceptanceNodeProperty -Node $Window -Name 'height')
    if ($x -lt ($windowX - $PixelTolerance) -or $y -lt ($windowY - $PixelTolerance) -or
        ($x + $width) -gt ($windowX + $windowWidth + $PixelTolerance) -or
        ($y + $height) -gt ($windowY + $windowHeight + $PixelTolerance)) {
        throw $Code
    }
}

function Open-VisualPage {
    param([Parameter(Mandatory)][string] $Page, [Parameter(Mandatory)][string] $Prefix)
    $spec = $pageSpecs[$Page]
    Invoke-VisualAction -Step ($Prefix + '.nav') -Arguments @('ui','invoke',[string] $spec.Nav) `
        -Code 'visual.navigation' | Out-Null
    if (-not (Wait-Visual -Arguments @('ui','wait-for',[string] $spec.Wait) -Seconds $WaitTimeoutSeconds)) {
        throw 'visual.page_unavailable'
    }
}

function Assert-VisualPageLayout {
    param([Parameter(Mandatory)][string] $Page, [Parameter(Mandatory)][int] $WidthDip,
        [Parameter(Mandatory)][int] $ClientWidth, [Parameter(Mandatory)][int] $ClientHeight)
    $spec = $pageSpecs[$Page]
    $nodes = Read-VisualNodes
    $windowNode = Find-VisualId -Nodes $nodes -Id $windowId -Code 'visual.window_unavailable'
    $root = Find-VisualId -Nodes $nodes -Id ([string] $spec.Root) -Code 'visual.page_root'
    Assert-VisualOnscreen -Node $root -Code 'visual.page_root'
    Assert-VisualBounds -Node $root -Code 'visual.page_bounds' -ClientWidth $ClientWidth `
        -ClientHeight $ClientHeight
    Assert-VisualInsideWindow -Node $root -Window $windowNode -Code 'visual.page_clipped'
    foreach ($id in @($spec.Keys)) {
        $node = Find-VisualId -Nodes $nodes -Id ([string] $id) -Code 'visual.page_element'
        Assert-VisualBounds -Node $node -Code 'visual.page_bounds' -ClientWidth $ClientWidth `
            -ClientHeight $ClientHeight
        Assert-VisualInsideWindow -Node $node -Window $windowNode -Code 'visual.page_clipped'
    }
    if ([string]::IsNullOrWhiteSpace([string] $spec.Inspector)) { return }
    $pane = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId ([string] $spec.Inspector) `
        -FailureCode 'visual.inspector_state'
    $open = Find-UiAcceptanceUniqueNode -Nodes $nodes -AutomationId ([string] $spec.Open) `
        -FailureCode 'visual.inspector_state'
    if ($WidthDip -le 1024) {
        # Selection must not auto-open the drawer, and the explicit open control must be reachable.
        if ($null -ne $pane -and (Get-UiAcceptanceNodeProperty -Node $pane -Name 'isOffscreen') -ceq 'False') {
            throw 'visual.inspector_state'
        }
        if ($null -eq $open) { throw 'visual.inspector_state' }
        Assert-VisualOnscreen -Node $open -Code 'visual.inspector_state'
    }
    else {
        # Wide layouts keep the inspector inline; the compact open control must not be shown.
        if ($null -eq $pane) { throw 'visual.inspector_state' }
        Assert-VisualOnscreen -Node $pane -Code 'visual.inspector_state'
        if ($null -ne $open -and (Get-UiAcceptanceNodeProperty -Node $open -Name 'isOffscreen') -ceq 'False') {
            throw 'visual.inspector_state'
        }
    }
}

function Select-VisualRow {
    param([Parameter(Mandatory)][string] $Prefix, [Parameter(Mandatory)][string] $ContainerId,
        [Parameter(Mandatory)][string] $Needle, [Parameter(Mandatory)][string] $Code)
    $nodes = Read-VisualNodes
    $rows = @(Get-UiAcceptanceListRowTexts -Nodes $nodes -ContainerId $ContainerId -FailureCode $Code)
    # A keep-both ingest can leave several rows whose text merely contains the fixed filename,
    # so an exact text match is preferred before a containment match is considered at all.
    $exact = @($rows | Where-Object {
        @($_.Texts | Where-Object { ([string] $_) -ceq $Needle }).Count -gt 0
    })
    if ($exact.Count -eq 1) { $matches = $exact }
    else {
        $matches = @($rows | Where-Object {
            $rowTexts = @($_.Texts)
            @($rowTexts | Where-Object { ([string] $_).Contains($Needle) }).Count -gt 0
        })
    }
    if ($matches.Count -ne 1) { throw $Code }
    $selector = [string] $matches[0].Selector
    if ([string]::IsNullOrWhiteSpace($selector) -or $selector -match '[\{\}]') { throw $Code }
    Invoke-VisualAction -Step ($Prefix + '.select-row') -Arguments @('ui','invoke',$selector) -Code $Code | Out-Null
    return $matches[0]
}

function Invoke-VisualProcessSelected {
    param([Parameter(Mandatory)][string] $Prefix)
    Invoke-VisualAction -Step ($Prefix + '.process.start') `
        -Arguments @('ui','invoke','InboxStartProcessingButton') -Code 'visual.processing_start' | Out-Null
    if (-not (Wait-Visual -Arguments @('ui','wait-for','IngestButton','--property','IsEnabled','--value','True') -Seconds $ProcessingWaitSeconds)) {
        throw 'visual.processing_ready'
    }
}

function Test-VisualInboxRow {
    $nodes = Read-VisualNodes
    $rows = @(Get-UiAcceptanceListRowTexts -Nodes $nodes -ContainerId 'InboxGrid' `
        -FailureCode 'visual.inbox_unavailable')
    return [bool] (@($rows | Where-Object {
        @($_.Texts) | Where-Object { ([string] $_).Contains('paired.mp4') }
    }).Count -gt 0)
}

function Ensure-VisualInboxRow {
    param([Parameter(Mandatory)][string] $Prefix)
    if (Test-VisualInboxRow) { return }
    $source = $script:fixtureSources['paired.mp4']
    $destination = Join-Path $script:folders['inbox'] 'paired.mp4'
    if (-not [IO.File]::Exists($destination)) {
        [IO.File]::Copy($source, $destination, $false)
    }
    $audio = $script:fixtureSources['paired.m4a']
    $audioDestination = Join-Path $script:folders['inbox'] 'paired.m4a'
    if (-not [IO.File]::Exists($audioDestination)) {
        [IO.File]::Copy($audio, $audioDestination, $false)
    }
    Invoke-VisualAction -Step ($Prefix + '.scan') `
        -Arguments @('ui','invoke','InboxRefreshButton') -Code 'visual.scan' | Out-Null
    if (-not (Wait-Visual -Arguments @('ui','wait-for','paired.mp4') -Seconds $RowWaitTimeoutSeconds)) {
        throw 'visual.inbox_row_missing'
    }
}

function Set-VisualFolders {
    param([Parameter(Mandatory)][string] $Prefix)
    # A configured root may reopen on another page; the folder form is only addressable after the
    # settings navigation has actually landed.
    Open-VisualPage -Page 'settings' -Prefix ($Prefix + '.folders')
    Invoke-VisualAction -Step ($Prefix + '.folders.tab') `
        -Arguments @('ui','invoke','FolderSettingsTab') -Code 'visual.folder_settings' | Out-Null
    $preparation = $script:contract.sessionPreparation
    $needsSave = $false
    foreach ($folderInput in @($preparation.folderInputs)) {
        $path = $script:folders[[string] $folderInput.key]
        $current = ''
        try { $current = Read-VisualValue -AutomationId ([string] $folderInput.automationId) -Code 'visual.folder_settings' }
        catch { $current = '' }
        if ($current -cne $path) {
            Invoke-VisualAction -Step ($Prefix + '.folder.' + [string] $folderInput.key) `
                -Arguments @('ui','set-value',[string] $folderInput.automationId,$path) `
                -Code 'visual.folder_settings' | Out-Null
            $readBack = Read-VisualValue -AutomationId ([string] $folderInput.automationId) `
                -Code 'visual.folder_settings'
            if ($readBack -cne $path) { throw 'visual.folder_settings' }
            $needsSave = $true
        }
    }
    if ($needsSave) {
        Invoke-VisualAction -Step ($Prefix + '.folder.save') `
            -Arguments @('ui','invoke',[string] $preparation.saveFolderSettingsAutomationId) `
            -Code 'visual.folder_settings' | Out-Null
        $status = Read-VisualValue -AutomationId ([string] $preparation.savedStatusAutomationId) `
            -Code 'visual.folder_settings'
        if ($status -cne [string] $preparation.savedStatusText) { throw 'visual.folder_settings' }
    }
}

function Invoke-VisualValidationError {
    param([Parameter(Mandatory)][string] $Prefix, [Parameter(Mandatory)][string] $Theme,
        [Parameter(Mandatory)][string] $Viewport, [Parameter(Mandatory)][int] $ClientWidth,
        [Parameter(Mandatory)][int] $ClientHeight, [Parameter(Mandatory)][double] $Dpi,
        [switch] $Preparation)
    Open-VisualPage -Page 'settings' -Prefix ($Prefix + '.validation')
    Invoke-VisualAction -Step ($Prefix + '.validation.folder-tab') `
        -Arguments @('ui','invoke','FolderSettingsTab') -Code 'visual.validation_error' | Out-Null
    $missing = Join-Path ([IO.Path]::GetTempPath()) 'tracefold-acceptance-missing-folder'
    Invoke-VisualAction -Step ($Prefix + '.validation.set') `
        -Arguments @('ui','set-value','InboxFolderPath',$missing) -Code 'visual.validation_error' | Out-Null
    Invoke-VisualAction -Step ($Prefix + '.validation.save') `
        -Arguments @('ui','invoke','SaveFolderSettings') -Code 'visual.validation_error' | Out-Null
    $shown = Wait-Visual -Arguments @('ui','wait-for','SettingsValidationErrors','--property','IsOffscreen','--value','False') `
        -Seconds 10
    if (-not $shown) {
        # Restore the valid path before reporting, so the failure does not leave the owned root
        # with an unusable setting.
        Invoke-VisualAction -Step ($Prefix + '.validation.restore') `
            -Arguments @('ui','set-value','InboxFolderPath',[string] $script:folders['inbox']) `
            -Code 'visual.validation_error' | Out-Null
        Invoke-VisualAction -Step ($Prefix + '.validation.restore-save') `
            -Arguments @('ui','invoke','SaveFolderSettings') -Code 'visual.validation_error' | Out-Null
        throw 'visual.validation_error'
    }
    $stateName = 'validation-error'
    if ($Preparation) { $stateName = 'validation-error-prep' }
    [void] (Capture-VisualState -Prefix $Prefix -Theme $Theme -Viewport $Viewport -Page 'settings' `
            -State $stateName -Selector 'TraceFoldMainWindow' -ClientWidth $ClientWidth `
            -ClientHeight $ClientHeight -Dpi $Dpi)
    Invoke-VisualAction -Step ($Prefix + '.validation.restore') `
        -Arguments @('ui','set-value','InboxFolderPath',[string] $script:folders['inbox']) `
        -Code 'visual.validation_error' | Out-Null
    Invoke-VisualAction -Step ($Prefix + '.validation.restore-save') `
        -Arguments @('ui','invoke','SaveFolderSettings') -Code 'visual.validation_error' | Out-Null
}

function Get-VisualFocusFacts {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]] $Nodes)
    $observable = $false
    $focused = @()
    foreach ($node in @($Nodes)) {
        $focusProperty = $null
        foreach ($property in $node.PSObject.Properties) {
            if ($property.Name -match '(?i)focus') { $focusProperty = $property; break }
        }
        if ($null -eq $focusProperty) { continue }
        $observable = $true
        if ([bool] $focusProperty.Value) {
            $focused += [pscustomobject] @{
                AutomationId = [string] (Get-UiAcceptanceNodeProperty -Node $node -Name 'automationId')
                Name = [string] (Get-UiAcceptanceNodeProperty -Node $node -Name 'name')
                Offscreen = [string] (Get-UiAcceptanceNodeProperty -Node $node -Name 'isOffscreen')
                Property = [string] $focusProperty.Name
            }
        }
    }
    if ($observable) { $script:focusPropertySeen = $true }
    return [pscustomobject] @{ Observable = $observable; Focused = @($focused) }
}

function Assert-VisualFocusReadable {
    param([Parameter(Mandatory)][string] $Code)
    $facts = Get-VisualFocusFacts -Nodes (Read-VisualNodes)
    if (-not $facts.Observable -and -not $script:focusPropertySeen) {
        throw 'visual.focus_observation_unavailable'
    }
    if (@($facts.Focused).Count -eq 0) { throw $Code }
    if (@($facts.Focused | Where-Object { [string] $_.Offscreen -cne 'False' }).Count -eq @($facts.Focused).Count) {
        throw 'visual.focus_not_visible'
    }
    return $facts
}

function Assert-VisualFocusInside {
    param([Parameter(Mandatory)][string[]] $AutomationIds, [Parameter(Mandatory)][string] $Code)
    $facts = Assert-VisualFocusReadable -Code $Code
    foreach ($focused in @($facts.Focused)) {
        if ($AutomationIds -ccontains [string] $focused.AutomationId) { return }
    }
    throw $Code
}

function Assert-VisualFocusOnRow {
    param([Parameter(Mandatory)][string] $Needle, [Parameter(Mandatory)][string] $Code)
    $facts = Assert-VisualFocusReadable -Code $Code
    foreach ($focused in @($facts.Focused)) {
        if (([string] $focused.Name).Contains($Needle) -or [string] $focused.AutomationId -ceq 'InboxGrid') {
            return
        }
    }
    throw $Code
}

function Assert-VisualDialogScroll {
    param([Parameter(Mandatory)][string] $ScrollId, [Parameter(Mandatory)][string] $Code)
    if ([string]::IsNullOrWhiteSpace($ScrollId)) { return }
    $raw = & $SupportPath native lookup --root $OwnedRoot --token $RunToken `
        --process-id $script:productId --automation-id $ScrollId --pattern Scroll
    # A probe that cannot answer is not evidence that the pattern is absent; it blocks. Only a
    # probe that did answer can fail the dialog on a missing Scroll pattern.
    if ($LASTEXITCODE -ne 0) { throw 'visual.native_probe_unavailable' }
    $fact = ($raw | Out-String | ConvertFrom-Json)
    if ($null -eq $fact.PSObject.Properties['patterns']) { throw 'visual.native_probe_unavailable' }
    $scroll = @($fact.patterns | Where-Object { [string] $_.name -ceq 'Scroll' })
    if ($scroll.Count -ne 1) { throw $Code }
    if ($scroll[0].available -ne $true) { throw $Code }
}

function Capture-VisualDialog {
    param([Parameter(Mandatory)][string] $Prefix, [Parameter(Mandatory)][string] $Theme,
        [Parameter(Mandatory)][string] $Viewport, [Parameter(Mandatory)][string] $Page,
        [Parameter(Mandatory)][string] $State, [Parameter(Mandatory)][string] $DialogId,
        [Parameter(Mandatory)][string] $ScrollId, [Parameter(Mandatory)][AllowEmptyString()][string] $CloseId,
        [Parameter(Mandatory)][int] $ClientWidth, [Parameter(Mandatory)][int] $ClientHeight,
        [Parameter(Mandatory)][double] $Dpi)
    if (-not (Wait-Visual -Arguments @('ui','wait-for',$DialogId) -Seconds $WaitTimeoutSeconds)) {
        throw 'visual.dialog_unavailable'
    }
    $nodes = Read-VisualNodes
    $dialog = Find-VisualId -Nodes $nodes -Id $DialogId -Code 'visual.dialog_unavailable'
    Assert-VisualOnscreen -Node $dialog -Code 'visual.dialog_unavailable'
    Assert-VisualBounds -Node $dialog -Code 'visual.dialog_bounds' -ClientWidth $ClientWidth `
        -ClientHeight $ClientHeight
    if (-not [string]::IsNullOrWhiteSpace($ScrollId)) {
        $scrollNode = Find-VisualId -Nodes $nodes -Id $ScrollId -Code 'visual.dialog_scroll'
        Assert-VisualBounds -Node $scrollNode -Code 'visual.dialog_bounds' -ClientWidth $ClientWidth `
            -ClientHeight $ClientHeight
        Assert-VisualDialogScroll -ScrollId $ScrollId -Code 'visual.dialog_scroll'
    }
    [void] (Capture-VisualState -Prefix $Prefix -Theme $Theme -Viewport $Viewport -Page $Page `
            -State $State -Selector $DialogId -ClientWidth $ClientWidth -ClientHeight $ClientHeight `
            -Dpi $Dpi -Dialog)
    if (-not [string]::IsNullOrWhiteSpace($CloseId)) {
        $closeNode = Find-VisualId -Nodes (Read-VisualNodes) -Id $CloseId -Code 'visual.dialog_close'
        if ((Get-UiAcceptanceNodeProperty -Node $closeNode -Name 'isOffscreen') -cne 'False') {
            Invoke-VisualAction -Step ($Prefix + '.dialog.scroll') `
                -Arguments @('ui','scroll-into-view',$CloseId) -Code 'visual.dialog_close' | Out-Null
            $closeNode = Find-VisualId -Nodes (Read-VisualNodes) -Id $CloseId -Code 'visual.dialog_close'
        }
        Assert-VisualOnscreen -Node $closeNode -Code 'visual.dialog_close'
        Invoke-VisualAction -Step ($Prefix + '.dialog.close') `
            -Arguments @('ui','invoke',$CloseId) -Code 'visual.dialog_close' | Out-Null
    }
    else {
        Invoke-VisualKey -Step ($Prefix + '.dialog.escape') `
            -Arguments @('ui','send-keys','escape') -Code 'visual.dialog_close' | Out-Null
    }
    if (-not (Wait-Visual -Arguments @('ui','wait-for',$DialogId,'--gone') -Seconds $WaitTimeoutSeconds)) {
        throw 'visual.dialog_close'
    }
}

function Stop-VisualProduct {
    if ($script:productId -le 0) { return }
    $ledgerPath = Join-Path $OwnedRoot 'owned-processes.txt'
    $records = @()
    if ([IO.File]::Exists($ledgerPath)) {
        foreach ($line in [IO.File]::ReadAllLines($ledgerPath)) {
            $parts = $line.Split('|')
            if ($parts.Count -ne 3) { continue }
            $processId = 0
            [long] $ticks = 0
            if (-not [int]::TryParse($parts[0], [ref] $processId) -or $processId -le 0) { continue }
            if (-not [long]::TryParse($parts[1], [ref] $ticks) -or $ticks -le 0) { continue }
            if (-not [IO.Path]::IsPathRooted($parts[2])) { continue }
            $records += [pscustomobject] @{ ProcessId = $processId; StartUtcTicks = $ticks; ExecutablePath = $parts[2] }
        }
    }
    if ($records.Count -gt 0) {
        $approved = @([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ExecutablePath)))
        try {
            # Processing can leave ffmpeg/ffprobe children that the product ledger does not name.
            # They are adopted only when their parent identity and approved directory both match.
            $added = @(Register-OwnedToolDescendants -OwnedRoot $OwnedRoot -RunToken $RunToken `
                -SupportPath $SupportPath -Ledger $records -ApprovedDirectories $approved)
            foreach ($entry in $added) { $records += $entry }
        }
        catch { }
        $null = Stop-OwnedProcessTree -Ledger $records -ApprovedDirectories $approved
    }
    $script:productId = 0
    $script:productIdentity = $null
}

# The theme command wrapper and its response validators belong to TraceFold.UiAcceptance.VisualGate
# (Invoke-UiAcceptanceThemeCommand, Test-UiAcceptanceThemeApplyResponse,
# Test-UiAcceptanceThemeRestoreResponse). This scenario never builds a second command path: the bare
# '& support' invocation with an [int] exit cast that used to live here is deliberately gone.

function Protect-VisualModeName {
    param([Parameter(Mandatory)][string] $Mode)
    if ($Mode -ceq 'light' -or $Mode -ceq 'dark') { return $Mode }
    return 'current'
}

function New-VisualMatrix {
    # An empty reason is the normal success case, not a missing argument: the projection is built
    # even when the run passed, otherwise every clean matrix would leave the report without facts.
    #
    # This function returns data only. The screenshot index and its page belong to the reporting
    # outlet (Write-UiVisualIndex through the Runner): that writer validates every link, redacts
    # once through the shared text protection, refuses to overwrite an occupied target and embeds
    # the JSON with the script-closing sequence escaped. A scenario-side index would be a second
    # writer with a second set of those rules, which is exactly what this change removes.
    param([Parameter(Mandatory)][string] $ThemeMatrix, [Parameter(Mandatory)][string] $Status,
        [AllowEmptyString()][Parameter(Mandatory)][string] $Reason)
    $doc = [ordered] @{
        schemaVersion = 1
        scenario = 'Visual'
        themeMatrix = $ThemeMatrix
        productHash = $script:productHash
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        journal = [ordered] @{
            name = 'theme-recovery.json'
            userScope = 'LocalApplicationData'
            # The scenario's own gate result. 'clear' is the normal state; a direct invocation
            # that found and closed a dead-owner journal reports 'recovered' here.
            gate = $(if ($null -ne $journalGateFacts) { [string] $journalGateFacts.state } else { 'unknown' })
        }
        # Declared coverage policy, so the matrix cannot be read as a full state x viewport
        # cross product it never claimed: the base grid is complete; transient states are
        # observed once per theme at the minimum viewport; dialogs and validation are checked at
        # every viewport; diagnostics-running is a minimum-viewport observation.
        matrixPolicy = [ordered] @{
            baseGrid = 'themes x viewports x pages'
            transientStates = 'minimum viewport per theme'
            dialogs = 'all viewports'
            validationError = 'all viewports'
            diagnosticsRunning = 'minimum viewport per theme'
        }
        synthetic = $syntheticFlag
        themes = $script:themeFacts.ToArray()
        keyboard = $script:keyboardFacts.ToArray()
        entries = $script:entries.ToArray()
        # The last restore failure is kept beside the run status: a confirmed restore clears the
        # journal, it does not erase the fact that an earlier attempt failed.
        themeRestoreFailure = [string] $script:themeRestoreFailure
        stopReason = [string] $script:themeStopReason
        status = $Status
        reason = $Reason
    }
    # The projection is tokenless by construction; this is the last-line check that no future
    # field smuggled the capability into the shared document. It runs on the exact text the
    # reporting outlet is about to render, before the capability is cleared below.
    $capability = ''
    if ($null -ne $script:themeRound) { $capability = [string] $script:themeRound.HandbackToken }
    $projection = ConvertTo-Json -InputObject $doc -Depth 24
    Assert-UiAcceptanceNoCapabilityValue -Text $projection -Value $capability -Code 'visual.capability_value_leak'
    return $doc
}

$failureCode = 'visual.unexpected'
$status = 'Passed'
$reason = ''

try {
    foreach ($requiredInput in @($ExecutablePath, $ContractPath, $OwnedRoot, $OutputDirectory, $SupportPath, $WinAppPath)) {
        if ([string]::IsNullOrWhiteSpace($requiredInput)) { throw 'visual.invalid_input' }
    }
    if ([IO.Path]::GetFileName($ExecutablePath) -ine 'TraceFold.Media.exe') { throw 'visual.product_unavailable' }
    if (-not [IO.File]::Exists($ContractPath)) { throw 'visual.contract_missing' }
    if (-not [IO.File]::Exists($SupportPath) -or -not [IO.File]::Exists($WinAppPath)) {
        throw 'visual.tooling_missing'
    }

    $script:contract = ConvertFrom-UiAcceptanceJson -Text ([IO.File]::ReadAllText($ContractPath)) -Source 'uia contract'
    Assert-UiAcceptanceFields -InputObject $script:contract -Required @(
        'entries','dynamicRows','fixtureAssets','sessionPreparation','layoutProbe','layoutViewports')
    $windowEntries = @($script:contract.entries | Where-Object { [string] $_.scope -ceq 'window' })
    if ($windowEntries.Count -ne 1) { throw 'visual.contract_empty' }
    $windowId = [string] $windowEntries[0].automationId

    $declared = @($script:contract.layoutViewports)
    $accepted = Get-UiAcceptanceRequiredViewport
    if ($declared.Count -ne $accepted.Count) { throw 'visual.contract_empty' }
    for ($position = 0; $position -lt $accepted.Count; $position++) {
        if ([int] $declared[$position].widthDip -ne [int] $accepted[$position].WidthDip -or
            [int] $declared[$position].heightDip -ne [int] $accepted[$position].HeightDip) {
            throw 'visual.contract_empty'
        }
    }

    $preparation = $script:contract.sessionPreparation
    Assert-UiAcceptanceFields -InputObject $preparation -Required @(
        'savedStatusText','savedStatusAutomationId','settingsNavigationAutomationId',
        'inboxNavigationAutomationId','saveFolderSettingsAutomationId','scanTriggerAutomationId','folderInputs')
    foreach ($folderInput in @($preparation.folderInputs)) {
        Assert-UiAcceptanceFields -InputObject $folderInput -Required @('key','automationId','relativeDirectory')
        $relative = [string] $folderInput.relativeDirectory
        if ($relative -cnotmatch '^[a-z0-9][a-z0-9-]*$') { throw 'visual.fixture_target' }
        $directory = [IO.Path]::GetFullPath((Join-Path $OwnedRoot $relative))
        $rootBoundary = [IO.Path]::GetFullPath($OwnedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $directory.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'visual.fixture_target'
        }
        [void][IO.Directory]::CreateDirectory($directory)
        $script:folders[[string] $folderInput.key] = $directory
    }
    if (-not $script:folders.ContainsKey('inbox') -or -not $script:folders.ContainsKey('library')) {
        throw 'visual.fixture_target'
    }

    $script:fixtureSources = @{}
    foreach ($name in @('paired.mp4','paired.m4a')) {
        $asset = @($script:contract.fixtureAssets | Where-Object { [string] $_.fixedFilename -ceq $name })
        if ($asset.Count -ne 1) { throw 'visual.fixture_missing' }
        $verified = Test-UiAcceptanceFixtureAsset -Asset $asset[0] -RepositoryRoot $repositoryRoot
        $script:fixtureSources[$name] = $verified.Source
    }

    $script:productHash = (Get-FileHash -LiteralPath $ExecutablePath -Algorithm SHA256).Hash
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    [void][IO.Directory]::CreateDirectory($script:screenshotRoot)

    # --- journal safety -----------------------------------------------------------------------
    # The public entry already refuses to start when a leftover journal cannot be closed. This is
    # the same gate through the same implementation, kept for a directly invoked scenario: it is
    # read-only for a clear journal and refuses an unclosed one instead of letting the first apply
    # overwrite it with CreateNew.
    $entryGate = Invoke-UiAcceptanceThemeJournalGate -JournalPath $journalPath -SupportPath $SupportPath `
        -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture -AllowRestore:$allowThemeRestore -EnforceAdapterSelection
    if ($entryGate.state -ceq 'blocked') { throw 'visual.theme_journal_blocked' }
    $journalGateFacts = Export-UiAcceptanceThemeJournalGate -Gate $entryGate

    # The caller identity is the real persistent harness, captured once for this execution:
    # PID, Int64 start ticks and a per-run context. The binding states which product and which
    # toolkit members the request is about. Both are sent with every theme request; an unknown
    # identity or binding refuses the matrix instead of sending a partial one.
    $harnessIdentity = Get-UiAcceptanceHarnessIdentity
    if ($harnessIdentity.state -cne 'captured') { throw 'visual.theme_owner_identity_unavailable' }
    $themeBinding = Get-UiAcceptanceThemeBinding -ProductExecutable $ExecutablePath `
        -SupportExecutable $SupportPath `
        -GateModulePath (Join-Path $PSScriptRoot '../TraceFold.UiAcceptance.VisualGate.psm1')
    if ($themeBinding.state -cne 'captured') { throw 'visual.theme_binding_unavailable' }

    $themeModes = @('light','dark')
    if ($ThemeMatrix -ceq 'CurrentOnly') { $themeModes = @('current') }

    foreach ($mode in $themeModes) {
        # The stop gate: a previous theme whose restore was not confirmed must not be followed by
        # another apply or another product start.
        if ($null -ne $script:themeRound) {
            $startGate = Test-UiAcceptanceThemeRoundMayStart -State $script:themeRound
            if (-not $startGate.Allowed) {
                $script:themeStopReason = 'visual.theme_next_round_blocked'
                break
            }
        }

        $modeLabel = Protect-VisualModeName -Mode $mode
        $round = New-UiAcceptanceThemeRoundState -Mode $modeLabel `
            -OwnerIdentity $harnessIdentity -Binding $themeBinding
        $script:themeRound = $round
        $applyData = $null
        $applied = $false
        if ($ThemeMatrix -ceq 'Both') {
            # Pending is set before the command runs: from here the round may have written a
            # journal or a registry value, whatever its answer turns out to be.
            $script:themePendingRestore = $true
            $applyData = Invoke-UiAcceptanceThemeApplyPhase -Round $round -JournalPath $journalPath `
                -SupportPath $SupportPath -Mode $mode -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture -EnforceAdapterSelection
            $script:themePendingRestore = [bool] $round.RestoreOwed
            if (-not $applyData.ok) {
                # First cause wins: the finally may restore, but it can never turn this into a pass.
                if ($applyData.notIntegrated) { throw 'visual.theme_coordinator_unavailable' }
                throw 'visual.theme_apply_failed'
            }
            $applied = $true
        }

        $themeFact = [ordered] @{
            mode = $modeLabel
            attempted = [bool] $round.Attempted
            applied = $applied
            writesPossible = [string] $round.WritesPossible
            ownershipConfirmed = [bool] $round.OwnershipConfirmed
            # Process identity stays in the journal. The shared matrix carries only the fact that
            # the request named a persistent harness and whether the receipt arrived.
            ownerKind = 'HarnessRunner'
            adapters = $(if ($null -ne $applyData) { [string] $applyData.adapters } else { 'unknown' })
            handbackTokenPresent = [bool] $round.HandbackTokenPresent
            bindingState = [string] $themeBinding.state
            productSha256 = [string] $themeBinding.productSha256
            toolkitSha256 = [string] $themeBinding.toolkitSha256
            identityMatch = $(if ($null -ne $applyData) { [bool] $applyData.identityMatch } else { $null })
            bindingMatch = $(if ($null -ne $applyData) { [bool] $applyData.bindingMatch } else { $null })
            apply = (Export-UiAcceptanceThemeDecision -Decision $applyData -Fields @(
                'ok', 'failure', 'ran', 'exitKnown', 'exitCode', 'parseState', 'adapters',
                'applied', 'mode', 'journalState', 'journalWritten', 'journalRetained',
                'registryWritten', 'readBackVerified', 'broadcastSent',
                'originalPresent', 'originalValueKind', 'originalBytes',
                'identityMatch', 'bindingMatch', 'ownershipConfirmed', 'handbackTokenPresent',
                'writesPossible'))
            restore = $null
            readback = $null
            luminance = $null
            startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
            endedUtc = $null
        }

        # Every theme gets a fresh product process: WinUI resolves theme resources at startup.
        $launched = Start-TraceFoldScenario -ExecutablePath $ExecutablePath -OwnedRoot $OwnedRoot `
            -RunToken $RunToken -SupportPath $SupportPath -StartProcess $LaunchProcess
        $script:productId = [int] $launched.ProcessId
        # Process identity stays in the owned ledger, not in the shared matrix document.
        $script:productIdentity = [ordered] @{
            startUtcTicks = [long] $launched.StartUtcTicks
        }

        if (-not (Wait-Visual -Arguments @('ui','wait-for',$windowId) -Seconds $WaitTimeoutSeconds)) {
            throw 'visual.window_unavailable'
        }

        # --- preparation: folders, validation state and the first owned inbox rows --------------
        $prefix = 'visual.' + $modeLabel + '.prep'
        Set-VisualFolders -Prefix $prefix
        $resize = Resize-VisualWindow -WidthDip 1024 -HeightDip 720
        $clientWidth = [int] $resize.ClientWidthPixels
        $clientHeight = [int] $resize.ClientHeightPixels
        $dpi = [double] $resize.Dpi

        Invoke-VisualValidationError -Prefix $prefix -Theme $modeLabel -Viewport '1024x720' `
            -ClientWidth $clientWidth -ClientHeight $clientHeight -Dpi $dpi -Preparation

        Open-VisualPage -Page 'inbox' -Prefix $prefix
        Ensure-VisualInboxRow -Prefix $prefix

        # Empty selection is the state a fresh product actually opens with; a selection is refused
        # here rather than cleared, so the evidence cannot hide an automatic selection. The open
        # control is the primary action that stays addressable while the inspector is closed.
        $nodes = Read-VisualNodes
        $openButton = Find-VisualId -Nodes $nodes -Id 'InboxInspectorOpenButton' -Code 'visual.disabled_action'
        if ((Get-UiAcceptanceNodeProperty -Node $openButton -Name 'isEnabled') -ceq 'True') {
            throw 'visual.empty_selection_state'
        }
        [void] (Capture-VisualState -Prefix ($prefix + '.empty') -Theme $modeLabel -Viewport '1024x720' `
                -Page 'inbox' -State 'empty-selection' -Selector $windowId -ClientWidth $clientWidth `
                -ClientHeight $clientHeight -Dpi $dpi)
        [void] (Capture-VisualState -Prefix ($prefix + '.disabled') -Theme $modeLabel -Viewport '1024x720' `
                -Page 'inbox' -State 'disabled-primary-action' -Selector $windowId -ClientWidth $clientWidth `
                -ClientHeight $clientHeight -Dpi $dpi)

        # Batch metadata dialog, then a single selected row.
        $selectAll = Invoke-VisualKey -Step ($prefix + '.batch.select-all') `
            -Arguments @('ui','send-keys','ctrl+a','--target','InboxGrid') -Code 'visual.batch_selection'
        if (-not (Wait-Visual -Arguments @('ui','wait-for','InboxBatchMetadataButton','--property','IsEnabled','--value','True') -Seconds 10)) {
            throw 'visual.batch_selection'
        }
        Invoke-VisualAction -Step ($prefix + '.batch.open') `
            -Arguments @('ui','invoke','InboxBatchMetadataButton') -Code 'visual.batch_dialog' | Out-Null
        Capture-VisualDialog -Prefix ($prefix + '.batch') -Theme $modeLabel -Viewport '1024x720' `
            -Page 'inbox' -State 'batch-metadata-dialog-prep' -DialogId 'BatchMetadataDialog' `
            -ScrollId 'BatchMetadataScrollRoot' -CloseId '' -ClientWidth $clientWidth `
            -ClientHeight $clientHeight -Dpi $dpi
        Invoke-VisualAction -Step ($prefix + '.batch.clear') `
            -Arguments @('ui','invoke','InboxCancelSelectionButton') -Code 'visual.batch_selection' | Out-Null

        [void] (Select-VisualRow -Prefix ($prefix + '.selected') -ContainerId 'InboxGrid' `
            -Needle 'paired.mp4' -Code 'visual.row_selection')
        [void] (Capture-VisualState -Prefix ($prefix + '.selected') -Theme $modeLabel -Viewport '1024x720' `
                -Page 'inbox' -State 'selected' -Selector $windowId -ClientWidth $clientWidth `
                -ClientHeight $clientHeight -Dpi $dpi)

        # --- processing: queued then ready, both read from the live tree ------------------------
        Invoke-VisualAction -Step ($prefix + '.processing.start') `
            -Arguments @('ui','invoke','InboxStartProcessingButton') -Code 'visual.processing_start' | Out-Null
        $queued = Wait-Visual -Arguments @('ui','wait-for','InboxProcessingProgress','--property','IsOffscreen','--value','False') -Seconds 10
        if ($queued) {
            [void] (Capture-VisualState -Prefix ($prefix + '.queued') -Theme $modeLabel `
                    -Viewport '1024x720' -Page 'inbox' -State 'processing-queued' -Selector $windowId `
                    -ClientWidth $clientWidth -ClientHeight $clientHeight -Dpi $dpi)
        }
        else {
            Add-VisualNotRun -Theme $modeLabel -Viewport '1024x720' -Page 'inbox' `
                -State 'processing-queued' -Reason 'visual.processing_queued_not_observed' `
                -ClientWidth $clientWidth -ClientHeight $clientHeight -Dpi $dpi
        }
        if (-not (Wait-Visual -Arguments @('ui','wait-for','IngestButton','--property','IsEnabled','--value','True') -Seconds $ProcessingWaitSeconds)) {
            throw 'visual.processing_ready'
        }
        [void] (Capture-VisualState -Prefix ($prefix + '.ready') -Theme $modeLabel -Viewport '1024x720' `
                -Page 'inbox' -State 'processing-ready' -Selector $windowId -ClientWidth $clientWidth `
                -ClientHeight $clientHeight -Dpi $dpi)

        # --- ingest; the second copy in the light pass, and the first row in the dark pass, hit
        # the existing library file and therefore exercise the conflict dialog -------------------
        Invoke-VisualAction -Step ($prefix + '.ingest') `
            -Arguments @('ui','invoke','IngestButton') -Code 'visual.ingest' | Out-Null
        $conflictSeen = Wait-Visual -Arguments @('ui','wait-for','ConflictComparisonDialog') -Seconds 8
        if ($conflictSeen) {
            Capture-VisualDialog -Prefix ($prefix + '.conflict') -Theme $modeLabel -Viewport '1024x720' `
                -Page 'inbox' -State 'conflict-dialog' -DialogId 'ConflictComparisonDialog' `
                -ScrollId 'ConflictScrollRoot' -CloseId 'ConflictKeepBothButton' `
                -ClientWidth $clientWidth -ClientHeight $clientHeight -Dpi $dpi
        }
        if (-not (Wait-Visual -Arguments @('ui','wait-for','paired.mp4','--gone') -Seconds 90)) {
            throw 'visual.ingest'
        }

        if ($mode -ceq 'light' -and -not $conflictSeen) {
            # The first ingest in an empty library cannot conflict; a second fixed fixture makes
            # the conflict state deterministic without touching the product outside its own UI.
            Ensure-VisualInboxRow -Prefix ($prefix + '.second')
            [void] (Select-VisualRow -Prefix ($prefix + '.second') -ContainerId 'InboxGrid' `
                -Needle 'paired.mp4' -Code 'visual.conflict')
            Invoke-VisualProcessSelected -Prefix ($prefix + '.second')
            Invoke-VisualAction -Step ($prefix + '.second.ingest') `
                -Arguments @('ui','invoke','IngestButton') -Code 'visual.conflict' | Out-Null
            if (Wait-Visual -Arguments @('ui','wait-for','ConflictComparisonDialog') -Seconds 8) {
                Capture-VisualDialog -Prefix ($prefix + '.conflict') -Theme $modeLabel `
                    -Viewport '1024x720' -Page 'inbox' -State 'conflict-dialog' `
                    -DialogId 'ConflictComparisonDialog' -ScrollId 'ConflictScrollRoot' `
                    -CloseId 'ConflictKeepBothButton' -ClientWidth $clientWidth `
                    -ClientHeight $clientHeight -Dpi $dpi
            }
            else {
                Add-VisualNotRun -Theme $modeLabel -Viewport '1024x720' -Page 'inbox' `
                    -State 'conflict-dialog' -Reason 'visual.conflict_not_observed' `
                    -ClientWidth $clientWidth -ClientHeight $clientHeight -Dpi $dpi -Dialog $true
            }
            if (-not (Wait-Visual -Arguments @('ui','wait-for','paired.mp4','--gone') -Seconds 90)) {
                throw 'visual.conflict'
            }
        }

        # A persistent inbox row is materialised again for the page matrix; it is never ingested.
        Ensure-VisualInboxRow -Prefix ($prefix + '.matrix')
        Select-VisualRow -Prefix ($prefix + '.matrix') -ContainerId 'InboxGrid' `
            -Needle 'paired.mp4' -Code 'visual.matrix_row' | Out-Null

        # --- library page and the library batch edit dialog --------------------------------------
        Open-VisualPage -Page 'library' -Prefix ($prefix + '.library')
        [void] (Select-VisualRow -Prefix ($prefix + '.library') -ContainerId 'LibraryGrid' `
            -Needle 'paired.mp4' -Code 'visual.library_row')
        if (-not (Wait-Visual -Arguments @('ui','wait-for','LibraryBatchEditButton','--property','IsEnabled','--value','True') -Seconds 10)) {
            throw 'visual.library_row'
        }
        [void] (Capture-VisualState -Prefix ($prefix + '.library.selected') -Theme $modeLabel `
                -Viewport '1024x720' -Page 'library' -State 'selected' -Selector $windowId `
                -ClientWidth $clientWidth -ClientHeight $clientHeight -Dpi $dpi)
        Invoke-VisualAction -Step ($prefix + '.library.batch-open') `
            -Arguments @('ui','invoke','LibraryBatchEditButton') -Code 'visual.library_batch_dialog' | Out-Null
        Capture-VisualDialog -Prefix ($prefix + '.library.batch') -Theme $modeLabel -Viewport '1024x720' `
            -Page 'library' -State 'library-batch-edit-dialog-prep' -DialogId 'LibraryBatchEditDialog' `
            -ScrollId 'LibraryBatchEditScrollRoot' -CloseId 'LibraryBatchCancelButton' `
            -ClientWidth $clientWidth -ClientHeight $clientHeight -Dpi $dpi

        # --- taxonomy terms, selection and the merge dialog --------------------------------------
        Open-VisualPage -Page 'taxonomy' -Prefix ($prefix + '.taxonomy')
        $terms = @('VisualAuthorA', 'VisualAuthorB')
        foreach ($term in $terms) {
            $nodes = Read-VisualNodes
            $rows = @(Get-UiAcceptanceListRowTexts -Nodes $nodes -ContainerId 'TaxonomyTermList' `
                -FailureCode 'visual.taxonomy_rows')
            $present = @($rows | Where-Object {
                @($_.Texts) | Where-Object { ([string] $_).Contains($term) }
            }).Count -gt 0
            if ($present) { continue }
            Invoke-VisualAction -Step ($prefix + '.taxonomy.create.' + (ConvertTo-UiAcceptanceSlug -Value $term)) `
                -Arguments @('ui','invoke','TaxonomyCreateButton') -Code 'visual.taxonomy_create' | Out-Null
            if (-not (Wait-Visual -Arguments @('ui','wait-for','TaxonomyNameInput') -Seconds 10)) {
                throw 'visual.taxonomy_create'
            }
            Invoke-VisualAction -Step ($prefix + '.taxonomy.name.' + (ConvertTo-UiAcceptanceSlug -Value $term)) `
                -Arguments @('ui','set-value','TaxonomyNameInput',$term) -Code 'visual.taxonomy_create' | Out-Null
            Invoke-VisualAction -Step ($prefix + '.taxonomy.confirm.' + (ConvertTo-UiAcceptanceSlug -Value $term)) `
                -Arguments @('ui','invoke','PrimaryButton') -Code 'visual.taxonomy_create' | Out-Null
            if (-not (Wait-Visual -Arguments @('ui','wait-for','TaxonomyNameInput','--gone') -Seconds 30)) {
                throw 'visual.taxonomy_create'
            }
        }
        Select-VisualRow -Prefix ($prefix + '.taxonomy') -ContainerId 'TaxonomyTermList' `
            -Needle 'VisualAuthorA' -Code 'visual.taxonomy_selection' | Out-Null
        if (-not (Wait-Visual -Arguments @('ui','wait-for','TaxonomyMergeButton','--property','IsEnabled','--value','True') -Seconds 10)) {
            throw 'visual.taxonomy_selection'
        }
        [void] (Capture-VisualState -Prefix ($prefix + '.taxonomy') -Theme $modeLabel `
                -Viewport '1024x720' -Page 'taxonomy' -State 'selection' -Selector $windowId `
                -ClientWidth $clientWidth -ClientHeight $clientHeight -Dpi $dpi)
        Invoke-VisualAction -Step ($prefix + '.taxonomy.merge-open') `
            -Arguments @('ui','invoke','TaxonomyMergeButton') -Code 'visual.taxonomy_merge_dialog' | Out-Null
        Capture-VisualDialog -Prefix ($prefix + '.taxonomy.merge') -Theme $modeLabel -Viewport '1024x720' `
            -Page 'taxonomy' -State 'taxonomy-merge-dialog-prep' -DialogId 'TaxonomyMergeDialog' `
            -ScrollId 'TaxonomyMergeScrollRoot' -CloseId 'TaxonomyMergeCancelButton' `
            -ClientWidth $clientWidth -ClientHeight $clientHeight -Dpi $dpi

        # --- the full page grid: three viewports, four pages ------------------------------------
        foreach ($viewport in @($accepted)) {
            $widthDip = [int] $viewport.WidthDip
            $heightDip = [int] $viewport.HeightDip
            $viewportName = [string] $widthDip + 'x' + [string] $heightDip
            $resized = Resize-VisualWindow -WidthDip $widthDip -HeightDip $heightDip
            $gridWidth = [int] $resized.ClientWidthPixels
            $gridHeight = [int] $resized.ClientHeightPixels
            $gridDpi = [double] $resized.Dpi

            foreach ($page in @('inbox','library','taxonomy','settings')) {
                Open-VisualPage -Page $page -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.' + $page)
                if ($page -ceq 'inbox') {
                    Ensure-VisualInboxRow -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.inbox')
                    Select-VisualRow -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.inbox') `
                        -ContainerId 'InboxGrid' -Needle 'paired.mp4' -Code 'visual.matrix_row' | Out-Null
                }
                elseif ($page -ceq 'library') {
                    Select-VisualRow -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.library') `
                        -ContainerId 'LibraryGrid' -Needle 'paired.mp4' -Code 'visual.library_row' | Out-Null
                }
                elseif ($page -ceq 'taxonomy') {
                    Select-VisualRow -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.taxonomy') `
                        -ContainerId 'TaxonomyTermList' -Needle 'VisualAuthorA' -Code 'visual.taxonomy_selection' | Out-Null
                }
                else {
                    Invoke-VisualAction -Step ('visual.' + $modeLabel + '.' + $viewportName + '.settings.tab') `
                        -Arguments @('ui','invoke','FolderSettingsTab') -Code 'visual.settings_tab' | Out-Null
                }

                Assert-VisualPageLayout -Page $page -WidthDip $widthDip -ClientWidth $gridWidth `
                    -ClientHeight $gridHeight
                [void] (Capture-VisualState -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.' + $page) `
                        -Theme $modeLabel -Viewport $viewportName -Page $page -State 'matrix' `
                        -Selector $windowId -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi)
            }

            # Dialogs are layout-constrained at every viewport, not only at the minimum.
            Open-VisualPage -Page 'inbox' -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs')
            $selectAll = Invoke-VisualKey -Step ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.select-all') `
                -Arguments @('ui','send-keys','ctrl+a','--target','InboxGrid') -Code 'visual.batch_selection'
            if (-not (Wait-Visual -Arguments @('ui','wait-for','InboxBatchMetadataButton','--property','IsEnabled','--value','True') -Seconds 10)) {
                throw 'visual.batch_selection'
            }
            Invoke-VisualAction -Step ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.batch-open') `
                -Arguments @('ui','invoke','InboxBatchMetadataButton') -Code 'visual.batch_dialog' | Out-Null
            Capture-VisualDialog -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.batch') `
                -Theme $modeLabel -Viewport $viewportName -Page 'inbox' -State 'batch-metadata-dialog' `
                -DialogId 'BatchMetadataDialog' -ScrollId 'BatchMetadataScrollRoot' -CloseId '' `
                -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi
            Invoke-VisualAction -Step ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.clear') `
                -Arguments @('ui','invoke','InboxCancelSelectionButton') -Code 'visual.batch_selection' | Out-Null

            Open-VisualPage -Page 'library' -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.library')
            Select-VisualRow -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.library') `
                -ContainerId 'LibraryGrid' -Needle 'paired.mp4' -Code 'visual.library_row' | Out-Null
            Invoke-VisualAction -Step ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.library.batch-open') `
                -Arguments @('ui','invoke','LibraryBatchEditButton') -Code 'visual.library_batch_dialog' | Out-Null
            Capture-VisualDialog -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.library.batch') `
                -Theme $modeLabel -Viewport $viewportName -Page 'library' -State 'library-batch-edit-dialog' `
                -DialogId 'LibraryBatchEditDialog' -ScrollId 'LibraryBatchEditScrollRoot' `
                -CloseId 'LibraryBatchCancelButton' -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi

            Open-VisualPage -Page 'taxonomy' -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.taxonomy')
            Select-VisualRow -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.taxonomy') `
                -ContainerId 'TaxonomyTermList' -Needle 'VisualAuthorA' -Code 'visual.taxonomy_selection' | Out-Null
            Invoke-VisualAction -Step ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.taxonomy.merge-open') `
                -Arguments @('ui','invoke','TaxonomyMergeButton') -Code 'visual.taxonomy_merge_dialog' | Out-Null
            Capture-VisualDialog -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.dialogs.taxonomy.merge') `
                -Theme $modeLabel -Viewport $viewportName -Page 'taxonomy' -State 'taxonomy-merge-dialog' `
                -DialogId 'TaxonomyMergeDialog' -ScrollId 'TaxonomyMergeScrollRoot' `
                -CloseId 'TaxonomyMergeCancelButton' -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi

            # The settings page carries the folder form and the diagnostics tab at this viewport.
            Open-VisualPage -Page 'settings' -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.settings.details')
            Invoke-VisualAction -Step ('visual.' + $modeLabel + '.' + $viewportName + '.settings.folder-tab') `
                -Arguments @('ui','invoke','FolderSettingsTab') -Code 'visual.settings_tab' | Out-Null
            [void] (Capture-VisualState -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.settings.form') `
                    -Theme $modeLabel -Viewport $viewportName -Page 'settings' -State 'folder-form' `
                    -Selector $windowId -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi)
            Invoke-VisualValidationError -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.validation') `
                -Theme $modeLabel -Viewport $viewportName -ClientWidth $gridWidth `
                -ClientHeight $gridHeight -Dpi $gridDpi

            Invoke-VisualAction -Step ('visual.' + $modeLabel + '.' + $viewportName + '.settings.diag-tab') `
                -Arguments @('ui','invoke','SystemDiagnosticsTab') -Code 'visual.diagnostics_tab' | Out-Null
            if (-not (Wait-Visual -Arguments @('ui','wait-for','DiagnosticsRuntimeGroup') -Seconds $DiagnosticsWaitSeconds)) {
                throw 'visual.diagnostics_complete'
            }
            [void] (Capture-VisualState -Prefix ('visual.' + $modeLabel + '.' + $viewportName + '.settings.diag') `
                    -Theme $modeLabel -Viewport $viewportName -Page 'settings' -State 'diagnostics-complete' `
                    -Selector $windowId -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi)

            if ($widthDip -eq 1024) {
                Invoke-VisualAction -Step ('visual.' + $modeLabel + '.1024.diag.refresh') `
                    -Arguments @('ui','invoke','DiagnosticsRefreshButton') -Code 'visual.diagnostics_running' | Out-Null
                $running = Wait-Visual -Arguments @('ui','wait-for','DiagnosticsRefreshButton','--property','IsEnabled','--value','False') -Seconds 5
                if ($running) {
                    [void] (Capture-VisualState -Prefix ('visual.' + $modeLabel + '.1024.diag.running') `
                            -Theme $modeLabel -Viewport $viewportName -Page 'settings' -State 'diagnostics-running' `
                            -Selector $windowId -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi)
                }
                else {
                    Add-VisualNotRun -Theme $modeLabel -Viewport $viewportName -Page 'settings' `
                        -State 'diagnostics-running' -Reason 'visual.diagnostics_running_not_observed' `
                        -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi
                }
                if (-not (Wait-Visual -Arguments @('ui','wait-for','DiagnosticsRefreshButton','--property','IsEnabled','--value','True') -Seconds $DiagnosticsWaitSeconds)) {
                    throw 'visual.diagnostics_refresh'
                }
            }

            # The compact drawer, and the wide inline inspector, are proven on the inbox page.
            if ($widthDip -eq 1024) {
                Open-VisualPage -Page 'inbox' -Prefix ('visual.' + $modeLabel + '.1024.drawer')
                Ensure-VisualInboxRow -Prefix ('visual.' + $modeLabel + '.1024.drawer')
                Select-VisualRow -Prefix ('visual.' + $modeLabel + '.1024.drawer') `
                    -ContainerId 'InboxGrid' -Needle 'paired.mp4' -Code 'visual.drawer_selection' | Out-Null
                [void] (Capture-VisualState -Prefix ('visual.' + $modeLabel + '.1024.drawer.closed') `
                        -Theme $modeLabel -Viewport $viewportName -Page 'inbox' -State 'drawer-closed' `
                        -Selector $windowId -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi)
                Invoke-VisualAction -Step ('visual.' + $modeLabel + '.1024.drawer.open') `
                    -Arguments @('ui','invoke','InboxInspectorOpenButton') -Code 'visual.drawer_open' | Out-Null
                if (-not (Wait-Visual -Arguments @('ui','wait-for','InspectorPane','--property','IsOffscreen','--value','False') -Seconds 10)) {
                    throw 'visual.drawer_open'
                }
                [void] (Capture-VisualState -Prefix ('visual.' + $modeLabel + '.1024.drawer.open') `
                        -Theme $modeLabel -Viewport $viewportName -Page 'inbox' -State 'drawer-open' `
                        -Selector $windowId -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi)
                Assert-VisualFocusInside -AutomationIds @(
                    'InboxInspectorHeading','InboxInspectorCloseButton','DisplayNameBox',
                    'InboxAuthorInput','IngestButton','InboxTagInput') -Code 'visual.drawer_focus'
                Invoke-VisualKey -Step ('visual.' + $modeLabel + '.1024.drawer.escape') `
                    -Arguments @('ui','send-keys','escape') -Code 'visual.drawer_close' | Out-Null
                if (-not (Wait-Visual -Arguments @('ui','wait-for','InspectorPane','--property','IsOffscreen','--value','True') -Seconds 10)) {
                    throw 'visual.drawer_close'
                }
                [void] (Capture-VisualState -Prefix ('visual.' + $modeLabel + '.1024.drawer.after') `
                        -Theme $modeLabel -Viewport $viewportName -Page 'inbox' -State 'drawer-closed-after-escape' `
                        -Selector $windowId -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi)
                Assert-VisualFocusOnRow -Needle 'paired.mp4' -Code 'visual.drawer_focus_return'
            }

            # A conflict at the wide end proves the dialog is not only compact-safe.
            if ($widthDip -eq 1600) {
                Open-VisualPage -Page 'inbox' -Prefix ('visual.' + $modeLabel + '.1600.conflict')
                Ensure-VisualInboxRow -Prefix ('visual.' + $modeLabel + '.1600.conflict')
                [void] (Select-VisualRow -Prefix ('visual.' + $modeLabel + '.1600.conflict') `
                    -ContainerId 'InboxGrid' -Needle 'paired.mp4' -Code 'visual.conflict')
                Invoke-VisualProcessSelected -Prefix ('visual.' + $modeLabel + '.1600.conflict')
                Invoke-VisualAction -Step ('visual.' + $modeLabel + '.1600.conflict.ingest') `
                    -Arguments @('ui','invoke','IngestButton') -Code 'visual.conflict' | Out-Null
                if (Wait-Visual -Arguments @('ui','wait-for','ConflictComparisonDialog') -Seconds 8) {
                    Capture-VisualDialog -Prefix ('visual.' + $modeLabel + '.1600.conflict') -Theme $modeLabel `
                        -Viewport $viewportName -Page 'inbox' -State 'conflict-dialog' `
                        -DialogId 'ConflictComparisonDialog' -ScrollId 'ConflictScrollRoot' `
                        -CloseId 'ConflictKeepBothButton' -ClientWidth $gridWidth `
                        -ClientHeight $gridHeight -Dpi $gridDpi
                }
                else {
                    Add-VisualNotRun -Theme $modeLabel -Viewport $viewportName -Page 'inbox' `
                        -State 'conflict-dialog' -Reason 'visual.conflict_not_observed' `
                        -ClientWidth $gridWidth -ClientHeight $gridHeight -Dpi $gridDpi -Dialog $true
                }
                if (-not (Wait-Visual -Arguments @('ui','wait-for','paired.mp4','--gone') -Seconds 90)) {
                    throw 'visual.conflict'
                }
            }
        }

        # --- keyboard matrix: every injection is followed by a UIA focus read -------------------
        $keyboardPrefix = 'visual.' + $modeLabel + '.keyboard'
        $resized = Resize-VisualWindow -WidthDip 1024 -HeightDip 720
        $keyWidth = [int] $resized.ClientWidthPixels
        $keyHeight = [int] $resized.ClientHeightPixels
        foreach ($page in @('inbox','library','taxonomy','settings')) {
            Open-VisualPage -Page $page -Prefix ($keyboardPrefix + '.' + $page)
            if ($page -ceq 'inbox') {
                Ensure-VisualInboxRow -Prefix ($keyboardPrefix + '.inbox')
                Select-VisualRow -Prefix ($keyboardPrefix + '.inbox') -ContainerId 'InboxGrid' `
                    -Needle 'paired.mp4' -Code 'visual.keyboard_selection' | Out-Null
                $target = 'InboxGrid'
            }
            elseif ($page -ceq 'library') {
                Select-VisualRow -Prefix ($keyboardPrefix + '.library') -ContainerId 'LibraryGrid' `
                    -Needle 'paired.mp4' -Code 'visual.keyboard_selection' | Out-Null
                $target = 'LibraryGrid'
            }
            elseif ($page -ceq 'taxonomy') {
                Select-VisualRow -Prefix ($keyboardPrefix + '.taxonomy') -ContainerId 'TaxonomyTermList' `
                    -Needle 'VisualAuthorA' -Code 'visual.keyboard_selection' | Out-Null
                $target = 'TaxonomyTermList'
            }
            else {
                $target = 'SettingsTabs'
            }
            $baselineFocus = Assert-VisualFocusReadable -Code 'visual.focus_not_reported'
            $baselineKey = (@($baselineFocus.Focused | ForEach-Object {
                [string] $_.AutomationId + '|' + [string] $_.Name
            }) -join '~')
            foreach ($key in @('tab','shift+tab','down','up','enter','space','escape')) {
                $step = $keyboardPrefix + '.' + $page + '.' + (ConvertTo-UiAcceptanceSlug -Value $key)
                Invoke-VisualKey -Step $step -Arguments @('ui','send-keys',$key,'--target',$target) `
                    -Code 'visual.keyboard' | Out-Null
                $focus = Assert-VisualFocusReadable -Code 'visual.focus_not_reported'
                $focusKey = (@($focus.Focused | ForEach-Object {
                    [string] $_.AutomationId + '|' + [string] $_.Name
                }) -join '~')
                # Tab must leave the current element and Shift+Tab must return to it; anything
                # else means the focus path is not the logical order this gate is proving.
                if ($key -ceq 'tab' -and $focusKey -ceq $baselineKey) { throw 'visual.keyboard_order' }
                if ($key -ceq 'shift+tab' -and $focusKey -cne $baselineKey) { throw 'visual.keyboard_order' }
                $script:keyboardFacts.Add([ordered] @{
                    theme = $modeLabel
                    page = $page
                    key = $key
                    target = $target
                    focused = @($focus.Focused | ForEach-Object { [ordered] @{
                        automationId = [string] $_.AutomationId
                        name = [string] $_.Name
                        property = [string] $_.Property
                    } })
                    observedUtc = [DateTimeOffset]::UtcNow.ToString('o')
                })
            }
        }

        # Theme confirmation is measured on the inbox page at the minimum viewport.
        $themeEntries = @($script:entries | Where-Object {
            [string] $_.theme -ceq $modeLabel -and [string] $_.state -ceq 'matrix' -and
            [string] $_.page -ceq 'inbox' -and [string] $_.viewport -ceq '1024x720'
        })
        if ($themeEntries.Count -eq 1) {
            $themeFact['luminance'] = $themeEntries[0].averageLuminance
            $themeFact['pixelState'] = $themeEntries[0].pixelState
        }

        Stop-VisualProduct

        # Each theme owns one apply/restore pair: the journal is CreateNew, so the next apply must
        # only start after the previous theme has been restored, read back and closed. A round
        # that cannot prove the journal is its own makes no restore call at all.
        $themeRoundFailed = $false
        if ($round.RestoreOwed) {
            $restoreOutcome = Invoke-UiAcceptanceThemeRestorePhase -Round $round -JournalPath $journalPath `
                -SupportPath $SupportPath -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture -EnforceAdapterSelection -WriteEvidence {
                    param($restoreDecision, $readbackDecision)
                    [void] (Write-UiThemeEvidence -OutputDirectory $OutputDirectory `
                            -EvidenceId ('theme-' + $modeLabel) -Document ([ordered] @{
                                mode = $modeLabel
                                attempted = [bool] $round.Attempted
                                applied = $applied
                                writesPossible = [string] $round.WritesPossible
                                ownershipConfirmed = [bool] $round.OwnershipConfirmed
                                apply = (Export-UiAcceptanceThemeDecision -Decision $applyData -Fields @(
                                    'ok', 'failure', 'ran', 'exitKnown', 'exitCode', 'parseState',
                                    'adapters', 'applied', 'mode', 'journalState', 'journalWritten',
                                    'journalRetained', 'registryWritten', 'readBackVerified',
                                    'broadcastSent', 'originalPresent', 'originalValueKind',
                                    'originalBytes', 'identityMatch', 'bindingMatch',
                                    'ownershipConfirmed', 'handbackTokenPresent', 'writesPossible'))
                                restore = (Export-UiAcceptanceThemeDecision -Decision $restoreDecision -Fields @(
                                    'ok', 'failure', 'ran', 'exitKnown', 'exitCode', 'parseState',
                                    'adapters', 'disposition', 'restored', 'noAction', 'journalState',
                                    'journalPresent', 'journalRemoved', 'journalChecksumValid',
                                    'ownerState', 'identityMatch',
                                    'originalPresent', 'originalValueKind', 'originalBytes',
                                    'readbackPresent', 'readbackValueKind', 'readbackBytes',
                                    'readbackMatches', 'broadcastSent', 'registryRestored', 'bindingMatchesJournal'))
                                readback = (Export-UiAcceptanceThemeDecision -Decision $readbackDecision -Fields @(
                                    'ok', 'failure', 'exitKnown', 'exitCode', 'parseState', 'adapters',
                                    'journalState', 'ownerState', 'startupRecoveryAllowed',
                                    'handbackAccepted', 'handbackRejection'))
                                journal = [ordered] @{
                                    name = 'theme-recovery.json'
                                    closed = [bool] $round.JournalClosed
                                    restoreFailure = [string] $restoreDecision.failure
                                }
                                window = [ordered] @{
                                    automationId = 'TraceFoldMainWindow'
                                    startUtcTicks = $(if ($null -ne $script:productIdentity) { $script:productIdentity.startUtcTicks } else { $null })
                                }
                            }) -RunToken $RunToken -OwnedRoot $OwnedRoot `
                            -ForbiddenValue ([string] $round.HandbackToken))
                }
            $themeFact['restore'] = (Export-UiAcceptanceThemeDecision -Decision $restoreOutcome.decision -Fields @(
                'ok', 'failure', 'ran', 'exitKnown', 'exitCode', 'parseState', 'adapters',
                'disposition', 'restored', 'noAction', 'journalState', 'journalPresent',
                'journalRemoved', 'journalChecksumValid', 'ownerState', 'identityMatch', 'originalPresent', 'originalValueKind',
                'originalBytes', 'readbackPresent', 'readbackValueKind', 'readbackBytes',
                'readbackMatches', 'broadcastSent', 'registryRestored', 'bindingMatchesJournal'))
            $themeFact['readback'] = (Export-UiAcceptanceThemeDecision -Decision $restoreOutcome.inspect -Fields @(
                'ok', 'failure', 'exitKnown', 'exitCode', 'parseState', 'adapters',
                'journalState', 'ownerState', 'startupRecoveryAllowed', 'handbackAccepted',
                'handbackRejection'))
            $themeFact['restoreConfirmed'] = [bool] $round.RestoreConfirmed
            $themeFact['journalClosed'] = [bool] $round.JournalClosed
            # No retry is left to finally: either the restore was attempted and did not confirm,
            # or it was refused because the journal could not be proven ours. The journal stays.
            $script:themePendingRestore = $false
            if (-not $round.RestoreConfirmed) {
                $script:themeRestoreFailure = [string] $restoreOutcome.failure
                if ($status -cne 'Failed') { $status = 'Failed' }
                $reason += ('; visual.theme_restore_failed.' + (ConvertTo-UiAcceptanceSlug -Value ([string] $restoreOutcome.failure)))
                $themeRoundFailed = $true
            }
        }

        $themeFact['endedUtc'] = [DateTimeOffset]::UtcNow.ToString('o')
        $script:themeFacts.Add($themeFact)
        if ($themeRoundFailed) { break }
    }

    # A stopped theme round must not fall through to the both-theme observation check, which
    # would report a missing theme instead of the reason the next round was refused.
    if (-not [string]::IsNullOrWhiteSpace($script:themeStopReason)) {
        throw $script:themeStopReason
    }

    if ($ThemeMatrix -ceq 'Both') {
        $light = @($script:entries | Where-Object { [string] $_.theme -ceq 'light' -and [string] $_.state -ceq 'matrix' -and [string] $_.page -ceq 'inbox' -and [string] $_.viewport -ceq '1024x720' })
        $dark = @($script:entries | Where-Object { [string] $_.theme -ceq 'dark' -and [string] $_.state -ceq 'matrix' -and [string] $_.page -ceq 'inbox' -and [string] $_.viewport -ceq '1024x720' })
        if ($light.Count -ne 1 -or $dark.Count -ne 1) { throw 'visual.theme_observation_missing' }
        if ([string] $light[0].pixelState -cne 'captured' -or [string] $dark[0].pixelState -cne 'captured') {
            throw 'visual.pixel_sample_unavailable'
        }
        if ([double] $light[0].averageLuminance -le [double] $dark[0].averageLuminance) {
            throw 'visual.theme_not_observed'
        }
    }

    # CurrentOnly can never be a full visual pass; the missing mode is an explicit NotRun entry.
    if ($ThemeMatrix -ceq 'CurrentOnly') {
        [void] (Add-VisualEntry -Theme 'missing-mode' -Viewport 'n/a' -Page 'matrix' `
            -State 'theme-mode-not-run' -Status 'NotRun' -Reason 'visual.theme_matrix_current_only')
        $status = 'NotRun'
        $reason = 'visual.theme_matrix_current_only'
    }

    # Machine set gate, not a spot check: the base grid must hold exactly one entry per
    # theme/viewport/page, no theme|viewport|page|state key may repeat, and every state and
    # dialog the plan names must exist for each theme that ran. A required entry that is present
    # but NotRun is evidence, not a defect: it can only lower the run to NotRun.
    $matrixViewports = @()
    foreach ($viewportSpec in @($accepted)) {
        $matrixViewports += ([string] $viewportSpec.WidthDip + 'x' + [string] $viewportSpec.HeightDip)
    }
    $matrixSet = Test-UiAcceptanceVisualMatrixSet -Entries @($script:entries.ToArray()) `
        -Themes @($script:themeFacts | ForEach-Object { [string] $_.mode }) `
        -Pages @('inbox', 'library', 'taxonomy', 'settings') `
        -Viewports $matrixViewports `
        -RequiredStates $requiredStates -DialogStates $dialogStates
    if (-not $matrixSet.ok) {
        $firstDefect = [string] @($matrixSet.defects)[0]
        throw ('visual.matrix_incomplete.' + (ConvertTo-UiAcceptanceSlug -Value $firstDefect))
    }
    if (@($matrixSet.notPassed).Count -gt 0 -and $status -ceq 'Passed') {
        $status = 'NotRun'
        $reason = 'visual.matrix_state_not_run'
    }

}
catch {
    $failureCode = $_.Exception.Message
    if ([string]::IsNullOrWhiteSpace($failureCode) -or $failureCode -notlike 'visual.*') {
        $failureCode = 'visual.unexpected'
    }
    $mappedStatus = if ($blockedFailureCodes -ccontains $failureCode) { 'Blocked' } else { 'Failed' }
    if ([string]::IsNullOrWhiteSpace($reason)) { $reason = $failureCode }
    else { $reason += '; ' + $failureCode }
    $status = $mappedStatus
}
finally {
    try { Stop-VisualProduct } catch {
        if ($status -cne 'Failed') { $status = 'Failed' }
        $reason += ('; product_stop_failed.' + (ConvertTo-UiAcceptanceSlug -Value ([string] $_.Exception.Message)))
    }
    # Crash safety: a round that failed before it could reach its own restore still owes the
    # original registry state back. The restore only runs for a journal this round proved it owns;
    # otherwise the journal is preserved and the round is reported as unconfirmed. A restore that
    # was already attempted is never re-sent from here, so a repeated action cannot rewrite the
    # first failure.
    if ($script:themePendingRestore -and $null -ne $script:themeRound -and $script:themeRound.RestoreOwed) {
        if ($script:themeRound.RestoreAttempted) {
            $script:themeRestoreFailure = 'restore_already_attempted'
            if ($status -cne 'Failed') { $status = 'Failed' }
            $reason += '; visual.theme_restore_unconfirmed'
        }
        else {
            $outcome = Invoke-UiAcceptanceThemeRestorePhase -Round $script:themeRound `
                -JournalPath $journalPath -SupportPath $SupportPath -FixtureRoot $FixtureRoot -AllowFixture:$AllowFixture -EnforceAdapterSelection -WriteEvidence {
                    param($restoreDecision, $readbackDecision)
                    [void] (Write-UiThemeEvidence -OutputDirectory $OutputDirectory `
                            -EvidenceId 'theme-recovery' -Document ([ordered] @{
                                mode = 'recovery'
                                attempted = [bool] $script:themeRound.Attempted
                                writesPossible = [string] $script:themeRound.WritesPossible
                                ownershipConfirmed = [bool] $script:themeRound.OwnershipConfirmed
                                apply = (Export-UiAcceptanceThemeDecision -Decision $script:themeRound.ApplyDecision -Fields @(
                                    'ok', 'failure', 'ran', 'exitKnown', 'exitCode', 'parseState',
                                    'adapters', 'applied', 'mode', 'journalState', 'journalWritten',
                                    'journalRetained', 'registryWritten', 'readBackVerified',
                                    'broadcastSent', 'originalPresent', 'originalValueKind',
                                    'originalBytes', 'identityMatch', 'bindingMatch',
                                    'ownershipConfirmed', 'handbackTokenPresent', 'writesPossible'))
                                restore = (Export-UiAcceptanceThemeDecision -Decision $restoreDecision -Fields @(
                                    'ok', 'failure', 'ran', 'exitKnown', 'exitCode', 'parseState',
                                    'adapters', 'disposition', 'restored', 'noAction', 'journalState',
                                    'journalPresent', 'journalRemoved', 'journalChecksumValid',
                                    'ownerState', 'identityMatch',
                                    'originalPresent', 'originalValueKind', 'originalBytes',
                                    'readbackPresent', 'readbackValueKind', 'readbackBytes',
                                    'readbackMatches', 'broadcastSent', 'registryRestored', 'bindingMatchesJournal'))
                                readback = (Export-UiAcceptanceThemeDecision -Decision $readbackDecision -Fields @(
                                    'ok', 'failure', 'exitKnown', 'exitCode', 'parseState', 'adapters',
                                    'journalState', 'ownerState', 'startupRecoveryAllowed',
                                    'handbackAccepted', 'handbackRejection'))
                                journal = [ordered] @{
                                    name = 'theme-recovery.json'
                                    closed = [bool] $script:themeRound.JournalClosed
                                    restoreFailure = [string] $restoreDecision.failure
                                }
                            }) -RunToken $RunToken -OwnedRoot $OwnedRoot `
                            -ForbiddenValue ([string] $script:themeRound.HandbackToken))
                }
            $script:themePendingRestore = $false
            if (-not $outcome.ok) {
                $script:themeRestoreFailure = [string] $outcome.failure
                if ($status -cne 'Failed') { $status = 'Failed' }
                # Appended, never substituted: the first failure keeps its place in the reason.
                if ([string] $outcome.failure -ceq 'ownership.unconfirmed') {
                    $reason += '; visual.theme_restore_unconfirmed'
                }
                else {
                    $reason += '; visual.theme_restore_failed'
                }
            }
        }
    }
}

$visual = $null
try {
    # Data only: the Runner hands this to the reporting outlet, which owns the single index.
    $visual = New-VisualMatrix -ThemeMatrix $ThemeMatrix -Status $status -Reason $reason
}
catch {
    if ($status -cne 'Failed') { $status = 'Failed' }
    $reason += ('; visual.matrix_projection_failed.' + (ConvertTo-UiAcceptanceSlug -Value ([string] $_.Exception.Message)))
}

# The capability must not outlive the scenario object in a long-lived host.
if ($null -ne $script:themeRound) { $script:themeRound.HandbackToken = '' }

return [pscustomobject] @{
    ScenarioId = $ScenarioId
    Status     = $status
    Reason     = $reason
    Artifacts  = @($script:artifacts.ToArray())
    Viewports  = @($accepted | ForEach-Object { [pscustomobject] @{
        widthDip = [int] $_.WidthDip
        heightDip = [int] $_.HeightDip
    } })
    Visual     = $visual
}
