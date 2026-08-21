using Microsoft.Extensions.Logging;

namespace TraceFold.Media.Desktop.Services;

public sealed class WindowsFolderPicker(
    WindowContext windowContext,
    ILogger<WindowsFolderPicker> logger) : IFolderPicker
{
    public async Task<string?> PickFolderAsync(
        string title,
        CancellationToken cancellationToken)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation =
                Windows.Storage.Pickers.PickerLocationId.VideosLibrary,
            CommitButtonText = title
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            windowContext.GetWindowHandle());
        logger.LogInformation("FolderPickerOpened");
        try
        {
            Windows.Storage.StorageFolder? folder =
                await picker.PickSingleFolderAsync().AsTask(cancellationToken);
            logger.LogInformation(
                "FolderPickerCompleted HasSelection={HasSelection} HasPhysicalPath={HasPhysicalPath}",
                folder is not null,
                !string.IsNullOrWhiteSpace(folder?.Path));
            return folder?.Path;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "FolderPickerFailed");
            throw;
        }
    }
}
