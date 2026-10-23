using System.Text.Json;
using Xunit;
using Observation = TraceFold.Media.Desktop.Lifecycle.ActivationNativeObservation;

namespace TraceFold.Media.Desktop.Tests.Lifecycle;

public sealed class ActivationNativeObservationTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Foreground_owner_is_bound_to_the_one_sample_even_if_a_second_read_would_change()
    {
        int foregroundReads = 0;
        var ownerHandles = new List<nint>();
        var snapshot = Capture(
            readForeground: () => new nint(++foregroundReads == 1 ? 501 : 999),
            readOwner: handle =>
            {
                ownerHandles.Add(handle);
                return handle == new nint(501)
                    ? new Observation.WindowOwner(71, 91)
                    : new Observation.WindowOwner(72, 81);
            });

        Assert.Equal(1, foregroundReads);
        Assert.Equal(new nint[] { new(501), new(401) }, ownerHandles);
        Assert.Equal(501L, snapshot.ForegroundHwnd);
        Assert.Equal((uint)91, snapshot.ForegroundOwnerPid);
        Assert.Equal((uint)71, snapshot.ForegroundThreadId);
        Assert.Equal(401L, snapshot.PrimaryHwnd);
        Assert.Equal((uint)81, snapshot.PrimaryOwnerPid);
        Assert.Equal("Captured", snapshot.CaptureState);
    }

    [Fact]
    public void Failed_foreground_reader_retains_unknown_and_never_serializes_exception_payload()
    {
        const string secret = "DIAGNOSTIC_PRIVATE_TOKEN C:\\private\\user-data receipt=private";
        var failure = new InvalidOperationException(secret);
        var snapshot = Capture(
            primaryHwnd: null,
            readForeground: () => throw failure);

        Assert.Null(snapshot.ForegroundHwnd);
        Assert.Null(snapshot.ForegroundOwnerPid);
        Assert.Equal("Partial", snapshot.CaptureState);
        Assert.Equal("foreground_unavailable", snapshot.Reason);
        Assert.Equal(typeof(InvalidOperationException).FullName, snapshot.ErrorType);
        Assert.Equal(failure.HResult, snapshot.ErrorHResult);
        string json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("DIAGNOSTIC_PRIVATE_TOKEN", json);
        Assert.DoesNotContain("private", json);
        Assert.DoesNotContain("StackTrace", json);
        Assert.DoesNotContain("Message", json);
    }

    [Fact]
    public void Missing_foreground_has_no_fake_zero_pid_or_owner_lookup()
    {
        int ownerReads = 0;
        var snapshot = Capture(
            primaryHwnd: 0,
            readForeground: () => nint.Zero,
            readOwner: _ =>
            {
                ownerReads++;
                return new Observation.WindowOwner(11, 22);
            });

        Assert.Equal(0, ownerReads);
        Assert.Null(snapshot.ForegroundHwnd);
        Assert.Null(snapshot.ForegroundOwnerPid);
        Assert.Null(snapshot.PrimaryHwnd);
        Assert.Null(snapshot.PrimaryHwndCapturedUtc);
        Assert.Equal(Observation.PrimaryHandleSource.Unknown, snapshot.PrimaryHwndSource);
        Assert.Equal("Partial", snapshot.CaptureState);
    }

    [Fact]
    public void Failed_receiver_identity_does_not_discard_an_independent_foreground_sample()
    {
        var snapshot = Capture(
            readReceiver: () => throw new InvalidOperationException("receiver unavailable"));

        Assert.Null(snapshot.ReceiverPid);
        Assert.Null(snapshot.ReceiverStartUtcTicks);
        Assert.Equal(501L, snapshot.ForegroundHwnd);
        Assert.Equal((uint)91, snapshot.ForegroundOwnerPid);
        Assert.Equal("receiver_unavailable", snapshot.Reason);
    }

    [Fact]
    public void Invalid_owner_is_unknown_even_when_a_handle_was_read()
    {
        var snapshot = Capture(
            readOwner: _ => new Observation.WindowOwner(0, 0));

        Assert.Equal(501L, snapshot.ForegroundHwnd);
        Assert.Null(snapshot.ForegroundOwnerPid);
        Assert.Null(snapshot.ForegroundThreadId);
        Assert.Equal("foreground_owner_unavailable", snapshot.Reason);
    }

    [Fact]
    public void Read_failures_including_the_clock_do_not_escape_the_observer()
    {
        Observation.Snapshot? snapshot = null;
        Exception? thrown = Record.Exception(() =>
        {
            snapshot = Capture(
                primaryHwnd: null,
                clock: () => throw new InvalidOperationException("clock unavailable"),
                readReceiver: () => throw new InvalidOperationException("receiver unavailable"),
                readThread: () => throw new InvalidOperationException("thread unavailable"),
                readForeground: () => throw new InvalidOperationException("foreground unavailable"));
        });

        Assert.Null(thrown);
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.CaptureBeginUtc);
        Assert.Null(snapshot.CaptureEndUtc);
        Assert.Null(snapshot.NativeThreadId);
        Assert.Equal("clock_begin_unavailable", snapshot.Reason);
    }

    [Fact]
    public void Cached_primary_provenance_and_separate_capture_boundaries_are_preserved()
    {
        int clockReads = 0;
        var cachedAt = Epoch.AddMinutes(-1);
        var snapshot = Observation.Capture(
            401, Observation.PrimaryHandleSource.CachedUi, cachedAt,
            () => Epoch.AddTicks(clockReads++),
            () => new Observation.ReceiverIdentity(81, 123456),
            () => 31,
            () => new nint(501),
            _ => new Observation.WindowOwner(71, 91));

        Assert.Equal(2, clockReads);
        Assert.Equal(Epoch, snapshot.CaptureBeginUtc);
        Assert.Equal(Epoch.AddTicks(1), snapshot.CaptureEndUtc);
        Assert.Equal(cachedAt, snapshot.PrimaryHwndCapturedUtc);
        Assert.Equal(Observation.PrimaryHandleSource.CachedUi, snapshot.PrimaryHwndSource);
    }

    private static Observation.Snapshot Capture(
        long? primaryHwnd = 401,
        Func<DateTimeOffset>? clock = null,
        Func<Observation.ReceiverIdentity>? readReceiver = null,
        Func<uint>? readThread = null,
        Func<nint>? readForeground = null,
        Func<nint, Observation.WindowOwner>? readOwner = null) => Observation.Capture(
            primaryHwnd, Observation.PrimaryHandleSource.FreshUi, Epoch,
            clock ?? (() => Epoch),
            readReceiver ?? (() => new Observation.ReceiverIdentity(81, 123456)),
            readThread ?? (() => 31),
            readForeground ?? (() => new nint(501)),
            readOwner ?? (_ => new Observation.WindowOwner(71, 91)));
}
