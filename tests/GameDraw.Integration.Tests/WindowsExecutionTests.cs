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
    public async Task ExecutorKeepsEachStrokeDownLongEnoughForAGameFrame()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(10, 20, 100, 100, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var started = System.Diagnostics.Stopwatch.StartNew();

        var result = await executor.ExecuteAsync(
            CreatePlan(),
            new DrawingExecutionOptions
            {
                SpeedMultiplier = 100_000,
                InterStrokeDelayMilliseconds = 0,
                ColorChangeDelayMilliseconds = 0,
                StrokeStartSettleMilliseconds = 5,
                PenDownSettleMilliseconds = 2,
                MinimumPenDownMilliseconds = 20,
                PenUpSettleMilliseconds = 10
            });

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds(60));
        var firstUp = input.Events.IndexOf("up");
        var nextMove = input.Events.FindIndex(firstUp + 1, item => item.StartsWith("move:", StringComparison.Ordinal));
        Assert.True(firstUp >= 0 && nextMove > firstUp);
        Assert.True(input.Events.Take(nextMove).Count(item => item == "up") >= 3);
    }

    [Fact]
    public async Task ExecutorInterpolatesLongVectorSegmentsForGameSampling()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 101, 101, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var plan = new DrawingPlan(
            DrawingMode.CleanStroke,
            new PixelSize(101, 101),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    new DrawingStroke(new[] { new NormalizedPoint(0, 0.5), new NormalizedPoint(1, 0.5) })
                })
            });

        var result = await executor.ExecuteAsync(plan, new DrawingExecutionOptions
        {
            SpeedMultiplier = 100_000,
            InterStrokeDelayMilliseconds = 0,
            ColorChangeDelayMilliseconds = 0,
            MaximumMoveStepPixels = 10
        });

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.True(input.Moves.Count >= 11, $"Expected interpolated movement but received {input.Moves.Count} moves.");
        Assert.Equal(new ScreenPoint(100, 50), input.Moves[^1]);
        Assert.True(provider.GeometryReads < input.Moves.Count);
        Assert.True(provider.ForegroundReads >= input.Moves.Count);
    }

    [Fact]
    public async Task ExecutorUsesReleasedOrCaptureResetPositioningBeforeEveryStampMove()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 101, 101, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());

        var plan = new DrawingPlan(
            DrawingMode.SafeStamp,
            new PixelSize(3, 1),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    new DrawingStroke(new[] { new NormalizedPoint(0.1, 0.5) }),
                    new DrawingStroke(new[] { new NormalizedPoint(0.5, 0.5) }),
                    new DrawingStroke(new[] { new NormalizedPoint(0.9, 0.5) })
                })
            });
        var result = await executor.ExecuteAsync(plan, new DrawingExecutionOptions
        {
            SpeedMultiplier = 100_000,
            InterStrokeDelayMilliseconds = 0,
            ColorChangeDelayMilliseconds = 0,
            StrokeStartSettleMilliseconds = 0,
            StrokeStartReleaseConfirmationCount = 2,
            PenUpSettleMilliseconds = 5,
            AdditionalPenUpConfirmationCount = 0,
            MaximumContinuousPenDownDistancePixels = 512
        });

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        var moveEvents = input.Events
            .Select((item, index) => (item, index))
            .Where(pair => pair.item.StartsWith("move:", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(plan.Statistics.StrokeCount, moveEvents.Length);
        Assert.All(moveEvents, pair =>
        {
            Assert.True(pair.index > 0);
            var boundary = input.Events[pair.index - 1];
            Assert.True(
                boundary == "atomic-up-move" || boundary.StartsWith("reset-pointer-capture:", StringComparison.Ordinal),
                $"Move at event {pair.index} was not preceded by a released boundary: {boundary}");
        });
        Assert.True(input.Events.Count(item => item == "release-all-buttons") >= plan.Statistics.StrokeCount * 3);
        Assert.Equal(plan.Statistics.StrokeCount, input.Events.Count(item => item == "up"));
    }

    [Fact]
    public async Task ExecutorKeepsACompactRasterDetailDownAcrossACompleteFrame()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 201, 201, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var plan = new DrawingPlan(
            DrawingMode.SafeStamp,
            new PixelSize(201, 201),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    new DrawingStroke(new[]
                    {
                        new NormalizedPoint(0.50, 0.50),
                        new NormalizedPoint(0.52, 0.50)
                    })
                })
            });

        var result = await executor.ExecuteAsync(plan, new DrawingExecutionOptions
        {
            SpeedMultiplier = 100_000,
            InterStrokeDelayMilliseconds = 0,
            ColorChangeDelayMilliseconds = 0,
            StrokeStartSettleMilliseconds = 0,
            PenDownSettleMilliseconds = 0,
            PenUpSettleMilliseconds = 0,
            MinimumPenDownMilliseconds = 0,
            MaximumMoveStepPixels = 64,
            MaximumContinuousPenDownDistancePixels = 512
        });

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.True(result.Duration >= TimeSpan.FromMilliseconds(35), $"Compact detail completed too quickly: {result.Duration}.");
        var down = input.Events.IndexOf("down");
        var detailMove = input.Events.FindIndex(down + 1, item => item.StartsWith("move:", StringComparison.Ordinal));
        var up = input.Events.FindIndex(detailMove + 1, item => item == "up");
        Assert.True(down >= 0 && detailMove > down && up > detailMove);
    }

    [Fact]
    public async Task ExecutorResetsNativeCaptureBeforeEveryFarDisconnectedStroke()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 201, 201, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var plan = new DrawingPlan(
            DrawingMode.ArtistStroke,
            new PixelSize(201, 201),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    new DrawingStroke(new[] { new NormalizedPoint(0.1, 0.1), new NormalizedPoint(0.2, 0.2) }),
                    new DrawingStroke(new[] { new NormalizedPoint(0.8, 0.8), new NormalizedPoint(0.9, 0.9) })
                })
            });

        var result = await executor.ExecuteAsync(plan, new DrawingExecutionOptions
        {
            SpeedMultiplier = 100_000,
            InterStrokeDelayMilliseconds = 0,
            PenUpSettleMilliseconds = 0
        });

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.Equal(1, input.PointerCaptureResetCalls);
        var reset = input.Events.IndexOf("reset-pointer-capture:1234");
        var secondMove = input.Events.FindIndex(reset + 1, item => item == "move:160,160");
        Assert.True(reset >= 0 && secondMove > reset);
        Assert.DoesNotContain("down", input.Events.Skip(reset + 1).Take(secondMove - reset - 1));
    }

    [Fact]
    public async Task ArtistModeResetsCaptureBetweenNearbyDisconnectedLines()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 201, 201, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var plan = new DrawingPlan(
            DrawingMode.ArtistStroke,
            new PixelSize(201, 201),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    new DrawingStroke(new[] { new NormalizedPoint(0.40, 0.40), new NormalizedPoint(0.45, 0.40) }),
                    new DrawingStroke(new[] { new NormalizedPoint(0.46, 0.42), new NormalizedPoint(0.51, 0.42) })
                })
            });

        var result = await executor.ExecuteAsync(plan, new DrawingExecutionOptions
        {
            SpeedMultiplier = 100_000,
            InterStrokeDelayMilliseconds = 0,
            PenUpSettleMilliseconds = 0
        });

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.Equal(1, input.PointerCaptureResetCalls);
        Assert.Equal(1, input.AtomicPositioningCalls);
    }

    [Fact]
    public async Task SafeStampModeResetsCaptureBetweenNearbyDisconnectedStrokes()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 201, 201, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var plan = new DrawingPlan(
            DrawingMode.SafeStamp,
            new PixelSize(201, 201),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    new DrawingStroke(new[] { new NormalizedPoint(0.40, 0.40) }),
                    new DrawingStroke(new[] { new NormalizedPoint(0.45, 0.40) })
                })
            });

        var result = await executor.ExecuteAsync(plan, new DrawingExecutionOptions
        {
            SpeedMultiplier = 100_000,
            InterStrokeDelayMilliseconds = 0,
            PenUpSettleMilliseconds = 0
        });

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.Equal(1, input.PointerCaptureResetCalls);
        Assert.Equal(1, input.AtomicPositioningCalls);
        Assert.True(input.ReleaseAllButtonCalls >= plan.Statistics.StrokeCount * 2);
        Assert.Empty(input.PressedButtons);
    }

    [Fact]
    public async Task ExecutorBreaksLongPenDownAtTheSameCoordinate()
    {
        var provider = new FakeGeometryProvider(CreateGeometry(0, 0, 101, 101, 96));
        var binding = new TargetWindowBinding(provider.Current, provider);
        var input = new RecordingInputController();
        using var executor = new WindowsDrawingExecutor(input, binding, new SafeVerifier());
        var plan = new DrawingPlan(
            DrawingMode.ArtistStroke,
            new PixelSize(101, 101),
            new[]
            {
                new DrawingColorGroup(RgbColor.Black, new[]
                {
                    new DrawingStroke(new[] { new NormalizedPoint(0, 0.5), new NormalizedPoint(1, 0.5) })
                })
            });

        var result = await executor.ExecuteAsync(plan, new DrawingExecutionOptions
        {
            SpeedMultiplier = 100_000,
            InterStrokeDelayMilliseconds = 0,
            ColorChangeDelayMilliseconds = 0,
            MaximumMoveStepPixels = 5,
            MaximumContinuousPenDownDistancePixels = 20,
            PenUpSettleMilliseconds = 0
        });

        Assert.Equal(DrawingExecutionState.Completed, result.State);
        Assert.True(input.Events.Count(item => item == "down") >= 4);
        Assert.True(input.Events.Count(item => item == "up") >= 6);
        Assert.Equal(new ScreenPoint(100, 50), input.Moves[^1]);
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
        Assert.Equal(2, hooks.BeforeStrokeCalls);
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

        public int BeforeStrokeCalls { get; private set; }

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

        public ValueTask BeforeStrokeAsync(
            DrawingStroke stroke,
            int strokeIndex,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeStrokeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeGeometryProvider(TargetWindowGeometry initial) : IWindowGeometryProvider, IForegroundWindowProbe
    {
        public TargetWindowGeometry Current { get; set; } = initial;

        public int GeometryReads { get; private set; }

        public int ForegroundReads { get; private set; }

        public bool IsForeground(long handle)
        {
            ForegroundReads++;
            return Current.Snapshot.Handle == handle && Current.Snapshot.IsForeground;
        }

        public ValueTask<TargetWindowGeometry?> GetGeometryAsync(
            long handle,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GeometryReads++;
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

        public int AtomicPositioningCalls { get; private set; }

        public int PointerCaptureResetCalls { get; private set; }

        public async ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default)
        {
            await DelayAsync(cancellationToken);
            Moves.Add(point);
            Events.Add($"move:{point.X},{point.Y}");
        }

        public async ValueTask MoveWithButtonsReleasedAsync(
            ScreenPoint point,
            CancellationToken cancellationToken = default)
        {
            await ReleaseAllButtonsAsync(cancellationToken);
            AtomicPositioningCalls++;
            Events.Add("atomic-up-move");
            await MoveToAsync(point, cancellationToken);
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

        public ValueTask ResetPointerCaptureAsync(
            long targetWindowHandle,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PointerCaptureResetCalls++;
            Events.Add($"reset-pointer-capture:{targetWindowHandle}");
            PressedButtons.Clear();
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
