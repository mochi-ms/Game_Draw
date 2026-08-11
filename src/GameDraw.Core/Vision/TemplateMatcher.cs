using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;

namespace GameDraw.Core.Vision;

/// <summary>
/// Small, deterministic normalized-error matcher. It is deliberately free of
/// native or UI dependencies so synthetic frames can exercise recognition and
/// confidence gates in tests.
/// </summary>
public sealed class TemplateMatcher
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The matcher remains an instance service so callers can replace it in adapter composition.")]
    public TemplateMatchResult FindBest(
        ImageFrame source,
        ImageFrame template,
        VisionMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(template);
        options ??= new VisionMatchOptions();
        options.Validate();

        if (template.Width > source.Width || template.Height > source.Height)
        {
            return TemplateMatchResult.NoMatch("Template is larger than the source frame.");
        }

        var samples = BuildSamples(template, options.MaxSamplesPerCandidate);
        if (samples.Count == 0)
        {
            return TemplateMatchResult.NoMatch("Template has no visible pixels.");
        }

        var maxX = source.Width - template.Width;
        var maxY = source.Height - template.Height;
        var region = options.SearchRegion ?? new PixelRect(0, 0, source.Width, source.Height);
        var startX = Math.Clamp(region.X, 0, maxX);
        var startY = Math.Clamp(region.Y, 0, maxY);
        var endX = Math.Min(maxX, region.Right - template.Width);
        var endY = Math.Min(maxY, region.Bottom - template.Height);
        if (endX < startX || endY < startY)
        {
            return TemplateMatchResult.NoMatch("Search region does not contain the template.");
        }

        var candidateWidth = ((endX - startX) / options.SearchStep) + 1;
        var candidateHeight = ((endY - startY) / options.SearchStep) + 1;
        var candidateCount = (long)candidateWidth * candidateHeight;
        var effectiveStep = options.SearchStep;
        if (candidateCount > options.MaxCandidates)
        {
            var multiplier = (int)Math.Ceiling(Math.Sqrt(candidateCount / (double)options.MaxCandidates));
            effectiveStep = checked(options.SearchStep * Math.Max(1, multiplier));
        }

        var bestConfidence = double.MinValue;
        var bestPoint = default(PixelPoint);
        for (var y = startY; y <= endY; y += effectiveStep)
        {
            for (var x = startX; x <= endX; x += effectiveStep)
            {
                var confidence = Score(source, template, samples, x, y);
                if (confidence > bestConfidence)
                {
                    bestConfidence = confidence;
                    bestPoint = new PixelPoint(x, y);
                }
            }
        }

        var bounds = new PixelRect(bestPoint.X, bestPoint.Y, template.Width, template.Height);
        var normalized = new NormalizedRect(
            bounds.X / (double)source.Width,
            bounds.Y / (double)source.Height,
            bounds.Width / (double)source.Width,
            bounds.Height / (double)source.Height);
        var isMatch = bestConfidence >= options.MinimumConfidence;
        return new TemplateMatchResult(
            isMatch,
            Math.Clamp(bestConfidence, 0d, 1d),
            bounds,
            normalized,
            isMatch ? null : "Best candidate is below the confidence threshold.");
    }

    private static List<PixelPoint> BuildSamples(ImageFrame template, int maxSamples)
    {
        var visible = new List<PixelPoint>();
        for (var y = 0; y < template.Height; y++)
        {
            for (var x = 0; x < template.Width; x++)
            {
                if (!template[x, y].IsTransparent)
                {
                    visible.Add(new PixelPoint(x, y));
                }
            }
        }

        if (visible.Count <= maxSamples)
        {
            return visible;
        }

        var stride = (int)Math.Ceiling(visible.Count / (double)maxSamples);
        return visible.Where((_, index) => index % stride == 0).Take(maxSamples).ToList();
    }

    private static double Score(
        ImageFrame source,
        ImageFrame template,
        List<PixelPoint> samples,
        int offsetX,
        int offsetY)
    {
        var error = 0d;
        foreach (var sample in samples)
        {
            var expected = template[sample.X, sample.Y];
            var actual = source[offsetX + sample.X, offsetY + sample.Y];
            var alpha = expected.Alpha / 255d;
            var red = Math.Abs(expected.Color.R - actual.Color.R);
            var green = Math.Abs(expected.Color.G - actual.Color.G);
            var blue = Math.Abs(expected.Color.B - actual.Color.B);
            error += ((red + green + blue) / 765d) * alpha;
        }

        return Math.Clamp(1d - (error / samples.Count), 0d, 1d);
    }
}

public sealed class AnchorMatcher
{
    private readonly TemplateMatcher _templateMatcher;

    public AnchorMatcher(TemplateMatcher? templateMatcher = null)
    {
        _templateMatcher = templateMatcher ?? new TemplateMatcher();
    }

    public AnchorDetectionResult Detect(
        ImageFrame source,
        IReadOnlyList<VisualAnchorDefinition> anchors,
        VisionMatchOptions? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(anchors);
        defaults ??= new VisionMatchOptions();
        defaults.Validate();

        var matches = new List<AnchorMatchResult>(anchors.Count);
        var missing = new List<string>();
        foreach (var anchor in anchors)
        {
            ArgumentNullException.ThrowIfNull(anchor);
            anchor.Validate();
            var options = defaults with
            {
                MinimumConfidence = anchor.MinimumConfidence ?? defaults.MinimumConfidence,
                SearchRegion = anchor.SearchRegion ?? defaults.SearchRegion
            };
            var match = _templateMatcher.FindBest(source, anchor.Template, options);
            var required = options.MinimumConfidence;
            var result = new AnchorMatchResult(anchor.Id, match, required);
            matches.Add(result);
            if (!result.IsMatch)
            {
                missing.Add(anchor.Id);
            }
        }

        return new AnchorDetectionResult(matches, missing);
    }
}
