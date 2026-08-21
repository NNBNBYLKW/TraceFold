using TraceFold.Media.Infrastructure.Settings;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Settings;

public sealed class LocalDirectoryProbeTests
{
    [Fact]
    public async Task CanWriteAsync_removes_its_probe_file()
    {
        using var temp = TestDirectory.Create();
        var probe = new LocalDirectoryProbe();

        bool writable = await probe.CanWriteAsync(
            temp.Path,
            CancellationToken.None);

        Assert.True(writable);
        Assert.Empty(Directory.GetFiles(
            temp.Path,
            ".tracefold-write-probe-*.tmp"));
    }

    [Fact]
    public async Task CanWriteAsync_returns_false_for_a_missing_directory()
    {
        using var temp = TestDirectory.Create();
        string missing = Path.Combine(temp.Path, "missing");
        var probe = new LocalDirectoryProbe();

        bool writable = await probe.CanWriteAsync(
            missing,
            CancellationToken.None);

        Assert.False(writable);
        Assert.False(Directory.Exists(missing));
    }
}
