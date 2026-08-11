using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Models;

namespace GameDraw.Core.Tests;

public sealed class DomainContractsTests
{
    [Fact]
    public void RgbColorRoundTripsHexValue()
    {
        var color = RgbColor.Parse("#336699");

        Assert.Equal((byte)0x33, color.R);
        Assert.Equal((byte)0x66, color.G);
        Assert.Equal((byte)0x99, color.B);
        Assert.Equal("#336699", color.ToHex());
        Assert.False(RgbColor.TryParse("not-a-color", out _));
    }

    [Fact]
    public void ImageFrameWithPixelDoesNotMutateOriginal()
    {
        var original = new ImageFrame(2, 1, new[]
        {
            RgbaPixel.Opaque(RgbColor.Black),
            RgbaPixel.Opaque(RgbColor.White)
        });
        var updated = original.WithPixel(0, 0, RgbaPixel.Opaque(new RgbColor(12, 34, 56)));

        Assert.Equal(RgbColor.Black, original[0, 0].Color);
        Assert.Equal(new RgbColor(12, 34, 56), updated[0, 0].Color);
    }

    [Fact]
    public void DrawingPlanComputesOrderedStatistics()
    {
        var firstStroke = new DrawingStroke(new[]
        {
            new NormalizedPoint(0, 0),
            new NormalizedPoint(0.5, 0)
        });
        var secondStroke = new DrawingStroke(new[]
        {
            new NormalizedPoint(0.5, 0.5),
            new NormalizedPoint(1, 0.5),
            new NormalizedPoint(1, 1)
        });
        var plan = new DrawingPlan(
            DrawingMode.Hybrid,
            new PixelSize(2, 2),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[] { firstStroke }),
                new DrawingColorGroup(RgbColor.White, new[] { secondStroke })
            });

        Assert.Equal(2, plan.Statistics.ColorCount);
        Assert.Equal(2, plan.Statistics.StrokeCount);
        Assert.Equal(5, plan.Statistics.PointCount);
        Assert.Equal(1, plan.Statistics.ColorChanges);
        Assert.Equal(1.5, plan.Statistics.NormalizedTravelDistance, precision: 6);
        Assert.Equal(2, plan.EnumerateStrokes().Count());
    }

    [Fact]
    public void DrawingStrokeRejectsPointsOutsideCanvas()
    {
        Assert.Throws<ArgumentException>(() => new DrawingStroke(new[]
        {
            new NormalizedPoint(-0.01, 0)
        }));
    }

    [Fact]
    public void DrawingModeContainsPixelAndLineStrategies()
    {
        Assert.Contains(DrawingMode.Pixel, Enum.GetValues<DrawingMode>());
        Assert.Contains(DrawingMode.HorizontalScanline, Enum.GetValues<DrawingMode>());
        Assert.Contains(DrawingMode.VerticalScanline, Enum.GetValues<DrawingMode>());
        Assert.Contains(DrawingMode.CleanStroke, Enum.GetValues<DrawingMode>());
        Assert.Contains(DrawingMode.ArtistStroke, Enum.GetValues<DrawingMode>());
    }
}
