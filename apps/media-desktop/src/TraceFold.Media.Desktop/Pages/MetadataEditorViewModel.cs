using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class MetadataEditorViewModel(
    IMetadataEditorService service,
    IDebounceScheduler scheduler) : ObservableObject
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _delayCancellation;
    private Task _pendingSave = Task.CompletedTask;
    private long _version;
    private long _revision;
    private bool _dirty;
    private bool _suppressChanges;

    [ObservableProperty]
    public partial MetadataOwner? Owner { get; private set; }

    [ObservableProperty]
    public partial string? DisplayName { get; set; }

    [ObservableProperty]
    public partial TaxonomyTermDto? Series { get; private set; }

    [ObservableProperty]
    public partial bool IsSaving { get; private set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; private set; } = string.Empty;

    public ObservableCollection<TaxonomyTermDto> Authors { get; } = [];

    public ObservableCollection<TaxonomyTermDto> Tags { get; } = [];

    public async Task LoadAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            if (Owner == owner)
            {
                return;
            }

            await FlushAsync(cancellationToken);
            MetadataDocument? document = await service.LoadAsync(owner, cancellationToken);
            Owner = owner;
            if (document is null)
            {
                ApplyEmpty();
                ErrorMessage = "未找到可编辑的资料记录。";
                return;
            }

            ApplyDocument(document);
            ErrorMessage = string.Empty;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        CancelDelay();
        await _pendingSave.WaitAsync(cancellationToken);
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            if (_dirty && Owner is not null)
            {
                await SaveCurrentAsync(Owner, _version, cancellationToken);
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void ApplyTaxonomySelection(
        TaxonomyKind kind,
        IReadOnlyList<TaxonomyTermDto> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        _suppressChanges = true;
        try
        {
            switch (kind)
            {
                case TaxonomyKind.Author:
                    Replace(Authors, terms.Where(term => term.Kind == kind));
                    break;
                case TaxonomyKind.Series:
                    Series = terms.LastOrDefault(term => term.Kind == kind);
                    break;
                case TaxonomyKind.Tag:
                    Replace(Tags, terms.Where(term => term.Kind == kind));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        finally
        {
            _suppressChanges = false;
        }

        MarkDirtyAndSchedule();
    }

    partial void OnDisplayNameChanged(string? value)
    {
        _ = value;
        MarkDirtyAndSchedule();
    }

    private void MarkDirtyAndSchedule()
    {
        if (_suppressChanges || Owner is null)
        {
            return;
        }

        _dirty = true;
        _revision++;
        CancelDelay();
        _delayCancellation = new CancellationTokenSource();
        MetadataOwner capturedOwner = Owner;
        long capturedVersion = _version;
        _pendingSave = DebounceAndSaveAsync(
            capturedOwner,
            capturedVersion,
            _delayCancellation.Token);
    }

    private async Task DebounceAndSaveAsync(
        MetadataOwner capturedOwner,
        long capturedVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.DelayAsync(SaveDelay, cancellationToken);
            await _saveGate.WaitAsync(cancellationToken);
            try
            {
                if (_dirty
                    && Owner == capturedOwner
                    && _version == capturedVersion)
                {
                    await SaveCurrentAsync(
                        capturedOwner,
                        capturedVersion,
                        cancellationToken);
                }
            }
            finally
            {
                _saveGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SaveCurrentAsync(
        MetadataOwner owner,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        long capturedRevision = _revision;
        IsSaving = true;
        try
        {
            MetadataSaveResult result = await service.SaveAsync(
                owner,
                new MetadataUpdate(
                    DisplayName,
                    Authors.Select(term => term.Id).ToArray(),
                    Series?.Id,
                    Tags.Select(term => term.Id).ToArray(),
                    expectedVersion),
                cancellationToken);
            if (result.Succeeded)
            {
                _version = result.CurrentVersion;
                if (_revision == capturedRevision)
                {
                    _dirty = false;
                }

                ErrorMessage = string.Empty;
                return;
            }

            if (result.ErrorCode == "metadata.version_conflict")
            {
                MetadataDocument? latest = await service.LoadAsync(
                    owner,
                    cancellationToken);
                if (latest is not null)
                {
                    ApplyDocument(latest);
                }

                ErrorMessage = "资料已在其他位置更新，请重新确认。";
                return;
            }

            ErrorMessage = "资料保存失败，请重试。";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ApplyDocument(MetadataDocument document)
    {
        _suppressChanges = true;
        try
        {
            DisplayName = document.DisplayName;
            Replace(Authors, document.Authors);
            Series = document.Series;
            Replace(Tags, document.Tags);
            _version = document.Version;
            _dirty = false;
        }
        finally
        {
            _suppressChanges = false;
        }
    }

    private void ApplyEmpty()
    {
        _suppressChanges = true;
        try
        {
            DisplayName = null;
            Authors.Clear();
            Series = null;
            Tags.Clear();
            _version = 0;
            _dirty = false;
        }
        finally
        {
            _suppressChanges = false;
        }
    }

    private void CancelDelay()
    {
        CancellationTokenSource? cancellation = _delayCancellation;
        _delayCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private static void Replace(
        ObservableCollection<TaxonomyTermDto> target,
        IEnumerable<TaxonomyTermDto> values)
    {
        target.Clear();
        foreach (TaxonomyTermDto value in values.DistinctBy(term => term.Id))
        {
            target.Add(value);
        }
    }
}
