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
    public void CleanStrokeModeTurnsAThickLineIntoOneContinuousSimplifiedStroke()
    {
        const int width = 12;
        const int height = 7;
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        for (var y = 2; y <= 4; y++)
        {
            for (var x = 1; x <= 10; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }

        var frame = new ImageFrame(width, height, pixels);
        var quantized = new PaletteQuantizer().Quantize(
            frame,
            new ColorPalette(new[] { RgbColor.Black }),
            new QuantizationOptions { PreserveAlpha = true });

        var result = new DrawingPlanner().Plan(quantized, new DrawingPlannerOptions
        {
            Mode = DrawingMode.CleanStroke
        });

        var stroke = Assert.Single(result.Plan.ColorGroups).Strokes.Single();
        Assert.False(stroke.IsClosed);
        Assert.Equal(2, stroke.Points.Count);
        Assert.True(stroke.Points[0].X < stroke.Points[^1].X);
        Assert.Equal(DrawingMode.CleanStroke, result.SelectedMode);
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
    public void AutomaticModeHandlesDenseMediumImageWithoutContourSearchExplosion()
    {
        const int size = 128;
        var pixels = Enumerable.Range(0, size * size)
            .Select(index => ((index % size) + (index / size)) % 2 == 0
                ? RgbColor.Black
                : RgbColor.White)
            .ToArray();
        var image = Quantize(pixels, size, size);

        var result = new DrawingPlanner().Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.Auto
        });

        Assert.NotEqual(DrawingMode.Auto, result.SelectedMode);
        Assert.Equal(6, result.Candidates.Count);
        Assert.Same(result.Plan, result.Candidates.Single(candidate => candidate.Mode == result.SelectedMode).Plan);
        Assert.All(
            result.Candidates.Where(candidate => candidate.Mode != result.SelectedMode),
            candidate => Assert.Empty(candidate.Plan.ColorGroups));
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

    [Fact]
    public void PlanPreviewRendersTheExactStrokePathOnWhite()
    {
        var stroke = new DrawingStroke(new[]
        {
            new GameDraw.Core.Geometry.NormalizedPoint(0, 0),
            new GameDraw.Core.Geometry.NormalizedPoint(1, 1)
        });
        var plan = new DrawingPlan(
            DrawingMode.CleanStroke,
            new GameDraw.Core.Geometry.PixelSize(5, 5),
            new[] { new DrawingColorGroup(RgbColor.Black, new[] { stroke }) });

        var preview = DrawingPlanPostProcessor.RenderPreview(plan);

        Assert.Equal(RgbColor.Black, preview[0, 0].Color);
        Assert.Equal(RgbColor.Black, preview[2, 2].Color);
        Assert.Equal(RgbColor.Black, preview[4, 4].Color);
        Assert.Equal(RgbColor.White, preview[4, 0].Color);
    }

    [Fact]
    public void FacePriorityMovesIntersectingStrokesBeforeOuterForm()
    {
        var outer = new DrawingStroke(new[]
        {
            new GameDraw.Core.Geometry.NormalizedPoint(0.05, 0.8),
            new GameDraw.Core.Geometry.NormalizedPoint(0.95, 0.8)
        });
        var eye = new DrawingStroke(new[]
        {
            new GameDraw.Core.Geometry.NormalizedPoint(0.4, 0.25),
            new GameDraw.Core.Geometry.NormalizedPoint(0.6, 0.25)
        });
        var plan = new DrawingPlan(
            DrawingMode.CleanStroke,
            new GameDraw.Core.Geometry.PixelSize(100, 100),
            new[] { new DrawingColorGroup(RgbColor.Black, new[] { outer, eye }) });

        var result = DrawingPlanPostProcessor.PrioritizeRegion(
            plan,
            new GameDraw.Core.Geometry.NormalizedRect(0.25, 0.1, 0.5, 0.4));

        Assert.Same(eye, result.ColorGroups[0].Strokes[0]);
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
