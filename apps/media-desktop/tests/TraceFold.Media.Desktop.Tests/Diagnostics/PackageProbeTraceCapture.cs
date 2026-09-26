using System.Diagnostics;
using TraceFold.Media.Desktop.Diagnostics;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

/// <summary>
/// Captures the probe-only trace channel for the current execution flow, so a failing
/// probe test can report the detailed probe state and the original product exception
/// while the probe result contract keeps its stable error codes.
/// </summary>
internal sealed class PackageProbeTraceCapture : TraceListener
{
    private static readonly AsyncLocal<PackageProbeTraceCapture?> Current = new();
    private static readonly object RegistrationSync = new();
    private readonly List<string> facts = [];

    public PackageProbeTraceCapture()
    {
        lock (RegistrationSync)
        {
            Current.Value = this;
            Trace.Listeners.Add(this);
        }
    }

    public string Summary
    {
        get
        {
            lock (facts)
            {
                return string.Join(Environment.NewLine, facts);
            }
        }
    }

    public override void Write(string? message)
    {
    }

    public override void WriteLine(string? message)
    {
    }

    public override void WriteLine(string? message, string? category)
    {
        if (category == PackageProbeTrace.Category && ReferenceEquals(Current.Value, this))
        {
            lock (facts)
            {
                facts.Add(message ?? string.Empty);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (RegistrationSync)
            {
                Trace.Listeners.Remove(this);
            }

            if (ReferenceEquals(Current.Value, this))
            {
                Current.Value = null;
            }
        }

        base.Dispose(disposing);
    }
}
