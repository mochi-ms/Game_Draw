using GameDraw.Core.Presentation;

namespace GameDraw.Core.Tests;

public sealed class ResponsiveLayoutPolicyTests
{
    [Theory]
    [InlineData(0, ResponsiveLayoutMode.Compact)]
    [InlineData(719, ResponsiveLayoutMode.Compact)]
    [InlineData(720, ResponsiveLayoutMode.Medium)]
    [InlineData(1119, ResponsiveLayoutMode.Medium)]
    [InlineData(1120, ResponsiveLayoutMode.Expanded)]
    [InlineData(double.NaN, ResponsiveLayoutMode.Compact)]
    public void WidthMapsToStableOneUiBuckets(double width, ResponsiveLayoutMode expected)
    {
        Assert.Equal(expected, ResponsiveLayoutPolicy.FromWidth(width));
    }

    [Fact]
    public void CompactIsSingleColumnAndOtherBucketsAreTwoColumn()
    {
        Assert.False(ResponsiveLayoutPolicy.IsTwoColumn(ResponsiveLayoutMode.Compact));
        Assert.True(ResponsiveLayoutPolicy.IsTwoColumn(ResponsiveLayoutMode.Medium));
        Assert.True(ResponsiveLayoutPolicy.IsTwoColumn(ResponsiveLayoutMode.Expanded));
    }
}
