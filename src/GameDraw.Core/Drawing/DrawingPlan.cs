using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Models;

namespace GameDraw.Core.Drawing;

public sealed record Stroke(IReadOnlyList<NormalizedPoint> Points)
{
    public bool IsEmpty => Points.Count == 0;
}

public sealed record ColorGroup(RgbColor Color, IReadOnlyList<Stroke> Strokes);

public sealed record PlanStatistics(
    int ColorCount,
    int StrokeCount,
    int PointCount,
    int ColorChanges,
    double NormalizedTravelDistance);

public sealed record DrawingPlan(
    DrawingMode Mode,
    int Width,
    int Height,
    IReadOnlyList<ColorGroup> ColorGroups)
{
    public PlanStatistics Statistics
    {
        get
        {
            var strokes = ColorGroups.Sum(group => group.Strokes.Count);
            var points = ColorGroups.Sum(group => group.Strokes.Sum(stroke => stroke.Points.Count));
            var distance = ColorGroups
                .SelectMany(group => group.Strokes)
                .Sum(stroke =>
                {
                    var total = 0d;
                    for (var index = 1; index < stroke.Points.Count; index++)
                    {
                        total += stroke.Points[index - 1].DistanceTo(stroke.Points[index]);
                    }

                    return total;
                });

            return new PlanStatistics(
                ColorGroups.Count,
                strokes,
                points,
                Math.Max(0, ColorGroups.Count - 1),
                distance);
        }
    }

    public IEnumerable<(RgbColor Color, Stroke Stroke)> EnumerateStrokes()
    {
        foreach (var group in ColorGroups)
        {
            foreach (var stroke in group.Strokes)
            {
                yield return (group.Color, stroke);
            }
        }
    }

    public static DrawingPlan Empty(DrawingMode mode, ImageBuffer image) => new(mode, image.Width, image.Height, Array.Empty<ColorGroup>());
}
