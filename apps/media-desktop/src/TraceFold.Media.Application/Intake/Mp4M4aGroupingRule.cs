namespace TraceFold.Media.Application.Intake;

public sealed class Mp4M4aGroupingRule : IInboxGroupingRule
{
    private static readonly TimeSpan CompanionWindow = TimeSpan.FromSeconds(10);

    public string Name => "mp4_m4a";

    public int Priority => 100;

    public IReadOnlySet<string> ObservedExtensions { get; } =
        new HashSet<string>([".mp4", ".m4a"], StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CandidateGroup> Group(
        IReadOnlyList<ObservedFile> files,
        DateTimeOffset observedAtUtc)
    {
        var result = new List<CandidateGroup>();
        IEnumerable<IGrouping<string, ObservedFile>> groups = files
            .Where(file => ObservedExtensions.Contains(file.Extension))
            .GroupBy(
                file => Path.GetFileNameWithoutExtension(file.Filename),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, ObservedFile> group in groups)
        {
            ObservedFile[] videoSources = group
                .Where(file => file.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ObservedFile[] audioSources = group
                .Where(file => file.Extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (videoSources.Length == 0)
            {
                continue;
            }

            if (audioSources.Length > 0)
            {
                result.Add(new CandidateGroup(
                    Name,
                    group.Key.ToUpperInvariant(),
                    MediaKind.Video,
                    CandidateDisposition.RequiresProcessing,
                    [.. videoSources, .. audioSources],
                    "mp4_m4a_merge"));
                continue;
            }

            foreach (ObservedFile videoSource in videoSources)
            {
                CandidateDisposition disposition =
                    observedAtUtc < videoSource.FirstObservedAtUtc + CompanionWindow
                        ? CandidateDisposition.WaitingForCompanion
                        : CandidateDisposition.Direct;
                result.Add(new CandidateGroup(
                    Name,
                    videoSource.FullPath.ToUpperInvariant(),
                    MediaKind.Video,
                    disposition,
                    [videoSource],
                    null));
            }
        }

        return result;
    }
}
