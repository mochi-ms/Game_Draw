using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;

namespace GameDraw.Core.Vision;

public sealed record VisionMatchOptions
{
    public double MinimumConfidence { get; init; } = 0.85d;

    public int SearchStep { get; init; } = 1;

    public int MaxSamplesPerCandidate { get; init; } = 1024;

    public int MaxCandidates { get; init; } = 250_000;

    /// <summary>
    /// Region of the source image in which the template's top-left corner may
    /// occur. A null value searches the complete source image.
    /// </summary>
    public PixelRect? SearchRegion { get; init; }

    public void Validate()
    {
        if (!double.IsFinite(MinimumConfidence) || MinimumConfidence is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumConfidence));
        }

        if (SearchStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SearchStep));
        }

        if (MaxSamplesPerCandidate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSamplesPerCandidate));
        }

        if (MaxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCandidates));
        }

        if (SearchRegion is { } region && !region.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(SearchRegion));
        }
    }
}

public sealed record TemplateMatchResult(
    bool IsMatch,
    double Confidence,
    PixelRect Bounds,
    NormalizedRect NormalizedBounds,
    string? Reason = null)
{
    public PixelPoint Center => Bounds.Center;

    public static TemplateMatchResult NoMatch(string reason)
        => new(false, 0d, default, default, reason);
}

public sealed record VisualAnchorDefinition(
    string Id,
    ImageFrame Template,
    double? MinimumConfidence = null,
    PixelRect? SearchRegion = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("Anchor id is required.", nameof(Id));
        }

        ArgumentNullException.ThrowIfNull(Template);
        if (MinimumConfidence is { } confidence &&
            (!double.IsFinite(confidence) || confidence is < 0d or > 1d))
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumConfidence));
        }

        if (SearchRegion is { } region && !region.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(SearchRegion));
        }
    }
}

public sealed record AnchorMatchResult(
    string Id,
    TemplateMatchResult Match,
    double RequiredConfidence)
{
    public bool IsMatch => Match.IsMatch && Match.Confidence >= RequiredConfidence;
}

public sealed record AnchorDetectionResult(
    IReadOnlyList<AnchorMatchResult> Matches,
    IReadOnlyList<string> MissingIds)
{
    public bool IsSafeToContinue => MissingIds.Count == 0;
}
