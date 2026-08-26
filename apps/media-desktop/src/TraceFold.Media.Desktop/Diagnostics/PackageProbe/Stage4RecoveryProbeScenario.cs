using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed class Stage4RecoveryProbeScenario : IPackageProbeScenario
{
    private const string PhotoFixture = "photo.jpg";

    public PackageProbeScenarioKind Kind => PackageProbeScenarioKind.Stage4CrashSeed;

    public async Task<int> RunAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken) =>
        request.Scenario == PackageProbeScenarioKind.Stage4CrashSeed
            ? await RunCrashSeedAsync(request, environment, cancellationToken)
            : await RunCrashResumeAsync(request, environment, cancellationToken);

    private static async Task<int> RunCrashSeedAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken)
    {
        IngestFaultPoint faultPoint = request.FaultPoint
            ?? throw new InvalidOperationException("A diagnostic crash seed requires a fault point.");
        environment.CreateRuntimeDirectories();
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);

        var clock = new ProbeTimeProvider(DateTimeOffset.Parse("2026-08-24T13:00:00Z"));
        IHost? host = null;
        try
        {
            host = environment.BuildHost(clock);
            await environment.StartHostAsync(host, saveFolders: true, cancellationToken);
            Guid assetId = await environment.SeedAssetAsync(
                host, PhotoFixture, "registered.jpg", clock, cancellationToken);
            string target = Path.Combine(environment.LibraryDirectory, "registered.jpg");
            string originalHash = await PackageProbeEnvironment.FileSha256Async(target, cancellationToken);

            await PackageProbeEnvironment.StopHostAsync(host);
            host.Dispose();
            host = null;

            string source = Path.Combine(environment.InboxDirectory, "registered.jpg");
            File.Copy(Path.Combine(environment.FixturesDirectory, PhotoFixture), source);
            await File.AppendAllTextAsync(source, request.OwnerToken, cancellationToken);
            string incomingHash = await PackageProbeEnvironment.FileSha256Async(source, cancellationToken);
            string manifestPath = Path.Combine(
                request.Root, Stage4PackageProbeOptions.CrashManifestFilename);
            Stage4CrashProbeManifest manifest = Stage4CrashProbeManifest.Create(
                request.OwnerToken, faultPoint, assetId, "registered.jpg", originalHash, incomingHash);
            manifest.WriteTo(manifestPath);

            var diagnostic = new DiagnosticIngestFaultInjector(faultPoint, manifest, manifestPath);
            host = environment.BuildHost(clock, diagnostic);
            await environment.StartHostAsync(host, saveFolders: false, cancellationToken);
            await PackageProbeEnvironment.ObserveAsync(host, clock, cancellationToken);
            InboxCardDto card = await PackageProbeEnvironment.FindCardAsync(
                host, "registered.jpg", cancellationToken);
            IngestResult blocked = await host.Services.GetRequiredService<IIngestService>()
                .IngestAsync(card.Id, cancellationToken);
            _ = await ResolveAsync(host, blocked, ConflictResolution.Replace,
                match => match.AssetId == assetId, cancellationToken);

            await WriteCrashResultAsync(environment, new Stage4CrashProbeResult(
                false, false, faultPoint.ToString(), assetId, null,
                "probe.diagnostic_crash_not_reached"), cancellationToken);
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
                await PackageProbeEnvironment.StopHostAsync(host);
                host.Dispose();
            }
        }
    }

    private static async Task<int> RunCrashResumeAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken)
    {
        Stage4CrashProbeManifest manifest = request.CrashManifest
            ?? throw new InvalidOperationException("A diagnostic crash resume requires a persisted manifest.");
        var clock = new ProbeTimeProvider(DateTimeOffset.Parse("2026-08-24T13:30:00Z"));
        IHost? host = null;
        try
        {
            host = environment.BuildHost(clock);
            await environment.StartHostAsync(host, saveFolders: false, cancellationToken);

            ILibraryQueryService libraryQuery = host.Services.GetRequiredService<ILibraryQueryService>();
            IReadOnlyList<LibraryAssetDto> assets = await libraryQuery.SearchAsync(
                LibraryQuery.Default,
                cancellationToken);
            LibraryAssetDto asset = assets.Single(candidate => candidate.Id == manifest.AssetId
                && candidate.ActualFilename.Equals(manifest.TargetFilename, StringComparison.OrdinalIgnoreCase));
            LibraryAssetDetailsDto details = (await libraryQuery.GetDetailsAsync(
                asset.Id, cancellationToken))!;
            string target = Path.Combine(environment.LibraryDirectory, manifest.TargetFilename);
            string targetHash = await PackageProbeEnvironment.FileSha256Async(target, cancellationToken);
            IReadOnlyList<IngestOperationRecord> incomplete = await host.Services
                .GetRequiredService<IIngestStore>().LoadIncompleteAsync(cancellationToken);
            string[] formalFiles = Directory.GetFiles(environment.LibraryDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).Where(name => name is not null).Cast<string>().ToArray();

            PackageProbeEnvironment.Require(
                assets.Count == 1 && details.Card.Id == manifest.AssetId
                && StringComparer.Ordinal.Equals(details.Sha256, manifest.IncomingSha256)
                && StringComparer.Ordinal.Equals(targetHash, manifest.IncomingSha256)
                && formalFiles.SequenceEqual([manifest.TargetFilename], StringComparer.OrdinalIgnoreCase)
                && !File.Exists(Path.Combine(environment.InboxDirectory, manifest.TargetFilename))
                && incomplete.All(operation => operation.Id != manifest.OperationId)
                && environment.InternalStorageIsClean(), "probe.crash_recovery_invalid");

            await WriteCrashResultAsync(environment, new Stage4CrashProbeResult(
                true, true, manifest.FaultPoint.ToString(), asset.Id, targetHash, null), cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProbeFailureException error)
        {
            await TryWriteCrashFailureAsync(environment, manifest, error.ErrorCode);
            return 1;
        }
        catch
        {
            await TryWriteCrashFailureAsync(environment, manifest, "probe.crash_resume_unexpected_failure");
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
    }

    private static async Task<IngestResult> ResolveAsync(
        IHost host,
        IngestResult blocked,
        ConflictResolution resolution,
        Func<ConflictMatchRecord, bool>? matchSelector,
        CancellationToken cancellationToken)
    {
        PackageProbeEnvironment.Require(blocked.Outcome == IngestOutcome.ConflictBlocked
            && blocked.ConflictCaseId.HasValue, "probe.conflict_missing");
        IConflictCaseStore store = host.Services.GetRequiredService<IConflictCaseStore>();
        ConflictCaseRecord conflict = (await store.LoadAsync(
            blocked.ConflictCaseId!.Value, cancellationToken))!;
        Guid? selectedId = matchSelector is null ? null : conflict.Matches.Single(matchSelector).Id;
        return await host.Services.GetRequiredService<IConflictResolutionService>()
            .ResolveAsync(conflict.Id, resolution, selectedId, cancellationToken);
    }

    private static async Task TryWriteCrashFailureAsync(
        PackageProbeEnvironment environment,
        Stage4CrashProbeManifest manifest,
        string errorCode)
    {
        try
        {
            await WriteCrashResultAsync(environment, new Stage4CrashProbeResult(
                false, false, manifest.FaultPoint.ToString(), manifest.AssetId, null, errorCode),
                CancellationToken.None);
        }
        catch
        {
        }
    }

    private static async Task WriteCrashResultAsync(
        PackageProbeEnvironment environment,
        Stage4CrashProbeResult result,
        CancellationToken cancellationToken)
    {
        environment.ValidateCrashResultWrite();
        await environment.WriteResultAsync(Stage4PackageProbeOptions.ResultFilename,
            result, cancellationToken, writeThrough: true);
    }
}
