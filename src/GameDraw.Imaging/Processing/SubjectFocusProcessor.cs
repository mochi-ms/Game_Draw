using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Processing;

public sealed record SubjectFocusOptions
{
    public double BackgroundTolerance { get; init; } = 52d;

    public double CropMarginRatio { get; init; } = 0.07d;

    public double MinimumForegroundRatio { get; init; } = 0.012d;

    public double MaximumForegroundRatio { get; init; } = 0.92d;

    public void Validate()
    {
        if (!double.IsFinite(BackgroundTolerance) || BackgroundTolerance is <= 0d or > 441.7d)
        {
            throw new ArgumentOutOfRangeException(nameof(BackgroundTolerance));
        }

        if (!double.IsFinite(CropMarginRatio) || CropMarginRatio is < 0d or > 0.4d)
        {
            throw new ArgumentOutOfRangeException(nameof(CropMarginRatio));
        }

        if (!double.IsFinite(MinimumForegroundRatio) ||
            !double.IsFinite(MaximumForegroundRatio) ||
            MinimumForegroundRatio is < 0d or >= 1d ||
            MaximumForegroundRatio is <= 0d or > 1d ||
            MinimumForegroundRatio >= MaximumForegroundRatio)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumForegroundRatio));
        }
    }
}

public sealed record SubjectFocusResult(
    ImageFrame Frame,
    PixelRect SourceBounds,
    bool BackgroundRemoved,
    bool Cropped,
    bool PersonLikely,
    NormalizedRect? FacePriorityRegion)
{
    public static SubjectFocusResult Unchanged(ImageFrame source)
        => new(
            source.Clone(),
            new PixelRect(0, 0, source.Width, source.Height),
            false,
            false,
            false,
            null);
}

/// <summary>
/// Fully local subject isolation for drawings and portraits. The detector uses
/// the dominant border colour and a border-connected flood fill, so uniform or
/// softly varying backgrounds are removed without uploading the image or
/// requiring a heavyweight model. A conservative portrait-layout estimate is
/// also returned so the planner can draw facial features before the outer form.
/// </summary>
public static class SubjectFocusProcessor
{
    public static SubjectFocusResult Process(
        ImageFrame source,
        SubjectFocusOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new SubjectFocusOptions();
        options.Validate();
        if (source.Width < 8 || source.Height < 8)
        {
            return SubjectFocusResult.Unchanged(source);
        }

        var background = EstimateBorderColor(source);
        var backgroundMask = FloodBackground(source, background, options.BackgroundTolerance);
        var foregroundCount = backgroundMask.Count(value => !value);
        var foregroundRatio = foregroundCount / (double)source.PixelCount;
        if (foregroundRatio < options.MinimumForegroundRatio ||
            foregroundRatio > options.MaximumForegroundRatio)
        {
            return SubjectFocusResult.Unchanged(source);
        }

        var bounds = ForegroundBounds(backgroundMask, source.Width, source.Height);
        if (!bounds.IsValid)
        {
            return SubjectFocusResult.Unchanged(source);
        }

        var margin = Math.Max(2, (int)Math.Round(Math.Max(bounds.Width, bounds.Height) * options.CropMarginRatio));
        bounds = Expand(bounds, margin, source.Width, source.Height);
        var cropped = bounds.X > 0 || bounds.Y > 0 || bounds.Right < source.Width || bounds.Bottom < source.Height;
        var pixels = new RgbaPixel[checked(bounds.Width * bounds.Height)];
        for (var y = 0; y < bounds.Height; y++)
        {
            for (var x = 0; x < bounds.Width; x++)
            {
                var sourceX = bounds.X + x;
                var sourceY = bounds.Y + y;
                var sourceIndex = (sourceY * source.Width) + sourceX;
                var pixel = source[sourceX, sourceY];
                pixels[(y * bounds.Width) + x] = backgroundMask[sourceIndex]
                    ? new RgbaPixel(pixel.Color, 0)
                    : pixel;
            }
        }

        var frame = new ImageFrame(bounds.Width, bounds.Height, pixels);
        var subjectInCrop = ForegroundBounds(
            pixels.Select(pixel => pixel.Alpha == 0).ToArray(),
            frame.Width,
            frame.Height);
        var personLikely = LooksLikePortrait(subjectInCrop, frame.Width, frame.Height);
        NormalizedRect? face = personLikely
            ? EstimateFaceRegion(subjectInCrop, frame.Width, frame.Height)
            : null;
        return new SubjectFocusResult(frame, bounds, true, cropped, personLikely, face);
    }

    private static RgbColor EstimateBorderColor(ImageFrame source)
    {
        var reds = new List<byte>((source.Width + source.Height) * 2);
        var greens = new List<byte>(reds.Capacity);
        var blues = new List<byte>(reds.Capacity);
        AddRow(0);
        AddRow(source.Height - 1);
        for (var y = 1; y < source.Height - 1; y++)
        {
            Add(source[0, y]);
            Add(source[source.Width - 1, y]);
        }

        reds.Sort();
        greens.Sort();
        blues.Sort();
        if (reds.Count == 0)
        {
            return RgbColor.White;
        }

        var middle = reds.Count / 2;
        return new RgbColor(reds[middle], greens[middle], blues[middle]);

        void AddRow(int y)
        {
            for (var x = 0; x < source.Width; x++)
            {
                Add(source[x, y]);
            }
        }

        void Add(RgbaPixel pixel)
        {
            if (pixel.Alpha < 16)
            {
                return;
            }

            reds.Add(pixel.Color.R);
            greens.Add(pixel.Color.G);
            blues.Add(pixel.Color.B);
        }
    }

    private static bool[] FloodBackground(ImageFrame source, RgbColor background, double tolerance)
    {
        var backgroundMask = new bool[source.PixelCount];
        var queued = new bool[source.PixelCount];
        var queue = new Queue<int>();
        for (var x = 0; x < source.Width; x++)
        {
            Enqueue(x, 0);
            Enqueue(x, source.Height - 1);
        }

        for (var y = 1; y < source.Height - 1; y++)
        {
            Enqueue(0, y);
            Enqueue(source.Width - 1, y);
        }

        var toleranceSquared = tolerance * tolerance;
        while (queue.TryDequeue(out var index))
        {
            var x = index % source.Width;
            var y = index / source.Width;
            var pixel = source[x, y];
            if (pixel.Alpha >= 16 && ColorDistanceSquared(pixel.Color, background) > toleranceSquared)
            {
                continue;
            }

            backgroundMask[index] = true;
            if (x > 0) Enqueue(x - 1, y);
            if (x + 1 < source.Width) Enqueue(x + 1, y);
            if (y > 0) Enqueue(x, y - 1);
            if (y + 1 < source.Height) Enqueue(x, y + 1);
        }

        return backgroundMask;

        void Enqueue(int x, int y)
        {
            var index = (y * source.Width) + x;
            if (!queued[index])
            {
                queued[index] = true;
                queue.Enqueue(index);
            }
        }
    }

    private static PixelRect ForegroundBounds(bool[] backgroundMask, int width, int height)
    {
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (backgroundMask[(y * width) + x])
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left || bottom < top
            ? default
            : new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    private static PixelRect Expand(PixelRect bounds, int margin, int width, int height)
    {
        var left = Math.Max(0, bounds.X - margin);
        var top = Math.Max(0, bounds.Y - margin);
        var right = Math.Min(width, bounds.Right + margin);
        var bottom = Math.Min(height, bounds.Bottom + margin);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static bool LooksLikePortrait(PixelRect subject, int width, int height)
    {
        if (!subject.IsValid)
        {
            return false;
        }

        var aspect = subject.Height / (double)Math.Max(1, subject.Width);
        var centerOffset = Math.Abs(subject.Center.X - (width / 2d)) / width;
        var coverage = (subject.Width * subject.Height) / (double)(width * height);
        return aspect is >= 0.9d and <= 3.4d && centerOffset <= 0.2d && coverage >= 0.2d;
    }

    private static NormalizedRect EstimateFaceRegion(PixelRect subject, int width, int height)
    {
        var faceWidth = Math.Min(subject.Width * 0.72d, subject.Height * 0.58d);
        var faceHeight = Math.Min(subject.Height * 0.42d, faceWidth * 1.22d);
        var faceX = subject.Center.X - (faceWidth / 2d);
        var faceY = subject.Y + (subject.Height * 0.07d);
        var normalizedX = Math.Clamp(faceX / width, 0d, 1d);
        var normalizedY = Math.Clamp(faceY / height, 0d, 1d);
        return new NormalizedRect(
            normalizedX,
            normalizedY,
            Math.Clamp(faceWidth / width, 0d, 1d - normalizedX),
            Math.Clamp(faceHeight / height, 0d, 1d - normalizedY));
    }

    private static double ColorDistanceSquared(RgbColor first, RgbColor second)
    {
        var red = first.R - second.R;
        var green = first.G - second.G;
        var blue = first.B - second.B;
        return (red * red) + (green * green) + (blue * blue);
    }
}
