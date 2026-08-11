using GameDraw.Core.Colors;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Vision;

namespace GameDraw.Core.Tests;

public sealed class VisionTests
{
    [Fact]
    public void TemplateMatcherFindsExactPatternAndNormalizedBounds()
    {
        var source = Solid(8, 8, RgbColor.Black);
        source = source
            .WithPixel(3, 4, RgbaPixel.Opaque(RgbColor.White))
            .WithPixel(4, 4, RgbaPixel.Opaque(new RgbColor(255, 0, 0)))
            .WithPixel(3, 5, RgbaPixel.Opaque(new RgbColor(0, 255, 0)))
            .WithPixel(4, 5, RgbaPixel.Opaque(new RgbColor(0, 0, 255)));
        var template = new ImageFrame(2, 2, new[]
        {
            RgbaPixel.Opaque(RgbColor.White),
            RgbaPixel.Opaque(new RgbColor(255, 0, 0)),
            RgbaPixel.Opaque(new RgbColor(0, 255, 0)),
            RgbaPixel.Opaque(new RgbColor(0, 0, 255))
        });

        var result = new TemplateMatcher().FindBest(source, template, new VisionMatchOptions
        {
            MinimumConfidence = 0.99d
        });

        Assert.True(result.IsMatch);
        Assert.Equal(1d, result.Confidence, precision: 6);
        Assert.Equal(new PixelRect(3, 4, 2, 2), result.Bounds);
        Assert.Equal(0.375, result.NormalizedBounds.X, precision: 6);
        Assert.Equal(0.5, result.NormalizedBounds.Y, precision: 6);
    }

    [Fact]
    public void AnchorMatcherReportsBelowThresholdAsMissing()
    {
        var source = Solid(4, 4, RgbColor.Black);
        var template = new ImageFrame(1, 1, new[] { RgbaPixel.Opaque(RgbColor.White) });
        var result = new AnchorMatcher().Detect(
            source,
            new[] { new VisualAnchorDefinition("pencil", template, 0.95d) });

        Assert.False(result.IsSafeToContinue);
        Assert.Contains("pencil", result.MissingIds);
        Assert.False(result.Matches[0].IsMatch);
    }

    [Fact]
    public void VisualDriftMonitorPausesOnlyAfterConsecutiveFailures()
    {
        var pause = new RecordingVisualPauseController();
        var monitor = new VisualDriftMonitor(new VisualVerificationPolicy
        {
            MaximumCanvasShiftPixels = 4d,
            ConsecutiveFailuresBeforePause = 2
        });
        var baseline = new VisualObservation(
            new PixelSize(100, 100),
            new NormalizedRect(0.1, 0.1, 0.5, 0.5),
            0.99d,
            new Dictionary<string, NormalizedPoint>
            {
                ["pencil"] = new NormalizedPoint(0.2, 0.2)
            });
        var drifted = baseline with
        {
            CanvasBounds = new NormalizedRect(0.2, 0.1, 0.5, 0.5),
            Anchors = new Dictionary<string, NormalizedPoint>
            {
                ["pencil"] = new NormalizedPoint(0.3, 0.2)
            }
        };

        Assert.False(monitor.Observe(baseline, pause).ShouldPause);
        var first = monitor.Observe(drifted, pause);
        var second = monitor.Observe(drifted, pause);

        Assert.True(first.IsDriftDetected);
        Assert.False(first.ShouldPause);
        Assert.True(second.ShouldPause);
        Assert.True(pause.VisualPauseRequested);
        Assert.Contains("drift", pause.VisualPauseReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VisualFailureCanRequestPauseAndResetClearsController()
    {
        var pause = new RecordingVisualPauseController();
        var monitor = new VisualDriftMonitor(new VisualVerificationPolicy
        {
            ConsecutiveFailuresBeforePause = 1
        });

        var notice = monitor.ObserveFailure("canvas missing", pause);
        var reason = pause.VisualPauseReason;
        monitor.Reset();
        pause.ClearVisualPause();

        Assert.True(notice.ShouldPause);
        Assert.Equal("canvas missing", reason);
        Assert.False(pause.VisualPauseRequested);
        Assert.Null(monitor.Baseline);
    }

    private static ImageFrame Solid(int width, int height, RgbColor color)
        => new(width, height, Enumerable.Repeat(RgbaPixel.Opaque(color), width * height).ToArray());

    private sealed class RecordingVisualPauseController : IVisualPauseController
    {
        public bool VisualPauseRequested { get; private set; }

        public string? VisualPauseReason { get; private set; }

        public void RequestVisualPause(string reason)
        {
            VisualPauseRequested = true;
            VisualPauseReason = reason;
        }

        public void ClearVisualPause()
        {
            VisualPauseRequested = false;
            VisualPauseReason = null;
        }
    }
}
