using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Logging;

public sealed class Stage2LoggingPrivacyTests
{
    [Fact]
    public async Task Failed_ingest_logs_operation_and_code_without_metadata_values()
    {
        const string secretDisplay = "SECRET-DISPLAY-NAME";
        const string secretAuthor = "SECRET-AUTHOR-NAME";
        const string secretSeries = "SECRET-SERIES-NAME";
        const string secretTag = "SECRET-TAG-NAME";
        string root = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-log-privacy-{Guid.NewGuid():N}");
        string inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;
        string library = Directory.CreateDirectory(Path.Combine(root, "library")).FullName;
        string work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        string data = Path.Combine(root, "data");
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero));
        var logs = new CapturingLoggerProvider();

        try
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddProvider(logs));
            services.AddSingleton<IHostApplicationLifetime, TestHostLifetime>();
            services.AddMediaApplication();
            services.AddMediaInfrastructure(data);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IControlledMediaStorage, StageFailingStorage>();
            await using ServiceProvider provider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

            await provider.GetRequiredService<IDatabaseInitializer>()
                .InitializeAsync(TestContext.Current.CancellationToken);
            DirectorySettingsResult saved = await provider
                .GetRequiredService<DirectorySettingsService>()
                .SaveAsync(
                    new LibraryFolders(inbox, library, work),
                    TestContext.Current.CancellationToken);
            Assert.True(saved.Succeeded);

            ITaxonomyService taxonomy =
                provider.GetRequiredService<ITaxonomyService>();
            TaxonomyTermDto author = await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                secretAuthor,
                TestContext.Current.CancellationToken);
            TaxonomyTermDto series = await taxonomy.CreateAsync(
                TaxonomyKind.Series,
                secretSeries,
                TestContext.Current.CancellationToken);
            TaxonomyTermDto tag = await taxonomy.CreateAsync(
                TaxonomyKind.Tag,
                secretTag,
                TestContext.Current.CancellationToken);

            string source = Path.Combine(inbox, "private-photo.png");
            await File.WriteAllBytesAsync(
                source,
                [137, 80, 78, 71, 13, 10, 26, 10],
                TestContext.Current.CancellationToken);
            IInboxScanner scanner = provider.GetRequiredService<IInboxScanner>();
            await scanner.ScanAsync(
                ScanTrigger.Manual,
                TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(2));
            await scanner.ScanAsync(
                ScanTrigger.StabilityCheck,
                TestContext.Current.CancellationToken);

            InboxCardDto card = Assert.Single(await provider
                .GetRequiredService<IInboxQueryService>()
                .SearchAsync(
                    new InboxQuery(null, null),
                    TestContext.Current.CancellationToken));
            MetadataSaveResult metadata = await provider
                .GetRequiredService<IMetadataEditorService>()
                .SaveAsync(
                    new MetadataOwner(MetadataOwnerKind.InboxItem, card.Id),
                    new MetadataUpdate(
                        secretDisplay,
                        [author.Id],
                        series.Id,
                        [tag.Id],
                        0),
                    TestContext.Current.CancellationToken);
            Assert.True(metadata.Succeeded);

            IngestResult result = await provider
                .GetRequiredService<IIngestService>()
                .IngestAsync(card.Id, TestContext.Current.CancellationToken);

            string captured = string.Join(Environment.NewLine, logs.Messages);
            Assert.Equal(IngestOutcome.Failed, result.Outcome);
            Assert.NotNull(result.OperationId);
            Assert.Contains(
                result.OperationId.Value.ToString(),
                captured,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ingest.stage_failed", captured, StringComparison.Ordinal);
            Assert.Contains("controlled stage failure", captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretDisplay, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretAuthor, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretSeries, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretTag, captured, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class StageFailingStorage : IControlledMediaStorage
    {
        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            string sourcePath,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            Task.FromException<StagedFile>(
                new IOException("controlled stage failure"));

        public Task<CommittedFile> CommitAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            string targetFilename,
            CancellationToken cancellationToken) => throw Unused();

        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
            LibraryFolders folders,
            string sourcePath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken) => throw Unused();

        public Task<bool> VerifyCommittedAsync(
            LibraryFolders folders,
            CommittedFile committed,
            CancellationToken cancellationToken) => throw Unused();

        public Task CleanupTemporaryFilesAsync(
            Guid operationId,
            LibraryFolders folders,
            CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() => new();
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow += value;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(Messages);

        public void Dispose()
        {
        }

        private sealed class Logger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                string rendered = formatter(state, exception);
                if (exception is not null)
                {
                    rendered += Environment.NewLine + exception;
                }

                messages.Add(rendered);
            }
        }
    }

    private sealed class TestHostLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
