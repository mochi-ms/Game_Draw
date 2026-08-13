using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Processing;

namespace GameDraw.Imaging.Tests;

public sealed class GrayscalePhotoTests
{
    [Fact]
    public void ConvertsRgbUsingPerceptualLuminanceAndPreservesAlpha()
    {
        var source = new ImageFrame(2, 1, new[]
        {
            new RgbaPixel(new RgbColor(255, 0, 0), 170),
            RgbaPixel.Transparent
        });

        var result = GrayscalePhotoProcessor.Process(source);

        Assert.Equal(new RgbColor(54, 54, 54), result[0, 0].Color);
        Assert.Equal((byte)170, result[0, 0].Alpha);
        Assert.Equal(RgbaPixel.Transparent, result[1, 0]);
    }

    [Fact]
    public void NeutralPixelsRemainNeutral()
    {
        var source = new ImageFrame(1, 1, new[]
        {
            RgbaPixel.Opaque(new RgbColor(123, 123, 123))
        });

        Assert.Equal(source.Pixels, GrayscalePhotoProcessor.Process(source).Pixels);
    }
}
