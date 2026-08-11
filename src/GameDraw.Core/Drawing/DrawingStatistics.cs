namespace GameDraw.Core.Drawing;

public sealed record DrawingStatistics(
    int ColorCount,
    int StrokeCount,
    int PointCount,
    int ColorChanges,
    double NormalizedTravelDistance)
{
    public static DrawingStatistics From(IReadOnlyList<DrawingColorGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var strokeCount = 0;
        var pointCount = 0;
        var travelDistance = 0d;

        foreach (var group in groups)
        {
            strokeCount += group.Strokes.Count;
            foreach (var stroke in group.Strokes)
            {
                pointCount += stroke.Points.Count;
                travelDistance += stroke.TravelDistance;
            }
        }

        return new DrawingStatistics(
            groups.Count,
            strokeCount,
            pointCount,
            Math.Max(0, groups.Count - 1),
            travelDistance);
    }
}
