namespace TraceFold.Media.UiAcceptance.Support;

/// <summary>
/// Why a read-only native UIA lookup was refused. Every refusal is a precondition failure, so
/// the caller can always distinguish "the harness cannot ask" from "the product answered".
/// </summary>
public enum UiLookupRejection
{
    None,
    MissingAutomationId,
    MissingProcessId,
    UnexpectedExecutable,
    UnownedProcess,
    WindowUnavailable,
    UnknownPatternName,
}

/// <summary>
/// Availability of one UIA pattern. <c>null</c> means the query did not establish an answer: it
/// must never be read as "unsupported" nor as "supported".
/// </summary>
public sealed record UiPatternFact(string Name, bool? Available);

/// <summary>
/// One exact-AutomationId lookup, optionally scoped to a unique descendant of a given class (and
/// optionally a given accessible name). Every product fact is nullable: a nil value always means
/// the query did not establish it, never that the product lacks it.
/// </summary>
public sealed record UiElementFact(
    int OuterMatchCount,
    int DescendantMatchCount,
    string? Name,
    string? ControlType,
    string? ClassName,
    bool? IsEnabled,
    bool? IsOffscreen,
    int? X,
    int? Y,
    int? Width,
    int? Height,
    IReadOnlyList<UiPatternFact> Patterns);

/// <summary>
/// The Windows UIA client operations the probe needs. Production uses the managed UIA client;
/// tests supply a fake, so the ownership gate, the lookup, the descendant scoping and the null
/// semantics are all verifiable without a real window.
/// </summary>
public interface IUiAutomationNative
{
    /// <summary>
    /// Returns the facts for an exact AutomationId match inside the window subtree, or
    /// <c>null</c> when the window or the UIA client itself is unavailable. When
    /// <paramref name="descendantClassName"/> is set, the reported facts belong to the unique
    /// descendant of that class rather than to the matched element itself.
    /// </summary>
    UiElementFact? FindByAutomationId(
        IntPtr window,
        string automationId,
        IReadOnlyList<string> patternNames,
        string? descendantClassName,
        string? descendantName);
}

/// <summary>
/// Read-only native UIA verification for the acceptance gate. It only ever reads: it invokes
/// nothing, sets no value, moves no focus and creates no provider. Its whole purpose is to answer
/// the questions the pinned driver cannot - the real native control type, whether a required UIA
/// pattern is genuinely available, and (for composite controls) whether that pattern lives on a
/// unique descendant rather than on the stable outer element.
/// </summary>
public static class UiAutomationProbe
{
    public const string ExpectedExecutableName = "TraceFold.Media";

    /// <summary>
    /// The pattern names the contract may require. This list is names only: the mapping to the
    /// managed UIA pattern types lives in the Windows client, so this file stays pure. A name
    /// outside this set is a contract error, not an "unsupported" answer.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownPatternNames =
    [
        "Invoke",
        "SelectionItem",
        "Selection",
        "ExpandCollapse",
        "Toggle",
        "Value",
        "Scroll",
    ];

    public static bool IsKnownPatternName(string name) =>
        !string.IsNullOrEmpty(name) && KnownPatternNames.Contains(name, StringComparer.Ordinal);

    public static UiLookupRejection Validate(
        string automationId,
        int processId,
        string executableName,
        bool ownedByRunner,
        IntPtr window)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return UiLookupRejection.MissingAutomationId;
        }

        if (processId <= 0)
        {
            return UiLookupRejection.MissingProcessId;
        }

        string name = executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executableName[..^4]
            : executableName;
        if (!string.Equals(name, ExpectedExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return UiLookupRejection.UnexpectedExecutable;
        }

        if (!ownedByRunner)
        {
            return UiLookupRejection.UnownedProcess;
        }

        return window == IntPtr.Zero ? UiLookupRejection.WindowUnavailable : UiLookupRejection.None;
    }

    /// <summary>
    /// Looks one element up by exact AutomationId, optionally resolving its unique descendant of
    /// a given class. Returns <c>null</c> only when the probe could not answer at all; an answered
    /// lookup always reports both match counts, including zero, so "absent" and "could not ask"
    /// never collapse into each other.
    /// </summary>
    public static UiElementFact? Lookup(
        IntPtr window,
        string automationId,
        IReadOnlyList<string> patternNames,
        IUiAutomationNative native,
        string? descendantClassName = null,
        string? descendantName = null)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(patternNames);
        foreach (string patternName in patternNames)
        {
            if (!IsKnownPatternName(patternName))
            {
                throw new ArgumentException(
                    "Unknown UIA pattern name: " + patternName, nameof(patternNames));
            }
        }

        if (window == IntPtr.Zero || string.IsNullOrWhiteSpace(automationId))
        {
            return null;
        }

        if (descendantClassName is not null && string.IsNullOrWhiteSpace(descendantClassName))
        {
            // An empty class filter would silently match everything, so it is a contract error.
            throw new ArgumentException("The descendant class filter is empty.", nameof(descendantClassName));
        }

        return native.FindByAutomationId(
            window, automationId, patternNames, descendantClassName, descendantName);
    }
}
