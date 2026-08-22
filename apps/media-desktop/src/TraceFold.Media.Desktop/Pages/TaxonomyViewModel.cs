using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Pages;

public sealed record TaxonomyKindOption(
    TaxonomyKind Kind,
    string DisplayName);

public sealed partial class TaxonomyViewModel : ObservableObject
{
    private readonly ITaxonomyService _taxonomy;
    private readonly ILogger<TaxonomyViewModel>? _logger;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    public TaxonomyViewModel(
        ITaxonomyService taxonomy,
        ILogger<TaxonomyViewModel>? logger = null)
    {
        _taxonomy = taxonomy;
        _logger = logger;
        CreateCommand = new AsyncRelayCommand<string?>(CreateAsync);
        RenameCommand = new AsyncRelayCommand<string?>(
            RenameAsync,
            _ => SelectedTerm is not null);
        DeleteCommand = new AsyncRelayCommand(
            DeleteAsync,
            () => SelectedTerm is { ReferenceCount: 0 });
    }

    public IReadOnlyList<TaxonomyKindOption> KindOptions { get; } =
    [
        new(TaxonomyKind.Author, "作者"),
        new(TaxonomyKind.Series, "系列"),
        new(TaxonomyKind.Tag, "标签")
    ];

    public ObservableCollection<TaxonomyTermDto> Terms { get; } = [];

    public IAsyncRelayCommand<string?> CreateCommand { get; }

    public IAsyncRelayCommand<string?> RenameCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    [ObservableProperty]
    public partial TaxonomyKind SelectedKind { get; set; } = TaxonomyKind.Author;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TaxonomyTermDto? SelectedTerm { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool CanRename { get; private set; }

    [ObservableProperty]
    public partial bool CanDelete { get; private set; }

    public Task LoadAsync(CancellationToken cancellationToken) =>
        ReloadAsync(null, cancellationToken);

    public Task SelectAsync(
        TaxonomyTermDto? term,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SelectedTerm = term;
        return Task.CompletedTask;
    }

    public async Task CreateAsync(
        string? name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "请输入词条名称。";
            return;
        }

        try
        {
            TaxonomyTermDto created = await _taxonomy.CreateAsync(
                SelectedKind,
                name,
                cancellationToken);
            await ReloadAsync(created.Id, cancellationToken);
            StatusMessage = "词条已创建。";
        }
        catch (ArgumentException)
        {
            StatusMessage = "请输入有效的词条名称。";
        }
    }

    public async Task RenameAsync(
        string? name,
        CancellationToken cancellationToken)
    {
        TaxonomyTermDto? selected = SelectedTerm;
        if (selected is null || string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = selected is null
                ? "请先选择词条。"
                : "请输入词条名称。";
            return;
        }

        TaxonomyMutationResult result;
        try
        {
            result = await _taxonomy.RenameAsync(
                selected.Id,
                name,
                cancellationToken);
        }
        catch (ArgumentException)
        {
            StatusMessage = "请输入有效的词条名称。";
            return;
        }

        if (!result.Succeeded)
        {
            StatusMessage = result.ErrorCode switch
            {
                "taxonomy.name_conflict" => "词条名称已存在。",
                "taxonomy.not_found" => "词条已不存在。",
                _ => "重命名失败，请重试。"
            };
            return;
        }

        await ReloadAsync(selected.Id, cancellationToken);
        StatusMessage = "词条已重命名。";
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        TaxonomyTermDto? selected = SelectedTerm;
        if (selected is null)
        {
            StatusMessage = "请先选择词条。";
            return;
        }

        if (selected.ReferenceCount > 0)
        {
            SetSelectionStatus(selected);
            return;
        }

        TaxonomyMutationResult result = await _taxonomy.DeleteAsync(
            selected.Id,
            cancellationToken);
        if (!result.Succeeded)
        {
            string message = result.ErrorCode switch
            {
                "taxonomy.in_use" => "该词条已被资料引用，不能删除。",
                "taxonomy.not_found" => "词条已不存在。",
                _ => "删除失败，请重试。"
            };
            await ReloadAsync(null, cancellationToken);
            StatusMessage = message;
            return;
        }

        await ReloadAsync(null, cancellationToken);
        StatusMessage = "词条已删除。";
    }

    partial void OnSelectedKindChanged(TaxonomyKind value)
    {
        _ = value;
        _ = ReloadSafeAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = value;
        _ = ReloadSafeAsync();
    }

    partial void OnSelectedTermChanged(TaxonomyTermDto? value)
    {
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CanRename = value is not null;
        CanDelete = value is { ReferenceCount: 0 };
        SetSelectionStatus(value);
    }

    private async Task ReloadAsync(
        Guid? selectId,
        CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        IsBusy = true;
        try
        {
            IReadOnlyList<TaxonomyTermDto> terms = await _taxonomy.SearchAsync(
                SelectedKind,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                cancellationToken);
            Terms.Clear();
            foreach (TaxonomyTermDto term in terms)
            {
                Terms.Add(term);
            }

            SelectedTerm = selectId.HasValue
                ? Terms.FirstOrDefault(term => term.Id == selectId.Value)
                : null;
        }
        finally
        {
            IsBusy = false;
            _reloadGate.Release();
        }
    }

    private async Task ReloadSafeAsync()
    {
        try
        {
            await ReloadAsync(null, CancellationToken.None);
        }
        catch (Exception error)
        {
            _logger?.LogError(error, "TaxonomyReloadFailed");
            StatusMessage = "加载词条失败，请重试。";
        }
    }

    public void ReportOperationFailure() =>
        StatusMessage = "词条操作失败，请重试。";

    private void SetSelectionStatus(TaxonomyTermDto? term)
    {
        StatusMessage = term is { ReferenceCount: > 0 }
            ? $"该词条已被 {term.ReferenceCount} 项资料引用，不能删除。"
            : string.Empty;
    }
}
