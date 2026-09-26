using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed class PackageProbeEnvironment
{
    private static readonly IReadOnlyDictionary<string, string> FixtureHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["paired.mp4"] = "B5B85F2CA95EA5EB781B42D202D1C3DB361BF37F5BF4B3107A79469FC716A1A3",
            ["paired.m4a"] = "187F3BE0AB2C07C6A967840BEBFA0EB1646227B52179387F74AB4D8BBC64AAC1",
            ["photo.jpg"] = "7D1BD6791F1D40FFAFE6C7704FB2E71B6FBF6CBD26B71F9A45E62460D61312A6"
        };
    private static readonly string[] RuntimeDirectories = ["data", "inbox", "library", "work"];
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(30);

    private PackageProbeEnvironment(PackageProbeRequest request, string? mediaToolRootOverride)
    {
        Request = request;
        MediaToolRootOverride = mediaToolRootOverride;
        DataDirectory = Path.Combine(request.Root, "data");
        InboxDirectory = Path.Combine(request.Root, "inbox");
        LibraryDirectory = Path.Combine(request.Root, "library");
        WorkDirectory = Path.Combine(request.Root, "work");
        FixturesDirectory = Path.Combine(
            request.Root,
            Stage4PackageProbeOptions.FixturesDirectoryName);
    }

    public PackageProbeRequest Request { get; }

    public string ResultFilename => ResultFilenameFor(Request.Scenario);

    public string? MediaToolRootOverride { get; }

    public string DataDirectory { get; }

    public string InboxDirectory { get; }

    public string LibraryDirectory { get; }

    public string WorkDirectory { get; }

    public string FixturesDirectory { get; }

    public IReadOnlyList<string> RuntimeDirectoryPaths =>
        [DataDirectory, InboxDirectory, LibraryDirectory, WorkDirectory];

    public static PackageProbeEnvironment Create(
        PackageProbeRequest request,
        string? mediaToolRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        string root = ValidateRootOwnership(
            request.Root, request.OwnerToken, request.Scenario);
        if (!ValidateFixtures(Path.Combine(root, Stage4PackageProbeOptions.FixturesDirectoryName)))
        {
            throw new IOException("The package probe fixtures changed.");
        }

        return new(request with { Root = root }, mediaToolRootOverride);
    }

    public void CreateRuntimeDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(InboxDirectory);
        Directory.CreateDirectory(LibraryDirectory);
        Directory.CreateDirectory(WorkDirectory);
    }

    public IHost BuildHost(ProbeTimeProvider clock, IIngestFaultInjector? faultInjector = null) =>
        Host.CreateDefaultBuilder([])
            .ConfigureLogging(logging => logging.AddProvider(new PackageProbeLogProvider()))
            .ConfigureServices(services =>
            {
                services.AddMediaApplication();
                services.AddMediaInfrastructure(DataDirectory, MediaToolRootOverride);
                services.AddSingleton<TimeProvider>(clock);
                if (faultInjector is not null)
                {
                    services.AddSingleton(faultInjector);
                    services.AddSingleton<IIngestFaultInjector>(faultInjector);
                }
            })
            .Build();

    public async Task StartHostAsync(
        IHost host,
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
                .SaveAsync(
                    new LibraryFolders(InboxDirectory, LibraryDirectory, WorkDirectory),
                    cancellationToken);
            Require(saved.Succeeded, "probe.directories_invalid");
        }

        await host.Services.GetRequiredService<IStartupCoordinator>()
            .InitializeAsync(cancellationToken);
    }

    public static async Task StopHostAsync(IHost host)
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

    public async Task<Guid> SeedAssetAsync(
        IHost host,
        string fixtureFilename,
        string targetFilename,
        ProbeTimeProvider clock,
        CancellationToken cancellationToken)
    {
        InboxCardDto card = await CopyAndObserveAsync(
            host, fixtureFilename, targetFilename, clock, cancellationToken);
        IngestResult result = await host.Services.GetRequiredService<IIngestService>()
            .IngestAsync(card.Id, cancellationToken);
        if (result.Outcome != IngestOutcome.Completed || !result.AssetId.HasValue)
        {
            // The persisted result keeps the stable code; the state that explains it
            // goes to the probe trace, which only tests consume.
            PackageProbeTrace.Write("probe.seed_asset_failed; outcome=" + result.Outcome
                + "; message=" + result.Message
                + "; card=" + card.State);
            throw new ProbeFailureException("probe.seed_asset_failed");
        }

        return result.AssetId.GetValueOrDefault();
    }

    public async Task<InboxCardDto> CopyAndObserveAsync(
        IHost host,
        string fixtureFilename,
        string targetFilename,
        ProbeTimeProvider clock,
        CancellationToken cancellationToken)
    {
        File.Copy(
            Path.Combine(FixturesDirectory, fixtureFilename),
            Path.Combine(InboxDirectory, targetFilename),
            overwrite: false);
        await ObserveAsync(host, clock, cancellationToken);
        return await FindCardAsync(host, targetFilename, cancellationToken);
    }

    public static async Task ObserveAsync(
        IHost host,
        ProbeTimeProvider clock,
        CancellationToken cancellationToken)
    {
        IInboxScanner scanner = host.Services.GetRequiredService<IInboxScanner>();
        await scanner.ScanAsync(ScanTrigger.Manual, cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(11));
        await scanner.ScanAsync(ScanTrigger.StabilityCheck, cancellationToken);
    }

    public static async Task<InboxCardDto> FindCardAsync(
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

    public static async Task<InboxDetailsDto> WaitForProcessingAsync(
        IInboxQueryService query,
        Guid inboxItemId,
        Guid expectedJobId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + ProcessingTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            InboxDetailsDto? details = await query.GetDetailsAsync(inboxItemId, cancellationToken);
            ProcessingSummaryDto? processing = details?.Processing;
            if (processing?.JobId == expectedJobId)
            {
                if (processing.Status == ProcessingJobStatus.Succeeded)
                {
                    return details!;
                }

                if (processing.Status is ProcessingJobStatus.Failed
                    or ProcessingJobStatus.Interrupted or ProcessingJobStatus.Cancelled)
                {
                    throw new ProbeFailureException(
                        processing.ErrorCode ?? "probe.processing_failed");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new ProbeFailureException("probe.processing_timeout");
    }

    public static async Task<string> FileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
    }

    public async Task WriteResultAsync<T>(
        string filename,
        T result,
        CancellationToken cancellationToken,
        bool writeThrough = false)
    {
        string path = Path.Combine(Request.Root, filename);
        await using var output = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | (writeThrough ? FileOptions.WriteThrough : FileOptions.None)
        });
        await JsonSerializer.SerializeAsync(
            output,
            result,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    public void ValidateOwnedProbeTree(params string[] directories)
    {
        _ = ValidateRootOwnership(
            Request.Root, Request.OwnerToken, Request.Scenario);
        foreach (string directory in directories)
        {
            string fullPath = Path.GetFullPath(directory);
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(fullPath), Request.Root)
                || !Directory.Exists(fullPath) || IsReparsePoint(fullPath))
            {
                throw new IOException("The package probe directory is not safe.");
            }
        }

        if (!ValidateFixtures(FixturesDirectory))
        {
            throw new IOException("The package probe fixtures changed.");
        }

        string[] allowedEntries =
        [
            Stage4PackageProbeOptions.OwnerFilename,
            Stage4PackageProbeOptions.FixturesDirectoryName,
            .. directories.Select(Path.GetFileName).Where(name => name is not null).Cast<string>()
        ];
        string[] actualEntries = Directory.GetFileSystemEntries(Request.Root)
            .Select(Path.GetFileName).Where(name => name is not null).Cast<string>().ToArray();
        if (actualEntries.Length != allowedEntries.Length
            || actualEntries.Except(allowedEntries, StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new IOException("The package probe root contains an extra entry.");
        }

        EnsureResultDoesNotExist(ResultFilename);
    }

    public void ValidateCrashResultWrite()
    {
        _ = ValidateRootOwnership(
            Request.Root, Request.OwnerToken, Request.Scenario);
        if (Stage4CrashProbeManifest.ReadValidated(
                Path.Combine(Request.Root, Stage4PackageProbeOptions.CrashManifestFilename),
                Request.OwnerToken) is null)
        {
            throw new IOException("The diagnostic crash manifest changed.");
        }

        EnsureResultDoesNotExist(Stage4PackageProbeOptions.ResultFilename);
    }

    public bool ProcessingWorkspaceIsEmpty()
    {
        string processing = Path.Combine(WorkDirectory, ".tracefold", "processing");
        return !Directory.Exists(processing) || !Directory.EnumerateFileSystemEntries(processing).Any();
    }

    public bool InternalStorageIsClean()
    {
        foreach (string category in new[] { "commit", "recovery" })
        {
            string path = Path.Combine(LibraryDirectory, ".tracefold", category);
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            {
                return false;
            }
        }

        return true;
    }

    public bool InternalWorkIsClean() =>
        ProcessingWorkspaceIsEmpty() && InternalStorageIsClean();

    public Task WriteScenarioResultAsync<T>(
        T result,
        CancellationToken cancellationToken) =>
        WriteResultAsync(ResultFilename, result, cancellationToken);

    internal static string ValidateRootOwnership(
        string rootValue,
        string ownerToken,
        PackageProbeScenarioKind scenario)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootValue));
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        string? parent = Path.GetDirectoryName(root);
        string basename = Path.GetFileName(root);
        if (parent is null
            || !StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(parent), temp)
            || !basename.StartsWith(RootNamePrefixFor(scenario), StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(root) || IsReparsePoint(root))
        {
            throw new IOException("The probe root is not owned.");
        }

        string ownerPath = Path.Combine(root, Stage4PackageProbeOptions.OwnerFilename);
        if (HasReparsePointAtOrBelow(root)
            || !File.Exists(ownerPath)
            || !StringComparer.Ordinal.Equals(File.ReadAllText(ownerPath), ownerToken))
        {
            throw new ProbeOptionValidationException("probe.owner_mismatch");
        }

        return root;
    }

    internal static string ResultFilenameFor(PackageProbeScenarioKind scenario) => scenario switch
    {
        PackageProbeScenarioKind.Stage4Conflict or
        PackageProbeScenarioKind.Stage4CrashSeed or
        PackageProbeScenarioKind.Stage4CrashResume => Stage4PackageProbeOptions.ResultFilename,
        PackageProbeScenarioKind.Stage5ABatch => PackageProbeOptions.Stage5AResultFilename,
        PackageProbeScenarioKind.Stage5BLibrary => PackageProbeOptions.Stage5BResultFilename,
        PackageProbeScenarioKind.Stage5CTaxonomy => PackageProbeOptions.Stage5CResultFilename,
        PackageProbeScenarioKind.Stage5CBDiagnostics =>
            PackageProbeOptions.Stage5CBResultFilename,
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    internal static bool ValidateFixtures(string directory)
    {
        if (!Directory.Exists(directory) || IsReparsePoint(directory))
        {
            return false;
        }

        string[] entries = Directory.GetFileSystemEntries(directory);
        if (entries.Length != FixtureHashes.Count)
        {
            return false;
        }

        foreach ((string filename, string expectedHash) in FixtureHashes)
        {
            string path = Path.Combine(directory, filename);
            if (!File.Exists(path) || Directory.Exists(path) || IsReparsePoint(path))
            {
                return false;
            }

            using FileStream input = File.OpenRead(path);
            if (!StringComparer.Ordinal.Equals(
                    Convert.ToHexString(SHA256.HashData(input)),
                    expectedHash))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool ValidateInitialEntries(string root)
    {
        foreach (string name in RuntimeDirectories
            .Append(Stage4PackageProbeOptions.ResultFilename)
            .Append(Stage4PackageProbeOptions.CrashManifestFilename))
        {
            if (File.Exists(Path.Combine(root, name)) || Directory.Exists(Path.Combine(root, name)))
            {
                return false;
            }
        }

        return EntriesEqual(root, [Stage4PackageProbeOptions.FixturesDirectoryName, Stage4PackageProbeOptions.OwnerFilename]);
    }

    internal static bool ValidateResumeEntries(string root)
    {
        foreach (string name in RuntimeDirectories)
        {
            string path = Path.Combine(root, name);
            if (!Directory.Exists(path) || IsReparsePoint(path))
            {
                return false;
            }
        }

        string manifest = Path.Combine(root, Stage4PackageProbeOptions.CrashManifestFilename);
        return File.Exists(manifest) && !IsReparsePoint(manifest)
            && !File.Exists(Path.Combine(root, Stage4PackageProbeOptions.ResultFilename))
            && !Directory.Exists(Path.Combine(root, Stage4PackageProbeOptions.ResultFilename))
            && EntriesEqual(root,
            [
                Stage4PackageProbeOptions.FixturesDirectoryName,
                Stage4PackageProbeOptions.OwnerFilename,
                Stage4PackageProbeOptions.CrashManifestFilename,
                .. RuntimeDirectories
            ]);
    }

    internal static void Require(bool condition, string errorCode)
    {
        if (!condition)
        {
            throw new ProbeFailureException(errorCode);
        }
    }

    /// <summary>
    /// Keeps the historical stable probe error code in the persisted result and reports
    /// the observed state, with every predicate that did not hold, on the probe trace.
    /// </summary>
    internal static void RequireStep(
        string errorCode,
        string detail,
        params (string Name, bool Holds)[] conditions)
    {
        string[] failed = [.. conditions
            .Where(condition => !condition.Holds)
            .Select(condition => condition.Name)];
        if (failed.Length == 0)
        {
            return;
        }

        PackageProbeTrace.Write(errorCode + "; failed=" + string.Join("+", failed)
            + "; " + detail);
        throw new ProbeFailureException(errorCode);
    }

    internal static string Describe(IngestResult result) =>
        result.Outcome + "/" + result.Message;

    private void EnsureResultDoesNotExist(string filename)
    {
        string resultPath = Path.Combine(Request.Root, filename);
        if (File.Exists(resultPath) || Directory.Exists(resultPath))
        {
            throw new IOException("The package probe result already exists.");
        }
    }

    private static bool EntriesEqual(string root, string[] expected)
    {
        string[] actual = Directory.GetFileSystemEntries(root).Select(Path.GetFileName)
            .Where(name => name is not null).Cast<string>().Order(StringComparer.Ordinal).ToArray();
        Array.Sort(expected, StringComparer.Ordinal);
        return actual.SequenceEqual(expected, StringComparer.Ordinal);
    }

    private static bool IsReparsePoint(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static string RootNamePrefixFor(PackageProbeScenarioKind scenario) => scenario switch
    {
        PackageProbeScenarioKind.Stage4Conflict or
        PackageProbeScenarioKind.Stage4CrashSeed or
        PackageProbeScenarioKind.Stage4CrashResume => Stage4PackageProbeOptions.RootNamePrefix,
        PackageProbeScenarioKind.Stage5ABatch => PackageProbeOptions.Stage5ARootNamePrefix,
        PackageProbeScenarioKind.Stage5BLibrary => PackageProbeOptions.Stage5BRootNamePrefix,
        PackageProbeScenarioKind.Stage5CTaxonomy => PackageProbeOptions.Stage5CRootNamePrefix,
        PackageProbeScenarioKind.Stage5CBDiagnostics =>
            PackageProbeOptions.Stage5CBRootNamePrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    private static bool HasReparsePointAtOrBelow(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (IsReparsePoint(entry))
                {
                    return true;
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }

        return false;
    }

    internal sealed class ProbeOptionValidationException(string errorCode) : IOException
    {
        public string ErrorCode { get; } = errorCode;
    }
}

public sealed class ProbeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset now = utcNow;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan value) => now += value;
}

public sealed class ProbeFailureException(string errorCode) : Exception
{
    public string ErrorCode { get; } = errorCode;
}
