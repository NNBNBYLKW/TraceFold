using TraceFold.Media.Application.Startup;
using TraceFold.Media.Desktop.Shell;
using TraceFold.Media.Desktop.Tests.Support;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Shell;

public sealed class ShellViewModelTests
{
    [Fact]
    public void SetupRequired_selects_settings_and_exposes_the_message()
    {
        var startup = new FakeStartupCoordinator();
        var viewModel = new ShellViewModel(startup);

        startup.Publish(new StartupStatus(
            StartupState.SetupRequired,
            "请先配置三个工作目录。",
            []));

        Assert.Equal("settings", viewModel.SelectedPageKey);
        Assert.Equal(StartupState.SetupRequired, viewModel.StartupState);
        Assert.Equal("请先配置三个工作目录。", viewModel.StartupMessage);
        Assert.True(viewModel.IsStartupInfoBarOpen);
    }

    [Theory]
    [InlineData(StartupState.Initializing, true)]
    [InlineData(StartupState.SetupRequired, true)]
    [InlineData(StartupState.Ready, false)]
    [InlineData(StartupState.Failed, true)]
    public void Status_changes_are_reflected_without_redirecting_non_setup_states(
        StartupState state,
        bool expectedInfoBarOpen)
    {
        var startup = new FakeStartupCoordinator();
        var viewModel = new ShellViewModel(startup)
        {
            SelectedPageKey = "library"
        };

        startup.Publish(new StartupStatus(state, state.ToString(), []));

        Assert.Equal(state, viewModel.StartupState);
        Assert.Equal(state.ToString(), viewModel.StartupMessage);
        Assert.Equal(expectedInfoBarOpen, viewModel.IsStartupInfoBarOpen);
        Assert.Equal(
            state == StartupState.SetupRequired ? "settings" : "library",
            viewModel.SelectedPageKey);
    }
}
