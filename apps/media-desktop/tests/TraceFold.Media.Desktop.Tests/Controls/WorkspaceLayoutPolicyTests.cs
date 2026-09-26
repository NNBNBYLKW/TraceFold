using TraceFold.Media.Desktop.Controls;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Controls;

/// <summary>
/// The breakpoint decision and the DIP to physical-pixel conversion are pure functions so
/// they can be verified exactly instead of inferred from XAML: the responsive host and the
/// shell both delegate every layout decision to this policy.
/// </summary>
public sealed class WorkspaceLayoutPolicyTests
{
    [Theory]
    [InlineData(0, WorkspaceLayoutMode.Compact)]
    [InlineData(1024, WorkspaceLayoutMode.Compact)]
    [InlineData(1100, WorkspaceLayoutMode.Compact)]
    [InlineData(1279.99, WorkspaceLayoutMode.Compact)]
    [InlineData(1280, WorkspaceLayoutMode.Wide)]
    [InlineData(1600, WorkspaceLayoutMode.Wide)]
    public void Breakpoint_is_exact(double width, WorkspaceLayoutMode expected) =>
        Assert.Equal(expected, WorkspaceLayoutPolicy.GetMode(width));

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_width_is_rejected(double width) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WorkspaceLayoutPolicy.GetMode(width));

    [Fact]
    public void Breakpoint_matches_the_wide_token_value() =>
        Assert.Equal(1280d, WorkspaceLayoutPolicy.WideBreakpoint);

    [Theory]
    [InlineData(1280, 800, 1.0, 1280, 800)]
    [InlineData(1280, 800, 1.25, 1600, 1000)]
    [InlineData(1280, 800, 1.5, 1920, 1200)]
    [InlineData(1024, 720, 1.25, 1280, 900)]
    [InlineData(1024, 720, 2.0, 2048, 1440)]
    [InlineData(1280, 800, 1.75, 2240, 1400)]
    public void Physical_window_size_scales_with_rasterization(
        double widthDip,
        double heightDip,
        double scale,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.Equal(expectedWidth, WorkspaceLayoutPolicy.PhysicalPixels(widthDip, scale));
        Assert.Equal(expectedHeight, WorkspaceLayoutPolicy.PhysicalPixels(heightDip, scale));
    }

    [Theory]
    [InlineData(1280, 0)]
    [InlineData(1280, -1)]
    [InlineData(double.NaN, 1)]
    public void Invalid_conversion_inputs_are_rejected(double dip, double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WorkspaceLayoutPolicy.PhysicalPixels(dip, scale));
}
