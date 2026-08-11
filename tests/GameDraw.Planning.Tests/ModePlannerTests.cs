using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Imaging;
using GameDraw.Core.Models;
using GameDraw.Imaging.Palettes;
using GameDraw.Imaging.Quantization;
using GameDraw.Planning.Modes;

namespace GameDraw.Planning.Tests;

public sealed class ModePlannerTests
{
    [Fact]
    public void PixelModeEmitsOnePointPerDrawablePixel()
    {
        var image = Quantize(new[]
        {
            RgbColor.Black, RgbColor.White,
            RgbColor.White, RgbColor.Black
        }, 2, 2);
        var planner = new DrawingPlanner();

        var result = planner.Plan(image, new DrawingPlannerOptions { Mode = DrawingMode.Pixel });

        Assert.Equal(DrawingMode.Pixel, result.SelectedMode);
        Assert.Equal(4, result.Estimate.StrokeCount);
        Assert.Equal(4, result.Estimate.PointCount);
        Assert.Equal(2, result.Plan.ColorGroups.Count);
    }

    [Fact]
    public void HorizontalAndVerticalScanlinesMergeRuns()
    {
        var image = Quantize(new[]
        {
            RgbColor.Black, RgbColor.Black, RgbColor.White,
            RgbColor.Black, RgbColor.Black, RgbColor.White
        }, 3, 2);
        var planner = new DrawingPlanner();

        var horizontal = planner.Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.HorizontalScanline
        });
        var vertical = planner.Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.VerticalScanline
        });

        Assert.Equal(4, horizontal.Estimate.StrokeCount);
        Assert.Equal(3, vertical.Estimate.StrokeCount);
        Assert.Contains(horizontal.Plan.EnumerateStrokes(), item => item.Stroke.Points.Count == 2);
        Assert.Contains(vertical.Plan.EnumerateStrokes(), item => item.Stroke.Points.Count == 2);
    }

    [Fact]
    public void ContourModeBuildsClosedSimplifiedRectangle()
    {
        var image = Quantize(new[]
        {
            RgbColor.Black, RgbColor.Black,
            RgbColor.Black, RgbColor.Black
        }, 2, 2);
        var planner = new DrawingPlanner();

        var result = planner.Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.Contour
        });
        var stroke = Assert.Single(result.Plan.ColorGroups).Strokes.Single();

        Assert.True(stroke.IsClosed);
        Assert.Equal(4, stroke.Points.Count);
        Assert.Contains(new GameDraw.Core.Geometry.NormalizedPoint(0, 0), stroke.Points);
        Assert.Contains(new GameDraw.Core.Geometry.NormalizedPoint(1, 1), stroke.Points);
    }

    [Fact]
    public void FillAndHybridModesKeepBothSurfaceAndContourStrokes()
    {
        var image = Quantize(new[]
        {
            RgbColor.Black, RgbColor.Black,
            RgbColor.Black, RgbColor.Black
        }, 2, 2);
        var planner = new DrawingPlanner();

        var fill = planner.Plan(image, new DrawingPlannerOptions { Mode = DrawingMode.Fill });
        var hybrid = planner.Plan(image, new DrawingPlannerOptions { Mode = DrawingMode.Hybrid });

        Assert.Equal(2, fill.Estimate.StrokeCount);
        Assert.Equal(3, hybrid.Estimate.StrokeCount);
        Assert.Contains(hybrid.Plan.EnumerateStrokes(), item => item.Stroke.IsClosed);
    }

    [Fact]
    public void AutomaticModeScoresEveryStrategyAndReturnsConcreteMode()
    {
        var image = Quantize(new[]
        {
            RgbColor.Black, RgbColor.Black, RgbColor.Black, RgbColor.Black,
            RgbColor.Black, RgbColor.White, RgbColor.White, RgbColor.Black,
            RgbColor.Black, RgbColor.White, RgbColor.White, RgbColor.Black,
            RgbColor.Black, RgbColor.Black, RgbColor.Black, RgbColor.Black
        }, 4, 4);
        var planner = new DrawingPlanner();

        var result = planner.Plan(image, new DrawingPlannerOptions { Mode = DrawingMode.Auto });

        Assert.NotEqual(DrawingMode.Auto, result.SelectedMode);
        Assert.Equal(6, result.Candidates.Count);
        Assert.Contains(result.Candidates, candidate => candidate.Mode == DrawingMode.Pixel);
        Assert.True(result.Candidates.SequenceEqual(result.Candidates.OrderBy(candidate => candidate.Score)));
    }

    [Fact]
    public void PlannerSkipsTransparentPixelsByDefault()
    {
        var frame = new ImageFrame(2, 2, new[]
        {
            new RgbaPixel(RgbColor.White, 0),
            RgbaPixel.Opaque(RgbColor.Black),
            new RgbaPixel(RgbColor.White, 0),
            new RgbaPixel(RgbColor.White, 0)
        });
        var palette = new ColorPalette(new[] { RgbColor.White, RgbColor.Black });
        var image = new PaletteQuantizer().Quantize(frame, palette);

        var result = new DrawingPlanner().Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.Pixel
        });

        Assert.Equal(1, result.Estimate.StrokeCount);
    }

    [Fact]
    public void TravelEstimatorIncludesPenUpMovementAndDelays()
    {
        var image = Quantize(new[]
        {
            RgbColor.Black, RgbColor.White
        }, 2, 1);
        var planner = new DrawingPlanner();

        var result = planner.Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.Pixel,
            MovementPixelsPerSecond = 100,
            InterStrokeDelayMilliseconds = 100,
            ColorChangeDelayMilliseconds = 200
        });

        Assert.True(result.Estimate.PenUpTravelPixels > 0d);
        Assert.Equal(1, result.Estimate.ColorChanges);
        Assert.True(result.Estimate.EstimatedDuration > TimeSpan.Zero);
    }

    private static QuantizedImage Quantize(IReadOnlyList<RgbColor> colors, int width, int height)
    {
        var frame = new ImageFrame(
            width,
            height,
            colors.Select(RgbaPixel.Opaque).ToArray());
        var palette = new ColorPalette(colors.Distinct());
        return new PaletteQuantizer().Quantize(frame, palette, new QuantizationOptions
        {
            PreserveAlpha = false
        });
    }
}
