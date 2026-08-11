using GameDraw.Automation.Windows.Capture;
using GameDraw.Automation.Windows.Execution;
using GameDraw.Automation.Windows.Input;
using GameDraw.Automation.Windows.Targeting;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Core.Targeting;
using GameDraw.Core.Vision;
using GameDraw.GameAdapters;
using GameDraw.GameAdapters.Podiums;
using GameDraw.GameAdapters.Podiums.Calibration;
using GameDraw.Imaging.Decoding;
using GameDraw.Imaging.Palettes;
using GameDraw.Imaging.Processing;
using GameDraw.Imaging.Quantization;
using GameDraw.Planning;
using GameDraw.Profiles;

namespace GameDraw_App.Services;

public sealed record PreparedDrawing(
    string SourcePath,
    ImageProcessingResult Image,
    DrawingPlanningResult Planning,
    int RequestedColors)
{
    public string Summary =>
        $"{Planning.Plan.LogicalSize.Width}×{Planning.Plan.LogicalSize.Height} · " +
        $"{Planning.Estimate.ColorCount}색 · {Planning.Estimate.StrokeCount:N0}스트로크 · " +
        $"예상 {FormatDuration(Planning.Estimate.EstimatedDuration)}";

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? $"{duration.TotalHours:0.#}시간"
            : duration.TotalMinutes >= 1
                ? $"{duration.TotalMinutes:0.#}분"
                : $"{duration.TotalSeconds:0.#}초";
}

public sealed class DrawingSessionController : IDisposable
{
    private readonly ImageDecoder _decoder = new();
    private readonly ImageProcessingPipeline _imaging = new();
    private readonly DrawingPlanner _planner = new();
    private readonly WindowsWindowLocator _windowLocator = new();
    private readonly WindowsWindowGeometryProvider _geometryProvider = new();
    private readonly WindowsCursorPositionProvider _cursor = new();
    private readonly WindowsWindowCapture _capture = new();
    private readonly PodiumsGameAdapter _adapter = new();
    private readonly JsonGameProfileStore _profileStore;
    private readonly ProfileTransferService _profileTransfer = new();
    private readonly object _executionLock = new();
    private WindowsDrawingExecutor? _activeExecutor;
    private WindowsInputController? _activeInput;
    private bool _disposed;

    public DrawingSessionController()
    {
        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameDraw",
            "profiles.json");
        _profileStore = new JsonGameProfileStore(profilePath);
        CurrentProfile = _adapter.CreateDefaultProfile();
    }

    public GameProfile CurrentProfile { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_executionLock)
            {
                return _activeExecutor is not null;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_executionLock)
            {
                return _activeExecutor?.IsPaused == true;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var profiles = await _profileStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        CurrentProfile = profiles.FirstOrDefault(profile =>
            string.Equals(profile.GameName, "Podiums", StringComparison.OrdinalIgnoreCase))
            ?? _adapter.CreateDefaultProfile();
    }

    public async Task<GameProfile> SaveCalibrationAsync(
        PodiumsCalibrationResult result,
        string? profileName = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            throw new ArgumentException(result.ErrorMessage ?? "Podiums 캘리브레이션이 완료되지 않았습니다.", nameof(result));
        }

        var profile = PodiumsProfileSettings.ApplyControlLayout(CurrentProfile, result.Controls) with
        {
            Name = string.IsNullOrWhiteSpace(profileName) ? CurrentProfile.Name : profileName.Trim(),
            Canvas = result.Canvas
        };
        await _profileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        CurrentProfile = profile;
        return profile;
    }

    public Task ExportCurrentProfileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _profileTransfer.ExportAsync(CurrentProfile, path, cancellationToken);
    }

    public async Task<GameProfile> ImportProfileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var profile = await _profileTransfer.ImportAsync(path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(profile.GameName, "Podiums", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("현재 화면에서는 Podiums 프로필만 가져올 수 있습니다.");
        }

        await _profileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        CurrentProfile = profile;
        return profile;
    }

    public async Task<TargetWindowSnapshot?> FindPodiumsTargetAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var candidates = await _windowLocator.GetCandidatesAsync(cancellationToken).ConfigureAwait(false);
        var matcher = CurrentProfile.Window;
        return candidates
            .Where(candidate => string.IsNullOrWhiteSpace(matcher.ProcessName) ||
                string.Equals(
                    Path.GetFileNameWithoutExtension(candidate.ProcessName),
                    Path.GetFileNameWithoutExtension(matcher.ProcessName),
                    StringComparison.OrdinalIgnoreCase))
            .Where(candidate => string.IsNullOrWhiteSpace(matcher.TitleContains) ||
                candidate.Title.Contains(matcher.TitleContains, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.IsForeground)
            .ThenByDescending(candidate => candidate.ClientWidth * candidate.ClientHeight)
            .FirstOrDefault();
    }

    public async Task<NormalizedPoint?> CaptureCursorInTargetAsync(
        TargetWindowSnapshot target,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        var geometry = await _geometryProvider.GetGeometryAsync(target.Handle, cancellationToken).ConfigureAwait(false);
        if (geometry is null || !_cursor.TryGetScreenPosition(out var cursor))
        {
            return null;
        }

        var bounds = geometry.ClientBounds;
        var x = (cursor.X - bounds.X) / (double)Math.Max(1, bounds.Width - 1);
        var y = (cursor.Y - bounds.Y) / (double)Math.Max(1, bounds.Height - 1);
        var normalized = new NormalizedPoint(x, y);
        return normalized.IsWithinUnitSquare ? normalized : null;
    }

    public async Task<PreparedDrawing> PrepareAsync(
        string sourcePath,
        DrawingMode mode,
        int maximumColors,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("이미지 경로가 필요합니다.", nameof(sourcePath));
        }

        if (maximumColors is < 2 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumColors), "색상 수는 2~256이어야 합니다.");
        }

        status?.Report("원본 이미지를 디코딩하는 중입니다…");
        var decoded = await _decoder.DecodeFileAsync(sourcePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var target = FitWithin(
            new PixelSize(decoded.Frame.Width, decoded.Frame.Height),
            CurrentProfile.Canvas.IsCalibrated
                ? new PixelSize(CurrentProfile.Canvas.LogicalWidth, CurrentProfile.Canvas.LogicalHeight)
                : new PixelSize(512, 512));

        status?.Report("색상과 해상도를 최적화하는 중입니다…");
        var processingOptions = new ImageProcessingOptions
        {
            TargetSize = target,
            Palette = new PaletteBuildOptions
            {
                MaxColors = maximumColors,
                MaxSamples = 250_000
            },
            Quantization = new QuantizationOptions
            {
                DitherMode = DitherMode.OrderedBayer4,
                PreserveAlpha = true
            }
        };
        var image = await Task.Run(
            () => _imaging.ProcessFrame(
                decoded.Frame,
                processingOptions,
                sourcePath,
                decoded.FormatName,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        status?.Report("그리기 경로와 예상 시간을 계산하는 중입니다…");
        var plannerOptions = new DrawingPlannerOptions
        {
            Mode = mode,
            MovementPixelsPerSecond = CurrentProfile.Timing.MovementPixelsPerSecond,
            InterStrokeDelayMilliseconds = CurrentProfile.Timing.InterStrokeDelayMilliseconds,
            ColorChangeDelayMilliseconds = CurrentProfile.Timing.ColorChangeDelayMilliseconds
        };
        var planning = await Task.Run(
            () => _planner.Plan(image.Quantized, plannerOptions),
            cancellationToken).ConfigureAwait(false);
        return new PreparedDrawing(sourcePath, image, planning, maximumColors);
    }

    public async Task<DrawingExecutionResult> ExecuteAsync(
        PreparedDrawing prepared,
        IProgress<DrawingProgress>? progress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(prepared);
        if (!CurrentProfile.Canvas.IsCalibrated)
        {
            throw new InvalidOperationException("먼저 Podiums 프로필을 캘리브레이션하세요.");
        }

        lock (_executionLock)
        {
            if (_activeExecutor is not null)
            {
                throw new InvalidOperationException("이미 그리기 실행이 진행 중입니다.");
            }
        }

        status?.Report("15초 안에 Roblox Podiums 창으로 전환하세요. F8은 언제든 즉시 중지합니다.");
        var target = await WaitForForegroundTargetAsync(TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);
        var geometry = await _geometryProvider.GetGeometryAsync(target.Handle, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Roblox 창 좌표를 읽지 못했습니다.");
        var verification = await _adapter.VerifyAsync(target, CurrentProfile, cancellationToken).ConfigureAwait(false);
        if (!verification.IsSafeToRun)
        {
            throw new InvalidOperationException(string.Join(" ", verification.Issues.Select(issue => issue.Message)));
        }

        status?.Report("Podiums 캔버스가 현재 위치에 있는지 확인하는 중입니다…");
        await VerifyVisualPreflightAsync(target, cancellationToken).ConfigureAwait(false);

        var binding = new TargetWindowBinding(geometry, _geometryProvider, CurrentProfile.Canvas.Bounds);
        var input = new WindowsInputController();
        var executor = new WindowsDrawingExecutor(input, binding);
        var context = new GameAdapterExecutionContext(input, target, binding.MapClient);
        var hooks = new PodiumsExecutionHooks(CurrentProfile, context);
        lock (_executionLock)
        {
            _activeInput = input;
            _activeExecutor = executor;
        }

        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitor = MonitorVisualSafetyAsync(binding, executor, status, monitorCancellation.Token);
        try
        {
            return await executor.ExecuteAsync(
                prepared.Planning.Plan,
                new DrawingExecutionOptions
                {
                    MovementPixelsPerSecond = CurrentProfile.Timing.MovementPixelsPerSecond,
                    InterStrokeDelayMilliseconds = CurrentProfile.Timing.InterStrokeDelayMilliseconds,
                    ColorChangeDelayMilliseconds = CurrentProfile.Timing.ColorChangeDelayMilliseconds,
                    Hooks = hooks,
                    RequireForegroundTarget = true
                },
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            monitorCancellation.Cancel();
            try
            {
                await monitor.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when execution finishes before the next capture.
            }

            lock (_executionLock)
            {
                _activeExecutor = null;
                _activeInput = null;
            }

            executor.Dispose();
            input.Dispose();
        }
    }

    public void TogglePause()
    {
        lock (_executionLock)
        {
            if (_activeExecutor is null)
            {
                return;
            }

            if (_activeExecutor.VisualPauseRequested)
            {
                _activeExecutor.ClearVisualPause();
            }
            else if (_activeExecutor.IsPaused)
            {
                _activeExecutor.Resume();
            }
            else
            {
                _activeExecutor.Pause();
            }
        }
    }

    public void Stop()
    {
        lock (_executionLock)
        {
            _activeExecutor?.RequestStop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        lock (_executionLock)
        {
            _activeExecutor?.Dispose();
            _activeInput?.Dispose();
            _activeExecutor = null;
            _activeInput = null;
        }

        _profileStore.Dispose();
        _disposed = true;
    }

    private async Task<TargetWindowSnapshot> WaitForForegroundTargetAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = await FindPodiumsTargetAsync(cancellationToken).ConfigureAwait(false);
            if (target?.IsForeground == true)
            {
                return target;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new InvalidOperationException("포그라운드 상태의 Roblox Podiums 창을 찾지 못했습니다.");
    }

    private async Task MonitorVisualSafetyAsync(
        TargetWindowBinding binding,
        WindowsDrawingExecutor executor,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (!CurrentProfile.VisualVerification.Enabled)
        {
            return;
        }

        var coordinator = _adapter.CreateVisualSafetyCoordinator(CurrentProfile, executor);
        coordinator.Reset(new VisualObservation(
            new PixelSize(binding.Snapshot.ClientWidth, binding.Snapshot.ClientHeight),
            CurrentProfile.Canvas.Bounds,
            1d));
        var captureFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(750, cancellationToken).ConfigureAwait(false);
            if (!await binding.RefreshAsync(cancellationToken).ConfigureAwait(false))
            {
                executor.RequestVisualPause("대상 창 좌표를 갱신하지 못했습니다.");
                status?.Report("대상 창을 확인할 수 없어 안전 일시 정지했습니다. F7로 확인 후 재개하세요.");
                continue;
            }

            var frame = await _capture.CaptureAsync(binding.Snapshot, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                captureFailures++;
                if (captureFailures >= CurrentProfile.VisualVerification.ConsecutiveFailuresBeforePause)
                {
                    executor.RequestVisualPause("Roblox 화면 캡처가 연속으로 실패했습니다.");
                    status?.Report("화면 인식에 실패해 안전 일시 정지했습니다. F7로 확인 후 재개하세요.");
                }

                continue;
            }

            captureFailures = 0;
            var detection = _adapter.VisualDetector.Detect(frame.ToImageFrame());
            var notice = coordinator.Observe(detection);
            if (notice.ShouldPause)
            {
                status?.Report("캔버스 위치 변화가 감지되어 안전 일시 정지했습니다. F7로 확인 후 재개하세요.");
            }
        }
    }

    private async Task VerifyVisualPreflightAsync(
        TargetWindowSnapshot target,
        CancellationToken cancellationToken)
    {
        if (!CurrentProfile.VisualVerification.Enabled)
        {
            return;
        }

        var captured = await _capture.CaptureAsync(target, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Roblox 화면을 캡처하지 못해 안전 검사를 완료할 수 없습니다.");
        var detection = _adapter.VisualDetector.Detect(captured.ToImageFrame());
        if (!detection.IsSafeToContinue)
        {
            throw new InvalidOperationException(
                detection.Canvas.Reason ?? "현재 화면에서 Podiums 캔버스를 찾지 못했습니다.");
        }

        var monitor = new VisualDriftMonitor(new VisualVerificationPolicy
        {
            MinimumCanvasConfidence = CurrentProfile.VisualVerification.MinimumConfidence,
            MaximumCanvasShiftPixels = CurrentProfile.VisualVerification.MaximumCanvasShiftPixels,
            MaximumCanvasScaleDelta = CurrentProfile.VisualVerification.MaximumCanvasScaleDelta,
            MaximumAnchorShiftPixels = CurrentProfile.VisualVerification.MaximumAnchorShiftPixels,
            ConsecutiveFailuresBeforePause = 1
        });
        monitor.Reset(new VisualObservation(
            captured.Size,
            CurrentProfile.Canvas.Bounds,
            1d));
        var notice = monitor.Observe(detection.ToObservation());
        if (notice.IsDriftDetected)
        {
            throw new InvalidOperationException(
                "현재 화면의 캔버스가 저장된 Podiums 캘리브레이션과 일치하지 않습니다. 다시 캘리브레이션하세요.");
        }
    }

    private static PixelSize FitWithin(PixelSize source, PixelSize bounds)
    {
        var scale = Math.Min(
            1d,
            Math.Min(bounds.Width / (double)source.Width, bounds.Height / (double)source.Height));
        return new PixelSize(
            Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero)));
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
