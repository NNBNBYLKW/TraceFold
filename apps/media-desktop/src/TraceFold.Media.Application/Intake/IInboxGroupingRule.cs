namespace TraceFold.Media.Application.Intake;

public interface IInboxGroupingRule
{
    string Name { get; }

    int Priority { get; }

    IReadOnlySet<string> ObservedExtensions { get; }

    IReadOnlyList<CandidateGroup> Group(
        IReadOnlyList<ObservedFile> files,
        DateTimeOffset observedAtUtc);
}
