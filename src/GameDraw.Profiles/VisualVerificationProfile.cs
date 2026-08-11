namespace GameDraw.Profiles;

public sealed record VisualVerificationProfile
{
    public bool Enabled { get; init; } = true;

    public double MinimumConfidence { get; init; } = 0.80d;

    public double MaximumCanvasShiftPixels { get; init; } = 12d;

    public double MaximumCanvasScaleDelta { get; init; } = 0.08d;

    public double MaximumAnchorShiftPixels { get; init; } = 16d;

    public int ConsecutiveFailuresBeforePause { get; init; } = 2;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!double.IsFinite(MinimumConfidence) || MinimumConfidence is < 0d or > 1d)
        {
            errors.Add("Visual minimum confidence must be finite and between zero and one.");
        }

        if (!double.IsFinite(MaximumCanvasShiftPixels) || MaximumCanvasShiftPixels < 0d)
        {
            errors.Add("Visual canvas shift tolerance must be finite and non-negative.");
        }

        if (!double.IsFinite(MaximumCanvasScaleDelta) || MaximumCanvasScaleDelta < 0d)
        {
            errors.Add("Visual canvas scale tolerance must be finite and non-negative.");
        }

        if (!double.IsFinite(MaximumAnchorShiftPixels) || MaximumAnchorShiftPixels < 0d)
        {
            errors.Add("Visual anchor shift tolerance must be finite and non-negative.");
        }

        if (ConsecutiveFailuresBeforePause <= 0)
        {
            errors.Add("Visual consecutive failure count must be greater than zero.");
        }

        return errors;
    }
}
