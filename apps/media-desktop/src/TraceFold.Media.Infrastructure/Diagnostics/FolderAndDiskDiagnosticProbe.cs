using System.Globalization;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Infrastructure.Diagnostics;

public sealed record VolumeSpace
{
    public VolumeSpace(
        string volumeRoot,
        long totalBytes,
        long availableBytes)
    {
        if (string.IsNullOrWhiteSpace(volumeRoot)
            || !Path.IsPathFullyQualified(volumeRoot))
        {
            throw new ArgumentException(
                "The volume root must be fully qualified.",
                nameof(volumeRoot));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(availableBytes);
        if (availableBytes > totalBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(availableBytes));
        }

        VolumeRoot = Path.GetFullPath(volumeRoot);
        TotalBytes = totalBytes;
        AvailableBytes = availableBytes;
    }

    public string VolumeRoot { get; }

    public long TotalBytes { get; }

    public long AvailableBytes { get; }
}

public interface IVolumeSpaceReader
{
    ValueTask<VolumeSpace> ReadAsync(
        string volumeRoot,
        CancellationToken cancellationToken);
}

public sealed class WindowsVolumeSpaceReader : IVolumeSpaceReader
{
    public ValueTask<VolumeSpace> ReadAsync(
        string volumeRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string canonicalRoot = Path.GetFullPath(volumeRoot);
        var drive = new DriveInfo(canonicalRoot);
        return ValueTask.FromResult(new VolumeSpace(
            drive.RootDirectory.FullName,
            drive.TotalSize,
            drive.AvailableFreeSpace));
    }
}

public sealed class FolderAndDiskDiagnosticProbe(
    IDirectorySettingsStore settingsStore,
    IDirectoryProbe directoryProbe,
    IVolumeSpaceReader volumeSpaceReader) : ISystemDiagnosticProbe
{
    private const long LowSpaceBytes = 10L * 1024 * 1024 * 1024;

    private static readonly FolderDefinition[] Definitions =
    [
        new(
            "inbox",
            "待入库目录",
            SystemDiagnosticActionTarget.InboxDirectory,
            folders => folders.InboxDirectory),
        new(
            "library",
            "正式文件库目录",
            SystemDiagnosticActionTarget.LibraryDirectory,
            folders => folders.LibraryDirectory),
        new(
            "work",
            "工作目录",
            SystemDiagnosticActionTarget.WorkDirectory,
            folders => folders.WorkDirectory)
    ];

    public SystemDiagnosticProbeDescriptor Descriptor { get; } =
        SystemDiagnosticProbeDescriptor.Create(
            "folders",
            SystemDiagnosticCategory.FoldersAndStorage,
            order: 0,
            failureSeverity: SystemDiagnosticSeverity.Unavailable,
            failureTitle: "目录与磁盘检查失败",
            failureConclusion: "无法完成目录与磁盘检查",
            failureImpact: "入库、正式存储或前置处理可能不可用。",
            failureGuidance: "请重新检查；问题持续时查看本地日志。",
            timeoutErrorCode: "diagnostics.probe.timeout");

    public async Task<IReadOnlyList<SystemDiagnosticItem>> CheckAsync(
        CancellationToken cancellationToken)
    {
        LibraryFolders? folders = await settingsStore.LoadAsync(cancellationToken);
        if (folders is null)
        {
            return Definitions
                .Select((definition, order) => CreateUnavailable(
                    definition,
                    order,
                    originalPath: null,
                    "diagnostics.folder.not_configured",
                    "尚未配置",
                    "对应的文件管理功能无法使用。",
                    "请转到文件夹设置并保存目录。",
                    facts: []))
                .ToArray();
        }

        FolderObservation[] observations = Definitions
            .Select((definition, order) => ObservePath(
                definition,
                order,
                definition.SelectPath(folders)))
            .ToArray();
        MarkUnsafeRelationships(observations);
        var volumeCache = new Dictionary<string, VolumeObservation>(
            StringComparer.OrdinalIgnoreCase);
        var results = new List<SystemDiagnosticItem>(Definitions.Length);
        foreach (FolderObservation observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await CreateItemAsync(
                observation,
                volumeCache,
                cancellationToken));
        }

        return results;
    }

    private async Task<SystemDiagnosticItem> CreateItemAsync(
        FolderObservation observation,
        IDictionary<string, VolumeObservation> volumeCache,
        CancellationToken cancellationToken)
    {
        List<SystemDiagnosticFact> facts = BaseFacts(observation);
        if (observation.PathErrorCode is not null)
        {
            return CreateUnavailable(
                observation.Definition,
                observation.Order,
                observation.OriginalPath,
                observation.PathErrorCode,
                observation.PathErrorCode == "diagnostics.folder.not_configured"
                    ? "尚未配置"
                    : "路径无效",
                "对应的文件管理功能无法使用。",
                "请转到文件夹设置并保存有效的绝对路径。",
                facts);
        }

        string canonicalPath = observation.CanonicalPath!;
        bool exists = await directoryProbe.ExistsAsync(
            canonicalPath,
            cancellationToken);
        facts.Add(Fact(
            $"folders.{observation.Definition.Key}.exists",
            "目录存在",
            exists ? "是" : "否",
            SystemDiagnosticFactSensitivity.Shareable,
            order: 2));
        if (!exists)
        {
            return CreateUnavailable(
                observation.Definition,
                observation.Order,
                observation.OriginalPath,
                "diagnostics.folder.missing",
                "目录不存在",
                "对应的文件管理功能无法使用。",
                "请创建该目录或在文件夹设置中选择其他目录。",
                facts);
        }

        bool writable = await directoryProbe.CanWriteAsync(
            canonicalPath,
            cancellationToken);
        facts.Add(Fact(
            $"folders.{observation.Definition.Key}.writable",
            "可写",
            writable ? "是" : "否",
            SystemDiagnosticFactSensitivity.Shareable,
            order: 3));
        if (!writable)
        {
            return CreateUnavailable(
                observation.Definition,
                observation.Order,
                observation.OriginalPath,
                "diagnostics.folder.not_writable",
                "目录不可写",
                "无法安全写入或移动文件。",
                "请检查目录权限，或在文件夹设置中选择可写目录。",
                facts);
        }

        VolumeObservation volume = await ReadVolumeAsync(
            canonicalPath,
            volumeCache,
            cancellationToken);
        if (volume.Space is not null)
        {
            AddVolumeFacts(observation, volume.Space, facts);
        }

        if (observation.UnsafeRelationship)
        {
            return CreateUnavailable(
                observation.Definition,
                observation.Order,
                observation.OriginalPath,
                "diagnostics.folder.unsafe_relationship",
                "目录关系不安全",
                "目录相同或互相包含可能导致重复扫描或错误移动。",
                "请在文件夹设置中选择彼此独立的三个目录。",
                facts);
        }

        if (volume.Error)
        {
            return CreateAttention(
                observation,
                "diagnostics.storage.query_failed",
                "无法读取磁盘容量",
                "当前无法判断剩余空间是否充足。",
                "请检查磁盘连接状态后重新检查。",
                facts);
        }

        if (IsLow(volume.Space!))
        {
            return CreateAttention(
                observation,
                "diagnostics.storage.space_low",
                "磁盘剩余空间较低",
                "后续下载、前置处理或正式入库可能失败。",
                "请人工释放空间或更换目录所在磁盘。",
                facts);
        }

        return SystemDiagnosticItem.Create(
            $"folders.{observation.Definition.Key}",
            SystemDiagnosticCategory.FoldersAndStorage,
            SystemDiagnosticSeverity.Healthy,
            observation.Order,
            observation.Definition.Label,
            "目录存在、可写且磁盘空间充足",
            impact: null,
            guidance: null,
            errorCode: null,
            facts,
            actions: [NavigateAction(observation.Definition)]);
    }

    private async Task<VolumeObservation> ReadVolumeAsync(
        string canonicalPath,
        IDictionary<string, VolumeObservation> cache,
        CancellationToken cancellationToken)
    {
        string? root = Path.GetPathRoot(canonicalPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return VolumeObservation.Failed;
        }

        string canonicalRoot = Path.GetFullPath(root);
        if (cache.TryGetValue(canonicalRoot, out VolumeObservation? cached))
        {
            return cached;
        }

        try
        {
            VolumeSpace space = await volumeSpaceReader.ReadAsync(
                canonicalRoot,
                cancellationToken);
            var result = new VolumeObservation(space, Error: false);
            cache[canonicalRoot] = result;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
        {
            cache[canonicalRoot] = VolumeObservation.Failed;
            return VolumeObservation.Failed;
        }
    }

    private static FolderObservation ObservePath(
        FolderDefinition definition,
        int order,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new(
                definition,
                order,
                path,
                CanonicalPath: null,
                "diagnostics.folder.not_configured");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return new(
                definition,
                order,
                path,
                CanonicalPath: null,
                "diagnostics.folder.invalid_path");
        }

        try
        {
            return new(
                definition,
                order,
                path,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                PathErrorCode: null);
        }
        catch (Exception error) when (error is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return new(
                definition,
                order,
                path,
                CanonicalPath: null,
                "diagnostics.folder.invalid_path");
        }
    }

    private static void MarkUnsafeRelationships(FolderObservation[] observations)
    {
        for (int left = 0; left < observations.Length; left++)
        {
            string? leftPath = observations[left].CanonicalPath;
            if (leftPath is null)
            {
                continue;
            }

            for (int right = left + 1; right < observations.Length; right++)
            {
                string? rightPath = observations[right].CanonicalPath;
                if (rightPath is null)
                {
                    continue;
                }

                if (!IsSameOrNested(leftPath, rightPath)
                    && !IsSameOrNested(rightPath, leftPath))
                {
                    continue;
                }

                observations[left].UnsafeRelationship = true;
                observations[right].UnsafeRelationship = true;
            }
        }
    }

    private static bool IsSameOrNested(string parent, string candidate)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(parent, candidate))
        {
            return true;
        }

        string relative = Path.GetRelativePath(parent, candidate);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            && !relative.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    private static List<SystemDiagnosticFact> BaseFacts(
        FolderObservation observation) =>
    [
        Fact(
            $"folders.{observation.Definition.Key}.logical_name",
            "逻辑目录",
            observation.Definition.Label,
            SystemDiagnosticFactSensitivity.Shareable,
            order: 0),
        Fact(
            $"folders.{observation.Definition.Key}.path",
            "本机路径",
            string.IsNullOrWhiteSpace(observation.OriginalPath)
                ? "（未配置）"
                : observation.OriginalPath,
            SystemDiagnosticFactSensitivity.LocalOnly,
            order: 1)
    ];

    private static void AddVolumeFacts(
        FolderObservation observation,
        VolumeSpace space,
        ICollection<SystemDiagnosticFact> facts)
    {
        facts.Add(Fact(
            $"folders.{observation.Definition.Key}.total_bytes",
            "磁盘总容量",
            FormatGiB(space.TotalBytes),
            SystemDiagnosticFactSensitivity.Shareable,
            order: 4));
        facts.Add(Fact(
            $"folders.{observation.Definition.Key}.available_bytes",
            "磁盘剩余容量",
            FormatGiB(space.AvailableBytes),
            SystemDiagnosticFactSensitivity.Shareable,
            order: 5));
        facts.Add(Fact(
            $"folders.{observation.Definition.Key}.volume_root",
            "磁盘根目录",
            space.VolumeRoot,
            SystemDiagnosticFactSensitivity.LocalOnly,
            order: 6));
    }

    private static bool IsLow(VolumeSpace space) =>
        space.AvailableBytes < LowSpaceBytes
        || (decimal)space.AvailableBytes / space.TotalBytes < 0.10m;

    private static string FormatGiB(long bytes) =>
        $"{bytes / (decimal)(1024L * 1024 * 1024):0.0} GiB";

    private static SystemDiagnosticItem CreateUnavailable(
        FolderDefinition definition,
        int order,
        string? originalPath,
        string errorCode,
        string conclusion,
        string impact,
        string guidance,
        IReadOnlyList<SystemDiagnosticFact> facts)
    {
        IReadOnlyList<SystemDiagnosticFact> effectiveFacts = facts.Count == 0
            ? BaseFacts(new FolderObservation(
                definition,
                order,
                originalPath,
                CanonicalPath: null,
                errorCode))
            : facts;
        return SystemDiagnosticItem.Create(
            $"folders.{definition.Key}",
            SystemDiagnosticCategory.FoldersAndStorage,
            SystemDiagnosticSeverity.Unavailable,
            order,
            definition.Label,
            conclusion,
            impact,
            guidance,
            errorCode,
            effectiveFacts,
            actions: [NavigateAction(definition), CopyErrorAction()]);
    }

    private static SystemDiagnosticItem CreateAttention(
        FolderObservation observation,
        string errorCode,
        string conclusion,
        string impact,
        string guidance,
        IReadOnlyList<SystemDiagnosticFact> facts) =>
        SystemDiagnosticItem.Create(
            $"folders.{observation.Definition.Key}",
            SystemDiagnosticCategory.FoldersAndStorage,
            SystemDiagnosticSeverity.Attention,
            observation.Order,
            observation.Definition.Label,
            conclusion,
            impact,
            guidance,
            errorCode,
            facts,
            actions:
            [
                NavigateAction(observation.Definition),
                CopyErrorAction()
            ]);

    private static SystemDiagnosticFact Fact(
        string key,
        string label,
        string value,
        SystemDiagnosticFactSensitivity sensitivity,
        int order) =>
        SystemDiagnosticFact.Create(key, label, value, sensitivity, order);

    private static SystemDiagnosticAction NavigateAction(
        FolderDefinition definition) =>
        SystemDiagnosticAction.Create(
            SystemDiagnosticActionKind.NavigateToFolderSettings,
            definition.Target,
            "转到文件夹设置");

    private static SystemDiagnosticAction CopyErrorAction() =>
        SystemDiagnosticAction.Create(
            SystemDiagnosticActionKind.CopyErrorCode,
            SystemDiagnosticActionTarget.None,
            "复制错误代码");

    private sealed record FolderDefinition(
        string Key,
        string Label,
        SystemDiagnosticActionTarget Target,
        Func<LibraryFolders, string> SelectPath);

    private sealed record FolderObservation(
        FolderDefinition Definition,
        int Order,
        string? OriginalPath,
        string? CanonicalPath,
        string? PathErrorCode)
    {
        public bool UnsafeRelationship { get; set; }
    }

    private sealed record VolumeObservation(VolumeSpace? Space, bool Error)
    {
        public static VolumeObservation Failed { get; } = new(null, Error: true);
    }
}
