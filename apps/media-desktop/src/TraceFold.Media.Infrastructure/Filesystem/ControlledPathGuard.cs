namespace TraceFold.Media.Infrastructure.Filesystem;

public sealed class ControlledPathException : InvalidOperationException
{
    public ControlledPathException(string message)
        : base(message)
    {
    }

    public ControlledPathException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class ControlledPathGuard
{
    public static string RequireDirectChild(string root, string candidate)
    {
        string canonicalRoot = CanonicalizeRoot(root);
        string canonicalCandidate = CanonicalizeCandidate(candidate);
        string? parent = Path.GetDirectoryName(canonicalCandidate);

        if (parent is null
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.TrimEndingDirectorySeparator(parent),
                canonicalRoot))
        {
            throw new ControlledPathException(
                "The candidate path is not a direct child of the controlled root.");
        }

        return canonicalCandidate;
    }

    public static string RequireDescendant(string root, string candidate)
    {
        string canonicalRoot = CanonicalizeRoot(root);
        string canonicalCandidate = CanonicalizeCandidate(candidate);
        string relative = Path.GetRelativePath(canonicalRoot, canonicalCandidate);

        if (relative == "."
            || Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ControlledPathException(
                "The candidate path is not a descendant of the controlled root.");
        }

        return canonicalCandidate;
    }

    private static string CanonicalizeRoot(string root)
    {
        string canonicalRoot = Canonicalize(root, "root");
        return Path.TrimEndingDirectorySeparator(canonicalRoot);
    }

    private static string CanonicalizeCandidate(string candidate) =>
        Canonicalize(candidate, "candidate");

    private static string Canonicalize(string path, string role)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                throw new ControlledPathException(
                    $"The controlled {role} path must be fully qualified.");
            }

            return Path.GetFullPath(path);
        }
        catch (ControlledPathException)
        {
            throw;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            throw new ControlledPathException(
                $"The controlled {role} path is invalid.",
                error);
        }
    }
}
