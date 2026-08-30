using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Diagnostics;
using Xunit;

namespace TraceFold.Media.Application.Tests.Diagnostics;

public sealed class SystemDiagnosticsLoggingPrivacyTests
{
    [Fact]
    public async Task Logs_include_safe_probe_facts_and_exclude_exception_message()
    {
        const string pathSentinel = @"C:\Users\PRIVATE\movie.mp4";
        const string metadataSentinel = "PRIVATE_AUTHOR_SERIES_TAG";
        const string idSentinel = "2fd685e7-0852-458a-802f-029ca89210b4";
        var logger = new RecordingLogger<SystemDiagnosticsService>();
        ISystemDiagnosticProbe probe = SystemDiagnosticsServiceTests.Probe(
            SystemDiagnosticsServiceTests.Descriptor(
                "database",
                SystemDiagnosticCategory.Database),
            _ => throw new InvalidOperationException(
                $"{pathSentinel}|{metadataSentinel}|{idSentinel}"));
        var service = new SystemDiagnosticsService(
            [probe],
            SystemDiagnosticsOptions.Production,
            TimeProvider.System,
            logger);

        SystemDiagnosticsSnapshot result = await service.CheckAsync(
            TestContext.Current.CancellationToken);

        string logs = string.Join(Environment.NewLine, logger.Messages);
        Assert.Equal(
            "diagnostics.probe.failed",
            Assert.Single(result.Items).ErrorCode);
        Assert.Contains("database", logs, StringComparison.Ordinal);
        Assert.Contains("diagnostics.probe.failed", logs, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(pathSentinel, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(metadataSentinel, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(idSentinel, logs, StringComparison.OrdinalIgnoreCase);
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
}
