using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Vision;

namespace GameDraw.GameAdapters.Podiums.Vision;

public sealed record PodiumsVisualDetectionOptions
{
    public byte MinimumCanvasChannel { get; init; } = 225;

    public byte MaximumCanvasChannelSpread { get; init; } = 24;

    public double MinimumCanvasAreaFraction { get; init; } = 0.10d;

    public double MinimumCanvasFillRatio { get; init; } = 0.55d;

    public double MinimumCanvasConfidence { get; init; } = 0.80d;

    public VisionMatchOptions AnchorMatching { get; init; } = new();

    public void Validate()
    {
        if (MinimumCanvasChannel == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumCanvasChannel));
        }

        if (MaximumCanvasChannelSpread > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCanvasChannelSpread));
        }

        if (!double.IsFinite(MinimumCanvasAreaFraction) || MinimumCanvasAreaFraction is <= 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumCanvasAreaFraction));
        }

        if (!double.IsFinite(MinimumCanvasFillRatio) || MinimumCanvasFillRatio is <= 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumCanvasFillRatio));
        }

        if (!double.IsFinite(MinimumCanvasConfidence) || MinimumCanvasConfidence is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumCanvasConfidence));
        }

        AnchorMatching.Validate();
    }
}
public sealed record PodiumsVisualTemplates
{
    public VisualAnchorDefinition? PencilTool { get; init; }

    public VisualAnchorDefinition? BrushTool { get; init; }

    public VisualAnchorDefinition? FillTool { get; init; }

    public VisualAnchorDefinition? HexInput { get; init; }

    public VisualAnchorDefinition? HexApply { get; init; }

    public IReadOnlyList<VisualAnchorDefinition> RequiredAnchors
        => new[] { PencilTool, BrushTool, HexInput, HexApply }
            .Where(anchor => anchor is not null)
            .Cast<VisualAnchorDefinition>()
            .ToArray();

    public IReadOnlyList<VisualAnchorDefinition> OptionalAnchors
        => new[] { FillTool }
            .Where(anchor => anchor is not null)
            .Cast<VisualAnchorDefinition>()
            .ToArray();
}

public sealed record PodiumsCanvasDetection(
    bool IsMatch,
    double Confidence,
    PixelRect Bounds,
    NormalizedRect NormalizedBounds,
    string? Reason = null)
{
    public static PodiumsCanvasDetection NoMatch(string reason)
        => new(false, 0d, default, default, reason);
}

public sealed record PodiumsVisualDetectionResult(
    PixelSize FrameSize,
    PodiumsCanvasDetection Canvas,
    AnchorDetectionResult RequiredAnchors,
    AnchorDetectionResult OptionalAnchors)
{
    public bool IsSafeToContinue => Canvas.IsMatch && RequiredAnchors.IsSafeToContinue;

    public IReadOnlyDictionary<string, NormalizedPoint> AnchorCenters
        => RequiredAnchors.Matches
            .Concat(OptionalAnchors.Matches)
            .Where(result => result.Match.Bounds.IsValid)
            .ToDictionary(
                result => result.Id,
                result => result.Match.NormalizedBounds.Center,
                StringComparer.OrdinalIgnoreCase);

    public VisualObservation ToObservation()
        => new(FrameSize, Canvas.NormalizedBounds, Canvas.Confidence, AnchorCenters);
}

public sealed class PodiumsVisualDetector
{
    private readonly AnchorMatcher _anchorMatcher;

    public PodiumsVisualDetector(AnchorMatcher? anchorMatcher = null)
    {
        _anchorMatcher = anchorMatcher ?? new AnchorMatcher();
    }

    public PodiumsVisualDetectionResult Detect(
        ImageFrame frame,
        PodiumsVisualTemplates? templates = null,
        PodiumsVisualDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        options ??= new PodiumsVisualDetectionOptions();
        options.Validate();
        templates ??= new PodiumsVisualTemplates();

        var canvas = DetectCanvas(frame, options);
        var required = _anchorMatcher.Detect(frame, templates.RequiredAnchors, options.AnchorMatching);
        var optional = _anchorMatcher.Detect(frame, templates.OptionalAnchors, options.AnchorMatching);
        return new PodiumsVisualDetectionResult(
            new PixelSize(frame.Width, frame.Height),
            canvas,
            required,
            optional);
    }

    private static PodiumsCanvasDetection DetectCanvas(
        ImageFrame frame,
        PodiumsVisualDetectionOptions options)
    {
        var visible = new bool[frame.PixelCount];
        for (var index = 0; index < visible.Length; index++)
        {
            var pixel = frame.Pixels[index];
            var minimum = Math.Min(pixel.Color.R, Math.Min(pixel.Color.G, pixel.Color.B));
            var maximum = Math.Max(pixel.Color.R, Math.Max(pixel.Color.G, pixel.Color.B));
            visible[index] = pixel.Alpha > 0 &&
                minimum >= options.MinimumCanvasChannel &&
                maximum - minimum <= options.MaximumCanvasChannelSpread;
        }

        var visited = new bool[visible.Length];
        var queue = new int[visible.Length];
        var minimumPixels = Math.Max(1, (int)Math.Ceiling(frame.PixelCount * options.MinimumCanvasAreaFraction));
        var best = default(Component);
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var start = (y * frame.Width) + x;
                if (!visible[start] || visited[start])
                {
                    continue;
                }

                var component = FloodFill(visible, visited, queue, frame.Width, frame.Height, x, y);
                var fillRatio = component.Count / (double)(component.Width * component.Height);
                if (component.Count < minimumPixels || fillRatio < options.MinimumCanvasFillRatio)
                {
                    continue;
                }

                var areaFraction = component.Count / (double)frame.PixelCount;
                var areaScore = Math.Clamp(areaFraction / options.MinimumCanvasAreaFraction, 0d, 1d);
                var confidence = Math.Clamp((fillRatio * 0.75d) + (areaScore * 0.25d), 0d, 1d);
                var candidate = component with { Confidence = confidence };
                if (candidate.Confidence > best.Confidence ||
                    candidate.Confidence.Equals(best.Confidence) && candidate.Count > best.Count)
                {
                    best = candidate;
                }
            }
        }

        if (best.Count == 0)
        {
            return PodiumsCanvasDetection.NoMatch("No bright neutral canvas component was found.");
        }

        var bounds = new PixelRect(best.MinX, best.MinY, best.Width, best.Height);
        var normalized = new NormalizedRect(
            bounds.X / (double)frame.Width,
            bounds.Y / (double)frame.Height,
            bounds.Width / (double)frame.Width,
            bounds.Height / (double)frame.Height);
        var matched = best.Confidence >= options.MinimumCanvasConfidence;
        return new PodiumsCanvasDetection(
            matched,
            best.Confidence,
            bounds,
            normalized,
            matched ? null : "Canvas candidate is below the confidence threshold.");
    }

    private static Component FloodFill(
        bool[] visible,
        bool[] visited,
        int[] queue,
        int width,
        int height,
        int startX,
        int startY)
    {
        var head = 0;
        var tail = 1;
        queue[0] = (startY * width) + startX;
        visited[queue[0]] = true;
        var count = 0;
        var minX = startX;
        var maxX = startX;
        var minY = startY;
        var maxY = startY;
        while (head < tail)
        {
            var index = queue[head++];
            var x = index % width;
            var y = index / width;
            count++;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            Visit(x - 1, y);
            Visit(x + 1, y);
            Visit(x, y - 1);
            Visit(x, y + 1);
        }

        return new Component(
            count,
            minX,
            minY,
            maxX - minX + 1,
            maxY - minY + 1,
            0d);

        void Visit(int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                return;
            }

            var index = (y * width) + x;
            if (!visible[index] || visited[index])
            {
                return;
            }

            visited[index] = true;
            queue[tail++] = index;
        }
    }

    private readonly record struct Component(
        int Count,
        int MinX,
        int MinY,
        int Width,
        int Height,
        double Confidence);
}
