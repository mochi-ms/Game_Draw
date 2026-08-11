using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Color;
using GameDraw.Imaging.Decoding;
using GameDraw.Imaging.Palettes;
using GameDraw.Imaging.Processing;
using GameDraw.Imaging.Quantization;
using GameDraw.Imaging.Resampling;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using CoreImageFrame = GameDraw.Core.Imaging.ImageFrame;

namespace GameDraw.Imaging.Tests;

public sealed class ImagingPipelineTests
{
    [Fact]
    public void ColorMathUsesPerceptualLabDistance()
    {
        var black = ColorMath.ToLab(RgbColor.Black);
        var white = ColorMath.ToLab(RgbColor.White);

        Assert.Equal(0d, ColorMath.DeltaE76(black, black), precision: 10);
        Assert.True(ColorMath.DeltaE76(black, white) > 90d);
        Assert.Equal(0d, ColorMath.DeltaE2000(black, black), precision: 10);

        var referenceA = new LabColor(50, 2.6772, -79.7751);
        var referenceB = new LabColor(50, 0, -82.7485);
        Assert.InRange(ColorMath.DeltaE2000(referenceA, referenceB), 2.041d, 2.044d);
    }

    [Fact]
    public void CompositeOverUsesLinearLightBlending()
    {
        var result = ColorMath.CompositeOver(RgbColor.Black, 128, RgbColor.White);

        Assert.InRange(result.R, (byte)187, (byte)189);
        Assert.Equal(result.R, result.G);
        Assert.Equal(result.G, result.B);
    }

    [Fact]
    public void LanczosResizeProducesStableLinearLightGoldenPixel()
    {
        var source = new CoreImageFrame(2, 2, new[]
        {
            RgbaPixel.Opaque(RgbColor.Black),
            RgbaPixel.Opaque(RgbColor.White),
            RgbaPixel.Opaque(RgbColor.White),
            RgbaPixel.Opaque(RgbColor.Black)
        });

        var resized = ImageResampler.Resize(source, new PixelSize(1, 1));

        Assert.InRange(resized[0, 0].Color.R, (byte)187, (byte)189);
        Assert.Equal(resized[0, 0].Color.R, resized[0, 0].Color.G);
        Assert.Equal(resized[0, 0].Color.G, resized[0, 0].Color.B);
    }

    [Fact]
    public void ResamplingPreservesTransparentAlphaWithPremultipliedEdges()
    {
        var source = new CoreImageFrame(2, 1, new[]
        {
            new RgbaPixel(RgbColor.White, 0),
            RgbaPixel.Opaque(RgbColor.Black)
        });

        var resized = ImageResampler.Resize(source, new PixelSize(1, 1));

        Assert.InRange(resized[0, 0].Alpha, (byte)127, (byte)129);
        Assert.InRange(resized[0, 0].Color.R, (byte)0, (byte)2);
    }

    [Fact]
    public void AdaptivePaletteIsDeterministicAndBounded()
    {
        var source = new CoreImageFrame(4, 1, new[]
        {
            RgbaPixel.Opaque(new RgbColor(255, 0, 0)),
            RgbaPixel.Opaque(new RgbColor(250, 10, 0)),
            RgbaPixel.Opaque(new RgbColor(0, 0, 255)),
            RgbaPixel.Opaque(new RgbColor(0, 10, 250))
        });
        var options = new PaletteBuildOptions { MaxColors = 2 };
        var builder = new AdaptivePaletteBuilder();

        var first = builder.Build(source, options);
        var second = builder.Build(source, options);

        Assert.Equal(2, first.Count);
        Assert.Equal(first.Colors, second.Colors);
        Assert.Contains(first.Colors, color => color.R > 200 && color.B < 50);
        Assert.Contains(first.Colors, color => color.B > 200 && color.R < 50);
    }

    [Fact]
    public void PaletteQuantizerMapsAndDithersWithoutChangingDimensions()
    {
        var source = new CoreImageFrame(4, 1, new[]
        {
            RgbaPixel.Opaque(new RgbColor(0, 0, 0)),
            RgbaPixel.Opaque(new RgbColor(64, 64, 64)),
            RgbaPixel.Opaque(new RgbColor(192, 192, 192)),
            RgbaPixel.Opaque(new RgbColor(255, 255, 255))
        });
        var palette = new ColorPalette(new[] { RgbColor.Black, RgbColor.White });
        var quantizer = new PaletteQuantizer();

        var mapped = quantizer.Quantize(source, palette);
        var dithered = quantizer.Quantize(source, palette, new QuantizationOptions
        {
            DitherMode = DitherMode.FloydSteinberg
        });

        Assert.Equal(new byte[] { 0, 0, 1, 1 }, mapped.Indices);
        Assert.Equal(source.Width, dithered.Width);
        Assert.Equal(source.Height, dithered.Height);
        Assert.Contains((byte)0, dithered.Indices);
        Assert.Contains((byte)1, dithered.Indices);
    }

    [Fact]
    public async Task DecoderPreservesAndCompositesAlphaByPolicy()
    {
        await using var source = new MemoryStream();
        using (var image = new Image<Rgba32>(2, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 128);
            image[1, 0] = new Rgba32(0, 255, 0, 255);
            await image.SaveAsPngAsync(source);
        }

        source.Position = 0;
        var decoder = new ImageDecoder();
        var preserved = await decoder.DecodeAsync(source, "alpha.png", new ImageDecodeOptions
        {
            AlphaPolicy = AlphaPolicy.Preserve
        });

        Assert.Equal(2, preserved.Frame.Width);
        Assert.Equal((byte)128, preserved.Frame[0, 0].Alpha);
        Assert.Equal((byte)255, preserved.Frame[1, 0].Alpha);

        source.Position = 0;
        var composited = await decoder.DecodeAsync(source, "alpha.png", new ImageDecodeOptions
        {
            AlphaPolicy = AlphaPolicy.CompositeOnBackground,
            BackgroundColor = RgbColor.White
        });

        Assert.Equal((byte)255, composited.Frame[0, 0].Alpha);
        Assert.InRange(composited.Frame[0, 0].Color.G, (byte)187, (byte)189);
    }

    [Fact]
    public void ProcessingPipelineResizesAndUsesFixedPalette()
    {
        var frame = new CoreImageFrame(2, 2, new[]
        {
            RgbaPixel.Opaque(RgbColor.Black),
            RgbaPixel.Opaque(RgbColor.White),
            RgbaPixel.Opaque(RgbColor.White),
            RgbaPixel.Opaque(RgbColor.Black)
        });
        var pipeline = new ImageProcessingPipeline();
        var result = pipeline.ProcessFrame(frame, new ImageProcessingOptions
        {
            TargetSize = new PixelSize(1, 1),
            FixedPalette = new ColorPalette(new[] { RgbColor.Black, RgbColor.White })
        });

        Assert.Equal(new PixelSize(1, 1), new PixelSize(result.WorkingFrame.Width, result.WorkingFrame.Height));
        Assert.Single(result.Quantized.Indices);
        Assert.Equal(2, result.Palette.Count);
    }

    [Fact]
    public void LineArtExtractsBlackEdgesWithTransparentBackground()
    {
        var pixels = new RgbaPixel[25];
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                pixels[(y * 5) + x] = RgbaPixel.Opaque(x < 2 ? RgbColor.Black : RgbColor.White);
            }
        }

        var result = LineArtProcessor.Extract(new CoreImageFrame(5, 5, pixels));

        Assert.Contains(result.Pixels, pixel => pixel.IsOpaque && pixel.Color == RgbColor.Black);
        Assert.Contains(result.Pixels, pixel => pixel.IsTransparent);
        Assert.DoesNotContain(result.Pixels, pixel => pixel.IsOpaque && pixel.Color != RgbColor.Black);
    }

    [Fact]
    public void LineArtLeavesUniformImageTransparent()
    {
        var frame = new CoreImageFrame(
            4,
            4,
            Enumerable.Repeat(RgbaPixel.Opaque(RgbColor.White), 16).ToArray());

        var result = LineArtProcessor.Extract(frame);

        Assert.All(result.Pixels, pixel => Assert.True(pixel.IsTransparent));
    }

    [Fact]
    public void SmartSubjectRemovesBorderBackgroundAndCropsPortrait()
    {
        const int width = 20;
        const int height = 20;
        var pixels = Enumerable.Repeat(RgbaPixel.Opaque(RgbColor.White), width * height).ToArray();
        for (var y = 4; y < 16; y++)
        {
            for (var x = 7; x < 13; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }

        var result = SubjectFocusProcessor.Process(new CoreImageFrame(width, height, pixels));

        Assert.True(result.BackgroundRemoved);
        Assert.True(result.Cropped);
        Assert.True(result.PersonLikely);
        Assert.NotNull(result.FacePriorityRegion);
        Assert.True(result.Frame.Width < width);
        Assert.True(result.Frame.Height < height);
        Assert.Contains(result.Frame.Pixels, pixel => pixel.Alpha == 0);
    }

    [Fact]
    public void SmartSubjectKeepsUniformFrameWhenNoSubjectExists()
    {
        var frame = new CoreImageFrame(
            12,
            12,
            Enumerable.Repeat(RgbaPixel.Opaque(RgbColor.White), 144).ToArray());

        var result = SubjectFocusProcessor.Process(frame);

        Assert.False(result.BackgroundRemoved);
        Assert.False(result.Cropped);
        Assert.Equal(frame.Width, result.Frame.Width);
        Assert.Equal(frame.Height, result.Frame.Height);
    }
}
