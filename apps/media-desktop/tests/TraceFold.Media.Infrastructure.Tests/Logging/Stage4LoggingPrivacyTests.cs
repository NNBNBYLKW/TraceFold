using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Logging;

public sealed class Stage4LoggingPrivacyTests
{
    [Fact]
    public async Task Conflict_failure_keeps_ids_and_code_without_private_evidence()
    {
        const string pathMarker = "STAGE4-PRIVATE-PATH";
        const string filenameMarker = "STAGE4-PRIVATE-FILENAME";
        const string displayMarker = "STAGE4-PRIVATE-DISPLAY";
        const string authorMarker = "STAGE4-PRIVATE-AUTHOR";
        const string seriesMarker = "STAGE4-PRIVATE-SERIES";
        const string tagMarker = "STAGE4-PRIVATE-TAG";
        const string hashMarker =
            "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        const string probeMarker = "STAGE4-PRIVATE-PROBE-TOKEN";
        string privateEvidence = string.Join(
            '|',
            pathMarker,
            filenameMarker,
            displayMarker,
            authorMarker,
            seriesMarker,
            tagMarker,
            hashMarker,
            probeMarker);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-stage4-log-privacy-{Guid.NewGuid():N}");
        string inbox = Directory.CreateDirectory(
            Path.Combine(root, pathMarker, "inbox")).FullName;
        string library = Directory.CreateDirectory(
            Path.Combine(root, "library")).FullName;
        string work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        string data = Path.Combine(root, "data");
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var logs = new CapturingLoggerProvider();

        try
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder
                .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning)
                .AddProvider(logs));
            services.AddSingleton<IHostApplicationLifetime, TestHostLifetime>();
            services.AddMediaApplication();
            services.AddMediaInfrastructure(data);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IConflictDetectionService>(
                new MarkerThrowingConflictDetector(privateEvidence));
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

            ITaxonomyService taxonomy = provider.GetRequiredService<ITaxonomyService>();
            TaxonomyTermDto author = await taxonomy.CreateAsync(
                TaxonomyKind.Author,
                authorMarker,
                TestContext.Current.CancellationToken);
            TaxonomyTermDto series = await taxonomy.CreateAsync(
                TaxonomyKind.Series,
                seriesMarker,
                TestContext.Current.CancellationToken);
            TaxonomyTermDto tag = await taxonomy.CreateAsync(
                TaxonomyKind.Tag,
                tagMarker,
                TestContext.Current.CancellationToken);

            string source = Path.Combine(inbox, $"{filenameMarker}.jpg");
            await File.WriteAllBytesAsync(
                source,
                [255, 216, 255, 217],
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
                        displayMarker,
                        [author.Id],
                        series.Id,
                        [tag.Id],
                        0),
                    TestContext.Current.CancellationToken);
            Assert.True(metadata.Succeeded);

            IngestResult result = await provider.GetRequiredService<IIngestService>()
                .IngestAsync(card.Id, TestContext.Current.CancellationToken);

            string captured = string.Join(Environment.NewLine, logs.Messages);
            Assert.Equal(IngestOutcome.Failed, result.Outcome);
            Assert.Contains(card.Id.ToString(), captured,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ingest.commit_failed", captured, StringComparison.Ordinal);
            foreach (string marker in new[]
            {
                pathMarker,
                filenameMarker,
                displayMarker,
                authorMarker,
                seriesMarker,
                tagMarker,
                hashMarker,
                probeMarker
            })
            {
                Assert.DoesNotContain(marker, captured, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class MarkerThrowingConflictDetector(string evidence)
        : IConflictDetectionService
    {
        public Task<ConflictCaseRecord?> DetectAsync(
            IngestPreparation preparation,
            LibraryFolders folders,
            CancellationToken cancellationToken) =>
            Task.FromException<ConflictCaseRecord?>(new IOException(evidence));

        public Task<ConflictRevalidationResult> RevalidateAsync(
            ConflictCaseRecord conflict,
            IngestPreparation current,
            LibraryFolders folders,
            CancellationToken cancellationToken) =>
            Task.FromException<ConflictRevalidationResult>(new IOException(evidence));
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private DateTimeOffset now = utcNow;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan value) => now += value;
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
