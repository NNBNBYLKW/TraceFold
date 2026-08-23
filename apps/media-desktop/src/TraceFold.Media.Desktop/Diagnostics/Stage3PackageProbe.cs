using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Domain.Processing;
using TraceFold.Media.Infrastructure;
using TraceFold.Media.Infrastructure.MediaTools;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed record Stage3PackageProbeResult(
    bool Succeeded,
    Guid? JobId,
    string? ActualFilename,
    bool HasVideo,
    bool HasAudio,
    string? ToolPath,
    string? ErrorCode);

public static class Stage3PackageProbe
{
    private const string ProbeFilename = "paired.mp4";
    private const string ProbeAudioFilename = "paired.m4a";
    private const string ProbeDisplayName = "Stage 3 Probe";
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(
        Stage3PackageProbeOptions supplied,
        string? mediaToolRootOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        Stage3PackageProbeOptionResult validation =
            Stage3PackageProbeOptions.Parse(
            [
                Stage3PackageProbeOptions.RootArgument,
                supplied.Root,
                Stage3PackageProbeOptions.TokenArgument,
                supplied.TokenText
            ]);
        if (validation.State != Stage3PackageProbeOptionState.Ready
            || validation.Options is null)
        {
            return 2;
        }

        Stage3PackageProbeOptions options = validation.Options;
        string data = Path.Combine(options.Root, "data");
        string inbox = Path.Combine(options.Root, "inbox");
        string library = Path.Combine(options.Root, "library");
        string work = Path.Combine(options.Root, "work");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(work);
        ValidateOwnedProbeTree(options, data, inbox, library, work);

        string fixtures = Path.Combine(
            options.Root,
            Stage3PackageProbeOptions.FixturesDirectoryName);
        string videoSource = Path.Combine(inbox, ProbeFilename);
        string audioSource = Path.Combine(inbox, ProbeAudioFilename);
        File.Copy(Path.Combine(fixtures, ProbeFilename), videoSource);
        File.Copy(Path.Combine(fixtures, ProbeAudioFilename), audioSource);

        using IHost host = Host.CreateDefaultBuilder([])
            .ConfigureServices(services =>
            {
                services.AddMediaApplication();
                services.AddMediaInfrastructure(data, mediaToolRootOverride);
            })
            .Build();

        Guid? jobId = null;
        string? toolPath = null;
        try
        {
            await host.StartAsync(cancellationToken);
            await host.Services.GetRequiredService<IDatabaseInitializer>()
                .InitializeAsync(cancellationToken);
            DirectorySettingsResult saved = await host.Services
                .GetRequiredService<DirectorySettingsService>()
                .SaveAsync(
                    new LibraryFolders(inbox, library, work),
                    cancellationToken);
            Require(saved.Succeeded, "probe.directories_invalid");
            await host.Services.GetRequiredService<IStartupCoordinator>()
                .InitializeAsync(cancellationToken);

            MediaToolLocation? location = host.Services
                .GetRequiredService<BundledMediaToolLocator>()
                .TryResolve();
            Require(location is not null, "processing.tool_missing");
            toolPath = location!.FfmpegPath;

            IInboxScanner scanner = host.Services.GetRequiredService<IInboxScanner>();
            await scanner.ScanAsync(ScanTrigger.Manual, cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(2100), cancellationToken);
            await scanner.ScanAsync(ScanTrigger.StabilityCheck, cancellationToken);

            IInboxQueryService inboxQuery =
                host.Services.GetRequiredService<IInboxQueryService>();
            IReadOnlyList<InboxCardDto> cards = await inboxQuery.SearchAsync(
                new InboxQuery(ProbeFilename, null),
                cancellationToken);
            InboxCardDto card = cards.Single(candidate =>
                candidate.ActualFilename.Equals(
                    ProbeFilename,
                    StringComparison.OrdinalIgnoreCase));
            Require(
                card.State == InboxItemState.RequiresProcessing,
                "probe.source_not_processable");

            ProcessingCommandResult queued = await host.Services
                .GetRequiredService<IProcessingCommandService>()
                .StartAsync(card.Id, cancellationToken);
            Require(
                queued.Outcome == ProcessingCommandOutcome.Queued
                && queued.JobId.HasValue,
                queued.Message);
            jobId = queued.JobId;
            Guid expectedJobId = queued.JobId.GetValueOrDefault();

            InboxDetailsDto processed = await WaitForProcessingAsync(
                inboxQuery,
                card.Id,
                expectedJobId,
                cancellationToken);
            Require(
                processed.Card.State == InboxItemState.ReadyToTag,
                "probe.processed_item_not_ready");

            MetadataOwner owner = new(MetadataOwnerKind.InboxItem, card.Id);
            MetadataSaveResult metadata = await host.Services
                .GetRequiredService<IMetadataEditorService>()
                .SaveAsync(
                    owner,
                    new MetadataUpdate(ProbeDisplayName, [], null, [], 0),
                    cancellationToken);
            Require(metadata.Succeeded, "probe.metadata_failed");

            PreviewDescriptor preview = await host.Services
                .GetRequiredService<IMediaPreviewResolver>()
                .ResolveAsync(owner, cancellationToken);
            Require(preview.Exists, "probe.processed_preview_missing");
            MediaProbeResult outputProbe = await host.Services
                .GetRequiredService<IMediaTool>()
                .ProbeAsync(preview.TrustedFullPath, cancellationToken);
            Require(
                outputProbe.HasVideo && outputProbe.HasAudio,
                "probe.output_streams_invalid");

            IngestResult ingest = await host.Services
                .GetRequiredService<IIngestService>()
                .IngestAsync(card.Id, cancellationToken);
            Require(ingest.Outcome == IngestOutcome.Completed, ingest.Message);
            string formal = Path.Combine(library, ProbeFilename);
            Require(!File.Exists(videoSource), "probe.video_source_retained");
            Require(!File.Exists(audioSource), "probe.audio_source_retained");
            Require(File.Exists(formal), "probe.formal_missing");

            IReadOnlyList<LibraryAssetDto> assets = await host.Services
                .GetRequiredService<ILibraryQueryService>()
                .SearchAsync(null, cancellationToken);
            LibraryAssetDto asset = assets.Single();
            Require(
                asset.ActualFilename == ProbeFilename,
                "probe.filename_changed");
            Require(
                asset.DisplayName == ProbeDisplayName,
                "probe.display_name_missing");
            Require(
                ProcessingWorkspaceIsEmpty(work),
                "probe.processing_workspace_retained");

            await WriteResultAsync(
                options,
                [data, inbox, library, work],
                new Stage3PackageProbeResult(
                    true,
                    jobId,
                    asset.ActualFilename,
                    outputProbe.HasVideo,
                    outputProbe.HasAudio,
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
            await WriteResultAsync(
                options,
                [data, inbox, library, work],
                new Stage3PackageProbeResult(
                    false,
                    jobId,
                    null,
                    false,
                    false,
                    toolPath,
                    error.ErrorCode),
                CancellationToken.None);
            return 1;
        }
        catch
        {
            await WriteResultAsync(
                options,
                [data, inbox, library, work],
                new Stage3PackageProbeResult(
                    false,
                    jobId,
                    null,
                    false,
                    false,
                    toolPath,
                    "probe.unexpected_failure"),
                CancellationToken.None);
            return 1;
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(15));
            try
            {
                await host.StopAsync(stopTimeout.Token);
            }
            catch (OperationCanceledException) when (stopTimeout.IsCancellationRequested)
            {
            }
        }
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

    private static async Task WriteResultAsync(
        Stage3PackageProbeOptions options,
        IReadOnlyList<string> directories,
        Stage3PackageProbeResult result,
        CancellationToken cancellationToken)
    {
        ValidateOwnedProbeTree(options, [.. directories]);
        string resultPath = Path.Combine(
            options.Root,
            Stage3PackageProbeOptions.ResultFilename);
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
        Stage3PackageProbeOptions options,
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
            Stage3PackageProbeOptions.FixturesDirectoryName);
        if (!Stage3PackageProbeOptions.ValidateFixtures(fixtures))
        {
            throw new IOException("The package probe fixtures changed.");
        }

        string[] allowedEntries =
        [
            Stage3PackageProbeOptions.OwnerFilename,
            Stage3PackageProbeOptions.FixturesDirectoryName,
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
            Stage3PackageProbeOptions.ResultFilename);
        if (File.Exists(resultPath) || Directory.Exists(resultPath))
        {
            throw new IOException("The package probe result already exists.");
        }
    }

    private static void ValidateRootOwnership(Stage3PackageProbeOptions options)
    {
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(options.Root));
        string temp = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.GetTempPath()));
        string? parent = Path.GetDirectoryName(root);
        string ownerPath = Path.Combine(
            root,
            Stage3PackageProbeOptions.OwnerFilename);
        if (parent is null
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.TrimEndingDirectorySeparator(parent),
                temp)
            || !Path.GetFileName(root).StartsWith(
                Stage3PackageProbeOptions.RootNamePrefix,
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

    private static void Require(bool condition, string errorCode)
    {
        if (!condition)
        {
            throw new ProbeFailureException(errorCode);
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

    private sealed class ProbeFailureException(string errorCode) : Exception
    {
        public string ErrorCode { get; } = errorCode;
    }
}
