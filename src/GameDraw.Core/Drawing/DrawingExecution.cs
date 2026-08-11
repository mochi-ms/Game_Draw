using System.Diagnostics;
using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;

namespace GameDraw.Core.Drawing;

public sealed class PauseGate
{
    private readonly object _sync = new();
    private TaskCompletionSource<bool> _resume = CompletedSource();

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return !_resume.Task.IsCompleted;
            }
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_resume.Task.IsCompleted)
            {
                _resume = NewSource();
            }
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            _resume.TrySetResult(true);
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return _resume.Task.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource<bool> NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> CompletedSource()
    {
        var source = NewSource();
        source.SetResult(true);
        return source;
    }
}

public sealed record DrawingProgress(
    DrawingExecutionState State,
    RgbColor? CurrentColor,
    int ColorIndex,
    int ColorCount,
    int StrokeIndex,
    int StrokeCount,
    double Percentage,
    TimeSpan Elapsed,
    TimeSpan EstimatedRemaining,
    string? Message = null);

public sealed record DrawingEstimate(
    int StrokeCount,
    int ColorChanges,
    double TravelDistancePixels,
    TimeSpan EstimatedDuration);

public static class DrawingEstimator
{
    public static DrawingEstimate Estimate(DrawingPlan plan, GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);

        var canvasDiagonal = Math.Sqrt((profile.Canvas.Bounds.Width * profile.Canvas.Bounds.Width) + (profile.Canvas.Bounds.Height * profile.Canvas.Bounds.Height));
        var travelPixels = plan.Statistics.NormalizedTravelDistance * canvasDiagonal;
        var movementSeconds = travelPixels / Math.Max(1d, profile.InputSampling.MovementSpeedPixelsPerSecond * Math.Max(0.1d, profile.DrawingSpeed));
        var delayMilliseconds = (plan.Statistics.StrokeCount * (profile.Delays.InterStrokeDelayMs + profile.Delays.ClickDelayMs))
            + (plan.Statistics.ColorChanges * profile.Delays.ColorChangeDelayMs);
        var estimated = TimeSpan.FromSeconds(movementSeconds) + TimeSpan.FromMilliseconds(delayMilliseconds);
        return new DrawingEstimate(plan.Statistics.StrokeCount, plan.Statistics.ColorChanges, travelPixels, estimated);
    }
}

public sealed class DrawingExecutor
{
    public async Task ExecuteAsync(
        DrawingPlan plan,
        GameProfile profile,
        IColorAdapter adapter,
        IInputController input,
        PauseGate pauseGate,
        IProgress<DrawingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(pauseGate);

        var adapterValidation = adapter.Validate(profile.ColorAdapter);
        if (!adapterValidation.IsValid)
        {
            throw new InvalidOperationException($"Color adapter validation failed: {string.Join("; ", adapterValidation.Errors)}");
        }

        var estimate = DrawingEstimator.Estimate(plan, profile);
        var stopwatch = Stopwatch.StartNew();
        var strokeIndex = 0;
        var mouseButtonDown = false;

        Report(progress, new DrawingProgress(DrawingExecutionState.Preparing, null, 0, plan.ColorGroups.Count, 0, estimate.StrokeCount, 0, TimeSpan.Zero, estimate.EstimatedDuration));

        try
        {
            for (var colorIndex = 0; colorIndex < plan.ColorGroups.Count; colorIndex++)
            {
                var group = plan.ColorGroups[colorIndex];
                await pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                await adapter.SelectColorAsync(group.Color, profile.ColorAdapter, input, cancellationToken).ConfigureAwait(false);
                await DelayAsync(profile.Delays.ColorChangeDelayMs, cancellationToken).ConfigureAwait(false);

                foreach (var stroke in group.Strokes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await pauseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    strokeIndex++;
                    var screenPoints = stroke.Points
                        .Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y))
                        .Select(point => profile.Canvas.Bounds.Map(point))
                        .ToArray();
                    if (screenPoints.Length == 0)
                    {
                        continue;
                    }

                    if (screenPoints.Length == 1)
                    {
                        await input.ClickAsync(screenPoints[0], InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var sampled = StrokeSampler.Sample(screenPoints, profile.InputSampling.SampleSpacingPixels);
                        await input.MoveToAsync(sampled[0], cancellationToken).ConfigureAwait(false);
                        await input.MouseDownAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
                        mouseButtonDown = true;
                        var strokeStopwatch = Stopwatch.StartNew();
                        for (var pointIndex = 1; pointIndex < sampled.Count; pointIndex++)
                        {
                            var previous = sampled[pointIndex - 1];
                            var current = sampled[pointIndex];
                            await input.MoveToAsync(current, cancellationToken).ConfigureAwait(false);
                            await DelayForDistanceAsync(previous, current, profile.InputSampling, cancellationToken).ConfigureAwait(false);
                        }

                        var minimumDuration = profile.InputSampling.MinimumStrokeDurationMs;
                        if (strokeStopwatch.ElapsedMilliseconds < minimumDuration)
                        {
                            await DelayAsync((int)(minimumDuration - strokeStopwatch.ElapsedMilliseconds), cancellationToken).ConfigureAwait(false);
                        }

                        await input.MouseUpAsync(InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
                        mouseButtonDown = false;
                    }

                    await DelayAsync(profile.Delays.InterStrokeDelayMs, cancellationToken).ConfigureAwait(false);
                    var percentage = estimate.StrokeCount == 0 ? 1d : strokeIndex / (double)estimate.StrokeCount;
                    var remaining = TimeSpan.FromSeconds(Math.Max(0d, estimate.EstimatedDuration.TotalSeconds - stopwatch.Elapsed.TotalSeconds));
                    Report(progress, new DrawingProgress(DrawingExecutionState.Drawing, group.Color, colorIndex + 1, plan.ColorGroups.Count, strokeIndex, estimate.StrokeCount, percentage, stopwatch.Elapsed, remaining));
                }
            }

            Report(progress, new DrawingProgress(DrawingExecutionState.Completed, null, plan.ColorGroups.Count, plan.ColorGroups.Count, strokeIndex, estimate.StrokeCount, 1d, stopwatch.Elapsed, TimeSpan.Zero, "Drawing completed."));
        }
        catch (OperationCanceledException)
        {
            Report(progress, new DrawingProgress(DrawingExecutionState.Stopping, null, 0, plan.ColorGroups.Count, strokeIndex, estimate.StrokeCount, 0d, stopwatch.Elapsed, TimeSpan.Zero, "Drawing stopped."));
            throw;
        }
        catch (Exception exception)
        {
            Report(progress, new DrawingProgress(DrawingExecutionState.Error, null, 0, plan.ColorGroups.Count, strokeIndex, estimate.StrokeCount, 0d, stopwatch.Elapsed, TimeSpan.Zero, exception.Message));
            throw;
        }
        finally
        {
            if (mouseButtonDown)
            {
                await input.MouseUpAsync(InputMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
            }

            await input.MouseUpAsync(InputMouseButton.Left, CancellationToken.None).ConfigureAwait(false);
            stopwatch.Stop();
        }
    }

    private static async ValueTask DelayForDistanceAsync(ScreenPoint previous, ScreenPoint current, InputSamplingProfile sampling, CancellationToken cancellationToken)
    {
        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var milliseconds = Math.Max(1, (int)Math.Round(distance / Math.Max(1d, sampling.MovementSpeedPixelsPerSecond) * 1000d));
        await DelayAsync(milliseconds, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Report(IProgress<DrawingProgress>? progress, DrawingProgress value) => progress?.Report(value);
}

public static class StrokeSampler
{
    public static IReadOnlyList<ScreenPoint> Sample(IReadOnlyList<ScreenPoint> points, double spacingPixels)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count <= 1)
        {
            return points.ToArray();
        }

        var spacing = Math.Max(1d, spacingPixels);
        var sampled = new List<ScreenPoint> { points[0] };
        for (var index = 1; index < points.Count; index++)
        {
            var start = points[index - 1];
            var end = points[index];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            var steps = Math.Max(1, (int)Math.Ceiling(distance / spacing));
            for (var step = 1; step <= steps; step++)
            {
                var fraction = step / (double)steps;
                sampled.Add(new ScreenPoint(
                    (int)Math.Round(start.X + (dx * fraction)),
                    (int)Math.Round(start.Y + (dy * fraction))));
            }
        }

        return sampled;
    }
}
