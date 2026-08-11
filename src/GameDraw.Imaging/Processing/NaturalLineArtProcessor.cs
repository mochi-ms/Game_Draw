using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Processing;

public sealed record NaturalLineArtOptions
{
    public double DetailLevel { get; init; } = 0.62d;

    public byte MinimumSourceAlpha { get; init; } = 8;

    public int MinimumComponentPixels { get; init; } = 5;

    public void Validate()
    {
        if (!double.IsFinite(DetailLevel) || DetailLevel is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(DetailLevel));
        }

        if (MinimumComponentPixels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumComponentPixels));
        }
    }
}

/// <summary>
/// Extracts dark authored marks rather than the two outside edges of every
/// mark. The resulting ink bands are intentionally consumed by the clean
/// stroke planner's skeleton pass, producing one centreline per pen mark.
/// A Difference-of-Gaussians response retains subtle facial and hair detail
/// while component filtering rejects texture and compression speckles.
/// </summary>
public static class NaturalLineArtProcessor
{
    public static ImageFrame Extract(ImageFrame source, NaturalLineArtOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new NaturalLineArtOptions();
        options.Validate();
        if (source.Width < 3 || source.Height < 3)
        {
            return new ImageFrame(source.Width, source.Height,
                Enumerable.Repeat(RgbaPixel.Transparent, source.PixelCount).ToArray());
        }

        var raw = BuildLuminance(source);
        var fine = Blur(raw, source.Width, source.Height, [1, 2, 1]);
        var broad = Blur(raw, source.Width, source.Height, [1, 4, 6, 4, 1]);
        var response = new double[source.PixelCount];
        for (var y = 1; y < source.Height - 1; y++)
        {
            for (var x = 1; x < source.Width - 1; x++)
            {
                var index = (y * source.Width) + x;
                if (source.Pixels[index].Alpha < options.MinimumSourceAlpha)
                {
                    continue;
                }

                var dog = Math.Max(0d, broad[index] - fine[index]);
                var darkness = Math.Max(0d, 176d - fine[index]) * 0.055d;
                var gx = Math.Abs(fine[index + 1] - fine[index - 1]);
                var gy = Math.Abs(fine[index + source.Width] - fine[index - source.Width]);
                var edgeSupport = Math.Sqrt((gx * gx) + (gy * gy)) * 0.12d;
                response[index] = (dog * 1.85d) + darkness + edgeSupport;
            }
        }

        var positive = response.Where(value => value > 0.35d).OrderBy(value => value).ToArray();
        var percentile = 0.82d - (options.DetailLevel * 0.34d);
        var adaptive = positive.Length == 0
            ? double.MaxValue
            : positive[Math.Clamp(
                (int)Math.Round((positive.Length - 1) * percentile),
                0,
                positive.Length - 1)];
        var minimum = 5.8d - (options.DetailLevel * 2.8d);
        var threshold = Math.Max(minimum, adaptive);
        var ink = response.Select(value => value >= threshold).ToArray();

        BridgeSinglePixelGaps(ink, source.Width, source.Height);
        RemoveSmallComponents(ink, source.Width, source.Height, options.MinimumComponentPixels);

        var output = new RgbaPixel[source.PixelCount];
        for (var index = 0; index < output.Length; index++)
        {
            output[index] = ink[index] ? RgbaPixel.Opaque(RgbColor.Black) : RgbaPixel.Transparent;
        }

        return new ImageFrame(source.Width, source.Height, output);
    }

    private static double[] BuildLuminance(ImageFrame source)
    {
        var result = new double[source.PixelCount];
        for (var index = 0; index < result.Length; index++)
        {
            var pixel = source.Pixels[index];
            var alpha = pixel.Alpha / 255d;
            var luminance = (0.2126d * pixel.Color.R) + (0.7152d * pixel.Color.G) + (0.0722d * pixel.Color.B);
            result[index] = (luminance * alpha) + (255d * (1d - alpha));
        }

        return result;
    }

    private static double[] Blur(double[] source, int width, int height, int[] kernel)
    {
        var radius = kernel.Length / 2;
        var divisor = kernel.Sum();
        var horizontal = new double[source.Length];
        var result = new double[source.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var total = 0d;
                for (var offset = -radius; offset <= radius; offset++)
                {
                    total += source[(y * width) + Math.Clamp(x + offset, 0, width - 1)] * kernel[offset + radius];
                }

                horizontal[(y * width) + x] = total / divisor;
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var total = 0d;
                for (var offset = -radius; offset <= radius; offset++)
                {
                    total += horizontal[(Math.Clamp(y + offset, 0, height - 1) * width) + x] * kernel[offset + radius];
                }

                result[(y * width) + x] = total / divisor;
            }
        }

        return result;
    }

    private static void BridgeSinglePixelGaps(bool[] ink, int width, int height)
    {
        var additions = new List<int>();
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var index = (y * width) + x;
                if (ink[index])
                {
                    continue;
                }

                if ((ink[index - 1] && ink[index + 1]) ||
                    (ink[index - width] && ink[index + width]) ||
                    (ink[index - width - 1] && ink[index + width + 1]) ||
                    (ink[index - width + 1] && ink[index + width - 1]))
                {
                    additions.Add(index);
                }
            }
        }

        foreach (var index in additions)
        {
            ink[index] = true;
        }
    }

    private static void RemoveSmallComponents(bool[] ink, int width, int height, int minimumPixels)
    {
        var visited = new bool[ink.Length];
        var queue = new Queue<int>();
        var component = new List<int>();
        for (var start = 0; start < ink.Length; start++)
        {
            if (!ink[start] || visited[start])
            {
                continue;
            }

            component.Clear();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var index))
            {
                component.Add(index);
                var x = index % width;
                var y = index / width;
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if ((offsetX == 0 && offsetY == 0) ||
                            (uint)(x + offsetX) >= (uint)width ||
                            (uint)(y + offsetY) >= (uint)height)
                        {
                            continue;
                        }

                        var neighbor = ((y + offsetY) * width) + x + offsetX;
                        if (ink[neighbor] && !visited[neighbor])
                        {
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            if (component.Count < minimumPixels)
            {
                foreach (var index in component)
                {
                    ink[index] = false;
                }
            }
        }
    }
}
