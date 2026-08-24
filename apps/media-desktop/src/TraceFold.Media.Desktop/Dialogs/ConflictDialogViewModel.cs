using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class ConflictDialogViewModel : ObservableObject
{
    public ConflictDialogViewModel(ConflictCaseDto conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        Case = conflict;
        Incoming = new ConflictMediaViewModel(conflict.Incoming);
        Matches = conflict.Matches
            .Select(match => new ConflictMediaViewModel(match))
            .ToArray();
        Guid? selectedId = conflict.PersistedSelectedMatchId
            ?? conflict.DefaultReplaceMatchId;
        SelectedMatch = selectedId is null
            ? null
            : Matches.FirstOrDefault(match => match.Id == selectedId.Value);
    }

    public ConflictCaseDto Case { get; }

    public ConflictMediaViewModel Incoming { get; }

    public IReadOnlyList<ConflictMediaViewModel> Matches { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReplace))]
    public partial ConflictMediaViewModel? SelectedMatch { get; set; }

    public bool CanReplace => Case.CanReplace && SelectedMatch is not null;
}

public sealed class ConflictMediaViewModel
{
    public ConflictMediaViewModel(ConflictMediaDto media)
    {
        ArgumentNullException.ThrowIfNull(media);
        Media = media;
    }

    public ConflictMediaDto Media { get; }

    public Guid Id => Media.Id;

    public string DisplayName => Media.DisplayName;

    public string ActualFilename => Media.ActualFilename;

    public PreviewDescriptor? Preview => Media.Preview;

    public string RegistrationText => Media.IsIncoming
        ? "待入库文件"
        : Media.IsRegistered
            ? "已登记到媒体库"
            : "文件库中的未登记文件";

    public string ReasonText => Media.IsIncoming
        ? "新文件"
        : FormatReasons(Media.Reason);

    public string SizeText => FormatSize(Media.Size);

    public string ModifiedText => Media.LastWriteTimeUtc
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss");

    public string TechnicalText
    {
        get
        {
            var values = new List<string> { Media.Technical.Format.ToUpperInvariant() };
            if (Media.Technical.PixelWidth is int width
                && Media.Technical.PixelHeight is int height)
            {
                values.Add($"{width} × {height}");
            }

            if (Media.Technical.Duration is TimeSpan duration)
            {
                values.Add(duration.ToString(duration.TotalHours >= 1
                    ? @"h\:mm\:ss"
                    : @"m\:ss"));
            }

            return string.Join(" · ", values);
        }
    }

    public string AuthorsText => JoinOrEmpty(Media.Metadata.Authors);

    public string SeriesText => string.IsNullOrWhiteSpace(Media.Metadata.Series)
        ? "未设置"
        : Media.Metadata.Series;

    public string TagsText => JoinOrEmpty(Media.Metadata.Tags);

    private static string FormatReasons(ConflictReason reason)
    {
        var values = new List<string>();
        if (reason.HasFlag(ConflictReason.Name))
        {
            values.Add("文件同名");
        }

        if (reason.HasFlag(ConflictReason.Content))
        {
            values.Add("内容相同");
        }

        return values.Count == 0 ? "需要确认" : string.Join("、", values);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }

    private static string JoinOrEmpty(IReadOnlyList<string> values) =>
        values.Count == 0 ? "未设置" : string.Join("、", values);
}
