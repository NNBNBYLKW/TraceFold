namespace TraceFold.Media.Application.Ingest;

public sealed record RecoveryLocationDto(string TrustedFullPath);

public interface IRecoveryLocationResolver
{
    Task<RecoveryLocationDto?> ResolveAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}
