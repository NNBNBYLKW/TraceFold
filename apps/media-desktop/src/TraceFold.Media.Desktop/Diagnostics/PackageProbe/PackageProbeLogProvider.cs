using Microsoft.Extensions.Logging;

namespace TraceFold.Media.Desktop.Diagnostics;

/// <summary>
/// Scoped log collection for the probe host. It forwards error-level records, with the
/// structured fields and the original exception chain, into the probe trace so a failing
/// probe can report why the product failed. It is only registered by
/// <see cref="PackageProbeEnvironment.BuildHost"/>, it never formats or forwards
/// business values beyond the structured log fields, and it cannot change the outcome:
/// a failure inside this provider is recorded as a trace line instead of propagating.
/// </summary>
internal sealed class PackageProbeLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ProbeLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class ProbeLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error || !PackageProbeTrace.CaptureEnabled)
            {
                return;
            }

            try
            {
                PackageProbeTrace.Write("log category=" + category
                    + "; level=" + logLevel
                    + "; event=" + eventId.Id
                    + "; message=" + formatter(state, exception));
                PackageProbeTrace.WriteException("log", exception);
            }
            catch (Exception error)
            {
                // Diagnostics never replace the original result.
                PackageProbeTrace.Write("log.observation_unavailable type="
                    + error.GetType().FullName);
            }
        }
    }
}
