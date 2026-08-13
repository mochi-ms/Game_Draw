using GameDraw.Automation.Windows;
using GameDraw.Automation.Windows.Capture;
using GameDraw.Automation.Windows.Execution;
using GameDraw.Automation.Windows.Input;
using GameDraw.Automation.Windows.Targeting;
using GameDraw.Core.Colors;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Models;
using GameDraw.Core.Targeting;
using GameDraw.Core.Vision;
using GameDraw.GameAdapters;
using GameDraw.GameAdapters.Podiums;
using GameDraw.GameAdapters.Podiums.Calibration;
using GameDraw.Imaging.Analysis;
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
    ImageFrame PlanPreview,
    SubjectFocusResult SubjectFocus,
    int RequestedColors,
    DrawingRenderStyle RenderStyle,
    DrawingQualityPreset Quality,
    double SpeedMultiplier,
    bool SmartSubjectEnabled,
    bool FacePriorityApplied)
{
    public string Summary =>
        $"{ModeLabel(Planning.Plan.Mode, RenderStyle)} · {QualityLabel(Quality)} · " +
        $"{Planning.Plan.LogicalSize.Width}×{Planning.Plan.LogicalSize.Height} · " +
        $"{Planning.Estimate.ColorCount}색 · {Planning.Estimate.StrokeCount:N0}스트로크 · " +
        $"예상 {FormatDuration(Planning.Estimate.EstimatedDuration)}" +
        (SubjectFocus.BackgroundRemoved ? " · 배경 제거" : string.Empty) +
        (FacePriorityApplied ? " · 얼굴 특징 우선" : string.Empty);

    private static string StyleLabel(DrawingRenderStyle style) => style switch
    {
        DrawingRenderStyle.NaturalLineArt => "자연스러운 펜선",
        DrawingRenderStyle.LineArt => "정밀 윤곽선",
        DrawingRenderStyle.AutoColor => "원본 색상",
        DrawingRenderStyle.PhotoHalftone => "고품질 망점 사진",
        DrawingRenderStyle.ArtistLineArt => "작가식 정밀 선화",
        DrawingRenderStyle.SmartPaint => "AI 밑그림·안전 채색",
        DrawingRenderStyle.FullPalette => "원본 팔레트 256색",
        DrawingRenderStyle.GrayscalePhoto => "AI 흑백 사진",
        _ => "자동"
    };

    private static string ModeLabel(DrawingMode mode, DrawingRenderStyle style) => mode switch
    {
        DrawingMode.SafeStamp when style == DrawingRenderStyle.FullPalette => "원본 팔레트 256색",
        DrawingMode.SafeStamp when style == DrawingRenderStyle.GrayscalePhoto => "AI 흑백 사진",
        DrawingMode.SafeStamp => "1점 안전 점묘",
        DrawingMode.HalftoneStamp => "고품질 망점 사진",
        DrawingMode.SmartFill => "AI 밑그림·안전 채색",
        DrawingMode.ArtistStroke when style == DrawingRenderStyle.ArtistLineArt => "작가식 정밀 선화",
        DrawingMode.ArtistStroke => "원본 펜선 보존",
        DrawingMode.CleanStroke => "자연스러운 펜선",
        _ => StyleLabel(style)
    };

    private static string QualityLabel(DrawingQualityPreset quality) => quality switch
    {
        DrawingQualityPreset.FastDraft => "속도 우선",
        DrawingQualityPreset.High => "정밀",
        DrawingQualityPreset.OriginalPriority => "최고 정밀",
        _ => "추천"
    };

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? $"{duration.TotalHours:0.#}시간"
            : duration.TotalMinutes >= 1
                ? $"{duration.TotalMinutes:0.#}분"
                : $"{duration.TotalSeconds:0.#}초";
}

public sealed record BrushMeasurementResult(
    double ScreenDiameterPixels,
    double LogicalDiameterPixels,
    int SuccessfulDots,
    double Confidence);

public sealed class DrawingSessionController : IDisposable
{
    private static readonly double[] BrushTestFractions = [0.2d, 0.5d, 0.8d];
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

    public async Task<IReadOnlyList<ScreenRect>> GetExecutionProtectedRegionsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!CurrentProfile.Canvas.IsCalibrated)
        {
            return Array.Empty<ScreenRect>();
        }

        var target = await FindPodiumsTargetAsync(cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return Array.Empty<ScreenRect>();
        }

        var geometry = await _geometryProvider.GetGeometryAsync(target.Handle, cancellationToken).ConfigureAwait(false);
        if (geometry is null || !geometry.IsValid)
        {
            return Array.Empty<ScreenRect>();
        }

        var binding = new TargetWindowBinding(geometry, _geometryProvider, CurrentProfile.Canvas.Bounds);
        var canvasTopLeft = binding.Map(new NormalizedPoint(0d, 0d));
        var canvasBottomRight = binding.Map(new NormalizedPoint(1d, 1d));
        var regions = new List<ScreenRect>
        {
            new(
                canvasTopLeft.X,
                canvasTopLeft.Y,
                Math.Max(1, canvasBottomRight.X - canvasTopLeft.X + 1),
                Math.Max(1, canvasBottomRight.Y - canvasTopLeft.Y + 1))
        };

        var controls = PodiumsProfileSettings.ReadControlLayout(CurrentProfile);
        AddControlRegion(controls.PencilTool);
        AddControlRegion(controls.EraserTool);
        if (controls.HasFillTool)
        {
            AddControlRegion(controls.FillTool);
        }

        if (controls.HasColorControls)
        {
            AddControlRegion(controls.HexInput, 96, 48);
        }

        return regions;

        void AddControlRegion(NormalizedPoint point, int width = 56, int height = 56)
        {
            if (!point.IsWithinUnitSquare)
            {
                return;
            }

            var screen = binding.MapClient(point);
            regions.Add(new ScreenRect(screen.X - (width / 2), screen.Y - (height / 2), width, height));
        }
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
        DrawingQualityPreset quality,
        double speedMultiplier,
        bool smartSubjectEnabled,
        NormalizedRect? manualCrop = null,
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

        if (!Enum.IsDefined(quality))
        {
            throw new ArgumentOutOfRangeException(nameof(quality));
        }

        if (!double.IsFinite(speedMultiplier) || speedMultiplier is < 0.5d or > 10d)
        {
            throw new ArgumentOutOfRangeException(nameof(speedMultiplier));
        }

        status?.Report("원본 이미지를 디코딩하는 중입니다…");
        var decoded = await _decoder.DecodeFileAsync(sourcePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var focusSource = manualCrop is { IsWithinUnitSquare: true, Width: > 0.001d, Height: > 0.001d }
            ? CropFrame(decoded.Frame, manualCrop.Value)
            : decoded.Frame;
        var subjectFocus = smartSubjectEnabled
            ? await Task.Run(() => SubjectFocusProcessor.Process(
                focusSource,
                renderStyle is DrawingRenderStyle.SmartPaint or DrawingRenderStyle.GrayscalePhoto
                    ? new SubjectFocusOptions
                    {
                        BackgroundTolerance = 42d,
                        LocalGradientTolerance = 18d,
                        MaximumGradientExpansion = 1.75d,
                        CropMarginRatio = 0.055d
                    }
                    : null), cancellationToken).ConfigureAwait(false)
            : SubjectFocusResult.Unchanged(focusSource);
        if (subjectFocus.BackgroundRemoved)
        {
            status?.Report(subjectFocus.PersonLikely
                ? "피사체 배경을 정리하고 얼굴 특징의 우선순위를 분석했습니다…"
                : "피사체를 중심으로 배경을 정리하고 크롭했습니다…");
        }

        var colorRendering = renderStyle is DrawingRenderStyle.AutoColor or DrawingRenderStyle.SmartPaint or DrawingRenderStyle.FullPalette or DrawingRenderStyle.GrayscalePhoto;
        // SubjectFocus already removes only border-connected background. The
        // former global near-white filter also erased teeth, eye whites, and
        // clothing highlights, leaving the later black detail pass with no
        // light pixels to repaint it in Podiums.
        var processingSource = colorRendering && !subjectFocus.BackgroundRemoved
            ? NearWhiteBackgroundProcessor.RemoveBorderConnected(subjectFocus.Frame)
            : subjectFocus.Frame;
        var smartPaintColorLimit = quality switch
        {
            DrawingQualityPreset.FastDraft => 16,
            DrawingQualityPreset.Balanced => 32,
            DrawingQualityPreset.High => 64,
            _ => 128
        };
        var effectiveMaximumColors = renderStyle == DrawingRenderStyle.FullPalette
            ? FullPaletteColorBudget(maximumColors, speedMultiplier)
            : colorRendering
            ? Math.Min(
                Math.Min(maximumColors, ColorMaximumColors(quality)),
                renderStyle == DrawingRenderStyle.SmartPaint ? smartPaintColorLimit : int.MaxValue)
            : maximumColors;
        var requestedBounds = CurrentProfile.Canvas.IsCalibrated
            ? new PixelSize(CurrentProfile.Canvas.LogicalWidth, CurrentProfile.Canvas.LogicalHeight)
            : new PixelSize(512, 512);
        var qualitySettings = QualitySettings.For(quality, colorRendering);
        var requestedMaximumDimension = renderStyle switch
        {
            DrawingRenderStyle.PhotoHalftone => Math.Min(
                qualitySettings.MaximumDimension,
                HalftoneMaximumDimension(quality)),
            DrawingRenderStyle.AutoColor or DrawingRenderStyle.SmartPaint or DrawingRenderStyle.FullPalette or DrawingRenderStyle.GrayscalePhoto => Math.Min(
                qualitySettings.MaximumDimension,
                ColorMaximumDimension(quality)),
            _ when mode == DrawingMode.SafeStamp => Math.Min(
                qualitySettings.MaximumDimension,
                SafeStampMaximumDimension(quality)),
            _ => qualitySettings.MaximumDimension
        };
        // AI previews were collapsing to ~104px whenever a noisy brush
        // measurement reported 7px. Keep the source-resolution budget for AI
        // modes; their printer pitch is handled by the plan brush below.
        var maximumAnalysisDimension = renderStyle is DrawingRenderStyle.SmartPaint or DrawingRenderStyle.GrayscalePhoto
            ? requestedMaximumDimension
            : BrushAwareMaximumDimension(
                requestedMaximumDimension,
                requestedBounds,
                CurrentProfile.Brush,
                1.15d);
        var analysisBounds = FitWithin(
            requestedBounds,
            new PixelSize(maximumAnalysisDimension, maximumAnalysisDimension));
        var fittedSource = ResizeAndLetterbox(processingSource, analysisBounds);

        status?.Report("색상과 해상도를 최적화하는 중입니다…");
        ImageProcessingResult image;
        if (renderStyle == DrawingRenderStyle.PhotoHalftone)
        {
            image = await Task.Run(() =>
            {
                var resized = fittedSource;
                var halftone = PhotoHalftoneProcessor.Process(resized, new PhotoHalftoneOptions
                {
                    ToneGamma = quality == DrawingQualityPreset.FastDraft ? 1.18d : 1.08d,
                    ToneStrength = quality == DrawingQualityPreset.OriginalPriority ? 0.94d : 0.88d,
                    EdgeStrength = quality == DrawingQualityPreset.FastDraft ? 0.62d : 0.72d
                });
                var palette = new ColorPalette(new[] { RgbColor.Black }, "photo-halftone");
                var quantized = _quantizer.Quantize(halftone, palette, new QuantizationOptions
                {
                    DitherMode = DitherMode.None,
                    PreserveAlpha = true
                });
                cancellationToken.ThrowIfCancellationRequested();
                return new ImageProcessingResult(decoded, halftone, palette, quantized);
            }, cancellationToken).ConfigureAwait(false);
        }
        else if (renderStyle == DrawingRenderStyle.ArtistLineArt)
        {
            image = await Task.Run(() =>
            {
                var resized = fittedSource;
                var artistLineArt = ArtistLineArtProcessor.Process(resized, new ArtistLineArtOptions
                {
                    EdgeThreshold = quality == DrawingQualityPreset.FastDraft ? 16d : 9d,
                    AdaptivePercentile = quality switch
                    {
                        DrawingQualityPreset.FastDraft => 0.68d,
                        DrawingQualityPreset.Balanced => 0.60d,
                        DrawingQualityPreset.High => 0.54d,
                        _ => 0.49d
                    },
                    WeakEdgeRatio = quality == DrawingQualityPreset.FastDraft ? 0.28d : 0.18d,
                    MinimumComponentPixels = quality == DrawingQualityPreset.FastDraft ? 4 : 2
                });
                var palette = new ColorPalette(new[] { RgbColor.Black }, "artist-line-art");
                var quantized = _quantizer.Quantize(artistLineArt, palette, new QuantizationOptions
                {
                    DitherMode = DitherMode.None,
                    PreserveAlpha = true
                });
                cancellationToken.ThrowIfCancellationRequested();
                return new ImageProcessingResult(decoded, artistLineArt, palette, quantized);
            }, cancellationToken).ConfigureAwait(false);
        }
        else if (renderStyle == DrawingRenderStyle.GrayscalePhoto)
        {
            image = await Task.Run(() =>
            {
                var grayscale = GrayscalePhotoProcessor.Process(fittedSource);
                var shadeCount = Math.Clamp(effectiveMaximumColors, 4, 32);
                var shades = Enumerable.Range(0, shadeCount)
                    .Select(index =>
                    {
                        var value = (byte)Math.Round(index * 255d / (shadeCount - 1));
                        return new RgbColor(value, value, value);
                    })
                    .ToArray();
                var palette = new ColorPalette(shades, "ai-grayscale-photo");
                var quantized = _quantizer.Quantize(grayscale, palette, new QuantizationOptions
                {
                    DitherMode = DitherMode.None,
                    PreserveAlpha = true
                });
                return new ImageProcessingResult(decoded, grayscale, palette, quantized);
            }, cancellationToken).ConfigureAwait(false);
        }
        else if (renderStyle is DrawingRenderStyle.LineArt or DrawingRenderStyle.NaturalLineArt)
        {
            image = await Task.Run(() =>
            {
                var resized = fittedSource;
                var lineArt = LineArtProcessor.Extract(resized, PhotoLineOptions(
                    quality,
                    precise: renderStyle == DrawingRenderStyle.LineArt));
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
                TargetSize = analysisBounds,
                Palette = new PaletteBuildOptions
                {
                    MaxColors = effectiveMaximumColors,
                    MaxSamples = 250_000
                },
                Quantization = new QuantizationOptions
                {
                    DitherMode = DitherMode.None,
                    PreserveAlpha = true
                }
            };
            image = await Task.Run(
                () => _imaging.ProcessFrame(
                    fittedSource,
                    processingOptions,
                    sourcePath,
                    decoded.FormatName,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        status?.Report("그리기 경로와 예상 시간을 계산하는 중입니다…");
        // At a 256px AI plan on the current Podiums canvas, one plan pixel is
        // already approximately the visible 3px pencil pitch. Inflating the
        // virtual brush from a stale 7px measurement blurred both the preview
        // and the actual result a second time.
        var planBrushDiameter = renderStyle is DrawingRenderStyle.SmartPaint or DrawingRenderStyle.GrayscalePhoto
            ? 1
            : EffectivePlanBrushDiameter(
                CurrentProfile.Brush,
                image.Quantized.Width,
                image.Quantized.Height,
                requestedBounds);
        var plannerOptions = new DrawingPlannerOptions
        {
            Mode = (renderStyle is DrawingRenderStyle.LineArt or DrawingRenderStyle.NaturalLineArt or DrawingRenderStyle.ArtistLineArt) && mode == DrawingMode.Auto
                ? DrawingMode.CleanStroke
                : mode,
            MovementPixelsPerSecond = CurrentProfile.Timing.MovementPixelsPerSecond * speedMultiplier,
            InterStrokeDelayMilliseconds = speedMultiplier >= 8d
                ? 0
                : (int)Math.Round(CurrentProfile.Timing.InterStrokeDelayMilliseconds / speedMultiplier),
            ColorChangeDelayMilliseconds = (int)Math.Round(CurrentProfile.Timing.ColorChangeDelayMilliseconds / speedMultiplier),
            PerStrokeSafetyDelayMilliseconds = mode is DrawingMode.SafeStamp or DrawingMode.HalftoneStamp or DrawingMode.Pixel or DrawingMode.SmartFill
                ? speedMultiplier >= 8d ? 28 : speedMultiplier >= 5d ? 37 : 60
                : speedMultiplier >= 8d ? 38 : 37,
            StrokeSimplificationTolerancePixels = qualitySettings.SimplificationTolerance,
            MinimumStrokeLengthPixels = qualitySettings.MinimumStrokeLength,
            BrushDiameterPixels = planBrushDiameter,
            OrderColorGroupsByTravel = renderStyle == DrawingRenderStyle.FullPalette,
            PriorityRegion = subjectFocus.FacePriorityRegion
        };
        var planning = await Task.Run(
            () => _planner.Plan(image.Quantized, plannerOptions),
            cancellationToken).ConfigureAwait(false);
        var facePriorityApplied = false;
        if (planning.Plan.Mode == DrawingMode.ArtistStroke)
        {
            var artistOrdered = DrawingPlanPostProcessor.OrderArtistically(
                planning.Plan,
                subjectFocus.FacePriorityRegion);
            planning = planning with
            {
                Plan = artistOrdered,
                Estimate = _planner.Estimate(artistOrdered, plannerOptions)
            };
            facePriorityApplied = subjectFocus.FacePriorityRegion is not null;
        }
        else if (renderStyle is not DrawingRenderStyle.SmartPaint and not DrawingRenderStyle.FullPalette and not DrawingRenderStyle.GrayscalePhoto && subjectFocus.FacePriorityRegion is { } faceRegion)
        {
            var prioritized = DrawingPlanPostProcessor.PrioritizeRegion(planning.Plan, faceRegion);
            planning = planning with
            {
                Plan = prioritized,
                Estimate = _planner.Estimate(prioritized, plannerOptions)
            };
            facePriorityApplied = true;
        }

        // Artistic/face-priority passes above may deliberately change stroke
        // order.  Make the final executable plan printer-like for every mode:
        // top to bottom with alternating horizontal direction and no extra HEX
        // visits.
        var coverageOrdered = renderStyle is DrawingRenderStyle.SmartPaint or DrawingRenderStyle.GrayscalePhoto or DrawingRenderStyle.FullPalette
            ? DrawingPlanPostProcessor.OrderColorsByCoverage(
                planning.Plan,
                preserveFirstGroup: planning.Plan.Mode == DrawingMode.SmartFill)
            : planning.Plan;
        var printerOrdered = DrawingPlanPostProcessor.OrderForPrinterTravel(coverageOrdered);
        planning = planning with
        {
            Plan = printerOrdered,
            Estimate = _planner.Estimate(printerOrdered, plannerOptions)
        };

        // Podiums' minimum visible pencil (3 screen pixels in the current UI)
        // maps to roughly a two-pixel logical brush on the usual calibrated
        // canvas. Render artist paths at that width so the preview represents
        // the manually selected in-game pencil more faithfully.
        // AI raster paths are executed with Podiums' minimum visible pencil,
        // which covers more than one logical sample on the usual canvas.
        // Show that conservative physical footprint so mouth/eye crowding is
        // visible before F5 instead of promising an unrealistically thin path.
        var previewBrushDiameter = renderStyle is DrawingRenderStyle.SmartPaint or DrawingRenderStyle.GrayscalePhoto
            ? 2
            : planBrushDiameter;
        var preview = DrawingPlanPostProcessor.RenderPreview(planning.Plan, previewBrushDiameter);
        return new PreparedDrawing(
            sourcePath,
            image,
            planning,
            preview,
            subjectFocus,
            effectiveMaximumColors,
            renderStyle,
            quality,
            speedMultiplier,
            smartSubjectEnabled,
            facePriorityApplied);
    }

    public async Task<DrawingExecutionResult> ExecuteAsync(
        PreparedDrawing prepared,
        long focusSinkWindowHandle,
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
        var safeStamp = prepared.Planning.Plan.Mode is
            DrawingMode.SafeStamp or DrawingMode.HalftoneStamp or DrawingMode.Pixel or DrawingMode.SmartFill;
        var input = new WindowsInputController(new WindowsInputOptions
        {
            FocusSinkWindowHandle = focusSinkWindowHandle,
            // Roblox coalesces very dense mouse messages. Frame-paced input
            // preserves curves and prevents skipped pen-up events.
            MaxEventsPerSecond = safeStamp
                ? prepared.SpeedMultiplier >= 8d ? 960d : prepared.SpeedMultiplier >= 5d ? 720d : 192d
                : prepared.SpeedMultiplier >= 8d ? 480d : 192d
        });
        var executor = new WindowsDrawingExecutor(input, binding);
        var context = new GameAdapterExecutionContext(input, target, binding.MapClient);
        // Brush thickness is intentionally left untouched. Podiums slider
        // orientation and range can change between UI versions, and users can
        // safely choose the desired thickness in-game before pressing F5.
        var hooks = new PodiumsExecutionHooks(
            CurrentProfile,
            context,
            selectColors: prepared.RenderStyle is DrawingRenderStyle.AutoColor or DrawingRenderStyle.SmartPaint or DrawingRenderStyle.FullPalette or DrawingRenderStyle.GrayscalePhoto);
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
                    InterStrokeDelayMilliseconds = safeStamp
                        ? 0
                        : prepared.SpeedMultiplier >= 8d
                        ? 0
                        : CurrentProfile.Timing.InterStrokeDelayMilliseconds,
                    ColorChangeDelayMilliseconds = CurrentProfile.Timing.ColorChangeDelayMilliseconds,
                    // Connected raster ink stays on one pen-down path. Every
                    // remaining disconnected move is promoted by the executor
                    // to the full capture/focus fence.
                    StrokeStartSettleMilliseconds = safeStamp
                        ? prepared.SpeedMultiplier >= 8d ? 2 : prepared.SpeedMultiplier >= 5d ? 7 : 14
                        : 10,
                    StrokeStartReleaseConfirmationCount = safeStamp && prepared.SpeedMultiplier >= 8d ? 0 : 1,
                    PenDownSettleMilliseconds = safeStamp ? 3 : 2,
                    PenUpSettleMilliseconds = safeStamp
                        ? prepared.SpeedMultiplier >= 8d ? 30 : prepared.SpeedMultiplier >= 5d ? 34 : 40
                        : 36,
                    // Maximum speed keeps three stationary up deliveries for
                    // local hops. Long travel and HEX changes still use the
                    // stronger capture/focus and neutralization boundaries.
                    AdditionalPenUpConfirmationCount = 2,
                    MinimumPenDownMilliseconds = safeStamp
                        ? prepared.SpeedMultiplier >= 8d ? 18 : prepared.SpeedMultiplier >= 5d ? 20 : 24
                        : prepared.SpeedMultiplier >= 8d ? 17 : 18,
                    MaximumMoveStepPixels = QualitySettings.For(
                        prepared.Quality,
                        prepared.RenderStyle is DrawingRenderStyle.AutoColor or DrawingRenderStyle.SmartPaint or DrawingRenderStyle.FullPalette or DrawingRenderStyle.GrayscalePhoto).MaximumMoveStepPixels,
                    MaximumContinuousPenDownDistancePixels = prepared.SpeedMultiplier >= 8d ? 160 : 96,
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

    public async Task<string> TestHexColorAsync(
        RgbColor color,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var controls = PodiumsProfileSettings.ReadControlLayout(CurrentProfile);
        if (!CurrentProfile.Canvas.IsCalibrated || !controls.IsConfigured || !controls.HasColorControls)
        {
            throw new InvalidOperationException("먼저 캔버스와 HEX 입력칸 위치를 설정하세요.");
        }

        lock (_executionLock)
        {
            if (_activeExecutor is not null)
            {
                throw new InvalidOperationException("그리기 실행 중에는 HEX 테스트를 할 수 없습니다.");
            }
        }

        status?.Report($"Roblox를 활성화하고 HEX 입력칸에 {color.ToHex()} 색상을 시험합니다…");
        var target = await WaitForForegroundTargetAsync(TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);
        var geometry = await _geometryProvider.GetGeometryAsync(target.Handle, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Roblox 창 좌표를 읽지 못했습니다.");
        var verification = await _adapter.VerifyAsync(target, CurrentProfile, cancellationToken).ConfigureAwait(false);
        if (!verification.IsSafeToRun)
        {
            throw new InvalidOperationException(string.Join(" ", verification.Issues.Select(issue => issue.Message)));
        }

        using var input = new WindowsInputController(new WindowsInputOptions { MaxEventsPerSecond = 96d });
        var binding = new TargetWindowBinding(geometry, _geometryProvider, CurrentProfile.Canvas.Bounds);
        var context = new GameAdapterExecutionContext(input, target, binding.MapClient);
        var result = await new PodiumsColorAdapter()
            .SelectColorAsync(color, CurrentProfile, context, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Message ?? "HEX 입력 테스트에 실패했습니다.");
        }

        status?.Report($"HEX 테스트 완료: 게임 색상이 {color.ToHex()}로 바뀌었는지 확인하세요.");
        return color.ToHex();
    }

    public async Task<BrushMeasurementResult> MeasureCurrentBrushAsync(
        NormalizedRect testRegion,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!CurrentProfile.Canvas.IsCalibrated || !testRegion.IsWithinUnitSquare)
        {
            throw new InvalidOperationException("캔버스와 유효한 빈 테스트 영역이 필요합니다.");
        }

        var calibratedCanvas = CurrentProfile.Canvas.Bounds;
        if (testRegion.X < calibratedCanvas.X ||
            testRegion.Y < calibratedCanvas.Y ||
            testRegion.X + testRegion.Width > calibratedCanvas.X + calibratedCanvas.Width ||
            testRegion.Y + testRegion.Height > calibratedCanvas.Y + calibratedCanvas.Height)
        {
            throw new InvalidOperationException("펜 테스트 영역은 저장된 흰색 캔버스 안쪽에서 선택하세요.");
        }

        lock (_executionLock)
        {
            if (_activeExecutor is not null)
            {
                throw new InvalidOperationException("그리기 실행 중에는 펜 굵기를 측정할 수 없습니다.");
            }
        }

        status?.Report("Roblox를 활성화하고 현재 펜으로 테스트 점 3개를 찍습니다…");
        var target = await WaitForForegroundTargetAsync(TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);
        var geometry = await _geometryProvider.GetGeometryAsync(target.Handle, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Roblox 창 좌표를 읽지 못했습니다.");
        var verification = await _adapter.VerifyAsync(target, CurrentProfile, cancellationToken).ConfigureAwait(false);
        if (!verification.IsSafeToRun)
        {
            throw new InvalidOperationException(string.Join(" ", verification.Issues.Select(issue => issue.Message)));
        }

        var binding = new TargetWindowBinding(geometry, _geometryProvider, CurrentProfile.Canvas.Bounds);
        using var input = new WindowsInputController(new WindowsInputOptions
        {
            MaxEventsPerSecond = 72d,
            MinimumIntervalMilliseconds = 12
        });
        var context = new GameAdapterExecutionContext(input, target, binding.MapClient);
        var toolResult = await new PodiumsToolAdapter()
            .SelectToolAsync(PodiumsToolKind.Pencil, CurrentProfile, context, cancellationToken)
            .ConfigureAwait(false);
        if (!toolResult.Succeeded)
        {
            throw new InvalidOperationException(toolResult.Message ?? "연필 도구를 선택하지 못했습니다.");
        }

        var controls = PodiumsProfileSettings.ReadControlLayout(CurrentProfile);
        if (controls.HasColorControls)
        {
            var colorResult = await new PodiumsColorAdapter()
                .SelectColorAsync(RgbColor.Black, CurrentProfile, context, cancellationToken)
                .ConfigureAwait(false);
            if (!colorResult.Succeeded)
            {
                throw new InvalidOperationException(colorResult.Message ?? "측정용 검정색을 선택하지 못했습니다.");
            }
        }

        await Task.Delay(180, cancellationToken).ConfigureAwait(false);
        var before = await _capture.CaptureAsync(target, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("측정 전 Roblox 화면을 캡처하지 못했습니다.");
        var points = BrushTestFractions
            .Select(fraction => new NormalizedPoint(
                testRegion.X + (testRegion.Width * fraction),
                testRegion.Y + (testRegion.Height * 0.5d)))
            .ToArray();
        foreach (var point in points)
        {
            // Measurement clicks intentionally span several Roblox render
            // frames. A short synthetic click can move to the third point but
            // have both button transitions coalesced before the canvas sees it.
            await input.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
            await input.MoveWithButtonsReleasedAsync(binding.MapClient(point), cancellationToken).ConfigureAwait(false);
            await Task.Delay(90, cancellationToken).ConfigureAwait(false);
            await input.MouseDownAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await Task.Delay(85, cancellationToken).ConfigureAwait(false);
            await input.MouseUpAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await input.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(180, cancellationToken).ConfigureAwait(false);
        }

        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        var after = await _capture.CaptureAsync(target, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("측정 후 Roblox 화면을 캡처하지 못했습니다.");
        var measured = BrushDotAnalyzer.MeasureDiameters(
            before.ToImageFrame(),
            after.ToImageFrame(),
            testRegion,
            points);
        if (measured.Count < 3)
        {
            status?.Report($"테스트 점이 {measured.Count}/3개만 보여 안전하게 다시 찍는 중…");
            foreach (var point in points)
            {
                await input.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
                await input.MoveWithButtonsReleasedAsync(binding.MapClient(point), cancellationToken).ConfigureAwait(false);
                await Task.Delay(110, cancellationToken).ConfigureAwait(false);
                await input.MouseDownAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                await Task.Delay(110, cancellationToken).ConfigureAwait(false);
                await input.MouseUpAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                await input.ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(210, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            after = await _capture.CaptureAsync(target, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("재시도 후 Roblox 화면을 캡처하지 못했습니다.");
            measured = BrushDotAnalyzer.MeasureDiameters(
                before.ToImageFrame(),
                after.ToImageFrame(),
                testRegion,
                points);
        }

        if (measured.Count == 0)
        {
            throw new InvalidOperationException(
                "지정한 가상 영역의 세 예상 좌표에서 검은 점을 찾지 못했습니다. 빈 흰색 영역을 다시 지정해 주세요.");
        }

        var ordered = measured.OrderBy(value => value).ToArray();
        var screenDiameter = ordered[ordered.Length / 2];
        var canvas = CurrentProfile.Canvas;
        var screenPerLogicalX = (geometry.ClientBounds.Width * canvas.Bounds.Width) / canvas.LogicalWidth;
        var screenPerLogicalY = (geometry.ClientBounds.Height * canvas.Bounds.Height) / canvas.LogicalHeight;
        var screenPerLogical = Math.Sqrt(Math.Max(0.0001d, screenPerLogicalX * screenPerLogicalY));
        var logicalDiameter = Math.Clamp(screenDiameter / screenPerLogical, 0.5d, 32d);
        var confidence = Math.Clamp(measured.Count / 3d, 0d, 1d);
        var profile = CurrentProfile with
        {
            Brush = CurrentProfile.Brush with
            {
                DiameterPixels = logicalDiameter,
                PixelPitchPixels = logicalDiameter,
                ScreenDiameterPixels = screenDiameter,
                IsMeasured = true
            }
        };
        await _profileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        CurrentProfile = profile;
        status?.Report(
            $"펜 굵기 측정 완료 · 점 {measured.Count}/3 · 화면 {screenDiameter:0.#}px · 가상 캔버스 {logicalDiameter:0.##}칸");
        return new BrushMeasurementResult(screenDiameter, logicalDiameter, measured.Count, confidence);
    }

    public async Task<BrushMeasurementResult> ApplyManualBrushDiameterAsync(
        double screenDiameterPixels,
        TargetWindowGeometry geometry,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(screenDiameterPixels) || screenDiameterPixels is < 1d or > 32d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(screenDiameterPixels),
                "게임에 표시된 펜 굵기는 1~32 사이로 입력하세요.");
        }

        ArgumentNullException.ThrowIfNull(geometry);
        if (!geometry.IsValid || !CurrentProfile.Canvas.IsCalibrated)
        {
            throw new InvalidOperationException("먼저 Roblox 캔버스 영역을 지정하세요.");
        }

        var canvas = CurrentProfile.Canvas;
        var screenPerLogicalX = (geometry.ClientBounds.Width * canvas.Bounds.Width) / canvas.LogicalWidth;
        var screenPerLogicalY = (geometry.ClientBounds.Height * canvas.Bounds.Height) / canvas.LogicalHeight;
        var screenPerLogical = Math.Sqrt(Math.Max(0.0001d, screenPerLogicalX * screenPerLogicalY));
        var logicalDiameter = Math.Clamp(screenDiameterPixels / screenPerLogical, 0.5d, 32d);
        var profile = CurrentProfile with
        {
            Brush = CurrentProfile.Brush with
            {
                DiameterPixels = logicalDiameter,
                PixelPitchPixels = logicalDiameter,
                ScreenDiameterPixels = screenDiameterPixels,
                IsMeasured = true
            }
        };
        await _profileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        CurrentProfile = profile;
        return new BrushMeasurementResult(screenDiameterPixels, logicalDiameter, 0, 1d);
    }

    private sealed record QualitySettings(
        int MaximumDimension,
        double DetailLevel,
        int MinimumComponentPixels,
        double SimplificationTolerance,
        double MinimumStrokeLength,
        int MaximumMoveStepPixels)
    {
        public static QualitySettings For(DrawingQualityPreset quality, bool color) => quality switch
        {
            DrawingQualityPreset.FastDraft => new(color ? 96 : 288, 0.46d, 9, 1.35d, 5d, 8),
            DrawingQualityPreset.High => new(color ? 192 : 448, 0.76d, 4, 0.55d, 2.5d, 4),
            DrawingQualityPreset.OriginalPriority => new(color ? 256 : 512, 0.9d, 3, 0.35d, 2d, 3),
            _ => new(color ? 144 : 384, 0.62d, 6, 0.85d, 3.5d, 6)
        };
    }

    private static int SafeStampMaximumDimension(DrawingQualityPreset quality) => quality switch
    {
        DrawingQualityPreset.FastDraft => 160,
        DrawingQualityPreset.High => 256,
        DrawingQualityPreset.OriginalPriority => 320,
        _ => 208
    };

    private static int FullPaletteColorBudget(int requestedMaximumColors, double speedMultiplier)
    {
        var requested = Math.Clamp(requestedMaximumColors, 2, 256);
        if (speedMultiplier >= 8d && requested > 64)
        {
            // At maximum speed the dominant cost is leaving the canvas and
            // committing a new HEX value. Perceptual palette construction
            // keeps the most useful representatives while guaranteeing at
            // most half as many risky color-control round trips.
            return Math.Min(128, Math.Max(64, (requested + 1) / 2));
        }

        if (speedMultiplier >= 5d && requested > 96)
        {
            return Math.Min(192, Math.Max(96, (requested * 3 + 3) / 4));
        }

        return requested;
    }

    private static int HalftoneMaximumDimension(DrawingQualityPreset quality) => quality switch
    {
        DrawingQualityPreset.FastDraft => 128,
        DrawingQualityPreset.High => 256,
        DrawingQualityPreset.OriginalPriority => 320,
        _ => 192
    };

    private static int ColorMaximumDimension(DrawingQualityPreset quality) => quality switch
    {
        DrawingQualityPreset.FastDraft => 128,
        DrawingQualityPreset.High => 256,
        DrawingQualityPreset.OriginalPriority => 320,
        _ => 192
    };

    private static int ColorMaximumColors(DrawingQualityPreset quality) => quality switch
    {
        DrawingQualityPreset.FastDraft => 16,
        DrawingQualityPreset.High => 64,
        DrawingQualityPreset.OriginalPriority => 128,
        _ => 32
    };

    private static LineArtOptions PhotoLineOptions(DrawingQualityPreset quality, bool precise)
    {
        var options = quality switch
        {
            DrawingQualityPreset.FastDraft => new LineArtOptions
            {
                EdgeThreshold = 42d,
                WeakEdgeRatio = 0.38d,
                AdaptivePercentile = 0.82d,
                MinimumComponentPixels = 7
            },
            DrawingQualityPreset.High => new LineArtOptions
            {
                EdgeThreshold = 20d,
                WeakEdgeRatio = 0.27d,
                AdaptivePercentile = 0.66d,
                MinimumComponentPixels = 3
            },
            DrawingQualityPreset.OriginalPriority => new LineArtOptions
            {
                EdgeThreshold = 12d,
                WeakEdgeRatio = 0.2d,
                AdaptivePercentile = 0.56d,
                MinimumComponentPixels = 2
            },
            _ => new LineArtOptions
            {
                EdgeThreshold = 28d,
                WeakEdgeRatio = 0.32d,
                AdaptivePercentile = 0.74d,
                MinimumComponentPixels = 4
            }
        };

        return precise
            ? options with
            {
                EdgeThreshold = Math.Max(8d, options.EdgeThreshold * 0.82d),
                WeakEdgeRatio = Math.Max(0.16d, options.WeakEdgeRatio - 0.05d),
                AdaptivePercentile = Math.Max(0.48d, options.AdaptivePercentile - 0.07d),
                MinimumComponentPixels = Math.Max(1, options.MinimumComponentPixels - 1)
            }
            : options;
    }

    private static int EffectivePlanBrushDiameter(
        BrushProfile brush,
        int planWidth,
        int planHeight,
        PixelSize logicalCanvas)
    {
        if (!brush.IsMeasured)
        {
            return 2;
        }

        var scaleX = planWidth / (double)Math.Max(1, logicalCanvas.Width);
        var scaleY = planHeight / (double)Math.Max(1, logicalCanvas.Height);
        var planDiameter = brush.DiameterPixels * Math.Sqrt(Math.Max(0.0001d, scaleX * scaleY));
        return Math.Clamp((int)Math.Round(planDiameter, MidpointRounding.AwayFromZero), 1, 32);
    }

    private static int BrushAwareMaximumDimension(
        int requestedMaximum,
        PixelSize logicalCanvas,
        BrushProfile brush,
        double samplingAllowance)
    {
        if (!brush.IsMeasured)
        {
            return requestedMaximum;
        }

        // A plan denser than the measured physical footprint only creates
        // overlapping blobs, not additional detail. Keep roughly one virtual
        // sample per real pen diameter with a small anti-aliasing allowance.
        var logicalLongSide = Math.Max(logicalCanvas.Width, logicalCanvas.Height);
        var brushLimited = (int)Math.Round(
            (logicalLongSide / Math.Max(0.5d, brush.DiameterPixels)) * samplingAllowance,
            MidpointRounding.AwayFromZero);
        return Math.Min(requestedMaximum, Math.Max(96, brushLimited));
    }

    private static ImageFrame ResizeAndLetterbox(ImageFrame source, PixelSize canvasSize)
    {
        var contentSize = FitWithin(
            new PixelSize(source.Width, source.Height),
            canvasSize);
        var resized = ImageResampler.Resize(source, contentSize);
        if (contentSize == canvasSize)
        {
            return resized;
        }

        var pixels = Enumerable.Repeat(
            RgbaPixel.Transparent,
            checked(canvasSize.Width * canvasSize.Height)).ToArray();
        var offsetX = (canvasSize.Width - contentSize.Width) / 2;
        var offsetY = (canvasSize.Height - contentSize.Height) / 2;
        for (var y = 0; y < contentSize.Height; y++)
        {
            for (var x = 0; x < contentSize.Width; x++)
            {
                pixels[((y + offsetY) * canvasSize.Width) + x + offsetX] = resized[x, y];
            }
        }

        return new ImageFrame(canvasSize.Width, canvasSize.Height, pixels);
    }

    private static ImageFrame CropFrame(ImageFrame source, NormalizedRect crop)
    {
        var left = Math.Clamp((int)Math.Floor(crop.X * source.Width), 0, source.Width - 1);
        var top = Math.Clamp((int)Math.Floor(crop.Y * source.Height), 0, source.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling((crop.X + crop.Width) * source.Width), left + 1, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling((crop.Y + crop.Height) * source.Height), top + 1, source.Height);
        var width = right - left;
        var height = bottom - top;
        var pixels = new RgbaPixel[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = source[left + x, top + y];
            }
        }

        return new ImageFrame(width, height, pixels);
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

        // The user's drag selection is the authoritative virtual canvas. The
        // detector is only a safety check; substituting its fuzzy bright-area
        // rectangle here made the same plan shift or scale between runs.
        return saved;
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
