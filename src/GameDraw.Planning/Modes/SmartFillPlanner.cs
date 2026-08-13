using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning.Modes;

/// <summary>
/// Builds a deliberate two-phase workflow. Only the subject silhouette is
/// sketched first; the complete quantized photograph is then laid over it
/// with short, local pencil-stamp chunks. The underdrawing therefore guides
/// the execution order without becoming a black border around every color
/// patch in the final result.
/// </summary>
internal static class SmartFillPlanner
{
    public static DrawingPlan Create(QuantizedImage image, DrawingPlannerOptions options)
    {
        var drawable = Enumerable.Range(0, image.Width * image.Height)
            .Select(index => PlanningUtilities.IsDrawable(
                image,
                index % image.Width,
                index / image.Width,
                options))
            .ToArray();
        var subjectMap = new byte[drawable.Length];
        var subjectComponents = FindComponents(image.Width, image.Height, subjectMap, drawable)
            .OrderByDescending(component => component.Pixels.Count)
            .ThenBy(component => component.Top)
            .ThenBy(component => component.Left)
            .ToArray();
        var outlines = new List<DrawingStroke>();
        var minimumOutlinePixels = Math.Max(8, (int)Math.Ceiling(options.BrushDiameterPixels * 4d));

        foreach (var component in subjectComponents)
        {
            if (component.Pixels.Count < minimumOutlinePixels)
            {
                continue;
            }

            // Letterbox/background remnants commonly form a full rectangular
            // component. It is not a subject silhouette and creates exactly
            // the large square border seen on the Podiums canvas.
            if (component.Left == 0 || component.Top == 0 ||
                component.Right == image.Width - 1 || component.Bottom == image.Height - 1)
            {
                continue;
            }

            var loops = TraceBoundary(component, image.Width, image.Height)
                .Select(Simplify)
                .Where(loop => loop.Count >= 3)
                .ToArray();
            foreach (var simplified in loops)
            {
                var points = simplified.Select(point => PlanningUtilities.GridPoint(
                        point.X,
                        point.Y,
                        image.Width,
                        image.Height))
                    .ToArray();
                outlines.Add(new DrawingStroke(points, isClosed: true, toolAction: DrawingToolAction.Pencil));
            }
        }

        // Quantization can leave thousands of isolated one-pixel color islands.
        // They are visually insignificant at Podiums' physical pen pitch but
        // each island requires a costly and risky up/move/down transition.
        // Merge only those singletons in one non-cascading pass; multi-pixel
        // facial, hair, and clothing details remain untouched.
        var cleanedColorMap = SmoothTinyRegions(
            image,
            drawable,
            maximumRegionPixels: 1,
            maximumPasses: 1);
        var colorPlan = SafeStampPlanner.Create(image, options, cleanedColorMap);
        var groups = new List<DrawingColorGroup>();
        if (outlines.Count > 0)
        {
            groups.Add(new DrawingColorGroup(
                FindDarkestPaletteColor(image),
                OrderUnderdrawing(outlines, options.PriorityRegion)));
        }

        // The complete color pass is intentionally last, so it covers the
        // construction line everywhere except the true outside contour.
        groups.AddRange(colorPlan.ColorGroups);

        return new DrawingPlan(
            DrawingMode.SmartFill,
            new PixelSize(image.Width, image.Height),
            groups);
    }

    private static RgbColor FindDarkestPaletteColor(QuantizedImage image)
        => image.Palette.Colors
            .OrderBy(color => (color.R * 299) + (color.G * 587) + (color.B * 114))
            .First();

    private static IEnumerable<DrawingStroke> BuildFallbackStamps(
        Component component,
        int width,
        int height,
        int brushDiameter)
    {
        var member = component.Pixels.ToHashSet();
        var covered = new HashSet<int>();
        var radius = Math.Max(0, brushDiameter / 2);
        foreach (var index in component.Pixels.OrderBy(value => value))
        {
            if (covered.Contains(index))
            {
                continue;
            }

            var x = index % width;
            var y = index / width;
            yield return new DrawingStroke(
                new[] { PlanningUtilities.Center(x, y, width, height) },
                toolAction: DrawingToolAction.Pencil);
            for (var offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (var offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    var candidateX = x + offsetX;
                    var candidateY = y + offsetY;
                    if ((uint)candidateX >= (uint)width || (uint)candidateY >= (uint)height)
                    {
                        continue;
                    }

                    var candidate = (candidateY * width) + candidateX;
                    if (member.Contains(candidate))
                    {
                        covered.Add(candidate);
                    }
                }
            }
        }
    }

    private static IReadOnlyList<DrawingStroke> OrderUnderdrawing(
        List<DrawingStroke> outlines,
        NormalizedRect? priorityRegion)
    {
        if (outlines.Count <= 2)
        {
            return outlines;
        }

        var measured = outlines
            .Select((stroke, index) =>
            {
                var left = stroke.Points.Min(point => point.X);
                var top = stroke.Points.Min(point => point.Y);
                var right = stroke.Points.Max(point => point.X);
                var bottom = stroke.Points.Max(point => point.Y);
                var area = Math.Max(0d, right - left) * Math.Max(0d, bottom - top);
                var inPriority = priorityRegion is { } region &&
                    right >= region.X && left <= region.X + region.Width &&
                    bottom >= region.Y && top <= region.Y + region.Height;
                return new { Stroke = stroke, Index = index, Area = area, InPriority = inPriority };
            })
            .ToArray();
        var largestArea = measured.Max(item => item.Area);
        var silhouetteCutoff = largestArea * 0.98d;
        return measured
            .OrderBy(item => item.Area >= silhouetteCutoff ? 0 : item.InPriority ? 1 : 2)
            .ThenByDescending(item => item.Area)
            .ThenBy(item => item.Index)
            .Select(item => item.Stroke)
            .ToArray();
    }

    private static byte[] SmoothTinyRegions(
        QuantizedImage image,
        bool[] drawable,
        int maximumRegionPixels,
        int maximumPasses)
    {
        var map = image.Indices.ToArray();
        for (var pass = 0; pass < maximumPasses; pass++)
        {
            var components = FindComponents(image.Width, image.Height, map, drawable);
            var changed = false;
            foreach (var component in components.Where(item => item.Pixels.Count <= maximumRegionPixels))
            {
                var neighbors = new Dictionary<byte, int>();
                foreach (var index in component.Pixels)
                {
                    var x = index % image.Width;
                    var y = index / image.Width;
                    Count(x - 1, y);
                    Count(x + 1, y);
                    Count(x, y - 1);
                    Count(x, y + 1);
                }

                if (neighbors.Count == 0)
                {
                    continue;
                }

                var replacement = neighbors
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key)
                    .First().Key;
                foreach (var index in component.Pixels)
                {
                    map[index] = replacement;
                }

                changed = true;

                void Count(int x, int y)
                {
                    if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height)
                    {
                        return;
                    }

                    var index = (y * image.Width) + x;
                    var candidate = map[index];
                    if (!drawable[index] || candidate == component.PaletteIndex)
                    {
                        return;
                    }

                    neighbors[candidate] = neighbors.GetValueOrDefault(candidate) + 1;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return map;
    }

    private static List<Component> FindComponents(
        int width,
        int height,
        byte[] regionMap,
        bool[] drawable)
    {
        var visited = new bool[width * height];
        var queue = new Queue<int>();
        var components = new List<Component>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = (y * width) + x;
                if (visited[start] || !drawable[start])
                {
                    continue;
                }

                var paletteIndex = regionMap[start];
                var pixels = new List<int>();
                visited[start] = true;
                queue.Enqueue(start);
                while (queue.TryDequeue(out var index))
                {
                    pixels.Add(index);
                    var currentX = index % width;
                    var currentY = index / width;
                    Visit(currentX - 1, currentY);
                    Visit(currentX + 1, currentY);
                    Visit(currentX, currentY - 1);
                    Visit(currentX, currentY + 1);
                }

                components.Add(new Component(paletteIndex, pixels, width));

                void Visit(int nextX, int nextY)
                {
                    if ((uint)nextX >= (uint)width || (uint)nextY >= (uint)height)
                    {
                        return;
                    }

                    var next = (nextY * width) + nextX;
                    if (visited[next] || !drawable[next] || regionMap[next] != paletteIndex)
                    {
                        return;
                    }

                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
        }

        return components;
    }

    private static List<IReadOnlyList<GridPoint>> TraceBoundary(
        Component component,
        int width,
        int height)
    {
        var member = component.Pixels.ToHashSet();
        var edges = new List<GridEdge>();
        foreach (var index in component.Pixels)
        {
            var x = index % width;
            var y = index / width;
            if (!Contains(x, y - 1)) edges.Add(new GridEdge(new GridPoint(x, y), new GridPoint(x + 1, y)));
            if (!Contains(x + 1, y)) edges.Add(new GridEdge(new GridPoint(x + 1, y), new GridPoint(x + 1, y + 1)));
            if (!Contains(x, y + 1)) edges.Add(new GridEdge(new GridPoint(x + 1, y + 1), new GridPoint(x, y + 1)));
            if (!Contains(x - 1, y)) edges.Add(new GridEdge(new GridPoint(x, y + 1), new GridPoint(x, y)));
        }

        var remaining = new HashSet<GridEdge>(edges);
        var outgoing = edges.GroupBy(edge => edge.Start)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var loops = new List<IReadOnlyList<GridPoint>>();
        while (remaining.Count > 0)
        {
            var first = remaining.OrderBy(edge => edge.Start.Y).ThenBy(edge => edge.Start.X).First();
            remaining.Remove(first);
            var loop = new List<GridPoint> { first.Start };
            var current = first.End;
            var guard = edges.Count + 1;
            while (current != first.Start && guard-- > 0)
            {
                loop.Add(current);
                if (!outgoing.TryGetValue(current, out var candidates))
                {
                    break;
                }

                var next = candidates.FirstOrDefault(remaining.Contains);
                if (next == default)
                {
                    break;
                }

                remaining.Remove(next);
                current = next.End;
            }

            if (current == first.Start)
            {
                loops.Add(loop);
            }
        }

        return loops;

        bool Contains(int x, int y)
            => (uint)x < (uint)width && (uint)y < (uint)height && member.Contains((y * width) + x);
    }

    private static GridPoint? FindSafeFillSeed(
        Component component,
        int width,
        int height,
        double brushDiameter)
    {
        if (component.Left == 0 || component.Top == 0 ||
            component.Right == width - 1 || component.Bottom == height - 1)
        {
            // Never use a paint bucket on a region that relies on the canvas
            // edge as part of its enclosure.
            return null;
        }

        var minimumArea = Math.Max(9, (int)Math.Ceiling(brushDiameter * brushDiameter * 3d));
        if (component.Pixels.Count < minimumArea)
        {
            return null;
        }

        var member = component.Pixels.ToHashSet();
        var distance = new Dictionary<int, int>(component.Pixels.Count);
        var queue = new Queue<int>();
        foreach (var index in component.Pixels)
        {
            var x = index % width;
            var y = index / width;
            if (!member.Contains((y * width) + Math.Max(0, x - 1)) ||
                !member.Contains((y * width) + Math.Min(width - 1, x + 1)) ||
                !member.Contains((Math.Max(0, y - 1) * width) + x) ||
                !member.Contains((Math.Min(height - 1, y + 1) * width) + x) ||
                x == 0 || y == 0 || x == width - 1 || y == height - 1)
            {
                distance[index] = 0;
                queue.Enqueue(index);
            }
        }

        while (queue.TryDequeue(out var index))
        {
            var x = index % width;
            var y = index / width;
            Visit(x - 1, y, index);
            Visit(x + 1, y, index);
            Visit(x, y - 1, index);
            Visit(x, y + 1, index);
        }

        var best = component.Pixels
            .Where(distance.ContainsKey)
            .OrderByDescending(index => distance[index])
            .ThenBy(index => index)
            .FirstOrDefault();
        var requiredDistance = Math.Max(1, (int)Math.Ceiling(brushDiameter / 2d));
        return distance.TryGetValue(best, out var bestDistance) && bestDistance >= requiredDistance
            ? new GridPoint(best % width, best / width)
            : null;

        void Visit(int x, int y, int previous)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                return;
            }

            var index = (y * width) + x;
            if (!member.Contains(index) || distance.ContainsKey(index))
            {
                return;
            }

            distance[index] = distance[previous] + 1;
            queue.Enqueue(index);
        }
    }

    private static IReadOnlyList<GridPoint> Simplify(IReadOnlyList<GridPoint> loop)
    {
        var simplified = new List<GridPoint>(loop.Count);
        for (var index = 0; index < loop.Count; index++)
        {
            var previous = loop[(index + loop.Count - 1) % loop.Count];
            var current = loop[index];
            var next = loop[(index + 1) % loop.Count];
            if (((current.X - previous.X) * (next.Y - current.Y)) -
                ((current.Y - previous.Y) * (next.X - current.X)) != 0)
            {
                simplified.Add(current);
            }
        }

        return simplified;
    }

    private sealed record Component(int PaletteIndex, IReadOnlyList<int> Pixels, int Width)
    {
        public int Left { get; } = Pixels.Min(index => index % Width);
        public int Top { get; } = Pixels.Min(index => index / Width);
        public int Right { get; } = Pixels.Max(index => index % Width);
        public int Bottom { get; } = Pixels.Max(index => index / Width);
    }

    private readonly record struct GridPoint(int X, int Y);

    private readonly record struct GridEdge(GridPoint Start, GridPoint End);
}
