using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Processing;

public sealed record LineArtOptions
{
    public double EdgeThreshold { get; init; } = 54d;

    public byte MinimumSourceAlpha { get; init; } = 8;

    public void Validate()
    {
        if (!double.IsFinite(EdgeThreshold) || EdgeThreshold is <= 0d or > 1_442d)
        {
            throw new ArgumentOutOfRangeException(nameof(EdgeThreshold));
        }
    }
}

/// <summary>
/// Produces a transparent monochrome Sobel edge frame. Only opaque black edge
/// pixels are drawable, so the normal planner can render line art without
/// painting the background.
/// </summary>
public static class LineArtProcessor
{
    public static ImageFrame Extract(ImageFrame source, LineArtOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new LineArtOptions();
        options.Validate();
        var luminance = new double[source.PixelCount];
        for (var index = 0; index < source.PixelCount; index++)
        {
            var pixel = source.Pixels[index];
            var alpha = pixel.Alpha / 255d;
            var value = (0.2126d * pixel.Color.R) + (0.7152d * pixel.Color.G) + (0.0722d * pixel.Color.B);
            luminance[index] = (value * alpha) + (255d * (1d - alpha));
        }

        var output = Enumerable.Repeat(RgbaPixel.Transparent, source.PixelCount).ToArray();
        for (var y = 1; y < source.Height - 1; y++)
        {
            for (var x = 1; x < source.Width - 1; x++)
            {
                var center = source[x, y];
                if (center.Alpha < options.MinimumSourceAlpha)
                {
                    continue;
                }

                var gx =
                    -Luminance(x - 1, y - 1) + Luminance(x + 1, y - 1)
                    - (2d * Luminance(x - 1, y)) + (2d * Luminance(x + 1, y))
                    - Luminance(x - 1, y + 1) + Luminance(x + 1, y + 1);
                var gy =
                    -Luminance(x - 1, y - 1) - (2d * Luminance(x, y - 1)) - Luminance(x + 1, y - 1)
                    + Luminance(x - 1, y + 1) + (2d * Luminance(x, y + 1)) + Luminance(x + 1, y + 1);
                if (Math.Sqrt((gx * gx) + (gy * gy)) >= options.EdgeThreshold)
                {
                    output[(y * source.Width) + x] = RgbaPixel.Opaque(RgbColor.Black);
                }
            }
        }

        return new ImageFrame(source.Width, source.Height, output);

        double Luminance(int x, int y) => luminance[(y * source.Width) + x];
    }
}
