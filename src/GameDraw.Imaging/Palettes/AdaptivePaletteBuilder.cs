using GameDraw.Core.Colors;
using GameDraw.Core.Imaging;
using GameDraw.Imaging.Color;

namespace GameDraw.Imaging.Palettes;

public sealed record PaletteBuildOptions
{
    public int MaxColors { get; init; } = 16;

    public int MaxSamples { get; init; } = 100_000;

    public byte MinimumAlpha { get; init; } = 1;

    public bool IncludeBackground { get; init; }

    public RgbColor BackgroundColor { get; init; } = RgbColor.White;

    public void Validate()
    {
        if (MaxColors is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxColors), "팔레트 색상 수는 1~256이어야 합니다.");
        }

        if (MaxSamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSamples));
        }
    }
}

public sealed class AdaptivePaletteBuilder
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Kept as an instance service so the pipeline can replace it with a profile-specific builder.")]
    public ColorPalette Build(ImageFrame frame, PaletteBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        options ??= new PaletteBuildOptions();
        options.Validate();

        var samples = CollectSamples(frame, options);
        if (samples.Count == 0)
        {
            return new ColorPalette(new[] { options.BackgroundColor }, "adaptive-empty");
        }

        var distinct = samples.Distinct().ToList();
        if (distinct.Count <= options.MaxColors)
        {
            if (options.IncludeBackground && !distinct.Contains(options.BackgroundColor))
            {
                distinct.Insert(0, options.BackgroundColor);
                if (distinct.Count > options.MaxColors)
                {
                    distinct.RemoveAt(distinct.Count - 1);
                }
            }

            return new ColorPalette(distinct, "adaptive");
        }

        var boxes = new List<ColorBox> { new(samples) };
        while (boxes.Count < options.MaxColors)
        {
            var splitIndex = FindSplittableBox(boxes);
            if (splitIndex < 0)
            {
                break;
            }

            var box = boxes[splitIndex];
            var split = box.Split();
            boxes[splitIndex] = split.Left;
            boxes.Insert(splitIndex + 1, split.Right);
        }

        var colors = boxes
            .Select(box => box.AverageLinear())
            .Distinct()
            .ToList();

        if (options.IncludeBackground && !colors.Contains(options.BackgroundColor))
        {
            colors.Insert(0, options.BackgroundColor);
            if (colors.Count > options.MaxColors)
            {
                colors.RemoveAt(colors.Count - 1);
            }
        }

        return new ColorPalette(colors, "adaptive");
    }

    private static List<RgbColor> CollectSamples(ImageFrame frame, PaletteBuildOptions options)
    {
        var samples = new List<RgbColor>(Math.Min(frame.PixelCount, options.MaxSamples));
        var stride = Math.Max(1, (int)Math.Ceiling(frame.PixelCount / (double)options.MaxSamples));

        for (var index = 0; index < frame.PixelCount; index += stride)
        {
            var pixel = frame.Pixels[index];
            if (pixel.Alpha < options.MinimumAlpha)
            {
                continue;
            }

            samples.Add(pixel.Color);
        }

        return samples;
    }

    private static int FindSplittableBox(IReadOnlyList<ColorBox> boxes)
    {
        var index = -1;
        var largestRange = -1;
        for (var candidate = 0; candidate < boxes.Count; candidate++)
        {
            if (boxes[candidate].Colors.Count < 2)
            {
                continue;
            }

            var range = boxes[candidate].LargestRange;
            if (range > largestRange)
            {
                largestRange = range;
                index = candidate;
            }
        }

        return index;
    }

    private sealed class ColorBox
    {
        public ColorBox(IReadOnlyList<RgbColor> colors)
        {
            Colors = colors.OrderBy(color => color.R).ThenBy(color => color.G).ThenBy(color => color.B).ToList();
            MinR = Colors.Min(color => color.R);
            MaxR = Colors.Max(color => color.R);
            MinG = Colors.Min(color => color.G);
            MaxG = Colors.Max(color => color.G);
            MinB = Colors.Min(color => color.B);
            MaxB = Colors.Max(color => color.B);
        }

        public List<RgbColor> Colors { get; }

        public int MinR { get; }
        public int MaxR { get; }
        public int MinG { get; }
        public int MaxG { get; }
        public int MinB { get; }
        public int MaxB { get; }

        public int LargestRange => Math.Max(MaxR - MinR, Math.Max(MaxG - MinG, MaxB - MinB));

        public (ColorBox Left, ColorBox Right) Split()
        {
            var channel = LargestChannel();
            var sorted = channel switch
            {
                0 => Colors.OrderBy(color => color.R).ThenBy(color => color.G).ThenBy(color => color.B).ToList(),
                1 => Colors.OrderBy(color => color.G).ThenBy(color => color.R).ThenBy(color => color.B).ToList(),
                _ => Colors.OrderBy(color => color.B).ThenBy(color => color.R).ThenBy(color => color.G).ToList()
            };
            var splitAt = sorted.Count / 2;
            return (new ColorBox(sorted.Take(splitAt).ToList()), new ColorBox(sorted.Skip(splitAt).ToList()));
        }

        public RgbColor AverageLinear()
        {
            var totalR = 0d;
            var totalG = 0d;
            var totalB = 0d;
            foreach (var color in Colors)
            {
                var linear = ColorMath.ToLinear(color);
                totalR += linear.R;
                totalG += linear.G;
                totalB += linear.B;
            }

            var count = Colors.Count;
            return ColorMath.FromLinear(new LinearRgb(totalR / count, totalG / count, totalB / count));
        }

        private int LargestChannel()
        {
            var redRange = MaxR - MinR;
            var greenRange = MaxG - MinG;
            var blueRange = MaxB - MinB;
            if (redRange >= greenRange && redRange >= blueRange)
            {
                return 0;
            }

            return greenRange >= blueRange ? 1 : 2;
        }
    }
}
