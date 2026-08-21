using System.Security.Cryptography;
using TraceFold.Media.Application.Storage;

namespace TraceFold.Media.Infrastructure.Filesystem;

public sealed class Sha256FileHasher : IFileHasher
{
    public async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
