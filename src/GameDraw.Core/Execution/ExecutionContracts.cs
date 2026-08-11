using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Core.Targeting;

namespace GameDraw.Core.Execution;

public enum DrawingExecutionState
{
    Idle = 0,
    Preparing = 1,
    Running = 2,
    Paused = 3,
    Completed = 4,
    Stopping = 5,
    Failed = 6
}

public sealed record DrawingExecutionOptions
{
    public double SpeedMultiplier { get; init; } = 1d;

    public int InterStrokeDelayMilliseconds { get; init; } = 25;

    public int ColorChangeDelayMilliseconds { get; init; } = 100;

    public bool RequireForegroundTarget { get; init; } = true;

    public void Validate()
    {
        if (!double.IsFinite(SpeedMultiplier) || SpeedMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedMultiplier), "Speed multiplier must be finite and greater than zero.");
        }

        if (InterStrokeDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InterStrokeDelayMilliseconds));
        }

        if (ColorChangeDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ColorChangeDelayMilliseconds));
        }
    }
}

public sealed record DrawingProgress(
    DrawingExecutionState State,
    double Fraction,
    int CompletedStrokes,
    int TotalStrokes,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining,
    string Message)
{
    public double ClampedFraction => double.IsFinite(Fraction) ? Math.Clamp(Fraction, 0d, 1d) : 0d;
}

public sealed record DrawingExecutionResult(
    DrawingExecutionState State,
    int CompletedStrokes,
    TimeSpan Duration,
    string? ErrorMessage = null);

public interface IDrawingExecutor
{
    Task<DrawingExecutionResult> ExecuteAsync(
        DrawingPlan plan,
        DrawingExecutionOptions options,
        IProgress<DrawingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IEmergencyStopController
{
    bool StopRequested { get; }

    void RequestStop();
}

public interface IPauseController
{
    bool IsPaused { get; }

    void Pause();

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Resume is the public pause/resume vocabulary used by the execution contract.")]
    void Resume();
}

public interface IVisualPauseController
{
    bool VisualPauseRequested { get; }

    string? VisualPauseReason { get; }

    void RequestVisualPause(string reason);

    void ClearVisualPause();
}

public interface ITargetVerifier
{
    ValueTask<TargetVerificationResult> VerifyAsync(
        TargetWindowSnapshot target,
        DrawingMode mode,
        CancellationToken cancellationToken = default);
}
