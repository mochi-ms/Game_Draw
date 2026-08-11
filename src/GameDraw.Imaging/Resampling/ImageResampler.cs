using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Color;

namespace GameDraw.Imaging.Resampling;

public enum ResamplingFilter
{
    Nearest = 0,
    Bilinear = 1,
    Bicubic = 2,
    Lanczos3 = 3
}

public sealed record ResamplingOptions
{
    public ResamplingFilter Filter { get; init; } = ResamplingFilter.Lanczos3;

    public bool UseLinearLight { get; init; } = true;

    public bool PreserveAlpha { get; init; } = true;

    public void Validate()
    {
        if (!Enum.IsDefined(Filter))
        {
            throw new ArgumentOutOfRangeException(nameof(Filter));
        }
    }
}

public static class ImageResampler
{
    public static ImageFrame Resize(
        ImageFrame source,
        PixelSize targetSize,
        ResamplingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new ResamplingOptions();
        options.Validate();

        if (source.Width == targetSize.Width && source.Height == targetSize.Height)
        {
            return source.Clone();
        }

        var horizontal = ResampleHorizontal(source, targetSize.Width, options);
        return ResampleVertical(horizontal, source.Height, targetSize.Height, options);
    }

    private static LinearPixel[] ResampleHorizontal(
        ImageFrame source,
        int targetWidth,
        ResamplingOptions options)
    {
        var contributions = BuildContributions(source.Width, targetWidth, options.Filter);
        var output = new LinearPixel[checked(targetWidth * source.Height)];

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < targetWidth; x++)
            {
                var sumR = 0d;
                var sumG = 0d;
                var sumB = 0d;
                var sumA = 0d;

                foreach (var contribution in contributions[x])
                {
                    var pixel = source[contribution.SourceIndex, y];
                    var alpha = pixel.Alpha / 255d;
                    var rgb = ToWorkingRgb(pixel.Color, options.UseLinearLight);
                    sumR += rgb.R * alpha * contribution.Weight;
                    sumG += rgb.G * alpha * contribution.Weight;
                    sumB += rgb.B * alpha * contribution.Weight;
                    sumA += alpha * contribution.Weight;
                }

                output[(y * targetWidth) + x] = new LinearPixel(sumR, sumG, sumB, Math.Clamp(sumA, 0d, 1d));
            }
        }

        return output;
    }

    private static ImageFrame ResampleVertical(
        LinearPixel[] horizontal,
        int horizontalHeight,
        int targetHeight,
        ResamplingOptions options)
    {
        var targetWidth = horizontal.Length / horizontalHeight;
        var contributions = BuildContributions(horizontalHeight, targetHeight, options.Filter);
        var output = new RgbaPixel[checked(targetWidth * targetHeight)];

        for (var y = 0; y < targetHeight; y++)
        {
            for (var x = 0; x < targetWidth; x++)
            {
                var sumR = 0d;
                var sumG = 0d;
                var sumB = 0d;
                var sumA = 0d;

                foreach (var contribution in contributions[y])
                {
                    var pixel = horizontal[(contribution.SourceIndex * targetWidth) + x];
                    sumR += pixel.R * contribution.Weight;
                    sumG += pixel.G * contribution.Weight;
                    sumB += pixel.B * contribution.Weight;
                    sumA += pixel.A * contribution.Weight;
                }

                var alpha = Math.Clamp(sumA, 0d, 1d);
                var rgb = alpha > 0.000001d
                    ? new LinearRgb(sumR / alpha, sumG / alpha, sumB / alpha)
                    : new LinearRgb(0d, 0d, 0d);
                var color = FromWorkingRgb(rgb, options.UseLinearLight);
                var alphaByte = options.PreserveAlpha
                    ? (byte)Math.Clamp(Math.Round(alpha * 255d, MidpointRounding.ToEven), 0d, 255d)
                    : byte.MaxValue;
                output[(y * targetWidth) + x] = new RgbaPixel(color, alphaByte);
            }
        }

        return new ImageFrame(targetWidth, targetHeight, output);
    }

    private static Contribution[][] BuildContributions(
        int sourceLength,
        int targetLength,
        ResamplingFilter filter)
    {
        var result = new Contribution[targetLength][];
        for (var targetIndex = 0; targetIndex < targetLength; targetIndex++)
        {
            if (filter == ResamplingFilter.Nearest)
            {
                var nearest = Math.Clamp(
                    (int)Math.Floor(((targetIndex + 0.5d) * sourceLength / targetLength)),
                    0,
                    sourceLength - 1);
                result[targetIndex] = new[] { new Contribution(nearest, 1d) };
                continue;
            }

            var scale = Math.Min(1d, targetLength / (double)sourceLength);
            var radius = FilterRadius(filter) / scale;
            var center = ((targetIndex + 0.5d) * sourceLength / targetLength) - 0.5d;
            var start = Math.Max(0, (int)Math.Ceiling(center - radius));
            var end = Math.Min(sourceLength - 1, (int)Math.Floor(center + radius));
            var contributions = new List<Contribution>(Math.Max(1, end - start + 1));
            var total = 0d;

            for (var sourceIndex = start; sourceIndex <= end; sourceIndex++)
            {
                var distance = sourceIndex - center;
                var weight = FilterWeight(filter, distance * scale) * scale;
                if (Math.Abs(weight) < 0.000000001d)
                {
                    continue;
                }

                contributions.Add(new Contribution(sourceIndex, weight));
                total += weight;
            }

            if (Math.Abs(total) < 0.000000001d)
            {
                result[targetIndex] = new[] { new Contribution(Math.Clamp((int)Math.Round(center), 0, sourceLength - 1), 1d) };
            }
            else
            {
                result[targetIndex] = contributions
                    .Select(item => new Contribution(item.SourceIndex, item.Weight / total))
                    .ToArray();
            }
        }

        return result;
    }

    private static double FilterRadius(ResamplingFilter filter)
        => filter switch
        {
            ResamplingFilter.Bilinear => 1d,
            ResamplingFilter.Bicubic => 2d,
            ResamplingFilter.Lanczos3 => 3d,
            _ => 0.5d
        };

    private static double FilterWeight(ResamplingFilter filter, double value)
    {
        var distance = Math.Abs(value);
        return filter switch
        {
            ResamplingFilter.Bilinear => Math.Max(0d, 1d - distance),
            ResamplingFilter.Bicubic => Bicubic(distance),
            ResamplingFilter.Lanczos3 => Lanczos(distance, 3d),
            _ => distance < 0.5d ? 1d : 0d
        };
    }

    private static double Bicubic(double distance)
    {
        if (distance <= 1d)
        {
            return (1.5d * distance * distance * distance) - (2.5d * distance * distance) + 1d;
        }

        if (distance < 2d)
        {
            return (-0.5d * distance * distance * distance) + (2.5d * distance * distance) - (4d * distance) + 2d;
        }

        return 0d;
    }

    private static double Lanczos(double distance, double radius)
    {
        if (distance < 0.000000001d)
        {
            return 1d;
        }

        if (distance >= radius)
        {
            return 0d;
        }

        var piDistance = Math.PI * distance;
        return (Math.Sin(piDistance) / piDistance)
            * (Math.Sin(piDistance / radius) / (piDistance / radius));
    }

    private static LinearRgb ToWorkingRgb(GameDraw.Core.Colors.RgbColor color, bool linearLight)
    {
        if (linearLight)
        {
            return ColorMath.ToLinear(color);
        }

        return new LinearRgb(color.R / 255d, color.G / 255d, color.B / 255d);
    }

    private static GameDraw.Core.Colors.RgbColor FromWorkingRgb(LinearRgb color, bool linearLight)
    {
        if (linearLight)
        {
            return ColorMath.FromLinear(color);
        }

        return new GameDraw.Core.Colors.RgbColor(
            (byte)Math.Clamp(Math.Round(color.R * 255d, MidpointRounding.ToEven), 0d, 255d),
            (byte)Math.Clamp(Math.Round(color.G * 255d, MidpointRounding.ToEven), 0d, 255d),
            (byte)Math.Clamp(Math.Round(color.B * 255d, MidpointRounding.ToEven), 0d, 255d));
    }

    private readonly record struct Contribution(int SourceIndex, double Weight);

    private readonly record struct LinearPixel(double R, double G, double B, double A);
}
