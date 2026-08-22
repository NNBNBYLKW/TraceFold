using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Desktop.Services;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class MetadataEditorViewModelTests
{
    private static readonly MetadataOwner FirstOwner =
        new(MetadataOwnerKind.InboxItem, Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private static readonly MetadataOwner SecondOwner =
        new(MetadataOwnerKind.InboxItem, Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public async Task Edit_saves_once_after_500_milliseconds()
    {
        var gateway = new RecordingMetadataGateway();
        var scheduler = new ManualDebounceScheduler();
        var viewModel = new MetadataEditorViewModel(gateway, scheduler);
        await viewModel.LoadAsync(FirstOwner, TestContext.Current.CancellationToken);

        viewModel.DisplayName = "A";
        viewModel.DisplayName = "AB";
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(499));
        await scheduler.DrainAsync();
        Assert.Equal(0, gateway.SaveCount);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await scheduler.DrainAsync();

        Assert.Equal(1, gateway.SaveCount);
        Assert.Equal("AB", gateway.LastUpdate!.DisplayName);
    }

    [Fact]
    public async Task Switching_selection_flushes_old_owner_before_loading_new()
    {
        var gateway = new RecordingMetadataGateway();
        var viewModel = new MetadataEditorViewModel(
            gateway,
            new ManualDebounceScheduler());
        await viewModel.LoadAsync(FirstOwner, TestContext.Current.CancellationToken);
        viewModel.DisplayName = "unsaved";

        await viewModel.LoadAsync(SecondOwner, TestContext.Current.CancellationToken);

        Assert.Equal(FirstOwner, Assert.Single(gateway.SavedOwners));
        Assert.Equal(SecondOwner, viewModel.Owner);
    }

    [Fact]
    public async Task Version_conflict_reloads_latest_document_and_shows_message()
    {
        var gateway = new RecordingMetadataGateway { ConflictOnSave = true };
        var viewModel = new MetadataEditorViewModel(
            gateway,
            new ManualDebounceScheduler());
        await viewModel.LoadAsync(FirstOwner, TestContext.Current.CancellationToken);
        viewModel.DisplayName = "stale";
        gateway.LatestDisplayName = "latest";

        await viewModel.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal("latest", viewModel.DisplayName);
        Assert.Equal("资料已在其他位置更新，请重新确认。", viewModel.ErrorMessage);
    }

    private sealed class RecordingMetadataGateway : IMetadataEditorService
    {
        public int SaveCount { get; private set; }

        public MetadataUpdate? LastUpdate { get; private set; }

        public List<MetadataOwner> SavedOwners { get; } = [];

        public bool ConflictOnSave { get; init; }

        public string? LatestDisplayName { get; set; }

        public Task<MetadataDocument?> LoadAsync(
            MetadataOwner owner,
            CancellationToken cancellationToken) =>
            Task.FromResult<MetadataDocument?>(new MetadataDocument(
                owner,
                LatestDisplayName,
                [],
                null,
                [],
                ConflictOnSave ? 2 : 0));

        public Task<MetadataSaveResult> SaveAsync(
            MetadataOwner owner,
            MetadataUpdate update,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            LastUpdate = update;
            SavedOwners.Add(owner);
            return Task.FromResult(ConflictOnSave
                ? new MetadataSaveResult(
                    false,
                    2,
                    "metadata.version_conflict")
                : new MetadataSaveResult(
                    true,
                    update.ExpectedVersion + 1,
                    null));
        }
    }

    private sealed class ManualDebounceScheduler : IDebounceScheduler
    {
        private readonly List<ScheduledDelay> _delays = [];
        private TimeSpan _elapsed;

        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
            _delays.Add(new ScheduledDelay(
                _elapsed + delay,
                completion,
                registration));
            return completion.Task;
        }

        public void AdvanceBy(TimeSpan duration)
        {
            _elapsed += duration;
            foreach (ScheduledDelay delay in _delays.Where(
                         item => item.DueAt <= _elapsed).ToArray())
            {
                delay.Registration.Dispose();
                delay.Completion.TrySetResult();
                _delays.Remove(delay);
            }
        }

        public async Task DrainAsync()
        {
            for (int iteration = 0; iteration < 8; iteration++)
            {
                await Task.Yield();
            }
        }

        private sealed record ScheduledDelay(
            TimeSpan DueAt,
            TaskCompletionSource Completion,
            CancellationTokenRegistration Registration);
    }
}
