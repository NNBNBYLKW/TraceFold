using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Infrastructure.Filesystem;

public sealed class LocalInboxFileCatalog : IInboxFileCatalog
{
    private static readonly string[] TemporarySuffixes =
        [".part", ".crdownload", ".tmp"];

    public async Task<IReadOnlyList<FileSnapshot>> EnumerateTopLevelAsync(
        string inboxDirectory,
        IReadOnlySet<string> observedExtensions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observedExtensions);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        string root;
        try
        {
            if (string.IsNullOrWhiteSpace(inboxDirectory)
                || !Path.IsPathFullyQualified(inboxDirectory))
            {
                throw new ArgumentException(
                    "The inbox directory must be fully qualified.",
                    nameof(inboxDirectory));
            }

            root = Path.GetFullPath(inboxDirectory);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            throw new IntakeFileAccessException(
                "intake.invalid_root",
                inboxDirectory ?? string.Empty,
                error);
        }

        try
        {
            var result = new List<FileSnapshot>();
            foreach (string enumeratedPath in Directory.EnumerateFiles(
                         root,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath = ControlledPathGuard.RequireDirectChild(root, enumeratedPath);
                FileAttributes attributes = File.GetAttributes(fullPath);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                string filename = Path.GetFileName(fullPath);
                if (TemporarySuffixes.Any(suffix =>
                        filename.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string extension = Path.GetExtension(filename);
                if (!observedExtensions.Contains(extension))
                {
                    continue;
                }

                var info = new FileInfo(fullPath);
                result.Add(new FileSnapshot(
                    fullPath,
                    filename,
                    extension,
                    new FileFingerprint(
                        info.Length,
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero))));
            }

            return result
                .OrderBy(item => item.Filename, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DirectoryNotFoundException error)
        {
            throw new IntakeFileAccessException(
                "intake.directory_not_found",
                root,
                error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new IntakeFileAccessException(
                "intake.access_denied",
                root,
                error);
        }
        catch (IOException error)
        {
            throw new IntakeFileAccessException(
                "intake.io_error",
                root,
                error);
        }
    }
}
