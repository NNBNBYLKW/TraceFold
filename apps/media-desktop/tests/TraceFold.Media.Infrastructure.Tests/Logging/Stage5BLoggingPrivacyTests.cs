using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Logging;

public sealed class Stage5BLoggingPrivacyTests
{
    [Fact]
    public async Task Batch_logs_operational_counts_without_names_or_rule_text()
    {
        const string filenameMarker = "STAGE5B-PRIVATE-FILENAME";
        const string authorMarker = "STAGE5B-PRIVATE-AUTHOR";
        const string ruleMarker = "STAGE5B-PRIVATE-RULE";
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid assetId = await database.SeedFormalAssetAsync(
            $"{filenameMarker}.jpg",
            null,
            0);
        LibrarySelectionEntry[] selection = [new(assetId, 0, 0)];
        var logger = new CapturingLogger<LibraryBatchEditorService>();
        var service = new LibraryBatchEditorService(
            new EfLibraryBatchEditStore(
                database.CreateContextFactory(),
                new NamingRuleEngine()),
            new LibraryBatchPreviewService(new NamingRuleEngine()),
            new LibraryActivityNotifier(),
            logger);
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Add,
                new(
                    BatchMetadataFieldOperation.Add,
                    [BatchTaxonomyReference.New(
                        TaxonomyKind.Author,
                        authorMarker)]),
                BatchMetadataSeriesEdit.Unchanged,
                BatchMetadataSetEdit.Unchanged),
            NamingDefinition.Create(
                NamingSource.ActualFilename,
                [new PrefixNamingRule(ruleMarker)]));

        LibraryBatchApplyResult result = await service.ApplyAsync(
            selection,
            draft,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        string captured = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("Library batch apply", captured, StringComparison.Ordinal);
        Assert.Contains("SelectedCount 1", captured, StringComparison.Ordinal);
        Assert.Contains("prefix", captured, StringComparison.Ordinal);
        Assert.DoesNotContain(filenameMarker, captured, StringComparison.Ordinal);
        Assert.DoesNotContain(authorMarker, captured, StringComparison.Ordinal);
        Assert.DoesNotContain(ruleMarker, captured, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, null));
    }
}
