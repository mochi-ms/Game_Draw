using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Processing;

namespace GameDraw.Imaging.Tests;

public sealed class ArtistLineArtTests
{
    [Fact]
    public void WhiteCanvasProducesNoInk()
    {
        var result = ArtistLineArtProcessor.Process(Solid(24, 24, RgbColor.White));

        Assert.All(result.Pixels, pixel => Assert.False(pixel.IsOpaque));
    }

    [Fact]
    public void DarkToneProducesConnectedDirectionalHatching()
    {
        var result = ArtistLineArtProcessor.Process(Solid(28, 28, new RgbColor(55, 55, 55)));

        Assert.True(result.Pixels.Count(pixel => pixel.IsOpaque) > 120);
        Assert.Contains(Enumerable.Range(1, 27), y =>
            Enumerable.Range(0, 26).Any(x => result[x, y].IsOpaque && result[x + 2, y - 1].IsOpaque));
        Assert.All(result.Pixels.Where(pixel => pixel.IsOpaque), pixel => Assert.Equal(RgbColor.Black, pixel.Color));
    }

    [Fact]
    public void PortraitFeaturesAndShadowsAreBothRetainedDeterministically()
    {
        var pixels = Enumerable.Repeat(RgbaPixel.Opaque(new RgbColor(225, 225, 225)), 40 * 40).ToArray();
        for (var y = 7; y < 33; y++)
        {
            for (var x = 5; x < 14; x++)
            {
                pixels[(y * 40) + x] = RgbaPixel.Opaque(new RgbColor(65, 65, 65));
            }
        }

        for (var x = 17; x <= 22; x++)
        {
            pixels[(15 * 40) + x] = RgbaPixel.Opaque(RgbColor.Black);
            pixels[(25 * 40) + x] = RgbaPixel.Opaque(new RgbColor(95, 95, 95));
        }

        var source = new ImageFrame(40, 40, pixels);
        var first = ArtistLineArtProcessor.Process(source);
        var second = ArtistLineArtProcessor.Process(source);

        Assert.Equal(first.Pixels, second.Pixels);
        Assert.Contains(Enumerable.Range(13, 4), y => Enumerable.Range(16, 8).Any(x => first[x, y].IsOpaque));
        Assert.Contains(Enumerable.Range(23, 5), y => Enumerable.Range(16, 8).Any(x => first[x, y].IsOpaque));
        Assert.True(Enumerable.Range(7, 26).Sum(y => Enumerable.Range(5, 9).Count(x => first[x, y].IsOpaque)) > 40);
    }

    private static ImageFrame Solid(int width, int height, RgbColor color)
        => new(width, height, Enumerable.Repeat(RgbaPixel.Opaque(color), width * height).ToArray());
}
