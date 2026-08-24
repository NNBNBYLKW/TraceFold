namespace TraceFold.Media.Domain.Ingest;

public sealed class ConflictCase
{
    private readonly IReadOnlySet<Guid> matchIds;

    private ConflictCase(
        Guid id,
        Guid inboxItemId,
        string inputSha256,
        string targetFilename,
        IReadOnlySet<Guid> matchIds)
    {
        Id = id;
        InboxItemId = inboxItemId;
        InputSha256 = inputSha256;
        TargetFilename = targetFilename;
        this.matchIds = matchIds;
        Status = ConflictCaseStatus.Open;
    }

    public Guid Id { get; }

    public Guid InboxItemId { get; }

    public string InputSha256 { get; }

    public string TargetFilename { get; }

    public ConflictCaseStatus Status { get; private set; }

    public ConflictResolution? Resolution { get; private set; }

    public Guid? SelectedMatchId { get; private set; }

    public string? InvalidationReason { get; private set; }

    public static ConflictCase Open(
        Guid id,
        Guid inboxItemId,
        string inputSha256,
        string targetFilename,
        IReadOnlyCollection<Guid> matchIds)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A conflict case id is required.", nameof(id));
        }

        if (inboxItemId == Guid.Empty)
        {
            throw new ArgumentException("An inbox item id is required.", nameof(inboxItemId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(inputSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilename);
        ArgumentNullException.ThrowIfNull(matchIds);

        HashSet<Guid> immutableMatchIds = matchIds.ToHashSet();
        if (immutableMatchIds.Count == 0 || immutableMatchIds.Contains(Guid.Empty))
        {
            throw new ArgumentException(
                "At least one persisted match id is required.",
                nameof(matchIds));
        }

        return new ConflictCase(
            id,
            inboxItemId,
            inputSha256,
            targetFilename,
            immutableMatchIds);
    }

    public void Choose(ConflictResolution resolution, Guid? selectedMatchId)
    {
        if (Status == ConflictCaseStatus.Chosen)
        {
            if (Resolution == resolution && SelectedMatchId == selectedMatchId)
            {
                return;
            }

            throw new InvalidOperationException(
                "A persisted conflict choice cannot be changed without revalidation.");
        }

        RequireStatus(ConflictCaseStatus.Open);

        switch (resolution)
        {
            case ConflictResolution.Skip:
                if (selectedMatchId is not null)
                {
                    throw new InvalidOperationException("Skip cannot select a replacement target.");
                }

                Skip();
                return;

            case ConflictResolution.KeepBoth:
                if (selectedMatchId is not null)
                {
                    throw new InvalidOperationException(
                        "Keep Both cannot select a replacement target.");
                }

                break;

            case ConflictResolution.Replace:
                if (selectedMatchId is not Guid matchId || !matchIds.Contains(matchId))
                {
                    throw new InvalidOperationException(
                        "Replace requires one match from the persisted match set.");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        Resolution = resolution;
        SelectedMatchId = selectedMatchId;
        Status = ConflictCaseStatus.Chosen;
    }

    public void Resolve()
    {
        RequireStatus(ConflictCaseStatus.Chosen);
        Status = ConflictCaseStatus.Resolved;
    }

    public void Invalidate(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status is not ConflictCaseStatus.Open and not ConflictCaseStatus.Chosen)
        {
            throw new InvalidOperationException(
                $"Cannot invalidate a conflict case in {Status} state.");
        }

        InvalidationReason = reason;
        Status = ConflictCaseStatus.Invalidated;
    }

    public void Skip()
    {
        RequireStatus(ConflictCaseStatus.Open);
        Resolution = ConflictResolution.Skip;
        SelectedMatchId = null;
        Status = ConflictCaseStatus.Skipped;
    }

    private void RequireStatus(ConflictCaseStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"Conflict case must be {expected}, but is {Status}.");
        }
    }
}
