using GameDraw.Core.Colors;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GameDraw.Core.Imaging;

public sealed class ImageProcessor
{
    public async Task<ImageBuffer> LoadAsync(string path, int maxDimension = 4096, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected image could not be found.", path);
        }

        using var image = await Image.LoadAsync<Rgba32>(path, cancellationToken).ConfigureAwait(false);
        image.Mutate(context => context.AutoOrient());

        if (maxDimension > 0 && Math.Max(image.Width, image.Height) > maxDimension)
        {
            var scale = maxDimension / (double)Math.Max(image.Width, image.Height);
            var size = new Size(
                Math.Max(1, (int)Math.Round(image.Width * scale)),
                Math.Max(1, (int)Math.Round(image.Height * scale)));
            image.Mutate(context => context.Resize(size));
        }

        var buffer = new ImageBuffer(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                buffer[x, y] = pixel.A < 10 ? null : new RgbColor(pixel.R, pixel.G, pixel.B);
            }
        }

        return buffer;
    }

    public ImageBuffer Process(ImageBuffer source, ImageProcessingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.TargetWidth <= 0 || options.TargetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Target dimensions must be positive.");
        }

        var fitted = FitToCanvas(source, options, cancellationToken);
        var palette = options.AdapterKind == ColorAdapterKind.FixedPalette && options.Palette.Count > 0
            ? options.Palette.Select(entry => entry.Color).Distinct().ToArray()
            : ColorQuantizer.BuildPalette(fitted, Math.Clamp(options.ColorCount, 1, 32));

        return ApplyPalette(fitted, palette, options.Dithering, cancellationToken);
    }

    private static ImageBuffer FitToCanvas(ImageBuffer source, ImageProcessingOptions options, CancellationToken cancellationToken)
    {
        var target = new ImageBuffer(options.TargetWidth, options.TargetHeight);
        var scale = options.Fit == FitMode.Stretch
            ? 1d
            : Math.Min(options.TargetWidth / (double)source.Width, options.TargetHeight / (double)source.Height);
        var fittedWidth = options.Fit == FitMode.Stretch
            ? options.TargetWidth
            : Math.Max(1, (int)Math.Round(source.Width * scale));
        var fittedHeight = options.Fit == FitMode.Stretch
            ? options.TargetHeight
            : Math.Max(1, (int)Math.Round(source.Height * scale));
        var offsetX = (options.TargetWidth - fittedWidth) / 2;
        var offsetY = (options.TargetHeight - fittedHeight) / 2;

        for (var y = 0; y < options.TargetHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < options.TargetWidth; x++)
            {
                var sourceX = x - offsetX;
                var sourceY = y - offsetY;
                if (sourceX < 0 || sourceX >= fittedWidth || sourceY < 0 || sourceY >= fittedHeight)
                {
                    target[x, y] = null;
                    continue;
                }

                var originalX = Math.Min(source.Width - 1, (int)(sourceX * source.Width / (double)fittedWidth));
                var originalY = Math.Min(source.Height - 1, (int)(sourceY * source.Height / (double)fittedHeight));
                var color = source[originalX, originalY];
                target[x, y] = ShouldIgnore(color, options) ? null : color;
            }
        }

        return target;
    }

    private static bool ShouldIgnore(RgbColor? color, ImageProcessingOptions options)
    {
        if (color is null)
        {
            return true;
        }

        return options.Background switch
        {
            BackgroundMode.IgnoreWhite => color.Value.R >= 245 && color.Value.G >= 245 && color.Value.B >= 245,
            BackgroundMode.IgnoreCustomColor => ColorMath.DeltaE76(color.Value, options.CustomIgnoreColor) <= 4,
            BackgroundMode.IgnoreTransparent => false,
            _ => false
        };
    }

    private static ImageBuffer ApplyPalette(ImageBuffer source, IReadOnlyList<RgbColor> palette, bool dithering, CancellationToken cancellationToken)
    {
        var output = new ImageBuffer(source.Width, source.Height);
        if (palette.Count == 0)
        {
            return source.Clone();
        }

        var redError = dithering ? new double[source.Width, source.Height] : null;
        var greenError = dithering ? new double[source.Width, source.Height] : null;
        var blueError = dithering ? new double[source.Width, source.Height] : null;

        for (var y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Width; x++)
            {
                var sourceColor = source[x, y];
                if (sourceColor is not { } color)
                {
                    output[x, y] = null;
                    continue;
                }

                var red = Math.Clamp(color.R + (redError?[x, y] ?? 0d), 0d, 255d);
                var green = Math.Clamp(color.G + (greenError?[x, y] ?? 0d), 0d, 255d);
                var blue = Math.Clamp(color.B + (blueError?[x, y] ?? 0d), 0d, 255d);
                var adjusted = new RgbColor((byte)Math.Round(red), (byte)Math.Round(green), (byte)Math.Round(blue));
                var nearest = ColorMath.FindNearest(adjusted, palette);
                output[x, y] = nearest;

                if (!dithering)
                {
                    continue;
                }

                var er = red - nearest.R;
                var eg = green - nearest.G;
                var eb = blue - nearest.B;
                AddError(redError!, greenError!, blueError!, x + 1, y, er, eg, eb, 7d / 16d);
                AddError(redError!, greenError!, blueError!, x - 1, y + 1, er, eg, eb, 3d / 16d);
                AddError(redError!, greenError!, blueError!, x, y + 1, er, eg, eb, 5d / 16d);
                AddError(redError!, greenError!, blueError!, x + 1, y + 1, er, eg, eb, 1d / 16d);
            }
        }

        return output;
    }

    private static void AddError(double[,] red, double[,] green, double[,] blue, int x, int y, double er, double eg, double eb, double factor)
    {
        if (x < 0 || y < 0 || x >= red.GetLength(0) || y >= red.GetLength(1))
        {
            return;
        }

        red[x, y] += er * factor;
        green[x, y] += eg * factor;
        blue[x, y] += eb * factor;
    }
}

internal static class ColorQuantizer
{
    public static IReadOnlyList<RgbColor> BuildPalette(ImageBuffer image, int maxColors)
    {
        var colors = image.Pixels
            .Where(pixel => pixel is not null)
            .Select(pixel => pixel!.Value)
            .Distinct()
            .ToList();

        if (colors.Count <= maxColors)
        {
            return colors;
        }

        colors.Sort((left, right) =>
        {
            var leftHsv = left.ToHsv();
            var rightHsv = right.ToHsv();
            var hue = leftHsv.Hue.CompareTo(rightHsv.Hue);
            return hue != 0 ? hue : leftHsv.Value.CompareTo(rightHsv.Value);
        });

        var palette = new List<RgbColor>(maxColors);
        for (var index = 0; index < maxColors; index++)
        {
            var sourceIndex = (int)Math.Round(index * (colors.Count - 1d) / Math.Max(1, maxColors - 1));
            palette.Add(colors[sourceIndex]);
        }

        return palette.Distinct().ToArray();
    }
}
