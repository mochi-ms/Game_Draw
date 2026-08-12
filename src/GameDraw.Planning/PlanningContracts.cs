using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
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

    /// <summary>
    /// Optional subject-detail region. Layered planners keep the largest
    /// silhouette first, then prioritize closed feature lines here before
    /// completing the remaining underdrawing.
    /// </summary>
    public NormalizedRect? PriorityRegion { get; init; }

    public int FillRowStep { get; init; } = 1;

    public double MovementPixelsPerSecond { get; init; } = 500d;

    public int InterStrokeDelayMilliseconds { get; init; } = 25;

    public int ColorChangeDelayMilliseconds { get; init; } = 100;

    public int PerStrokeSafetyDelayMilliseconds { get; init; }

    public double PenUpMovementMultiplier { get; init; } = 1d;

    public double StrokeSimplificationTolerancePixels { get; init; } = 0.9d;

    public double MinimumStrokeLengthPixels { get; init; } = 3d;

    public int BrushDiameterPixels { get; init; } = 2;

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

        if (PriorityRegion is { } priorityRegion && !priorityRegion.IsWithinUnitSquare)
        {
            throw new ArgumentOutOfRangeException(nameof(PriorityRegion));
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

        if (PerStrokeSafetyDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PerStrokeSafetyDelayMilliseconds));
        }

        if (!double.IsFinite(PenUpMovementMultiplier) || PenUpMovementMultiplier < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(PenUpMovementMultiplier));
        }

        if (!double.IsFinite(StrokeSimplificationTolerancePixels) || StrokeSimplificationTolerancePixels < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(StrokeSimplificationTolerancePixels));
        }

        if (!double.IsFinite(MinimumStrokeLengthPixels) || MinimumStrokeLengthPixels < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumStrokeLengthPixels));
        }

        if (BrushDiameterPixels is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(BrushDiameterPixels));
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
