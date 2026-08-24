namespace TraceFold.Media.Desktop.Services;

public interface IRecoveryRevealService
{
    Task<bool> RevealAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}
