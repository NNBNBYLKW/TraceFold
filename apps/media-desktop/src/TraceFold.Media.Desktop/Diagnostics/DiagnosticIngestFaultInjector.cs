using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed class DiagnosticIngestFaultInjector(
    IngestFaultPoint selectedPoint,
    Stage4CrashProbeManifest manifest,
    string manifestPath) : IIngestFaultInjector
{
    public Task ReachAsync(
        IngestFaultPoint point,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (point != selectedPoint)
        {
            return Task.CompletedTask;
        }

        if (manifest.FaultPoint != selectedPoint
            || operationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The diagnostic crash checkpoint does not match its manifest.");
        }

        manifest.WithCheckpoint(operationId, point).WriteTo(manifestPath);
        Environment.FailFast("Stage4 diagnostic crash");
        return Task.CompletedTask;
    }
}
