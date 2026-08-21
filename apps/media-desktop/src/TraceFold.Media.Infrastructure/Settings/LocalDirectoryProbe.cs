using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Infrastructure.Settings;

public sealed class LocalDirectoryProbe : IDirectoryProbe
{
    public ValueTask<bool> ExistsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Directory.Exists(path));
    }

    public async ValueTask<bool> CanWriteAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string probePath = Path.Combine(
            path,
            $".tracefold-write-probe-{Guid.NewGuid():N}.tmp");

        try
        {
            await using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await stream.FlushAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }
}
