using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Models;
using GameDraw.Imaging.Decoding;
using GameDraw.Imaging.Palettes;
using GameDraw.Imaging.Processing;
using GameDraw.Imaging.Quantization;
using GameDraw.Imaging.Resampling;
using GameDraw.Planning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using CoreImageFrame = GameDraw.Core.Imaging.ImageFrame;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: GameDraw.PreviewProbe <image> <output-directory>");
    return 2;
}

var decoded = await new ImageDecoder().DecodeFileAsync(args[0]);
var subject = SubjectFocusProcessor.Process(decoded.Frame);
var target = new PixelSize(416, 416);
var resized = Letterbox(subject.Frame, target);
var lineArt = LineArtProcessor.Extract(resized, new LineArtOptions
{
    EdgeThreshold = 12d,
    WeakEdgeRatio = 0.2d,
    AdaptivePercentile = 0.56d,
    MinimumComponentPixels = 2
});
var palette = new ColorPalette(new[] { RgbColor.Black }, "probe");
var quantized = new PaletteQuantizer().Quantize(lineArt, palette, new QuantizationOptions
{
    DitherMode = DitherMode.None,
    PreserveAlpha = true
});
var planning = new DrawingPlanner().Plan(quantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.ArtistStroke,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 38
});
var plan = DrawingPlanPostProcessor.OrderArtistically(planning.Plan, subject.FacePriorityRegion);
var preview = DrawingPlanPostProcessor.RenderPreview(plan, 2);
var centerlinePlan = new DrawingPlanner().Plan(quantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.CleanStroke,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 38
}).Plan;
var filledPlan = new DrawingPlanner().Plan(quantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.HorizontalScanline,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 38
}).Plan;
var filledPreview = DrawingPlanPostProcessor.RenderPreview(filledPlan);
var hybridPlan = new DrawingPlanner().Plan(quantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.Hybrid,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 38
}).Plan;
var hybridPreview = DrawingPlanPostProcessor.RenderPreview(hybridPlan);
var safeTarget = new PixelSize(320, 320);
var safeSource = Letterbox(subject.Frame, safeTarget);
var safeLineArt = LineArtProcessor.Extract(safeSource, new LineArtOptions
{
    EdgeThreshold = 12d,
    WeakEdgeRatio = 0.2d,
    AdaptivePercentile = 0.56d,
    MinimumComponentPixels = 2
});
var safeQuantized = new PaletteQuantizer().Quantize(safeLineArt, palette, new QuantizationOptions
{
    DitherMode = DitherMode.None,
    PreserveAlpha = true
});
var safePlan = new DrawingPlanner().Plan(safeQuantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.SafeStamp,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 35
}).Plan;
var safePreview = DrawingPlanPostProcessor.RenderPreview(safePlan, 2);
var halftoneTarget = new PixelSize(320, 320);
var halftone = PhotoHalftoneProcessor.Process(safeSource);
var halftoneQuantized = new PaletteQuantizer().Quantize(halftone, palette, new QuantizationOptions
{
    DitherMode = DitherMode.None,
    PreserveAlpha = true
});
var halftonePlan = new DrawingPlanner().Plan(halftoneQuantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.HalftoneStamp,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 28
}).Plan;
var halftonePreview = DrawingPlanPostProcessor.RenderPreview(halftonePlan);
var colorResult = new ImageProcessingPipeline().ProcessFrame(safeSource, new ImageProcessingOptions
{
    TargetSize = null,
    Palette = new PaletteBuildOptions { MaxColors = 10 },
    Quantization = new QuantizationOptions { DitherMode = DitherMode.None, PreserveAlpha = true }
});
var artistLineArt = ArtistLineArtProcessor.Process(resized, new ArtistLineArtOptions
{
    EdgeThreshold = 9d,
    AdaptivePercentile = 0.49d,
    WeakEdgeRatio = 0.18d,
    MinimumComponentPixels = 2
});
var artistQuantized = new PaletteQuantizer().Quantize(artistLineArt, palette, new QuantizationOptions
{
    DitherMode = DitherMode.None,
    PreserveAlpha = true
});
var artistPlan = new DrawingPlanner().Plan(artistQuantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.ArtistStroke,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 38
}).Plan;
artistPlan = DrawingPlanPostProcessor.OrderArtistically(artistPlan, subject.FacePriorityRegion);
var artistPreview = DrawingPlanPostProcessor.RenderPreview(artistPlan, 2);
var smartFillPlan = new DrawingPlanner().Plan(colorResult.Quantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.SmartFill,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 38,
    BrushDiameterPixels = 2,
    OrderStrokesByTravel = false,
    OrderColorGroupsByTravel = false,
    PriorityRegion = subject.FacePriorityRegion
}).Plan;
var smartFillPreview = DrawingPlanPostProcessor.RenderPreview(smartFillPlan, 2);
var fullPaletteResult = new ImageProcessingPipeline().ProcessFrame(safeSource, new ImageProcessingOptions
{
    TargetSize = null,
    Palette = new PaletteBuildOptions { MaxColors = 256 },
    Quantization = new QuantizationOptions { DitherMode = DitherMode.None, PreserveAlpha = true }
});
var fullPalettePlan = new DrawingPlanner().Plan(fullPaletteResult.Quantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.SafeStamp,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 28,
    BrushDiameterPixels = 2
}).Plan;
var fullPalettePreview = DrawingPlanPostProcessor.RenderPreview(fullPalettePlan, 2);
var acceleratedPaletteResult = new ImageProcessingPipeline().ProcessFrame(safeSource, new ImageProcessingOptions
{
    TargetSize = null,
    Palette = new PaletteBuildOptions { MaxColors = 128 },
    Quantization = new QuantizationOptions { DitherMode = DitherMode.None, PreserveAlpha = true }
});
var acceleratedPalettePlan = new DrawingPlanner().Plan(acceleratedPaletteResult.Quantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.SafeStamp,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 28,
    BrushDiameterPixels = 2,
    OrderColorGroupsByTravel = true
}).Plan;
var acceleratedPalettePreview = DrawingPlanPostProcessor.RenderPreview(acceleratedPalettePlan, 2);
// Match the current calibrated Podiums profile (499x512 logical canvas,
// measured 2.75px brush) so real grayscale reports can be compared without
// launching the UI. The brush-aware analysis size is 209x214.
var podiumsGraySource = Letterbox(subject.Frame, new PixelSize(209, 214));
var podiumsGray = GrayscalePhotoProcessor.Process(podiumsGraySource);
var podiumsShades = Enumerable.Range(0, 16)
    .Select(index =>
    {
        var value = (byte)Math.Round(index * 255d / 15d);
        return new RgbColor(value, value, value);
    })
    .ToArray();
var podiumsGrayQuantized = new PaletteQuantizer().Quantize(
    podiumsGray,
    new ColorPalette(podiumsShades, "podiums-gray-16"),
    new QuantizationOptions { DitherMode = DitherMode.None, PreserveAlpha = true });
var podiumsSafePlan = new DrawingPlanner().Plan(podiumsGrayQuantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.SafeStamp,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 28,
    BrushDiameterPixels = 1
}).Plan;
var podiumsScanPlan = new DrawingPlanner().Plan(podiumsGrayQuantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.HorizontalScanline,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 28,
    BrushDiameterPixels = 1
}).Plan;
var podiumsScanPreview = DrawingPlanPostProcessor.RenderPreview(podiumsScanPlan);

Directory.CreateDirectory(args[1]);
await SaveAsync(resized, Path.Combine(args[1], "01-subject.png"));
await SaveAsync(lineArt, Path.Combine(args[1], "02-line-art.png"));
await SaveAsync(preview, Path.Combine(args[1], "03-execution-path.png"));
await SaveAsync(filledPreview, Path.Combine(args[1], "04-ink-preserving-path.png"));
await SaveAsync(hybridPreview, Path.Combine(args[1], "05-hybrid-path.png"));
await SaveAsync(safeLineArt, Path.Combine(args[1], "06-safe-source.png"));
await SaveAsync(safePreview, Path.Combine(args[1], "07-safe-stamp-path.png"));
await SaveAsync(halftonePreview, Path.Combine(args[1], "08-photo-halftone.png"));
await SaveAsync(colorResult.Quantized.Rendered, Path.Combine(args[1], "09-color-16.png"));
await SaveAsync(artistLineArt, Path.Combine(args[1], "10-artist-line-source.png"));
await SaveAsync(artistPreview, Path.Combine(args[1], "11-artist-line-path.png"));
await SaveAsync(smartFillPreview, Path.Combine(args[1], "12-smart-outline-fill.png"));
await SaveAsync(fullPalettePreview, Path.Combine(args[1], "13-full-palette-256.png"));
await SaveAsync(acceleratedPalettePreview, Path.Combine(args[1], "14-ai-fast-palette-128.png"));
await SaveAsync(podiumsScanPreview, Path.Combine(args[1], "15-podiums-gray-scan.png"));
Console.WriteLine($"source={decoded.Frame.Width}x{decoded.Frame.Height}");
Console.WriteLine($"subject={subject.Frame.Width}x{subject.Frame.Height} backgroundRemoved={subject.BackgroundRemoved} cropped={subject.Cropped} person={subject.PersonLikely}");
Console.WriteLine($"working={target.Width}x{target.Height} opaqueEdges={lineArt.Pixels.Count(pixel => pixel.IsOpaque)}");
Console.WriteLine($"strokes={plan.Statistics.StrokeCount} points={plan.Statistics.PointCount}");
Console.WriteLine($"centerlineStrokes={centerlinePlan.Statistics.StrokeCount} points={centerlinePlan.Statistics.PointCount}");
Console.WriteLine($"inkStrokes={filledPlan.Statistics.StrokeCount} points={filledPlan.Statistics.PointCount}");
Console.WriteLine($"hybridStrokes={hybridPlan.Statistics.StrokeCount} points={hybridPlan.Statistics.PointCount}");
Console.WriteLine($"safeWorking={safeTarget.Width}x{safeTarget.Height} safeStrokes={safePlan.Statistics.StrokeCount} safePoints={safePlan.Statistics.PointCount} maxChunk={safePlan.EnumerateStrokes().Max(item => item.Stroke.Points.Count)}");
Console.WriteLine($"halftoneWorking={halftoneTarget.Width}x{halftoneTarget.Height} halftoneDots={halftonePlan.Statistics.StrokeCount}");
Console.WriteLine($"artistStrokes={artistPlan.Statistics.StrokeCount} artistPoints={artistPlan.Statistics.PointCount} artistInk={artistLineArt.Pixels.Count(pixel => pixel.IsOpaque)}");
Console.WriteLine($"smartColorStrokes={smartFillPlan.Statistics.StrokeCount} fillClicks={smartFillPlan.EnumerateStrokes().Count(item => item.Stroke.ToolAction == GameDraw.Core.Drawing.DrawingToolAction.Fill)} silhouetteStrokes={smartFillPlan.EnumerateStrokes().Count(item => item.Stroke.IsClosed)} stampStrokes={smartFillPlan.EnumerateStrokes().Count(item => !item.Stroke.IsClosed)}");
Console.WriteLine($"fullPaletteColors={fullPalettePlan.ColorGroups.Select(group => group.Color).Distinct().Count()} fullPaletteStrokes={fullPalettePlan.Statistics.StrokeCount} fullPalettePoints={fullPalettePlan.Statistics.PointCount}");
Console.WriteLine($"acceleratedPaletteColors={acceleratedPalettePlan.ColorGroups.Select(group => group.Color).Distinct().Count()} acceleratedPaletteStrokes={acceleratedPalettePlan.Statistics.StrokeCount} acceleratedPalettePoints={acceleratedPalettePlan.Statistics.PointCount}");
Console.WriteLine($"podiumsGraySafeStrokes={podiumsSafePlan.Statistics.StrokeCount} points={podiumsSafePlan.Statistics.PointCount}");
Console.WriteLine($"podiumsGrayScanStrokes={podiumsScanPlan.Statistics.StrokeCount} points={podiumsScanPlan.Statistics.PointCount}");
var sourceInk = lineArt.Pixels.Select(pixel => pixel.IsOpaque).ToArray();
var previewInk = preview.Pixels.Select(pixel => pixel.Color == RgbColor.Black).ToArray();
var intersection = sourceInk.Zip(previewInk).Count(pair => pair.First && pair.Second);
var sourceInkCount = sourceInk.Count(value => value);
var previewInkCount = previewInk.Count(value => value);
Console.WriteLine($"inkRecall={intersection / (double)Math.Max(1, sourceInkCount):P1} inkPrecision={intersection / (double)Math.Max(1, previewInkCount):P1}");
var safeSourceInk = safeLineArt.Pixels.Select(pixel => pixel.IsOpaque).ToArray();
var safePreviewInk = safePreview.Pixels.Select(pixel => pixel.Color == RgbColor.Black).ToArray();
var safeIntersection = safeSourceInk.Zip(safePreviewInk).Count(pair => pair.First && pair.Second);
Console.WriteLine($"safeInkRecall={safeIntersection / (double)Math.Max(1, safeSourceInk.Count(value => value)):P1} safeInkPrecision={safeIntersection / (double)Math.Max(1, safePreviewInk.Count(value => value)):P1}");
return 0;

static PixelSize FitWithin(PixelSize source, PixelSize bounds)
{
    var scale = Math.Min(1d, Math.Min(bounds.Width / (double)source.Width, bounds.Height / (double)source.Height));
    return new PixelSize(
        Math.Max(1, (int)Math.Round(source.Width * scale)),
        Math.Max(1, (int)Math.Round(source.Height * scale)));
}

static CoreImageFrame Letterbox(CoreImageFrame source, PixelSize canvas)
{
    var contentSize = FitWithin(new PixelSize(source.Width, source.Height), canvas);
    var resized = ImageResampler.Resize(source, contentSize);
    var pixels = Enumerable.Repeat(
        RgbaPixel.Transparent,
        checked(canvas.Width * canvas.Height)).ToArray();
    var offsetX = (canvas.Width - contentSize.Width) / 2;
    var offsetY = (canvas.Height - contentSize.Height) / 2;
    for (var y = 0; y < contentSize.Height; y++)
    {
        for (var x = 0; x < contentSize.Width; x++)
        {
            pixels[((y + offsetY) * canvas.Width) + x + offsetX] = resized[x, y];
        }
    }

    return new CoreImageFrame(canvas.Width, canvas.Height, pixels);
}

static async Task SaveAsync(CoreImageFrame frame, string path)
{
    using var output = new Image<Rgba32>(frame.Width, frame.Height);
    for (var y = 0; y < frame.Height; y++)
    {
        for (var x = 0; x < frame.Width; x++)
        {
            var pixel = frame[x, y];
            var alpha = pixel.Alpha / 255f;
            output[x, y] = new Rgba32(
                (byte)Math.Round((pixel.Color.R * alpha) + (255 * (1 - alpha))),
                (byte)Math.Round((pixel.Color.G * alpha) + (255 * (1 - alpha))),
                (byte)Math.Round((pixel.Color.B * alpha) + (255 * (1 - alpha))),
                255);
        }
    }

    await output.SaveAsPngAsync(path);
}
