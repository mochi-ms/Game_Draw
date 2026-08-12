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
    public void TwoPixelPreviewBrushIsExactlyTwoPixelsWide()
    {
        var stroke = new DrawingStroke(new[]
        {
            new GameDraw.Core.Geometry.NormalizedPoint(0.5, 0.5)
        });
        var plan = new DrawingPlan(
            DrawingMode.ArtistStroke,
            new GameDraw.Core.Geometry.PixelSize(5, 5),
            new[] { new DrawingColorGroup(RgbColor.Black, new[] { stroke }) });

        var preview = DrawingPlanPostProcessor.RenderPreview(plan, 2);

        Assert.Equal(4, preview.Pixels.Count(pixel => pixel.Color == RgbColor.Black));
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

    [Fact]
    public void PrinterOrderingSweepsTopToBottomAndAlternatesHorizontalDirection()
    {
        static DrawingStroke Point(double x, double y) =>
            new(new[] { new GameDraw.Core.Geometry.NormalizedPoint(x, y) });

        var plan = new DrawingPlan(
            DrawingMode.SafeStamp,
            new GameDraw.Core.Geometry.PixelSize(100, 100),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    Point(0.9, 0.9),
                    Point(0.8, 0.1),
                    Point(0.2, 0.9),
                    Point(0.1, 0.1)
                })
            });

        var result = DrawingPlanPostProcessor.OrderForPrinterTravel(plan);
        var starts = result.ColorGroups[0].Strokes.Select(stroke => stroke.Points[0]).ToArray();

        Assert.Equal(new GameDraw.Core.Geometry.NormalizedPoint(0.1, 0.1), starts[0]);
        Assert.Equal(new GameDraw.Core.Geometry.NormalizedPoint(0.8, 0.1), starts[1]);
        Assert.Equal(new GameDraw.Core.Geometry.NormalizedPoint(0.9, 0.9), starts[2]);
        Assert.Equal(new GameDraw.Core.Geometry.NormalizedPoint(0.2, 0.9), starts[3]);
    }

    [Fact]
    public void CoverageOrderingDrawsDominantColorBeforeSmallAccents()
    {
        var accent = new DrawingColorGroup(
            new RgbColor(255, 0, 0),
            new[] { new DrawingStroke(new[] { new GameDraw.Core.Geometry.NormalizedPoint(0.1, 0.1) }) });
        var dominant = new DrawingColorGroup(
            new RgbColor(0, 0, 255),
            new[]
            {
                new DrawingStroke(new[]
                {
                    new GameDraw.Core.Geometry.NormalizedPoint(0.1, 0.5),
                    new GameDraw.Core.Geometry.NormalizedPoint(0.9, 0.5)
                })
            });
        var plan = new DrawingPlan(
            DrawingMode.SafeStamp,
            new GameDraw.Core.Geometry.PixelSize(100, 100),
            new[] { accent, dominant });

        var result = DrawingPlanPostProcessor.OrderColorsByCoverage(plan);

        Assert.Equal(dominant.Color, result.ColorGroups[0].Color);
        Assert.Equal(accent.Color, result.ColorGroups[1].Color);
    }

    [Fact]
    public void CoverageOrderingCanKeepSmartFillUnderdrawingFirst()
    {
        var underdrawing = new DrawingColorGroup(
            RgbColor.Black,
            new[] { new DrawingStroke(new[] { new GameDraw.Core.Geometry.NormalizedPoint(0.5, 0.5) }) });
        var smallFill = new DrawingColorGroup(
            new RgbColor(255, 0, 0),
            new[] { new DrawingStroke(new[] { new GameDraw.Core.Geometry.NormalizedPoint(0.2, 0.2) }) });
        var largeFill = new DrawingColorGroup(
            new RgbColor(0, 0, 255),
            new[]
            {
                new DrawingStroke(new[]
                {
                    new GameDraw.Core.Geometry.NormalizedPoint(0.1, 0.7),
                    new GameDraw.Core.Geometry.NormalizedPoint(0.9, 0.7)
                })
            });
        var plan = new DrawingPlan(
            DrawingMode.SmartFill,
            new GameDraw.Core.Geometry.PixelSize(100, 100),
            new[] { underdrawing, smallFill, largeFill });

        var result = DrawingPlanPostProcessor.OrderColorsByCoverage(plan, preserveFirstGroup: true);

        Assert.Equal(underdrawing.Color, result.ColorGroups[0].Color);
        Assert.Equal(largeFill.Color, result.ColorGroups[1].Color);
        Assert.Equal(smallFill.Color, result.ColorGroups[2].Color);
    }

    [Fact]
    public void ArtistOrderDrawsLargeOuterFormBeforeFacialDetails()
    {
        var outer = new DrawingStroke(new[]
        {
            new GameDraw.Core.Geometry.NormalizedPoint(0.1, 0.1),
            new GameDraw.Core.Geometry.NormalizedPoint(0.9, 0.1),
            new GameDraw.Core.Geometry.NormalizedPoint(0.9, 0.9),
            new GameDraw.Core.Geometry.NormalizedPoint(0.1, 0.9)
        }, isClosed: true);
        var eye = new DrawingStroke(new[]
        {
            new GameDraw.Core.Geometry.NormalizedPoint(0.4, 0.35),
            new GameDraw.Core.Geometry.NormalizedPoint(0.6, 0.35)
        });
        var hairDetail = new DrawingStroke(new[]
        {
            new GameDraw.Core.Geometry.NormalizedPoint(0.45, 0.12),
            new GameDraw.Core.Geometry.NormalizedPoint(0.5, 0.22)
        });
        var plan = new DrawingPlan(
            DrawingMode.ArtistStroke,
            new GameDraw.Core.Geometry.PixelSize(100, 100),
            new[] { new DrawingColorGroup(RgbColor.Black, new[] { hairDetail, eye, outer }) });

        var result = DrawingPlanPostProcessor.OrderArtistically(
            plan,
            new GameDraw.Core.Geometry.NormalizedRect(0.25, 0.2, 0.5, 0.4));

        Assert.Same(outer, result.ColorGroups[0].Strokes[0]);
        Assert.Same(eye, result.ColorGroups[0].Strokes[1]);
        Assert.Same(hairDetail, result.ColorGroups[0].Strokes[2]);
    }

    [Fact]
    public void ArtistStrokeModePreservesHeavyInkInsteadOfCollapsingItToOneLine()
    {
        const int width = 18;
        const int height = 9;
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        for (var y = 2; y <= 6; y++)
        {
            for (var x = 2; x <= 15; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }

        var image = new PaletteQuantizer().Quantize(
            new ImageFrame(width, height, pixels),
            new ColorPalette(new[] { RgbColor.Black }),
            new QuantizationOptions { PreserveAlpha = true });
        var result = new DrawingPlanner().Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.ArtistStroke
        });

        Assert.Equal(DrawingMode.ArtistStroke, result.SelectedMode);
        var preview = DrawingPlanPostProcessor.RenderPreview(result.Plan, 2);
        for (var y = 2; y <= 6; y++)
        {
            for (var x = 2; x <= 15; x++)
            {
                Assert.Equal(RgbColor.Black, preview[x, y].Color);
            }
        }

        Assert.True(result.Estimate.StrokeCount < 8);
    }

    [Fact]
    public void ArtistStrokeNeverConnectsSeparateHeavyInkRegions()
    {
        const int width = 32;
        const int height = 12;
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        for (var y = 2; y <= 9; y++)
        {
            for (var x = 2; x <= 10; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }

            for (var x = 21; x <= 29; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }

        var image = new PaletteQuantizer().Quantize(
            new ImageFrame(width, height, pixels),
            new ColorPalette(new[] { RgbColor.Black }),
            new QuantizationOptions { PreserveAlpha = true });
        var result = new DrawingPlanner().Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.ArtistStroke
        });
        var preview = DrawingPlanPostProcessor.RenderPreview(result.Plan, 2);

        for (var y = 0; y < height; y++)
        {
            for (var x = 13; x <= 18; x++)
            {
                Assert.Equal(RgbColor.White, preview[x, y].Color);
            }
        }
    }

    [Fact]
    public void SafeStampWalksOneConnectedInkRegionWithoutPenUpTravel()
    {
        const int width = 24;
        const int height = 5;
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        for (var x = 1; x < width - 1; x++)
        {
            pixels[(2 * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
        }

        var result = PlanTransparent(pixels, width, height, DrawingMode.SafeStamp);

        Assert.Equal(DrawingMode.SafeStamp, result.SelectedMode);
        Assert.Equal(1, result.Estimate.StrokeCount);
        var preview = DrawingPlanPostProcessor.RenderPreview(result.Plan, 2);
        for (var x = 1; x < width - 1; x++)
        {
            Assert.Equal(RgbColor.Black, preview[x, 2].Color);
        }
    }

    [Fact]
    public void SafeStampNeverDrawsAcrossSeparatedInkRegions()
    {
        const int width = 36;
        const int height = 12;
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        for (var y = 2; y <= 9; y++)
        {
            for (var x = 2; x <= 10; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }

            for (var x = 25; x <= 33; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }

        var result = PlanTransparent(pixels, width, height, DrawingMode.SafeStamp);
        var preview = DrawingPlanPostProcessor.RenderPreview(result.Plan, 2);

        Assert.DoesNotContain(
            result.Plan.EnumerateStrokes(),
            item => item.Stroke.Points.Min(point => point.X) < 0.4d &&
                    item.Stroke.Points.Max(point => point.X) > 0.6d);
        for (var y = 0; y < height; y++)
        {
            for (var x = 14; x <= 21; x++)
            {
                Assert.Equal(RgbColor.White, preview[x, y].Color);
            }
        }
    }

    [Fact]
    public void SafeStampTwoPixelBrushCoversEverySourceInkPixel()
    {
        const int width = 17;
        const int height = 11;
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        for (var y = 2; y <= 8; y++)
        {
            for (var x = 3; x <= 13; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }

        var result = PlanTransparent(pixels, width, height, DrawingMode.SafeStamp);
        var preview = DrawingPlanPostProcessor.RenderPreview(result.Plan, 2);

        for (var y = 2; y <= 8; y++)
        {
            for (var x = 3; x <= 13; x++)
            {
                Assert.Equal(RgbColor.Black, preview[x, y].Color);
            }
        }
    }

    [Fact]
    public void SafeStampUsesMeasuredBrushDiameterToReduceClicksAndKeepCoverage()
    {
        const int width = 18;
        const int height = 12;
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        for (var y = 2; y <= 9; y++)
        {
            for (var x = 2; x <= 15; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }

        var image = new PaletteQuantizer().Quantize(
            new ImageFrame(width, height, pixels),
            new ColorPalette(new[] { RgbColor.Black }),
            new QuantizationOptions { PreserveAlpha = true });
        var planner = new DrawingPlanner();
        var twoPixel = planner.Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.SafeStamp,
            BrushDiameterPixels = 2
        });
        var fourPixel = planner.Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.SafeStamp,
            BrushDiameterPixels = 4
        });
        var preview = DrawingPlanPostProcessor.RenderPreview(fourPixel.Plan, 4);

        Assert.NotEmpty(twoPixel.Plan.EnumerateStrokes());
        Assert.NotEmpty(fourPixel.Plan.EnumerateStrokes());
        for (var y = 2; y <= 9; y++)
        {
            for (var x = 2; x <= 15; x++)
            {
                Assert.Equal(RgbColor.Black, preview[x, y].Color);
            }
        }
    }

    [Fact]
    public void HalftoneStampPreservesEveryIndependentDot()
    {
        const int width = 8;
        const int height = 5;
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        pixels[(1 * width) + 1] = RgbaPixel.Opaque(RgbColor.Black);
        pixels[(2 * width) + 4] = RgbaPixel.Opaque(RgbColor.Black);
        pixels[(3 * width) + 6] = RgbaPixel.Opaque(RgbColor.Black);

        var result = PlanTransparent(pixels, width, height, DrawingMode.HalftoneStamp);

        Assert.Equal(DrawingMode.HalftoneStamp, result.SelectedMode);
        Assert.Equal(3, result.Plan.Statistics.StrokeCount);
        Assert.All(result.Plan.EnumerateStrokes(), item => Assert.Single(item.Stroke.Points));
    }

    [Fact]
    public void SmartFillDrawsOnlySubjectSilhouetteThenCompletesImageWithSafeColorStamps()
    {
        const int width = 12;
        const int height = 12;
        var transparent = RgbaPixel.Transparent;
        var red = new RgbColor(220, 80, 80);
        var blue = new RgbColor(70, 100, 210);
        var pixels = Enumerable.Repeat(transparent, width * height).ToArray();
        for (var y = 2; y <= 9; y++)
        {
            for (var x = 2; x <= 9; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(red);
            }
        }

        for (var y = 4; y <= 7; y++)
        {
            for (var x = 4; x <= 7; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(blue);
            }
        }

        var image = new PaletteQuantizer().Quantize(
            new ImageFrame(width, height, pixels),
            new ColorPalette(new[] { red, blue }),
            new QuantizationOptions { PreserveAlpha = true });
        var result = new DrawingPlanner().Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.SmartFill,
            BrushDiameterPixels = 1,
            OrderStrokesByTravel = false
        });
        var preview = DrawingPlanPostProcessor.RenderPreview(result.Plan);

        Assert.Equal(DrawingMode.SmartFill, result.SelectedMode);
        Assert.All(result.Plan.ColorGroups[0].Strokes, stroke =>
        {
            Assert.True(stroke.IsClosed);
            Assert.Equal(DrawingToolAction.Pencil, stroke.ToolAction);
        });
        Assert.All(result.Plan.ColorGroups.Skip(1).SelectMany(group => group.Strokes), stroke =>
        {
            Assert.Equal(DrawingToolAction.Pencil, stroke.ToolAction);
            Assert.NotEmpty(stroke.Points);
        });
        Assert.DoesNotContain(result.Plan.EnumerateStrokes(), item =>
            item.Stroke.ToolAction == DrawingToolAction.Fill);
        Assert.Equal(red, preview[3, 3].Color);
        Assert.Equal(blue, preview[5, 5].Color);
        Assert.NotEmpty(result.Plan.ColorGroups[0].Strokes);
        for (var y = 2; y <= 9; y++)
        {
            for (var x = 2; x <= 9; x++)
            {
                var expected = x is >= 4 and <= 7 && y is >= 4 and <= 7
                    ? blue
                    : red;
                Assert.Equal(expected, preview[x, y].Color);
            }
        }
    }

    [Fact]
    public void SmartFillNeverBucketsARegionTouchingCanvasEdge()
    {
        const int width = 10;
        const int height = 10;
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        for (var y = 2; y <= 7; y++)
        {
            for (var x = 0; x <= 5; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(RgbColor.Black);
            }
        }

        var result = PlanTransparent(pixels, width, height, DrawingMode.SmartFill);

        Assert.DoesNotContain(result.Plan.EnumerateStrokes(), item =>
            item.Stroke.ToolAction == DrawingToolAction.Fill);
        Assert.Contains(result.Plan.EnumerateStrokes(), item =>
            item.Stroke.ToolAction == DrawingToolAction.Pencil);
    }

    [Fact]
    public void SmartFillMergesOnlyIsolatedColorNoiseAndKeepsRealDetails()
    {
        const int width = 10;
        const int height = 10;
        var red = new RgbColor(220, 70, 70);
        var blue = new RgbColor(55, 90, 220);
        var pixels = Enumerable.Repeat(RgbaPixel.Transparent, width * height).ToArray();
        for (var y = 1; y <= 8; y++)
        {
            for (var x = 1; x <= 8; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(red);
            }
        }

        pixels[(2 * width) + 2] = RgbaPixel.Opaque(blue); // isolated noise
        pixels[(6 * width) + 6] = RgbaPixel.Opaque(blue); // real 2px detail
        pixels[(6 * width) + 7] = RgbaPixel.Opaque(blue);
        var image = new PaletteQuantizer().Quantize(
            new ImageFrame(width, height, pixels),
            new ColorPalette(new[] { red, blue }),
            new QuantizationOptions { PreserveAlpha = true });

        var result = new DrawingPlanner().Plan(image, new DrawingPlannerOptions
        {
            Mode = DrawingMode.SmartFill,
            BrushDiameterPixels = 1,
            OrderStrokesByTravel = false
        });
        var preview = DrawingPlanPostProcessor.RenderPreview(result.Plan);

        Assert.Equal(red, preview[2, 2].Color);
        Assert.Equal(blue, preview[6, 6].Color);
        Assert.Equal(blue, preview[7, 6].Color);
    }

    private static DrawingPlanningResult PlanTransparent(
        IReadOnlyList<RgbaPixel> pixels,
        int width,
        int height,
        DrawingMode mode)
    {
        var image = new PaletteQuantizer().Quantize(
            new ImageFrame(width, height, pixels),
            new ColorPalette(new[] { RgbColor.Black }),
            new QuantizationOptions { PreserveAlpha = true });
        return new DrawingPlanner().Plan(image, new DrawingPlannerOptions { Mode = mode });
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
