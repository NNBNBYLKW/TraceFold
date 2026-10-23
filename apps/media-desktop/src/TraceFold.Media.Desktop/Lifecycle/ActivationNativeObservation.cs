using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TraceFold.Media.Desktop.Lifecycle;

/// <summary>Read-only, best-effort facts. No activation, logging, or process control.</summary>
public static class ActivationNativeObservation
{
    public enum PrimaryHandleSource { Unknown, CachedUi, FreshUi }

    public readonly record struct ReceiverIdentity(int ProcessId, long StartUtcTicks);
    public readonly record struct WindowOwner(uint ThreadId, uint ProcessId);

    public sealed record Snapshot(
        DateTimeOffset? CaptureBeginUtc,
        DateTimeOffset? CaptureEndUtc,
        int? ReceiverPid,
        long? ReceiverStartUtcTicks,
        uint? NativeThreadId,
        long? PrimaryHwnd,
        PrimaryHandleSource PrimaryHwndSource,
        DateTimeOffset? PrimaryHwndCapturedUtc,
        uint? PrimaryOwnerPid,
        uint? PrimaryThreadId,
        long? ForegroundHwnd,
        uint? ForegroundOwnerPid,
        uint? ForegroundThreadId,
        string CaptureState,
        string Reason,
        string? ErrorType,
        int? ErrorHResult);

    public static Snapshot Capture(
        long? primaryHwnd,
        PrimaryHandleSource primarySource,
        DateTimeOffset? primaryCapturedUtc) => Capture(
            primaryHwnd, primarySource, primaryCapturedUtc,
            () => DateTimeOffset.UtcNow,
            ReadReceiverIdentity,
            GetCurrentThreadId,
            GetForegroundWindow,
            ReadWindowOwner);

    // Small read seams let direct tests exercise failure/identity handling without a desktop.
    public static Snapshot Capture(
        long? primaryHwnd,
        PrimaryHandleSource primarySource,
        DateTimeOffset? primaryCapturedUtc,
        Func<DateTimeOffset> clock,
        Func<ReceiverIdentity> readReceiver,
        Func<uint> readThread,
        Func<nint> readForeground,
        Func<nint, WindowOwner> readOwner)
    {
        Exception? firstError = null;
        string reason = string.Empty;

        void Missing(string code)
        {
            if (reason.Length == 0) reason = code;
        }

        T? Read<T>(Func<T> reader, string code) where T : struct
        {
            try { return reader(); }
            catch (Exception exception)
            {
                firstError ??= exception;
                Missing(code);
                return null;
            }
        }

        WindowOwner? Owner(nint handle, string code)
        {
            WindowOwner? owner = Read(() => readOwner(handle), code);
            if (owner is { ProcessId: > 0, ThreadId: > 0 }) return owner;
            Missing(code);
            return null;
        }

        DateTimeOffset? begin = Read(clock, "clock_begin_unavailable");
        ReceiverIdentity? receiver = Read(readReceiver, "receiver_unavailable");
        if (receiver is not { ProcessId: > 0, StartUtcTicks: > 0 })
        {
            receiver = null;
            Missing("receiver_unavailable");
        }
        uint? thread = Read(readThread, "thread_unavailable");
        if (thread is not > 0)
        {
            thread = null;
            Missing("thread_unavailable");
        }

        // One foreground read, followed by its own HWND -> owner lookup, never a second sample.
        nint? foreground = Read(readForeground, "foreground_unavailable");
        WindowOwner? foregroundOwner = null;
        if (foreground.HasValue && foreground.Value != nint.Zero)
            foregroundOwner = Owner(foreground.Value, "foreground_owner_unavailable");
        else
        {
            foreground = null;
            Missing("foreground_unavailable");
        }

        WindowOwner? primaryOwner = null;
        if (primaryHwnd is > 0)
        {
            nint handle = new(primaryHwnd.Value);
            primaryOwner = foreground.HasValue && handle == foreground.Value
                ? foregroundOwner : Owner(handle, "primary_owner_unavailable");
        }
        else
        {
            primaryHwnd = null;
            primarySource = PrimaryHandleSource.Unknown;
            primaryCapturedUtc = null;
        }
        DateTimeOffset? end = Read(clock, "clock_end_unavailable");

        return new Snapshot(
            begin, end, receiver?.ProcessId, receiver?.StartUtcTicks, thread,
            primaryHwnd, primarySource, primaryCapturedUtc,
            primaryOwner?.ProcessId, primaryOwner?.ThreadId,
            foreground?.ToInt64(), foregroundOwner?.ProcessId, foregroundOwner?.ThreadId,
            reason.Length == 0 ? "Captured" : "Partial", reason,
            firstError?.GetType().FullName, firstError?.HResult);
    }

    private static ReceiverIdentity ReadReceiverIdentity()
    {
        using Process process = Process.GetCurrentProcess();
        return new ReceiverIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks);
    }

    private static WindowOwner ReadWindowOwner(nint handle)
    {
        uint thread = GetWindowThreadProcessId(handle, out uint process);
        return new WindowOwner(thread, process);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
}
