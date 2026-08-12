using System.Diagnostics;
using GameDraw.Automation.Windows.Targeting;
using GameDraw.Core.Drawing;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;

namespace GameDraw.Automation.Windows.Execution;

public sealed class WindowsDrawingExecutor :
    IDrawingExecutor,
    IPauseController,
    IEmergencyStopController,
    IVisualPauseController,
    IDisposable
{
    private readonly IWindowsInputController _input;
    private readonly TargetWindowBinding _binding;
    private readonly ITargetVerifier _targetVerifier;
    private readonly PauseGate _pauseGate = new();
    private readonly object _pauseStateLock = new();
    private int _running;
    private int _stopRequested;
    private CancellationTokenSource? _activeCancellation;
    private IProgress<DrawingProgress>? _activeProgress;
    private long _activeStarted;
    private int _activeCompleted;
    private int _activeTotal;
    private bool _visualPauseRequested;
    private string? _visualPauseReason;
    private bool _manualPauseRequested;
    private long _inputReleaseEpoch;
    private bool _disposed;

    public WindowsDrawingExecutor(
        IWindowsInputController input,
        TargetWindowBinding binding,
        ITargetVerifier? targetVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(binding);
        _input = input;
        _binding = binding;
        _targetVerifier = targetVerifier ?? new Targeting.WindowsTargetVerifier();
    }

    public bool IsPaused => _pauseGate.IsPaused;

    public bool StopRequested => Volatile.Read(ref _stopRequested) != 0;

    public bool VisualPauseRequested
    {
        get
        {
            lock (_pauseStateLock)
            {
                return _visualPauseRequested;
            }
        }
    }

    public string? VisualPauseReason
    {
        get
        {
            lock (_pauseStateLock)
            {
                return _visualPauseReason;
            }
        }
    }

    public void Pause()
    {
        lock (_pauseStateLock)
        {
            _manualPauseRequested = true;
        }

        EnterPausedState("일시 정지되었습니다.");
    }

    private void EnterPausedState(string message)
    {
        _pauseGate.Pause();
        ReleaseInputStateBestEffort();
        if (Volatile.Read(ref _running) != 0 && _activeProgress is not null)
        {
            Report(
                _activeProgress,
                DrawingExecutionState.Paused,
                Volatile.Read(ref _activeCompleted),
                Volatile.Read(ref _activeTotal),
                Volatile.Read(ref _activeStarted),
                message);
        }
    }

    public void Resume()
    {
        lock (_pauseStateLock)
        {
            _manualPauseRequested = false;
            if (_visualPauseRequested)
            {
                return;
            }
        }

        ResumeCore();
    }

    private void ResumeCore()
    {
        _pauseGate.Resume();
        if (Volatile.Read(ref _running) != 0 && _activeProgress is not null)
        {
            Report(
                _activeProgress,
                DrawingExecutionState.Running,
                Volatile.Read(ref _activeCompleted),
                Volatile.Read(ref _activeTotal),
                Volatile.Read(ref _activeStarted),
                "그리기를 재개합니다.");
        }
    }

    public void RequestVisualPause(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_pauseStateLock)
        {
            _visualPauseRequested = true;
            _visualPauseReason = reason;
        }

        EnterPausedState($"시각 안전 검사로 일시 정지했습니다. {reason}");
    }

    public void ClearVisualPause()
    {
        var shouldResume = false;
        lock (_pauseStateLock)
        {
            shouldResume = _visualPauseRequested;
            _visualPauseRequested = false;
            _visualPauseReason = null;
            shouldResume &= !_manualPauseRequested;
        }

        if (shouldResume)
        {
            ResumeCore();
        }
    }

    public void RequestStop()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        _pauseGate.Resume();
        try
        {
            _activeCancellation?.Cancel();
            ReleaseInputStateBestEffort();
        }
        catch
        {
            // A stop request is best effort and must never throw back through a
            // global hotkey callback. ExecuteAsync performs a second release.
        }
    }

    public async Task<DrawingExecutionResult> ExecuteAsync(
        DrawingPlan plan,
        DrawingExecutionOptions options,
        IProgress<DrawingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InvalidOperationException("이미 다른 그리기 실행이 진행 중입니다.");
        }

        Interlocked.Exchange(ref _stopRequested, 0);
        var started = Stopwatch.GetTimestamp();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeCancellation = linkedCancellation;
        _activeProgress = progress;
        _activeStarted = started;
        var token = linkedCancellation.Token;
        var totalStrokes = plan.Statistics.StrokeCount;
        var completedStrokes = 0;
        var lastStrokeProgressReport = started;
        _activeTotal = totalStrokes;
        _activeCompleted = completedStrokes;

        try
        {
            Report(progress, DrawingExecutionState.Preparing, completedStrokes, totalStrokes, started, "대상 창과 좌표를 확인하는 중입니다.");
            await EnsureTargetAsync(plan, options, token).ConfigureAwait(false);

            if (StopRequested)
            {
                return StoppingResult(progress, completedStrokes, totalStrokes, started, "중지 요청으로 실행을 시작하지 않았습니다.");
            }

            Report(progress, DrawingExecutionState.Running, completedStrokes, totalStrokes, started, "그리기를 시작합니다.");
            if (options.Hooks is not null)
            {
                await options.Hooks.BeforePlanAsync(plan, token).ConfigureAwait(false);
            }

            GameDraw.Core.Colors.RgbColor? activeColor = null;
            NormalizedPoint? previousStrokeEnd = null;
            for (var groupIndex = 0; groupIndex < plan.ColorGroups.Count; groupIndex++)
            {
                var group = plan.ColorGroups[groupIndex];
                await _pauseGate.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                await EnsureTargetAsync(plan, options, token).ConfigureAwait(false);
                var colorChanged = activeColor is null || activeColor.Value != group.Color;
                if (options.Hooks is not null && colorChanged)
                {
                    if (plan.ColorGroups.Count > 1)
                    {
                        Report(
                            progress,
                            DrawingExecutionState.Running,
                            completedStrokes,
                            totalStrokes,
                            started,
                            $"색상 {groupIndex + 1}/{plan.ColorGroups.Count} · {group.Color.ToHex()} 적용 중");
                    }

                    await options.Hooks.BeforeColorGroupAsync(group.Color, groupIndex, token).ConfigureAwait(false);
                }
                activeColor = group.Color;
                var firstStrokeAfterColorControl = colorChanged && options.Hooks is not null;

                for (var strokeIndex = 0; strokeIndex < group.Strokes.Count; strokeIndex++)
                {
                    await _pauseGate.WaitAsync(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    await EnsureTargetAsync(plan, options, token).ConfigureAwait(false);
                    if (options.Hooks is not null)
                    {
                        await options.Hooks.BeforeStrokeAsync(
                            group.Strokes[strokeIndex],
                            completedStrokes,
                            token).ConfigureAwait(false);
                    }
                    var stroke = group.Strokes[strokeIndex];
                    var extendedTravelFence = firstStrokeAfterColorControl ||
                        RequiresExtendedTravelFence(previousStrokeEnd, stroke.Points[0], plan.Mode);
                    await DrawStrokeAsync(
                        stroke,
                        plan.LogicalSize,
                        plan.Mode,
                        options,
                        extendedTravelFence,
                        token).ConfigureAwait(false);
                    previousStrokeEnd = stroke.IsClosed ? stroke.Points[0] : stroke.Points[^1];
                    firstStrokeAfterColorControl = false;
                    completedStrokes++;
                    Volatile.Write(ref _activeCompleted, completedStrokes);
                    // WinUI progress dispatch is substantially more expensive
                    // than one local stamp. Five updates per second stays
                    // visually smooth without queueing thousands of UI posts.
                    var progressNow = Stopwatch.GetTimestamp();
                    if (completedStrokes == totalStrokes ||
                        progressNow - lastStrokeProgressReport >= Stopwatch.Frequency / 5)
                    {
                        Report(progress, DrawingExecutionState.Running, completedStrokes, totalStrokes, started, $"스트로크 {completedStrokes}/{totalStrokes} 완료");
                        lastStrokeProgressReport = progressNow;
                    }
                    await DelayAsync(options.InterStrokeDelayMilliseconds, options.SpeedMultiplier, token).ConfigureAwait(false);
                }

                if (groupIndex < plan.ColorGroups.Count - 1 &&
                    plan.ColorGroups[groupIndex + 1].Color != group.Color)
                {
                    await DelayAsync(options.ColorChangeDelayMilliseconds, options.SpeedMultiplier, token).ConfigureAwait(false);
                }
            }

            Report(progress, DrawingExecutionState.Completed, completedStrokes, totalStrokes, started, "그리기가 완료되었습니다.");
            return new DrawingExecutionResult(DrawingExecutionState.Completed, completedStrokes, Elapsed(started));
        }
        catch (OperationCanceledException) when (StopRequested || cancellationToken.IsCancellationRequested)
        {
            return StoppingResult(progress, completedStrokes, totalStrokes, started, "그리기가 중지되었습니다.");
        }
        catch (Exception exception)
        {
            Report(progress, DrawingExecutionState.Failed, completedStrokes, totalStrokes, started, exception.Message);
            return new DrawingExecutionResult(DrawingExecutionState.Failed, completedStrokes, Elapsed(started), exception.Message);
        }
        finally
        {
            try
            {
                await _input.ReleaseAllButtonsAsync(CancellationToken.None).ConfigureAwait(false);
                await _input.ReleaseAllKeysAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Keep cleanup best-effort; the input backend also releases on
                // Dispose and the emergency-stop path performs a direct release.
            }

            _activeCancellation = null;
            _activeProgress = null;
            _activeStarted = 0;
            _activeCompleted = 0;
            _activeTotal = 0;
            lock (_pauseStateLock)
            {
                if (!_manualPauseRequested && !_visualPauseRequested)
                {
                    _pauseGate.Resume();
                }
            }
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        RequestStop();
        _disposed = true;
    }

    private async ValueTask EnsureTargetAsync(
        DrawingPlan plan,
        DrawingExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (!await _binding.RefreshAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("대상 창이 사라졌거나 클라이언트 좌표를 읽을 수 없습니다.");
        }

        var verification = await _targetVerifier.VerifyAsync(
            _binding.Snapshot,
            plan.Mode,
            cancellationToken).ConfigureAwait(false);
        if (!verification.IsSafeToRun)
        {
            throw new InvalidOperationException(string.Join(" ", verification.Issues.Select(issue => issue.Message)));
        }

        if (options.RequireForegroundTarget && !_binding.Snapshot.IsForeground)
        {
            throw new InvalidOperationException("대상 창이 포그라운드가 아니므로 안전을 위해 실행을 중지했습니다.");
        }
    }

    private async ValueTask DrawStrokeAsync(
        DrawingStroke stroke,
        PixelSize logicalSize,
        DrawingMode mode,
        DrawingExecutionOptions options,
        bool extendedTravelFence,
        CancellationToken cancellationToken)
    {
        if (stroke.Points.Count == 0)
        {
            return;
        }

        EnsureForegroundTarget(options);
        var compactRasterDetail = IsCompactRasterDetail(stroke, mode);
        var first = _binding.Map(stroke.Points[0]);
        // Roblox samples the pen state once per rendered frame. Keep an
        // explicit button-up frame before moving; batching MouseUp and Move in
        // one SendInput call can still make the game draw a connector to the
        // new coordinate using its previous-frame pen state.
        var startGuardMilliseconds = extendedTravelFence
            ? Math.Max(42, options.StrokeStartSettleMilliseconds)
            : options.StrokeStartSettleMilliseconds;
        var startConfirmationCount = extendedTravelFence
            ? Math.Max(2, options.StrokeStartReleaseConfirmationCount)
            : options.StrokeStartReleaseConfirmationCount;
        var startGuardIntervals = startConfirmationCount + 1;
        var startGuardInterval = startGuardMilliseconds / startGuardIntervals;
        var startGuardElapsed = 0;
        await _input.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
        for (var confirmation = 0;
             confirmation < startConfirmationCount;
             confirmation++)
        {
            await DelayUnscaledAsync(startGuardInterval, cancellationToken).ConfigureAwait(false);
            startGuardElapsed += startGuardInterval;
            await _input.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
        }
        await DelayUnscaledAsync(
            startGuardMilliseconds - startGuardElapsed,
            cancellationToken).ConfigureAwait(false);
        // Never perform a disconnected positioning move while Roblox may
        // still own a Lua-side drag capture. Far jumps receive a full native
        // cancel/focus boundary; every other jump uses an ordered up+move
        // batch after at least one released render frame.
        if (extendedTravelFence)
        {
            await _input.RepositionWithCaptureResetAsync(
                _binding.Snapshot.Handle,
                first,
                cancellationToken).ConfigureAwait(false);
            EnsureForegroundTarget(options);
        }
        else
        {
            await _input.MoveWithButtonsReleasedAsync(first, cancellationToken).ConfigureAwait(false);
        }
        await DelayUnscaledAsync(
            extendedTravelFence
                ? Math.Max(8, options.StrokeStartSettleMilliseconds)
                : Math.Max(4, options.StrokeStartSettleMilliseconds),
            cancellationToken).ConfigureAwait(false);
        await _pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _input.MouseDownAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
        var penDownStarted = Stopwatch.GetTimestamp();
        // A tiny pupil/highlight can contain several valid plan points while
        // occupying only a few screen pixels. At maximum speed those points
        // previously arrived inside one Roblox render frame, so the first dot
        // appeared but the rest of the feature was silently coalesced. Pace
        // compact raster details only; large printer passes keep full speed.
        await DelayUnscaledAsync(
            compactRasterDetail
                ? Math.Max(18, options.PenDownSettleMilliseconds)
                : options.PenDownSettleMilliseconds,
            cancellationToken).ConfigureAwait(false);
        var buttonDown = true;
        var releaseEpoch = Volatile.Read(ref _inputReleaseEpoch);
        var continuousDistance = 0d;
        try
        {
            var previous = stroke.Points[0];
            for (var index = 1; index < stroke.Points.Count; index++)
            {
                var next = stroke.Points[index];
                await MovePenSegmentAsync(previous, next).ConfigureAwait(false);
                previous = next;
            }

            if (stroke.IsClosed && stroke.Points.Count > 1)
            {
                await MovePenSegmentAsync(previous, stroke.Points[0]).ConfigureAwait(false);
            }

            async ValueTask MovePenSegmentAsync(NormalizedPoint from, NormalizedPoint to)
            {
                await DelayForMovementAsync(from, to, logicalSize, options, cancellationToken).ConfigureAwait(false);
                var screenFrom = _binding.Map(from);
                var screenTo = _binding.Map(to);
                var deltaX = screenTo.X - screenFrom.X;
                var deltaY = screenTo.Y - screenFrom.Y;
                var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                var steps = Math.Max(1, (int)Math.Ceiling(distance / options.MaximumMoveStepPixels));
                for (var step = 1; step <= steps; step++)
                {
                    await _pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureForegroundTarget(options);
                    if (releaseEpoch != Volatile.Read(ref _inputReleaseEpoch))
                    {
                        await _input.MouseDownAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
                        releaseEpoch = Volatile.Read(ref _inputReleaseEpoch);
                    }

                    var amount = step / (double)steps;
                    var target = new ScreenPoint(
                        (int)Math.Round(screenFrom.X + (deltaX * amount)),
                        (int)Math.Round(screenFrom.Y + (deltaY * amount)));
                    var priorAmount = (step - 1) / (double)steps;
                    var prior = new ScreenPoint(
                        (int)Math.Round(screenFrom.X + (deltaX * priorAmount)),
                        (int)Math.Round(screenFrom.Y + (deltaY * priorAmount)));
                    if (compactRasterDetail)
                    {
                        // 12 ms is long enough to prevent a complete compact
                        // path from collapsing into one native cursor sample,
                        // without applying frame pacing to the full image.
                        await DelayUnscaledAsync(12, cancellationToken).ConfigureAwait(false);
                    }

                    await _input.MoveToAsync(target, cancellationToken).ConfigureAwait(false);
                    var stepX = target.X - prior.X;
                    var stepY = target.Y - prior.Y;
                    continuousDistance += Math.Sqrt((stepX * stepX) + (stepY * stepY));
                    if (continuousDistance >= options.MaximumContinuousPenDownDistancePixels &&
                        (step < steps || to != stroke.Points[^1] || stroke.IsClosed))
                    {
                        // Break at the current coordinate. Even if Roblox
                        // samples the release late, there is no repositioning
                        // movement that could draw an unrelated connector.
                        await _input.MouseUpAsync(InputMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
                        var breakDelay = Math.Max(9, Math.Min(18, options.PenUpSettleMilliseconds));
                        await DelayUnscaledAsync(breakDelay, CancellationToken.None).ConfigureAwait(false);
                        await _input.MouseUpAsync(InputMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
                        await DelayUnscaledAsync(breakDelay, CancellationToken.None).ConfigureAwait(false);
                        await _input.MouseUpAsync(InputMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
                        await _input.MouseDownAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
                        penDownStarted = Stopwatch.GetTimestamp();
                        await DelayUnscaledAsync(options.PenDownSettleMilliseconds, cancellationToken).ConfigureAwait(false);
                        continuousDistance = 0d;
                    }
                }
            }
        }
        finally
        {
            if (buttonDown)
            {
                try
                {
                    if (!cancellationToken.IsCancellationRequested && !StopRequested)
                    {
                        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - penDownStarted) *
                            1000d / Stopwatch.Frequency;
                        // A stationary one-point detail must span a complete
                        // 30 Hz frame as well. Otherwise Windows can report a
                        // successful down/up pair that Podiums never paints.
                        var minimumPenDownMilliseconds = compactRasterDetail
                            ? Math.Max(42, options.MinimumPenDownMilliseconds)
                            : options.MinimumPenDownMilliseconds;
                        var remaining = Math.Max(0, minimumPenDownMilliseconds - elapsedMilliseconds);
                        if (remaining > 0d)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(remaining), CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }

                    await _input.MouseUpAsync(InputMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
                    if (!cancellationToken.IsCancellationRequested && !StopRequested)
                    {
                        // Keep the released cursor stationary for the complete
                        // guard interval. Optional confirmation events are used
                        // by long-drag modes; rapid dots need no redundant input
                        // because there was no movement while the button was down.
                        if (options.PenUpSettleMilliseconds > 0)
                        {
                            var intervalCount = options.AdditionalPenUpConfirmationCount + 1;
                            var interval = options.PenUpSettleMilliseconds / intervalCount;
                            var elapsed = 0;
                            for (var confirmation = 0;
                                 confirmation < options.AdditionalPenUpConfirmationCount;
                                 confirmation++)
                            {
                                await DelayUnscaledAsync(interval, CancellationToken.None).ConfigureAwait(false);
                                elapsed += interval;
                                await _input.MouseUpAsync(InputMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
                            }

                            await DelayUnscaledAsync(
                                options.PenUpSettleMilliseconds - elapsed,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                    }

                    // This is unconditional and bypasses the rate limiter.
                    // It also fixes stop/pause recovery when local state was
                    // cleared but Roblox missed the earlier release event.
                    await _input.ReleaseAllButtonsAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    await _input.ReleaseAllButtonsAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    private bool IsCompactRasterDetail(DrawingStroke stroke, DrawingMode mode)
    {
        var rasterPrinterMode = mode is
            DrawingMode.SafeStamp or
            DrawingMode.HalftoneStamp or
            DrawingMode.Pixel or
            DrawingMode.SmartFill;
        if (!rasterPrinterMode || stroke.ToolAction == DrawingToolAction.Fill || stroke.Points.Count > 96)
        {
            return false;
        }

        var first = _binding.Map(stroke.Points[0]);
        var minimumX = first.X;
        var maximumX = first.X;
        var minimumY = first.Y;
        var maximumY = first.Y;
        for (var index = 1; index < stroke.Points.Count; index++)
        {
            var point = _binding.Map(stroke.Points[index]);
            minimumX = Math.Min(minimumX, point.X);
            maximumX = Math.Max(maximumX, point.X);
            minimumY = Math.Min(minimumY, point.Y);
            maximumY = Math.Max(maximumY, point.Y);
            if (maximumX - minimumX > 28 || maximumY - minimumY > 28)
            {
                return false;
            }
        }

        return true;
    }

    private bool RequiresExtendedTravelFence(
        NormalizedPoint? previousStrokeEnd,
        NormalizedPoint nextStrokeStart,
        DrawingMode mode)
    {
        if (previousStrokeEnd is not { } previous)
        {
            return false;
        }

        var from = _binding.Map(previous);
        var to = _binding.Map(nextStrokeStart);
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;
        var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
        // Vector/artist strokes are intentionally disconnected. Give every
        // visible reposition a genuine focus/capture boundary so a missed
        // Roblox InputEnded can never connect two preview strokes. Raster
        // printer modes contain many brush-local fragments; they retain the
        // fast path only inside a tiny 12px neighbourhood.
        var rasterPrinterMode = mode is
            DrawingMode.SafeStamp or
            DrawingMode.HalftoneStamp or
            DrawingMode.Pixel or
            DrawingMode.SmartFill or
            DrawingMode.HorizontalScanline or
            DrawingMode.VerticalScanline;
        var threshold = rasterPrinterMode ? 12 : 2;
        return distanceSquared >= threshold * threshold;
    }

    private static async ValueTask DelayForMovementAsync(
        NormalizedPoint first,
        NormalizedPoint second,
        PixelSize logicalSize,
        DrawingExecutionOptions options,
        CancellationToken cancellationToken)
    {
        // Ultra-fast mode still emits every planned mouse point; the input
        // controller's event-rate limiter provides the pacing. Avoiding one
        // Task.Delay per point removes the dominant scheduler overhead while
        // preserving the exact path shown in the preview.
        if (options.SpeedMultiplier >= 8d)
        {
            return;
        }

        var x = (second.X - first.X) * logicalSize.Width;
        var y = (second.Y - first.Y) * logicalSize.Height;
        var logicalPixels = Math.Sqrt((x * x) + (y * y));
        var seconds = logicalPixels / (options.MovementPixelsPerSecond * options.SpeedMultiplier);
        if (seconds > 0d)
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask DelayAsync(
        int milliseconds,
        double speedMultiplier,
        CancellationToken cancellationToken)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(milliseconds / speedMultiplier), cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask DelayUnscaledAsync(
        int milliseconds,
        CancellationToken cancellationToken)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken).ConfigureAwait(false);
        }
    }

    private static DrawingExecutionResult StoppingResult(
        IProgress<DrawingProgress>? progress,
        int completedStrokes,
        int totalStrokes,
        long started,
        string message)
    {
        Report(progress, DrawingExecutionState.Stopping, completedStrokes, totalStrokes, started, message);
        return new DrawingExecutionResult(DrawingExecutionState.Stopping, completedStrokes, Elapsed(started), message);
    }

    private static void Report(
        IProgress<DrawingProgress>? progress,
        DrawingExecutionState state,
        int completedStrokes,
        int totalStrokes,
        long started,
        string message)
    {
        if (progress is null)
        {
            return;
        }

        var elapsed = Elapsed(started);
        var fraction = totalStrokes == 0 ? 1d : completedStrokes / (double)totalStrokes;
        TimeSpan? remaining = null;
        if (fraction > 0d && fraction < 1d)
        {
            remaining = TimeSpan.FromSeconds(elapsed.TotalSeconds * ((1d - fraction) / fraction));
        }

        progress.Report(new DrawingProgress(
            state,
            fraction,
            completedStrokes,
            totalStrokes,
            elapsed,
            remaining,
            message));
    }

    private static TimeSpan Elapsed(long started)
        => TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureForegroundTarget(DrawingExecutionOptions options)
    {
        if (options.RequireForegroundTarget && !_binding.IsForegroundNow())
        {
            throw new InvalidOperationException("대상 창이 포그라운드가 아니므로 입력을 즉시 중단했습니다.");
        }
    }

    private void ReleaseInputStateBestEffort()
    {
        Interlocked.Increment(ref _inputReleaseEpoch);
        try
        {
            _input.ReleaseAllButtonsAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            _input.ReleaseAllKeysAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Pause/stop callbacks must never throw. ExecuteAsync performs a
            // final cleanup pass when control returns to the execution loop.
        }
    }
}
