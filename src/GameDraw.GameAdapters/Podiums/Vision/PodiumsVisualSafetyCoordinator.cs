using GameDraw.Core.Execution;
using GameDraw.Core.Vision;
using GameDraw.Profiles;

namespace GameDraw.GameAdapters.Podiums.Vision;

/// <summary>
/// Bridges Podiums visual detections to the generic drift monitor. A missing
/// canvas or required anchor is treated as a repeated visual failure, so the
/// bound executor can pause before sending input to a shifted target.
/// </summary>
public sealed class PodiumsVisualSafetyCoordinator
{
    private readonly VisualDriftMonitor _monitor;
    private readonly IVisualPauseController? _pauseController;
    private readonly bool _enabled;

    public PodiumsVisualSafetyCoordinator(
        VisualVerificationPolicy? policy = null,
        IVisualPauseController? pauseController = null,
        bool enabled = true)
    {
        _monitor = new VisualDriftMonitor(policy);
        _pauseController = pauseController;
        _enabled = enabled;
    }

    public bool IsEnabled => _enabled;

    public static PodiumsVisualSafetyCoordinator ForProfile(
        GameProfile profile,
        IVisualPauseController? pauseController = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var settings = profile.VisualVerification ?? new VisualVerificationProfile();
        return new PodiumsVisualSafetyCoordinator(
            new VisualVerificationPolicy
            {
                MinimumCanvasConfidence = settings.MinimumConfidence,
                MaximumCanvasShiftPixels = settings.MaximumCanvasShiftPixels,
                MaximumCanvasScaleDelta = settings.MaximumCanvasScaleDelta,
                MaximumAnchorShiftPixels = settings.MaximumAnchorShiftPixels,
                ConsecutiveFailuresBeforePause = settings.ConsecutiveFailuresBeforePause
            },
            pauseController,
            settings.Enabled);
    }

    public VisualObservation? Baseline => _monitor.Baseline;

    public VisualDriftNotice Observe(PodiumsVisualDetectionResult detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        if (!_enabled)
        {
            return VisualDriftNotice.BaselineEstablished;
        }

        if (!detection.Canvas.IsMatch)
        {
            return _monitor.ObserveFailure(
                detection.Canvas.Reason ?? "Podiums canvas was not detected.",
                _pauseController);
        }

        if (!detection.RequiredAnchors.IsSafeToContinue)
        {
            var missing = string.Join(", ", detection.RequiredAnchors.MissingIds);
            return _monitor.ObserveFailure(
                $"Podiums required visual anchors are missing: {missing}.",
                _pauseController);
        }

        return _monitor.Observe(detection.ToObservation(), _pauseController);
    }

    public void Reset(VisualObservation? baseline = null)
    {
        _monitor.Reset(baseline);
        _pauseController?.ClearVisualPause();
    }
}
