using GameDraw.Core.Colors;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning.Modes;

/// <summary>
/// Removes only sub-brush, low-contrast palette islands. High-contrast small
/// features such as pupils, eyelashes, text and jewellery are preserved.
/// Processing is one non-cascading pass so a replacement can never spread
/// through a larger authored region.
/// </summary>
internal static class PaletteRegionSmoother
{
    public static byte[] Smooth(
        QuantizedImage image,
        DrawingPlannerOptions options)
    {
        var source = image.Indices.ToArray();
        var result = source.ToArray();
        var drawable = Enumerable.Range(0, source.Length)
            .Select(index => PlanningUtilities.IsDrawable(
                image,
                index % image.Width,
                index / image.Width,
                options))
            .ToArray();
        var visited = new bool[source.Length];
        var queue = new Queue<int>();

        for (var start = 0; start < source.Length; start++)
        {
            if (visited[start] || !drawable[start])
            {
                continue;
            }

            var paletteIndex = source[start];
            var component = new List<int>();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var index))
            {
                component.Add(index);
                var x = index % image.Width;
                var y = index / image.Width;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            if (component.Count > options.MaximumTinyColorRegionPixels)
            {
                continue;
            }

            var neighbours = new Dictionary<byte, int>();
            foreach (var index in component)
            {
                var x = index % image.Width;
                var y = index / image.Width;
                CountNeighbour(x - 1, y);
                CountNeighbour(x + 1, y);
                CountNeighbour(x, y - 1);
                CountNeighbour(x, y + 1);
            }

            var original = image.Palette[paletteIndex];
            var replacement = neighbours
                .Select(pair => new
                {
                    pair.Key,
                    pair.Value,
                    Distance = PerceptualRgbDistance(original, image.Palette[pair.Key])
                })
                .Where(item => item.Distance <= options.MaximumTinyColorRegionDistance)
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Distance)
                .ThenBy(item => item.Key)
                .FirstOrDefault();
            if (replacement is null)
            {
                continue;
            }

            foreach (var index in component)
            {
                result[index] = replacement.Key;
            }

            void Visit(int x, int y)
            {
                if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height)
                {
                    return;
                }

                var next = (y * image.Width) + x;
                if (visited[next] || !drawable[next] || source[next] != paletteIndex)
                {
                    return;
                }

                visited[next] = true;
                queue.Enqueue(next);
            }

            void CountNeighbour(int x, int y)
            {
                if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height)
                {
                    return;
                }

                var index = (y * image.Width) + x;
                var candidate = source[index];
                if (!drawable[index] || candidate == paletteIndex)
                {
                    return;
                }

                neighbours[candidate] = neighbours.GetValueOrDefault(candidate) + 1;
            }
        }

        return result;
    }

    private static double PerceptualRgbDistance(RgbColor first, RgbColor second)
    {
        var red = first.R - second.R;
        var green = first.G - second.G;
        var blue = first.B - second.B;
        var luminanceWeighted = Math.Sqrt(
            (red * red * 0.2126d) +
            (green * green * 0.7152d) +
            (blue * blue * 0.0722d));
        // Luminance weighting alone can understate a saturated red/blue edge.
        // Keep a chroma guard so lips, irises and colored line art remain even
        // when their component occupies only one physical pen footprint.
        var maximumChannelDelta = Math.Max(Math.Abs(red), Math.Max(Math.Abs(green), Math.Abs(blue)));
        return Math.Max(luminanceWeighted, maximumChannelDelta * 0.75d);
    }
}
