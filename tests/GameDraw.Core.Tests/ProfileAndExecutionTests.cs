using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;

namespace GameDraw.Core.Tests;

public sealed class ProfileAndExecutionTests
{
    [Fact]
    public void ProfileRoundTripsThroughJson()
    {
        var profile = GameProfile.CreateDefault("Whiteboard", "Example Game") with
        {
            Canvas = new CanvasProfile { Bounds = new CanvasRect(-100, 50, 800, 600), LogicalWidth = 80, LogicalHeight = 60 },
            ColorAdapter = new ColorAdapterProfile { Kind = ColorAdapterKind.Manual }
        };

        var json = ProfileSerializer.Serialize(profile);
        var restored = ProfileSerializer.Deserialize(json);

        Assert.Contains("\"schemaVersion\"", json, StringComparison.Ordinal);
        Assert.Equal(profile.Id, restored.Id);
        Assert.Equal("Whiteboard", restored.Name);
        Assert.Equal(new CanvasRect(-100, 50, 800, 600), restored.Canvas.Bounds);
    }

    [Fact]
    public void ProfileValidationRejectsUncalibratedHexInput()
    {
        var profile = GameProfile.CreateDefault("Hex", "Game") with
        {
            ColorAdapter = new ColorAdapterProfile { Kind = ColorAdapterKind.HexInput }
        };

        var validation = profile.Validate();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("HEX", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanStatisticsCountColorsStrokesPointsAndTravel()
    {
        var plan = new DrawingPlan(
            DrawingMode.Scanline,
            10,
            10,
            new[]
            {
                new ColorGroup(new RgbColor(0, 0, 0), new[]
                {
                    new Stroke(new[] { new NormalizedPoint(0, 0), new NormalizedPoint(0.5, 0) })
                }),
                new ColorGroup(new RgbColor(255, 255, 255), new[]
                {
                    new Stroke(new[] { new NormalizedPoint(0.5, 0), new NormalizedPoint(1, 0) })
                })
            });

        var stats = plan.Statistics;

        Assert.Equal(2, stats.ColorCount);
        Assert.Equal(2, stats.StrokeCount);
        Assert.Equal(4, stats.PointCount);
        Assert.Equal(1, stats.ColorChanges);
        Assert.Equal(1d, stats.NormalizedTravelDistance, precision: 6);
    }

    [Fact]
    public async Task ExecutorAlwaysReleasesMouseWhenCancelled()
    {
        var plan = new DrawingPlan(
            DrawingMode.Scanline,
            10,
            10,
            new[]
            {
                new ColorGroup(new RgbColor(0, 0, 0), new[]
                {
                    new Stroke(new[] { new NormalizedPoint(0, 0), new NormalizedPoint(1, 0) })
                })
            });
        var profile = GameProfile.CreateDefault("Test", "Game") with
        {
            Canvas = new CanvasProfile { Bounds = new CanvasRect(0, 0, 100, 100), LogicalWidth = 10, LogicalHeight = 10 },
            InputSampling = new InputSamplingProfile { MovementSpeedPixelsPerSecond = 10, SampleSpacingPixels = 1, MinimumStrokeDurationMs = 0 }
        };
        var input = new RecordingInputController();
        using var cancellation = new CancellationTokenSource();
        var adapter = new NoOpColorAdapter();
        var execution = new DrawingExecutor();

        var task = execution.ExecuteAsync(plan, profile, adapter, input, new PauseGate(), cancellationToken: cancellation.Token);
        await Task.Delay(50);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(input.MouseUpCount > 0);
    }

    private sealed class NoOpColorAdapter : IColorAdapter
    {
        public ColorAdapterKind Kind => ColorAdapterKind.Manual;
        public string DisplayName => "Test";
        public AdapterCapabilities Capabilities => AdapterCapabilities.None;
        public ProfileValidationResult Validate(ColorAdapterProfile profile) => new(Array.Empty<string>());
        public ValueTask SelectColorAsync(RgbColor color, ColorAdapterProfile profile, IInputController input, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingInputController : IInputController
    {
        public int MouseUpCount { get; private set; }

        public ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask MouseDownAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask MouseUpAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
        {
            MouseUpCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClickAsync(ScreenPoint point, InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask KeyDownAsync(InputKey key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask TypeTextAsync(string text, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
