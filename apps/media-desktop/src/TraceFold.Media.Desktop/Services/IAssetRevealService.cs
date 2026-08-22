using System.Diagnostics;

namespace TraceFold.Media.Desktop.Services;

public sealed record AssetRevealResult(bool Succeeded, string? ErrorCode);

public interface IAssetRevealService
{
    Task<AssetRevealResult> RevealAsync(
        Guid assetId,
        CancellationToken cancellationToken);
}

public interface IProcessStarter
{
    void Start(ProcessStartInfo startInfo);
}
