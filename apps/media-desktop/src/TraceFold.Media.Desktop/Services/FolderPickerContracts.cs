namespace TraceFold.Media.Desktop.Services;

public interface IFolderPicker
{
    Task<string?> PickFolderAsync(
        string title,
        CancellationToken cancellationToken);
}
