using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Processing;

public sealed record PhotoHalftoneOptions
{
    public double ToneGamma { get; init; } = 1.08d;

    public double ToneStrength { get; init; } = 0.88d;

    public double EdgeStrength { get; init; } = 0.72d;

    public double HighlightCutoff { get; init; } = 0.94d;

    public double EdgeInkThreshold { get; init; } = 0.09d;

    public void Validate()
    {
        if (!double.IsFinite(ToneGamma) || ToneGamma is < 0.4d or > 3d)
        {
            throw new ArgumentOutOfRangeException(nameof(ToneGamma));
        }

        if (!double.IsFinite(ToneStrength) || ToneStrength is < 0d or > 2d)
        {
            throw new ArgumentOutOfRangeException(nameof(ToneStrength));
        }

        if (!double.IsFinite(EdgeStrength) || EdgeStrength is < 0d or > 3d)
        {
            throw new ArgumentOutOfRangeException(nameof(EdgeStrength));
        }

        if (!double.IsFinite(HighlightCutoff) || HighlightCutoff is < 0.5d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(HighlightCutoff));
        }

        if (!double.IsFinite(EdgeInkThreshold) || EdgeInkThreshold is <= 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(EdgeInkThreshold));
        }
    }
}

/// <summary>
/// Converts a photograph into a deterministic black-dot density field.
/// Tone supplies the newspaper-style shading while a small Sobel term keeps
/// eyes, lips, nostrils, hair strands, and the face silhouette legible.
/// White pixels become transparent because the Podiums canvas is already white.
/// </summary>
public static class PhotoHalftoneProcessor
{
    public static ImageFrame Process(ImageFrame source, PhotoHalftoneOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new PhotoHalftoneOptions();
        options.Validate();

        var luminance = new double[source.PixelCount];
        for (var index = 0; index < source.PixelCount; index++)
        {
            var pixel = source.Pixels[index];
            luminance[index] = pixel.Alpha == 0 ? 1d : Luminance(pixel.Color);
        }

        var output = new RgbaPixel[source.PixelCount];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var index = (y * source.Width) + x;
                var sourcePixel = source.Pixels[index];
                if (sourcePixel.Alpha == 0 || luminance[index] >= options.HighlightCutoff)
                {
                    output[index] = RgbaPixel.Transparent;
                    continue;
                }

                var darkness = Math.Pow(Math.Clamp(1d - luminance[index], 0d, 1d), options.ToneGamma);
                var edge = Sobel(luminance, x, y, source.Width, source.Height);
                var density = Math.Clamp(
                    (darkness * options.ToneStrength) + (edge * options.EdgeStrength),
                    0d,
                    0.985d);
                // A coordinate hash distributes dots without the visible X
                // and checkerboard bands produced by a small Bayer tile. It
                // remains deterministic, so preview and execution are exact.
                var threshold = DispersedThreshold(x, y);
                output[index] = edge >= options.EdgeInkThreshold || density > threshold
                    ? RgbaPixel.Opaque(RgbColor.Black)
                    : RgbaPixel.Transparent;
            }
        }

        return new ImageFrame(source.Width, source.Height, output);
    }

    private static double Luminance(RgbColor color)
        => ((0.2126d * color.R) + (0.7152d * color.G) + (0.0722d * color.B)) / 255d;

    private static double Sobel(double[] luminance, int x, int y, int width, int height)
    {
        var left = Math.Max(0, x - 1);
        var right = Math.Min(width - 1, x + 1);
        var top = Math.Max(0, y - 1);
        var bottom = Math.Min(height - 1, y + 1);
        var tl = luminance[(top * width) + left];
        var tc = luminance[(top * width) + x];
        var tr = luminance[(top * width) + right];
        var ml = luminance[(y * width) + left];
        var mr = luminance[(y * width) + right];
        var bl = luminance[(bottom * width) + left];
        var bc = luminance[(bottom * width) + x];
        var br = luminance[(bottom * width) + right];
        var gx = -tl + tr - (2d * ml) + (2d * mr) - bl + br;
        var gy = -tl - (2d * tc) - tr + bl + (2d * bc) + br;
        return Math.Clamp(Math.Sqrt((gx * gx) + (gy * gy)) / 4d, 0d, 1d);
    }

    private static double DispersedThreshold(int x, int y)
    {
        unchecked
        {
            uint value = (uint)x * 0x9E3779B1u;
            value ^= (uint)y * 0x85EBCA77u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value + 0.5d) / (uint.MaxValue + 1d);
        }
    }
}
