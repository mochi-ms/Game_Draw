using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Imaging.Quantization;
using GameDraw.Planning.Modes;

namespace GameDraw.Planning;

public sealed class DrawingPlanner : IDrawingPlanner
{
    private static readonly DrawingMode[] AutomaticModes =
    {
        DrawingMode.Pixel,
        DrawingMode.HorizontalScanline,
        DrawingMode.VerticalScanline,
        DrawingMode.Contour,
        DrawingMode.Fill,
        DrawingMode.Hybrid
    };

    public DrawingPlanningResult Plan(
        QuantizedImage image,
        DrawingPlannerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new DrawingPlannerOptions();
        options.Validate();

        if (options.Mode != DrawingMode.Auto)
        {
            var plan = CreateForMode(image, options.Mode, options);
            var estimate = Estimate(plan, options);
            var candidate = new ModeCandidate(
                options.Mode,
                plan,
                estimate,
                Score(estimate),
                ReasonFor(options.Mode, estimate));
            return new DrawingPlanningResult(plan, estimate, new[] { candidate });
        }

        var summaries = new List<ModeCandidate>(AutomaticModes.Length);
        DrawingPlan? selectedPlan = null;
        PlanEstimate? selectedEstimate = null;
        var selectedMode = DrawingMode.Auto;
        var bestScore = double.PositiveInfinity;
        foreach (var mode in AutomaticModes)
        {
            var plan = CreateForMode(image, mode, options with { Mode = mode });
            var estimate = Estimate(plan, options);
            var score = Score(estimate);
            summaries.Add(new ModeCandidate(
                mode,
                DrawingPlan.Empty(mode, plan.LogicalSize),
                estimate,
                score,
                ReasonFor(mode, estimate)));
            if (score < bestScore || (score.Equals(bestScore) && mode < selectedMode))
            {
                bestScore = score;
                selectedPlan = plan;
                selectedEstimate = estimate;
                selectedMode = mode;
            }
        }

        var candidates = summaries
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Mode)
            .Select(candidate => candidate.Mode == selectedMode && selectedPlan is not null
                ? candidate with { Plan = selectedPlan }
                : candidate)
            .ToArray();

        if (candidates.Length == 0)
        {
            var empty = DrawingPlan.Empty(DrawingMode.Auto, new GameDraw.Core.Geometry.PixelSize(image.Width, image.Height));
            var emptyEstimate = Estimate(empty, options);
            return new DrawingPlanningResult(empty, emptyEstimate, Array.Empty<ModeCandidate>());
        }

        return new DrawingPlanningResult(selectedPlan!, selectedEstimate!, candidates);
    }

    public PlanEstimate Estimate(
        DrawingPlan plan,
        DrawingPlannerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= new DrawingPlannerOptions();
        options.Validate();
        return DrawingTimeEstimator.Estimate(plan, options);
    }

    private static DrawingPlan CreateForMode(
        QuantizedImage image,
        DrawingMode mode,
        DrawingPlannerOptions options)
        => mode switch
        {
            DrawingMode.Pixel => PixelPlanner.Create(image, options),
            DrawingMode.HorizontalScanline => ScanlinePlanner.CreateHorizontal(image, options),
            DrawingMode.VerticalScanline => ScanlinePlanner.CreateVertical(image, options),
            DrawingMode.Contour => ContourPlanner.Create(image, options),
            DrawingMode.Fill => FillPlanner.Create(image, options),
            DrawingMode.Hybrid => HybridPlanner.Create(image, options),
            DrawingMode.Auto => throw new ArgumentException("자동 모드는 후보 생성 경로에서만 사용할 수 있습니다.", nameof(mode)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    // Travel dominates because it determines input time; stroke count is a
    // small tie-breaker for pixel-heavy plans with similar distance.
    private static double Score(PlanEstimate estimate)
        => estimate.TotalTravelPixels + (estimate.StrokeCount * 0.25d) + (estimate.ColorChanges * 5d);

    private static string ReasonFor(DrawingMode mode, PlanEstimate estimate)
        => mode switch
        {
            DrawingMode.Pixel => $"원본 점 보존 우선 · 스트로크 {estimate.StrokeCount}개",
            DrawingMode.HorizontalScanline => $"가로 연속 색상 병합 · 이동 {estimate.TotalTravelPixels:F1}px",
            DrawingMode.VerticalScanline => $"세로 연속 색상 병합 · 이동 {estimate.TotalTravelPixels:F1}px",
            DrawingMode.Contour => $"경계 윤곽선 중심 · 스트로크 {estimate.StrokeCount}개",
            DrawingMode.Fill => $"면 채우기 중심 · 스트로크 {estimate.StrokeCount}개",
            DrawingMode.Hybrid => $"윤곽선+채우기 · 예상 {estimate.EstimatedDuration.TotalSeconds:F1}초",
            _ => "자동 후보"
        };
}
