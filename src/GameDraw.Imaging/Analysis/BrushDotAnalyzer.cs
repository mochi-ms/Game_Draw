using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;

namespace GameDraw.Imaging.Analysis;

/// <summary>
/// Measures freshly stamped brush dots around known client-normalized points.
/// Searching around each expected point avoids treating unrelated animation or
/// chat changes as brush ink.
/// </summary>
public static class BrushDotAnalyzer
{
    public static IReadOnlyList<double> MeasureDiameters(
        ImageFrame before,
        ImageFrame after,
        NormalizedRect testRegion,
        IReadOnlyList<NormalizedPoint> expectedPoints)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(expectedPoints);
        if (before.Width != after.Width || before.Height != after.Height ||
            !testRegion.IsWithinUnitSquare)
        {
            return Array.Empty<double>();
        }

        var halfWidth = Math.Clamp(
            (int)Math.Ceiling(testRegion.Width * before.Width * 0.12d),
            6,
            64);
        var halfHeight = Math.Clamp(
            (int)Math.Ceiling(testRegion.Height * before.Height * 0.30d),
            6,
            64);
        var result = new List<double>(expectedPoints.Count);
        foreach (var point in expectedPoints)
        {
            if (!point.IsWithinUnitSquare)
            {
                continue;
            }

            var centerX = (int)Math.Round(point.X * Math.Max(0, before.Width - 1));
            var centerY = (int)Math.Round(point.Y * Math.Max(0, before.Height - 1));
            var left = Math.Max(0, centerX - halfWidth);
            var top = Math.Max(0, centerY - halfHeight);
            var right = Math.Min(before.Width, centerX + halfWidth + 1);
            var bottom = Math.Min(before.Height, centerY + halfHeight + 1);
            var width = right - left;
            var height = bottom - top;
            var changed = new bool[width * height];
            var absoluteInk = new bool[width * height];
            var lightValues = new int[width * height];
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var first = before[x, y].Color;
                    var second = after[x, y].Color;
                    var channelDelta =
                        Math.Abs(first.R - second.R) +
                        Math.Abs(first.G - second.G) +
                        Math.Abs(first.B - second.B);
                    var darkening =
                        (first.R + first.G + first.B) -
                        (second.R + second.G + second.B);
                    var index = ((y - top) * width) + x - left;
                    changed[index] =
                        channelDelta >= 18 && darkening >= 12;
                    lightValues[index] = second.R + second.G + second.B;
                }
            }

            // Directly recognize visible ink as well as the before/after
            // difference. A Direct3D capture can expose a newly stamped dot in
            // both frames, making its delta zero even though the dot is plainly
            // visible to the user.
            var orderedLight = lightValues.Order().ToArray();
            var backgroundLight = orderedLight[(orderedLight.Length * 3) / 4];
            var inkThreshold = Math.Clamp(backgroundLight - 45, 60, 640);
            for (var index = 0; index < lightValues.Length; index++)
            {
                absoluteInk[index] = lightValues[index] <= inkThreshold;
            }

            var localCenterX = centerX - left;
            var localCenterY = centerY - top;
            var seedDistance = Math.Clamp(Math.Min(halfWidth, halfHeight) / 3, 4, 18);
            var component = NearestComponentBounds(
                absoluteInk,
                width,
                height,
                localCenterX,
                localCenterY,
                seedDistance)
                ?? NearestComponentBounds(
                    changed,
                    width,
                    height,
                    localCenterX,
                    localCenterY,
                    seedDistance);
            if (component is { Width: > 0, Height: > 0, Area: >= 1 })
            {
                result.Add(Math.Max(component.Value.Width, component.Value.Height));
            }
        }

        return result;
    }

    private static (int Width, int Height, int Area)? NearestComponentBounds(
        bool[] mask,
        int width,
        int height,
        int centerX,
        int centerY,
        int maximumSeedDistance)
    {
        var visited = new bool[mask.Length];
        var queue = new Queue<int>();
        (int Width, int Height, int Area)? best = null;
        var bestDistanceSquared = int.MaxValue;
        for (var start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || visited[start])
            {
                continue;
            }

            var left = width;
            var top = height;
            var right = -1;
            var bottom = -1;
            var area = 0;
            var distanceSquared = int.MaxValue;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var index))
            {
                var x = index % width;
                var y = index / width;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
                area++;
                var deltaX = x - centerX;
                var deltaY = y - centerY;
                distanceSquared = Math.Min(distanceSquared, (deltaX * deltaX) + (deltaY * deltaY));
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

                        var next = ((y + offsetY) * width) + x + offsetX;
                        if (mask[next] && !visited[next])
                        {
                            visited[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            var candidate = (Width: right - left + 1, Height: bottom - top + 1, Area: area);
            if (distanceSquared <= maximumSeedDistance * maximumSeedDistance &&
                (best is null ||
                 distanceSquared < bestDistanceSquared ||
                 (distanceSquared == bestDistanceSquared && candidate.Area > best.Value.Area)))
            {
                best = candidate;
                bestDistanceSquared = distanceSquared;
            }
        }

        return best;
    }
}
