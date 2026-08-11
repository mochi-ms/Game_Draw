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
var target = FitWithin(new PixelSize(subject.Frame.Width, subject.Frame.Height), new PixelSize(416, 416));
var resized = ImageResampler.Resize(subject.Frame, target);
var lineArt = NaturalLineArtProcessor.Extract(resized);
var palette = new ColorPalette(new[] { RgbColor.Black }, "probe");
var quantized = new PaletteQuantizer().Quantize(lineArt, palette, new QuantizationOptions
{
    DitherMode = DitherMode.None,
    PreserveAlpha = true
});
var planning = new DrawingPlanner().Plan(quantized, new DrawingPlannerOptions
{
    Mode = DrawingMode.CleanStroke,
    MovementPixelsPerSecond = 4_000,
    PerStrokeSafetyDelayMilliseconds = 38
});
var plan = subject.FacePriorityRegion is { } face
    ? DrawingPlanPostProcessor.PrioritizeRegion(planning.Plan, face)
    : planning.Plan;
var preview = DrawingPlanPostProcessor.RenderPreview(plan, 2);
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

Directory.CreateDirectory(args[1]);
await SaveAsync(resized, Path.Combine(args[1], "01-subject.png"));
await SaveAsync(lineArt, Path.Combine(args[1], "02-line-art.png"));
await SaveAsync(preview, Path.Combine(args[1], "03-execution-path.png"));
await SaveAsync(filledPreview, Path.Combine(args[1], "04-ink-preserving-path.png"));
await SaveAsync(hybridPreview, Path.Combine(args[1], "05-hybrid-path.png"));
Console.WriteLine($"source={decoded.Frame.Width}x{decoded.Frame.Height}");
Console.WriteLine($"subject={subject.Frame.Width}x{subject.Frame.Height} backgroundRemoved={subject.BackgroundRemoved} cropped={subject.Cropped} person={subject.PersonLikely}");
Console.WriteLine($"working={target.Width}x{target.Height} opaqueEdges={lineArt.Pixels.Count(pixel => pixel.IsOpaque)}");
Console.WriteLine($"strokes={plan.Statistics.StrokeCount} points={plan.Statistics.PointCount}");
Console.WriteLine($"inkStrokes={filledPlan.Statistics.StrokeCount} points={filledPlan.Statistics.PointCount}");
Console.WriteLine($"hybridStrokes={hybridPlan.Statistics.StrokeCount} points={hybridPlan.Statistics.PointCount}");
return 0;

static PixelSize FitWithin(PixelSize source, PixelSize bounds)
{
    var scale = Math.Min(1d, Math.Min(bounds.Width / (double)source.Width, bounds.Height / (double)source.Height));
    return new PixelSize(
        Math.Max(1, (int)Math.Round(source.Width * scale)),
        Math.Max(1, (int)Math.Round(source.Height * scale)));
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
