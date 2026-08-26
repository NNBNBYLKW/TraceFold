using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly ILibraryQueryService _library;
    private readonly IAssetRevealService _reveal;
    private readonly IMediaPreviewResolver _previewResolver;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private bool _settingSelection;

    public LibraryViewModel(
        ILibraryQueryService library,
        LibraryInspectorViewModel inspector,
        IAssetRevealService reveal,
        IMediaPreviewResolver previewResolver)
    {
        _library = library;
        Inspector = inspector;
        _reveal = reveal;
        _previewResolver = previewResolver;
        Inspector.Metadata.Saved += OnMetadataSaved;
    }

    public ObservableCollection<LibraryCardViewModel> Items { get; } = [];

    public LibraryInspectorViewModel Inspector { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LibraryCardViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = string.Empty;

    public Task LoadAsync(CancellationToken cancellationToken) =>
        ReloadAsync(cancellationToken);

    public Task ApplySearchAsync(CancellationToken cancellationToken) =>
        ReloadAsync(cancellationToken);

    public async Task SelectAsync(
        LibraryCardViewModel? item,
        CancellationToken cancellationToken)
    {
        if (ReferenceEquals(SelectedItem, item)
            && Inspector.AssetId == item?.Id)
        {
            return;
        }

        _settingSelection = true;
        try
        {
            SelectedItem = item;
        }
        finally
        {
            _settingSelection = false;
        }

        if (item is null)
        {
            await Inspector.ResetAsync(cancellationToken);
            RevealSelectedCommand.NotifyCanExecuteChanged();
            return;
        }

        LibraryAssetDetailsDto? details = await _library.GetDetailsAsync(
            item.Id,
            cancellationToken);
        if (details is null)
        {
            await Inspector.ResetAsync(cancellationToken);
            StatusMessage = "该正式资产已不存在。";
        }
        else
        {
            await Inspector.LoadAsync(details, cancellationToken);
            StatusMessage = string.Empty;
        }

        RevealSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRevealSelected))]
    private async Task RevealSelectedAsync(CancellationToken cancellationToken)
    {
        LibraryCardViewModel? selected = SelectedItem;
        if (selected is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            AssetRevealResult result = await _reveal.RevealAsync(
                selected.Id,
                cancellationToken);
            StatusMessage = result.Succeeded
                ? string.Empty
                : "正式文件已不在登记位置。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = value;
        _ = ReloadSafeAsync();
    }

    partial void OnSelectedItemChanged(LibraryCardViewModel? value)
    {
        if (!_settingSelection)
        {
            _ = SelectSafeAsync(value);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        _ = value;
        RevealSelectedCommand.NotifyCanExecuteChanged();
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            Guid? selectedId = SelectedItem?.Id;
            IReadOnlyList<LibraryAssetDto> cards = await _library.SearchAsync(
                LibraryQuery.Create(
                    SearchText,
                    LibraryMediaFilter.All,
                    [],
                    null,
                    [],
                    false,
                    false,
                    false,
                    LibraryDisplayNameFilter.All,
                    LibrarySortField.CreatedAt,
                    LibrarySortDirection.Descending),
                cancellationToken);
            var refreshed = new List<LibraryCardViewModel>(cards.Count);
            foreach (LibraryAssetDto card in cards)
            {
                string? thumbnail = null;
                if (card.MediaKind == MediaKind.Image)
                {
                    try
                    {
                        PreviewDescriptor descriptor = await _previewResolver.ResolveAsync(
                            new MetadataOwner(MetadataOwnerKind.MediaAsset, card.Id),
                            cancellationToken);
                        if (descriptor.Exists)
                        {
                            thumbnail = new Uri(descriptor.TrustedFullPath).AbsoluteUri;
                        }
                    }
                    catch (PreviewNotRegisteredException)
                    {
                    }
                }

                refreshed.Add(new LibraryCardViewModel(card, thumbnail));
            }

            Items.Clear();
            foreach (LibraryCardViewModel item in refreshed)
            {
                Items.Add(item);
            }

            if (selectedId.HasValue)
            {
                _settingSelection = true;
                try
                {
                    SelectedItem = Items.FirstOrDefault(item => item.Id == selectedId.Value);
                }
                finally
                {
                    _settingSelection = false;
                }
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task ReloadSafeAsync()
    {
        try
        {
            await ReloadAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            StatusMessage = $"刷新正式文件库失败：{error.Message}";
        }
    }

    private async Task SelectSafeAsync(LibraryCardViewModel? item)
    {
        try
        {
            await SelectAsync(item, CancellationToken.None);
        }
        catch (Exception error)
        {
            StatusMessage = $"加载资产详情失败：{error.Message}";
        }
    }

    private void OnMetadataSaved(MetadataOwner owner)
    {
        if (owner.Kind == MetadataOwnerKind.MediaAsset)
        {
            _ = ReloadSafeAsync();
        }
    }

    private bool CanRevealSelected() => !IsBusy && SelectedItem is not null;
}
