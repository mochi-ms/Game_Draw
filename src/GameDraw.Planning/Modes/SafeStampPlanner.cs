using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning.Modes;

/// <summary>
/// Covers raster ink with the measured virtual brush. Adjacent stamp centres
/// are walked as one connected printer pass; disconnected regions are always
/// separate strokes. This preserves the exact mask while avoiding thousands
/// of costly and failure-prone up/move/down transitions.
/// </summary>
internal static class SafeStampPlanner
{
    private const int MaximumNeighbourDistance = 12;

    public static DrawingPlan Create(QuantizedImage image, DrawingPlannerOptions options)
        => Create(image, options, remappedIndices: null);

    /// <summary>
    /// Builds the same exact safe-stamp paths from an optional spatially
    /// cleaned palette-index map. The source alpha and palette still come from
    /// the original quantized image, so cleanup can merge tiny color islands
    /// without making transparent background drawable.
    /// </summary>
    internal static DrawingPlan Create(
        QuantizedImage image,
        DrawingPlannerOptions options,
        IReadOnlyList<byte>? remappedIndices)
    {
        var strokesByColor = new Dictionary<int, List<DrawingStroke>>();
        var paletteIndices = remappedIndices is null
            ? PlanningUtilities.GetPaletteIndices(image, options)
            : Enumerable.Range(0, image.Width * image.Height)
                .Where(index => PlanningUtilities.IsDrawable(
                    image,
                    index % image.Width,
                    index / image.Width,
                    options))
                .Select(index => (int)remappedIndices[index])
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
        foreach (var paletteIndex in paletteIndices)
        {
            var mask = BuildMask(image, paletteIndex, options, remappedIndices);
            var centres = SelectBrushCentres(
                mask,
                image.Width,
                image.Height,
                options.BrushDiameterPixels);
            foreach (var chunk in BuildConnectedInkWalks(
                         centres,
                         mask,
                         image.Width,
                         image.Height,
                         Math.Clamp(options.BrushDiameterPixels * 3, 6, MaximumNeighbourDistance)))
            {
                var points = chunk
                    .Select(point => PlanningUtilities.Center(point.X, point.Y, image.Width, image.Height))
                    .ToArray();
                PlanningUtilities.AddStroke(
                    strokesByColor,
                    paletteIndex,
                    new DrawingStroke(points));
            }
        }

        return PlanningUtilities.BuildPlan(image, DrawingMode.SafeStamp, strokesByColor, options);
    }

    private static bool[] BuildMask(
        QuantizedImage image,
        int paletteIndex,
        DrawingPlannerOptions options,
        IReadOnlyList<byte>? remappedIndices)
    {
        var mask = new bool[checked(image.Width * image.Height)];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var index = (y * image.Width) + x;
                mask[index] = PlanningUtilities.IsDrawable(image, x, y, options) &&
                    (remappedIndices is null
                        ? image[x, y] == paletteIndex
                        : remappedIndices[index] == paletteIndex);
            }
        }

        return mask;
    }

    private static List<PixelPoint> SelectBrushCentres(
        bool[] mask,
        int width,
        int height,
        int brushDiameter)
    {
        var covered = new bool[mask.Length];
        var centres = new List<PixelPoint>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                if (!mask[index] || covered[index])
                {
                    continue;
                }

                var best = new PixelPoint(x, y);
                var bestScore = int.MinValue;
                var minimumOffset = -(brushDiameter / 2);
                var maximumOffset = (brushDiameter - 1) / 2;
                // Try every centre whose measured brush footprint can cover
                // this still-uncovered source pixel.
                for (var centreY = Math.Max(0, y - maximumOffset);
                     centreY <= Math.Min(height - 1, y - minimumOffset);
                     centreY++)
                {
                    for (var centreX = Math.Max(0, x - maximumOffset);
                         centreX <= Math.Min(width - 1, x - minimumOffset);
                         centreX++)
                    {
                        if (!BrushContains(x - centreX, y - centreY, brushDiameter))
                        {
                            continue;
                        }

                        var score = ScoreCentre(
                            centreX,
                            centreY,
                            mask,
                            covered,
                            width,
                            height,
                            brushDiameter);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = new PixelPoint(centreX, centreY);
                        }
                    }
                }

                centres.Add(best);
                MarkCovered(best.X, best.Y, covered, width, height, brushDiameter);
            }
        }

        return centres;
    }

    private static int ScoreCentre(
        int centreX,
        int centreY,
        bool[] mask,
        bool[] covered,
        int width,
        int height,
        int brushDiameter)
    {
        var score = 0;
        var minimumOffset = -(brushDiameter / 2);
        var maximumOffset = (brushDiameter - 1) / 2;
        for (var y = centreY + minimumOffset; y <= centreY + maximumOffset; y++)
        {
            for (var x = centreX + minimumOffset; x <= centreX + maximumOffset; x++)
            {
                if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                {
                    score -= 3;
                    continue;
                }

                var index = (y * width) + x;
                if (!BrushContains(x - centreX, y - centreY, brushDiameter))
                {
                    continue;
                }

                score += mask[index] ? (covered[index] ? 1 : 5) : -2;
            }
        }

        return score;
    }

    private static void MarkCovered(
        int centreX,
        int centreY,
        bool[] covered,
        int width,
        int height,
        int brushDiameter)
    {
        var minimumOffset = -(brushDiameter / 2);
        var maximumOffset = (brushDiameter - 1) / 2;
        for (var y = centreY + minimumOffset; y <= centreY + maximumOffset; y++)
        {
            for (var x = centreX + minimumOffset; x <= centreX + maximumOffset; x++)
            {
                if ((uint)x < (uint)width && (uint)y < (uint)height)
                {
                    if (BrushContains(x - centreX, y - centreY, brushDiameter))
                    {
                        covered[(y * width) + x] = true;
                    }
                }
            }
        }
    }

    private static bool BrushContains(int offsetX, int offsetY, int brushDiameter)
    {
        var minimumOffset = -(brushDiameter / 2);
        var maximumOffset = (brushDiameter - 1) / 2;
        var brushCenter = (minimumOffset + maximumOffset) / 2d;
        var radius = brushDiameter / 2d;
        var distanceX = offsetX - brushCenter;
        var distanceY = offsetY - brushCenter;
        return (distanceX * distanceX) + (distanceY * distanceY) <= radius * radius;
    }

    private static IEnumerable<IReadOnlyList<PixelPoint>> BuildConnectedInkWalks(
        IReadOnlyList<PixelPoint> centres,
        bool[] mask,
        int width,
        int height,
        int maximumNeighbourDistance)
    {
        var remaining = centres.ToHashSet();
        var neighbourOffsets = (
            from deltaY in Enumerable.Range(-maximumNeighbourDistance, (maximumNeighbourDistance * 2) + 1)
            from deltaX in Enumerable.Range(-maximumNeighbourDistance, (maximumNeighbourDistance * 2) + 1)
            let distance = (deltaX * deltaX) + (deltaY * deltaY)
            where distance > 0 && distance <= maximumNeighbourDistance * maximumNeighbourDistance
            orderby Math.Abs(deltaY), distance, deltaX descending
            select new PixelPoint(deltaX, deltaY)).ToArray();
        foreach (var start in centres)
        {
            if (!remaining.Remove(start))
            {
                continue;
            }

            // Iterative depth-first walk of a spanning tree. Returning along
            // an already valid edge may repaint ink, but never crosses a
            // transparent/different-colour gap. Collinear points are folded
            // so SendInput only receives direction changes.
            var walk = new List<PixelPoint>();
            AppendCompressed(walk, start);
            var stack = new Stack<WalkFrame>();
            stack.Push(new WalkFrame(start, Neighbours(start).GetEnumerator()));
            while (stack.Count > 0)
            {
                var frame = stack.Peek();
                PixelPoint? next = null;
                while (frame.Neighbours.MoveNext())
                {
                    var candidate = frame.Neighbours.Current;
                    if (remaining.Remove(candidate))
                    {
                        next = candidate;
                        break;
                    }
                }

                if (next is { } child)
                {
                    AppendCompressed(walk, child);
                    stack.Push(new WalkFrame(child, Neighbours(child).GetEnumerator()));
                    continue;
                }

                frame.Neighbours.Dispose();
                stack.Pop();
                if (stack.Count > 0)
                {
                    AppendCompressed(walk, stack.Peek().Point);
                }
            }

            yield return walk;

            IEnumerable<PixelPoint> Neighbours(PixelPoint point)
            {
                // Horizontal preference gives each component a printer-like
                // local sweep while keeping every connecting segment on ink.
                foreach (var offset in neighbourOffsets)
                {
                    var candidate = new PixelPoint(point.X + offset.X, point.Y + offset.Y);
                    if (remaining.Contains(candidate) &&
                        ConnectorTouchesInk(point, candidate, mask, width, height))
                    {
                        yield return candidate;
                    }
                }
            }
        }
    }

    private static void AppendCompressed(List<PixelPoint> points, PixelPoint next)
    {
        if (points.Count > 0 && points[^1] == next)
        {
            return;
        }

        if (points.Count >= 2)
        {
            var first = points[^2];
            var middle = points[^1];
            var firstX = middle.X - first.X;
            var firstY = middle.Y - first.Y;
            var secondX = next.X - middle.X;
            var secondY = next.Y - middle.Y;
            if ((firstX * secondY) == (firstY * secondX) &&
                ((firstX * secondX) + (firstY * secondY)) > 0)
            {
                points[^1] = next;
                return;
            }
        }

        points.Add(next);
    }

    private sealed record WalkFrame(PixelPoint Point, IEnumerator<PixelPoint> Neighbours);

    private static bool ConnectorTouchesInk(
        PixelPoint first,
        PixelPoint second,
        bool[] mask,
        int width,
        int height)
    {
        var steps = Math.Max(Math.Abs(second.X - first.X), Math.Abs(second.Y - first.Y));
        for (var step = 0; step <= steps; step++)
        {
            var amount = steps == 0 ? 0d : step / (double)steps;
            var x = (int)Math.Round(first.X + ((second.X - first.X) * amount));
            var y = (int)Math.Round(first.Y + ((second.Y - first.Y) * amount));
            var touchesInk = false;
            for (var brushY = y - 1; brushY <= y && !touchesInk; brushY++)
            {
                for (var brushX = x - 1; brushX <= x; brushX++)
                {
                    if ((uint)brushX < (uint)width &&
                        (uint)brushY < (uint)height &&
                        mask[(brushY * width) + brushX])
                    {
                        touchesInk = true;
                        break;
                    }
                }
            }

            if (!touchesInk)
            {
                return false;
            }
        }

        return true;
    }
}
