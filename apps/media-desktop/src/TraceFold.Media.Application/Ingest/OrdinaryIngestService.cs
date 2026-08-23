using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Ingest;

// Transitional Stage 2 adapter. Composition switches to IIngestService in Task 10.
public sealed class OrdinaryIngestService : IngestService, IOrdinaryIngestService
{
    public OrdinaryIngestService(
        IInboxScanner scanner,
        IIngestStore store,
        IDirectorySettingsStore settingsStore,
        IFileHasher hasher,
        IControlledMediaStorage storage,
        IMediaOperationCoordinator coordinator,
        ILogger<OrdinaryIngestService> logger)
        : base(
            scanner,
            store,
            settingsStore,
            new OrdinaryInputResolver(hasher),
            storage,
            new IngestCleanupService(store, storage),
            coordinator,
            logger)
    {
    }

    private sealed class OrdinaryInputResolver(IFileHasher hasher)
        : IIngestInputResolver
    {
        public async Task<PreparedIngestInput> ResolveAsync(
            IngestCandidate candidate,
            CancellationToken cancellationToken)
        {
            if (candidate.State != InboxItemState.ReadyToTag
                || candidate.Sources.Count != 1
                || !candidate.Sources[0].IsPrimary
                || !candidate.Sources[0].IsPresent)
            {
                throw new IngestInputException("ingest.not_ready");
            }

            IngestSourceRecord source = candidate.Sources[0];
            if (!Matches(source))
            {
                throw new IngestInputException(
                    File.Exists(source.FullPath)
                        ? "ingest.source_changed"
                        : "ingest.source_missing");
            }

            string sha256 = await hasher.ComputeSha256Async(
                source.FullPath,
                cancellationToken);
            if (!Matches(source))
            {
                throw new IngestInputException("ingest.source_changed");
            }

            var cleanup = new IngestCleanupFile(
                IngestCleanupKind.InboxSource,
                source.FullPath,
                source.Size,
                sha256.ToUpperInvariant());
            return new PreparedIngestInput(
                cleanup.Kind,
                source.FullPath,
                source.Size,
                source.LastWriteTimeUtc,
                cleanup.Sha256,
                [cleanup]);
        }

        private static bool Matches(IngestSourceRecord source)
        {
            var info = new FileInfo(source.FullPath);
            return info.Exists
                && info.Length == source.Size
                && new DateTimeOffset(info.LastWriteTimeUtc)
                    == source.LastWriteTimeUtc;
        }
    }
}
