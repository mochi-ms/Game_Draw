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
    public static DrawingPlan Create(
        QuantizedImage image,
        DrawingPlannerOptions options,
        DrawingMode mode = DrawingMode.CleanStroke)
    {
        var strokesByColor = new Dictionary<int, List<DrawingStroke>>();
        foreach (var paletteIndex in PlanningUtilities.GetPaletteIndices(image, options))
        {
            var mask = BuildMask(image, paletteIndex, options);
            var skeleton = (bool[])mask.Clone();
            Thin(skeleton, image.Width, image.Height);
            var colorStrokes = new List<DrawingStroke>();
            foreach (var traced in Trace(skeleton, image.Width, image.Height))
            {
                if (PathLength(traced.Points, traced.IsClosed) < options.MinimumStrokeLengthPixels)
                {
                    continue;
                }

                var smoothed = Smooth(traced.Points, traced.IsClosed);
                var simplified = Simplify(smoothed, options.StrokeSimplificationTolerancePixels);
                if (simplified.Count == 0)
                {
                    continue;
                }

                var points = simplified
                    .Select(point => new NormalizedPoint(
                        (point.X + 0.5d) / image.Width,
                        (point.Y + 0.5d) / image.Height))
                    .ToArray();
                colorStrokes.Add(new DrawingStroke(points, traced.IsClosed && points.Length >= 3));
            }

            // Artist mode is width-aware. A centreline is ideal for thin hair
            // and facial contours, but it destroys intentional heavy ink such
            // as eyelashes and overlapping locks of hair. Rasterize the real
            // two-pixel in-game brush over the centreline, then add only the
            // still-uncovered source ink as short local strokes. This keeps the
            // authored line weight without returning to full-image scan rows.
            if (mode == DrawingMode.ArtistStroke)
            {
                var heavyInk = BuildHeavyInkMask(mask, image.Width, image.Height);
                colorStrokes.AddRange(CreateInkCoverageStrokes(
                    mask,
                    heavyInk,
                    colorStrokes,
                    image.Width,
                    image.Height));
            }

            foreach (var stroke in colorStrokes)
            {
                PlanningUtilities.AddStroke(strokesByColor, paletteIndex, stroke);
            }
        }

        return PlanningUtilities.BuildPlan(image, mode, strokesByColor, options);
    }

    private static IEnumerable<DrawingStroke> CreateInkCoverageStrokes(
        bool[] sourceInk,
        bool[] requiredInk,
        IReadOnlyList<DrawingStroke> centerlines,
        int width,
        int height)
    {
        var covered = new bool[sourceInk.Length];
        var coverageRuns = new List<CoverageRun>();
        foreach (var stroke in centerlines)
        {
            RasterizeStroke(stroke, width, height, covered);
        }

        for (var y = 0; y < height; y++)
        {
            var x = 0;
            while (x < width)
            {
                if (!requiredInk[(y * width) + x] || covered[(y * width) + x])
                {
                    x++;
                    continue;
                }

                var start = x;
                while (start > 0 && requiredInk[(y * width) + start - 1])
                {
                    start--;
                }

                while (x + 1 < width && requiredInk[(y * width) + x + 1])
                {
                    x++;
                }

                var end = x;
                var centerY = ChooseCoverageCenterY(y, height);
                var centerStart = ChooseCoverageStartX(sourceInk, start, end, centerY, width, height);
                var centerEnd = Math.Max(centerStart, end);
                var points = centerStart == centerEnd
                    ? new[] { PlanningUtilities.Center(centerStart, centerY, width, height) }
                    : new[]
                    {
                        PlanningUtilities.Center(centerStart, centerY, width, height),
                        PlanningUtilities.Center(centerEnd, centerY, width, height)
                    };
                var stroke = new DrawingStroke(points);
                RasterizeStroke(stroke, width, height, covered);
                coverageRuns.Add(new CoverageRun(centerStart, centerEnd, centerY));
                x++;
            }
        }

        foreach (var stroke in MergeCoverageRuns(coverageRuns, sourceInk, width, height))
        {
            yield return stroke;
        }
    }

    private static IEnumerable<DrawingStroke> MergeCoverageRuns(
        IReadOnlyList<CoverageRun> runs,
        bool[] sourceInk,
        int width,
        int height)
    {
        var chains = new List<CoverageChain>();
        foreach (var run in runs.OrderBy(item => item.Y).ThenBy(item => item.StartX))
        {
            CoverageChain? selected = null;
            foreach (var chain in chains
                         .Where(item => run.Y > item.LastY && run.Y - item.LastY <= 2)
                         .OrderBy(item => Math.Abs(item.LastX - ((run.StartX + run.EndX) / 2d))))
            {
                if (chain.LastX < run.StartX || chain.LastX > run.EndX ||
                    !ConnectorStaysInInk(sourceInk, chain.LastX, chain.LastY, run.Y, width, height))
                {
                    continue;
                }

                selected = chain;
                break;
            }

            if (selected is null)
            {
                selected = new CoverageChain();
                chains.Add(selected);
                selected.Points.Add(new CoveragePoint(run.StartX, run.Y));
                if (run.EndX != run.StartX)
                {
                    selected.Points.Add(new CoveragePoint(run.EndX, run.Y));
                }
            }
            else
            {
                var entryX = selected.LastX;
                selected.Points.Add(new CoveragePoint(entryX, run.Y));
                var distanceToStart = entryX - run.StartX;
                var distanceToEnd = run.EndX - entryX;
                if (distanceToStart <= distanceToEnd)
                {
                    if (entryX != run.StartX)
                    {
                        selected.Points.Add(new CoveragePoint(run.StartX, run.Y));
                    }

                    if (run.EndX != run.StartX)
                    {
                        selected.Points.Add(new CoveragePoint(run.EndX, run.Y));
                    }
                }
                else
                {
                    if (entryX != run.EndX)
                    {
                        selected.Points.Add(new CoveragePoint(run.EndX, run.Y));
                    }

                    if (run.StartX != run.EndX)
                    {
                        selected.Points.Add(new CoveragePoint(run.StartX, run.Y));
                    }
                }
            }

            selected.LastX = selected.Points[^1].X;
            selected.LastY = run.Y;
        }

        foreach (var chain in chains)
        {
            var points = chain.Points
                .Where((point, index) => index == 0 || point != chain.Points[index - 1])
                .Select(point => PlanningUtilities.Center(point.X, point.Y, width, height))
                .ToArray();
            if (points.Length > 0)
            {
                yield return new DrawingStroke(points);
            }
        }
    }

    private static bool ConnectorStaysInInk(
        bool[] sourceInk,
        int x,
        int startY,
        int endY,
        int width,
        int height)
    {
        for (var y = startY; y <= endY; y++)
        {
            if (BrushPointScore(sourceInk, x, y, width, height) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool[] BuildHeavyInkMask(bool[] sourceInk, int width, int height)
    {
        var core = new bool[sourceInk.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!sourceInk[(y * width) + x])
                {
                    continue;
                }

                var neighbors = 0;
                for (var offsetY = -2; offsetY <= 2; offsetY++)
                {
                    for (var offsetX = -2; offsetX <= 2; offsetX++)
                    {
                        var sampleX = x + offsetX;
                        var sampleY = y + offsetY;
                        if ((uint)sampleX < (uint)width &&
                            (uint)sampleY < (uint)height &&
                            sourceInk[(sampleY * width) + sampleX])
                        {
                            neighbors++;
                        }
                    }
                }

                core[(y * width) + x] = neighbors >= 19;
            }
        }

        // Bring back the boundary of every genuinely dense region. A lone
        // anti-aliased contour has no dense core and therefore remains a clean
        // centreline, while eyelashes and overlapping hair retain their mass.
        var heavy = new bool[sourceInk.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!sourceInk[(y * width) + x])
                {
                    continue;
                }

                for (var offsetY = -2; offsetY <= 2 && !heavy[(y * width) + x]; offsetY++)
                {
                    for (var offsetX = -2; offsetX <= 2; offsetX++)
                    {
                        var sampleX = x + offsetX;
                        var sampleY = y + offsetY;
                        if ((uint)sampleX < (uint)width &&
                            (uint)sampleY < (uint)height &&
                            core[(sampleY * width) + sampleX])
                        {
                            heavy[(y * width) + x] = true;
                            break;
                        }
                    }
                }
            }
        }

        return heavy;
    }

    private static int ChooseCoverageCenterY(int y, int height)
    {
        // The real minimum brush covers two logical rows. Bias its centre one
        // row forward so a single local pass covers the current and next row;
        // this halves pen lifts inside dense ink regions.
        return Math.Min(height - 1, y + 1);
    }

    private static int ChooseCoverageStartX(
        bool[] sourceInk,
        int start,
        int end,
        int centerY,
        int width,
        int height)
    {
        if (start < end)
        {
            // A two-pixel brush is biased one pixel left/up. Starting one cell
            // to the right reconstructs the original run instead of growing it.
            return Math.Min(end, start + 1);
        }

        if (start + 1 >= width)
        {
            return start;
        }

        return BrushPointScore(sourceInk, start + 1, centerY, width, height) >
               BrushPointScore(sourceInk, start, centerY, width, height)
            ? start + 1
            : start;
    }

    private static int BrushPointScore(
        bool[] sourceInk,
        int centerX,
        int centerY,
        int width,
        int height)
    {
        var score = 0;
        for (var y = centerY - 1; y <= centerY; y++)
        {
            for (var x = centerX - 1; x <= centerX; x++)
            {
                if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                {
                    continue;
                }

                score += sourceInk[(y * width) + x] ? 1 : -1;
            }
        }

        return score;
    }

    private static void RasterizeStroke(
        DrawingStroke stroke,
        int width,
        int height,
        bool[] covered)
    {
        var previous = ToPixel(stroke.Points[0], width, height);
        Paint(previous.X, previous.Y);
        for (var index = 1; index < stroke.Points.Count; index++)
        {
            var next = ToPixel(stroke.Points[index], width, height);
            PaintLine(previous, next);
            previous = next;
        }

        if (stroke.IsClosed && stroke.Points.Count > 1)
        {
            PaintLine(previous, ToPixel(stroke.Points[0], width, height));
        }

        void PaintLine((int X, int Y) first, (int X, int Y) second)
        {
            var x = first.X;
            var y = first.Y;
            var deltaX = Math.Abs(second.X - first.X);
            var stepX = first.X < second.X ? 1 : -1;
            var deltaY = -Math.Abs(second.Y - first.Y);
            var stepY = first.Y < second.Y ? 1 : -1;
            var error = deltaX + deltaY;
            while (true)
            {
                Paint(x, y);
                if (x == second.X && y == second.Y)
                {
                    break;
                }

                var doubled = error * 2;
                if (doubled >= deltaY)
                {
                    error += deltaY;
                    x += stepX;
                }

                if (doubled <= deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }
            }
        }

        void Paint(int centerX, int centerY)
        {
            // Must match DrawingPlanPostProcessor.RenderPreview(..., 2).
            for (var y = centerY - 1; y <= centerY; y++)
            {
                for (var x = centerX - 1; x <= centerX; x++)
                {
                    if ((uint)x < (uint)width && (uint)y < (uint)height)
                    {
                        covered[(y * width) + x] = true;
                    }
                }
            }
        }
    }

    private static (int X, int Y) ToPixel(NormalizedPoint point, int width, int height)
        => (
            Math.Clamp((int)Math.Floor(point.X * width), 0, width - 1),
            Math.Clamp((int)Math.Floor(point.Y * height), 0, height - 1));

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

    private static IReadOnlyList<PixelPoint> Smooth(IReadOnlyList<PixelPoint> points, bool closed)
    {
        if (points.Count < 3)
        {
            return points;
        }

        var result = new PixelPoint[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            if (!closed && (index == 0 || index == points.Count - 1))
            {
                result[index] = points[index];
                continue;
            }

            var previous = points[(index - 1 + points.Count) % points.Count];
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            result[index] = new PixelPoint(
                (previous.X + (2d * current.X) + next.X) / 4d,
                (previous.Y + (2d * current.Y) + next.Y) / 4d);
        }

        return result;
    }

    private static double PathLength(IReadOnlyList<PixelPoint> points, bool closed)
    {
        var length = 0d;
        for (var index = 1; index < points.Count; index++)
        {
            length += Math.Sqrt(DistanceSquared(points[index - 1], points[index]));
        }

        if (closed && points.Count > 1)
        {
            length += Math.Sqrt(DistanceSquared(points[^1], points[0]));
        }

        return length;
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

    private readonly record struct CoverageRun(int StartX, int EndX, int Y);

    private readonly record struct CoveragePoint(int X, int Y);

    private sealed class CoverageChain
    {
        public List<CoveragePoint> Points { get; } = new();

        public int LastX { get; set; }

        public int LastY { get; set; }
    }
}
