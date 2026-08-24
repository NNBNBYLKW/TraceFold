using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Processing;
using TraceFold.Media.Infrastructure;
using TraceFold.Media.Infrastructure.MediaTools;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed record Stage4PackageProbeResult(
    bool Succeeded,
    bool RegisteredReplacePassed,
    bool ContentKeepBothPassed,
    bool UnmanagedReplacePassed,
    bool ReopenPassed,
    bool ProcessedMediaPassed,
    int ImageAssetCount,
    int VideoAssetCount,
    int TotalAssetCount,
    string? ToolPath,
    string? ErrorCode);

public sealed record Stage4CrashProbeResult(
    bool Succeeded,
    bool CrashRecoveryPassed,
    string FaultPoint,
    Guid? AssetId,
    string? TargetSha256,
    string? ErrorCode);

public static class Stage4PackageProbe
{
    private const string PhotoFixture = "photo.jpg";
    private const string VideoFixture = "paired.mp4";
    private const string AudioFixture = "paired.m4a";
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(
        Stage4PackageProbeOptions supplied,
        string? mediaToolRootOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        Stage4PackageProbeOptionResult validation =
            Stage4PackageProbeOptions.Parse(supplied.ToArguments());
        if (validation.State != Stage4PackageProbeOptionState.Ready
            || validation.Options is null)
        {
            return 2;
        }

        Stage4PackageProbeOptions options = validation.Options;
        if (StringComparer.Ordinal.Equals(
                options.Mode,
                Stage4PackageProbeOptions.CrashSeedMode))
        {
            return await RunCrashSeedAsync(
                options,
                mediaToolRootOverride,
                cancellationToken);
        }

        if (StringComparer.Ordinal.Equals(
                options.Mode,
                Stage4PackageProbeOptions.CrashResumeMode))
        {
            return await RunCrashResumeAsync(
                options,
                mediaToolRootOverride,
                cancellationToken);
        }

        string data = Path.Combine(options.Root, "data");
        string inbox = Path.Combine(options.Root, "inbox");
        string library = Path.Combine(options.Root, "library");
        string work = Path.Combine(options.Root, "work");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(work);
        string[] ownedDirectories = [data, inbox, library, work];
        ValidateOwnedProbeTree(options, ownedDirectories);

        string fixtures = Path.Combine(
            options.Root,
            Stage4PackageProbeOptions.FixturesDirectoryName);
        var clock = new ProbeTimeProvider(
            DateTimeOffset.Parse("2026-08-24T12:00:00Z"));
        IHost? host = null;
        string? toolPath = null;
        bool registeredReplace = false;
        bool contentKeepBoth = false;
        bool unmanagedReplace = false;
        bool reopened = false;
        bool processedMedia = false;
        try
        {
            host = BuildHost(data, mediaToolRootOverride, clock);
            await StartHostAsync(
                host,
                new LibraryFolders(inbox, library, work),
                saveFolders: true,
                cancellationToken);
            MediaToolLocation? location = host.Services
                .GetRequiredService<BundledMediaToolLocator>()
                .TryResolve();
            Require(location is not null, "processing.tool_missing");
            toolPath = location!.FfmpegPath;

            Guid registeredAssetId = await SeedAssetAsync(
                host,
                fixtures,
                PhotoFixture,
                inbox,
                "registered.jpg",
                clock,
                cancellationToken);
            InboxCardDto replaceCard = await CopyAndObserveAsync(
                host,
                fixtures,
                PhotoFixture,
                inbox,
                "registered.jpg",
                clock,
                cancellationToken);
            IngestResult registeredConflict = await host.Services
                .GetRequiredService<IIngestService>()
                .IngestAsync(replaceCard.Id, cancellationToken);
            IngestResult registeredResult = await ResolveAsync(
                host,
                registeredConflict,
                ConflictResolution.Replace,
                match => match.AssetId == registeredAssetId,
                cancellationToken);
            Require(
                registeredResult.Outcome == IngestOutcome.Completed
                && registeredResult.AssetId == registeredAssetId
                && !File.Exists(Path.Combine(inbox, "registered.jpg")),
                "probe.registered_replace_failed");
            registeredReplace = true;

            InboxCardDto keepBothCard = await CopyAndObserveAsync(
                host,
                fixtures,
                PhotoFixture,
                inbox,
                "registered.jpg",
                clock,
                cancellationToken);
            IngestResult keepBothConflict = await host.Services
                .GetRequiredService<IIngestService>()
                .IngestAsync(keepBothCard.Id, cancellationToken);
            IngestResult keepBothResult = await ResolveAsync(
                host,
                keepBothConflict,
                ConflictResolution.KeepBoth,
                matchSelector: null,
                cancellationToken);
            Require(
                keepBothResult.Outcome == IngestOutcome.Completed
                && File.Exists(Path.Combine(library, "registered (2).jpg"))
                && await CountAssetsWithFilenameAsync(
                    host,
                    "registered.jpg",
                    "registered (2).jpg",
                    cancellationToken) == 2,
                "probe.keep_both_failed");
            contentKeepBoth = true;

            File.Copy(
                Path.Combine(fixtures, PhotoFixture),
                Path.Combine(library, "unmanaged.jpg"));
            InboxCardDto unmanagedCard = await CopyAndObserveAsync(
                host,
                fixtures,
                PhotoFixture,
                inbox,
                "unmanaged.jpg",
                clock,
                cancellationToken);
            IngestResult unmanagedConflict = await host.Services
                .GetRequiredService<IIngestService>()
                .IngestAsync(unmanagedCard.Id, cancellationToken);
            IngestResult unmanagedResult = await ResolveAsync(
                host,
                unmanagedConflict,
                ConflictResolution.Replace,
                match => !match.IsRegistered
                    && match.ActualFilename.Equals(
                        "unmanaged.jpg",
                        StringComparison.OrdinalIgnoreCase),
                cancellationToken);
            Require(
                unmanagedResult.Outcome == IngestOutcome.Completed
                && unmanagedResult.AssetId.HasValue
                && !File.Exists(Path.Combine(inbox, "unmanaged.jpg"))
                && await CountAssetsWithFilenameAsync(
                    host,
                    "unmanaged.jpg",
                    null,
                    cancellationToken) == 1,
                "probe.unmanaged_replace_failed");
            unmanagedReplace = true;

            InboxCardDto reopenCard = await CopyAndObserveAsync(
                host,
                fixtures,
                PhotoFixture,
                inbox,
                "reopen.jpg",
                clock,
                cancellationToken);
            IngestResult reopenConflict = await host.Services
                .GetRequiredService<IIngestService>()
                .IngestAsync(reopenCard.Id, cancellationToken);
            Require(
                reopenConflict.Outcome == IngestOutcome.ConflictBlocked
                && reopenConflict.ConflictCaseId.HasValue,
                "probe.reopen_case_missing");
            Guid reopenCaseId = reopenConflict.ConflictCaseId.GetValueOrDefault();

            await StopHostAsync(host);
            host.Dispose();
            host = BuildHost(data, mediaToolRootOverride, clock);
            await StartHostAsync(
                host,
                new LibraryFolders(inbox, library, work),
                saveFolders: false,
                cancellationToken);
            ConflictCaseDto? persisted = await host.Services
                .GetRequiredService<IConflictQueryService>()
                .GetOpenAsync(reopenCard.Id, cancellationToken);
            Require(
                persisted?.Id == reopenCaseId,
                "probe.reopen_case_not_persisted");
            IngestResult skipped = await host.Services
                .GetRequiredService<IConflictResolutionService>()
                .ResolveAsync(
                    reopenCaseId,
                    ConflictResolution.Skip,
                    null,
                    cancellationToken);
            Require(
                skipped.Outcome == IngestOutcome.Skipped
                && File.Exists(Path.Combine(inbox, "reopen.jpg")),
                "probe.reopen_skip_failed");
            reopened = true;

            File.Copy(
                Path.Combine(fixtures, VideoFixture),
                Path.Combine(inbox, VideoFixture));
            File.Copy(
                Path.Combine(fixtures, AudioFixture),
                Path.Combine(inbox, AudioFixture));
            await ObserveAsync(host, clock, cancellationToken);
            InboxCardDto processedCard = await FindCardAsync(
                host,
                VideoFixture,
                cancellationToken);
            Require(
                processedCard.State == InboxItemState.RequiresProcessing,
                "probe.source_not_processable");
            ProcessingCommandResult queued = await host.Services
                .GetRequiredService<IProcessingCommandService>()
                .StartAsync(processedCard.Id, cancellationToken);
            Require(
                queued.Outcome == ProcessingCommandOutcome.Queued
                && queued.JobId.HasValue,
                "probe.processing_not_queued");
            InboxDetailsDto processed = await WaitForProcessingAsync(
                host.Services.GetRequiredService<IInboxQueryService>(),
                processedCard.Id,
                queued.JobId.GetValueOrDefault(),
                cancellationToken);
            Require(
                processed.Card.State == InboxItemState.ReadyToTag,
                "probe.processed_item_not_ready");
            IngestResult processedResult = await host.Services
                .GetRequiredService<IIngestService>()
                .IngestAsync(processedCard.Id, cancellationToken);
            Require(
                processedResult.Outcome == IngestOutcome.Completed
                && !File.Exists(Path.Combine(inbox, VideoFixture))
                && !File.Exists(Path.Combine(inbox, AudioFixture))
                && File.Exists(Path.Combine(library, VideoFixture))
                && ProcessingWorkspaceIsEmpty(work),
                "probe.processed_ingest_failed");
            processedMedia = true;

            IReadOnlyList<LibraryAssetDto> assets = await host.Services
                .GetRequiredService<ILibraryQueryService>()
                .SearchAsync(null, cancellationToken);
            int imageAssets = assets.Count(asset => asset.MediaKind == MediaKind.Image);
            int videoAssets = assets.Count(asset => asset.MediaKind == MediaKind.Video);
            Require(
                imageAssets == 3 && videoAssets == 1 && assets.Count == 4,
                "probe.asset_counts_invalid");
            Require(
                InternalStorageIsClean(library),
                "probe.internal_storage_retained");

            await WriteResultAsync(
                options,
                ownedDirectories,
                new Stage4PackageProbeResult(
                    true,
                    registeredReplace,
                    contentKeepBoth,
                    unmanagedReplace,
                    reopened,
                    processedMedia,
                    imageAssets,
                    videoAssets,
                    assets.Count,
                    toolPath,
                    null),
                cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProbeFailureException error)
        {
            await WriteFailureAsync(
                options,
                ownedDirectories,
                registeredReplace,
                contentKeepBoth,
                unmanagedReplace,
                reopened,
                processedMedia,
                toolPath,
                error.ErrorCode);
            return 1;
        }
        catch
        {
            await WriteFailureAsync(
                options,
                ownedDirectories,
                registeredReplace,
                contentKeepBoth,
                unmanagedReplace,
                reopened,
                processedMedia,
                toolPath,
                "probe.unexpected_failure");
            return 1;
        }
        finally
        {
            if (host is not null)
            {
                await StopHostAsync(host);
                host.Dispose();
            }
        }
    }

    private static async Task<int> RunCrashSeedAsync(
        Stage4PackageProbeOptions options,
        string? mediaToolRootOverride,
        CancellationToken cancellationToken)
    {
        IngestFaultPoint faultPoint = options.FaultPoint
            ?? throw new InvalidOperationException(
                "A diagnostic crash seed requires a fault point.");
        string data = Path.Combine(options.Root, "data");
        string inbox = Path.Combine(options.Root, "inbox");
        string library = Path.Combine(options.Root, "library");
        string work = Path.Combine(options.Root, "work");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(work);
        string[] ownedDirectories = [data, inbox, library, work];
        ValidateOwnedProbeTree(options, ownedDirectories);

        string fixtures = Path.Combine(
            options.Root,
            Stage4PackageProbeOptions.FixturesDirectoryName);
        string manifestPath = Path.Combine(
            options.Root,
            Stage4PackageProbeOptions.CrashManifestFilename);
        var clock = new ProbeTimeProvider(
            DateTimeOffset.Parse("2026-08-24T13:00:00Z"));
        IHost? host = null;
        try
        {
            host = BuildHost(data, mediaToolRootOverride, clock);
            await StartHostAsync(
                host,
                new LibraryFolders(inbox, library, work),
                saveFolders: true,
                cancellationToken);
            Guid assetId = await SeedAssetAsync(
                host,
                fixtures,
                PhotoFixture,
                inbox,
                "registered.jpg",
                clock,
                cancellationToken);
            string target = Path.Combine(library, "registered.jpg");
            string originalHash = await FileSha256Async(
                target,
                cancellationToken);

            await StopHostAsync(host);
            host.Dispose();
            host = null;

            string source = Path.Combine(inbox, "registered.jpg");
            File.Copy(Path.Combine(fixtures, PhotoFixture), source);
            await File.AppendAllTextAsync(
                source,
                options.TokenText,
                cancellationToken);
            string incomingHash = await FileSha256Async(
                source,
                cancellationToken);
            Stage4CrashProbeManifest manifest =
                Stage4CrashProbeManifest.Create(
                    options.TokenText,
                    faultPoint,
                    assetId,
                    "registered.jpg",
                    originalHash,
                    incomingHash);
            manifest.WriteTo(manifestPath);

            var diagnostic = new DiagnosticIngestFaultInjector(
                faultPoint,
                manifest,
                manifestPath);
            host = BuildHost(
                data,
                mediaToolRootOverride,
                clock,
                diagnostic);
            await StartHostAsync(
                host,
                new LibraryFolders(inbox, library, work),
                saveFolders: false,
                cancellationToken);
            await ObserveAsync(host, clock, cancellationToken);
            InboxCardDto card = await FindCardAsync(
                host,
                "registered.jpg",
                cancellationToken);
            IngestResult blocked = await host.Services
                .GetRequiredService<IIngestService>()
                .IngestAsync(card.Id, cancellationToken);
            _ = await ResolveAsync(
                host,
                blocked,
                ConflictResolution.Replace,
                match => match.AssetId == assetId,
                cancellationToken);

            await WriteCrashResultAsync(
                options,
                new Stage4CrashProbeResult(
                    false,
                    false,
                    faultPoint.ToString(),
                    assetId,
                    null,
                    "probe.diagnostic_crash_not_reached"),
                cancellationToken);
            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return 1;
        }
        finally
        {
            if (host is not null)
            {
                await StopHostAsync(host);
                host.Dispose();
            }
        }
    }

    private static async Task<int> RunCrashResumeAsync(
        Stage4PackageProbeOptions options,
        string? mediaToolRootOverride,
        CancellationToken cancellationToken)
    {
        Stage4CrashProbeManifest manifest = options.CrashManifest
            ?? throw new InvalidOperationException(
                "A diagnostic crash resume requires a persisted manifest.");
        string data = Path.Combine(options.Root, "data");
        string inbox = Path.Combine(options.Root, "inbox");
        string library = Path.Combine(options.Root, "library");
        string work = Path.Combine(options.Root, "work");
        var clock = new ProbeTimeProvider(
            DateTimeOffset.Parse("2026-08-24T13:30:00Z"));
        IHost? host = null;
        try
        {
            host = BuildHost(data, mediaToolRootOverride, clock);
            await StartHostAsync(
                host,
                new LibraryFolders(inbox, library, work),
                saveFolders: false,
                cancellationToken);

            ILibraryQueryService libraryQuery = host.Services
                .GetRequiredService<ILibraryQueryService>();
            IReadOnlyList<LibraryAssetDto> assets = await libraryQuery
                .SearchAsync(null, cancellationToken);
            LibraryAssetDto asset = assets.Single(candidate =>
                candidate.Id == manifest.AssetId
                && candidate.ActualFilename.Equals(
                    manifest.TargetFilename,
                    StringComparison.OrdinalIgnoreCase));
            LibraryAssetDetailsDto details = (await libraryQuery.GetDetailsAsync(
                asset.Id,
                cancellationToken))!;
            string target = Path.Combine(library, manifest.TargetFilename);
            string targetHash = await FileSha256Async(target, cancellationToken);
            IReadOnlyList<IngestOperationRecord> incomplete = await host.Services
                .GetRequiredService<IIngestStore>()
                .LoadIncompleteAsync(cancellationToken);
            string[] formalFiles = Directory.GetFiles(
                    library,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray();

            Require(
                assets.Count == 1
                && details.Card.Id == manifest.AssetId
                && StringComparer.Ordinal.Equals(
                    details.Sha256,
                    manifest.IncomingSha256)
                && StringComparer.Ordinal.Equals(
                    targetHash,
                    manifest.IncomingSha256)
                && formalFiles.SequenceEqual(
                    [manifest.TargetFilename],
                    StringComparer.OrdinalIgnoreCase)
                && !File.Exists(Path.Combine(inbox, manifest.TargetFilename))
                && incomplete.All(operation =>
                    operation.Id != manifest.OperationId)
                && InternalStorageIsClean(library),
                "probe.crash_recovery_invalid");

            await WriteCrashResultAsync(
                options,
                new Stage4CrashProbeResult(
                    true,
                    true,
                    manifest.FaultPoint.ToString(),
                    asset.Id,
                    targetHash,
                    null),
                cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProbeFailureException error)
        {
            await TryWriteCrashFailureAsync(options, manifest, error.ErrorCode);
            return 1;
        }
        catch
        {
            await TryWriteCrashFailureAsync(
                options,
                manifest,
                "probe.crash_resume_unexpected_failure");
            return 1;
        }
        finally
        {
            if (host is not null)
            {
                await StopHostAsync(host);
                host.Dispose();
            }
        }
    }

    private static async Task TryWriteCrashFailureAsync(
        Stage4PackageProbeOptions options,
        Stage4CrashProbeManifest manifest,
        string errorCode)
    {
        try
        {
            await WriteCrashResultAsync(
                options,
                new Stage4CrashProbeResult(
                    false,
                    false,
                    manifest.FaultPoint.ToString(),
                    manifest.AssetId,
                    null,
                    errorCode),
                CancellationToken.None);
        }
        catch
        {
        }
    }

    private static async Task WriteCrashResultAsync(
        Stage4PackageProbeOptions options,
        Stage4CrashProbeResult result,
        CancellationToken cancellationToken)
    {
        ValidateRootOwnership(options);
        string manifestPath = Path.Combine(
            options.Root,
            Stage4PackageProbeOptions.CrashManifestFilename);
        if (Stage4CrashProbeManifest.ReadValidated(
                manifestPath,
                options.TokenText) is null)
        {
            throw new IOException("The diagnostic crash manifest changed.");
        }

        string resultPath = Path.Combine(
            options.Root,
            Stage4PackageProbeOptions.ResultFilename);
        await using var output = new FileStream(
            resultPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            });
        await JsonSerializer.SerializeAsync(
            output,
            result,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static async Task<string> FileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream input = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(input, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static IHost BuildHost(
        string dataRoot,
        string? mediaToolRootOverride,
        ProbeTimeProvider clock,
        IIngestFaultInjector? faultInjector = null) =>
        Host.CreateDefaultBuilder([])
            .ConfigureServices(services =>
            {
                services.AddMediaApplication();
                services.AddMediaInfrastructure(dataRoot, mediaToolRootOverride);
                services.AddSingleton<TimeProvider>(clock);
                if (faultInjector is not null)
                {
                    services.AddSingleton(faultInjector);
                    services.AddSingleton<IIngestFaultInjector>(faultInjector);
                }
            })
            .Build();

    private static async Task StartHostAsync(
        IHost host,
        LibraryFolders folders,
        bool saveFolders,
        CancellationToken cancellationToken)
    {
        await host.StartAsync(cancellationToken);
        await host.Services.GetRequiredService<IDatabaseInitializer>()
            .InitializeAsync(cancellationToken);
        if (saveFolders)
        {
            DirectorySettingsResult saved = await host.Services
                .GetRequiredService<DirectorySettingsService>()
                .SaveAsync(folders, cancellationToken);
            Require(saved.Succeeded, "probe.directories_invalid");
        }

        await host.Services.GetRequiredService<IStartupCoordinator>()
            .InitializeAsync(cancellationToken);
    }

    private static async Task StopHostAsync(IHost host)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await host.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
    }

    private static async Task<Guid> SeedAssetAsync(
        IHost host,
        string fixtures,
        string fixtureFilename,
        string inbox,
        string targetFilename,
        ProbeTimeProvider clock,
        CancellationToken cancellationToken)
    {
        InboxCardDto card = await CopyAndObserveAsync(
            host,
            fixtures,
            fixtureFilename,
            inbox,
            targetFilename,
            clock,
            cancellationToken);
        IngestResult result = await host.Services
            .GetRequiredService<IIngestService>()
            .IngestAsync(card.Id, cancellationToken);
        Require(
            result.Outcome == IngestOutcome.Completed && result.AssetId.HasValue,
            "probe.seed_asset_failed");
        return result.AssetId.GetValueOrDefault();
    }

    private static async Task<InboxCardDto> CopyAndObserveAsync(
        IHost host,
        string fixtures,
        string fixtureFilename,
        string inbox,
        string targetFilename,
        ProbeTimeProvider clock,
        CancellationToken cancellationToken)
    {
        File.Copy(
            Path.Combine(fixtures, fixtureFilename),
            Path.Combine(inbox, targetFilename),
            overwrite: false);
        await ObserveAsync(host, clock, cancellationToken);
        return await FindCardAsync(host, targetFilename, cancellationToken);
    }

    private static async Task ObserveAsync(
        IHost host,
        ProbeTimeProvider clock,
        CancellationToken cancellationToken)
    {
        IInboxScanner scanner = host.Services.GetRequiredService<IInboxScanner>();
        await scanner.ScanAsync(ScanTrigger.Manual, cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(11));
        await scanner.ScanAsync(ScanTrigger.StabilityCheck, cancellationToken);
    }

    private static async Task<InboxCardDto> FindCardAsync(
        IHost host,
        string filename,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<InboxCardDto> cards = await host.Services
            .GetRequiredService<IInboxQueryService>()
            .SearchAsync(new InboxQuery(filename, null), cancellationToken);
        return cards.Single(card => card.ActualFilename.Equals(
            filename,
            StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<IngestResult> ResolveAsync(
        IHost host,
        IngestResult blocked,
        ConflictResolution resolution,
        Func<ConflictMatchRecord, bool>? matchSelector,
        CancellationToken cancellationToken)
    {
        Require(
            blocked.Outcome == IngestOutcome.ConflictBlocked
            && blocked.ConflictCaseId.HasValue,
            "probe.conflict_missing");
        IConflictCaseStore store = host.Services
            .GetRequiredService<IConflictCaseStore>();
        ConflictCaseRecord conflict = (await store.LoadAsync(
            blocked.ConflictCaseId!.Value,
            cancellationToken))!;
        Guid? selectedId = matchSelector is null
            ? null
            : conflict.Matches.Single(matchSelector).Id;
        return await host.Services
            .GetRequiredService<IConflictResolutionService>()
            .ResolveAsync(
                conflict.Id,
                resolution,
                selectedId,
                cancellationToken);
    }

    private static async Task<int> CountAssetsWithFilenameAsync(
        IHost host,
        string first,
        string? second,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LibraryAssetDto> assets = await host.Services
            .GetRequiredService<ILibraryQueryService>()
            .SearchAsync(null, cancellationToken);
        return assets.Count(asset =>
            asset.ActualFilename.Equals(first, StringComparison.OrdinalIgnoreCase)
            || second is not null
                && asset.ActualFilename.Equals(
                    second,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<InboxDetailsDto> WaitForProcessingAsync(
        IInboxQueryService query,
        Guid inboxItemId,
        Guid expectedJobId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + ProcessingTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            InboxDetailsDto? details = await query.GetDetailsAsync(
                inboxItemId,
                cancellationToken);
            ProcessingSummaryDto? processing = details?.Processing;
            if (processing?.JobId == expectedJobId)
            {
                if (processing.Status == ProcessingJobStatus.Succeeded)
                {
                    return details!;
                }

                if (processing.Status is ProcessingJobStatus.Failed
                    or ProcessingJobStatus.Interrupted
                    or ProcessingJobStatus.Cancelled)
                {
                    throw new ProbeFailureException(
                        processing.ErrorCode ?? "probe.processing_failed");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new ProbeFailureException("probe.processing_timeout");
    }

    private static async Task WriteFailureAsync(
        Stage4PackageProbeOptions options,
        IReadOnlyList<string> directories,
        bool registeredReplace,
        bool contentKeepBoth,
        bool unmanagedReplace,
        bool reopened,
        bool processedMedia,
        string? toolPath,
        string errorCode) =>
        await WriteResultAsync(
            options,
            directories,
            new Stage4PackageProbeResult(
                false,
                registeredReplace,
                contentKeepBoth,
                unmanagedReplace,
                reopened,
                processedMedia,
                0,
                0,
                0,
                toolPath,
                errorCode),
            CancellationToken.None);

    private static async Task WriteResultAsync(
        Stage4PackageProbeOptions options,
        IReadOnlyList<string> directories,
        Stage4PackageProbeResult result,
        CancellationToken cancellationToken)
    {
        ValidateOwnedProbeTree(options, [.. directories]);
        string resultPath = Path.Combine(
            options.Root,
            Stage4PackageProbeOptions.ResultFilename);
        await using var output = new FileStream(
            resultPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            });
        await JsonSerializer.SerializeAsync(
            output,
            result,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static void ValidateOwnedProbeTree(
        Stage4PackageProbeOptions options,
        params string[] directories)
    {
        ValidateRootOwnership(options);
        foreach (string directory in directories)
        {
            string fullPath = Path.GetFullPath(directory);
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetDirectoryName(fullPath),
                    options.Root)
                || !Directory.Exists(fullPath)
                || File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("The package probe directory is not safe.");
            }
        }

        string fixtures = Path.Combine(
            options.Root,
            Stage4PackageProbeOptions.FixturesDirectoryName);
        if (!Stage4PackageProbeOptions.ValidateFixtures(fixtures))
        {
            throw new IOException("The package probe fixtures changed.");
        }

        string[] allowedEntries =
        [
            Stage4PackageProbeOptions.OwnerFilename,
            Stage4PackageProbeOptions.FixturesDirectoryName,
            .. directories.Select(Path.GetFileName)
                .Where(name => name is not null)
                .Cast<string>()
        ];
        string[] actualEntries = Directory.GetFileSystemEntries(options.Root)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        if (actualEntries.Length != allowedEntries.Length
            || actualEntries.Except(
                allowedEntries,
                StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new IOException("The package probe root contains an extra entry.");
        }

        string resultPath = Path.Combine(
            options.Root,
            Stage4PackageProbeOptions.ResultFilename);
        if (File.Exists(resultPath) || Directory.Exists(resultPath))
        {
            throw new IOException("The package probe result already exists.");
        }
    }

    private static void ValidateRootOwnership(Stage4PackageProbeOptions options)
    {
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(options.Root));
        string temp = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.GetTempPath()));
        string? parent = Path.GetDirectoryName(root);
        string ownerPath = Path.Combine(
            root,
            Stage4PackageProbeOptions.OwnerFilename);
        if (parent is null
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.TrimEndingDirectorySeparator(parent),
                temp)
            || !Path.GetFileName(root).StartsWith(
                Stage4PackageProbeOptions.RootNamePrefix,
                StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(root)
            || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)
            || !File.Exists(ownerPath)
            || File.GetAttributes(ownerPath).HasFlag(FileAttributes.ReparsePoint)
            || !StringComparer.Ordinal.Equals(
                File.ReadAllText(ownerPath),
                options.TokenText))
        {
            throw new IOException("The package probe root ownership changed.");
        }
    }

    private static bool ProcessingWorkspaceIsEmpty(string workDirectory)
    {
        string processing = Path.Combine(
            workDirectory,
            ".tracefold",
            "processing");
        return !Directory.Exists(processing)
            || !Directory.EnumerateFileSystemEntries(processing).Any();
    }

    private static bool InternalStorageIsClean(string libraryDirectory)
    {
        foreach (string category in new[] { "commit", "recovery" })
        {
            string path = Path.Combine(
                libraryDirectory,
                ".tracefold",
                category);
            if (Directory.Exists(path)
                && Directory.EnumerateFileSystemEntries(path).Any())
            {
                return false;
            }
        }

        return true;
    }

    private static void Require(bool condition, string errorCode)
    {
        if (!condition)
        {
            throw new ProbeFailureException(errorCode);
        }
    }

    private sealed class ProbeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset now = utcNow;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan value) => now += value;
    }

    private sealed class ProbeFailureException(string errorCode) : Exception
    {
        public string ErrorCode { get; } = errorCode;
    }
}
