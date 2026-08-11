using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning.Modes;

internal static class PixelPlanner
{
    public static DrawingPlan Create(
        QuantizedImage image,
        DrawingPlannerOptions options)
    {
        var strokesByColor = new Dictionary<int, List<DrawingStroke>>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (!PlanningUtilities.IsDrawable(image, x, y, options))
                {
                    continue;
                }

                var stroke = new DrawingStroke(new[]
                {
                    PlanningUtilities.Center(x, y, image.Width, image.Height)
                });
                PlanningUtilities.AddStroke(strokesByColor, image[x, y], stroke);
            }
        }

        return PlanningUtilities.BuildPlan(image, DrawingMode.Pixel, strokesByColor, options);
    }
}
