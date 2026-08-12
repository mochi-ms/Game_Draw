using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning;

internal static class PlanningUtilities
{
    public static bool IsDrawable(
        QuantizedImage image,
        int x,
        int y,
        DrawingPlannerOptions options)
    {
        var pixel = image.Source[x, y];
        return options.IncludeTransparentPixels || pixel.Alpha > options.TransparentThreshold;
    }

    public static bool IsSameColor(
        QuantizedImage image,
        int x,
        int y,
        int paletteIndex,
        DrawingPlannerOptions options)
    {
        return (uint)x < (uint)image.Width
            && (uint)y < (uint)image.Height
            && IsDrawable(image, x, y, options)
            && image[x, y] == paletteIndex;
    }

    public static NormalizedPoint Center(int x, int y, int width, int height)
        => new((x + 0.5d) / width, (y + 0.5d) / height);

    public static NormalizedPoint GridPoint(int x, int y, int width, int height)
        => new(x / (double)width, y / (double)height);

    public static IReadOnlyList<int> GetPaletteIndices(
        QuantizedImage image,
        DrawingPlannerOptions options)
    {
        var used = new SortedSet<int>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (IsDrawable(image, x, y, options))
                {
                    used.Add(image[x, y]);
                }
            }
        }

        return used.ToArray();
    }

    public static DrawingPlan BuildPlan(
        QuantizedImage image,
        DrawingMode mode,
        IReadOnlyDictionary<int, List<DrawingStroke>> strokesByColor,
        DrawingPlannerOptions options)
    {
        var groups = new List<DrawingColorGroup>(strokesByColor.Count);
        foreach (var pair in strokesByColor.OrderBy(pair => pair.Key))
        {
            if (pair.Value.Count == 0)
            {
                continue;
            }

            var strokes = options.OrderStrokesByTravel
                ? StrokeOrdering.Order(pair.Value)
                : pair.Value;
            groups.Add(new DrawingColorGroup(image.Palette[pair.Key], strokes));
        }

        if (options.OrderColorGroupsByTravel && groups.Count > 1)
        {
            groups = OrderGroupsByTravel(groups).ToList();
        }

        return new DrawingPlan(mode, new PixelSize(image.Width, image.Height), groups);
    }

    public static void AddStroke(
        IDictionary<int, List<DrawingStroke>> strokesByColor,
        int paletteIndex,
        DrawingStroke stroke)
    {
        if (!strokesByColor.TryGetValue(paletteIndex, out var strokes))
        {
            strokes = new List<DrawingStroke>();
            strokesByColor.Add(paletteIndex, strokes);
        }

        strokes.Add(stroke);
    }

    private static IEnumerable<DrawingColorGroup> OrderGroupsByTravel(
        IReadOnlyList<DrawingColorGroup> groups)
    {
        var remaining = groups.ToList();
        var current = new NormalizedPoint(0d, 0d);
        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestDistance = double.PositiveInfinity;
            for (var index = 0; index < remaining.Count; index++)
            {
                var anchor = remaining[index].Strokes[0].Points[0];
                var distance = DistanceSquared(current, anchor);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }

            var selected = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);
            yield return selected;
            current = selected.Strokes[^1].Points[^1];
        }
    }

    public static double DistanceSquared(NormalizedPoint first, NormalizedPoint second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        return (x * x) + (y * y);
    }
}

internal static class StrokeOrdering
{
    private const int PrinterBandCount = 96;

    public static IReadOnlyList<DrawingStroke> Order(IReadOnlyList<DrawingStroke> strokes)
    {
        if (strokes.Count <= 1)
        {
            return strokes;
        }

        // Keep drawing-tool phases stable (all pencil work before fill work),
        // then sweep every phase like a printer.  A global nearest-neighbour
        // search can jump from the top-left to the bottom-right simply because
        // an endpoint happens to be marginally closer.  Horizontal bands make
        // vertical progress monotonic while the alternating direction avoids
        // a full-width return jump at the end of every row.
        var ordered = new List<DrawingStroke>(strokes.Count);
        foreach (var phase in strokes
                     .GroupBy(stroke => stroke.ToolAction)
                     .OrderBy(group => group.Key))
        {
            OrderPrinterPhase(phase.ToArray(), ordered);
        }

        return ordered;
    }

    private static void OrderPrinterPhase(
        IReadOnlyList<DrawingStroke> strokes,
        List<DrawingStroke> destination)
    {
        var rows = strokes
            .Select(stroke => new PrinterStroke(stroke, Center(stroke)))
            .GroupBy(item => Math.Clamp(
                (int)Math.Floor(item.Center.Y * PrinterBandCount),
                0,
                PrinterBandCount - 1))
            .OrderBy(group => group.Key)
            .ToArray();
        var current = new NormalizedPoint(0d, 0d);
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var leftToRight = rowIndex % 2 == 0;
            var row = leftToRight
                ? rows[rowIndex]
                    .OrderBy(item => item.Center.X)
                    .ThenBy(item => item.Center.Y)
                : rows[rowIndex]
                    .OrderByDescending(item => item.Center.X)
                    .ThenBy(item => item.Center.Y);
            foreach (var item in row)
            {
                var selected = Orient(item.Stroke, current, leftToRight);
                destination.Add(selected);
                current = selected.IsClosed ? selected.Points[0] : selected.Points[^1];
            }
        }
    }

    private static DrawingStroke Orient(
        DrawingStroke stroke,
        NormalizedPoint current,
        bool leftToRight)
    {
        if (stroke.Points.Count <= 1 || stroke.ToolAction == DrawingToolAction.Fill)
        {
            return stroke;
        }

        if (stroke.IsClosed)
        {
            var startIndex = 0;
            var bestDistance = double.PositiveInfinity;
            for (var index = 0; index < stroke.Points.Count; index++)
            {
                var distance = PlanningUtilities.DistanceSquared(current, stroke.Points[index]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    startIndex = index;
                }
            }

            if (startIndex == 0)
            {
                return stroke;
            }

            var rotated = stroke.Points
                .Skip(startIndex)
                .Concat(stroke.Points.Take(startIndex));
            return new DrawingStroke(rotated, isClosed: true, toolAction: stroke.ToolAction);
        }

        var first = stroke.Points[0];
        var last = stroke.Points[^1];
        var directionRequestsReverse = leftToRight ? last.X < first.X : last.X > first.X;
        if (Math.Abs(first.X - last.X) <= 1e-9)
        {
            directionRequestsReverse = PlanningUtilities.DistanceSquared(current, last) <
                PlanningUtilities.DistanceSquared(current, first);
        }

        return directionRequestsReverse
            ? new DrawingStroke(stroke.Points.Reverse(), stroke.IsClosed, stroke.ToolAction)
            : stroke;
    }

    private static NormalizedPoint Center(DrawingStroke stroke)
        => new(
            stroke.Points.Average(point => point.X),
            stroke.Points.Average(point => point.Y));

    private sealed record PrinterStroke(DrawingStroke Stroke, NormalizedPoint Center);
}
