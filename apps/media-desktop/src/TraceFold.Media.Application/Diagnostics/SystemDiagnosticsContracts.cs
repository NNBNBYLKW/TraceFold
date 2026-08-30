namespace TraceFold.Media.Application.Diagnostics;

public enum SystemDiagnosticSeverity
{
    Healthy,
    Attention,
    Unavailable
}

public enum SystemDiagnosticCategory
{
    FoldersAndStorage,
    Database,
    ProcessingTools,
    BackgroundWork,
    RuntimeAndLogs
}

public enum SystemDiagnosticFactSensitivity
{
    Shareable,
    LocalOnly
}

public enum SystemDiagnosticActionKind
{
    NavigateToFolderSettings,
    OpenRegisteredLogFolder,
    CopyErrorCode
}

public enum SystemDiagnosticActionTarget
{
    None,
    InboxDirectory,
    LibraryDirectory,
    WorkDirectory
}

public sealed record SystemDiagnosticFact
{
    private SystemDiagnosticFact(
        string key,
        string label,
        string value,
        SystemDiagnosticFactSensitivity sensitivity,
        int order)
    {
        Key = key;
        Label = label;
        Value = value;
        Sensitivity = sensitivity;
        Order = order;
    }

    public string Key { get; }

    public string Label { get; }

    public string Value { get; }

    public SystemDiagnosticFactSensitivity Sensitivity { get; }

    public int Order { get; }

    public static SystemDiagnosticFact Create(
        string key,
        string label,
        string value,
        SystemDiagnosticFactSensitivity sensitivity,
        int order)
    {
        DiagnosticContractGuard.RequireIdentifier(key, nameof(key));
        DiagnosticContractGuard.RequireText(label, nameof(label));
        DiagnosticContractGuard.RequireText(value, nameof(value));
        DiagnosticContractGuard.RequireEnum(sensitivity, nameof(sensitivity));
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        return new(key, label, value, sensitivity, order);
    }
}

public sealed record SystemDiagnosticAction
{
    private SystemDiagnosticAction(
        SystemDiagnosticActionKind kind,
        SystemDiagnosticActionTarget target,
        string label)
    {
        Kind = kind;
        Target = target;
        Label = label;
    }

    public SystemDiagnosticActionKind Kind { get; }

    public SystemDiagnosticActionTarget Target { get; }

    public string Label { get; }

    public static SystemDiagnosticAction Create(
        SystemDiagnosticActionKind kind,
        SystemDiagnosticActionTarget target,
        string label)
    {
        DiagnosticContractGuard.RequireEnum(kind, nameof(kind));
        DiagnosticContractGuard.RequireEnum(target, nameof(target));
        DiagnosticContractGuard.RequireText(label, nameof(label));

        bool valid = kind switch
        {
            SystemDiagnosticActionKind.NavigateToFolderSettings => target is
                SystemDiagnosticActionTarget.InboxDirectory
                or SystemDiagnosticActionTarget.LibraryDirectory
                or SystemDiagnosticActionTarget.WorkDirectory,
            SystemDiagnosticActionKind.OpenRegisteredLogFolder =>
                target == SystemDiagnosticActionTarget.None,
            SystemDiagnosticActionKind.CopyErrorCode =>
                target == SystemDiagnosticActionTarget.None,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException(
                "The diagnostic action kind and target are incompatible.",
                nameof(target));
        }

        return new(kind, target, label);
    }
}

public sealed record SystemDiagnosticItem
{
    private SystemDiagnosticItem(
        string id,
        SystemDiagnosticCategory category,
        SystemDiagnosticSeverity severity,
        int order,
        string title,
        string conclusion,
        string? impact,
        string? guidance,
        string? errorCode,
        IReadOnlyList<SystemDiagnosticFact> facts,
        IReadOnlyList<SystemDiagnosticAction> actions)
    {
        Id = id;
        Category = category;
        Severity = severity;
        Order = order;
        Title = title;
        Conclusion = conclusion;
        Impact = impact;
        Guidance = guidance;
        ErrorCode = errorCode;
        Facts = facts;
        Actions = actions;
    }

    public string Id { get; }

    public SystemDiagnosticCategory Category { get; }

    public SystemDiagnosticSeverity Severity { get; }

    public int Order { get; }

    public string Title { get; }

    public string Conclusion { get; }

    public string? Impact { get; }

    public string? Guidance { get; }

    public string? ErrorCode { get; }

    public IReadOnlyList<SystemDiagnosticFact> Facts { get; }

    public IReadOnlyList<SystemDiagnosticAction> Actions { get; }

    public static SystemDiagnosticItem Create(
        string id,
        SystemDiagnosticCategory category,
        SystemDiagnosticSeverity severity,
        int order,
        string title,
        string conclusion,
        string? impact,
        string? guidance,
        string? errorCode,
        IReadOnlyList<SystemDiagnosticFact> facts,
        IReadOnlyList<SystemDiagnosticAction> actions)
    {
        DiagnosticContractGuard.RequireIdentifier(id, nameof(id));
        DiagnosticContractGuard.RequireEnum(category, nameof(category));
        DiagnosticContractGuard.RequireEnum(severity, nameof(severity));
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        DiagnosticContractGuard.RequireText(title, nameof(title));
        DiagnosticContractGuard.RequireText(conclusion, nameof(conclusion));
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(actions);

        if (severity == SystemDiagnosticSeverity.Healthy)
        {
            if (!string.IsNullOrWhiteSpace(errorCode))
            {
                throw new ArgumentException(
                    "A healthy diagnostic item cannot have an error code.",
                    nameof(errorCode));
            }
        }
        else
        {
            DiagnosticContractGuard.RequireText(impact, nameof(impact));
            DiagnosticContractGuard.RequireText(guidance, nameof(guidance));
            DiagnosticContractGuard.RequireIdentifier(errorCode, nameof(errorCode));
        }

        SystemDiagnosticFact[] factCopy = [.. facts];
        if (factCopy.Any(fact => fact is null))
        {
            throw new ArgumentException(
                "Diagnostic facts cannot contain null values.",
                nameof(facts));
        }

        if (factCopy.Select(fact => fact.Key)
            .Distinct(StringComparer.Ordinal).Count() != factCopy.Length)
        {
            throw new ArgumentException(
                "Diagnostic fact keys must be unique within an item.",
                nameof(facts));
        }

        SystemDiagnosticAction[] actionCopy = [.. actions];
        if (actionCopy.Any(action => action is null))
        {
            throw new ArgumentException(
                "Diagnostic actions cannot contain null values.",
                nameof(actions));
        }

        if (actionCopy
            .GroupBy(action => (action.Kind, action.Target))
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Diagnostic actions must be unique within an item.",
                nameof(actions));
        }

        if (actionCopy.Any(action =>
                action.Kind == SystemDiagnosticActionKind.CopyErrorCode)
            && string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException(
                "Copy-error actions require an error code.",
                nameof(actions));
        }

        return new(
            id,
            category,
            severity,
            order,
            title,
            conclusion,
            string.IsNullOrWhiteSpace(impact) ? null : impact,
            string.IsNullOrWhiteSpace(guidance) ? null : guidance,
            string.IsNullOrWhiteSpace(errorCode) ? null : errorCode,
            Array.AsReadOnly(factCopy),
            Array.AsReadOnly(actionCopy));
    }
}

public sealed record SystemDiagnosticsSnapshot
{
    private SystemDiagnosticsSnapshot(
        Guid runId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        SystemDiagnosticSeverity overallSeverity,
        IReadOnlyList<SystemDiagnosticItem> items)
    {
        RunId = runId;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        OverallSeverity = overallSeverity;
        Items = items;
    }

    public Guid RunId { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public SystemDiagnosticSeverity OverallSeverity { get; }

    public IReadOnlyList<SystemDiagnosticItem> Items { get; }

    public static SystemDiagnosticsSnapshot Create(
        Guid runId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        SystemDiagnosticSeverity overallSeverity,
        IReadOnlyList<SystemDiagnosticItem> items)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException(
                "The diagnostics run ID cannot be empty.",
                nameof(runId));
        }

        if (completedAtUtc < startedAtUtc)
        {
            throw new ArgumentException(
                "The completion time cannot precede the start time.",
                nameof(completedAtUtc));
        }

        DiagnosticContractGuard.RequireEnum(overallSeverity, nameof(overallSeverity));
        ArgumentNullException.ThrowIfNull(items);
        SystemDiagnosticItem[] itemCopy = [.. items];
        if (itemCopy.Length == 0 || itemCopy.Any(item => item is null))
        {
            throw new ArgumentException(
                "A diagnostics snapshot requires at least one item.",
                nameof(items));
        }

        if (itemCopy.Select(item => item.Id)
            .Distinct(StringComparer.Ordinal).Count() != itemCopy.Length)
        {
            throw new ArgumentException(
                "Diagnostic item IDs must be unique.",
                nameof(items));
        }

        SystemDiagnosticSeverity expected = itemCopy.Max(item => item.Severity);
        if (overallSeverity != expected)
        {
            throw new ArgumentException(
                "The overall severity must equal the most severe item.",
                nameof(overallSeverity));
        }

        return new(
            runId,
            startedAtUtc,
            completedAtUtc,
            overallSeverity,
            Array.AsReadOnly(itemCopy));
    }
}

public sealed record SystemDiagnosticProbeDescriptor
{
    private SystemDiagnosticProbeDescriptor(
        string id,
        SystemDiagnosticCategory category,
        int order,
        SystemDiagnosticSeverity failureSeverity,
        string failureTitle,
        string failureConclusion,
        string failureImpact,
        string failureGuidance,
        string timeoutErrorCode)
    {
        Id = id;
        Category = category;
        Order = order;
        FailureSeverity = failureSeverity;
        FailureTitle = failureTitle;
        FailureConclusion = failureConclusion;
        FailureImpact = failureImpact;
        FailureGuidance = failureGuidance;
        TimeoutErrorCode = timeoutErrorCode;
    }

    public string Id { get; }

    public SystemDiagnosticCategory Category { get; }

    public int Order { get; }

    public SystemDiagnosticSeverity FailureSeverity { get; }

    public string FailureTitle { get; }

    public string FailureConclusion { get; }

    public string FailureImpact { get; }

    public string FailureGuidance { get; }

    public string TimeoutErrorCode { get; }

    public static SystemDiagnosticProbeDescriptor Create(
        string id,
        SystemDiagnosticCategory category,
        int order,
        SystemDiagnosticSeverity failureSeverity,
        string failureTitle,
        string failureConclusion,
        string failureImpact,
        string failureGuidance,
        string timeoutErrorCode)
    {
        DiagnosticContractGuard.RequireIdentifier(id, nameof(id));
        DiagnosticContractGuard.RequireEnum(category, nameof(category));
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        DiagnosticContractGuard.RequireEnum(failureSeverity, nameof(failureSeverity));
        if (failureSeverity == SystemDiagnosticSeverity.Healthy)
        {
            throw new ArgumentException(
                "Probe failures cannot be healthy.",
                nameof(failureSeverity));
        }

        DiagnosticContractGuard.RequireText(failureTitle, nameof(failureTitle));
        DiagnosticContractGuard.RequireText(failureConclusion, nameof(failureConclusion));
        DiagnosticContractGuard.RequireText(failureImpact, nameof(failureImpact));
        DiagnosticContractGuard.RequireText(failureGuidance, nameof(failureGuidance));
        DiagnosticContractGuard.RequireIdentifier(timeoutErrorCode, nameof(timeoutErrorCode));
        return new(
            id,
            category,
            order,
            failureSeverity,
            failureTitle,
            failureConclusion,
            failureImpact,
            failureGuidance,
            timeoutErrorCode);
    }
}

public sealed record SystemDiagnosticsOptions
{
    public SystemDiagnosticsOptions(
        TimeSpan fastProbeTimeout,
        TimeSpan databaseProbeTimeout,
        TimeSpan backgroundProbeTimeout,
        TimeSpan wholeRunTimeout)
    {
        RequirePositive(fastProbeTimeout, nameof(fastProbeTimeout));
        RequirePositive(databaseProbeTimeout, nameof(databaseProbeTimeout));
        RequirePositive(backgroundProbeTimeout, nameof(backgroundProbeTimeout));
        RequirePositive(wholeRunTimeout, nameof(wholeRunTimeout));
        if (wholeRunTimeout < fastProbeTimeout
            || wholeRunTimeout < databaseProbeTimeout
            || wholeRunTimeout < backgroundProbeTimeout)
        {
            throw new ArgumentException(
                "The whole-run timeout cannot be shorter than a probe timeout.",
                nameof(wholeRunTimeout));
        }

        FastProbeTimeout = fastProbeTimeout;
        DatabaseProbeTimeout = databaseProbeTimeout;
        BackgroundProbeTimeout = backgroundProbeTimeout;
        WholeRunTimeout = wholeRunTimeout;
    }

    public static SystemDiagnosticsOptions Production { get; } = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(20));

    public TimeSpan FastProbeTimeout { get; }

    public TimeSpan DatabaseProbeTimeout { get; }

    public TimeSpan BackgroundProbeTimeout { get; }

    public TimeSpan WholeRunTimeout { get; }

    public TimeSpan GetProbeTimeout(SystemDiagnosticCategory category)
    {
        DiagnosticContractGuard.RequireEnum(category, nameof(category));
        return category switch
        {
            SystemDiagnosticCategory.Database => DatabaseProbeTimeout,
            SystemDiagnosticCategory.BackgroundWork => BackgroundProbeTimeout,
            _ => FastProbeTimeout
        };
    }

    private static void RequirePositive(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed record BackgroundWorkSummary
{
    public BackgroundWorkSummary(
        int QueuedCount,
        int RunningCount,
        int InterruptedCount,
        int FailedCount,
        int RecoveryPendingCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(QueuedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(RunningCount);
        ArgumentOutOfRangeException.ThrowIfNegative(InterruptedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(FailedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(RecoveryPendingCount);
        this.QueuedCount = QueuedCount;
        this.RunningCount = RunningCount;
        this.InterruptedCount = InterruptedCount;
        this.FailedCount = FailedCount;
        this.RecoveryPendingCount = RecoveryPendingCount;
    }

    public int QueuedCount { get; }

    public int RunningCount { get; }

    public int InterruptedCount { get; }

    public int FailedCount { get; }

    public int RecoveryPendingCount { get; }
}

public interface ISystemDiagnosticProbe
{
    SystemDiagnosticProbeDescriptor Descriptor { get; }

    Task<IReadOnlyList<SystemDiagnosticItem>> CheckAsync(
        CancellationToken cancellationToken);
}

public interface ISystemDiagnosticsService
{
    Task<SystemDiagnosticsSnapshot> CheckAsync(
        CancellationToken cancellationToken);
}

public interface IDiagnosticSummaryFormatter
{
    string Format(SystemDiagnosticsSnapshot snapshot, TimeZoneInfo timeZone);
}

public interface IBackgroundWorkSummaryStore
{
    Task<BackgroundWorkSummary> LoadAsync(CancellationToken cancellationToken);
}

internal static class DiagnosticContractGuard
{
    public static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The value cannot be blank.",
                parameterName);
        }
    }

    public static void RequireIdentifier(string? value, string parameterName)
    {
        RequireText(value, parameterName);
        if (!char.IsLower(value![0]) || value.Any(character =>
                !char.IsLower(character)
                && !char.IsDigit(character)
                && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException(
                "The value must be a stable lowercase identifier.",
                parameterName);
        }
    }

    public static void RequireEnum<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
