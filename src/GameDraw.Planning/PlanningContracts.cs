using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;

namespace GameDraw.Planning;

public sealed record DrawingPlannerOptions
{
    public DrawingMode Mode { get; init; } = DrawingMode.Auto;

    public bool IncludeTransparentPixels { get; init; }

    public byte TransparentThreshold { get; init; }

    public bool AlternateScanDirection { get; init; } = true;

    public bool OrderStrokesByTravel { get; init; } = true;

    public bool OrderColorGroupsByTravel { get; init; }

    public bool CloseContours { get; init; } = true;

    public int FillRowStep { get; init; } = 1;

    public double MovementPixelsPerSecond { get; init; } = 500d;

    public int InterStrokeDelayMilliseconds { get; init; } = 25;

    public int ColorChangeDelayMilliseconds { get; init; } = 100;

    public double PenUpMovementMultiplier { get; init; } = 1d;

    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentException("지원하지 않는 그리기 모드입니다.");
        }

        if (FillRowStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FillRowStep));
        }

        if (!double.IsFinite(MovementPixelsPerSecond) || MovementPixelsPerSecond <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(MovementPixelsPerSecond));
        }

        if (InterStrokeDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InterStrokeDelayMilliseconds));
        }

        if (ColorChangeDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ColorChangeDelayMilliseconds));
        }

        if (!double.IsFinite(PenUpMovementMultiplier) || PenUpMovementMultiplier < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(PenUpMovementMultiplier));
        }
    }
}

public sealed record PlanEstimate(
    int StrokeCount,
    int PointCount,
    int ColorCount,
    int ColorChanges,
    double PenDownTravelPixels,
    double PenUpTravelPixels,
    double TotalTravelPixels,
    TimeSpan EstimatedDuration)
{
    public double TravelPixelsPerSecond
        => EstimatedDuration.TotalSeconds <= 0d
            ? 0d
            : TotalTravelPixels / EstimatedDuration.TotalSeconds;
}

public sealed record ModeCandidate(
    DrawingMode Mode,
    DrawingPlan Plan,
    PlanEstimate Estimate,
    double Score,
    string Reason);

public sealed record DrawingPlanningResult(
    DrawingPlan Plan,
    PlanEstimate Estimate,
    IReadOnlyList<ModeCandidate> Candidates)
{
    public DrawingMode SelectedMode => Plan.Mode;
}

public interface IDrawingPlanner
{
    DrawingPlanningResult Plan(
        QuantizedImage image,
        DrawingPlannerOptions? options = null);

    PlanEstimate Estimate(
        DrawingPlan plan,
        DrawingPlannerOptions? options = null);
}

public sealed class DrawingPlanningException(string message)
    : Exception(message);
