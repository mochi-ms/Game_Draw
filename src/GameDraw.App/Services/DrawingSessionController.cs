using GameDraw.Automation.Windows;
using GameDraw.Automation.Windows.Capture;
using GameDraw.Automation.Windows.Execution;
using GameDraw.Automation.Windows.Input;
using GameDraw.Automation.Windows.Targeting;
using GameDraw.Core.Colors;
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
using GameDraw.Imaging.Resampling;
using GameDraw.Planning;
using GameDraw.Profiles;

namespace GameDraw_App.Services;

public sealed record PreparedDrawing(
    string SourcePath,
    ImageProcessingResult Image,
    DrawingPlanningResult Planning,
    int RequestedColors,
    DrawingRenderStyle RenderStyle,
    double SpeedMultiplier)
{
    public string Summary =>
        $"{(RenderStyle == DrawingRenderStyle.LineArt ? "검정 선화" : "자동 채색")} · " +
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
    private readonly PaletteQuantizer _quantizer = new();
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

    public ValueTask<TargetWindowGeometry?> GetTargetGeometryAsync(
        TargetWindowSnapshot target,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        return _geometryProvider.GetGeometryAsync(target.Handle, cancellationToken);
    }

    public async Task<bool> ActivateTargetAsync(
        TargetWindowSnapshot target,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        _ = WindowsWindowActivator.TryRestoreAndActivate(target.Handle);
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        var refreshed = await _geometryProvider.GetGeometryAsync(target.Handle, cancellationToken).ConfigureAwait(false);
        return refreshed?.Snapshot.IsForeground == true;
    }

    public async Task<PreparedDrawing> PrepareAsync(
        string sourcePath,
        DrawingMode mode,
        int maximumColors,
        DrawingRenderStyle renderStyle,
        double speedMultiplier,
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

        if (!Enum.IsDefined(renderStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(renderStyle));
        }

        if (!double.IsFinite(speedMultiplier) || speedMultiplier is < 0.5d or > 6d)
        {
            throw new ArgumentOutOfRangeException(nameof(speedMultiplier));
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
        ImageProcessingResult image;
        if (renderStyle == DrawingRenderStyle.LineArt)
        {
            image = await Task.Run(() =>
            {
                var resized = ImageResampler.Resize(decoded.Frame, target);
                var lineArt = LineArtProcessor.Extract(resized);
                var palette = new ColorPalette(new[] { RgbColor.Black }, "line-art");
                var quantized = _quantizer.Quantize(lineArt, palette, new QuantizationOptions
                {
                    DitherMode = DitherMode.None,
                    PreserveAlpha = true
                });
                cancellationToken.ThrowIfCancellationRequested();
                return new ImageProcessingResult(decoded, lineArt, palette, quantized);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
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
            image = await Task.Run(
                () => _imaging.ProcessFrame(
                    decoded.Frame,
                    processingOptions,
                    sourcePath,
                    decoded.FormatName,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        status?.Report("그리기 경로와 예상 시간을 계산하는 중입니다…");
        var plannerOptions = new DrawingPlannerOptions
        {
            Mode = renderStyle == DrawingRenderStyle.LineArt && mode == DrawingMode.Auto
                ? DrawingMode.HorizontalScanline
                : mode,
            MovementPixelsPerSecond = CurrentProfile.Timing.MovementPixelsPerSecond * speedMultiplier,
            InterStrokeDelayMilliseconds = (int)Math.Round(CurrentProfile.Timing.InterStrokeDelayMilliseconds / speedMultiplier),
            ColorChangeDelayMilliseconds = (int)Math.Round(CurrentProfile.Timing.ColorChangeDelayMilliseconds / speedMultiplier)
        };
        var planning = await Task.Run(
            () => _planner.Plan(image.Quantized, plannerOptions),
            cancellationToken).ConfigureAwait(false);
        return new PreparedDrawing(sourcePath, image, planning, maximumColors, renderStyle, speedMultiplier);
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

        status?.Report("Roblox Podiums 창을 자동으로 활성화합니다. F8은 언제든 즉시 중지합니다.");
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
        var executionCanvas = await VerifyVisualPreflightAsync(target, status, cancellationToken).ConfigureAwait(false);

        var binding = new TargetWindowBinding(geometry, _geometryProvider, executionCanvas);
        var input = new WindowsInputController(new WindowsInputOptions { MaxEventsPerSecond = 1_000d });
        var executor = new WindowsDrawingExecutor(input, binding);
        var context = new GameAdapterExecutionContext(input, target, binding.MapClient);
        var hooks = new PodiumsExecutionHooks(CurrentProfile, context);
        lock (_executionLock)
        {
            _activeInput = input;
            _activeExecutor = executor;
        }

        try
        {
            return await executor.ExecuteAsync(
                prepared.Planning.Plan,
                new DrawingExecutionOptions
                {
                    MovementPixelsPerSecond = CurrentProfile.Timing.MovementPixelsPerSecond,
                    SpeedMultiplier = prepared.SpeedMultiplier,
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

            if (target is not null)
            {
                _ = WindowsWindowActivator.TryRestoreAndActivate(target.Handle);
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new InvalidOperationException("포그라운드 상태의 Roblox Podiums 창을 찾지 못했습니다.");
    }

    private async Task<NormalizedRect> VerifyVisualPreflightAsync(
        TargetWindowSnapshot target,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (!CurrentProfile.VisualVerification.Enabled)
        {
            return CurrentProfile.Canvas.Bounds;
        }

        var captured = await _capture.CaptureAsync(target, cancellationToken).ConfigureAwait(false);
        if (captured is null)
        {
            status?.Report("화면 자동 감지를 사용할 수 없어 드래그로 저장한 캔버스 영역을 사용합니다.");
            return CurrentProfile.Canvas.Bounds;
        }

        var detection = _adapter.VisualDetector.Detect(captured.ToImageFrame());
        if (!detection.IsSafeToContinue)
        {
            status?.Report("흰 캔버스를 자동으로 찾지 못해 드래그로 저장한 영역을 사용합니다.");
            return CurrentProfile.Canvas.Bounds;
        }

        var saved = CurrentProfile.Canvas.Bounds;
        var detected = detection.Canvas.NormalizedBounds;
        var allowedShift = Math.Max(
            CurrentProfile.VisualVerification.MaximumCanvasShiftPixels,
            Math.Min(captured.Size.Width, captured.Size.Height) * 0.06d);
        var allowedScale = Math.Max(CurrentProfile.VisualVerification.MaximumCanvasScaleDelta, 0.25d);
        var registration = CanvasRegistration.Compare(
            saved,
            detected,
            captured.Size,
            maximumCenterShiftPixels: allowedShift,
            maximumScaleDelta: allowedScale);
        if (!registration.IsCompatible)
        {
            status?.Report(
                $"자동 감지 결과가 드래그 영역과 달라 저장한 영역을 우선 사용합니다. " +
                $"겹침 {registration.IntersectionOverUnion:P0}, 중심 차이 {registration.CenterShiftPixels:0}px입니다.");
            return saved;
        }

        return detected;
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
