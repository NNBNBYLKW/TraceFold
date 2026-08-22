using System.Diagnostics;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Desktop.Services;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Services;

public sealed class WindowsAssetRevealServiceTests
{
    [Fact]
    public async Task RevealAsync_starts_explorer_with_only_registered_asset_path()
    {
        Guid assetId = Guid.NewGuid();
        string path = "C:\\library\\photo.jpg";
        var starter = new RecordingProcessStarter();
        var service = new WindowsAssetRevealService(
            new FixedPreviewResolver(new PreviewDescriptor(
                MediaKind.Image,
                path,
                true)),
            starter);

        AssetRevealResult result = await service.RevealAsync(
            assetId,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        ProcessStartInfo start = Assert.IsType<ProcessStartInfo>(starter.StartInfo);
        Assert.Equal("explorer.exe", start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.Equal(["/select,", path], start.ArgumentList);
    }

    [Fact]
    public async Task Missing_asset_does_not_start_explorer()
    {
        var starter = new RecordingProcessStarter();
        var service = new WindowsAssetRevealService(
            new FixedPreviewResolver(new PreviewDescriptor(
                MediaKind.Image,
                "C:\\library\\missing.jpg",
                false)),
            starter);

        AssetRevealResult result = await service.RevealAsync(
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("asset.file_missing", result.ErrorCode);
        Assert.Null(starter.StartInfo);
    }

    private sealed class FixedPreviewResolver(PreviewDescriptor descriptor)
        : IMediaPreviewResolver
    {
        public Task<PreviewDescriptor> ResolveAsync(
            MetadataOwner owner,
            CancellationToken cancellationToken) => Task.FromResult(descriptor);
    }

    private sealed class RecordingProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo) => StartInfo = startInfo;
    }
}
