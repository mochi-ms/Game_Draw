using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;

namespace GameDraw.Planning;

/// <summary>
/// Post-processing shared by preview and execution. The same immutable plan is
/// rendered and executed, eliminating the old analysis-preview mismatch.
/// </summary>
public static class DrawingPlanPostProcessor
{
    public static DrawingPlan PrioritizeRegion(DrawingPlan plan, NormalizedRect region)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!region.IsWithinUnitSquare || plan.ColorGroups.Count == 0)
        {
            return plan;
        }

        var priority = new List<DrawingColorGroup>();
        var remainder = new List<DrawingColorGroup>();
        foreach (var group in plan.ColorGroups)
        {
            var first = group.Strokes
                .Where(stroke => Intersects(stroke, region))
                .OrderBy(stroke => FacialFeatureRank(stroke, region))
                .ThenBy(stroke => DistanceFromRegionCenter(stroke, region))
                .ToArray();
            var later = group.Strokes.Where(stroke => !Intersects(stroke, region)).ToArray();
            if (first.Length > 0)
            {
                priority.Add(new DrawingColorGroup(group.Color, first));
            }

            if (later.Length > 0)
            {
                remainder.Add(new DrawingColorGroup(group.Color, later));
            }
        }

        return priority.Count == 0
            ? plan
            : new DrawingPlan(plan.Mode, plan.LogicalSize, priority.Concat(remainder));
    }

    public static ImageFrame RenderPreview(DrawingPlan plan, int brushDiameterPixels = 1)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (brushDiameterPixels is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(brushDiameterPixels));
        }
        var width = plan.LogicalSize.Width;
        var height = plan.LogicalSize.Height;
        var pixels = Enumerable.Repeat(RgbaPixel.Opaque(RgbColor.White), checked(width * height)).ToArray();
        foreach (var group in plan.ColorGroups)
        {
            foreach (var stroke in group.Strokes)
            {
                var previous = ToPixel(stroke.Points[0], width, height);
                Paint(previous.X, previous.Y, group.Color);
                for (var index = 1; index < stroke.Points.Count; index++)
                {
                    var next = ToPixel(stroke.Points[index], width, height);
                    PaintLine(previous, next, group.Color);
                    previous = next;
                }

                if (stroke.IsClosed && stroke.Points.Count > 1)
                {
                    PaintLine(previous, ToPixel(stroke.Points[0], width, height), group.Color);
                }
            }
        }

        return new ImageFrame(width, height, pixels);

        void PaintLine(PixelPoint first, PixelPoint second, RgbColor color)
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
                Paint(x, y, color);
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

        void Paint(int x, int y, RgbColor color)
        {
            var radius = brushDiameterPixels / 2d;
            var extent = brushDiameterPixels / 2;
            for (var offsetY = -extent; offsetY <= extent; offsetY++)
            {
                for (var offsetX = -extent; offsetX <= extent; offsetX++)
                {
                    if ((offsetX * offsetX) + (offsetY * offsetY) > radius * radius)
                    {
                        continue;
                    }

                    var targetX = x + offsetX;
                    var targetY = y + offsetY;
                    if ((uint)targetX < (uint)width && (uint)targetY < (uint)height)
                    {
                        pixels[(targetY * width) + targetX] = RgbaPixel.Opaque(color);
                    }
                }
            }
        }
    }

    private static bool Intersects(DrawingStroke stroke, NormalizedRect region)
    {
        var centerX = 0d;
        var centerY = 0d;
        foreach (var point in stroke.Points)
        {
            if (Contains(region, point))
            {
                return true;
            }

            centerX += point.X;
            centerY += point.Y;
        }

        return Contains(region, new NormalizedPoint(centerX / stroke.Points.Count, centerY / stroke.Points.Count));
    }

    private static int FacialFeatureRank(DrawingStroke stroke, NormalizedRect region)
    {
        var center = StrokeCenter(stroke);
        var relativeY = region.Height <= double.Epsilon
            ? 1d
            : (center.Y - region.Y) / region.Height;
        var relativeX = region.Width <= double.Epsilon
            ? 0.5d
            : (center.X - region.X) / region.Width;
        // Eye band first, then nose/mouth centre, then hair and face outline.
        if (relativeY is >= 0.27d and <= 0.53d && relativeX is >= 0.08d and <= 0.92d)
        {
            return 0;
        }

        if (relativeY is > 0.53d and <= 0.82d && relativeX is >= 0.2d and <= 0.8d)
        {
            return 1;
        }

        return 2;
    }

    private static double DistanceFromRegionCenter(DrawingStroke stroke, NormalizedRect region)
    {
        var center = StrokeCenter(stroke);
        var x = center.X - region.Center.X;
        var y = center.Y - region.Center.Y;
        return (x * x) + (y * y);
    }

    private static NormalizedPoint StrokeCenter(DrawingStroke stroke)
        => new(
            stroke.Points.Average(point => point.X),
            stroke.Points.Average(point => point.Y));

    private static bool Contains(NormalizedRect rect, NormalizedPoint point)
        => point.X >= rect.X && point.X <= rect.X + rect.Width &&
           point.Y >= rect.Y && point.Y <= rect.Y + rect.Height;

    private static PixelPoint ToPixel(NormalizedPoint point, int width, int height)
        => new(
            Math.Clamp((int)Math.Round(point.X * Math.Max(0, width - 1)), 0, width - 1),
            Math.Clamp((int)Math.Round(point.Y * Math.Max(0, height - 1)), 0, height - 1));
}
