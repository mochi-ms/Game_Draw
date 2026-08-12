using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Processing;

namespace GameDraw.Imaging.Tests;

public sealed class PhotoHalftoneTests
{
    [Fact]
    public void WhiteCanvasProducesNoDots()
    {
        var source = Solid(8, 8, RgbColor.White);

        var result = PhotoHalftoneProcessor.Process(source);

        Assert.All(result.Pixels, pixel => Assert.False(pixel.IsOpaque));
    }

    [Fact]
    public void DarkAndMidTonesProduceOrderedDensityLevels()
    {
        var black = PhotoHalftoneProcessor.Process(Solid(8, 8, RgbColor.Black));
        var gray = PhotoHalftoneProcessor.Process(Solid(8, 8, new RgbColor(128, 128, 128)));

        var blackDots = black.Pixels.Count(pixel => pixel.IsOpaque);
        var grayDots = gray.Pixels.Count(pixel => pixel.IsOpaque);
        Assert.InRange(blackDots, 58, 62);
        Assert.InRange(grayDots, 15, 45);
        Assert.True(blackDots > grayDots);
        Assert.All(black.Pixels.Where(pixel => pixel.IsOpaque), pixel => Assert.Equal(RgbColor.Black, pixel.Color));
    }

    [Fact]
    public void HalftoneIsDeterministicAndKeepsAContrastEdge()
    {
        var pixels = new RgbaPixel[16 * 8];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                pixels[(y * 16) + x] = RgbaPixel.Opaque(
                    x < 8 ? new RgbColor(90, 90, 90) : new RgbColor(230, 230, 230));
            }
        }

        var source = new ImageFrame(16, 8, pixels);
        var first = PhotoHalftoneProcessor.Process(source);
        var second = PhotoHalftoneProcessor.Process(source);

        Assert.Equal(first.Pixels, second.Pixels);
        Assert.Contains(first.Pixels, pixel => pixel.IsOpaque);
        Assert.Contains(Enumerable.Range(0, 8), y => first[7, y].IsOpaque || first[8, y].IsOpaque);
    }

    private static ImageFrame Solid(int width, int height, RgbColor color)
        => new(width, height, Enumerable.Repeat(RgbaPixel.Opaque(color), width * height).ToArray());
}
