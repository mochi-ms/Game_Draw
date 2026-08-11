using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;

namespace GameDraw.Core.Vision;

public sealed record VisualVerificationPolicy
{
    public double MinimumCanvasConfidence { get; init; } = 0.80d;

    public double MaximumCanvasShiftPixels { get; init; } = 12d;

    public double MaximumCanvasScaleDelta { get; init; } = 0.08d;

    public double MaximumAnchorShiftPixels { get; init; } = 16d;

    public int ConsecutiveFailuresBeforePause { get; init; } = 2;

    public void Validate()
    {
        if (!double.IsFinite(MinimumCanvasConfidence) || MinimumCanvasConfidence is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumCanvasConfidence));
        }

        if (!double.IsFinite(MaximumCanvasShiftPixels) || MaximumCanvasShiftPixels < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCanvasShiftPixels));
        }

        if (!double.IsFinite(MaximumCanvasScaleDelta) || MaximumCanvasScaleDelta < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCanvasScaleDelta));
        }

        if (!double.IsFinite(MaximumAnchorShiftPixels) || MaximumAnchorShiftPixels < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAnchorShiftPixels));
        }

        if (ConsecutiveFailuresBeforePause <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ConsecutiveFailuresBeforePause));
        }
    }
}

public sealed record VisualObservation(
    PixelSize FrameSize,
    NormalizedRect CanvasBounds,
    double CanvasConfidence,
    IReadOnlyDictionary<string, NormalizedPoint>? Anchors = null);

public sealed record VisualDriftNotice(
    bool IsDriftDetected,
    bool ShouldPause,
    bool RequiresRecalibration,
    int ConsecutiveFailures,
    double CanvasShiftPixels,
    double CanvasScaleDelta,
    double AnchorShiftPixels,
    IReadOnlyList<string> Reasons)
{
    public static VisualDriftNotice BaselineEstablished
        => new(false, false, false, 0, 0d, 0d, 0d, Array.Empty<string>());
}

/// <summary>
/// Compares normalized observations so a window can move or change DPI
/// without producing false alarms. Repeated drift is escalated to a visual
/// pause controller; the caller can then request a fresh calibration.
/// </summary>
public sealed class VisualDriftMonitor
{
    private readonly VisualVerificationPolicy _policy;
    private VisualObservation? _baseline;
    private int _consecutiveFailures;

    public VisualDriftMonitor(VisualVerificationPolicy? policy = null)
    {
        _policy = policy ?? new VisualVerificationPolicy();
        _policy.Validate();
    }

    public VisualObservation? Baseline => _baseline;

    public VisualDriftNotice Observe(
        VisualObservation observation,
        IVisualPauseController? pauseController = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ValidateObservation(observation);
        if (_baseline is null)
        {
            _baseline = observation;
            _consecutiveFailures = 0;
            return VisualDriftNotice.BaselineEstablished;
        }

        var reasons = new List<string>();
        var canvasShift = CanvasShiftPixels(_baseline, observation);
        var scaleDelta = CanvasScaleDelta(_baseline.CanvasBounds, observation.CanvasBounds);
        var anchorShift = AnchorShiftPixels(_baseline, observation);
        if (observation.CanvasConfidence < _policy.MinimumCanvasConfidence)
        {
            reasons.Add("Canvas confidence is below the visual safety threshold.");
        }

        if (canvasShift > _policy.MaximumCanvasShiftPixels)
        {
            reasons.Add("Canvas position drift exceeded the configured tolerance.");
        }

        if (scaleDelta > _policy.MaximumCanvasScaleDelta)
        {
            reasons.Add("Canvas scale drift exceeded the configured tolerance.");
        }

        if (anchorShift > _policy.MaximumAnchorShiftPixels)
        {
            reasons.Add("Tool anchor drift exceeded the configured tolerance.");
        }

        var drifted = reasons.Count > 0;
        _consecutiveFailures = drifted ? _consecutiveFailures + 1 : 0;
        var shouldPause = drifted && _consecutiveFailures >= _policy.ConsecutiveFailuresBeforePause;
        var requiresRecalibration = shouldPause;
        if (shouldPause && pauseController is not null)
        {
            pauseController.RequestVisualPause(string.Join(" ", reasons));
        }

        return new VisualDriftNotice(
            drifted,
            shouldPause,
            requiresRecalibration,
            _consecutiveFailures,
            canvasShift,
            scaleDelta,
            anchorShift,
            reasons);
    }

    public VisualDriftNotice ObserveFailure(
        string reason,
        IVisualPauseController? pauseController = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _consecutiveFailures++;
        var shouldPause = _consecutiveFailures >= _policy.ConsecutiveFailuresBeforePause;
        if (shouldPause)
        {
            pauseController?.RequestVisualPause(reason);
        }

        return new VisualDriftNotice(
            true,
            shouldPause,
            shouldPause,
            _consecutiveFailures,
            0d,
            0d,
            0d,
            new[] { reason });
    }

    public void Reset(VisualObservation? baseline = null)
    {
        _baseline = baseline;
        _consecutiveFailures = 0;
    }

    private static double CanvasShiftPixels(
        VisualObservation baseline,
        VisualObservation current)
    {
        var baselineCenter = baseline.CanvasBounds.Center;
        var currentCenter = current.CanvasBounds.Center;
        var width = Math.Max(1, Math.Min(baseline.FrameSize.Width, current.FrameSize.Width));
        var height = Math.Max(1, Math.Min(baseline.FrameSize.Height, current.FrameSize.Height));
        var x = (currentCenter.X - baselineCenter.X) * width;
        var y = (currentCenter.Y - baselineCenter.Y) * height;
        return Math.Sqrt((x * x) + (y * y));
    }

    private static double CanvasScaleDelta(NormalizedRect baseline, NormalizedRect current)
    {
        var width = RelativeDelta(baseline.Width, current.Width);
        var height = RelativeDelta(baseline.Height, current.Height);
        return Math.Max(width, height);
    }

    private static double AnchorShiftPixels(
        VisualObservation baseline,
        VisualObservation current)
    {
        if (baseline.Anchors is null || current.Anchors is null)
        {
            return 0d;
        }

        var width = Math.Max(1, Math.Min(baseline.FrameSize.Width, current.FrameSize.Width));
        var height = Math.Max(1, Math.Min(baseline.FrameSize.Height, current.FrameSize.Height));
        var maximum = 0d;
        foreach (var pair in baseline.Anchors)
        {
            if (!current.Anchors.TryGetValue(pair.Key, out var currentPoint))
            {
                continue;
            }

            var x = (currentPoint.X - pair.Value.X) * width;
            var y = (currentPoint.Y - pair.Value.Y) * height;
            maximum = Math.Max(maximum, Math.Sqrt((x * x) + (y * y)));
        }

        return maximum;
    }

    private static double RelativeDelta(double first, double second)
        => Math.Abs(first) < double.Epsilon
            ? Math.Abs(second)
            : Math.Abs(second - first) / Math.Abs(first);

    private static void ValidateObservation(VisualObservation observation)
    {
        if (observation.FrameSize.Width <= 0 ||
            observation.FrameSize.Height <= 0 ||
            !observation.CanvasBounds.IsWithinUnitSquare ||
            observation.CanvasBounds.Width <= 0 ||
            observation.CanvasBounds.Height <= 0 ||
            !double.IsFinite(observation.CanvasConfidence) ||
            observation.CanvasConfidence is < 0d or > 1d)
        {
            throw new ArgumentException("Visual observation is invalid.", nameof(observation));
        }
    }
}
