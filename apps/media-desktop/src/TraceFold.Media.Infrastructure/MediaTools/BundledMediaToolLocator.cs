using TraceFold.Media.Infrastructure.Filesystem;

namespace TraceFold.Media.Infrastructure.MediaTools;

public sealed class BundledMediaToolLocator(MediaToolOptions options)
{
    public MediaToolLocation? TryResolve()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(options.RootDirectory)
                || !Path.IsPathFullyQualified(options.RootDirectory)
                || !Directory.Exists(options.RootDirectory))
            {
                return null;
            }

            string root = Path.GetFullPath(options.RootDirectory);
            ControlledReparseGuard.RejectPath(root, root, includeCandidate: true);
            string ffmpeg = ControlledPathGuard.RequireDirectChild(
                root,
                Path.Combine(root, "ffmpeg.exe"));
            string ffprobe = ControlledPathGuard.RequireDirectChild(
                root,
                Path.Combine(root, "ffprobe.exe"));
            if (!File.Exists(ffmpeg) || !File.Exists(ffprobe))
            {
                return null;
            }

            ControlledReparseGuard.RejectPath(root, ffmpeg, includeCandidate: true);
            ControlledReparseGuard.RejectPath(root, ffprobe, includeCandidate: true);
            return new MediaToolLocation(ffmpeg, ffprobe);
        }
        catch (Exception error) when (error is ControlledPathException
            or ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
