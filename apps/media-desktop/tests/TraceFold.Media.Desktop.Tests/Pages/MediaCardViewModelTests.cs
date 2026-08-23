using Microsoft.UI.Xaml.Media;
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
}
