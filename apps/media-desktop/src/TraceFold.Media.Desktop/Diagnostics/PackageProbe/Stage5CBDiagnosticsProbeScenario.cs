using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed class Stage5CBDiagnosticsProbeScenario : IPackageProbeScenario
{
    private const string FormalFilename = "stage5cb-formal.jpg";
    private const string PendingFilename = "stage5cb-pending.jpg";
    private const string MetadataSentinel = "Stage5CB Metadata Sentinel";
    private const string ExceptionSentinel =
        "Stage5CB synthetic exception must remain private";

    private static readonly HashSet<string> BackgroundTables = new(
        [
            "processing_plans",
            "processing_plan_inputs",
            "processing_jobs",
            "processed_outputs",
            "ingest_operations",
            "ingest_cleanup_entries"
        ],
        StringComparer.Ordinal);

    public PackageProbeScenarioKind Kind =>
        PackageProbeScenarioKind.Stage5CBDiagnostics;

    public async Task<int> RunAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        environment.CreateRuntimeDirectories();
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);

        var clock = new ProbeTimeProvider(
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"));
        IHost? host = null;
        bool allCategoriesObserved = false;
        bool packagedEnvironmentHealthy = false;
        bool failureIsolationPassed = false;
        bool redactionPassed = false;
        bool businessDataUnchanged = false;
        bool settingsUnchanged = false;
        bool backgroundStateUnchanged = false;
        bool mediaFilesUnchanged = false;
        bool temporaryArtifactsClean = false;
        bool toolProcessesClean = false;
        string phase = "host";
        try
        {
            host = environment.BuildHost(clock);
            await environment.StartHostAsync(
                host,
                saveFolders: true,
                cancellationToken);
            IServiceProvider services = host.Services;

            phase = "seed";
            Guid formalAssetId = await environment.SeedAssetAsync(
                host,
                "photo.jpg",
                FormalFilename,
                clock,
                cancellationToken);
            TaxonomyTermDto author = await services
                .GetRequiredService<ITaxonomyService>()
                .CreateAsync(
                    TaxonomyKind.Author,
                    MetadataSentinel,
                    cancellationToken);
            IMetadataEditorService metadata =
                services.GetRequiredService<IMetadataEditorService>();
            var owner = new MetadataOwner(
                MetadataOwnerKind.MediaAsset,
                formalAssetId);
            MetadataDocument document = await metadata.LoadAsync(
                owner,
                cancellationToken)
                ?? throw new ProbeFailureException(
                    "probe.diagnostics_metadata_missing");
            MetadataSaveResult metadataSaved = await metadata.SaveAsync(
                owner,
                new MetadataUpdate(
                    MetadataSentinel,
                    [author.Id],
                    SeriesId: null,
                    TagIds: [],
                    document.Version),
                cancellationToken);
            PackageProbeEnvironment.Require(
                metadataSaved.Succeeded,
                metadataSaved.ErrorCode
                    ?? "probe.diagnostics_metadata_seed_failed");
            var pending = await environment.CopyAndObserveAsync(
                host,
                "photo.jpg",
                PendingFilename,
                clock,
                cancellationToken);
            string hashSentinel = await PackageProbeEnvironment.FileSha256Async(
                Path.Combine(environment.LibraryDirectory, FormalFilename),
                cancellationToken);

            phase = "snapshot_before";
            string databasePath = services
                .GetRequiredService<IAppDataPathProvider>()
                .GetPaths()
                .DatabasePath;
            DatabaseSnapshot databaseBefore = await SnapshotDatabaseAsync(
                databasePath,
                cancellationToken);
            IReadOnlyList<MediaFileSnapshot> mediaBefore =
                await SnapshotMediaFilesAsync(environment, cancellationToken);
            HashSet<int> toolProcessesBefore = SnapshotToolProcessIds(environment);

            phase = "diagnostics";
            ISystemDiagnosticProbe[] realProbes =
                [.. services.GetServices<ISystemDiagnosticProbe>()];
            PackageProbeEnvironment.Require(
                realProbes.Length == 5,
                "probe.diagnostics_probe_count_invalid");
            SystemDiagnosticsSnapshot realSnapshot = await services
                .GetRequiredService<ISystemDiagnosticsService>()
                .CheckAsync(cancellationToken);
            SystemDiagnosticCategory[] observedCategories =
                [.. realSnapshot.Items
                    .Select(item => item.Category)
                    .Distinct()
                    .Order()];
            allCategoriesObserved = observedCategories.SequenceEqual(
                Enum.GetValues<SystemDiagnosticCategory>());
            packagedEnvironmentHealthy = allCategoriesObserved
                && realSnapshot.OverallSeverity
                    == SystemDiagnosticSeverity.Healthy
                && realSnapshot.Items.All(item =>
                    item.Severity == SystemDiagnosticSeverity.Healthy);
            PackageProbeEnvironment.Require(
                allCategoriesObserved,
                "probe.diagnostics_categories_missing");

            phase = "failure_isolation";
            var isolatedService = new SystemDiagnosticsService(
                [
                    .. realProbes,
                    new ThrowingPackageDiagnosticProbe(ExceptionSentinel)
                ],
                services.GetRequiredService<SystemDiagnosticsOptions>(),
                clock);
            SystemDiagnosticsSnapshot isolatedSnapshot =
                await isolatedService.CheckAsync(cancellationToken);
            SystemDiagnosticItem? isolatedFailure = isolatedSnapshot.Items
                .SingleOrDefault(item =>
                    item.Id == "stage5cb-synthetic.failure");
            failureIsolationPassed = isolatedFailure?.ErrorCode
                    == "diagnostics.probe.failed"
                && realSnapshot.Items.All(real => isolatedSnapshot.Items.Any(
                    isolated => isolated.Id == real.Id
                        && isolated.Category == real.Category))
                && isolatedSnapshot.Items
                    .Select(item => item.Category)
                    .Distinct()
                    .Order()
                    .SequenceEqual(Enum.GetValues<SystemDiagnosticCategory>());
            PackageProbeEnvironment.Require(
                failureIsolationPassed,
                "probe.diagnostics_failure_isolation_failed");

            phase = "redaction";
            string summary = services
                .GetRequiredService<IDiagnosticSummaryFormatter>()
                .Format(isolatedSnapshot, TimeZoneInfo.Utc);
            string[] privateValues =
            [
                request.Root,
                request.OwnerToken,
                "paired.mp4",
                "paired.m4a",
                "photo.jpg",
                FormalFilename,
                PendingFilename,
                MetadataSentinel,
                hashSentinel,
                formalAssetId.ToString("D"),
                formalAssetId.ToString("N"),
                pending.Id.ToString("D"),
                pending.Id.ToString("N"),
                author.Id.ToString("D"),
                author.Id.ToString("N"),
                ExceptionSentinel,
                Environment.UserName
            ];
            redactionPassed = privateValues
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .All(value => !summary.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase));
            PackageProbeEnvironment.Require(
                redactionPassed,
                "probe.diagnostics_redaction_failed");

            phase = "snapshot_after";
            DatabaseSnapshot databaseAfter = await SnapshotDatabaseAsync(
                databasePath,
                cancellationToken);
            IReadOnlyList<MediaFileSnapshot> mediaAfter =
                await SnapshotMediaFilesAsync(environment, cancellationToken);
            settingsUnchanged = databaseBefore.SectionEquals(
                databaseAfter,
                table => table == "app_settings");
            backgroundStateUnchanged = databaseBefore.SectionEquals(
                databaseAfter,
                table => BackgroundTables.Contains(table));
            businessDataUnchanged = databaseBefore.SectionEquals(
                databaseAfter,
                table => table != "app_settings"
                    && !BackgroundTables.Contains(table));
            mediaFilesUnchanged = mediaBefore.SequenceEqual(mediaAfter);
            temporaryArtifactsClean = NoTemporaryArtifacts(environment)
                && databaseAfter.Tables.Keys.All(table => !table.Contains(
                    "diagnostic",
                    StringComparison.OrdinalIgnoreCase))
                && !Directory.EnumerateFiles(
                        request.Root,
                        "*diagnostic*",
                        SearchOption.AllDirectories)
                    .Any();
            toolProcessesClean = await WaitForToolProcessesAsync(
                environment,
                toolProcessesBefore,
                cancellationToken);
            PackageProbeEnvironment.Require(
                businessDataUnchanged,
                "probe.diagnostics_business_changed");
            PackageProbeEnvironment.Require(
                settingsUnchanged,
                "probe.diagnostics_settings_changed");
            PackageProbeEnvironment.Require(
                backgroundStateUnchanged,
                "probe.diagnostics_background_changed");
            PackageProbeEnvironment.Require(
                mediaFilesUnchanged,
                "probe.diagnostics_media_changed");
            PackageProbeEnvironment.Require(
                temporaryArtifactsClean,
                "probe.diagnostics_temporary_artifacts_retained");
            PackageProbeEnvironment.Require(
                toolProcessesClean,
                "probe.diagnostics_tool_process_retained");

            await WriteResultAsync(
                environment,
                Result(succeeded: true, errorCode: null),
                cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProbeFailureException error)
        {
            await WriteFailureAsync(error.ErrorCode);
            return 1;
        }
        catch
        {
            await WriteFailureAsync($"probe.unexpected_failure.{phase}");
            return 1;
        }
        finally
        {
            if (host is not null)
            {
                await PackageProbeEnvironment.StopHostAsync(host);
                host.Dispose();
            }
        }

        Stage5CBDiagnosticsProbeResult Result(
            bool succeeded,
            string? errorCode) => new(
                succeeded,
                allCategoriesObserved,
                packagedEnvironmentHealthy,
                failureIsolationPassed,
                redactionPassed,
                businessDataUnchanged,
                settingsUnchanged,
                backgroundStateUnchanged,
                mediaFilesUnchanged,
                temporaryArtifactsClean,
                toolProcessesClean,
                errorCode);

        Task WriteFailureAsync(string errorCode) => WriteResultAsync(
            environment,
            Result(succeeded: false, errorCode),
            CancellationToken.None);
    }

    private static async Task<DatabaseSnapshot> SnapshotDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        string[] tables;
        await using (DbCommand tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText = """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
                ORDER BY name
                """;
            await using DbDataReader reader =
                await tableCommand.ExecuteReaderAsync(cancellationToken);
            var names = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                names.Add(reader.GetString(0));
            }

            tables = [.. names];
        }

        var rowsByTable = new SortedDictionary<string, string[]>(
            StringComparer.Ordinal);
        foreach (string table in tables)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {Quote(table)}";
            await using DbDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var fields = new string[reader.FieldCount];
                for (int index = 0; index < reader.FieldCount; index++)
                {
                    fields[index] = Encode(reader.GetValue(index));
                }

                rows.Add(string.Concat(fields.Select(value =>
                    $"{value.Length.ToString(CultureInfo.InvariantCulture)}:{value}")));
            }

            rows.Sort(StringComparer.Ordinal);
            rowsByTable[table] = [.. rows];
        }

        return new DatabaseSnapshot(rowsByTable);
    }

    private static async Task<IReadOnlyList<MediaFileSnapshot>>
        SnapshotMediaFilesAsync(
            PackageProbeEnvironment environment,
            CancellationToken cancellationToken)
    {
        var result = new List<MediaFileSnapshot>();
        await AddDirectoryAsync("fixtures", environment.FixturesDirectory);
        await AddDirectoryAsync("inbox", environment.InboxDirectory);
        await AddDirectoryAsync("library", environment.LibraryDirectory);
        await AddDirectoryAsync("work", environment.WorkDirectory);
        return result
            .OrderBy(item => item.Scope, StringComparer.Ordinal)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        async Task AddDirectoryAsync(string scope, string root)
        {
            string internalPrefix = ".tracefold"
                + Path.DirectorySeparatorChar;
            foreach (string path in Directory.EnumerateFiles(
                         root,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, path);
                if (relative.StartsWith(
                    internalPrefix,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = new FileInfo(path);
                result.Add(new MediaFileSnapshot(
                    scope,
                    relative,
                    info.Length,
                    await PackageProbeEnvironment.FileSha256Async(
                        path,
                        cancellationToken)));
            }
        }
    }

    private static bool NoTemporaryArtifacts(
        PackageProbeEnvironment environment) =>
        Directory.GetFiles(
            environment.Request.Root,
            ".tracefold-write-probe-*.tmp",
            SearchOption.AllDirectories).Length == 0;

    private static async Task<bool> WaitForToolProcessesAsync(
        PackageProbeEnvironment environment,
        IReadOnlySet<int> before,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            HashSet<int> after = SnapshotToolProcessIds(environment);
            if (!after.Except(before).Any())
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private static HashSet<int> SnapshotToolProcessIds(
        PackageProbeEnvironment environment)
    {
        string toolRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            environment.MediaToolRootOverride
            ?? Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg")));
        var result = new HashSet<int>();
        foreach (string processName in new[] { "ffmpeg", "ffprobe" })
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        string? executable = process.MainModule?.FileName;
                        if (executable is not null
                            && IsAtOrBelow(executable, toolRoot))
                        {
                            result.Add(process.Id);
                        }
                    }
                    catch (Exception error) when (error is Win32Exception
                        or InvalidOperationException
                        or NotSupportedException)
                    {
                    }
                }
            }
        }

        return result;
    }

    private static bool IsAtOrBelow(string path, string root)
    {
        string canonical = Path.GetFullPath(path);
        return canonical.StartsWith(
            root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static string Encode(object value) => value switch
    {
        DBNull => "null",
        byte[] bytes => $"blob:{Convert.ToHexString(bytes)}",
        double number => $"real:{number.ToString("R", CultureInfo.InvariantCulture)}",
        float number => $"real:{number.ToString("R", CultureInfo.InvariantCulture)}",
        IFormattable formattable =>
            $"value:{formattable.ToString(null, CultureInfo.InvariantCulture)}",
        _ => $"text:{Convert.ToString(value, CultureInfo.InvariantCulture)}"
    };

    private static async Task WriteResultAsync(
        PackageProbeEnvironment environment,
        Stage5CBDiagnosticsProbeResult result,
        CancellationToken cancellationToken)
    {
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);
        await environment.WriteScenarioResultAsync(result, cancellationToken);
    }

    private sealed class DatabaseSnapshot(
        IReadOnlyDictionary<string, string[]> tables)
    {
        public IReadOnlyDictionary<string, string[]> Tables { get; } = tables;

        public bool SectionEquals(
            DatabaseSnapshot other,
            Func<string, bool> include)
        {
            string[] expectedTables =
                [.. Tables.Keys.Where(include).Order(StringComparer.Ordinal)];
            string[] actualTables =
                [.. other.Tables.Keys.Where(include).Order(StringComparer.Ordinal)];
            return expectedTables.SequenceEqual(actualTables, StringComparer.Ordinal)
                && expectedTables.All(table => Tables[table].SequenceEqual(
                    other.Tables[table],
                    StringComparer.Ordinal));
        }
    }

    private sealed record MediaFileSnapshot(
        string Scope,
        string RelativePath,
        long Length,
        string Sha256);

    private sealed class ThrowingPackageDiagnosticProbe(string message)
        : ISystemDiagnosticProbe
    {
        public SystemDiagnosticProbeDescriptor Descriptor { get; } =
            SystemDiagnosticProbeDescriptor.Create(
                "stage5cb-synthetic",
                SystemDiagnosticCategory.RuntimeAndLogs,
                order: 99,
                failureSeverity: SystemDiagnosticSeverity.Attention,
                failureTitle: "隔离检查",
                failureConclusion: "隔离检查未完成",
                failureImpact: "仅用于包内诊断证明。",
                failureGuidance: "无需用户操作。",
                timeoutErrorCode: "diagnostics.probe.timeout");

        public Task<IReadOnlyList<SystemDiagnosticItem>> CheckAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<IReadOnlyList<SystemDiagnosticItem>>(
                new InvalidOperationException(message));
        }
    }
}
