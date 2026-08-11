using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;

namespace GameDraw.Core.Tests;

public sealed class ColorAndGeometryTests
{
    [Fact]
    public void RgbConvertsToHexAndBack()
    {
        var original = new RgbColor(255, 0, 128);

        var hex = original.ToHex();
        var parsed = RgbColor.Parse(hex);

        Assert.Equal("#FF0080", hex);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void RgbConvertsToHsvAndBack()
    {
        var original = new RgbColor(255, 128, 0);

        var hsv = original.ToHsv();
        var roundTrip = hsv.ToRgb();

        Assert.Equal(30.117647d, hsv.Hue, precision: 3);
        Assert.Equal(1d, hsv.Saturation, precision: 3);
        Assert.Equal(1d, hsv.Value, precision: 6);
        Assert.InRange(Math.Abs(roundTrip.R - original.R), 0, 1);
        Assert.InRange(Math.Abs(roundTrip.G - original.G), 0, 1);
        Assert.InRange(Math.Abs(roundTrip.B - original.B), 0, 1);
    }

    [Fact]
    public void NearestPaletteUsesPerceptualDistance()
    {
        var source = new RgbColor(250, 20, 20);
        var palette = new[]
        {
            new RgbColor(255, 0, 0),
            new RgbColor(0, 0, 255),
            new RgbColor(0, 255, 0)
        };

        Assert.Equal(new RgbColor(255, 0, 0), ColorMath.FindNearest(source, palette));
    }

    [Fact]
    public void NormalizedCoordinateMapsToCanvasAndBack()
    {
        var canvas = new CanvasRect(100, 200, 800, 600);
        var normalized = new NormalizedPoint(0.5, 0.25);

        var screen = CoordinateMapper.ToPhysical(canvas, normalized);
        var roundTrip = CoordinateMapper.ToNormalized(canvas, screen);

        Assert.Equal(new ScreenPoint(500, 350), screen);
        Assert.InRange(roundTrip.X, 0.499, 0.501);
        Assert.InRange(roundTrip.Y, 0.249, 0.251);
    }
}
