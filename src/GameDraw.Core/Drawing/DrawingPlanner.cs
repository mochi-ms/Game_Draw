using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Models;

namespace GameDraw.Core.Drawing;

public sealed record DrawingPlannerOptions
{
    public bool Serpentine { get; init; } = true;
    public double EdgeThreshold { get; init; } = 42d;
}

public sealed class DrawingPlanner
{
    public DrawingPlan CreatePlan(ImageBuffer image, DrawingMode mode, DrawingPlannerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new DrawingPlannerOptions();

        return mode switch
        {
            DrawingMode.Pixel => CreatePixelPlan(image, cancellationToken),
            DrawingMode.LineArt => CreateLineArtPlan(image, options, cancellationToken),
            _ => CreateScanlinePlan(image, options, cancellationToken)
        };
    }

    private static DrawingPlan CreateScanlinePlan(ImageBuffer image, DrawingPlannerOptions options, CancellationToken cancellationToken)
    {
        var grouped = new GroupedStrokeBuilder();
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reverse = options.Serpentine && y % 2 == 1;
            var x = reverse ? image.Width - 1 : 0;
            var end = reverse ? -1 : image.Width;
            var step = reverse ? -1 : 1;

            while (x != end)
            {
                var color = image[x, y];
                if (color is null)
                {
                    x += step;
                    continue;
                }

                var start = x;
                var next = x + step;
                while (next != end && image[next, y] == color)
                {
                    x = next;
                    next += step;
                }

                var finish = x;
                grouped.Add(color.Value, new Stroke(new[]
                {
                    ToNormalized(start, y, image.Width, image.Height),
                    ToNormalized(finish, y, image.Width, image.Height)
                }));
                x = next;
            }
        }

        return new DrawingPlan(DrawingMode.Scanline, image.Width, image.Height, grouped.Build());
    }

    private static DrawingPlan CreatePixelPlan(ImageBuffer image, CancellationToken cancellationToken)
    {
        var grouped = new GroupedStrokeBuilder();
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                if (image[x, y] is { } color)
                {
                    grouped.Add(color, new Stroke(new[] { ToNormalized(x, y, image.Width, image.Height) }));
                }
            }
        }

        return new DrawingPlan(DrawingMode.Pixel, image.Width, image.Height, grouped.Build());
    }

    private static DrawingPlan CreateLineArtPlan(ImageBuffer image, DrawingPlannerOptions options, CancellationToken cancellationToken)
    {
        var edgeImage = new ImageBuffer(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x++)
            {
                if (image[x, y] is not { } current)
                {
                    continue;
                }

                var right = x + 1 < image.Width ? image[x + 1, y] : null;
                var down = y + 1 < image.Height ? image[x, y + 1] : null;
                var rightDistance = right is { } rightColor ? ColorMath.DeltaE76(current, rightColor) : 0d;
                var downDistance = down is { } downColor ? ColorMath.DeltaE76(current, downColor) : 0d;
                if (Math.Max(rightDistance, downDistance) >= options.EdgeThreshold)
                {
                    edgeImage[x, y] = new RgbColor(25, 25, 28);
                }
            }
        }

        var plan = CreateScanlinePlan(edgeImage, options with { Serpentine = false }, cancellationToken);
        return plan with { Mode = DrawingMode.LineArt };
    }

    private static NormalizedPoint ToNormalized(int x, int y, int width, int height) => new(
        (x + 0.5d) / width,
        (y + 0.5d) / height);

    private sealed class GroupedStrokeBuilder
    {
        private readonly List<RgbColor> _order = new();
        private readonly Dictionary<RgbColor, List<Stroke>> _groups = new();

        public void Add(RgbColor color, Stroke stroke)
        {
            if (!_groups.TryGetValue(color, out var strokes))
            {
                strokes = new List<Stroke>();
                _groups.Add(color, strokes);
                _order.Add(color);
            }

            strokes.Add(stroke);
        }

        public IReadOnlyList<ColorGroup> Build() => _order
            .Select(color => new ColorGroup(color, _groups[color].ToArray()))
            .ToArray();
    }
}
