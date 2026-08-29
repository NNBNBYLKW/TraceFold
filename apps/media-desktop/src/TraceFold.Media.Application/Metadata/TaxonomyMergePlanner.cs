using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public sealed class TaxonomyMaintenancePlanningException : Exception
{
    public TaxonomyMaintenancePlanningException(string errorCode)
        : base(errorCode)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public static class TaxonomyMergePlanner
{
    public static TaxonomyMergePlan Build(
        TaxonomyMergePreparation preparation,
        TaxonomyName? finalName)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ValidatePreparation(preparation);

        TaxonomyName resolvedName = finalName
            ?? TaxonomyName.Create(preparation.Target.DisplayName);
        var sourceIds = preparation.Selection.SourceIds.ToHashSet();
        bool nameCollision = preparation.SameKindNames.Any(owner =>
            StringComparer.Ordinal.Equals(
                owner.NormalizedName,
                resolvedName.Normalized)
            && owner.TermId != preparation.Target.Id
            && !sourceIds.Contains(owner.TermId));
        if (nameCollision)
        {
            throw new TaxonomyMaintenancePlanningException(
                TaxonomyMaintenanceErrorCodes.NameConflict);
        }

        var distinctOwners = new Dictionary<MetadataOwner, TaxonomyOwnerSnapshot>();
        foreach (TaxonomyOwnerSnapshot owner in preparation.Owners)
        {
            ValidateOwner(owner);
            if (distinctOwners.TryGetValue(
                    owner.Owner,
                    out TaxonomyOwnerSnapshot? existing))
            {
                if (!OwnerSnapshotsEqual(existing, owner))
                {
                    throw new TaxonomyMaintenancePlanningException(
                        TaxonomyMaintenanceErrorCodes.SelectionInvalid);
                }

                continue;
            }

            distinctOwners.Add(owner.Owner, owner);
        }

        var plannedOwners = new List<TaxonomyMergePlanOwner>(
            distinctOwners.Count);
        foreach (TaxonomyOwnerSnapshot owner in distinctOwners.Values)
        {
            TaxonomyAssignmentSnapshot after = MergeAssignment(
                owner.Assignment,
                preparation.Selection.Kind,
                sourceIds,
                preparation.Target.Id);
            if (AssignmentsEqual(owner.Assignment, after))
            {
                continue;
            }

            plannedOwners.Add(new TaxonomyMergePlanOwner(
                owner.Owner,
                owner.VisibleName,
                owner.Version,
                owner.Assignment,
                after));
        }

        return new TaxonomyMergePlan(
            preparation.OperationId,
            preparation.Selection.Kind,
            preparation.Target,
            resolvedName,
            preparation.Sources.ToArray(),
            plannedOwners.ToArray());
    }

    private static void ValidatePreparation(TaxonomyMergePreparation preparation)
    {
        if (preparation.OperationId == Guid.Empty
            || preparation.Selection is null
            || preparation.Target is null
            || preparation.Sources is null
            || preparation.SameKindNames is null
            || preparation.Owners is null)
        {
            throw new TaxonomyMaintenancePlanningException(
                TaxonomyMaintenanceErrorCodes.SelectionInvalid);
        }

        TaxonomyMergeSelection selection = preparation.Selection;
        if (preparation.Target.Id != selection.TargetId
            || preparation.Target.Kind != selection.Kind)
        {
            throw new TaxonomyMaintenancePlanningException(
                TaxonomyMaintenanceErrorCodes.TargetInvalid);
        }

        Guid[] sourceIds = preparation.Sources.Select(source => source.Id).ToArray();
        if (preparation.Sources.Count != selection.SourceIds.Count
            || preparation.Sources.Any(source => source.Kind != selection.Kind)
            || sourceIds.Distinct().Count() != sourceIds.Length
            || !sourceIds.ToHashSet().SetEquals(selection.SourceIds))
        {
            throw new TaxonomyMaintenancePlanningException(
                TaxonomyMaintenanceErrorCodes.KindMismatch);
        }

        TaxonomyName targetName = TaxonomyName.Create(
            preparation.Target.DisplayName);
        if (!StringComparer.Ordinal.Equals(
                targetName.Normalized,
                preparation.Target.NormalizedName))
        {
            throw new TaxonomyMaintenancePlanningException(
                TaxonomyMaintenanceErrorCodes.SelectionInvalid);
        }
    }

    private static void ValidateOwner(TaxonomyOwnerSnapshot owner)
    {
        if (owner is null
            || owner.Owner is null
            || !Enum.IsDefined(owner.Owner.Kind)
            || owner.Owner.Id == Guid.Empty
            || owner.VisibleName is null
            || owner.Version < 0
            || owner.Assignment is null
            || owner.Assignment.AuthorIds is null
            || owner.Assignment.TagIds is null)
        {
            throw new TaxonomyMaintenancePlanningException(
                TaxonomyMaintenanceErrorCodes.SelectionInvalid);
        }
    }

    private static TaxonomyAssignmentSnapshot MergeAssignment(
        TaxonomyAssignmentSnapshot current,
        TaxonomyKind kind,
        IReadOnlySet<Guid> sourceIds,
        Guid targetId) =>
        kind switch
        {
            TaxonomyKind.Author => new TaxonomyAssignmentSnapshot(
                MergeOrdered(current.AuthorIds, sourceIds, targetId),
                current.SeriesId,
                current.TagIds.ToArray()),
            TaxonomyKind.Series => new TaxonomyAssignmentSnapshot(
                current.AuthorIds.ToArray(),
                current.SeriesId is Guid seriesId && sourceIds.Contains(seriesId)
                    ? targetId
                    : current.SeriesId,
                current.TagIds.ToArray()),
            TaxonomyKind.Tag => new TaxonomyAssignmentSnapshot(
                current.AuthorIds.ToArray(),
                current.SeriesId,
                MergeOrdered(current.TagIds, sourceIds, targetId)),
            _ => throw new TaxonomyMaintenancePlanningException(
                TaxonomyMaintenanceErrorCodes.KindMismatch),
        };

    private static IReadOnlyList<Guid> MergeOrdered(
        IReadOnlyList<Guid> current,
        IReadOnlySet<Guid> sourceIds,
        Guid targetId)
    {
        if (current.Contains(targetId))
        {
            return current
                .Where(id => !sourceIds.Contains(id))
                .Distinct()
                .ToArray();
        }

        int firstSource = current
            .Select((id, index) => (id, index))
            .Where(item => sourceIds.Contains(item.id))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Min();
        if (firstSource < 0)
        {
            return current.ToArray();
        }

        var result = new List<Guid>(current.Count);
        for (int index = 0; index < current.Count; index++)
        {
            Guid id = current[index];
            if (index == firstSource)
            {
                result.Add(targetId);
            }

            if (!sourceIds.Contains(id))
            {
                result.Add(id);
            }
        }

        return result.Distinct().ToArray();
    }

    private static bool OwnerSnapshotsEqual(
        TaxonomyOwnerSnapshot left,
        TaxonomyOwnerSnapshot right) =>
        left.Owner == right.Owner
        && StringComparer.Ordinal.Equals(left.VisibleName, right.VisibleName)
        && left.Version == right.Version
        && AssignmentsEqual(left.Assignment, right.Assignment);

    private static bool AssignmentsEqual(
        TaxonomyAssignmentSnapshot left,
        TaxonomyAssignmentSnapshot right) =>
        left.SeriesId == right.SeriesId
        && left.AuthorIds.SequenceEqual(right.AuthorIds)
        && left.TagIds.SequenceEqual(right.TagIds);
}
