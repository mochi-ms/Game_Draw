using GameDraw.Automation.Windows;
using GameDraw.Automation.Windows.Capture;
using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.GameAdapters.Podiums.Vision;
using GameDraw.Profiles;

namespace GameDraw.Integration.Tests;

public sealed class PodiumsVisualTests
{
    [Fact]
    public void PodiumsDetectorFindsBrightCanvasAndRequiredAnchor()
    {
        var frame = CreateFrame(100, 80, 10, 10);
        var template = new ImageFrame(2, 2, new[]
        {
            RgbaPixel.Opaque(new RgbColor(255, 0, 0)),
            RgbaPixel.Opaque(new RgbColor(0, 255, 0)),
            RgbaPixel.Opaque(new RgbColor(0, 0, 255)),
            RgbaPixel.Opaque(RgbColor.Black)
        });
        frame = frame
            .WithPixel(85, 20, template[0, 0])
            .WithPixel(86, 20, template[1, 0])
            .WithPixel(85, 21, template[0, 1])
            .WithPixel(86, 21, template[1, 1]);

        var result = new PodiumsVisualDetector().Detect(
            frame,
            new PodiumsVisualTemplates
            {
                PencilTool = new GameDraw.Core.Vision.VisualAnchorDefinition("pencil", template, 0.99d)
            },
            new PodiumsVisualDetectionOptions
            {
                MinimumCanvasConfidence = 0.75d,
                AnchorMatching = new GameDraw.Core.Vision.VisionMatchOptions
                {
                    MinimumConfidence = 0.99d
                }
            });

        Assert.True(result.Canvas.IsMatch, result.Canvas.Reason);
        Assert.Equal(new PixelRect(10, 10, 70, 60), result.Canvas.Bounds);
        Assert.True(result.RequiredAnchors.IsSafeToContinue);
        Assert.True(result.IsSafeToContinue);
        Assert.Contains("pencil", result.AnchorCenters.Keys);
    }

    [Fact]
    public void SafetyCoordinatorRequestsPauseAfterCanvasMoves()
    {
        var detector = new PodiumsVisualDetector();
        var pause = new RecordingPauseController();
        var coordinator = new PodiumsVisualSafetyCoordinator(
            new GameDraw.Core.Vision.VisualVerificationPolicy
            {
                MaximumCanvasShiftPixels = 2d,
                ConsecutiveFailuresBeforePause = 2
            },
            pause);

        var baseline = detector.Detect(CreateFrame(100, 80, 10, 10));
        var moved = detector.Detect(CreateFrame(100, 80, 30, 10));
        coordinator.Observe(baseline);
        var first = coordinator.Observe(moved);
        var second = coordinator.Observe(moved);

        Assert.False(first.ShouldPause);
        Assert.True(second.ShouldPause);
        Assert.True(pause.VisualPauseRequested);
        Assert.Contains("drift", pause.VisualPauseReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapturedBgraFrameConvertsToCoreImageFrame()
    {
        var target = new GameDraw.Core.Targeting.TargetWindowSnapshot(
            123,
            "fake",
            "Fake",
            2,
            1,
            96,
            true);
        var captured = new CapturedWindowFrame(
            target,
            new PixelSize(2, 1),
            DateTimeOffset.UtcNow,
            new byte[] { 0, 0, 255, 0, 0, 255, 0, 255 });

        var frame = captured.ToImageFrame();

        Assert.Equal(new RgbColor(255, 0, 0), frame[0, 0].Color);
        Assert.Equal(byte.MaxValue, frame[0, 0].Alpha);
        Assert.Equal(new RgbColor(0, 255, 0), frame[1, 0].Color);
        Assert.Equal(byte.MaxValue, frame[1, 0].Alpha);
    }

    [Fact]
    public void ProfileVisualSettingsConfigureAndCanDisableSafetyCoordinator()
    {
        var profile = GameProfile.CreateDefault() with
        {
            VisualVerification = new VisualVerificationProfile
            {
                Enabled = false,
                MinimumConfidence = 0.92d,
                ConsecutiveFailuresBeforePause = 3
            }
        };
        var coordinator = new GameDraw.GameAdapters.Podiums.PodiumsGameAdapter()
            .CreateVisualSafetyCoordinator(profile);

        Assert.False(coordinator.IsEnabled);
        var result = coordinator.Observe(new PodiumsVisualDetector().Detect(CreateFrame(100, 80, 10, 10)));
        Assert.False(result.IsDriftDetected);
        Assert.False(result.ShouldPause);
    }

    private static ImageFrame CreateFrame(int width, int height, int canvasX, int canvasY)
    {
        var pixels = Enumerable.Repeat(
            RgbaPixel.Opaque(new RgbColor(12, 14, 18)),
            width * height).ToArray();
        for (var y = canvasY; y < canvasY + 60; y++)
        {
            for (var x = canvasX; x < canvasX + 70; x++)
            {
                pixels[(y * width) + x] = RgbaPixel.Opaque(new RgbColor(255, 255, 255));
            }
        }

        return new ImageFrame(width, height, pixels);
    }

    private sealed class RecordingPauseController : GameDraw.Core.Execution.IVisualPauseController
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
