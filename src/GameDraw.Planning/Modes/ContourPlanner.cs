using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning.Modes;

internal static class ContourPlanner
{
    public static DrawingPlan Create(
        QuantizedImage image,
        DrawingPlannerOptions options,
        DrawingMode mode = DrawingMode.Contour)
    {
        var strokesByColor = new Dictionary<int, List<DrawingStroke>>();
        foreach (var paletteIndex in PlanningUtilities.GetPaletteIndices(image, options))
        {
            var edges = BuildBoundaryEdges(image, paletteIndex, options);
            foreach (var loop in ChainEdges(edges))
            {
                var simplified = Simplify(loop);
                if (simplified.Count < 2)
                {
                    continue;
                }

                var points = simplified
                    .Select(point => PlanningUtilities.GridPoint(point.X, point.Y, image.Width, image.Height))
                    .ToArray();
                PlanningUtilities.AddStroke(
                    strokesByColor,
                    paletteIndex,
                    new DrawingStroke(points, options.CloseContours));
            }
        }

        return PlanningUtilities.BuildPlan(image, mode, strokesByColor, options);
    }

    private static List<GridEdge> BuildBoundaryEdges(
        QuantizedImage image,
        int paletteIndex,
        DrawingPlannerOptions options)
    {
        var edges = new List<GridEdge>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (!PlanningUtilities.IsSameColor(image, x, y, paletteIndex, options))
                {
                    continue;
                }

                if (!PlanningUtilities.IsSameColor(image, x, y - 1, paletteIndex, options))
                {
                    edges.Add(new GridEdge(new GridPoint(x, y), new GridPoint(x + 1, y)));
                }

                if (!PlanningUtilities.IsSameColor(image, x + 1, y, paletteIndex, options))
                {
                    edges.Add(new GridEdge(new GridPoint(x + 1, y), new GridPoint(x + 1, y + 1)));
                }

                if (!PlanningUtilities.IsSameColor(image, x, y + 1, paletteIndex, options))
                {
                    edges.Add(new GridEdge(new GridPoint(x + 1, y + 1), new GridPoint(x, y + 1)));
                }

                if (!PlanningUtilities.IsSameColor(image, x - 1, y, paletteIndex, options))
                {
                    edges.Add(new GridEdge(new GridPoint(x, y + 1), new GridPoint(x, y)));
                }
            }
        }

        return edges;
    }

    private static IEnumerable<List<GridPoint>> ChainEdges(List<GridEdge> edges)
    {
        var remaining = new HashSet<GridEdge>(edges);
        while (remaining.Count > 0)
        {
            var first = remaining
                .OrderBy(edge => edge.Start.Y)
                .ThenBy(edge => edge.Start.X)
                .ThenBy(edge => edge.End.Y)
                .ThenBy(edge => edge.End.X)
                .First();
            remaining.Remove(first);

            var loop = new List<GridPoint> { first.Start };
            var previous = first.Start;
            var current = first.End;
            var guard = edges.Count + 1;
            while (current != first.Start && guard-- > 0)
            {
                loop.Add(current);
                var candidates = remaining
                    .Where(edge => edge.Start == current)
                    .ToList();
                if (candidates.Count == 0)
                {
                    break;
                }

                var next = ChooseNext(previous, current, candidates);
                remaining.Remove(next);
                previous = current;
                current = next.End;
            }

            if (current == first.Start && loop.Count >= 2)
            {
                yield return loop;
            }
        }
    }

    private static GridEdge ChooseNext(
        GridPoint previous,
        GridPoint current,
        IReadOnlyList<GridEdge> candidates)
    {
        var incoming = Direction(previous, current);
        return candidates
            .OrderBy(edge => TurnRank(incoming, Direction(edge.Start, edge.End)))
            .ThenBy(edge => edge.End.Y)
            .ThenBy(edge => edge.End.X)
            .First();
    }

    private static int TurnRank(int incoming, int outgoing)
    {
        var turn = (outgoing - incoming + 4) % 4;
        return turn switch
        {
            0 => 0, // Continue straight whenever possible.
            1 => 1, // Clockwise corner is the normal outer contour.
            3 => 2, // Counter-clockwise corner handles concave regions.
            _ => 3
        };
    }

    private static int Direction(GridPoint start, GridPoint end)
    {
        var x = end.X - start.X;
        var y = end.Y - start.Y;
        return (x, y) switch
        {
            (1, 0) => 0,
            (0, 1) => 1,
            (-1, 0) => 2,
            (0, -1) => 3,
            _ => throw new DrawingPlanningException("윤곽선 에지 방향이 격자 단위가 아닙니다.")
        };
    }

    private static IReadOnlyList<GridPoint> Simplify(IReadOnlyList<GridPoint> loop)
    {
        if (loop.Count <= 2)
        {
            return loop;
        }

        var simplified = new List<GridPoint>(loop.Count);
        for (var index = 0; index < loop.Count; index++)
        {
            var previous = loop[(index + loop.Count - 1) % loop.Count];
            var current = loop[index];
            var next = loop[(index + 1) % loop.Count];
            var firstX = current.X - previous.X;
            var firstY = current.Y - previous.Y;
            var secondX = next.X - current.X;
            var secondY = next.Y - current.Y;
            if ((firstX * secondY) - (firstY * secondX) != 0)
            {
                simplified.Add(current);
            }
        }

        return simplified;
    }

    private readonly record struct GridPoint(int X, int Y);

    private readonly record struct GridEdge(GridPoint Start, GridPoint End);
}
