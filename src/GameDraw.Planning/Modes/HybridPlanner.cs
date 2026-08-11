using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning.Modes;

internal static class HybridPlanner
{
    public static DrawingPlan Create(
        QuantizedImage image,
        DrawingPlannerOptions options)
    {
        var contourPlan = ContourPlanner.Create(image, options, DrawingMode.Hybrid);
        var fillPlan = FillPlanner.Create(image, options, DrawingMode.Hybrid);
        var strokesByColor = new Dictionary<int, List<DrawingStroke>>();

        foreach (var group in contourPlan.ColorGroups)
        {
            var index = FindPaletteIndex(image, group.Color);
            if (index < 0)
            {
                continue;
            }

            foreach (var stroke in group.Strokes)
            {
                PlanningUtilities.AddStroke(strokesByColor, index, stroke);
            }
        }

        foreach (var group in fillPlan.ColorGroups)
        {
            var index = FindPaletteIndex(image, group.Color);
            if (index < 0)
            {
                continue;
            }

            foreach (var stroke in group.Strokes)
            {
                PlanningUtilities.AddStroke(strokesByColor, index, stroke);
            }
        }

        return PlanningUtilities.BuildPlan(image, DrawingMode.Hybrid, strokesByColor, options);
    }

    private static int FindPaletteIndex(QuantizedImage image, GameDraw.Core.Colors.RgbColor color)
    {
        for (var index = 0; index < image.Palette.Count; index++)
        {
            if (image.Palette[index] == color)
            {
                return index;
            }
        }

        return -1;
    }
}
