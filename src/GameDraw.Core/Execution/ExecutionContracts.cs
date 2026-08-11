using GameDraw.Core.Drawing;
using GameDraw.Core.Colors;
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

    public double MovementPixelsPerSecond { get; init; } = 500d;

    public int InterStrokeDelayMilliseconds { get; init; } = 25;

    public int ColorChangeDelayMilliseconds { get; init; } = 100;

    public bool RequireForegroundTarget { get; init; } = true;

    public IDrawingExecutionHooks? Hooks { get; init; }

    public void Validate()
    {
        if (!double.IsFinite(SpeedMultiplier) || SpeedMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedMultiplier), "Speed multiplier must be finite and greater than zero.");
        }

        if (!double.IsFinite(MovementPixelsPerSecond) || MovementPixelsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MovementPixelsPerSecond), "Movement speed must be finite and greater than zero.");
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

/// <summary>
/// Target-specific actions that must run around a generic drawing plan, such
/// as selecting a game tool or applying the next color. Implementations must
/// throw when the target cannot be prepared safely.
/// </summary>
public interface IDrawingExecutionHooks
{
    ValueTask BeforePlanAsync(
        DrawingPlan plan,
        CancellationToken cancellationToken = default);

    ValueTask BeforeColorGroupAsync(
        RgbColor color,
        int colorGroupIndex,
        CancellationToken cancellationToken = default);
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
