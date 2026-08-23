using Microsoft.UI.Xaml.Media;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;
using TraceFold.Media.Desktop.Pages;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class MediaCardViewModelTests
{
    [Theory]
    [InlineData(typeof(InboxCardViewModel))]
    [InlineData(typeof(LibraryCardViewModel))]
    public void Thumbnail_source_is_an_image_source_instead_of_a_convertible_string(
        Type cardType)
    {
        Type? propertyType = cardType.GetProperty("ThumbnailSource")?.PropertyType;

        Assert.Equal(typeof(ImageSource), propertyType);
    }

    [Fact]
    public void Running_inbox_card_exposes_compact_processing_progress()
    {
        var card = new InboxCardDto(
            Guid.NewGuid(),
            "clip.mp4",
            "clip.mp4",
            MediaKind.Video,
            InboxItemState.Processing,
            "处理中",
            false,
            new ProcessingSummaryDto(
                Guid.NewGuid(),
                ProcessingJobStatus.Running,
                "合并视频和音频",
                "合并音视频",
                63,
                null,
                false));

        var viewModel = new InboxCardViewModel(card);

        Assert.Equal("合并音视频 63%", viewModel.ProgressText);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible,
            viewModel.ProcessingVisibility);
    }
}
