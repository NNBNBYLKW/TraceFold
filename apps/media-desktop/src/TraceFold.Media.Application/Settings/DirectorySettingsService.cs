namespace TraceFold.Media.Application.Settings;

public sealed class DirectorySettingsService(
    IDirectoryProbe directoryProbe,
    IDirectorySettingsStore settingsStore)
{
    private static readonly (string Field, Func<LibraryFolders, string> Select)[] Fields =
    [
        ("inbox", folders => folders.InboxDirectory),
        ("library", folders => folders.LibraryDirectory),
        ("work", folders => folders.WorkDirectory)
    ];

    public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
        settingsStore.LoadAsync(cancellationToken);

    public async Task<DirectorySettingsResult> ValidateAsync(
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var errors = new List<SettingsError>();
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string field, Func<LibraryFolders, string> select) in Fields)
        {
            string path = select(folders);
            if (string.IsNullOrWhiteSpace(path))
            {
                errors.Add(new SettingsError(
                    "directory.required",
                    field,
                    "请选择文件夹。"));
                continue;
            }

            if (!Path.IsPathFullyQualified(path))
            {
                errors.Add(new SettingsError(
                    "directory.not_absolute",
                    field,
                    "文件夹必须使用绝对路径。"));
                continue;
            }

            try
            {
                canonical[field] = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(path));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                errors.Add(new SettingsError(
                    "directory.not_absolute",
                    field,
                    "文件夹必须使用有效的绝对路径。"));
            }
        }

        if (errors.Count > 0)
        {
            return new DirectorySettingsResult(false, null, errors);
        }

        LibraryFolders canonicalFolders = new(
            canonical["inbox"],
            canonical["library"],
            canonical["work"]);

        AddOverlapErrors(canonicalFolders, errors);
        if (errors.Count > 0)
        {
            return new DirectorySettingsResult(false, canonicalFolders, errors);
        }

        foreach ((string field, Func<LibraryFolders, string> select) in Fields)
        {
            string path = select(canonicalFolders);
            if (!await directoryProbe.ExistsAsync(path, cancellationToken))
            {
                errors.Add(new SettingsError(
                    "directory.not_found",
                    field,
                    "文件夹不存在。"));
                continue;
            }

            if (!await directoryProbe.CanWriteAsync(path, cancellationToken))
            {
                errors.Add(new SettingsError(
                    "directory.not_writable",
                    field,
                    "文件夹不可写。"));
            }
        }

        return new DirectorySettingsResult(
            errors.Count == 0,
            canonicalFolders,
            errors);
    }

    public async Task<DirectorySettingsResult> SaveAsync(
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        DirectorySettingsResult validation =
            await ValidateAsync(folders, cancellationToken);
        if (!validation.Succeeded || validation.Folders is null)
        {
            return validation;
        }

        await settingsStore.SaveAsync(validation.Folders, cancellationToken);
        return validation;
    }

    private static void AddOverlapErrors(
        LibraryFolders folders,
        ICollection<SettingsError> errors)
    {
        (string Field, string Path)[] values =
        [
            ("inbox", folders.InboxDirectory),
            ("library", folders.LibraryDirectory),
            ("work", folders.WorkDirectory)
        ];

        for (int left = 0; left < values.Length; left++)
        {
            for (int right = left + 1; right < values.Length; right++)
            {
                if (!IsSameOrNested(values[left].Path, values[right].Path)
                    && !IsSameOrNested(values[right].Path, values[left].Path))
                {
                    continue;
                }

                errors.Add(new SettingsError(
                    "directory.overlap",
                    values[right].Field,
                    "待入库、正式库和工作目录不能相同或互相包含。"));
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
}
