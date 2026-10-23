using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TraceFold.Media.UiAcceptance.Support;

/// <summary>The only app-theme mode switch this component is allowed to make.</summary>
public enum ThemeMode
{
    Light,
    Dark,
}

/// <summary>
/// Who owns a temporary theme change. The coordinator refuses to treat a single support CLI
/// child process as the persistent harness owner, so the owner kind is part of the identity
/// and is recorded in the recovery journal.
/// </summary>
public enum ThemeOwnerKind
{
    Unspecified = 0,
    HarnessRunner = 1,
    SupportCli = 2,
}

/// <summary>
/// The outcome of asking whether the journal's recorded owner is still the same process.
/// <see cref="ThemeOwnerState.Dead"/> is only reported when the identity was positively read;
/// anything that cannot be proven is <see cref="ThemeOwnerState.Unknown"/>.
/// </summary>
public enum ThemeOwnerState
{
    Unknown = 0,
    Dead = 1,
    Live = 2,
    PidReused = 3,
}

/// <summary>
/// Registry value kinds the component can round-trip byte-for-byte. The values match
/// <c>Microsoft.Win32.RegistryValueKind</c> so the Windows adapter can cast directly.
/// </summary>
public enum ThemeValueKind
{
    Unknown = 0,
    String = 1,
    ExpandString = 2,
    Binary = 3,
    DWord = 4,
    MultiString = 7,
    QWord = 11,
}

public enum ThemeJournalReadStatus
{
    Missing,
    Read,
    Unreadable,
}

/// <summary>
/// Why a restore is being attempted. The two cases are decided by different rules and are never
/// collapsed into one answer:
/// <see cref="ThemeRestoreDisposition.StartupRecovery"/> is the preflight case, where an older
/// journal may only be recovered once its owner is provably no longer the same live process;
/// <see cref="ThemeRestoreDisposition.Handback"/> is the normal end-of-round case, where the
/// harness that applied the change is still alive and hands it back with the receipt apply gave
/// it. A live owner is refused in the first case and required in the second.
/// </summary>
public enum ThemeRestoreDisposition
{
    StartupRecovery = 0,
    Handback = 1,
}

public enum ThemeRejection
{
    None,
    TargetNotApproved,
    ModeNotSupported,
    OwnerMissingProcessId,
    OwnerMissingStartTime,
    OwnerMissingContext,
    OwnerKindNotPersistentHarness,
    BindingsMissing,
    BindingsIncomplete,
    JournalAlreadyPresent,
    JournalWriteFailed,
    JournalUnreadable,
    JournalCorrupt,
    JournalTargetMismatch,
    OriginalReadFailed,
    OriginalKindUnsupported,
    ApplyWriteFailed,
    RestoreWriteFailed,
    RestoreDeleteFailed,
    ReadBackMismatch,
    BroadcastFailed,
    OwnerLive,
    OwnerUnknown,
    OwnerPidReused,
    OwnerIdentityUnreadable,
    HandbackOwnerMissing,
    HandbackTokenMissing,
    HandbackTokenMismatch,
    HandbackOwnerMismatch,
    HandbackOwnerNotLive,
    // Appended, never inserted: the earlier members keep their numeric values.
    BindingsMismatch,
}

public enum ThemeRestoreAction
{
    None,
    NoJournalNoAction,
    RestoredOriginal,
    AbsentValueRemoved,
}

/// <summary>
/// The identity of the process that requested a temporary theme change. A bare process id is
/// never sufficient: the start time disambiguates a reused id and the context names the owner.
/// </summary>
public sealed record ThemeOwnerIdentity(
    int ProcessId,
    long StartUtcTicks,
    string Context,
    ThemeOwnerKind Kind,
    string? ExecutablePath = null);

public sealed record ThemeOwnerProbe(ThemeOwnerState State, bool IdentityReadable, string Detail);

/// <summary>
/// One hash binding recorded in the journal. The hash kind and the covered members are part of
/// the binding: a digest alone says nothing about what it covers, and a digest for an artifact
/// that does not exist yet must never be substituted for one that does.
/// </summary>
public sealed record ThemeBinding(string HashKind, IReadOnlyList<string> Members, string Digest);

public sealed record ThemeBindings(ThemeBinding Product, ThemeBinding Toolkit);

public sealed record ThemeRegistrySnapshot(bool Present, ThemeValueKind Kind, byte[]? Bytes);

public sealed record ThemeApplyRequest(
    string KeyPath,
    string ValueName,
    ThemeMode Mode,
    string JournalPath,
    ThemeOwnerIdentity Owner,
    ThemeBindings Bindings);

/// <summary>
/// A request to give a journalled original state back. <see cref="Disposition"/> selects the rule
/// set; <see cref="CurrentOwner"/> and <see cref="HandbackToken"/> are only meaningful for
/// <see cref="ThemeRestoreDisposition.Handback"/> and are ignored otherwise. The defaults keep the
/// strictest case: an unspecified restore is start-up recovery, which never takes over from a live
/// owner.
/// <para>
/// <see cref="ExpectedBindings"/> is the binding the caller believes the journal holds. It is
/// decided against the same journal snapshot the restore is about to consume, and it is decided
/// <em>before</em> the registry is written, before the change is broadcast and before the journal
/// is deleted: a restore that would undo the wrong build's change must not run at all. Startup
/// recovery deliberately leaves it null, because an older journal is recovered without adopting
/// the current build's binding.
/// </para>
/// </summary>
public sealed record ThemeRestoreRequest(
    string KeyPath,
    string ValueName,
    string JournalPath,
    ThemeRestoreDisposition Disposition = ThemeRestoreDisposition.StartupRecovery,
    ThemeOwnerIdentity? CurrentOwner = null,
    string? HandbackToken = null,
    ThemeBindings? ExpectedBindings = null);

public sealed record ThemeApplyResult(
    bool Applied,
    ThemeRejection Rejection,
    string JournalPath,
    bool JournalWritten,
    bool RegistryWritten,
    bool AppliedReadBackVerified,
    bool BroadcastSucceeded,
    ThemeRegistrySnapshot? Original,
    ThemeRegistrySnapshot? AppliedReadBack,
    bool JournalRetained,
    string HandbackToken = "");

public sealed record ThemeRestoreResult(
    bool Restored,
    ThemeRejection Rejection,
    ThemeRestoreAction Action,
    bool JournalPresent,
    bool JournalChecksumValid,
    ThemeOwnerIdentity? Owner,
    ThemeOwnerProbe? OwnerProbe,
    bool RegistryRestored,
    bool ReadBackVerified,
    bool BroadcastSucceeded,
    bool JournalRemoved,
    ThemeRegistrySnapshot? ReadBack,
    bool JournalRetained,
    // Returns the exact state the restore consumed. Without it a caller has to read the journal
    // before the call and compare against a snapshot the restore may not have used.
    ThemeRegistrySnapshot? Original = null,
    ThemeBindings? Bindings = null);

/// <summary>
/// What a journal currently allows. The two permissions are reported separately on purpose:
/// <see cref="StartupRecoveryAllowed"/> answers "may an old, provably dead owner's journal be
/// recovered", <see cref="HandbackAccepted"/> answers "may the harness that applied this change
/// hand it back now". <see cref="Rejection"/> belongs to the first question and
/// <see cref="HandbackRejection"/> to the second; a null hand-back rejection means the question was
/// never reached (no journal, unreadable or corrupt journal).
/// </summary>
public sealed record ThemeJournalInspection(
    bool JournalPresent,
    bool JournalReadable,
    bool JournalChecksumValid,
    ThemeOwnerIdentity? Owner,
    ThemeOwnerProbe? OwnerProbe,
    ThemeRejection Rejection,
    bool StartupRecoveryAllowed,
    bool HandbackAccepted,
    ThemeRejection? HandbackRejection = null);

/// <summary>
/// The registry operations the theme rules need. The production implementation talks to the
/// current user's Windows app-theme value; tests declare the presence, kind and bytes they need
/// and can make a write or delete fail on demand.
/// </summary>
public interface IThemeRegistry
{
    /// <summary>Returns <c>null</c> when the value cannot be read at all.</summary>
    ThemeRegistrySnapshot? Read(string keyPath, string valueName);

    bool Write(string keyPath, string valueName, ThemeValueKind kind, byte[] bytes);

    bool Delete(string keyPath, string valueName);
}

public interface IThemeChangeBroadcaster
{
    bool Broadcast();
}

public interface IThemeOwnerInspector
{
    ThemeOwnerProbe Probe(ThemeOwnerIdentity owner);
}

public interface IThemeJournalStore
{
    bool Exists(string path);

    ThemeJournalReadStatus TryRead(string path, out string contents);

    /// <summary>Writes only when the journal does not exist yet, so an existing recovery record is never overwritten.</summary>
    bool TryCreateNew(string path, string contents);

    bool TryDelete(string path);
}

/// <summary>
/// Crash-safe temporary switching of the Windows app-theme value, and nothing else.
/// The component may address exactly one value,
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>,
/// and rejects every other hive, key or value. It never touches
/// <c>SystemUsesLightTheme</c>, high contrast, the wallpaper, an accent colour, a Windows
/// Settings window or any product data directory.
/// </summary>
/// <remarks>
/// Ordering contract: the recovery journal is written with <c>CreateNew</c> semantics and a
/// checksum before the registry is touched, and the journal is removed only after the original
/// value has been written back, read back byte-for-byte, and broadcast. Any failure, refusal or
/// unknown keeps the journal so a later run can recover; nothing is silently closed out.
/// </remarks>
public static class WindowsAppThemeCoordinator
{
    public const string ApprovedKeyPath =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public const string ApprovedValueName = "AppsUseLightTheme";

    public const string JournalDirectoryName = "TraceFold.UiAcceptance";

    public const string JournalFileName = "theme-recovery.json";

    private const int JournalSchemaVersion = 2;

    /// <summary>
    /// Journals written before the hand-back receipt existed are still accepted, but they carry no
    /// digest, so they can only ever be recovered by the start-up path.
    /// </summary>
    private const int LegacyJournalSchemaVersion = 1;

    private const int HandbackSchemaVersion = 2;

    private const string HivePrefix = @"HKEY_CURRENT_USER\";

    /// <summary>
    /// The single production journal location. It deliberately lives outside the product's data
    /// root, so product cleanup can never delete recovery evidence.
    /// </summary>
    public static string DefaultJournalPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        JournalDirectoryName,
        JournalFileName);

    public static ThemeRejection ValidateTarget(string keyPath, string valueName)
    {
        if (string.IsNullOrWhiteSpace(keyPath)
            || !keyPath.StartsWith(HivePrefix, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(keyPath, ApprovedKeyPath, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeRejection.TargetNotApproved;
        }

        if (!string.Equals(valueName, ApprovedValueName, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeRejection.TargetNotApproved;
        }

        return ThemeRejection.None;
    }

    public static ThemeRejection ValidateOwner(ThemeOwnerIdentity owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (owner.ProcessId <= 0)
        {
            return ThemeRejection.OwnerMissingProcessId;
        }

        if (owner.StartUtcTicks <= 0)
        {
            return ThemeRejection.OwnerMissingStartTime;
        }

        if (string.IsNullOrWhiteSpace(owner.Context))
        {
            return ThemeRejection.OwnerMissingContext;
        }

        if (owner.Kind != ThemeOwnerKind.HarnessRunner)
        {
            return ThemeRejection.OwnerKindNotPersistentHarness;
        }

        if (!string.IsNullOrWhiteSpace(owner.ExecutablePath)
            && !Path.IsPathFullyQualified(owner.ExecutablePath))
        {
            return ThemeRejection.OwnerMissingContext;
        }

        return ThemeRejection.None;
    }

    public static ThemeRejection ValidateBindings(ThemeBindings? bindings)
    {
        if (bindings is null)
        {
            return ThemeRejection.BindingsMissing;
        }

        return ValidateBinding(bindings.Product) == ThemeRejection.None
            && ValidateBinding(bindings.Toolkit) == ThemeRejection.None
            ? ThemeRejection.None
            : ThemeRejection.BindingsIncomplete;
    }

    public static ThemeRejection ValidateBinding(ThemeBinding? binding)
    {
        if (binding is null || string.IsNullOrWhiteSpace(binding.HashKind))
        {
            return ThemeRejection.BindingsIncomplete;
        }

        if (binding.Members is null || binding.Members.Count == 0)
        {
            return ThemeRejection.BindingsIncomplete;
        }

        foreach (string member in binding.Members)
        {
            if (string.IsNullOrWhiteSpace(member))
            {
                return ThemeRejection.BindingsIncomplete;
            }
        }

        return IsSha256Hex(binding.Digest)
            ? ThemeRejection.None
            : ThemeRejection.BindingsIncomplete;
    }

    public static int DwordFor(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => 1,
        ThemeMode.Dark => 0,
        _ => -1,
    };

    public static bool IsSupportedOriginalKind(ThemeValueKind kind) => kind is ThemeValueKind.DWord
        or ThemeValueKind.QWord
        or ThemeValueKind.String
        or ThemeValueKind.ExpandString
        or ThemeValueKind.Binary;

    /// <summary>
    /// Applies the requested mode after journalling the exact original state. The journal is
    /// written first and is never removed here: it survives until a verified restore.
    /// </summary>
    public static ThemeApplyResult Apply(
        ThemeApplyRequest request,
        IThemeRegistry registry,
        IThemeJournalStore journals,
        IThemeChangeBroadcaster broadcaster)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(journals);
        ArgumentNullException.ThrowIfNull(broadcaster);

        ThemeRejection target = ValidateTarget(request.KeyPath, request.ValueName);
        if (target != ThemeRejection.None)
        {
            return Failure(request.JournalPath, target);
        }

        int dword = DwordFor(request.Mode);
        if (dword < 0)
        {
            return Failure(request.JournalPath, ThemeRejection.ModeNotSupported);
        }

        ThemeRejection ownerRejection = ValidateOwner(request.Owner);
        if (ownerRejection != ThemeRejection.None)
        {
            return Failure(request.JournalPath, ownerRejection);
        }

        ThemeRejection bindingRejection = ValidateBindings(request.Bindings);
        if (bindingRejection != ThemeRejection.None)
        {
            return Failure(request.JournalPath, bindingRejection);
        }

        if (journals.Exists(request.JournalPath))
        {
            // A second apply must be refused before anything is written: two live journals would
            // make the original state ambiguous and the second one unrecoverable.
            return Failure(request.JournalPath, ThemeRejection.JournalAlreadyPresent);
        }

        ThemeRegistrySnapshot? original = registry.Read(request.KeyPath, request.ValueName);
        if (original is null)
        {
            return Failure(request.JournalPath, ThemeRejection.OriginalReadFailed);
        }

        if (original.Present && !IsSupportedOriginalKind(original.Kind))
        {
            return Failure(request.JournalPath, ThemeRejection.OriginalKindUnsupported, original);
        }

        // The receipt is generated before anything is written and is handed to the caller only.
        // The journal stores its digest, so restoring later requires the caller to present the
        // receipt; copying the journalled fields never reproduces it.
        string handbackToken = NewHandbackToken();
        string createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var journal = new ThemeJournal(
            JournalSchemaVersion,
            createdUtc,
            request.KeyPath,
            request.ValueName,
            original,
            request.Mode.ToString(),
            request.Owner,
            request.Bindings,
            string.Empty,
            ComputeHandbackDigest(
                handbackToken, createdUtc, request.KeyPath, request.ValueName, request.Owner));
        journal = journal with { Checksum = ComputeChecksum(journal) };

        if (!journals.TryCreateNew(request.JournalPath, Serialize(journal)))
        {
            return Failure(request.JournalPath, ThemeRejection.JournalWriteFailed, original);
        }

        byte[] applied = BitConverter.GetBytes(dword);
        if (!registry.Write(request.KeyPath, request.ValueName, ThemeValueKind.DWord, applied))
        {
            // The journal is already persisted, so the original state is owed back and the write
            // that reported failure may still have changed the value. The receipt must travel with
            // the failure: without it the still-live owner can neither hand back (no receipt) nor
            // be taken over by start-up recovery (owner live), which would strand the journal.
            return Failure(
                request.JournalPath,
                ThemeRejection.ApplyWriteFailed,
                original,
                journalWritten: true,
                handbackToken: handbackToken);
        }

        ThemeRegistrySnapshot? readBack = registry.Read(request.KeyPath, request.ValueName);
        bool readBackVerified = readBack is not null
            && readBack.Present
            && readBack.Kind == ThemeValueKind.DWord
            && BytesEqual(readBack.Bytes, applied);

        bool broadcast = broadcaster.Broadcast();
        bool appliedOk = readBackVerified && broadcast;
        return new ThemeApplyResult(
            appliedOk,
            appliedOk ? ThemeRejection.None : ClassifyApplyFailure(readBackVerified, broadcast),
            request.JournalPath,
            JournalWritten: true,
            RegistryWritten: true,
            readBackVerified,
            broadcast,
            original,
            readBack,
            JournalRetained: true,
            HandbackToken: handbackToken);
    }

    /// <summary>
    /// Restores the journalled original state. The journal is removed only after the registry
    /// write succeeded, the read back matched the recorded original exactly, and the change was
    /// broadcast. Every other outcome keeps a recoverable journal.
    /// </summary>
    public static ThemeRestoreResult Restore(
        ThemeRestoreRequest request,
        IThemeRegistry registry,
        IThemeJournalStore journals,
        IThemeChangeBroadcaster broadcaster,
        IThemeOwnerInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(journals);
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(inspector);

        ThemeRejection target = ValidateTarget(request.KeyPath, request.ValueName);
        if (target != ThemeRejection.None)
        {
            return RestoreFailure(request.JournalPath, target, ThemeRestoreAction.None);
        }

        ThemeJournalReadStatus status = journals.TryRead(request.JournalPath, out string contents);
        if (status == ThemeJournalReadStatus.Missing)
        {
            // "No journal" and "restored" are different facts: without a journal nothing was
            // measured, so nothing may be reported as restored.
            return new ThemeRestoreResult(
                false,
                ThemeRejection.None,
                ThemeRestoreAction.NoJournalNoAction,
                JournalPresent: false,
                JournalChecksumValid: false,
                null,
                null,
                false,
                false,
                false,
                false,
                null,
                JournalRetained: false);
        }

        if (status == ThemeJournalReadStatus.Unreadable)
        {
            return RestoreFailure(
                request.JournalPath,
                ThemeRejection.JournalUnreadable,
                ThemeRestoreAction.None,
                journalPresent: true);
        }

        ThemeJournal? journal = TryParse(contents);
        if (journal is null)
        {
            return RestoreFailure(
                request.JournalPath,
                ThemeRejection.JournalCorrupt,
                ThemeRestoreAction.None,
                journalPresent: true);
        }

        bool checksumValid = string.Equals(
            journal.Checksum,
            ComputeChecksum(journal),
            StringComparison.OrdinalIgnoreCase);
        if (!checksumValid)
        {
            return RestoreFailure(
                request.JournalPath,
                ThemeRejection.JournalCorrupt,
                ThemeRestoreAction.None,
                journalPresent: true);
        }

        if (!string.Equals(journal.KeyPath, ApprovedKeyPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(journal.ValueName, ApprovedValueName, StringComparison.OrdinalIgnoreCase))
        {
            return RestoreFailure(
                request.JournalPath,
                ThemeRejection.JournalTargetMismatch,
                ThemeRestoreAction.None,
                journalPresent: true,
                checksumValid: true,
                original: journal.Original,
                bindings: journal.Bindings);
        }

        // The binding is decided from the same journal snapshot the restore is about to consume,
        // and before the probe, the registry write, the broadcast and the delete. A post-hoc
        // comparison would already have undone another build's change by the time it failed.
        if (request.Disposition == ThemeRestoreDisposition.Handback
            && request.ExpectedBindings is not null
            && !BindingsEqual(request.ExpectedBindings, journal.Bindings))
        {
            return RestoreFailure(
                request.JournalPath,
                ThemeRejection.BindingsMismatch,
                ThemeRestoreAction.None,
                journalPresent: true,
                checksumValid: true,
                owner: journal.Owner,
                original: journal.Original,
                bindings: journal.Bindings);
        }

        ThemeOwnerProbe probe = inspector.Probe(journal.Owner);
        ThemeRejection ownerRejection = request.Disposition == ThemeRestoreDisposition.Handback
            ? EvaluateHandback(request, journal, probe)
            : EvaluateStartupRecovery(probe);
        if (ownerRejection != ThemeRejection.None)
        {
            return RestoreFailure(
                request.JournalPath,
                ownerRejection,
                ThemeRestoreAction.None,
                journalPresent: true,
                checksumValid: true,
                owner: journal.Owner,
                ownerProbe: probe,
                original: journal.Original,
                bindings: journal.Bindings);
        }

        bool restored = journal.Original.Present
            ? registry.Write(
                request.KeyPath,
                request.ValueName,
                journal.Original.Kind,
                journal.Original.Bytes ?? Array.Empty<byte>())
            : registry.Delete(request.KeyPath, request.ValueName);

        if (!restored)
        {
            return RestoreFailure(
                request.JournalPath,
                journal.Original.Present
                    ? ThemeRejection.RestoreWriteFailed
                    : ThemeRejection.RestoreDeleteFailed,
                ThemeRestoreAction.None,
                journalPresent: true,
                checksumValid: true,
                owner: journal.Owner,
                ownerProbe: probe,
                original: journal.Original,
                bindings: journal.Bindings);
        }

        ThemeRegistrySnapshot? readBack = registry.Read(request.KeyPath, request.ValueName);
        bool readBackVerified = SnapshotMatches(readBack, journal.Original);
        bool broadcast = broadcaster.Broadcast();

        if (!readBackVerified)
        {
            return RestoreFailure(
                request.JournalPath,
                ThemeRejection.ReadBackMismatch,
                ThemeRestoreAction.None,
                journalPresent: true,
                checksumValid: true,
                owner: journal.Owner,
                ownerProbe: probe,
                registryRestored: true,
                readBack: readBack,
                original: journal.Original,
                bindings: journal.Bindings);
        }

        if (!broadcast)
        {
            return RestoreFailure(
                request.JournalPath,
                ThemeRejection.BroadcastFailed,
                ThemeRestoreAction.None,
                journalPresent: true,
                checksumValid: true,
                owner: journal.Owner,
                ownerProbe: probe,
                registryRestored: true,
                readBackVerified: true,
                readBack: readBack,
                original: journal.Original,
                bindings: journal.Bindings);
        }

        bool removed = journals.TryDelete(request.JournalPath);
        return new ThemeRestoreResult(
            removed,
            removed ? ThemeRejection.None : ThemeRejection.JournalUnreadable,
            journal.Original.Present
                ? ThemeRestoreAction.RestoredOriginal
                : ThemeRestoreAction.AbsentValueRemoved,
            JournalPresent: true,
            JournalChecksumValid: true,
            journal.Owner,
            probe,
            RegistryRestored: true,
            ReadBackVerified: true,
            BroadcastSucceeded: true,
            removed,
            readBack,
            JournalRetained: !removed,
            Original: journal.Original,
            Bindings: journal.Bindings);
    }

    /// <summary>
    /// Runner preflight: reports what a journal currently says and whether restoring it is
    /// allowed. It never writes to the registry and never removes the journal.
    /// </summary>
    public static ThemeJournalInspection Inspect(
        string journalPath,
        IThemeJournalStore journals,
        IThemeOwnerInspector inspector,
        string? handbackToken = null,
        ThemeOwnerIdentity? currentOwner = null)
    {
        ArgumentNullException.ThrowIfNull(journals);
        ArgumentNullException.ThrowIfNull(inspector);

        ThemeJournalReadStatus status = journals.TryRead(journalPath, out string contents);
        if (status == ThemeJournalReadStatus.Missing)
        {
            return new ThemeJournalInspection(false, false, false, null, null,
                ThemeRejection.None, false, false);
        }

        if (status == ThemeJournalReadStatus.Unreadable)
        {
            return new ThemeJournalInspection(true, false, false, null, null,
                ThemeRejection.JournalUnreadable, false, false);
        }

        ThemeJournal? journal = TryParse(contents);
        if (journal is null)
        {
            return new ThemeJournalInspection(true, true, false, null, null,
                ThemeRejection.JournalCorrupt, false, false);
        }

        bool checksumValid = string.Equals(
            journal.Checksum,
            ComputeChecksum(journal),
            StringComparison.OrdinalIgnoreCase);
        if (!checksumValid)
        {
            return new ThemeJournalInspection(true, true, false, journal.Owner, null,
                ThemeRejection.JournalCorrupt, false, false);
        }

        ThemeOwnerProbe probe = inspector.Probe(journal.Owner);
        ThemeRejection rejection = EvaluateStartupRecovery(probe);
        ThemeRejection handback = EvaluateHandback(
            new ThemeRestoreRequest(
                journal.KeyPath,
                journal.ValueName,
                journalPath,
                ThemeRestoreDisposition.Handback,
                currentOwner,
                handbackToken),
            journal,
            probe);

        return new ThemeJournalInspection(
            true,
            true,
            true,
            journal.Owner,
            probe,
            rejection,
            rejection == ThemeRejection.None,
            handback == ThemeRejection.None,
            handback);
    }

    /// <summary>Serialises a journal in a fixed field order so the checksum is reproducible.</summary>
    public static string Serialize(ThemeJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", journal.SchemaVersion);
            writer.WriteString("createdUtc", journal.CreatedUtc);
            writer.WriteString("keyPath", journal.KeyPath);
            writer.WriteString("valueName", journal.ValueName);
            writer.WriteBoolean("originalPresent", journal.Original.Present);
            writer.WriteString("originalKind", journal.Original.Kind.ToString());
            if (journal.Original.Bytes is null)
            {
                writer.WriteNull("originalBytes");
            }
            else
            {
                writer.WriteBase64String("originalBytes", journal.Original.Bytes);
            }

            writer.WriteString("requestedMode", journal.RequestedMode);
            writer.WriteStartObject("owner");
            writer.WriteNumber("processId", journal.Owner.ProcessId);
            writer.WriteNumber("startUtcTicks", journal.Owner.StartUtcTicks);
            writer.WriteString("context", journal.Owner.Context);
            writer.WriteString("kind", journal.Owner.Kind.ToString());
            if (journal.Owner.ExecutablePath is null)
            {
                writer.WriteNull("executablePath");
            }
            else
            {
                writer.WriteString("executablePath", journal.Owner.ExecutablePath);
            }

            writer.WriteEndObject();
            writer.WriteStartObject("bindings");
            WriteBinding(writer, "product", journal.Bindings.Product);
            WriteBinding(writer, "toolkit", journal.Bindings.Toolkit);
            writer.WriteEndObject();
            if (journal.SchemaVersion >= HandbackSchemaVersion)
            {
                // Only the digest is persisted, never the receipt itself.
                writer.WriteString("handbackTokenDigest", journal.HandbackTokenDigest ?? string.Empty);
            }

            writer.WriteString("checksum", journal.Checksum);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string ComputeChecksum(ThemeJournal journal) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Serialize(journal with { Checksum = string.Empty }))));

    public static ThemeJournal? TryParse(string contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(contents);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out JsonElement version))
            {
                return null;
            }

            int schemaVersion = version.GetInt32();
            if (schemaVersion != JournalSchemaVersion
                && schemaVersion != LegacyJournalSchemaVersion)
            {
                return null;
            }

            string? handbackTokenDigest = schemaVersion >= HandbackSchemaVersion
                ? RequiredString(root, "handbackTokenDigest")
                : null;

            string keyPath = RequiredString(root, "keyPath");
            string valueName = RequiredString(root, "valueName");
            string mode = RequiredString(root, "requestedMode");
            string created = RequiredString(root, "createdUtc");
            string checksum = RequiredString(root, "checksum");
            bool present = root.GetProperty("originalPresent").GetBoolean();
            string kindName = RequiredString(root, "originalKind");
            if (!Enum.TryParse(kindName, out ThemeValueKind kind))
            {
                return null;
            }

            byte[]? bytes = null;
            if (root.TryGetProperty("originalBytes", out JsonElement bytesElement)
                && bytesElement.ValueKind == JsonValueKind.String)
            {
                bytes = bytesElement.GetBytesFromBase64();
            }

            JsonElement ownerElement = root.GetProperty("owner");
            int processId = ownerElement.GetProperty("processId").GetInt32();
            long startTicks = ownerElement.GetProperty("startUtcTicks").GetInt64();
            string context = RequiredString(ownerElement, "context");
            if (!Enum.TryParse(RequiredString(ownerElement, "kind"), out ThemeOwnerKind ownerKind))
            {
                return null;
            }

            string? executablePath = ownerElement.TryGetProperty("executablePath", out JsonElement exe)
                && exe.ValueKind == JsonValueKind.String
                ? exe.GetString()
                : null;

            JsonElement bindings = root.GetProperty("bindings");
            ThemeBinding? product = TryReadBinding(bindings, "product");
            ThemeBinding? toolkit = TryReadBinding(bindings, "toolkit");
            if (product is null || toolkit is null)
            {
                return null;
            }

            if (ValidateTarget(keyPath, valueName) != ThemeRejection.None)
            {
                return null;
            }

            var journal = new ThemeJournal(
                schemaVersion,
                created,
                keyPath,
                valueName,
                new ThemeRegistrySnapshot(present, kind, bytes),
                mode,
                new ThemeOwnerIdentity(processId, startTicks, context, ownerKind, executablePath),
                new ThemeBindings(product, toolkit),
                checksum,
                handbackTokenDigest);
            return ValidateOwner(journal.Owner) == ThemeRejection.None
                && ValidateBindings(journal.Bindings) == ThemeRejection.None
                ? journal
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public static bool SnapshotMatches(ThemeRegistrySnapshot? actual, ThemeRegistrySnapshot expected)
    {
        if (actual is null)
        {
            return false;
        }

        if (actual.Present != expected.Present || actual.Kind != expected.Kind)
        {
            return false;
        }

        return BytesEqual(actual.Bytes, expected.Bytes);
    }

    public static bool BytesEqual(byte[]? left, byte[]? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.AsSpan().SequenceEqual(right);
    }

    /// <summary>Stable snake_case reason codes for CLI output.</summary>
    public static string ReasonCode(ThemeRejection rejection) => rejection switch
    {
        ThemeRejection.None => "theme.none",
        ThemeRejection.TargetNotApproved => "theme.target_not_approved",
        ThemeRejection.ModeNotSupported => "theme.mode_not_supported",
        ThemeRejection.OwnerMissingProcessId => "theme.owner_missing_process_id",
        ThemeRejection.OwnerMissingStartTime => "theme.owner_missing_start_time",
        ThemeRejection.OwnerMissingContext => "theme.owner_missing_context",
        ThemeRejection.OwnerKindNotPersistentHarness => "theme.owner_kind_not_persistent_harness",
        ThemeRejection.BindingsMissing => "theme.bindings_missing",
        ThemeRejection.BindingsIncomplete => "theme.bindings_incomplete",
        ThemeRejection.BindingsMismatch => "theme.bindings_mismatch",
        ThemeRejection.JournalAlreadyPresent => "theme.journal_already_present",
        ThemeRejection.JournalWriteFailed => "theme.journal_write_failed",
        ThemeRejection.JournalUnreadable => "theme.journal_unreadable",
        ThemeRejection.JournalCorrupt => "theme.journal_corrupt",
        ThemeRejection.JournalTargetMismatch => "theme.journal_target_mismatch",
        ThemeRejection.OriginalReadFailed => "theme.original_read_failed",
        ThemeRejection.OriginalKindUnsupported => "theme.original_kind_unsupported",
        ThemeRejection.ApplyWriteFailed => "theme.apply_write_failed",
        ThemeRejection.RestoreWriteFailed => "theme.restore_write_failed",
        ThemeRejection.RestoreDeleteFailed => "theme.restore_delete_failed",
        ThemeRejection.ReadBackMismatch => "theme.read_back_mismatch",
        ThemeRejection.BroadcastFailed => "theme.broadcast_failed",
        ThemeRejection.OwnerLive => "theme.owner_live",
        ThemeRejection.OwnerUnknown => "theme.owner_unknown",
        ThemeRejection.OwnerPidReused => "theme.owner_pid_reused",
        ThemeRejection.OwnerIdentityUnreadable => "theme.owner_identity_unreadable",
        ThemeRejection.HandbackOwnerMissing => "theme.handback_owner_missing",
        ThemeRejection.HandbackTokenMissing => "theme.handback_token_missing",
        ThemeRejection.HandbackTokenMismatch => "theme.handback_token_mismatch",
        ThemeRejection.HandbackOwnerMismatch => "theme.handback_owner_mismatch",
        ThemeRejection.HandbackOwnerNotLive => "theme.handback_owner_not_live",
        _ => "theme.unknown",
    };

    /// <summary>Creates the receipt apply hands to its caller. Never persisted in the clear.</summary>
    public static string NewHandbackToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The digest of a receipt, bound to the apply instance it was issued for: the same token
    /// presented for a different created instant, target or owner does not match.
    /// </summary>
    public static string ComputeHandbackDigest(
        string token,
        string createdUtc,
        string keyPath,
        string valueName,
        ThemeOwnerIdentity owner) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            '\u001f',
            "tracefold.theme.handback.v1",
            token,
            createdUtc,
            keyPath,
            valueName,
            owner.ProcessId.ToString(CultureInfo.InvariantCulture),
            owner.StartUtcTicks.ToString(CultureInfo.InvariantCulture),
            owner.Context))));

    /// <summary>
    /// Compares two bindings field by field. A digest alone is not a binding: the hash kind and the
    /// member set the digest covers are part of it, so both are compared exactly. One
    /// implementation, used by the pre-action restore gate and by the command projection.
    /// </summary>
    public static bool BindingsEqual(ThemeBindings left, ThemeBindings right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return BindingEquals(left.Product, right.Product) && BindingEquals(left.Toolkit, right.Toolkit);
    }

    private static bool BindingEquals(ThemeBinding left, ThemeBinding right) =>
        string.Equals(left.HashKind, right.HashKind, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Digest, right.Digest, StringComparison.OrdinalIgnoreCase)
        && left.Members.Count == right.Members.Count
        && left.Members.Zip(right.Members, (a, b) => string.Equals(a, b, StringComparison.Ordinal))
            .All(equal => equal);

    /// <summary>Compares two identities the way the hand-back path requires: no field is optional.</summary>
    public static bool OwnerIdentityEquals(ThemeOwnerIdentity left, ThemeOwnerIdentity right) =>
        left.ProcessId == right.ProcessId
        && left.StartUtcTicks == right.StartUtcTicks
        && left.Kind == right.Kind
        && string.Equals(left.Context, right.Context, StringComparison.Ordinal)
        && string.Equals(
            left.ExecutablePath ?? string.Empty,
            right.ExecutablePath ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Start-up recovery: a journal may be recovered only when its owner is provably not the same
    /// live process any more. Everything that cannot prove that keeps the journal.
    /// </summary>
    public static ThemeRejection EvaluateStartupRecovery(ThemeOwnerProbe probe) => probe.State switch
    {
        ThemeOwnerState.Live => ThemeRejection.OwnerLive,
        ThemeOwnerState.PidReused => ThemeRejection.OwnerPidReused,
        ThemeOwnerState.Unknown => ThemeRejection.OwnerUnknown,
        _ => probe.IdentityReadable
            ? ThemeRejection.None
            : ThemeRejection.OwnerIdentityUnreadable,
    };

    /// <summary>
    /// Hand-back: the caller must be the very harness process that applied the change, it must
    /// still be the same live process, and it must present the receipt apply issued for that apply.
    /// Copying the journalled owner fields, declaring a support-CLI identity, or presenting an
    /// "owner = true" style flag never satisfies this.
    /// </summary>
    public static ThemeRejection EvaluateHandback(
        ThemeRestoreRequest request,
        ThemeJournal journal,
        ThemeOwnerProbe probe)
    {
        if (request.CurrentOwner is null)
        {
            return ThemeRejection.HandbackOwnerMissing;
        }

        ThemeRejection ownerShape = ValidateOwner(request.CurrentOwner);
        if (ownerShape != ThemeRejection.None)
        {
            return ownerShape;
        }

        if (string.IsNullOrWhiteSpace(request.HandbackToken))
        {
            return ThemeRejection.HandbackTokenMissing;
        }

        if (journal.HandbackTokenDigest is null
            || !DigestEquals(
                journal.HandbackTokenDigest,
                ComputeHandbackDigest(
                    request.HandbackToken,
                    journal.CreatedUtc,
                    journal.KeyPath,
                    journal.ValueName,
                    journal.Owner)))
        {
            return ThemeRejection.HandbackTokenMismatch;
        }

        if (!OwnerIdentityEquals(request.CurrentOwner, journal.Owner))
        {
            return ThemeRejection.HandbackOwnerMismatch;
        }

        return probe.State == ThemeOwnerState.Live
            ? probe.IdentityReadable
                ? ThemeRejection.None
                : ThemeRejection.OwnerIdentityUnreadable
            : probe.State == ThemeOwnerState.Dead && probe.IdentityReadable
                ? ThemeRejection.HandbackOwnerNotLive
                : EvaluateStartupRecovery(probe);
    }

    private static bool DigestEquals(string expected, string actual)
    {
        if (!IsSha256Hex(expected) || !IsSha256Hex(actual))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expected),
            Convert.FromHexString(actual));
    }

    /// <summary>
    /// A refused or failed apply. <paramref name="handbackToken"/> is only ever carried when the
    /// journal was actually persisted: an unpersisted journal leaves nothing owed back, so no
    /// receipt exists and none may be invented.
    /// </summary>
    private static ThemeApplyResult Failure(
        string journalPath,
        ThemeRejection rejection,
        ThemeRegistrySnapshot? original = null,
        bool journalWritten = false,
        string handbackToken = "") =>
        new(false, rejection, journalPath, journalWritten, false, false, false, original, null,
            journalWritten, journalWritten ? handbackToken : string.Empty);

    private static ThemeRestoreResult RestoreFailure(
        string journalPath,
        ThemeRejection rejection,
        ThemeRestoreAction action,
        bool journalPresent = false,
        bool checksumValid = false,
        ThemeOwnerIdentity? owner = null,
        ThemeOwnerProbe? ownerProbe = null,
        bool registryRestored = false,
        bool readBackVerified = false,
        ThemeRegistrySnapshot? readBack = null,
        ThemeRegistrySnapshot? original = null,
        ThemeBindings? bindings = null) =>
        new(false, rejection, action, journalPresent, checksumValid, owner, ownerProbe,
            registryRestored, readBackVerified, false, false, readBack, journalPresent,
            original, bindings);

    private static ThemeRejection ClassifyApplyFailure(bool readBackVerified, bool broadcast) =>
        readBackVerified ? ThemeRejection.BroadcastFailed : ThemeRejection.ReadBackMismatch;

    private static void WriteBinding(Utf8JsonWriter writer, string name, ThemeBinding binding)
    {
        writer.WriteStartObject(name);
        writer.WriteString("hashKind", binding.HashKind);
        writer.WriteStartArray("members");
        foreach (string member in binding.Members)
        {
            writer.WriteStringValue(member);
        }

        writer.WriteEndArray();
        writer.WriteString("digest", binding.Digest);
        writer.WriteEndObject();
    }

    private static ThemeBinding? TryReadBinding(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string hashKind = RequiredString(element, "hashKind");
        string digest = RequiredString(element, "digest");
        if (!element.TryGetProperty("members", out JsonElement members)
            || members.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<string>();
        foreach (JsonElement member in members.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            list.Add(member.GetString() ?? string.Empty);
        }

        ThemeBinding binding = new(hashKind, list, digest);
        return ValidateBinding(binding) == ThemeRejection.None ? binding : null;
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw new KeyNotFoundException(name);

    private static bool IsSha256Hex(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool hex = (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F');
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>The persisted recovery record. Immutable, so the checksum always covers one value.</summary>
public sealed record ThemeJournal(
    int SchemaVersion,
    string CreatedUtc,
    string KeyPath,
    string ValueName,
    ThemeRegistrySnapshot Original,
    string RequestedMode,
    ThemeOwnerIdentity Owner,
    ThemeBindings Bindings,
    string Checksum,
    string? HandbackTokenDigest = null);

/// <summary>
/// The production journal store. It writes with <c>CreateNew</c> so an existing recovery record
/// is never overwritten, and it never deletes a journal that a caller did not name.
/// </summary>
public sealed class SystemThemeJournalStore : IThemeJournalStore
{
    /// <summary>Diagnostic counter. Only the production store increments it; tests assert it stays zero.</summary>
    public static int InvocationCount { get; private set; }

    public bool Exists(string path)
    {
        InvocationCount++;
        return File.Exists(path);
    }

    public ThemeJournalReadStatus TryRead(string path, out string contents)
    {
        InvocationCount++;
        contents = string.Empty;
        if (!File.Exists(path))
        {
            return ThemeJournalReadStatus.Missing;
        }

        try
        {
            contents = File.ReadAllText(path);
            return ThemeJournalReadStatus.Read;
        }
        catch (IOException)
        {
            return ThemeJournalReadStatus.Unreadable;
        }
        catch (UnauthorizedAccessException)
        {
            return ThemeJournalReadStatus.Unreadable;
        }
    }

    public bool TryCreateNew(string path, string contents)
    {
        InvocationCount++;
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(contents);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryDelete(string path)
    {
        InvocationCount++;
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// The production registry adapter. It can only ever address the approved app-theme value under
/// <c>HKEY_CURRENT_USER</c>; every other path is refused before a handle is opened.
/// </summary>
public sealed class WindowsThemeRegistry : IThemeRegistry
{
    /// <summary>Diagnostic counter. Only the production adapter increments it; tests assert it stays zero.</summary>
    public static int InvocationCount { get; private set; }

    public ThemeRegistrySnapshot? Read(string keyPath, string valueName)
    {
        InvocationCount++;
        if (!TryOpen(keyPath, out string subPath))
        {
            return null;
        }

        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(subPath, false);
            if (key is null)
            {
                return new ThemeRegistrySnapshot(false, ThemeValueKind.Unknown, null);
            }

            object? value = key.GetValue(valueName, null,
                Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null)
            {
                return new ThemeRegistrySnapshot(false, ThemeValueKind.Unknown, null);
            }

            Microsoft.Win32.RegistryValueKind kind = key.GetValueKind(valueName);
            return new ThemeRegistrySnapshot(true, (ThemeValueKind)kind, ToBytes(value, kind));
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool Write(string keyPath, string valueName, ThemeValueKind kind, byte[] bytes)
    {
        InvocationCount++;
        if (!TryOpen(keyPath, out string subPath))
        {
            return false;
        }

        if (WindowsAppThemeCoordinator.ValidateTarget(keyPath, valueName)
            != ThemeRejection.None)
        {
            return false;
        }

        try
        {
            using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser
                .CreateSubKey(subPath)
                ?? throw new IOException("The app-theme key could not be opened for writing.");
            key.SetValue(valueName, FromBytes(bytes, kind), (Microsoft.Win32.RegistryValueKind)kind);
            return true;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool Delete(string keyPath, string valueName)
    {
        InvocationCount++;
        if (!TryOpen(keyPath, out string subPath))
        {
            return false;
        }

        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(subPath, true);
            if (key is null)
            {
                return true;
            }

            if (key.GetValue(valueName) is null)
            {
                return true;
            }

            key.DeleteValue(valueName, false);
            return true;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryOpen(string keyPath, out string subPath)
    {
        subPath = string.Empty;
        if (WindowsAppThemeCoordinator.ValidateTarget(keyPath,
                WindowsAppThemeCoordinator.ApprovedValueName) != ThemeRejection.None)
        {
            return false;
        }

        subPath = keyPath[@"HKEY_CURRENT_USER\".Length..];
        return true;
    }

    private static byte[]? ToBytes(object value, Microsoft.Win32.RegistryValueKind kind) => kind switch
    {
        Microsoft.Win32.RegistryValueKind.DWord => BitConverter.GetBytes((int)value),
        Microsoft.Win32.RegistryValueKind.QWord => BitConverter.GetBytes((long)value),
        Microsoft.Win32.RegistryValueKind.String => Encoding.Unicode.GetBytes((string)value),
        Microsoft.Win32.RegistryValueKind.ExpandString => Encoding.Unicode.GetBytes((string)value),
        Microsoft.Win32.RegistryValueKind.Binary => (byte[])value,
        _ => null,
    };

    private static object FromBytes(byte[] bytes, ThemeValueKind kind) => kind switch
    {
        ThemeValueKind.DWord => BitConverter.ToInt32(bytes, 0),
        ThemeValueKind.QWord => BitConverter.ToInt64(bytes, 0),
        ThemeValueKind.String => Encoding.Unicode.GetString(bytes),
        ThemeValueKind.ExpandString => Encoding.Unicode.GetString(bytes),
        ThemeValueKind.Binary => bytes,
        _ => bytes,
    };
}

/// <summary>
/// Broadcasts the setting change the way the Windows shell expects. Never used by unit tests:
/// they inject a broadcaster of their own.
/// </summary>
public sealed class WindowsThemeChangeBroadcaster : IThemeChangeBroadcaster
{
    /// <summary>Diagnostic counter. Only the production broadcaster increments it; tests assert it stays zero.</summary>
    public static int InvocationCount { get; private set; }

    private const uint WM_SETTINGCHANGE = 0x001A;

    private const nuint HWND_BROADCAST = 0xFFFF;

    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public bool Broadcast()
    {
        InvocationCount++;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        IntPtr result;
        _ = SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "ImmersiveColorSet",
            SMTO_ABORTIFHUNG, 2000, out result);
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendMessageTimeout(
        nuint window,
        uint message,
        UIntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}

/// <summary>
/// Asks Windows whether the journalled owner is still the same process. A live process whose
/// start time differs is reported as <see cref="ThemeOwnerState.PidReused"/>, never as live, and
/// an identity that cannot be read is always <see cref="ThemeOwnerState.Unknown"/>.
/// </summary>
public sealed class WindowsThemeOwnerInspector : IThemeOwnerInspector
{
    /// <summary>Diagnostic counter. Only the production inspector increments it; tests assert it stays zero.</summary>
    public static int InvocationCount { get; private set; }

    public ThemeOwnerProbe Probe(ThemeOwnerIdentity owner)
    {
        InvocationCount++;
        ArgumentNullException.ThrowIfNull(owner);
        if (owner.ProcessId <= 0 || owner.StartUtcTicks <= 0)
        {
            return new ThemeOwnerProbe(ThemeOwnerState.Unknown, false, "incomplete-identity");
        }

        try
        {
            using Process process = Process.GetProcessById(owner.ProcessId);
            long startTicks = process.StartTime.ToUniversalTime().Ticks;
            bool sameStart = startTicks == owner.StartUtcTicks;
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                path = null;
            }

            bool samePath = string.IsNullOrWhiteSpace(owner.ExecutablePath)
                || (path is not null
                    && string.Equals(Path.GetFullPath(path), Path.GetFullPath(owner.ExecutablePath),
                        StringComparison.OrdinalIgnoreCase));

            if (!sameStart)
            {
                return new ThemeOwnerProbe(ThemeOwnerState.PidReused, true, "start-time-mismatch");
            }

            if (!samePath)
            {
                return new ThemeOwnerProbe(ThemeOwnerState.PidReused, true, "executable-mismatch");
            }

            if (process.HasExited)
            {
                return new ThemeOwnerProbe(ThemeOwnerState.Dead, true, "exited");
            }

            return new ThemeOwnerProbe(ThemeOwnerState.Live, true, "live");
        }
        catch (ArgumentException)
        {
            return new ThemeOwnerProbe(ThemeOwnerState.Dead, true, "absent");
        }
        catch (InvalidOperationException)
        {
            return new ThemeOwnerProbe(ThemeOwnerState.Unknown, false, "identity-unreadable");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new ThemeOwnerProbe(ThemeOwnerState.Unknown, false, "identity-unreadable");
        }
    }
}
