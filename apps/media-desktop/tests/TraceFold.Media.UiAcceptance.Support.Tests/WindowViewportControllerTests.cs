using TraceFold.Media.UiAcceptance.Support;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

public sealed class WindowViewportControllerTests
{
    [Theory]
    [InlineData(1024, 96, 1024)]
    [InlineData(1024, 144, 1536)]
    [InlineData(1280, 192, 2560)]
    [InlineData(720, 120, 900)]
    public void Converts_client_dip_to_pixels(int dip, int dpi, int expectedPixels) =>
        Assert.Equal(expectedPixels, WindowViewportController.ToPixels(dip, dpi));

    [Theory]
    [InlineData(1024, 96, 8, 1032)]
    [InlineData(1280, 144, 16, 1944)]
    public void Expands_the_requested_client_area_by_the_nonclient_frame(
        int dip,
        int dpi,
        int frameDip,
        int expectedPixels) =>
        Assert.Equal(
            expectedPixels,
            WindowViewportController.ExpandForNonClient(dip, frameDip, dpi));

    [Fact]
    public void Rejects_an_invalid_dpi_or_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowViewportController.ToPixels(1024, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowViewportController.ToPixels(-1, 96));
    }

    [Theory]
    [InlineData(1023, 720, 100, ViewportRejection.WidthBelowMinimum)]
    [InlineData(1024, 719, 100, ViewportRejection.HeightBelowMinimum)]
    [InlineData(1024, 720, 0, ViewportRejection.MissingProcessId)]
    [InlineData(1024, 720, -5, ViewportRejection.MissingProcessId)]
    public void Rejects_a_request_outside_the_minimum_viewport(
        int width,
        int height,
        int processId,
        ViewportRejection expected) =>
        Assert.Equal(
            expected,
            WindowViewportController.Validate(width, height, processId, "TraceFold.Media", true));

    [Fact]
    public void Rejects_a_foreign_executable_or_an_unowned_process()
    {
        Assert.Equal(
            ViewportRejection.UnexpectedExecutable,
            WindowViewportController.Validate(1280, 800, 42, "notepad", true));
        Assert.Equal(
            ViewportRejection.UnownedProcess,
            WindowViewportController.Validate(1280, 800, 42, "TraceFold.Media", false));
        Assert.Equal(
            ViewportRejection.None,
            WindowViewportController.Validate(1280, 800, 42, "TraceFold.Media", true));
    }

    [Fact]
    public void Converts_a_minimum_viewport_request_end_to_end()
    {
        ViewportRequest request = Assert.IsType<ViewportRequest>(
            WindowViewportController.TryCreateRequest(
                1024,
                720,
                4242,
                "TraceFold.Media.exe",
                ownedByRunner: true,
                dpi: 144,
                frameWidthDip: 8,
                frameHeightDip: 32));

        Assert.Equal(1548, request.WidthPixels);
        Assert.Equal(1128, request.HeightPixels);
        Assert.Equal(4242, request.ProcessId);
    }

    [Fact]
    public void Refuses_to_create_a_request_for_an_unowned_process() =>
        Assert.Null(WindowViewportController.TryCreateRequest(
            1280,
            800,
            7,
            "TraceFold.Media.exe",
            ownedByRunner: false,
            dpi: 96,
            frameWidthDip: 0,
            frameHeightDip: 0));

    [Fact]
    public void Resize_calls_the_native_window_api_and_checks_the_measured_client_area()
    {
        var native = new FakeWindowNative();

        ViewportResizeResult result = Assert.IsType<ViewportResizeResult>(
            WindowViewportController.Resize((IntPtr)42, 1024, 720, native));

        Assert.Equal(1536, result.RequestedWidthPixels);
        Assert.Equal(1080, result.RequestedHeightPixels);
        Assert.Equal(1548, native.OuterWidth);
        Assert.Equal(1128, native.OuterHeight);
        Assert.True(native.SetWasCalled);
        Assert.Equal(1536, result.ActualWidthPixels);
        Assert.Equal(1080, result.ActualHeightPixels);
    }

    [Fact]
    public void Resize_rejects_a_failed_or_inaccurate_client_resize()
    {
        var native = new FakeWindowNative { ActualWidthDelta = 3 };
        Assert.Null(WindowViewportController.Resize((IntPtr)42, 1024, 720, native));

        native.ActualWidthDelta = 0;
        native.SetSucceeds = false;
        Assert.Null(WindowViewportController.Resize((IntPtr)42, 1024, 720, native));
    }

    [Fact]
    public void Owned_process_record_rejects_a_reused_pid_or_different_executable()
    {
        string executable = Path.Combine(Path.GetTempPath(), "product", "TraceFold.Media.exe");
        string otherExecutable = Path.Combine(Path.GetTempPath(), "other", "TraceFold.Media.exe");
        string[] ledger = [$"4242|638000000000000000|{executable}"];

        Assert.True(WindowViewportController.IsRecordedProcess(
            ledger, 4242, 638000000000000000, executable));
        Assert.False(WindowViewportController.IsRecordedProcess(
            ledger, 4242, 638000000000000001, executable));
        Assert.False(WindowViewportController.IsRecordedProcess(
            ledger, 4242, 638000000000000000, otherExecutable));
        Assert.False(WindowViewportController.IsRecordedProcess(
            ["4242|not-a-time|TraceFold.Media.exe"],
            4242, 638000000000000000, executable));
        Assert.False(WindowViewportController.IsRecordedProcess(
            ["4242|638000000000000000|TraceFold.Media.exe"],
            4242, 638000000000000000, executable));
    }

    private sealed class FakeWindowNative : IWindowViewportNative
    {
        private int requestedWidth;
        private int requestedHeight;

        public int ActualWidthDelta { get; set; }

        public bool SetSucceeds { get; set; } = true;

        public bool SetWasCalled { get; private set; }

        public int OuterWidth { get; private set; }

        public int OuterHeight { get; private set; }

        public int GetDpi(IntPtr window) => 144;

        public bool TryAdjustWindowRect(IntPtr window, int clientWidth,
            int clientHeight, int dpi, out int outerWidth, out int outerHeight)
        {
            requestedWidth = clientWidth;
            requestedHeight = clientHeight;
            outerWidth = clientWidth + 12;
            outerHeight = clientHeight + 48;
            return true;
        }

        public bool SetWindowSize(IntPtr window, int outerWidth, int outerHeight)
        {
            SetWasCalled = true;
            OuterWidth = outerWidth;
            OuterHeight = outerHeight;
            return SetSucceeds;
        }

        public bool TryGetClientSize(IntPtr window, out int width, out int height)
        {
            width = requestedWidth + ActualWidthDelta;
            height = requestedHeight;
            return true;
        }
    }
}
