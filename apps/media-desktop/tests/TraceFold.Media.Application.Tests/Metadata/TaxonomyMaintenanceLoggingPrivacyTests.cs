using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class TaxonomyMaintenanceLoggingPrivacyTests
{
    [Fact]
    public async Task Logs_include_safe_operation_facts_and_exclude_private_values()
    {
        const string targetSentinel = "PRIVATE_TARGET_8E3A";
        const string sourceSentinel = "PRIVATE_SOURCE_71B4";
        const string fileSentinel = @"T:\PRIVATE\FILE_64C2.mp4";
        TaxonomyMergePreparation preparation =
            TaxonomyMaintenanceServiceTests.Preparation(
                targetSentinel,
                sourceSentinel,
                fileSentinel);
        var receipt = new TaxonomyMergeUndoReceipt(
            preparation.OperationId,
            preparation.Target,
            TraceFold.Media.Domain.Metadata.TaxonomyName.Create(targetSentinel),
            preparation.Sources,
            []);
        var store = new TaxonomyMaintenanceServiceTests.RecordingMaintenanceStore
        {
            MergeResult = TaxonomyMaintenanceServiceTests.Success(receipt)
                with
                {
                    Succeeded = false,
                    UndoReceipt = null,
                    ErrorCode = TaxonomyMaintenanceErrorCodes.PersistenceFailed,
                }
        };
        var logger = new RecordingLogger<TaxonomyMaintenanceService>();
        var service = new TaxonomyMaintenanceService(
            store,
            new SilentInboxNotifier(),
            new SilentLibraryNotifier(),
            logger);

        await service.ApplyMergeAsync(
            preparation,
            finalNameOverride: null,
            CancellationToken.None);

        string logs = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains(preparation.OperationId.ToString(), logs, StringComparison.Ordinal);
        Assert.Contains("Author", logs, StringComparison.Ordinal);
        Assert.Contains(
            TaxonomyMaintenanceErrorCodes.PersistenceFailed,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(targetSentinel, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(sourceSentinel, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(fileSentinel, logs, StringComparison.Ordinal);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
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
            Messages.Add(formatter(state, exception));
    }

    private sealed class SilentInboxNotifier : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void Publish(InboxActivityKind kind)
        {
        }

        public Task WaitForCandidateAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class SilentLibraryNotifier : ILibraryActivityNotifier
    {
        public event EventHandler<LibraryActivityEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void Publish(
            LibraryActivityKind kind,
            IReadOnlyList<Guid> assetIds)
        {
        }
    }
}
