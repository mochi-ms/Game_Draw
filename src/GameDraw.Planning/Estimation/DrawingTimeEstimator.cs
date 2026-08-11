using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;

namespace GameDraw.Planning;

internal static class DrawingTimeEstimator
{
    public static PlanEstimate Estimate(
        DrawingPlan plan,
        DrawingPlannerOptions options)
    {
        var strokeCount = 0;
        var pointCount = 0;
        var penDown = 0d;
        var penUp = 0d;
        var colorCount = plan.ColorGroups.Select(group => group.Color).Distinct().Count();
        var colorChanges = plan.ColorGroups
            .Zip(plan.ColorGroups.Skip(1), (first, second) => first.Color != second.Color)
            .Count(changed => changed);
        var current = new NormalizedPoint(0d, 0d);

        for (var groupIndex = 0; groupIndex < plan.ColorGroups.Count; groupIndex++)
        {
            var group = plan.ColorGroups[groupIndex];
            foreach (var stroke in group.Strokes)
            {
                strokeCount++;
                pointCount += stroke.Points.Count;
                penUp += DistancePixels(current, stroke.Points[0], plan.LogicalSize);
                penDown += DistancePixels(stroke, plan.LogicalSize);
                current = stroke.Points[^1];
            }
        }

        var totalTravel = penDown + (penUp * options.PenUpMovementMultiplier);
        var movementSeconds = totalTravel / options.MovementPixelsPerSecond;
        var strokeDelaySeconds = strokeCount *
            (options.InterStrokeDelayMilliseconds + options.PerStrokeSafetyDelayMilliseconds) / 1000d;
        var colorDelaySeconds = colorChanges * options.ColorChangeDelayMilliseconds / 1000d;
        var duration = TimeSpan.FromSeconds(Math.Max(0d, movementSeconds + strokeDelaySeconds + colorDelaySeconds));

        return new PlanEstimate(
            strokeCount,
            pointCount,
            colorCount,
            colorChanges,
            penDown,
            penUp,
            totalTravel,
            duration);
    }

    private static double DistancePixels(
        DrawingStroke stroke,
        PixelSize size)
    {
        var distance = 0d;
        for (var index = 1; index < stroke.Points.Count; index++)
        {
            distance += DistancePixels(stroke.Points[index - 1], stroke.Points[index], size);
        }

        if (stroke.IsClosed && stroke.Points.Count > 1)
        {
            distance += DistancePixels(stroke.Points[^1], stroke.Points[0], size);
        }

        return distance;
    }

    private static double DistancePixels(
        NormalizedPoint first,
        NormalizedPoint second,
        PixelSize size)
    {
        var x = (second.X - first.X) * size.Width;
        var y = (second.Y - first.Y) * size.Height;
        return Math.Sqrt((x * x) + (y * y));
    }
}
