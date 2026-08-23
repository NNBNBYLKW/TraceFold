using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Logging;

public sealed class Stage3LoggingPrivacyTests
{
    [Fact]
    public async Task Failed_processing_logs_operational_context_without_metadata_or_binary()
    {
        const string secretSource = "SOURCE-SENTINEL";
        const string secretDisplay = "DISPLAY-SENTINEL";
        const string secretAuthor = "AUTHOR-SENTINEL";
        const string secretSeries = "SERIES-SENTINEL";
        const string secretTag = "TAG-SENTINEL";
        const string binarySentinel = "BINARY-SENTINEL";
        const string toolVersion = "ffmpeg version test-pin";
        string root = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-stage3-log-privacy-{Guid.NewGuid():N}");
        string inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;
        string library = Directory.CreateDirectory(Path.Combine(root, "library")).FullName;
        string work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        string data = Path.Combine(root, "data");
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero));
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
            services.AddSingleton<IMediaTool>(
                new FailingMediaTool(toolVersion, new string('D', 64 * 1024)));
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

            byte[] content = Encoding.UTF8.GetBytes(binarySentinel);
            await File.WriteAllBytesAsync(
                Path.Combine(inbox, $"{secretSource}.mp4"),
                content,
                TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(inbox, $"{secretSource}.m4a"),
                content,
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

            ProcessingCommandResult queued = await provider
                .GetRequiredService<IProcessingCommandService>()
                .StartAsync(card.Id, TestContext.Current.CancellationToken);
            Assert.Equal(ProcessingCommandOutcome.Queued, queued.Outcome);
            ProcessingJobRecord active = Assert.IsType<ProcessingJobRecord>(
                await provider.GetRequiredService<IProcessingStore>()
                    .FindActiveAsync(card.Id, TestContext.Current.CancellationToken));
            Assert.True(await provider.GetRequiredService<IProcessingJobExecutor>()
                .ExecuteNextAsync(TestContext.Current.CancellationToken));

            string captured = string.Join(Environment.NewLine, logs.Messages);
            Assert.NotNull(queued.JobId);
            Assert.Contains(active.CorrelationId, captured,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("mp4_m4a_merge", captured, StringComparison.Ordinal);
            Assert.Contains(toolVersion, captured, StringComparison.Ordinal);
            Assert.Contains("processing.ffmpeg_failed", captured,
                StringComparison.Ordinal);
            Assert.Contains(new string('D', 1024), captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretSource, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretDisplay, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretAuthor, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretSeries, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretTag, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(binarySentinel, captured, StringComparison.Ordinal);
            int longestDiagnosticRun = Regex.Matches(captured, "D+")
                .Select(match => match.Length)
                .DefaultIfEmpty()
                .Max();
            Assert.InRange(longestDiagnosticRun, 1, 32 * 1024);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FailingMediaTool(string version, string diagnostics)
        : IMediaTool
    {
        public Task<MediaToolAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaToolAvailability(true, version, null));

        public Task<MediaProbeResult> ProbeAsync(
            string trustedPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaProbeResult(
                TimeSpan.FromSeconds(1),
                [new MediaStreamInfo("video", null), new MediaStreamInfo("audio", null)]));

        public Task<FfmpegResult> RunFfmpegAsync(
            FfmpegRequest request,
            IProgress<int> progress,
            CancellationToken cancellationToken) =>
            Task.FromException<FfmpegResult>(new MediaToolException(
                "processing.ffmpeg_failed",
                diagnostics));
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
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
                messages.Add(formatter(state, exception));
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
