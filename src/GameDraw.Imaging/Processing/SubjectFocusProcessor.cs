using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Processing;

public sealed record SubjectFocusOptions
{
    public double BackgroundTolerance { get; init; } = 24d;

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

        var backgrounds = EstimateBorderColors(source);
        var backgroundMask = FloodBackground(source, backgrounds, options.BackgroundTolerance);
        KeepPrimarySubjectComponents(backgroundMask, source.Width, source.Height);
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

    private static RgbColor[] EstimateBorderColors(ImageFrame source)
    {
        var reds = new List<byte>((source.Width + source.Height) * 2);
        var greens = new List<byte>(reds.Capacity);
        var blues = new List<byte>(reds.Capacity);
        var bins = new Dictionary<int, BorderBin>();
        AddRow(0, 1);
        AddRow(source.Height - 1, 2);
        for (var y = 1; y < source.Height - 1; y++)
        {
            Add(source[0, y], 4);
            Add(source[source.Width - 1, y], 8);
        }

        reds.Sort();
        greens.Sort();
        blues.Sort();
        if (reds.Count == 0)
        {
            return new[] { RgbColor.White };
        }

        var middle = reds.Count / 2;
        var median = new RgbColor(reds[middle], greens[middle], blues[middle]);
        var colors = bins.Values
            // A foreground coat, hair, or prop can legitimately touch one
            // image edge and occupy more than 10% of the border. Requiring the
            // color on at least two different sides prevents that foreground
            // color from becoming a flood-fill background seed. The median
            // border color below still covers a uniform one-sided crop.
            .Where(bin => System.Numerics.BitOperations.PopCount((uint)bin.SideMask) >= 2)
            .OrderByDescending(bin => bin.Count)
            .Take(10)
            .Select(bin => new RgbColor(
                (byte)(bin.Red / bin.Count),
                (byte)(bin.Green / bin.Count),
                (byte)(bin.Blue / bin.Count)))
            // Multiple subject edges (for example a jacket touching left,
            // right, and bottom) are not background merely because they span
            // several sides. Accept border variants only near the robust
            // channel median, which represents the dominant background.
            .Where(color => ColorDistanceSquared(color, median) <= 48d * 48d)
            .ToList();
        colors.Add(median);
        return colors.Distinct().ToArray();

        void AddRow(int y, int side)
        {
            for (var x = 0; x < source.Width; x++)
            {
                Add(source[x, y], side);
            }
        }

        void Add(RgbaPixel pixel, int side)
        {
            if (pixel.Alpha < 16)
            {
                return;
            }

            reds.Add(pixel.Color.R);
            greens.Add(pixel.Color.G);
            blues.Add(pixel.Color.B);
            var key = ((pixel.Color.R >> 4) << 8) | ((pixel.Color.G >> 4) << 4) | (pixel.Color.B >> 4);
            bins.TryGetValue(key, out var bin);
            bins[key] = new BorderBin(
                bin.Red + pixel.Color.R,
                bin.Green + pixel.Color.G,
                bin.Blue + pixel.Color.B,
                bin.Count + 1,
                bin.SideMask | side);
        }
    }

    private static bool[] FloodBackground(
        ImageFrame source,
        IReadOnlyList<RgbColor> backgrounds,
        double tolerance)
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
            if (pixel.Alpha >= 16 &&
                backgrounds.All(background => ColorDistanceSquared(pixel.Color, background) > toleranceSquared))
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

    private static void KeepPrimarySubjectComponents(bool[] backgroundMask, int width, int height)
    {
        var labels = new int[backgroundMask.Length];
        Array.Fill(labels, -1);
        var components = new List<Component>();
        var queue = new Queue<int>();
        for (var start = 0; start < backgroundMask.Length; start++)
        {
            if (backgroundMask[start] || labels[start] >= 0)
            {
                continue;
            }

            var label = components.Count;
            var area = 0;
            var left = width;
            var top = height;
            var right = -1;
            var bottom = -1;
            labels[start] = label;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var index))
            {
                var x = index % width;
                var y = index / width;
                area++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
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
                        if (!backgroundMask[neighbor] && labels[neighbor] < 0)
                        {
                            labels[neighbor] = label;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            components.Add(new Component(area, new PixelRect(left, top, right - left + 1, bottom - top + 1)));
        }

        if (components.Count <= 1)
        {
            return;
        }

        var imageCenter = new PixelPoint(width / 2, height / 2);
        var primaryIndex = Enumerable.Range(0, components.Count)
            .OrderByDescending(index => ComponentScore(components[index], imageCenter, width, height))
            .First();
        var primary = components[primaryIndex];
        if (primary.Area < Math.Max(16, backgroundMask.Length * 0.006d))
        {
            return;
        }

        var minimumDetailArea = Math.Max(3, (int)Math.Round(primary.Area * 0.00015d));
        var minimumDetachedSubjectArea = Math.Max(16, (int)Math.Round(primary.Area * 0.01d));
        var keep = new bool[components.Count];
        keep[primaryIndex] = true;
        var maximumDetailDistance = Math.Clamp((int)Math.Round(Math.Min(width, height) * 0.004d), 5, 12);
        var faceEnvelope = new PixelRect(
            primary.Bounds.X + (int)Math.Round(primary.Bounds.Width * 0.31d),
            primary.Bounds.Y,
            Math.Max(1, (int)Math.Round(primary.Bounds.Width * 0.38d)),
            Math.Max(1, (int)Math.Round(primary.Bounds.Height * 0.43d)));
        var distances = new ushort[backgroundMask.Length];
        Array.Fill(distances, ushort.MaxValue);
        queue.Clear();
        for (var index = 0; index < labels.Length; index++)
        {
            if (labels[index] == primaryIndex)
            {
                distances[index] = 0;
                queue.Enqueue(index);
            }
        }

        while (queue.TryDequeue(out var index))
        {
            var distance = distances[index];
            if (distance >= maximumDetailDistance)
            {
                continue;
            }

            var x = index % width;
            var y = index / width;
            Visit(x - 1, y, distance);
            Visit(x + 1, y, distance);
            Visit(x, y - 1, distance);
            Visit(x, y + 1, distance);
        }

        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            if (label >= 0 &&
                (components[label].Area >= minimumDetachedSubjectArea ||
                 (components[label].Area >= minimumDetailArea &&
                   (distances[index] <= maximumDetailDistance ||
                    faceEnvelope.Contains(components[label].Bounds.Center)))))
            {
                keep[label] = true;
            }
        }

        for (var index = 0; index < backgroundMask.Length; index++)
        {
            if (!backgroundMask[index] && !keep[labels[index]])
            {
                backgroundMask[index] = true;
            }
        }

        void Visit(int x, int y, ushort distance)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                return;
            }

            var neighbor = (y * width) + x;
            var next = (ushort)(distance + 1);
            if (next < distances[neighbor])
            {
                distances[neighbor] = next;
                queue.Enqueue(neighbor);
            }
        }
    }

    private static double ComponentScore(Component component, PixelPoint imageCenter, int width, int height)
    {
        var normalizedX = (component.Bounds.Center.X - imageCenter.X) / (double)Math.Max(1, width);
        var normalizedY = (component.Bounds.Center.Y - imageCenter.Y) / (double)Math.Max(1, height);
        var centerWeight = Math.Max(0.25d, 1d - Math.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY)));
        var touchedEdges = 0;
        if (component.Bounds.X == 0) touchedEdges++;
        if (component.Bounds.Y == 0) touchedEdges++;
        if (component.Bounds.Right == width) touchedEdges++;
        if (component.Bounds.Bottom == height) touchedEdges++;
        var edgePenalty = touchedEdges switch
        {
            0 => 1d,
            1 => 0.72d,
            2 => 0.3d,
            _ => 0.1d
        };
        return component.Area * (0.55d + centerWeight) * edgePenalty;
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

    private sealed record Component(int Area, PixelRect Bounds);

    private readonly record struct BorderBin(long Red, long Green, long Blue, int Count, int SideMask);
}
