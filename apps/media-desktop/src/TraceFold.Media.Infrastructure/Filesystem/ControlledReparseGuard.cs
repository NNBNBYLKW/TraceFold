namespace TraceFold.Media.Infrastructure.Filesystem;

internal static class ControlledReparseGuard
{
    public static void RejectPath(
        string root,
        string candidate,
        bool includeCandidate)
    {
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string canonicalCandidate = Path.GetFullPath(candidate);
        string stop = includeCandidate
            ? canonicalCandidate
            : Path.GetDirectoryName(canonicalCandidate) ?? canonicalRoot;
        string? current = stop;
        while (current is not null)
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                FileAttributes attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ControlledPathException(
                        "A controlled storage path contains a reparse point.");
                }
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(
                    Path.TrimEndingDirectorySeparator(current),
                    canonicalRoot))
            {
                return;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new ControlledPathException(
            "The controlled storage path does not descend from its root.");
    }

    public static void RejectTree(string root, string ownedDirectory)
    {
        RejectPath(root, ownedDirectory, includeCandidate: true);
        var pending = new Stack<string>();
        pending.Push(ownedDirectory);
        while (pending.TryPop(out string? directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ControlledPathException(
                        "A controlled operation directory contains a reparse point.");
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(path);
                }
            }
        }
    }
}
