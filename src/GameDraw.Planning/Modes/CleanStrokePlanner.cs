using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning.Modes;

/// <summary>
/// Converts raster lines into a one-pixel skeleton and traces each branch as
/// one continuous pen-down path. A small Ramer-Douglas-Peucker pass removes
/// pixel stair-stepping without joining unrelated lines.
/// </summary>
internal static class CleanStrokePlanner
{
    private const double SimplificationTolerancePixels = 1.05d;

    public static DrawingPlan Create(QuantizedImage image, DrawingPlannerOptions options)
    {
        var strokesByColor = new Dictionary<int, List<DrawingStroke>>();
        foreach (var paletteIndex in PlanningUtilities.GetPaletteIndices(image, options))
        {
            var mask = BuildMask(image, paletteIndex, options);
            Thin(mask, image.Width, image.Height);
            foreach (var traced in Trace(mask, image.Width, image.Height))
            {
                var simplified = Simplify(traced.Points, SimplificationTolerancePixels);
                if (simplified.Count == 0)
                {
                    continue;
                }

                var points = simplified
                    .Select(point => new NormalizedPoint(
                        (point.X + 0.5d) / image.Width,
                        (point.Y + 0.5d) / image.Height))
                    .ToArray();
                PlanningUtilities.AddStroke(
                    strokesByColor,
                    paletteIndex,
                    new DrawingStroke(points, traced.IsClosed && points.Length >= 3));
            }
        }

        return PlanningUtilities.BuildPlan(image, DrawingMode.CleanStroke, strokesByColor, options);
    }

    private static bool[] BuildMask(
        QuantizedImage image,
        int paletteIndex,
        DrawingPlannerOptions options)
    {
        var mask = new bool[image.Width * image.Height];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                mask[(y * image.Width) + x] =
                    PlanningUtilities.IsSameColor(image, x, y, paletteIndex, options);
            }
        }

        return mask;
    }

    private static void Thin(bool[] pixels, int width, int height)
    {
        if (width < 3 || height < 3)
        {
            return;
        }

        var remove = new List<int>();
        var changed = true;
        while (changed)
        {
            changed = MarkAndRemove(firstPass: true);
            changed |= MarkAndRemove(firstPass: false);
        }

        bool MarkAndRemove(bool firstPass)
        {
            remove.Clear();
            Span<bool> neighbors = stackalloc bool[8];
            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    var index = (y * width) + x;
                    if (!pixels[index])
                    {
                        continue;
                    }

                    neighbors[0] = pixels[index - width];
                    neighbors[1] = pixels[index - width + 1];
                    neighbors[2] = pixels[index + 1];
                    neighbors[3] = pixels[index + width + 1];
                    neighbors[4] = pixels[index + width];
                    neighbors[5] = pixels[index + width - 1];
                    neighbors[6] = pixels[index - 1];
                    neighbors[7] = pixels[index - width - 1];
                    var count = 0;
                    var transitions = 0;
                    for (var neighbor = 0; neighbor < neighbors.Length; neighbor++)
                    {
                        if (neighbors[neighbor])
                        {
                            count++;
                        }

                        if (!neighbors[neighbor] && neighbors[(neighbor + 1) % neighbors.Length])
                        {
                            transitions++;
                        }
                    }

                    if (count is < 2 or > 6 || transitions != 1)
                    {
                        continue;
                    }

                    var firstConstraint = firstPass
                        ? !neighbors[0] || !neighbors[2] || !neighbors[4]
                        : !neighbors[0] || !neighbors[2] || !neighbors[6];
                    var secondConstraint = firstPass
                        ? !neighbors[2] || !neighbors[4] || !neighbors[6]
                        : !neighbors[0] || !neighbors[4] || !neighbors[6];
                    if (firstConstraint && secondConstraint)
                    {
                        remove.Add(index);
                    }
                }
            }

            foreach (var index in remove)
            {
                pixels[index] = false;
            }

            return remove.Count > 0;
        }
    }

    private static IEnumerable<TracedStroke> Trace(bool[] pixels, int width, int height)
    {
        var nodes = Enumerable.Range(0, pixels.Length).Where(index => pixels[index]).ToArray();
        var visitedEdges = new HashSet<long>();
        var isolated = new HashSet<int>();
        foreach (var start in nodes.Where(index => NeighborIndices(index, pixels, width, height).Count <= 1))
        {
            var neighbors = NeighborIndices(start, pixels, width, height);
            if (neighbors.Count == 0)
            {
                isolated.Add(start);
                yield return new TracedStroke(new[] { Point(start, width) }, false);
                continue;
            }

            foreach (var next in neighbors)
            {
                if (!visitedEdges.Contains(EdgeKey(start, next)))
                {
                    yield return Follow(start, next, pixels, width, height, visitedEdges);
                }
            }
        }

        // Remaining edges are loops and branches. Continuing through a branch
        // along the straightest unused edge creates long, author-like pen
        // strokes instead of stopping and restarting at every intersection.
        foreach (var start in nodes)
        {
            if (isolated.Contains(start))
            {
                continue;
            }

            foreach (var next in NeighborIndices(start, pixels, width, height))
            {
                if (!visitedEdges.Contains(EdgeKey(start, next)))
                {
                    yield return Follow(start, next, pixels, width, height, visitedEdges);
                }
            }
        }
    }

    private static TracedStroke Follow(
        int start,
        int next,
        bool[] pixels,
        int width,
        int height,
        HashSet<long> visitedEdges)
    {
        var points = new List<PixelPoint> { Point(start, width) };
        var previous = start;
        var current = next;
        var closed = false;
        while (points.Count <= pixels.Length + 1)
        {
            visitedEdges.Add(EdgeKey(previous, current));
            if (current == start)
            {
                closed = true;
                break;
            }

            points.Add(Point(current, width));
            var candidates = NeighborIndices(current, pixels, width, height)
                .Where(candidate => !visitedEdges.Contains(EdgeKey(current, candidate)))
                .ToArray();
            if (candidates.Length == 0)
            {
                break;
            }

            var previousPoint = Point(previous, width);
            var currentPoint = Point(current, width);
            var incomingX = currentPoint.X - previousPoint.X;
            var incomingY = currentPoint.Y - previousPoint.Y;
            var candidate = candidates
                .OrderByDescending(value => ContinuationScore(
                    incomingX,
                    incomingY,
                    currentPoint,
                    Point(value, width)))
                .ThenBy(value => value)
                .First();

            previous = current;
            current = candidate;
        }

        return new TracedStroke(points, closed);
    }

    private static List<int> NeighborIndices(int index, bool[] pixels, int width, int height)
    {
        var x = index % width;
        var y = index / width;
        var neighbors = new List<int>(8);
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
                if (pixels[neighbor])
                {
                    // If an orthogonal route exists, the diagonal edge is a
                    // corner-touch shortcut. Keeping it creates tiny triangles
                    // and occasional straight connector artefacts.
                    if (offsetX != 0 && offsetY != 0)
                    {
                        var horizontal = (y * width) + x + offsetX;
                        var vertical = ((y + offsetY) * width) + x;
                        if (pixels[horizontal] || pixels[vertical])
                        {
                            continue;
                        }
                    }

                    neighbors.Add(neighbor);
                }
            }
        }

        return neighbors;
    }

    private static IReadOnlyList<PixelPoint> Simplify(IReadOnlyList<PixelPoint> points, double tolerance)
    {
        if (points.Count <= 2)
        {
            return points;
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyRange(0, points.Count - 1);
        return points.Where((_, index) => keep[index]).ToArray();

        void SimplifyRange(int first, int last)
        {
            if (last <= first + 1)
            {
                return;
            }

            var furthest = -1;
            var maximum = tolerance * tolerance;
            for (var index = first + 1; index < last; index++)
            {
                var distance = SegmentDistanceSquared(points[index], points[first], points[last]);
                if (distance > maximum)
                {
                    maximum = distance;
                    furthest = index;
                }
            }

            if (furthest < 0)
            {
                return;
            }

            keep[furthest] = true;
            SimplifyRange(first, furthest);
            SimplifyRange(furthest, last);
        }
    }

    private static double SegmentDistanceSquared(PixelPoint point, PixelPoint start, PixelPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= double.Epsilon)
        {
            return DistanceSquared(point, start);
        }

        var position = Math.Clamp(
            (((point.X - start.X) * deltaX) + ((point.Y - start.Y) * deltaY)) / lengthSquared,
            0d,
            1d);
        return DistanceSquared(
            point,
            new PixelPoint(start.X + (position * deltaX), start.Y + (position * deltaY)));
    }

    private static double DistanceSquared(PixelPoint first, PixelPoint second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        return (x * x) + (y * y);
    }

    private static double ContinuationScore(
        double incomingX,
        double incomingY,
        PixelPoint current,
        PixelPoint candidate)
    {
        var outgoingX = candidate.X - current.X;
        var outgoingY = candidate.Y - current.Y;
        var denominator = Math.Sqrt(
            ((incomingX * incomingX) + (incomingY * incomingY)) *
            ((outgoingX * outgoingX) + (outgoingY * outgoingY)));
        return denominator <= double.Epsilon
            ? -1d
            : ((incomingX * outgoingX) + (incomingY * outgoingY)) / denominator;
    }

    private static PixelPoint Point(int index, int width) => new(index % width, index / width);

    private static long EdgeKey(int first, int second)
    {
        var minimum = Math.Min(first, second);
        var maximum = Math.Max(first, second);
        return ((long)minimum << 32) | (uint)maximum;
    }

    private readonly record struct PixelPoint(double X, double Y);

    private sealed record TracedStroke(IReadOnlyList<PixelPoint> Points, bool IsClosed);
}
