using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Imaging;
using GameDraw.Core.Models;

namespace GameDraw.Core.Tests;

public sealed class ImagingAndPlanningTests
{
    [Fact]
    public void ScanlineMergesContiguousRunsAndGroupsColors()
    {
        var red = new RgbColor(255, 0, 0);
        var blue = new RgbColor(0, 0, 255);
        var image = new ImageBuffer(6, 2, new RgbColor?[]
        {
            red, red, red, null, blue, blue,
            red, red, null, blue, blue, blue
        });

        var plan = new DrawingPlanner().CreatePlan(image, DrawingMode.Scanline, new DrawingPlannerOptions { Serpentine = false });

        Assert.Equal(2, plan.Statistics.ColorCount);
        Assert.Equal(4, plan.Statistics.StrokeCount);
        Assert.Equal(2, plan.ColorGroups.Single(group => group.Color == red).Strokes.Count);
        Assert.Equal(2, plan.ColorGroups.Single(group => group.Color == blue).Strokes.Count);
        Assert.All(plan.ColorGroups.SelectMany(group => group.Strokes), stroke => Assert.Equal(2, stroke.Points.Count));
    }

    [Fact]
    public void PixelModeCreatesOnePointStrokePerPixel()
    {
        var image = new ImageBuffer(2, 2, new RgbColor?[]
        {
            new RgbColor(0, 0, 0), null,
            new RgbColor(0, 0, 0), new RgbColor(255, 255, 255)
        });

        var plan = new DrawingPlanner().CreatePlan(image, DrawingMode.Pixel);

        Assert.Equal(3, plan.Statistics.StrokeCount);
        Assert.All(plan.EnumerateStrokes(), item => Assert.Single(item.Stroke.Points));
    }

    [Fact]
    public void BackgroundIgnoreRemovesWhitePixels()
    {
        var image = new ImageBuffer(3, 1, new RgbColor?[]
        {
            new RgbColor(255, 255, 255),
            new RgbColor(220, 30, 30),
            new RgbColor(250, 250, 250)
        });

        var processed = new ImageProcessor().Process(image, new ImageProcessingOptions
        {
            TargetWidth = 3,
            TargetHeight = 1,
            Background = BackgroundMode.IgnoreWhite,
            ColorCount = 2
        });

        Assert.Null(processed[0, 0]);
        Assert.NotNull(processed[1, 0]);
        Assert.Null(processed[2, 0]);
    }

    [Fact]
    public void FixedPaletteMappingUsesConfiguredColors()
    {
        var image = new ImageBuffer(2, 1, new RgbColor?[]
        {
            new RgbColor(252, 20, 20),
            new RgbColor(20, 20, 252)
        });

        var processed = new ImageProcessor().Process(image, new ImageProcessingOptions
        {
            TargetWidth = 2,
            TargetHeight = 1,
            AdapterKind = ColorAdapterKind.FixedPalette,
            Palette = new[]
            {
                new GameDraw.Core.Profiles.PaletteEntry { Name = "Red", Color = new RgbColor(255, 0, 0) },
                new GameDraw.Core.Profiles.PaletteEntry { Name = "Blue", Color = new RgbColor(0, 0, 255) }
            }
        });

        Assert.Equal(new RgbColor(255, 0, 0), processed[0, 0]);
        Assert.Equal(new RgbColor(0, 0, 255), processed[1, 0]);
    }
}
