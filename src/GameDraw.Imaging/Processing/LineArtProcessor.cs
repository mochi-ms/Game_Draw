using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Processing;

public sealed record LineArtOptions
{
    public double EdgeThreshold { get; init; } = 54d;

    public byte MinimumSourceAlpha { get; init; } = 8;

    public double WeakEdgeRatio { get; init; } = 0.42d;

    public int MinimumComponentPixels { get; init; } = 6;

    public void Validate()
    {
        if (!double.IsFinite(EdgeThreshold) || EdgeThreshold is <= 0d or > 1_442d)
        {
            throw new ArgumentOutOfRangeException(nameof(EdgeThreshold));
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
/// Creates a transparent, one-pixel monochrome edge frame. A Gaussian pass
/// removes camera noise, non-maximum suppression prevents double/thick edges,
/// and hysteresis keeps weak detail only when it belongs to a real contour.
/// </summary>
public static class LineArtProcessor
{
    public static ImageFrame Extract(ImageFrame source, LineArtOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new LineArtOptions();
        options.Validate();
        if (source.Width < 3 || source.Height < 3)
        {
            return new ImageFrame(
                source.Width,
                source.Height,
                Enumerable.Repeat(RgbaPixel.Transparent, source.PixelCount).ToArray());
        }

        var luminance = BuildBlurredLuminance(source);
        var magnitude = new double[source.PixelCount];
        var direction = new byte[source.PixelCount];
        for (var y = 1; y < source.Height - 1; y++)
        {
            for (var x = 1; x < source.Width - 1; x++)
            {
                if (source[x, y].Alpha < options.MinimumSourceAlpha)
                {
                    continue;
                }

                var gx =
                    -L(x - 1, y - 1) + L(x + 1, y - 1)
                    - (2d * L(x - 1, y)) + (2d * L(x + 1, y))
                    - L(x - 1, y + 1) + L(x + 1, y + 1);
                var gy =
                    -L(x - 1, y - 1) - (2d * L(x, y - 1)) - L(x + 1, y - 1)
                    + L(x - 1, y + 1) + (2d * L(x, y + 1)) + L(x + 1, y + 1);
                var index = (y * source.Width) + x;
                magnitude[index] = Math.Sqrt((gx * gx) + (gy * gy));
                direction[index] = QuantizeDirection(Math.Atan2(gy, gx));
            }
        }

        var suppressed = new double[source.PixelCount];
        for (var y = 1; y < source.Height - 1; y++)
        {
            for (var x = 1; x < source.Width - 1; x++)
            {
                var index = (y * source.Width) + x;
                var value = magnitude[index];
                var (first, second) = direction[index] switch
                {
                    0 => (magnitude[index - 1], magnitude[index + 1]),
                    1 => (magnitude[index - source.Width - 1], magnitude[index + source.Width + 1]),
                    2 => (magnitude[index - source.Width], magnitude[index + source.Width]),
                    _ => (magnitude[index - source.Width + 1], magnitude[index + source.Width - 1])
                };
                if (value >= first && value >= second)
                {
                    suppressed[index] = value;
                }
            }
        }

        var adaptiveThreshold = AdaptiveHighThreshold(
            suppressed,
            source.PixelCount < 256 ? options.EdgeThreshold * 0.15d : options.EdgeThreshold);
        var edges = Hysteresis(suppressed, source.Width, source.Height, adaptiveThreshold, options.WeakEdgeRatio);
        RemoveSmallComponents(
            edges,
            source.Width,
            source.Height,
            source.PixelCount < 256 ? 1 : options.MinimumComponentPixels);
        var output = new RgbaPixel[source.PixelCount];
        for (var index = 0; index < output.Length; index++)
        {
            output[index] = edges[index] ? RgbaPixel.Opaque(RgbColor.Black) : RgbaPixel.Transparent;
        }

        return new ImageFrame(source.Width, source.Height, output);

        double L(int x, int y) => luminance[(y * source.Width) + x];
    }

    private static double[] BuildBlurredLuminance(ImageFrame source)
    {
        var raw = new double[source.PixelCount];
        for (var index = 0; index < source.PixelCount; index++)
        {
            var pixel = source.Pixels[index];
            var alpha = pixel.Alpha / 255d;
            var value = (0.2126d * pixel.Color.R) + (0.7152d * pixel.Color.G) + (0.0722d * pixel.Color.B);
            raw[index] = (value * alpha) + (255d * (1d - alpha));
        }

        // A five-tap blur consumes almost the entire signal in icon-sized inputs.
        // Keep their real contrast so a deliberate one-pixel boundary is not erased.
        if (source.Width < 8 || source.Height < 8)
        {
            return raw;
        }

        var horizontal = new double[raw.Length];
        ReadOnlySpan<int> kernel = [1, 4, 6, 4, 1];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var total = 0d;
                for (var offset = -2; offset <= 2; offset++)
                {
                    var sampleX = Math.Clamp(x + offset, 0, source.Width - 1);
                    total += raw[(y * source.Width) + sampleX] * kernel[offset + 2];
                }

                horizontal[(y * source.Width) + x] = total / 16d;
            }
        }

        var blurred = new double[raw.Length];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var total = 0d;
                for (var offset = -2; offset <= 2; offset++)
                {
                    var sampleY = Math.Clamp(y + offset, 0, source.Height - 1);
                    total += horizontal[(sampleY * source.Width) + x] * kernel[offset + 2];
                }

                blurred[(y * source.Width) + x] = total / 16d;
            }
        }

        return blurred;
    }

    private static byte QuantizeDirection(double radians)
    {
        var degrees = (radians * 180d / Math.PI + 180d) % 180d;
        return degrees switch
        {
            < 22.5d or >= 157.5d => 0,
            < 67.5d => 1,
            < 112.5d => 2,
            _ => 3
        };
    }

    private static bool[] Hysteresis(
        double[] magnitude,
        int width,
        int height,
        double highThreshold,
        double weakRatio)
    {
        var edges = new bool[magnitude.Length];
        var queued = new bool[magnitude.Length];
        var queue = new Queue<int>();
        for (var index = 0; index < magnitude.Length; index++)
        {
            if (magnitude[index] >= highThreshold)
            {
                queued[index] = true;
                edges[index] = true;
                queue.Enqueue(index);
            }
        }

        var lowThreshold = highThreshold * weakRatio;
        while (queue.TryDequeue(out var index))
        {
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
                    if (!queued[neighbor] && magnitude[neighbor] >= lowThreshold)
                    {
                        queued[neighbor] = true;
                        edges[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        return edges;
    }

    private static double AdaptiveHighThreshold(double[] magnitude, double minimum)
    {
        var values = magnitude.Where(value => value > 0d).OrderBy(value => value).ToArray();
        if (values.Length == 0)
        {
            return minimum;
        }

        var density = values.Length / (double)magnitude.Length;
        var percentile = density >= 0.16d ? 0.9d : density >= 0.08d ? 0.84d : 0.72d;
        var index = Math.Clamp((int)Math.Round((values.Length - 1) * percentile), 0, values.Length - 1);
        return Math.Max(minimum, values[index]);
    }

    private static void RemoveSmallComponents(bool[] edges, int width, int height, int minimumPixels)
    {
        var visited = new bool[edges.Length];
        var queue = new Queue<int>();
        var component = new List<int>();
        for (var start = 0; start < edges.Length; start++)
        {
            if (!edges[start] || visited[start])
            {
                continue;
            }

            component.Clear();
            var touchesBorder = false;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var index))
            {
                component.Add(index);
                var x = index % width;
                var y = index / width;
                touchesBorder |= x <= 1 || y <= 1 || x >= width - 2 || y >= height - 2;
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
                        if (edges[neighbor] && !visited[neighbor])
                        {
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            var borderArtifactLimit = Math.Max(24, (width + height) / 16);
            if (component.Count < minimumPixels ||
                (edges.Length >= 256 && touchesBorder && component.Count < borderArtifactLimit))
            {
                foreach (var index in component)
                {
                    edges[index] = false;
                }
            }
        }
    }
}
