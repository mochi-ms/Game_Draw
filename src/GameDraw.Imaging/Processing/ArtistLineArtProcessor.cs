using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Processing;

public sealed record ArtistLineArtOptions
{
    public double EdgeThreshold { get; init; } = 10d;

    public double AdaptivePercentile { get; init; } = 0.54d;

    public double WeakEdgeRatio { get; init; } = 0.18d;

    public int MinimumComponentPixels { get; init; } = 2;

    public byte MinimumSourceAlpha { get; init; } = 8;

    public void Validate()
    {
        if (!double.IsFinite(EdgeThreshold) || EdgeThreshold is <= 0d or > 1_442d)
        {
            throw new ArgumentOutOfRangeException(nameof(EdgeThreshold));
        }

        if (!double.IsFinite(AdaptivePercentile) || AdaptivePercentile is < 0.4d or > 0.99d)
        {
            throw new ArgumentOutOfRangeException(nameof(AdaptivePercentile));
        }

        if (!double.IsFinite(WeakEdgeRatio) || WeakEdgeRatio is <= 0d or >= 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(WeakEdgeRatio));
        }

        if (MinimumComponentPixels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumComponentPixels));
        }
    }
}

/// <summary>
/// Produces monochrome portrait line art rather than a binary silhouette.
/// Fine contrast edges preserve eyes, lips, hair strands and fabric seams;
/// connected directional hatching expresses dark and mid-tone shading.
/// </summary>
public static class ArtistLineArtProcessor
{
    public static ImageFrame Process(ImageFrame source, ArtistLineArtOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new ArtistLineArtOptions();
        options.Validate();

        var contours = LineArtProcessor.Extract(source, new LineArtOptions
        {
            EdgeThreshold = options.EdgeThreshold,
            AdaptivePercentile = options.AdaptivePercentile,
            WeakEdgeRatio = options.WeakEdgeRatio,
            MinimumComponentPixels = options.MinimumComponentPixels,
            MinimumSourceAlpha = options.MinimumSourceAlpha
        });
        var luminance = BuildLuminance(source);
        var smoothed = BoxBlur(luminance, source.Width, source.Height, radius: 2);
        var pixels = new RgbaPixel[source.PixelCount];

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var index = (y * source.Width) + x;
                var sourcePixel = source.Pixels[index];
                if (contours.Pixels[index].IsOpaque)
                {
                    pixels[index] = RgbaPixel.Opaque(RgbColor.Black);
                    continue;
                }

                if (sourcePixel.Alpha < options.MinimumSourceAlpha)
                {
                    continue;
                }

                var tone = smoothed[index];
                var detail = Math.Abs(luminance[index] - tone);
                if (ShouldHatch(x, y, tone, detail))
                {
                    pixels[index] = RgbaPixel.Opaque(RgbColor.Black);
                }
            }
        }

        return new ImageFrame(source.Width, source.Height, pixels);
    }

    private static bool ShouldHatch(int x, int y, double luminance, double localDetail)
    {
        // Use one dominant stroke direction per tonal band. The old two-way
        // cross hatch produced a distracting wire-grid over hair and clothes;
        // portrait sketches read more naturally with tapered, parallel value
        // strokes plus the extracted feature contours.
        if (luminance < 62d)
        {
            return PositiveModulo(x + (2 * y), 5) == 0;
        }

        if (luminance < 104d)
        {
            return PositiveModulo(x + (2 * y), 7) == 0;
        }

        if (luminance < 146d)
        {
            return localDetail >= 2d && PositiveModulo(x + (2 * y), 10) == 0;
        }

        if (luminance < 184d && localDetail >= 4d)
        {
            return PositiveModulo(x + (2 * y), 14) == 0;
        }

        return luminance < 214d && localDetail >= 12d && PositiveModulo((2 * x) + y, 17) == 0;
    }

    private static int PositiveModulo(int value, int modulus)
        => ((value % modulus) + modulus) % modulus;

    private static double[] BuildLuminance(ImageFrame source)
    {
        var values = new double[source.PixelCount];
        for (var index = 0; index < values.Length; index++)
        {
            var pixel = source.Pixels[index];
            var alpha = pixel.Alpha / 255d;
            var value = (0.2126d * pixel.Color.R) + (0.7152d * pixel.Color.G) + (0.0722d * pixel.Color.B);
            values[index] = (value * alpha) + (255d * (1d - alpha));
        }

        return values;
    }

    private static double[] BoxBlur(double[] source, int width, int height, int radius)
    {
        var result = new double[source.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var total = 0d;
                var count = 0;
                for (var offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    var sampleY = Math.Clamp(y + offsetY, 0, height - 1);
                    for (var offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        var sampleX = Math.Clamp(x + offsetX, 0, width - 1);
                        total += source[(sampleY * width) + sampleX];
                        count++;
                    }
                }

                result[(y * width) + x] = total / count;
            }
        }

        return result;
    }
}
