namespace TraceFold.Media.Application.Settings;

public sealed record SettingsError(
    string Code,
    string Field,
    string Message);

public sealed record DirectorySettingsResult(
    bool Succeeded,
    LibraryFolders? Folders,
    IReadOnlyList<SettingsError> Errors);

public interface IDirectoryProbe
{
    ValueTask<bool> ExistsAsync(
        string path,
        CancellationToken cancellationToken);

    ValueTask<bool> CanWriteAsync(
        string path,
        CancellationToken cancellationToken);
}

public interface IDirectorySettingsStore
{
    Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        LibraryFolders folders,
        CancellationToken cancellationToken);
}
