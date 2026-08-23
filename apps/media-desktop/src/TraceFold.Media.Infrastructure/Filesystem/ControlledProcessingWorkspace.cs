using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Infrastructure.Filesystem;

public sealed class ControlledProcessingWorkspace : IProcessingWorkspace
{
    private const string PartialFilename = "merged.partial.mp4";

    public Task<ProcessingWorkspacePaths> PrepareAsync(
        Guid jobId,
        LibraryFolders folders,
        string outputFilename,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessingWorkspacePaths paths = ResolvePaths(jobId, folders, outputFilename);
        ControlledReparseGuard.RejectPath(
            folders.WorkDirectory,
            paths.RootDirectory,
            includeCandidate: false);
        Directory.CreateDirectory(paths.RootDirectory);
        ControlledReparseGuard.RejectPath(
            folders.WorkDirectory,
            paths.RootDirectory,
            includeCandidate: true);
        if (File.Exists(paths.PartialOutputPath)
            || File.Exists(paths.FinalOutputPath))
        {
            throw new IOException(
                "The processing workspace already contains an output payload.");
        }

        return Task.FromResult(paths);
    }

    public Task<string> PromoteVerifiedAsync(
        Guid jobId,
        LibraryFolders folders,
        ProcessingWorkspacePaths paths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(paths);
        ProcessingWorkspacePaths expected = ResolvePaths(
            jobId,
            folders,
            Path.GetFileName(paths.FinalOutputPath));
        if (!PathsEqual(paths.RootDirectory, expected.RootDirectory)
            || !PathsEqual(paths.PartialOutputPath, expected.PartialOutputPath)
            || !PathsEqual(paths.FinalOutputPath, expected.FinalOutputPath)
            || !StringComparer.Ordinal.Equals(
                paths.FinalOutputRelativePath,
                expected.FinalOutputRelativePath))
        {
            throw new ControlledPathException(
                "The processing output does not belong to the requested job.");
        }

        ControlledReparseGuard.RejectTree(
            folders.WorkDirectory,
            expected.RootDirectory);
        if (!File.Exists(expected.PartialOutputPath))
        {
            throw new FileNotFoundException(
                "The verified partial output is missing.",
                expected.PartialOutputPath);
        }

        File.Move(
            expected.PartialOutputPath,
            expected.FinalOutputPath,
            overwrite: false);
        return Task.FromResult(expected.FinalOutputPath);
    }

    public Task CleanupUnverifiedAsync(
        Guid jobId,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string processingRoot = ResolveProcessingRoot(folders);
        string jobRoot = ResolveJobRoot(jobId, processingRoot);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetDirectoryName(jobRoot),
                processingRoot)
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(jobRoot),
                jobId.ToString("N")))
        {
            throw new ControlledPathException(
                "The processing workspace is not owned by the requested job.");
        }

        if (Directory.Exists(jobRoot))
        {
            ControlledReparseGuard.RejectTree(folders.WorkDirectory, jobRoot);
            Directory.Delete(jobRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static ProcessingWorkspacePaths ResolvePaths(
        Guid jobId,
        LibraryFolders folders,
        string outputFilename)
    {
        ArgumentNullException.ThrowIfNull(folders);
        string filename = RequireOutputFilename(outputFilename);
        string processingRoot = ResolveProcessingRoot(folders);
        string jobRoot = ResolveJobRoot(jobId, processingRoot);
        string partial = ControlledPathGuard.RequireDirectChild(
            jobRoot,
            Path.Combine(jobRoot, PartialFilename));
        string final = ControlledPathGuard.RequireDirectChild(
            jobRoot,
            Path.Combine(jobRoot, filename));
        return new ProcessingWorkspacePaths(
            jobRoot,
            partial,
            final,
            Path.GetRelativePath(folders.WorkDirectory, final));
    }

    private static string ResolveProcessingRoot(LibraryFolders folders) =>
        ControlledPathGuard.RequireDescendant(
            folders.WorkDirectory,
            Path.Combine(folders.WorkDirectory, ".tracefold", "processing"));

    private static string ResolveJobRoot(Guid jobId, string processingRoot) =>
        ControlledPathGuard.RequireDescendant(
            processingRoot,
            Path.Combine(processingRoot, jobId.ToString("N")));

    private static string RequireOutputFilename(string outputFilename)
    {
        if (string.IsNullOrWhiteSpace(outputFilename)
            || Path.IsPathRooted(outputFilename)
            || outputFilename is "." or ".."
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(outputFilename),
                outputFilename)
            || outputFilename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetExtension(outputFilename),
                ".mp4")
            || StringComparer.OrdinalIgnoreCase.Equals(
                outputFilename,
                PartialFilename))
        {
            throw new ControlledPathException(
                "The processing output filename is not safe.");
        }

        return outputFilename;
    }

    private static bool PathsEqual(string left, string right) =>
        StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right));
}
