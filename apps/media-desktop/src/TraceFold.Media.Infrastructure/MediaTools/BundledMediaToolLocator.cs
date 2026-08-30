using TraceFold.Media.Infrastructure.Filesystem;

namespace TraceFold.Media.Infrastructure.MediaTools;

public enum BundledMediaToolCandidateState
{
    Available,
    Missing,
    Unsafe
}

public sealed record BundledMediaToolCandidate(
    BundledMediaToolCandidateState State,
    string? Path);

public sealed record BundledMediaToolInspection(
    BundledMediaToolCandidate Ffmpeg,
    BundledMediaToolCandidate Ffprobe);

public sealed class BundledMediaToolLocator(MediaToolOptions options)
{
    public MediaToolLocation? TryResolve()
    {
        BundledMediaToolInspection inspection = Inspect();
        return inspection.Ffmpeg.State == BundledMediaToolCandidateState.Available
            && inspection.Ffprobe.State == BundledMediaToolCandidateState.Available
            ? new MediaToolLocation(
                inspection.Ffmpeg.Path!,
                inspection.Ffprobe.Path!)
            : null;
    }

    public BundledMediaToolInspection Inspect()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(options.RootDirectory)
                || !Path.IsPathFullyQualified(options.RootDirectory)
                || !Directory.Exists(options.RootDirectory))
            {
                return UnsafeInspection();
            }

            string root = Path.GetFullPath(options.RootDirectory);
            ControlledReparseGuard.RejectPath(root, root, includeCandidate: true);
            return new BundledMediaToolInspection(
                InspectCandidate(root, "ffmpeg.exe"),
                InspectCandidate(root, "ffprobe.exe"));
        }
        catch (Exception error) when (error is ControlledPathException
            or ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException)
        {
            return UnsafeInspection();
        }
    }

    private static BundledMediaToolCandidate InspectCandidate(
        string root,
        string filename)
    {
        try
        {
            string candidate = ControlledPathGuard.RequireDirectChild(
                root,
                Path.Combine(root, filename));
            if (!File.Exists(candidate))
            {
                return new BundledMediaToolCandidate(
                    BundledMediaToolCandidateState.Missing,
                    candidate);
            }

            ControlledReparseGuard.RejectPath(
                root,
                candidate,
                includeCandidate: true);
            return new BundledMediaToolCandidate(
                BundledMediaToolCandidateState.Available,
                candidate);
        }
        catch (Exception error) when (error is ControlledPathException
            or ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException)
        {
            return new BundledMediaToolCandidate(
                BundledMediaToolCandidateState.Unsafe,
                Path: null);
        }
    }

    private static BundledMediaToolInspection UnsafeInspection() =>
        new(
            new BundledMediaToolCandidate(
                BundledMediaToolCandidateState.Unsafe,
                Path: null),
            new BundledMediaToolCandidate(
                BundledMediaToolCandidateState.Unsafe,
                Path: null));
}
