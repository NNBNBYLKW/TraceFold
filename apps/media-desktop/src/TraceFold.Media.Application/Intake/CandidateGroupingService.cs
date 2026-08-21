namespace TraceFold.Media.Application.Intake;

public sealed class CandidateGroupingService
{
    private readonly IReadOnlyList<IInboxGroupingRule> _rules;

    public CandidateGroupingService(IEnumerable<IInboxGroupingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Name, StringComparer.Ordinal)
            .ToArray();

        ObservedExtensions = new HashSet<string>(
            _rules.SelectMany(rule => rule.ObservedExtensions),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> ObservedExtensions { get; }

    public IReadOnlyList<CandidateGroup> Group(
        IReadOnlyList<ObservedFile> files,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(files);

        var unclaimed = new Dictionary<string, ObservedFile>(
            StringComparer.OrdinalIgnoreCase);
        foreach (ObservedFile file in files)
        {
            if (!unclaimed.TryAdd(file.FullPath, file))
            {
                throw new InvalidOperationException(
                    $"The observed path was supplied more than once: {file.FullPath}");
            }
        }

        var result = new List<CandidateGroup>();
        foreach (IInboxGroupingRule rule in _rules)
        {
            CandidateGroup[] groups = rule
                .Group(unclaimed.Values.ToArray(), observedAtUtc)
                .ToArray();
            var claimedByRule = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (CandidateGroup group in groups)
            {
                if (group.Sources.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Grouping rule {rule.Name} returned an empty candidate group.");
                }

                foreach (ObservedFile source in group.Sources)
                {
                    if (!claimedByRule.Add(source.FullPath)
                        || !unclaimed.ContainsKey(source.FullPath))
                    {
                        throw new InvalidOperationException(
                            $"Grouping rule {rule.Name} claimed a path more than once: {source.FullPath}");
                    }
                }
            }

            foreach (CandidateGroup group in groups)
            {
                foreach (ObservedFile source in group.Sources)
                {
                    unclaimed.Remove(source.FullPath);
                }

                result.Add(group);
            }
        }

        return result;
    }
}
