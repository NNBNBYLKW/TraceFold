using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Intake;

public sealed record FileSnapshot(
    string FullPath,
    string Filename,
    string Extension,
    FileFingerprint Fingerprint);

public interface IInboxFileCatalog
{
    Task<IReadOnlyList<FileSnapshot>> EnumerateTopLevelAsync(
        string inboxDirectory,
        IReadOnlySet<string> observedExtensions,
        CancellationToken cancellationToken);
}

public sealed class IntakeFileAccessException : IOException
{
    public IntakeFileAccessException(
        string errorCode,
        string root,
        Exception innerException)
        : base($"Inbox enumeration failed with code {errorCode}.", innerException)
    {
        ErrorCode = errorCode;
        Root = root;
    }

    public string ErrorCode { get; }

    public string Root { get; }
}
