using GameDraw.Automation.Windows;
using GameDraw.Automation.Windows.Coordinates;
using GameDraw.Automation.Windows.Execution;
using GameDraw.Automation.Windows.Targeting;
using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Core.Targeting;

namespace GameDraw.Integration.Tests;

public sealed class WindowsExecutionTests
{
    [Fact]
    public async Task ClientMapperTracksMoveResizeAndDpiGeometry()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(100, 200, 200, 100, 144));
        var binding = new TargetWindowBinding(
            provider.Current,
            provider,
            new NormalizedRect(0.25, 0.25, 0.5, 0.5));

        Assert.Equal(new ScreenPoint(150, 225), binding.Map(new NormalizedPoint(0, 0)));
        Assert.Equal(new ScreenPoint(249, 274), binding.Map(new NormalizedPoint(1, 1)));
        Assert.Equal(new ScreenPoint(100, 200), binding.MapClient(new NormalizedPoint(0, 0)));
        Assert.Equal(new ScreenPoint(299, 299), binding.MapClient(new NormalizedPoint(1, 1)));

        provider.Current = CreateGeometry(500, 600, 400, 200, 192);
        Assert.True(await binding.RefreshAsync());

        Assert.Equal(new ScreenPoint(600, 650), binding.Map(new NormalizedPoint(0, 0)));
        Assert.Equal(new ScreenPoint(799, 749), binding.Map(new NormalizedPoint(1, 1)));
        Assert.Equal(2d, new ClientCoordinateMapper(provider.Current).DpiScale, precision: 6);
    }

    [Fact]
    public async Task ExecutorCompletesAndReleasesButtons()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(10, 20, 100, 100, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var plan = CreatePlan();
        var progress = new List<DrawingProgress>();

        var result = await executor.ExecuteAsync(
            plan,
            new DrawingExecutionOptions
            {
                SpeedMultiplier = 100_000,
                InterStrokeDelayMilliseconds = 0,
                ColorChangeDelayMilliseconds = 0
            },
            new Progress<DrawingProgress>(progress.Add));

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.Equal(2, result.CompletedStrokes);
        Assert.Contains(progress, item => item.State == DrawingExecutionState.Completed);
        Assert.Contains(input.Events, item => item == "down");
        Assert.Contains(input.Events, item => item == "up");
        Assert.Empty(input.PressedButtons);
        Assert.Equal(new ScreenPoint(10, 20), input.Moves[0]);
    }

    [Fact]
    public async Task ForegroundRequirementStopsBeforeInput()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 100, 100, 96) with
        {
            Snapshot = CreateSnapshot(false) with { ClientWidth = 100, ClientHeight = 100 }
        });
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());

        var result = await executor.ExecuteAsync(
            CreatePlan(),
            new DrawingExecutionOptions
            {
                SpeedMultiplier = 100_000,
                InterStrokeDelayMilliseconds = 0,
                ColorChangeDelayMilliseconds = 0,
                RequireForegroundTarget = true
            });

        Assert.Equal(DrawingExecutionState.Failed, result.State);
        Assert.Empty(input.Moves);
        Assert.Empty(input.PressedButtons);
    }

    [Fact]
    public async Task EmergencyStopReturnsStoppingAndReleasesHeldState()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 100, 100, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController(TimeSpan.FromMilliseconds(5));
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var execution = executor.ExecuteAsync(
            CreateLongPlan(),
            new DrawingExecutionOptions
            {
                SpeedMultiplier = 100_000,
                InterStrokeDelayMilliseconds = 0,
                ColorChangeDelayMilliseconds = 0
            });

        await Task.Delay(25);
        executor.RequestStop();
        var result = await execution;

        Assert.Equal(DrawingExecutionState.Stopping, result.State);
        Assert.Empty(input.PressedButtons);
        Assert.True(input.ReleaseAllButtonCalls > 0);
    }

    [Fact]
    public async Task ForegroundLossDuringStrokeFailsBeforeFurtherInputAndReleasesMouse()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 100, 100, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController(TimeSpan.FromMilliseconds(2));
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var execution = executor.ExecuteAsync(
            CreateLongPlan(),
            new DrawingExecutionOptions
            {
                SpeedMultiplier = 100_000,
                InterStrokeDelayMilliseconds = 0,
                ColorChangeDelayMilliseconds = 0
            });

        await WaitUntilAsync(() => input.Moves.Count >= 3);
        provider.Current = provider.Current with
        {
            Snapshot = provider.Current.Snapshot with { IsForeground = false }
        };
        var result = await execution;

        Assert.Equal(DrawingExecutionState.Failed, result.State);
        Assert.Empty(input.PressedButtons);
        Assert.Contains("포그라운드", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PauseGatePausesBetweenPointsAndResumesSafely()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 100, 100, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController(TimeSpan.FromMilliseconds(2));
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        executor.Pause();

        var execution = executor.ExecuteAsync(
            CreatePlan(),
            new DrawingExecutionOptions
            {
                SpeedMultiplier = 100_000,
                InterStrokeDelayMilliseconds = 0,
                ColorChangeDelayMilliseconds = 0
            });
        await Task.Delay(20);
        Assert.True(executor.IsPaused);
        Assert.Empty(input.Moves);

        executor.Resume();
        var result = await execution;
        Assert.Equal(DrawingExecutionState.Completed, result.State);
    }

    [Fact]
    public async Task VisualPauseRequestBlocksInputUntilCleared()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 100, 100, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        executor.RequestVisualPause("canvas drift");

        var execution = executor.ExecuteAsync(
            CreatePlan(),
            new DrawingExecutionOptions
            {
                SpeedMultiplier = 100_000,
                InterStrokeDelayMilliseconds = 0,
                ColorChangeDelayMilliseconds = 0
            });
        await Task.Delay(20);

        Assert.True(executor.VisualPauseRequested);
        Assert.Equal("canvas drift", executor.VisualPauseReason);
        Assert.Empty(input.Moves);

        executor.ClearVisualPause();
        var result = await execution;
        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.False(executor.VisualPauseRequested);
    }

    [Fact]
    public async Task VisualPauseDuringStrokeImmediatelyReleasesMouseAndResumesSafely()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 100, 100, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController(TimeSpan.FromMilliseconds(2));
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var execution = executor.ExecuteAsync(
            CreateLongPlan(),
            new DrawingExecutionOptions
            {
                SpeedMultiplier = 100_000,
                InterStrokeDelayMilliseconds = 0,
                ColorChangeDelayMilliseconds = 0
            });

        await WaitUntilAsync(() => input.PressedButtons.Count > 0);
        executor.RequestVisualPause("canvas moved");

        Assert.Empty(input.PressedButtons);
        Assert.True(input.ReleaseAllButtonCalls > 0);
        Assert.True(executor.IsPaused);

        executor.ClearVisualPause();
        var result = await execution;
        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.Empty(input.PressedButtons);
    }

    [Fact]
    public async Task ManualPauseIsNotClearedByVisualPauseRecovery()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 100, 100, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());

        executor.Pause();
        executor.RequestVisualPause("canvas moved");
        executor.ClearVisualPause();

        Assert.True(executor.IsPaused);
        executor.Resume();
        Assert.False(executor.IsPaused);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ExecutionHooksRunBeforePlanAndEveryColorGroup()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 100, 100, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        var hooks = new RecordingExecutionHooks();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());

        var result = await executor.ExecuteAsync(
            CreatePlan(),
            new DrawingExecutionOptions
            {
                SpeedMultiplier = 100_000,
                InterStrokeDelayMilliseconds = 0,
                ColorChangeDelayMilliseconds = 0,
                Hooks = hooks
            });

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.Equal(1, hooks.BeforePlanCalls);
        Assert.Equal(new[] { RgbColor.Black, RgbColor.White }, hooks.Colors);
    }

    [Fact]
    public async Task WindowsVerifierRejectsInvalidHandle()
    {
        var verifier = new WindowsTargetVerifier();
        var result = await verifier.VerifyAsync(CreateSnapshot(true) with { Handle = 0 }, DrawingMode.Pixel);

        Assert.False(result.IsSafeToRun);
        Assert.Contains(result.Issues, issue => issue.Code == "TARGET_HANDLE_INVALID");
    }

    private static DrawingPlan CreatePlan()
        => new(
            DrawingMode.Pixel,
            new PixelSize(2, 2),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    new DrawingStroke(new[] { new NormalizedPoint(0, 0), new NormalizedPoint(1, 0) })
                }),
                new DrawingColorGroup(RgbColor.White, new[]
                {
                    new DrawingStroke(new[] { new NormalizedPoint(0, 1), new NormalizedPoint(1, 1) })
                })
            });

    private static DrawingPlan CreateLongPlan()
        => new(
            DrawingMode.Contour,
            new PixelSize(128, 1),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    new DrawingStroke(Enumerable.Range(0, 128)
                        .Select(index => new NormalizedPoint(index / 127d, 0.5)))
                })
            });

    private static TargetWindowGeometry CreateGeometry(int x, int y, int width, int height, uint dpi)
        => new(CreateSnapshot(true) with
        {
            ClientWidth = width,
            ClientHeight = height,
            Dpi = dpi
        }, new ScreenRect(x, y, width, height), dpi);

    private static TargetWindowSnapshot CreateSnapshot(bool foreground)
        => new(1234, "fake-game", "Fake Game", 100, 100, 96, foreground);

    private sealed class SafeVerifier : ITargetVerifier
    {
        public ValueTask<TargetVerificationResult> VerifyAsync(
            TargetWindowSnapshot target,
            DrawingMode mode,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(TargetVerificationResult.Safe());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private sealed class RecordingExecutionHooks : IDrawingExecutionHooks
    {
        public int BeforePlanCalls { get; private set; }

        public List<RgbColor> Colors { get; } = new();

        public ValueTask BeforePlanAsync(DrawingPlan plan, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforePlanCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask BeforeColorGroupAsync(RgbColor color, int colorGroupIndex, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Colors.Add(color);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeGeometryProvider(TargetWindowGeometry initial) : IWindowGeometryProvider
    {
        public TargetWindowGeometry Current { get; set; } = initial;

        public ValueTask<TargetWindowGeometry?> GetGeometryAsync(
            long handle,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<TargetWindowGeometry?>(Current);
        }
    }

    private sealed class RecordingInputController(TimeSpan? delay = null) : IWindowsInputController
    {
        private readonly TimeSpan _delay = delay ?? TimeSpan.Zero;

        public List<string> Events { get; } = new();

        public List<ScreenPoint> Moves { get; } = new();

        public HashSet<InputMouseButton> PressedButtons { get; } = new();

        public int ReleaseAllButtonCalls { get; private set; }

        public async ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default)
        {
            await DelayAsync(cancellationToken);
            Moves.Add(point);
            Events.Add($"move:{point.X},{point.Y}");
        }

        public async ValueTask MouseDownAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
        {
            await DelayAsync(cancellationToken);
            PressedButtons.Add(button);
            Events.Add("down");
        }

        public async ValueTask MouseUpAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
        {
            await DelayAsync(cancellationToken);
            PressedButtons.Remove(button);
            Events.Add("up");
        }

        public async ValueTask ClickAsync(ScreenPoint point, InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
        {
            await MoveToAsync(point, cancellationToken);
            await MouseDownAsync(button, cancellationToken);
            await MouseUpAsync(button, cancellationToken);
        }

        public async ValueTask KeyDownAsync(InputKey key, CancellationToken cancellationToken = default)
            => await DelayAsync(cancellationToken);

        public async ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default)
            => await DelayAsync(cancellationToken);

        public async ValueTask TypeTextAsync(string text, CancellationToken cancellationToken = default)
            => await DelayAsync(cancellationToken);

        public ValueTask ReleaseAllButtonsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseAllButtonCalls++;
            PressedButtons.Clear();
            Events.Add("release-all-buttons");
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAllKeysAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("release-all-keys");
            return ValueTask.CompletedTask;
        }

        private async ValueTask DelayAsync(CancellationToken cancellationToken)
        {
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }
    }
}
