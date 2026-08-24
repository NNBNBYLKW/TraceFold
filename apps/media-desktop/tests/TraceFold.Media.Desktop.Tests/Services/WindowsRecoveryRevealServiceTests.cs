using System.Diagnostics;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Desktop.Services;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Services;

public sealed class WindowsRecoveryRevealServiceTests
{
    [Fact]
    public async Task Reveal_starts_explorer_with_only_the_guarded_resolved_path()
    {
        Guid itemId = Guid.NewGuid();
        string path = "C:\\library\\.tracefold\\recovery\\original.jpg";
        var starter = new RecordingProcessStarter();
        var service = new WindowsRecoveryRevealService(
            new FixedResolver(new RecoveryLocationDto(path)),
            starter);

        bool revealed = await service.RevealAsync(
            itemId,
            TestContext.Current.CancellationToken);

        Assert.True(revealed);
        ProcessStartInfo start = Assert.IsType<ProcessStartInfo>(starter.StartInfo);
        Assert.Equal("explorer.exe", start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.Equal(["/select,", path], start.ArgumentList);
    }

    [Fact]
    public async Task Missing_guarded_location_does_not_start_a_process()
    {
        var starter = new RecordingProcessStarter();
        var service = new WindowsRecoveryRevealService(
            new FixedResolver(null),
            starter);

        bool revealed = await service.RevealAsync(
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(revealed);
        Assert.Null(starter.StartInfo);
    }

    private sealed class FixedResolver(RecoveryLocationDto? result)
        : IRecoveryLocationResolver
    {
        public Task<RecoveryLocationDto?> ResolveAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class RecordingProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo) => StartInfo = startInfo;
    }
}
