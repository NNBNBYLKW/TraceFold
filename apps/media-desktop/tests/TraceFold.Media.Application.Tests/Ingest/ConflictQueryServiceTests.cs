using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class ConflictQueryServiceTests
{
    private const string HashA =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 24, 1, 2, 3, TimeSpan.Zero);
    private static readonly LibraryFolders Folders = new(
        "T:\\inbox",
        "T:\\library",
        "T:\\work");

    [Fact]
    public async Task Query_defaults_same_name_but_requires_selection_for_content_only()
    {
        Guid namedItemId = Guid.NewGuid();
        Guid contentOnlyItemId = Guid.NewGuid();
        ConflictCaseRecord named = Case(
            namedItemId,
            Match("same.jpg", ConflictReason.Name, isRegistered: true));
        ConflictCaseRecord contentOnly = Case(
            contentOnlyItemId,
            Match("copy.jpg", ConflictReason.Content, isRegistered: true));
        var query = CreateQuery(new QueryConflictStore(named, contentOnly));

        ConflictCaseDto namedDto = (await query.GetOpenAsync(
            namedItemId,
            TestContext.Current.CancellationToken))!;
        ConflictCaseDto contentOnlyDto = (await query.GetOpenAsync(
            contentOnlyItemId,
            TestContext.Current.CancellationToken))!;

        Assert.NotNull(namedDto.DefaultReplaceMatchId);
        Assert.Null(contentOnlyDto.DefaultReplaceMatchId);
        Assert.True(contentOnlyDto.CanReplace);
    }

    [Fact]
    public async Task Query_restores_persisted_choice_after_service_rebuild()
    {
        Guid itemId = Guid.NewGuid();
        ConflictMatchRecord selected = Match(
            "same.jpg",
            ConflictReason.Name,
            isRegistered: true);
        ConflictCaseRecord chosen = Case(itemId, selected) with
        {
            Status = ConflictCaseStatus.Chosen,
            Resolution = ConflictResolution.Replace,
            SelectedMatchId = selected.Id
        };
        var store = new QueryConflictStore(chosen);

        var rebuiltQuery = CreateQuery(store);
        ConflictCaseDto result = (await rebuiltQuery.GetOpenAsync(
            itemId,
            TestContext.Current.CancellationToken))!;

        Assert.Equal(
            ConflictResolution.Replace,
            result.PersistedResolution);
        Assert.Equal(selected.Id, result.PersistedSelectedMatchId);
    }

    [Fact]
    public async Task Query_projects_latest_incoming_metadata_and_trusted_previews()
    {
        Guid itemId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        ConflictMatchRecord registered = Match(
            "same.jpg",
            ConflictReason.Name,
            isRegistered: true,
            assetId: assetId);
        ConflictCaseRecord conflict = Case(itemId, registered);
        var preview = new QueryPreviewResolver();
        var metadata = new QueryMetadataStore(itemId);
        var query = CreateQuery(
            new QueryConflictStore(conflict),
            metadata: metadata,
            preview: preview);

        ConflictCaseDto result = (await query.GetOpenAsync(
            itemId,
            TestContext.Current.CancellationToken))!;

        Assert.Equal("edited title", result.Incoming.DisplayName);
        Assert.Equal("author", Assert.Single(result.Incoming.Metadata.Authors));
        Assert.NotNull(result.Incoming.Preview);
        Assert.NotNull(Assert.Single(result.Matches).Preview);
        Assert.Contains(
            new MetadataOwner(MetadataOwnerKind.InboxItem, itemId),
            preview.Requests);
        Assert.Contains(
            new MetadataOwner(MetadataOwnerKind.MediaAsset, assetId),
            preview.Requests);
    }

    [Fact]
    public async Task Query_builds_unmanaged_preview_only_for_the_persisted_identity()
    {
        Guid itemId = Guid.NewGuid();
        ConflictMatchRecord unmanaged = Match(
            "same.jpg",
            ConflictReason.Name,
            isRegistered: false);
        ConflictCaseRecord conflict = Case(itemId, unmanaged);
        var storage = new QueryStorage(Snapshot("same.jpg", HashB));
        var query = CreateQuery(
            new QueryConflictStore(conflict),
            storage: storage,
            preview: new QueryPreviewResolver(throwNotRegistered: true));

        ConflictCaseDto exact = (await query.GetOpenAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        storage.Value = Snapshot("same.jpg", HashA);
        ConflictCaseDto changed = (await query.GetOpenAsync(
            itemId,
            TestContext.Current.CancellationToken))!;

        Assert.NotNull(Assert.Single(exact.Matches).Preview);
        Assert.Null(Assert.Single(changed.Matches).Preview);
    }

    [Fact]
    public async Task Query_projects_incoming_probe_and_persisted_match_technical_data()
    {
        Guid itemId = Guid.NewGuid();
        var persisted = new ConflictTechnicalMetadata(
            "mp4",
            null,
            null,
            TimeSpan.FromSeconds(5));
        ConflictMatchRecord match = Match(
            "same.MP4",
            ConflictReason.Name,
            isRegistered: true) with
        {
            MediaKind = MediaKind.Video,
            Technical = persisted
        };
        ConflictCaseRecord conflict = Case(itemId, match) with
        {
            Input = Input("same.MP4")
        };
        var query = CreateQuery(
            new QueryConflictStore(conflict),
            tool: new QueryProbeTool(TimeSpan.FromSeconds(12)));

        ConflictCaseDto result = (await query.GetOpenAsync(
            itemId,
            TestContext.Current.CancellationToken))!;

        Assert.Equal("mp4", result.Incoming.Technical.Format);
        Assert.Equal(
            TimeSpan.FromSeconds(12),
            result.Incoming.Technical.Duration);
        Assert.Equal(persisted, Assert.Single(result.Matches).Technical);
    }

    [Fact]
    public async Task Preview_resolution_failure_does_not_hide_the_conflict()
    {
        Guid itemId = Guid.NewGuid();
        ConflictCaseRecord conflict = Case(
            itemId,
            Match("same.jpg", ConflictReason.Name, isRegistered: true));
        var query = CreateQuery(
            new QueryConflictStore(conflict),
            preview: new QueryPreviewResolver(throwNotRegistered: true));

        ConflictCaseDto result = (await query.GetOpenAsync(
            itemId,
            TestContext.Current.CancellationToken))!;

        Assert.Null(result.Incoming.Preview);
        Assert.Null(Assert.Single(result.Matches).Preview);
    }

    private static ConflictQueryService CreateQuery(
        IConflictCaseStore store,
        QueryStorage? storage = null,
        IMetadataStore? metadata = null,
        IMediaPreviewResolver? preview = null,
        IMediaTool? tool = null) =>
        new(
            store,
            storage ?? new QueryStorage(),
            new QuerySettingsStore(),
            metadata ?? new QueryMetadataStore(),
            preview ?? new QueryPreviewResolver(),
            tool);

    private static ConflictCaseRecord Case(
        Guid inboxItemId,
        params ConflictMatchRecord[] matches) =>
        new(
            Guid.NewGuid(),
            inboxItemId,
            ConflictCaseStatus.Open,
            Input("same.jpg"),
            null,
            null,
            null,
            matches);

    private static ConflictInputSnapshot Input(string filename) =>
        new(
            $"T:\\inbox\\{filename}",
            3,
            ObservedAt,
            HashA,
            filename,
            1);

    private static ConflictMatchRecord Match(
        string filename,
        ConflictReason reason,
        bool isRegistered,
        Guid? assetId = null) =>
        new(
            Guid.NewGuid(),
            reason,
            isRegistered,
            isRegistered ? assetId ?? Guid.NewGuid() : null,
            isRegistered ? 1 : null,
            filename,
            filename,
            null,
            Path.GetExtension(filename).Equals(
                ".mp4",
                StringComparison.OrdinalIgnoreCase)
                ? MediaKind.Video
                : MediaKind.Image,
            3,
            ObservedAt,
            HashB,
            new ConflictMetadataSnapshot(null, [], null, []),
            new ConflictTechnicalMetadata(
                Path.GetExtension(filename).TrimStart('.').ToLowerInvariant(),
                null,
                null,
                null));

    private static LibraryFileSnapshot Snapshot(string filename, string hash) =>
        new(
            $"T:\\library\\{filename}",
            filename,
            3,
            ObservedAt,
            hash);

    private sealed class QueryConflictStore(
        params ConflictCaseRecord[] conflicts) : IConflictCaseStore
    {
        private readonly IReadOnlyDictionary<Guid, ConflictCaseRecord> _conflicts =
            conflicts.ToDictionary(conflict => conflict.InboxItemId);

        public Task<ConflictCaseRecord?> LoadOpenForInboxAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_conflicts.GetValueOrDefault(inboxItemId));

        public Task<IReadOnlyList<RegisteredConflictCandidate>>
            FindRegisteredCandidatesAsync(
                string targetFilename,
                string sha256,
                CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RegisteredConflictCandidate>>([]);

        public Task<ConflictCaseRecord> ReplaceOpenAsync(
            Guid inboxItemId,
            ConflictInputSnapshot input,
            IReadOnlyList<ConflictMatchRecord> matches,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConflictCaseRecord?> LoadAsync(
            Guid conflictCaseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConflictCaseRecord?>(null);

        public Task ChooseAsync(
            Guid conflictCaseId,
            ConflictResolution resolution,
            Guid? matchId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SkipAsync(
            Guid conflictCaseId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateAsync(
            Guid conflictCaseId,
            string reason,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class QueryStorage(
        LibraryFileSnapshot? value = null) : IControlledMediaStorage
    {
        public LibraryFileSnapshot? Value { get; set; } = value;

        public Task<LibraryFileSnapshot?> InspectTargetAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) =>
            Task.FromResult(Value is not null
                && StringComparer.OrdinalIgnoreCase.Equals(
                    Value.RelativePath,
                    targetFilename)
                    ? Value
                    : null);

        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) =>
            Task.FromResult(Value is not null);

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            string sourcePath,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CommittedFile> CommitAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            string targetFilename,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
            LibraryFolders folders,
            string sourcePath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> VerifyCommittedAsync(
            LibraryFolders folders,
            CommittedFile committed,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CleanupTemporaryFilesAsync(
            Guid operationId,
            LibraryFolders folders,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class QuerySettingsStore : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(Folders);

        public Task SaveAsync(
            LibraryFolders folders,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class QueryMetadataStore : IMetadataStore
    {
        private readonly MetadataDocument? _document;

        public QueryMetadataStore(Guid? itemId = null)
        {
            if (itemId is null)
            {
                return;
            }

            _document = new MetadataDocument(
                new MetadataOwner(MetadataOwnerKind.InboxItem, itemId.Value),
                "edited title",
                [Term(TaxonomyKind.Author, "author")],
                null,
                [],
                9);
        }

        public Task<MetadataDocument?> LoadAsync(
            MetadataOwner owner,
            CancellationToken cancellationToken) =>
            Task.FromResult(_document is not null && _document.Owner == owner
                ? _document
                : null);

        public Task<MetadataSaveResult> TrySaveAsync(
            MetadataOwner owner,
            MetadataSelection selection,
            long expectedVersion,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static TaxonomyTermDto Term(
            TaxonomyKind kind,
            string name) =>
            new(Guid.NewGuid(), kind, name, name.ToUpperInvariant(), 1);
    }

    private sealed class QueryPreviewResolver(
        bool throwNotRegistered = false) : IMediaPreviewResolver
    {
        public List<MetadataOwner> Requests { get; } = [];

        public Task<PreviewDescriptor> ResolveAsync(
            MetadataOwner owner,
            CancellationToken cancellationToken)
        {
            Requests.Add(owner);
            return throwNotRegistered
                ? Task.FromException<PreviewDescriptor>(
                    new PreviewNotRegisteredException())
                : Task.FromResult(new PreviewDescriptor(
                    MediaKind.Image,
                    $"T:\\preview\\{owner.Id:N}.jpg",
                    true));
        }
    }

    private sealed class QueryProbeTool(TimeSpan duration) : IMediaTool
    {
        public Task<MediaToolAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaToolAvailability(true, "test", null));

        public Task<MediaProbeResult> ProbeAsync(
            string trustedPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaProbeResult(
                duration,
                [new MediaStreamInfo("video", duration)]));

        public Task<FfmpegResult> RunFfmpegAsync(
            FfmpegRequest request,
            IProgress<int> progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
