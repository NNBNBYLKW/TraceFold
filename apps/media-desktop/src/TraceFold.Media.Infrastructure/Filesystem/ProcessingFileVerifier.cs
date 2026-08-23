using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;

namespace TraceFold.Media.Infrastructure.Filesystem;

public sealed class ProcessingFileVerifier(IFileHasher hasher)
    : IProcessingFileVerifier
{
    public async Task<bool> MatchesAsync(
        LibraryFolders folders,
        ProcessingPlanInput expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(expected);
        string source = ControlledPathGuard.RequireDirectChild(
            folders.InboxDirectory,
            expected.FullPath);
        if (!File.Exists(source))
        {
            return false;
        }

        ControlledReparseGuard.RejectPath(
            folders.InboxDirectory,
            source,
            includeCandidate: true);
        var before = new FileInfo(source);
        if (!MatchesSnapshot(before, expected))
        {
            return false;
        }

        string actualSha256;
        try
        {
            actualSha256 = await hasher.ComputeSha256Async(
                source,
                cancellationToken);
        }
        catch (Exception error) when (error is FileNotFoundException
            or DirectoryNotFoundException)
        {
            return false;
        }

        before.Refresh();
        return MatchesSnapshot(before, expected)
            && StringComparer.OrdinalIgnoreCase.Equals(
                actualSha256,
                expected.Sha256);
    }

    private static bool MatchesSnapshot(
        FileInfo file,
        ProcessingPlanInput expected) =>
        file.Exists
        && file.Length == expected.Size
        && new DateTimeOffset(file.LastWriteTimeUtc) == expected.LastWriteTimeUtc;
}
