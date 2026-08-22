using System.Diagnostics;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;

namespace TraceFold.Media.Desktop.Services;

public sealed class WindowsAssetRevealService(
    IMediaPreviewResolver previewResolver,
    IProcessStarter processStarter) : IAssetRevealService
{
    public async Task<AssetRevealResult> RevealAsync(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        PreviewDescriptor descriptor;
        try
        {
            descriptor = await previewResolver.ResolveAsync(
                new MetadataOwner(MetadataOwnerKind.MediaAsset, assetId),
                cancellationToken);
        }
        catch (PreviewNotRegisteredException)
        {
            return new AssetRevealResult(false, "asset.file_missing");
        }

        if (!descriptor.Exists)
        {
            return new AssetRevealResult(false, "asset.file_missing");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/select,");
        startInfo.ArgumentList.Add(descriptor.TrustedFullPath);
        processStarter.Start(startInfo);
        return new AssetRevealResult(true, null);
    }
}

public sealed class WindowsProcessStarter : IProcessStarter
{
    public void Start(ProcessStartInfo startInfo) =>
        Process.Start(startInfo)?.Dispose();
}
