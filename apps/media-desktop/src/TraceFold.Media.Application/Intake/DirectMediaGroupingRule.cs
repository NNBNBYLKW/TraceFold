namespace TraceFold.Media.Application.Intake;

public sealed class DirectMediaGroupingRule : IInboxGroupingRule
{
    public static readonly IReadOnlySet<string> ImageExtensions =
        new HashSet<string>(
            [".jpg", ".jpeg", ".png", ".webp", ".gif"],
            StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> VideoExtensions =
        new HashSet<string>(
            [".mp4", ".mov", ".mkv", ".webm"],
            StringComparer.OrdinalIgnoreCase);

    public string Name => "direct_media";

    public int Priority => 0;

    public IReadOnlySet<string> ObservedExtensions { get; } =
        new HashSet<string>(
            ImageExtensions.Concat(VideoExtensions),
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CandidateGroup> Group(
        IReadOnlyList<ObservedFile> files,
        DateTimeOffset observedAtUtc) => files
        .Where(file => ObservedExtensions.Contains(file.Extension))
        .OrderBy(file => file.Filename, StringComparer.OrdinalIgnoreCase)
        .Select(file => new CandidateGroup(
            Name,
            file.FullPath.ToUpperInvariant(),
            ImageExtensions.Contains(file.Extension) ? MediaKind.Image : MediaKind.Video,
            CandidateDisposition.Direct,
            [file],
            null))
        .ToArray();
}
