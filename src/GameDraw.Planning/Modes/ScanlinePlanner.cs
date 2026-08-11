using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning.Modes;

internal static class ScanlinePlanner
{
    public static DrawingPlan CreateHorizontal(
        QuantizedImage image,
        DrawingPlannerOptions options)
    {
        var strokesByColor = new Dictionary<int, List<DrawingStroke>>();
        for (var y = 0; y < image.Height; y++)
        {
            var reverse = options.AlternateScanDirection && (y % 2 == 1);
            var x = reverse ? image.Width - 1 : 0;
            var end = reverse ? -1 : image.Width;
            var step = reverse ? -1 : 1;
            while (x != end)
            {
                if (!PlanningUtilities.IsDrawable(image, x, y, options))
                {
                    x += step;
                    continue;
                }

                var paletteIndex = image[x, y];
                var start = x;
                while (x != end
                    && PlanningUtilities.IsDrawable(image, x, y, options)
                    && image[x, y] == paletteIndex)
                {
                    x += step;
                }

                var last = x - step;
                var points = start == last
                    ? new[] { PlanningUtilities.Center(start, y, image.Width, image.Height) }
                    : new[]
                    {
                        PlanningUtilities.Center(start, y, image.Width, image.Height),
                        PlanningUtilities.Center(last, y, image.Width, image.Height)
                    };
                PlanningUtilities.AddStroke(strokesByColor, paletteIndex, new DrawingStroke(points));
            }
        }

        return PlanningUtilities.BuildPlan(image, DrawingMode.HorizontalScanline, strokesByColor, options);
    }

    public static DrawingPlan CreateVertical(
        QuantizedImage image,
        DrawingPlannerOptions options)
    {
        var strokesByColor = new Dictionary<int, List<DrawingStroke>>();
        for (var x = 0; x < image.Width; x++)
        {
            var reverse = options.AlternateScanDirection && (x % 2 == 1);
            var y = reverse ? image.Height - 1 : 0;
            var end = reverse ? -1 : image.Height;
            var step = reverse ? -1 : 1;
            while (y != end)
            {
                if (!PlanningUtilities.IsDrawable(image, x, y, options))
                {
                    y += step;
                    continue;
                }

                var paletteIndex = image[x, y];
                var start = y;
                while (y != end
                    && PlanningUtilities.IsDrawable(image, x, y, options)
                    && image[x, y] == paletteIndex)
                {
                    y += step;
                }

                var last = y - step;
                var points = start == last
                    ? new[] { PlanningUtilities.Center(x, start, image.Width, image.Height) }
                    : new[]
                    {
                        PlanningUtilities.Center(x, start, image.Width, image.Height),
                        PlanningUtilities.Center(x, last, image.Width, image.Height)
                    };
                PlanningUtilities.AddStroke(strokesByColor, paletteIndex, new DrawingStroke(points));
            }
        }

        return PlanningUtilities.BuildPlan(image, DrawingMode.VerticalScanline, strokesByColor, options);
    }
}
