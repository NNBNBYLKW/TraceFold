using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed class Stage5BLibraryProbeScenario : IPackageProbeScenario
{
    private const string PhotoFixture = "photo.jpg";

    public PackageProbeScenarioKind Kind => PackageProbeScenarioKind.Stage5BLibrary;

    public async Task<int> RunAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken)
    {
        environment.CreateRuntimeDirectories();
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);

        var clock = new ProbeTimeProvider(DateTimeOffset.Parse("2026-08-27T08:00:00Z"));
        IHost? host = null;
        bool strictIntersectionPassed = false;
        bool atomicBatchPassed = false;
        bool namingPreviewMatchedApply = false;
        bool undoPassed = false;
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

            phase = "seed";
            Guid[] assetIds = await SeedFourDistinctAssetsAsync(
                host,
                environment,
                clock,
                cancellationToken);
            IServiceProvider services = host.Services;
            ILibraryQueryService library =
                services.GetRequiredService<ILibraryQueryService>();
            IMetadataEditorService metadata =
                services.GetRequiredService<IMetadataEditorService>();
            ITaxonomyService taxonomy =
                services.GetRequiredService<ITaxonomyService>();
            phase = "taxonomy";
            TaxonomyTerms terms = await CreateTermsAsync(
                taxonomy,
                cancellationToken);
            phase = "metadata";
            await AssignOverlapAsync(
                metadata,
                assetIds,
                terms,
                cancellationToken);

            phase = "filter";
            IReadOnlyList<LibraryAssetDto> intersection = await library.SearchAsync(
                StrictIntersectionQuery(terms),
                cancellationToken);
            strictIntersectionPassed = intersection.Count == 1
                && intersection[0].Id == assetIds[0];
            PackageProbeEnvironment.Require(
                strictIntersectionPassed,
                "probe.library_intersection_invalid");

            phase = "snapshot";
            IReadOnlyList<PhysicalFileFact> beforeFiles =
                await SnapshotPhysicalFilesAsync(
                    environment.LibraryDirectory,
                    cancellationToken);
            Guid[] orderedIds = [assetIds[2], assetIds[0], assetIds[3]];
            LibrarySelectionEntry[] selection = new LibrarySelectionEntry[orderedIds.Length];
            var beforeMetadata = new Dictionary<Guid, MetadataFact>();
            for (int ordinal = 0; ordinal < orderedIds.Length; ordinal++)
            {
                LibraryAssetDetailsDto details = await GetDetailsAsync(
                    library,
                    orderedIds[ordinal],
                    cancellationToken);
                selection[ordinal] = new LibrarySelectionEntry(
                    details.Card.Id,
                    details.Metadata.Version,
                    ordinal);
                beforeMetadata.Add(details.Card.Id, MetadataFact.From(details.Metadata));
            }

            phase = "prepare";
            ILibraryBatchEditorService batch =
                services.GetRequiredService<ILibraryBatchEditorService>();
            LibraryBatchPrepareResult prepared = await batch.PrepareAsync(
                selection,
                cancellationToken);
            PackageProbeEnvironment.Require(
                prepared.Succeeded && prepared.Editor is not null,
                prepared.ErrorCode ?? "probe.library_prepare_failed");
            phase = "preview";
            LibraryBatchEditDraft draft = CombinedDraft(
                terms.ReplacementAuthor.DisplayName);
            LibraryBatchPreviewDto preview = batch.Preview(prepared.Editor!, draft);
            PackageProbeEnvironment.Require(
                preview.CanApply
                && preview.Rows.Count == selection.Length
                && preview.ChangedCount == selection.Length
                && preview.ErrorCount == 0
                && preview.Rows.All(row =>
                    !string.IsNullOrWhiteSpace(row.GeneratedDisplayName)),
                preview.ErrorCode ?? "probe.library_preview_invalid");
            IReadOnlyDictionary<Guid, string> previewNames = preview.Rows
                .ToDictionary(row => row.AssetId, row => row.GeneratedDisplayName!);

            phase = "apply";
            LibraryBatchApplyResult applied = await batch.ApplyAsync(
                selection,
                draft,
                cancellationToken);
            atomicBatchPassed = applied.Succeeded
                && applied.ChangedCount == selection.Length
                && applied.UnchangedCount == 0
                && applied.CanUndo;
            PackageProbeEnvironment.Require(
                atomicBatchPassed,
                applied.ErrorCode ?? "probe.library_batch_invalid");

            phase = "compare";
            namingPreviewMatchedApply = true;
            foreach (LibrarySelectionEntry entry in selection)
            {
                LibraryAssetDetailsDto details = await GetDetailsAsync(
                    library,
                    entry.AssetId,
                    cancellationToken);
                namingPreviewMatchedApply &=
                    StringComparer.Ordinal.Equals(
                        details.Metadata.DisplayName,
                        previewNames[entry.AssetId])
                    && details.Metadata.Authors.Count == 1
                    && details.Metadata.Authors[0].Id == terms.ReplacementAuthor.Id;
            }

            PackageProbeEnvironment.Require(
                namingPreviewMatchedApply,
                "probe.library_preview_apply_drift");

            phase = "undo";
            LibraryBatchUndoResult undone = await batch.UndoLastAsync(
                cancellationToken);
            undoPassed = undone.Succeeded
                && undone.RestoredCount == selection.Length;
            foreach (LibrarySelectionEntry entry in selection)
            {
                LibraryAssetDetailsDto restored = await GetDetailsAsync(
                    library,
                    entry.AssetId,
                    cancellationToken);
                undoPassed &= beforeMetadata[entry.AssetId]
                    .Matches(restored.Metadata);
            }

            PackageProbeEnvironment.Require(
                undoPassed,
                undone.ErrorCode ?? "probe.library_undo_invalid");

            phase = "physical";
            IReadOnlyList<PhysicalFileFact> afterFiles =
                await SnapshotPhysicalFilesAsync(
                    environment.LibraryDirectory,
                    cancellationToken);
            physicalFilesUnchanged = beforeFiles.SequenceEqual(afterFiles);
            PackageProbeEnvironment.Require(
                physicalFilesUnchanged,
                "probe.library_physical_file_changed");

            phase = "storage";
            internalStorageClean = environment.InternalWorkIsClean();
            PackageProbeEnvironment.Require(
                internalStorageClean,
                "probe.library_internal_storage_retained");

            await WriteResultAsync(
                environment,
                new Stage5BPackageProbeResult(
                    true,
                    strictIntersectionPassed,
                    atomicBatchPassed,
                    namingPreviewMatchedApply,
                    undoPassed,
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
            await WriteFailureAsync(
                environment,
                strictIntersectionPassed,
                atomicBatchPassed,
                namingPreviewMatchedApply,
                undoPassed,
                physicalFilesUnchanged,
                internalStorageClean,
                error.ErrorCode);
            return 1;
        }
        catch
        {
            await WriteFailureAsync(
                environment,
                strictIntersectionPassed,
                atomicBatchPassed,
                namingPreviewMatchedApply,
                undoPassed,
                physicalFilesUnchanged,
                internalStorageClean,
                $"probe.unexpected_failure.{phase}");
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

    private static async Task<Guid[]> SeedFourDistinctAssetsAsync(
        IHost host,
        PackageProbeEnvironment environment,
        ProbeTimeProvider clock,
        CancellationToken cancellationToken)
    {
        var assetIds = new Guid[4];
        IIngestService ingest = host.Services.GetRequiredService<IIngestService>();
        for (int index = 0; index < assetIds.Length; index++)
        {
            string filename = $"stage5b-asset-{index + 1}.jpg";
            string inboxPath = Path.Combine(environment.InboxDirectory, filename);
            File.Copy(
                Path.Combine(environment.FixturesDirectory, PhotoFixture),
                inboxPath,
                overwrite: false);
            await using (var output = new FileStream(
                inboxPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                useAsync: true))
            {
                await output.WriteAsync(
                    new byte[] { checked((byte)(index + 1)) },
                    cancellationToken);
            }

            await PackageProbeEnvironment.ObserveAsync(
                host,
                clock,
                cancellationToken);
            var card = await PackageProbeEnvironment.FindCardAsync(
                host,
                filename,
                cancellationToken);
            IngestResult result = await ingest.IngestAsync(
                card.Id,
                cancellationToken);
            PackageProbeEnvironment.Require(
                result.Outcome == IngestOutcome.Completed
                && result.AssetId.HasValue,
                "probe.library_seed_failed");
            assetIds[index] = result.AssetId.GetValueOrDefault();
        }

        return assetIds;
    }

    private static async Task<TaxonomyTerms> CreateTermsAsync(
        ITaxonomyService taxonomy,
        CancellationToken cancellationToken) =>
        new(
            await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                "Probe Author One",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                "Probe Author Two",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                "Probe Author Replacement",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Tag,
                "Probe Tag One",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Tag,
                "Probe Tag Two",
                cancellationToken),
            await taxonomy.CreateAsync(
                TaxonomyKind.Series,
                "Probe Series",
                cancellationToken));

    private static async Task AssignOverlapAsync(
        IMetadataEditorService metadata,
        IReadOnlyList<Guid> assetIds,
        TaxonomyTerms terms,
        CancellationToken cancellationToken)
    {
        Guid[][] authors =
        [
            [terms.AuthorOne.Id, terms.AuthorTwo.Id],
            [terms.AuthorOne.Id],
            [terms.AuthorTwo.Id],
            [terms.AuthorOne.Id, terms.AuthorTwo.Id]
        ];
        Guid[][] tags =
        [
            [terms.TagOne.Id, terms.TagTwo.Id],
            [terms.TagOne.Id],
            [terms.TagTwo.Id],
            [terms.TagOne.Id]
        ];
        Guid?[] series = [null, terms.Series.Id, null, null];
        for (int index = 0; index < assetIds.Count; index++)
        {
            var owner = new MetadataOwner(
                MetadataOwnerKind.MediaAsset,
                assetIds[index]);
            MetadataDocument document = await metadata.LoadAsync(
                owner,
                cancellationToken)
                ?? throw new ProbeFailureException(
                    "probe.library_metadata_missing");
            MetadataSaveResult saved = await metadata.SaveAsync(
                owner,
                new MetadataUpdate(
                    document.DisplayName,
                    authors[index],
                    series[index],
                    tags[index],
                    document.Version),
                cancellationToken);
            PackageProbeEnvironment.Require(
                saved.Succeeded,
                saved.ErrorCode ?? "probe.library_metadata_seed_failed");
        }
    }

    private static LibraryQuery StrictIntersectionQuery(TaxonomyTerms terms) =>
        LibraryQuery.Create(
            null,
            LibraryMediaFilter.All,
            [terms.AuthorOne.Id, terms.AuthorTwo.Id],
            null,
            [terms.TagOne.Id, terms.TagTwo.Id],
            false,
            true,
            false,
            LibraryDisplayNameFilter.All,
            LibrarySortField.ActualFilename,
            LibrarySortDirection.Ascending);

    private static LibraryBatchEditDraft CombinedDraft(
        string replacementAuthorName) =>
        LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Replace,
                new BatchMetadataSetEdit(
                    BatchMetadataFieldOperation.Replace,
                    [BatchTaxonomyReference.New(
                        TaxonomyKind.Author,
                        replacementAuthorName)]),
                BatchMetadataSeriesEdit.Unchanged,
                BatchMetadataSetEdit.Unchanged),
            NamingDefinition.Create(
                NamingSource.ActualFilename,
                [new MetadataTemplateNamingRule(
                    "{authors}-{number}",
                    "、",
                    1,
                    1,
                    2)]));

    private static async Task<LibraryAssetDetailsDto> GetDetailsAsync(
        ILibraryQueryService library,
        Guid assetId,
        CancellationToken cancellationToken) =>
        await library.GetDetailsAsync(assetId, cancellationToken)
        ?? throw new ProbeFailureException("probe.library_asset_missing");

    private static async Task<IReadOnlyList<PhysicalFileFact>>
        SnapshotPhysicalFilesAsync(
            string libraryDirectory,
            CancellationToken cancellationToken)
    {
        string internalPrefix = ".tracefold" + Path.DirectorySeparatorChar;
        var facts = new List<PhysicalFileFact>();
        foreach (string path in Directory.EnumerateFiles(
            libraryDirectory,
            "*",
            SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(libraryDirectory, path);
            if (relative.StartsWith(
                internalPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var info = new FileInfo(path);
            facts.Add(new PhysicalFileFact(
                relative,
                info.Length,
                await PackageProbeEnvironment.FileSha256Async(
                    path,
                    cancellationToken)));
        }

        return facts
            .OrderBy(fact => fact.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Task WriteFailureAsync(
        PackageProbeEnvironment environment,
        bool strictIntersectionPassed,
        bool atomicBatchPassed,
        bool namingPreviewMatchedApply,
        bool undoPassed,
        bool physicalFilesUnchanged,
        bool internalStorageClean,
        string errorCode) =>
        WriteResultAsync(
            environment,
            new Stage5BPackageProbeResult(
                false,
                strictIntersectionPassed,
                atomicBatchPassed,
                namingPreviewMatchedApply,
                undoPassed,
                physicalFilesUnchanged,
                internalStorageClean,
                errorCode),
            CancellationToken.None);

    private static async Task WriteResultAsync(
        PackageProbeEnvironment environment,
        Stage5BPackageProbeResult result,
        CancellationToken cancellationToken)
    {
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);
        await environment.WriteScenarioResultAsync(result, cancellationToken);
    }

    private sealed record TaxonomyTerms(
        TaxonomyTermDto AuthorOne,
        TaxonomyTermDto AuthorTwo,
        TaxonomyTermDto ReplacementAuthor,
        TaxonomyTermDto TagOne,
        TaxonomyTermDto TagTwo,
        TaxonomyTermDto Series);

    private sealed record MetadataFact(
        string? DisplayName,
        IReadOnlyList<Guid> AuthorIds,
        Guid? SeriesId,
        IReadOnlyList<Guid> TagIds)
    {
        public static MetadataFact From(MetadataDocument document) =>
            new(
                document.DisplayName,
                document.Authors.Select(term => term.Id).ToArray(),
                document.Series?.Id,
                document.Tags.Select(term => term.Id).ToArray());

        public bool Matches(MetadataDocument document) =>
            StringComparer.Ordinal.Equals(DisplayName, document.DisplayName)
            && AuthorIds.SequenceEqual(document.Authors.Select(term => term.Id))
            && SeriesId == document.Series?.Id
            && TagIds.SequenceEqual(document.Tags.Select(term => term.Id));
    }

    private sealed record PhysicalFileFact(
        string RelativePath,
        long Length,
        string Sha256);
}
