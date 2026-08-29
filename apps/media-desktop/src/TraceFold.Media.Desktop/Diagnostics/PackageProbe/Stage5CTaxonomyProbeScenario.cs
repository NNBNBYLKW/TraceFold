using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed class Stage5CTaxonomyProbeScenario : IPackageProbeScenario
{
    private const string PhotoFixture = "photo.jpg";

    public PackageProbeScenarioKind Kind =>
        PackageProbeScenarioKind.Stage5CTaxonomy;

    public async Task<int> RunAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken)
    {
        environment.CreateRuntimeDirectories();
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);

        var clock = new ProbeTimeProvider(
            DateTimeOffset.Parse("2026-08-29T08:00:00Z"));
        IHost? host = null;
        bool queryPassed = false;
        bool mergePassed = false;
        bool mixedDeleteBlocked = false;
        bool deleteUndoPassed = false;
        bool mergeUndoPassed = false;
        bool versioningPassed = false;
        bool physicalFilesUnchanged = false;
        bool internalStorageClean = false;
        string phase = "host";
        try
        {
            host = environment.BuildHost(clock);
            await environment.StartHostAsync(
                host,
                saveFolders: true,
                cancellationToken);
            IServiceProvider services = host.Services;

            phase = "seed_owners";
            (MetadataOwner inboxOwner, MetadataOwner libraryOwner) =
                await SeedOwnersAsync(
                    host,
                    environment,
                    clock,
                    cancellationToken);

            phase = "seed_terms";
            ITaxonomyService taxonomy =
                services.GetRequiredService<ITaxonomyService>();
            ProbeTerms terms = await CreateTermsAsync(
                taxonomy,
                cancellationToken);
            IMetadataEditorService metadata =
                services.GetRequiredService<IMetadataEditorService>();
            await SaveAuthorsAsync(
                metadata,
                inboxOwner,
                [terms.SourceOne.Id, terms.Target.Id, terms.SourceTwo.Id],
                cancellationToken);
            await SaveAuthorsAsync(
                metadata,
                libraryOwner,
                [terms.Survivor.Id, terms.SourceTwo.Id, terms.SourceOne.Id],
                cancellationToken);

            phase = "query";
            ITaxonomyManagementQueryService managementQuery =
                services.GetRequiredService<ITaxonomyManagementQueryService>();
            IReadOnlyList<TaxonomyManagementTermDto> referenced =
                await QueryAuthorsAsync(
                    managementQuery,
                    TaxonomyReferenceFilter.Referenced,
                    cancellationToken);
            IReadOnlyList<TaxonomyManagementTermDto> unused =
                await QueryAuthorsAsync(
                    managementQuery,
                    TaxonomyReferenceFilter.Unused,
                    cancellationToken);
            queryPassed = referenced.Select(term => term.Id).ToHashSet().SetEquals([
                    terms.SourceOne.Id,
                    terms.SourceTwo.Id,
                    terms.Target.Id,
                    terms.Survivor.Id
                ])
                && unused.Select(term => term.Id).ToHashSet().SetEquals([
                    terms.UnusedDelete.Id,
                    terms.UnusedMixed.Id
                ])
                && referenced.Single(term => term.Id == terms.SourceOne.Id)
                    .ReferenceCount == 2
                && referenced.Single(term => term.Id == terms.Target.Id)
                    .InboxReferenceCount == 1
                && referenced.Single(term => term.Id == terms.Target.Id)
                    .LibraryReferenceCount == 0;
            PackageProbeEnvironment.Require(
                queryPassed,
                "probe.taxonomy_query_invalid");

            phase = "physical_before";
            IReadOnlyList<PhysicalFileFact> beforeFiles =
                await SnapshotPhysicalFilesAsync(environment, cancellationToken);

            ITaxonomyMaintenanceService maintenance =
                services.GetRequiredService<ITaxonomyMaintenanceService>();
            phase = "mixed_delete";
            TaxonomyDeletePrepareResult mixedPrepared =
                await maintenance.PrepareDeleteAsync(
                    TaxonomyDeleteSelection.Create(
                        TaxonomyKind.Author,
                        [terms.Target.Id, terms.UnusedMixed.Id]),
                    cancellationToken);
            PackageProbeEnvironment.Require(
                mixedPrepared.Succeeded && mixedPrepared.Preparation is not null,
                mixedPrepared.ErrorCode ?? "probe.taxonomy_mixed_prepare_failed");
            TaxonomyMaintenanceResult mixedResult =
                await maintenance.DeleteUnusedAsync(
                    mixedPrepared.Preparation!,
                    cancellationToken);
            IReadOnlyList<TaxonomyManagementTermDto> afterMixed =
                await QueryAllAuthorsAsync(managementQuery, cancellationToken);
            mixedDeleteBlocked = !mixedPrepared.Preparation!.CanDelete
                && mixedPrepared.Preparation.Blockers.Count == 1
                && mixedPrepared.Preparation.Blockers[0].Term.Id == terms.Target.Id
                && !mixedResult.Succeeded
                && mixedResult.ErrorCode
                    == TaxonomyMaintenanceErrorCodes.DeleteInUse
                && afterMixed.Any(term => term.Id == terms.UnusedMixed.Id);
            PackageProbeEnvironment.Require(
                mixedDeleteBlocked,
                "probe.taxonomy_mixed_delete_not_blocked");

            phase = "delete";
            TaxonomyDeletePrepareResult deletePrepared =
                await maintenance.PrepareDeleteAsync(
                    TaxonomyDeleteSelection.Create(
                        TaxonomyKind.Author,
                        [terms.UnusedDelete.Id]),
                    cancellationToken);
            PackageProbeEnvironment.Require(
                deletePrepared.Succeeded
                && deletePrepared.Preparation?.CanDelete == true,
                deletePrepared.ErrorCode ?? "probe.taxonomy_delete_prepare_failed");
            TaxonomyMaintenanceResult deleted =
                await maintenance.DeleteUnusedAsync(
                    deletePrepared.Preparation!,
                    cancellationToken);
            bool absentAfterDelete = !(await QueryAllAuthorsAsync(
                    managementQuery,
                    cancellationToken))
                .Any(term => term.Id == terms.UnusedDelete.Id);
            TaxonomyMaintenanceUndoResult deleteUndo =
                await maintenance.UndoLastAsync(cancellationToken);
            TaxonomyManagementTermDto? restoredUnused =
                (await QueryAllAuthorsAsync(managementQuery, cancellationToken))
                .SingleOrDefault(term => term.Id == terms.UnusedDelete.Id);
            deleteUndoPassed = deleted.Succeeded
                && deleted.DeletedTermCount == 1
                && deleted.CanUndo
                && absentAfterDelete
                && deleteUndo.Succeeded
                && deleteUndo.RestoredTermCount == 1
                && restoredUnused?.DisplayName == terms.UnusedDelete.DisplayName;
            PackageProbeEnvironment.Require(
                deleteUndoPassed,
                deleteUndo.ErrorCode ?? "probe.taxonomy_delete_undo_invalid");

            phase = "merge_prepare";
            MetadataFact inboxBefore = await MetadataFact.LoadAsync(
                metadata,
                inboxOwner,
                cancellationToken);
            MetadataFact libraryBefore = await MetadataFact.LoadAsync(
                metadata,
                libraryOwner,
                cancellationToken);
            TaxonomyMergePrepareResult mergePrepared =
                await maintenance.PrepareMergeAsync(
                    TaxonomyMergeSelection.Create(
                        TaxonomyKind.Author,
                        [terms.SourceOne.Id, terms.SourceTwo.Id],
                        terms.Target.Id),
                    cancellationToken);
            PackageProbeEnvironment.Require(
                mergePrepared.Succeeded && mergePrepared.Preparation is not null,
                mergePrepared.ErrorCode ?? "probe.taxonomy_merge_prepare_failed");
            TaxonomyMergePreviewDto preview = maintenance.PreviewMerge(
                mergePrepared.Preparation!,
                finalNameOverride: null);
            PackageProbeEnvironment.Require(
                preview.CanApply
                && preview.Rows.Count == 2
                && preview.InboxAffectedCount == 1
                && preview.LibraryAffectedCount == 1,
                preview.ErrorCode ?? "probe.taxonomy_merge_preview_invalid");

            phase = "merge_apply";
            TaxonomyMaintenanceResult merged = await maintenance.ApplyMergeAsync(
                mergePrepared.Preparation!,
                finalNameOverride: null,
                cancellationToken);
            MetadataFact inboxAfter = await MetadataFact.LoadAsync(
                metadata,
                inboxOwner,
                cancellationToken);
            MetadataFact libraryAfter = await MetadataFact.LoadAsync(
                metadata,
                libraryOwner,
                cancellationToken);
            IReadOnlyList<TaxonomyManagementTermDto> afterMerge =
                await QueryAllAuthorsAsync(managementQuery, cancellationToken);
            versioningPassed = inboxAfter.Version == inboxBefore.Version + 1
                && libraryAfter.Version == libraryBefore.Version + 1;
            mergePassed = merged.Succeeded
                && merged.ChangedInboxCount == 1
                && merged.ChangedLibraryCount == 1
                && merged.DeletedTermCount == 2
                && merged.CanUndo
                && inboxAfter.AuthorIds.SequenceEqual([terms.Target.Id])
                && libraryAfter.AuthorIds.SequenceEqual([
                    terms.Survivor.Id,
                    terms.Target.Id
                ])
                && afterMerge.All(term => term.Id != terms.SourceOne.Id)
                && afterMerge.All(term => term.Id != terms.SourceTwo.Id)
                && afterMerge.Single(term => term.Id == terms.Target.Id)
                    .ReferenceCount == 2;
            PackageProbeEnvironment.Require(
                mergePassed,
                merged.ErrorCode ?? "probe.taxonomy_merge_invalid");
            PackageProbeEnvironment.Require(
                versioningPassed,
                "probe.taxonomy_versioning_invalid");

            phase = "merge_undo";
            TaxonomyMaintenanceUndoResult mergeUndo =
                await maintenance.UndoLastAsync(cancellationToken);
            MetadataFact inboxRestored = await MetadataFact.LoadAsync(
                metadata,
                inboxOwner,
                cancellationToken);
            MetadataFact libraryRestored = await MetadataFact.LoadAsync(
                metadata,
                libraryOwner,
                cancellationToken);
            IReadOnlyList<TaxonomyManagementTermDto> afterMergeUndo =
                await QueryAllAuthorsAsync(managementQuery, cancellationToken);
            mergeUndoPassed = mergeUndo.Succeeded
                && mergeUndo.RestoredOwnerCount == 2
                && mergeUndo.RestoredTermCount == 2
                && inboxRestored.AuthorIds.SequenceEqual(inboxBefore.AuthorIds)
                && libraryRestored.AuthorIds.SequenceEqual(libraryBefore.AuthorIds)
                && afterMergeUndo.Any(term => term.Id == terms.SourceOne.Id)
                && afterMergeUndo.Any(term => term.Id == terms.SourceTwo.Id);
            PackageProbeEnvironment.Require(
                mergeUndoPassed,
                mergeUndo.ErrorCode ?? "probe.taxonomy_merge_undo_invalid");

            phase = "physical_after";
            IReadOnlyList<PhysicalFileFact> afterFiles =
                await SnapshotPhysicalFilesAsync(environment, cancellationToken);
            physicalFilesUnchanged = beforeFiles.SequenceEqual(afterFiles);
            PackageProbeEnvironment.Require(
                physicalFilesUnchanged,
                "probe.taxonomy_physical_file_changed");

            phase = "storage";
            internalStorageClean = environment.InternalWorkIsClean();
            PackageProbeEnvironment.Require(
                internalStorageClean,
                "probe.taxonomy_internal_storage_retained");

            await WriteResultAsync(
                environment,
                new Stage5CTaxonomyProbeResult(
                    true,
                    queryPassed,
                    mergePassed,
                    mixedDeleteBlocked,
                    deleteUndoPassed,
                    mergeUndoPassed,
                    versioningPassed,
                    physicalFilesUnchanged,
                    internalStorageClean,
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

        Task WriteFailureAsync(string errorCode) => WriteResultAsync(
            environment,
            new Stage5CTaxonomyProbeResult(
                false,
                queryPassed,
                mergePassed,
                mixedDeleteBlocked,
                deleteUndoPassed,
                mergeUndoPassed,
                versioningPassed,
                physicalFilesUnchanged,
                internalStorageClean,
                errorCode),
            CancellationToken.None);
    }

    private static async Task<(MetadataOwner Inbox, MetadataOwner Library)>
        SeedOwnersAsync(
            IHost host,
            PackageProbeEnvironment environment,
            ProbeTimeProvider clock,
            CancellationToken cancellationToken)
    {
        string formalFilename = "stage5c-formal.jpg";
        string formalPath = Path.Combine(environment.InboxDirectory, formalFilename);
        await CopyDistinctFixtureAsync(
            environment,
            formalPath,
            marker: 1,
            cancellationToken);
        await PackageProbeEnvironment.ObserveAsync(host, clock, cancellationToken);
        InboxCardDto formalCard = await PackageProbeEnvironment.FindCardAsync(
            host,
            formalFilename,
            cancellationToken);
        IngestResult ingested = await host.Services
            .GetRequiredService<IIngestService>()
            .IngestAsync(formalCard.Id, cancellationToken);
        PackageProbeEnvironment.Require(
            ingested.Outcome == IngestOutcome.Completed
            && ingested.AssetId.HasValue,
            "probe.taxonomy_formal_seed_failed");

        string pendingFilename = "stage5c-pending.jpg";
        string pendingPath = Path.Combine(environment.InboxDirectory, pendingFilename);
        await CopyDistinctFixtureAsync(
            environment,
            pendingPath,
            marker: 2,
            cancellationToken);
        await PackageProbeEnvironment.ObserveAsync(host, clock, cancellationToken);
        InboxCardDto pendingCard = await PackageProbeEnvironment.FindCardAsync(
            host,
            pendingFilename,
            cancellationToken);
        return (
            new MetadataOwner(MetadataOwnerKind.InboxItem, pendingCard.Id),
            new MetadataOwner(
                MetadataOwnerKind.MediaAsset,
                ingested.AssetId.GetValueOrDefault()));
    }

    private static async Task CopyDistinctFixtureAsync(
        PackageProbeEnvironment environment,
        string destination,
        byte marker,
        CancellationToken cancellationToken)
    {
        File.Copy(
            Path.Combine(environment.FixturesDirectory, PhotoFixture),
            destination,
            overwrite: false);
        await using var output = new FileStream(
            destination,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1,
            useAsync: true);
        await output.WriteAsync(new[] { marker }, cancellationToken);
    }

    private static async Task<ProbeTerms> CreateTermsAsync(
        ITaxonomyService taxonomy,
        CancellationToken cancellationToken) =>
        new(
            await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                "Probe Author Source One",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                "Probe Author Source Two",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                "Probe Author Target",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                "Probe Author Survivor",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                "Probe Author Unused Delete",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                "Probe Author Unused Mixed",
                cancellationToken));

    private static async Task SaveAuthorsAsync(
        IMetadataEditorService metadata,
        MetadataOwner owner,
        IReadOnlyList<Guid> authorIds,
        CancellationToken cancellationToken)
    {
        MetadataDocument document = await metadata.LoadAsync(
            owner,
            cancellationToken)
            ?? throw new ProbeFailureException(
                "probe.taxonomy_metadata_missing");
        MetadataSaveResult saved = await metadata.SaveAsync(
            owner,
            new MetadataUpdate(
                document.DisplayName,
                authorIds,
                document.Series?.Id,
                document.Tags.Select(tag => tag.Id).ToArray(),
                document.Version),
            cancellationToken);
        PackageProbeEnvironment.Require(
            saved.Succeeded,
            saved.ErrorCode ?? "probe.taxonomy_metadata_seed_failed");
    }

    private static Task<IReadOnlyList<TaxonomyManagementTermDto>>
        QueryAuthorsAsync(
            ITaxonomyManagementQueryService query,
            TaxonomyReferenceFilter referenceFilter,
            CancellationToken cancellationToken) =>
        query.QueryAsync(
            TaxonomyManagementQuery.Create(
                TaxonomyKind.Author,
                null,
                referenceFilter,
                TaxonomySortField.DisplayName,
                TaxonomySortDirection.Ascending),
            cancellationToken);

    private static Task<IReadOnlyList<TaxonomyManagementTermDto>>
        QueryAllAuthorsAsync(
            ITaxonomyManagementQueryService query,
            CancellationToken cancellationToken) =>
        QueryAuthorsAsync(query, TaxonomyReferenceFilter.All, cancellationToken);

    private static async Task<IReadOnlyList<PhysicalFileFact>>
        SnapshotPhysicalFilesAsync(
            PackageProbeEnvironment environment,
            CancellationToken cancellationToken)
    {
        var facts = new List<PhysicalFileFact>();
        await AddDirectoryAsync("inbox", environment.InboxDirectory);
        await AddDirectoryAsync("library", environment.LibraryDirectory);
        return facts
            .OrderBy(fact => fact.Scope, StringComparer.Ordinal)
            .ThenBy(fact => fact.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        async Task AddDirectoryAsync(string scope, string root)
        {
            string internalPrefix = ".tracefold" + Path.DirectorySeparatorChar;
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
                facts.Add(new PhysicalFileFact(
                    scope,
                    relative,
                    info.Length,
                    info.LastWriteTimeUtc.Ticks,
                    await PackageProbeEnvironment.FileSha256Async(
                        path,
                        cancellationToken)));
            }
        }
    }

    private static async Task WriteResultAsync(
        PackageProbeEnvironment environment,
        Stage5CTaxonomyProbeResult result,
        CancellationToken cancellationToken)
    {
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);
        await environment.WriteScenarioResultAsync(result, cancellationToken);
    }

    private sealed record ProbeTerms(
        TaxonomyTermDto SourceOne,
        TaxonomyTermDto SourceTwo,
        TaxonomyTermDto Target,
        TaxonomyTermDto Survivor,
        TaxonomyTermDto UnusedDelete,
        TaxonomyTermDto UnusedMixed);

    private sealed record MetadataFact(
        long Version,
        IReadOnlyList<Guid> AuthorIds)
    {
        public static async Task<MetadataFact> LoadAsync(
            IMetadataEditorService metadata,
            MetadataOwner owner,
            CancellationToken cancellationToken)
        {
            MetadataDocument document = await metadata.LoadAsync(
                owner,
                cancellationToken)
                ?? throw new ProbeFailureException(
                    "probe.taxonomy_metadata_missing");
            return new MetadataFact(
                document.Version,
                document.Authors.Select(author => author.Id).ToArray());
        }
    }

    private sealed record PhysicalFileFact(
        string Scope,
        string RelativePath,
        long Length,
        long LastWriteUtcTicks,
        string Sha256);
}
