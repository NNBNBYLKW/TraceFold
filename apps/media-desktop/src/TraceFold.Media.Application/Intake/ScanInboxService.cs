using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;

namespace TraceFold.Media.Application.Intake;

public sealed class ScanInboxService(
    IInboxFileCatalog fileCatalog,
    CandidateGroupingService groupingService,
    DirectorySettingsService directorySettings,
    IInboxScanStore store,
    IInboxActivityNotifier activity,
    TimeProvider timeProvider,
    IApplicationLifetimeToken lifetime,
    ILogger<ScanInboxService> logger,
    IScanPreferencesStore? scanPreferencesStore = null) : IInboxScanner
{
    private readonly object _sync = new();
    private Task<ScanResult>? _activeScan;

    public Task<ScanResult> ScanAsync(
        ScanTrigger trigger,
        CancellationToken cancellationToken)
    {
        Task<ScanResult> shared;
        lock (_sync)
        {
            shared = _activeScan ??= RunAndReleaseAsync(trigger);
        }

        return shared.WaitAsync(cancellationToken);
    }

    private async Task<ScanResult> RunAndReleaseAsync(ScanTrigger trigger)
    {
        await Task.Yield();
        try
        {
            ScanResult result =
                await RunCoreAsync(trigger, lifetime.ApplicationStopping);
            if (scanPreferencesStore is not null)
            {
                try
                {
                    await scanPreferencesStore.SaveLastScanAsync(
                        result.CompletedAtUtc,
                        result.Message,
                        lifetime.ApplicationStopping);
                }
                catch (OperationCanceledException)
                    when (lifetime.ApplicationStopping.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    logger.LogError(error, "ScanResultPersistenceFailed");
                }
            }

            return result;
        }
        finally
        {
            lock (_sync)
            {
                _activeScan = null;
            }
        }
    }

    private async Task<ScanResult> RunCoreAsync(
        ScanTrigger trigger,
        CancellationToken cancellationToken)
    {
        try
        {
            LibraryFolders? savedFolders =
                await directorySettings.LoadAsync(cancellationToken);
            if (savedFolders is null)
            {
                return Result(
                    ScanOutcome.NotConfigured,
                    message: "目录尚未配置。");
            }

            DirectorySettingsResult validation =
                await directorySettings.ValidateAsync(savedFolders, cancellationToken);
            if (!validation.Succeeded || validation.Folders is null)
            {
                return Result(
                    ScanOutcome.InvalidConfiguration,
                    message: "目录配置无效。");
            }

            DateTimeOffset observedAtUtc = timeProvider.GetUtcNow();
            IReadOnlyList<FileSnapshot> snapshots =
                await fileCatalog.EnumerateTopLevelAsync(
                    validation.Folders.InboxDirectory,
                    groupingService.ObservedExtensions,
                    cancellationToken);
            HashSet<string> observedPaths = snapshots
                .Select(snapshot => NormalizePath(snapshot.FullPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyDictionary<string, DateTimeOffset> firstObserved =
                await store.LoadFirstObservedAsync(observedPaths, cancellationToken);
            ObservedFile[] observedFiles = snapshots
                .Select(snapshot =>
                {
                    string pathKey = NormalizePath(snapshot.FullPath);
                    return new ObservedFile(
                        snapshot.FullPath,
                        snapshot.Filename,
                        snapshot.Extension,
                        snapshot.Fingerprint,
                        firstObserved.GetValueOrDefault(pathKey, observedAtUtc));
                })
                .ToArray();
            IReadOnlyList<CandidateGroup> groups =
                groupingService.Group(observedFiles, observedAtUtc);
            ScanPersistenceResult persisted = await store.ReconcileAsync(
                new ScanReconciliation(
                    validation.Folders.InboxDirectory,
                    observedAtUtc,
                    groups,
                    observedPaths),
                cancellationToken);

            if (persisted.HasWaitingCandidates)
            {
                activity.Publish(InboxActivityKind.CandidateWaiting);
            }

            activity.Publish(InboxActivityKind.ScanCompleted);
            return Result(
                ScanOutcome.Completed,
                snapshots.Count,
                persisted.CreatedItems,
                persisted.UpdatedItems,
                $"扫描完成：发现 {snapshots.Count} 个文件，新增 {persisted.CreatedItems} 项。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(
                error,
                "InboxScanFailed Trigger={Trigger}",
                trigger);
            activity.Publish(InboxActivityKind.ScanCompleted);
            return Result(
                ScanOutcome.Failed,
                message: "扫描失败，请查看日志。");
        }
    }

    private ScanResult Result(
        ScanOutcome outcome,
        int observedFiles = 0,
        int createdItems = 0,
        int updatedItems = 0,
        string message = "") =>
        new(
            outcome,
            observedFiles,
            createdItems,
            updatedItems,
            timeProvider.GetUtcNow(),
            message);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).ToUpperInvariant();
}
