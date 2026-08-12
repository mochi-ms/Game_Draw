using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Analysis;

namespace GameDraw.Imaging.Tests;

public sealed class BrushDotAnalyzerTests
{
    [Fact]
    public void MeasuresThreeDotsAtTheirExpectedLocations()
    {
        var before = Solid(200, 120, RgbColor.White);
        var afterPixels = before.Pixels.ToArray();
        var region = new NormalizedRect(0.10d, 0.20d, 0.80d, 0.50d);
        NormalizedPoint[] points =
        [
            new(0.26d, 0.45d),
            new(0.50d, 0.45d),
            new(0.74d, 0.45d)
        ];
        foreach (var point in points)
        {
            StampSquare(afterPixels, 200, 120, point, 5);
        }

        // A large unrelated change elsewhere in the selected rectangle must
        // not replace one of the known calibration dots.
        for (var y = 25; y < 35; y++)
        {
            for (var x = 90; x < 110; x++)
            {
                afterPixels[(y * 200) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }

        var measured = BrushDotAnalyzer.MeasureDiameters(
            before,
            new ImageFrame(200, 120, afterPixels),
            region,
            points);

        Assert.Equal([5d, 5d, 5d], measured);
    }

    [Fact]
    public void AcceptsAOnePixelBrushStamp()
    {
        var before = Solid(60, 40, RgbColor.White);
        var afterPixels = before.Pixels.ToArray();
        NormalizedPoint[] points = [new(0.5d, 0.5d)];
        StampSquare(afterPixels, 60, 40, points[0], 1);

        var measured = BrushDotAnalyzer.MeasureDiameters(
            before,
            new ImageFrame(60, 40, afterPixels),
            new NormalizedRect(0.2d, 0.2d, 0.6d, 0.6d),
            points);

        Assert.Equal([1d], measured);
    }

    [Fact]
    public void MeasuresVisibleDotsEvenWhenBothCaptureFramesContainThem()
    {
        var pixels = Solid(180, 100, RgbColor.White).Pixels.ToArray();
        var region = new NormalizedRect(0.10d, 0.20d, 0.80d, 0.60d);
        NormalizedPoint[] points =
        [
            new(0.26d, 0.50d),
            new(0.50d, 0.50d),
            new(0.74d, 0.50d)
        ];
        foreach (var point in points)
        {
            StampSquare(pixels, 180, 100, point, 3);
        }

        var staleFrame = new ImageFrame(180, 100, pixels);
        var measured = BrushDotAnalyzer.MeasureDiameters(
            staleFrame,
            staleFrame,
            region,
            points);

        Assert.Equal([3d, 3d, 3d], measured);
    }

    private static ImageFrame Solid(int width, int height, RgbColor color)
        => new(width, height, Enumerable.Repeat(RgbaPixel.Opaque(color), width * height).ToArray());

    private static void StampSquare(
        RgbaPixel[] pixels,
        int width,
        int height,
        NormalizedPoint point,
        int diameter)
    {
        var centerX = (int)Math.Round(point.X * (width - 1));
        var centerY = (int)Math.Round(point.Y * (height - 1));
        var startX = centerX - (diameter / 2);
        var startY = centerY - (diameter / 2);
        for (var y = 0; y < diameter; y++)
        {
            for (var x = 0; x < diameter; x++)
            {
                pixels[((startY + y) * width) + startX + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }
    }
}
