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
    private const int ExactOrderingLimit = 2_048;

    public static IReadOnlyList<DrawingStroke> Order(IReadOnlyList<DrawingStroke> strokes)
    {
        if (strokes.Count <= 1)
        {
            return strokes;
        }

        if (strokes.Count > ExactOrderingLimit)
        {
            return OrderLargePlan(strokes);
        }

        var remaining = strokes.ToList();
        var ordered = new List<DrawingStroke>(strokes.Count);
        var current = new NormalizedPoint(0d, 0d);
        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestDistance = double.PositiveInfinity;
            var reverse = false;
            for (var index = 0; index < remaining.Count; index++)
            {
                var stroke = remaining[index];
                var firstDistance = PlanningUtilities.DistanceSquared(current, stroke.Points[0]);
                var lastDistance = PlanningUtilities.DistanceSquared(current, stroke.Points[^1]);
                if (firstDistance < bestDistance)
                {
                    bestDistance = firstDistance;
                    bestIndex = index;
                    reverse = false;
                }

                if (!stroke.IsClosed && lastDistance < bestDistance)
                {
                    bestDistance = lastDistance;
                    bestIndex = index;
                    reverse = true;
                }
            }

            var selected = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);
            if (reverse)
            {
                selected = new DrawingStroke(selected.Points.Reverse(), selected.IsClosed);
            }

            ordered.Add(selected);
            current = selected.Points[^1];
        }

        return ordered;
    }

    private static List<DrawingStroke> OrderLargePlan(IReadOnlyList<DrawingStroke> strokes)
    {
        var ordered = new List<DrawingStroke>(strokes.Count);
        var rows = strokes
            .GroupBy(stroke => Math.Round(stroke.Points[0].Y, 9))
            .OrderBy(group => group.Key)
            .ToArray();
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var leftToRight = rowIndex % 2 == 0;
            var row = leftToRight
                ? rows[rowIndex].OrderBy(stroke => Math.Min(stroke.Points[0].X, stroke.Points[^1].X))
                : rows[rowIndex].OrderByDescending(stroke => Math.Max(stroke.Points[0].X, stroke.Points[^1].X));
            foreach (var stroke in row)
            {
                var firstX = stroke.Points[0].X;
                var lastX = stroke.Points[^1].X;
                var shouldReverse = !stroke.IsClosed &&
                    ((leftToRight && lastX < firstX) || (!leftToRight && lastX > firstX));
                ordered.Add(shouldReverse
                    ? new DrawingStroke(stroke.Points.Reverse(), stroke.IsClosed)
                    : stroke);
            }
        }

        return ordered;
    }
}
