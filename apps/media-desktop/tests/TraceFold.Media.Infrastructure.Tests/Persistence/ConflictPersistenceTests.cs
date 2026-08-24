using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class ConflictPersistenceTests
{
    private static readonly DateTimeOffset Instant =
        DateTimeOffset.Parse("2026-08-23T10:00:00Z");
    private static readonly string HashA = new('A', 64);
    private static readonly string HashB = new('B', 64);

    [Fact]
    public async Task FindRegisteredCandidates_returns_same_name_first_and_every_hash_candidate()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.SeedTaxonomyAsync();
        Guid sameId = await fixture.InsertAssetAsync(
            "same.jpg", HashA, "same", includeMetadata: true);
        await fixture.InsertAssetAsync("copy-1.jpg", HashA, "copy 1");
        await fixture.InsertAssetAsync("copy-2.jpg", HashA, "copy 2");

        IReadOnlyList<RegisteredConflictCandidate> matches = await fixture.Store
            .FindRegisteredCandidatesAsync("same.jpg", HashA, fixture.Token);

        Assert.Equal(3, matches.Count);
        Assert.Equal(sameId, matches[0].AssetId);
        Assert.True(matches[0].DatabaseReason.HasFlag(ConflictReason.Name));
        Assert.All(matches, item =>
            Assert.True(item.DatabaseReason.HasFlag(ConflictReason.Content)));
        Assert.Equal(["Alice", "Bob"], matches[0].Metadata.Authors);
        Assert.Equal("Series One", matches[0].Metadata.Series);
        Assert.Equal(["Nature", "Night"], matches[0].Metadata.Tags);
    }

    [Fact]
    public async Task ReplaceOpen_invalidates_old_case_and_keeps_one_active_case()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ConflictCaseRecord first = await fixture.SaveCaseAsync(HashA);

        ConflictCaseRecord second = await fixture.SaveCaseAsync(HashB);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(
            ConflictCaseStatus.Invalidated,
            (await fixture.Store.LoadAsync(first.Id, fixture.Token))!.Status);
        Assert.Equal(
            second.Id,
            (await fixture.Store.LoadOpenForInboxAsync(
                fixture.ItemId,
                fixture.Token))!.Id);
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM conflict_cases WHERE active_inbox_item_id IS NOT NULL"));
    }

    [Fact]
    public async Task Skip_returns_item_to_ready_and_preserves_sources()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ConflictCaseRecord conflict = await fixture.SaveCaseAsync(HashA);

        await fixture.Store.SkipAsync(conflict.Id, fixture.Token);

        Assert.Equal(
            InboxItemState.ReadyToTag.ToString().ToLowerInvariant(),
            await fixture.Database.ReadInboxStateAsync(fixture.ItemId));
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM source_files"));
        Assert.Equal(
            ConflictCaseStatus.Skipped,
            (await fixture.Store.LoadAsync(conflict.Id, fixture.Token))!.Status);
        Assert.Null(await fixture.Store.LoadOpenForInboxAsync(
            fixture.ItemId,
            fixture.Token));
    }

    [Fact]
    public async Task Chosen_single_target_survives_a_new_store_instance()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ConflictCaseRecord conflict = await fixture.SaveCaseAsync(HashA);
        Guid selectedMatchId = conflict.Matches[1].Id;
        await fixture.Store.ChooseAsync(
            conflict.Id,
            ConflictResolution.Replace,
            selectedMatchId,
            fixture.Token);

        var reloadedStore = new EfConflictCaseStore(
            fixture.Database.CreateContextFactory(),
            new FixedTimeProvider(Instant.AddMinutes(1)));
        ConflictCaseRecord? reloaded = await reloadedStore.LoadOpenForInboxAsync(
            fixture.ItemId,
            fixture.Token);

        Assert.NotNull(reloaded);
        Assert.Equal(ConflictCaseStatus.Chosen, reloaded.Status);
        Assert.Equal(ConflictResolution.Replace, reloaded.Resolution);
        Assert.Equal(selectedMatchId, reloaded.SelectedMatchId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reloadedStore.ChooseAsync(
                conflict.Id,
                ConflictResolution.Replace,
                Guid.NewGuid(),
                fixture.Token));
    }

    [Fact]
    public async Task Invalidate_records_reason_and_returns_item_to_ready()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ConflictCaseRecord conflict = await fixture.SaveCaseAsync(HashA);

        await fixture.Store.InvalidateAsync(
            conflict.Id,
            "conflict.target_hash_changed",
            fixture.Token);

        ConflictCaseRecord persisted =
            (await fixture.Store.LoadAsync(conflict.Id, fixture.Token))!;
        Assert.Equal(ConflictCaseStatus.Invalidated, persisted.Status);
        Assert.Equal("conflict.target_hash_changed", persisted.InvalidationReason);
        Assert.Equal(
            InboxItemState.ReadyToTag.ToString().ToLowerInvariant(),
            await fixture.Database.ReadInboxStateAsync(fixture.ItemId));
    }

    [Fact]
    public async Task Unknown_case_is_rejected_without_changing_item_state()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.SkipAsync(Guid.NewGuid(), fixture.Token));

        Assert.Equal(
            InboxItemState.ReadyToTag.ToString().ToLowerInvariant(),
            await fixture.Database.ReadInboxStateAsync(fixture.ItemId));
    }

    [Fact]
    public async Task Concurrent_replace_open_calls_leave_one_active_case()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        await Task.WhenAll(
            fixture.SaveCaseAsync(HashA),
            fixture.SaveCaseAsync(HashB));

        Assert.Equal(2, await fixture.Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM conflict_cases"));
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM conflict_cases WHERE active_inbox_item_id IS NOT NULL"));
        Assert.Equal(1, await fixture.Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM conflict_cases WHERE status = 'invalidated'"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Guid _author1 = Guid.NewGuid();
        private readonly Guid _author2 = Guid.NewGuid();
        private readonly Guid _series = Guid.NewGuid();
        private readonly Guid _tag1 = Guid.NewGuid();
        private readonly Guid _tag2 = Guid.NewGuid();

        private Fixture(TestMediaDatabase database, Guid itemId)
        {
            Database = database;
            ItemId = itemId;
            Store = new EfConflictCaseStore(
                database.CreateContextFactory(),
                new FixedTimeProvider(Instant));
        }

        public TestMediaDatabase Database { get; }

        public Guid ItemId { get; }

        public EfConflictCaseStore Store { get; }

        public CancellationToken Token => TestContext.Current.CancellationToken;

        public static async Task<Fixture> CreateAsync()
        {
            TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
            await database.MigrateAsync();
            Guid itemId = Guid.NewGuid();
            Guid sourceId = Guid.NewGuid();
            string itemKey = itemId.ToString().ToUpperInvariant();
            string sourceKey = sourceId.ToString().ToUpperInvariant();
            await database.ExecuteSqlAsync(
                $"""
                INSERT INTO inbox_items
                    (id, state, media_kind, actual_filename, grouping_rule,
                     is_retryable, created_at_utc, updated_at_utc)
                VALUES
                    ('{itemKey}', 'readytotag', 'image', 'same.jpg',
                     'direct_media', 0, '{Instant:O}', '{Instant:O}')
                """,
                $"""
                INSERT INTO source_files
                    (id, inbox_item_id, normalized_path, active_path_key,
                     filename, extension, size, last_write_time_utc,
                     first_observed_at_utc, stable_observation_count,
                     sha256, is_present, is_primary)
                VALUES
                    ('{sourceKey}', '{itemKey}', 'T:/INBOX/SAME.JPG',
                     'T:/INBOX/SAME.JPG', 'same.jpg', '.jpg', 3,
                     '{Instant:O}', '{Instant:O}', 2, '{HashA}', 1, 1)
                """);
            return new Fixture(database, itemId);
        }

        public async Task SeedTaxonomyAsync()
        {
            await Database.ExecuteSqlAsync(
                TaxonomySql(_author1, "author", "Alice", "ALICE"),
                TaxonomySql(_author2, "author", "Bob", "BOB"),
                TaxonomySql(_series, "series", "Series One", "SERIES ONE"),
                TaxonomySql(_tag1, "tag", "Nature", "NATURE"),
                TaxonomySql(_tag2, "tag", "Night", "NIGHT"));
        }

        public async Task<Guid> InsertAssetAsync(
            string filename,
            string hash,
            string display,
            bool includeMetadata = false)
        {
            Guid assetId = Guid.NewGuid();
            await Database.ExecuteSqlAsync($"""
                INSERT INTO media_assets
                    (id, media_kind, actual_filename, relative_path,
                     normalized_relative_path, display_name, size, sha256,
                     series_id, metadata_version, created_at_utc, updated_at_utc)
                VALUES
                    ('{assetId}', 'image', '{filename}', '{filename}',
                     '{filename.ToUpperInvariant()}', '{display}', 3, '{hash}',
                     {(includeMetadata ? $"'{_series}'" : "NULL")}, 4,
                     '{Instant:O}', '{Instant:O}')
                """);
            if (includeMetadata)
            {
                await Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO media_asset_authors
                        (media_asset_id, author_id, ordinal)
                    VALUES ('{assetId}', '{_author1}', 0),
                           ('{assetId}', '{_author2}', 1)
                    """,
                    $"""
                    INSERT INTO media_asset_tags
                        (media_asset_id, tag_id, ordinal)
                    VALUES ('{assetId}', '{_tag1}', 0),
                           ('{assetId}', '{_tag2}', 1)
                    """);
            }

            return assetId;
        }

        public Task<ConflictCaseRecord> SaveCaseAsync(string inputHash)
        {
            ConflictMatchRecord[] matches =
            [
                Match(ConflictReason.Name, "same.jpg", HashB),
                Match(ConflictReason.Content, "copy.jpg", inputHash)
            ];
            return Store.ReplaceOpenAsync(
                ItemId,
                new ConflictInputSnapshot(
                    "T:/inbox/same.jpg",
                    3,
                    Instant,
                    inputHash,
                    "same.jpg",
                    2),
                matches,
                Token);
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();

        private static ConflictMatchRecord Match(
            ConflictReason reason,
            string filename,
            string hash) =>
            new(
                Guid.NewGuid(),
                reason,
                false,
                null,
                null,
                filename,
                filename,
                null,
                MediaKind.Image,
                3,
                Instant,
                hash,
                new ConflictMetadataSnapshot(null, [], null, []),
                new ConflictTechnicalMetadata("jpg", 1, 1, null));

        private static string TaxonomySql(
            Guid id,
            string kind,
            string display,
            string normalized) => $"""
            INSERT INTO taxonomy_terms
                (id, kind, display_name, normalized_name,
                 created_at_utc, updated_at_utc)
            VALUES
                ('{id}', '{kind}', '{display}', '{normalized}',
                 '{Instant:O}', '{Instant:O}')
            """;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
