using System.Text;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Filesystem;

public sealed class Sha256FileHasherTests
{
    [Fact]
    public async Task ComputeSha256Async_returns_uppercase_hash_for_known_bytes()
    {
        using var directory = TestDirectory.Create();
        string path = Path.Combine(directory.Path, "known.bin");
        await File.WriteAllBytesAsync(
            path,
            Encoding.ASCII.GetBytes("abc"),
            TestContext.Current.CancellationToken);
        var hasher = new Sha256FileHasher();

        string result = await hasher.ComputeSha256Async(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            result);
    }

    [Fact]
    public async Task ComputeSha256Async_observes_preexisting_cancellation()
    {
        using var directory = TestDirectory.Create();
        string path = Path.Combine(directory.Path, "cancel.bin");
        await File.WriteAllBytesAsync(
            path,
            [1, 2, 3],
            TestContext.Current.CancellationToken);
        var hasher = new Sha256FileHasher();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            hasher.ComputeSha256Async(path, cancellation.Token));
    }
}
