using System.Diagnostics;
using GameDraw.Automation.Windows.Targeting;
using GameDraw.Core.Drawing;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;

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

            for (var groupIndex = 0; groupIndex < plan.ColorGroups.Count; groupIndex++)
            {
                var group = plan.ColorGroups[groupIndex];
                await _pauseGate.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                await EnsureTargetAsync(plan, options, token).ConfigureAwait(false);
                if (options.Hooks is not null)
                {
                    await options.Hooks.BeforeColorGroupAsync(group.Color, groupIndex, token).ConfigureAwait(false);
                }

                for (var strokeIndex = 0; strokeIndex < group.Strokes.Count; strokeIndex++)
                {
                    await _pauseGate.WaitAsync(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    await EnsureTargetAsync(plan, options, token).ConfigureAwait(false);
                    await DrawStrokeAsync(group.Strokes[strokeIndex], plan.LogicalSize, options, token).ConfigureAwait(false);
                    completedStrokes++;
                    Volatile.Write(ref _activeCompleted, completedStrokes);
                    Report(progress, DrawingExecutionState.Running, completedStrokes, totalStrokes, started, $"스트로크 {completedStrokes}/{totalStrokes} 완료");
                    await DelayAsync(options.InterStrokeDelayMilliseconds, options.SpeedMultiplier, token).ConfigureAwait(false);
                }

                if (groupIndex < plan.ColorGroups.Count - 1)
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
        DrawingExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (stroke.Points.Count == 0)
        {
            return;
        }

        await EnsureForegroundTargetAsync(options, cancellationToken).ConfigureAwait(false);
        var first = _binding.Map(stroke.Points[0]);
        await _input.MoveToAsync(first, cancellationToken).ConfigureAwait(false);
        await _pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _input.MouseDownAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
        var buttonDown = true;
        var releaseEpoch = Volatile.Read(ref _inputReleaseEpoch);
        try
        {
            var previous = stroke.Points[0];
            for (var index = 1; index < stroke.Points.Count; index++)
            {
                var next = stroke.Points[index];
                await DelayForMovementAsync(previous, next, logicalSize, options, cancellationToken).ConfigureAwait(false);
                await _pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureForegroundTargetAsync(options, cancellationToken).ConfigureAwait(false);
                if (releaseEpoch != Volatile.Read(ref _inputReleaseEpoch))
                {
                    await _input.MouseDownAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
                    releaseEpoch = Volatile.Read(ref _inputReleaseEpoch);
                }

                await _input.MoveToAsync(_binding.Map(next), cancellationToken).ConfigureAwait(false);
                previous = next;
            }

            if (stroke.IsClosed && stroke.Points.Count > 1)
            {
                await DelayForMovementAsync(previous, stroke.Points[0], logicalSize, options, cancellationToken).ConfigureAwait(false);
                await _pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureForegroundTargetAsync(options, cancellationToken).ConfigureAwait(false);
                if (releaseEpoch != Volatile.Read(ref _inputReleaseEpoch))
                {
                    await _input.MouseDownAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
                    releaseEpoch = Volatile.Read(ref _inputReleaseEpoch);
                }

                await _input.MoveToAsync(_binding.Map(stroke.Points[0]), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (buttonDown)
            {
                try
                {
                    await _input.MouseUpAsync(InputMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    await _input.ReleaseAllButtonsAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
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

    private async ValueTask EnsureForegroundTargetAsync(
        DrawingExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (!await _binding.RefreshAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("대상 창이 사라졌거나 클라이언트 좌표를 읽을 수 없습니다.");
        }

        if (options.RequireForegroundTarget && !_binding.Snapshot.IsForeground)
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
