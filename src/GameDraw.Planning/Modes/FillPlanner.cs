using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning.Modes;

internal static class FillPlanner
{
    public static DrawingPlan Create(
        QuantizedImage image,
        DrawingPlannerOptions options,
        DrawingMode mode = DrawingMode.Fill)
    {
        var strokesByColor = new Dictionary<int, List<DrawingStroke>>();
        for (var y = 0; y < image.Height; y += options.FillRowStep)
        {
            var reverse = options.AlternateScanDirection && ((y / options.FillRowStep) % 2 == 1);
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

        return PlanningUtilities.BuildPlan(image, mode, strokesByColor, options);
    }
}
