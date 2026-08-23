using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed record Stage2PackageProbeResult(
    bool Succeeded,
    string? SourceRelativePath,
    string? LibraryRelativePath,
    string? ActualFilename,
    string? DisplayName,
    string? ErrorCode);

public static class Stage2PackageProbe
{
    private const string ProbeFilename = "probe.png";
    private const string ProbeDisplayName = "Stage 2 Probe";
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6lGQAAAAASUVORK5CYII=");

    public static async Task<int> RunAsync(
        Stage2PackageProbeOptions supplied,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        Stage2PackageProbeOptionResult validation =
            Stage2PackageProbeOptions.Parse(
            [
                Stage2PackageProbeOptions.RootArgument,
                supplied.Root,
                Stage2PackageProbeOptions.TokenArgument,
                supplied.TokenText
            ]);
        if (validation.State != Stage2PackageProbeOptionState.Ready
            || validation.Options is null)
        {
            return 2;
        }

        Stage2PackageProbeOptions options = validation.Options;
        string data = Path.Combine(options.Root, "data");
        string inbox = Path.Combine(options.Root, "inbox");
        string library = Path.Combine(options.Root, "library");
        string work = Path.Combine(options.Root, "work");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(work);
        ValidateOwnedProbeTree(options, data, inbox, library, work);

        using IHost host = Host.CreateDefaultBuilder([])
            .ConfigureServices(services =>
            {
                services.AddMediaApplication();
                services.AddMediaInfrastructure(data);
            })
            .Build();

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

            string source = Path.Combine(inbox, ProbeFilename);
            await File.WriteAllBytesAsync(source, OnePixelPng, cancellationToken);
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
                card.State == InboxItemState.ReadyToTag,
                "probe.source_not_ready");

            MetadataSaveResult metadata = await host.Services
                .GetRequiredService<IMetadataEditorService>()
                .SaveAsync(
                    new MetadataOwner(MetadataOwnerKind.InboxItem, card.Id),
                    new MetadataUpdate(ProbeDisplayName, [], null, [], 0),
                    cancellationToken);
            Require(metadata.Succeeded, "probe.metadata_failed");

            IngestResult ingest = await host.Services
                .GetRequiredService<IIngestService>()
                .IngestAsync(card.Id, cancellationToken);
            Require(
                ingest.Outcome == IngestOutcome.Completed,
                ingest.Message);
            string formal = Path.Combine(library, ProbeFilename);
            Require(!File.Exists(source), "probe.source_retained");
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

            await WriteResultAsync(
                options,
                [data, inbox, library, work],
                new Stage2PackageProbeResult(
                    true,
                    "inbox/probe.png",
                    "library/probe.png",
                    asset.ActualFilename,
                    asset.DisplayName,
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
                new Stage2PackageProbeResult(
                    false,
                    null,
                    null,
                    null,
                    null,
                    error.ErrorCode),
                CancellationToken.None);
            return 1;
        }
        catch
        {
            await WriteResultAsync(
                options,
                [data, inbox, library, work],
                new Stage2PackageProbeResult(
                    false,
                    null,
                    null,
                    null,
                    null,
                    "probe.unexpected_failure"),
                CancellationToken.None);
            return 1;
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            try
            {
                await host.StopAsync(stopTimeout.Token);
            }
            catch (OperationCanceledException) when (stopTimeout.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task WriteResultAsync(
        Stage2PackageProbeOptions options,
        IReadOnlyList<string> directories,
        Stage2PackageProbeResult result,
        CancellationToken cancellationToken)
    {
        ValidateOwnedProbeTree(options, [.. directories]);
        string resultPath = Path.Combine(
            options.Root,
            Stage2PackageProbeOptions.ResultFilename);
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
        Stage2PackageProbeOptions options,
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

        string resultPath = Path.Combine(
            options.Root,
            Stage2PackageProbeOptions.ResultFilename);
        if (File.Exists(resultPath) || Directory.Exists(resultPath))
        {
            throw new IOException("The package probe result already exists.");
        }
    }

    private static void ValidateRootOwnership(Stage2PackageProbeOptions options)
    {
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(options.Root));
        string temp = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.GetTempPath()));
        string? parent = Path.GetDirectoryName(root);
        string ownerPath = Path.Combine(root, "probe-owner.txt");
        if (parent is null
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.TrimEndingDirectorySeparator(parent),
                temp)
            || !Path.GetFileName(root).StartsWith(
                Stage2PackageProbeOptions.RootNamePrefix,
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

    private sealed class ProbeFailureException(string errorCode) : Exception
    {
        public string ErrorCode { get; } = errorCode;
    }
}
