using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;
using GameDraw_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace GameDraw_App.ViewModels;

public partial class MainPageViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _drawingCancellation;
    private ImageBuffer? _sourceImage;
    private ImageBuffer? _processedImage;
    private DrawingPlan? _drawingPlan;
    private bool _initialized;

    public MainPageViewModel(AppServices services)
    {
        _services = services;
        _services.Hotkeys.PauseRequested += OnPauseRequested;
        _services.Hotkeys.EmergencyStopRequested += OnEmergencyStopRequested;

        WorkflowSteps.Add(new WorkflowStepViewModel("1", "이미지 선택", "그릴 이미지를 준비합니다.", "이미지 선택", SelectImageCommand));
        WorkflowSteps.Add(new WorkflowStepViewModel("2", "프로필 선택", "게임별 설정을 불러오거나 만듭니다.", "새 프로필", CreateDefaultProfileCommand));
        WorkflowSteps.Add(new WorkflowStepViewModel("3", "캔버스 위치", "게임의 그림 영역을 지정합니다.", "캔버스 설정", CalibrateCanvasCommand));
        WorkflowSteps.Add(new WorkflowStepViewModel("4", "색상 설정", "HEX, 팔레트 또는 수동 방식을 선택합니다.", string.Empty, null));
        WorkflowSteps.Add(new WorkflowStepViewModel("5", "변환 미리보기", "실제로 그려질 결과를 확인합니다.", string.Empty, null));
        WorkflowSteps.Add(new WorkflowStepViewModel("6", "사전 점검", "실제 클릭 없이 예상 결과를 확인합니다.", "사전 점검", DryRunCommand));
        WorkflowSteps.Add(new WorkflowStepViewModel("7", "자동 그리기", "준비가 끝나면 안전하게 실행합니다.", "자동 그리기 시작", StartDrawingCommand));
        UpdateWorkflow();
    }

    public ObservableCollection<GameProfile> Profiles { get; } = new();

    public ObservableCollection<WorkflowStepViewModel> WorkflowSteps { get; } = new();

    public IReadOnlyList<DrawingMode> DrawingModes { get; } = Enum.GetValues<DrawingMode>();

    public IReadOnlyList<ColorAdapterKind> ColorAdapterKinds { get; } = Enum.GetValues<ColorAdapterKind>();

    public IReadOnlyList<int> ColorCounts { get; } = new[] { 2, 4, 8, 12, 16, 24, 32 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WelcomeVisibility))]
    [NotifyPropertyChangedFor(nameof(HasImageVisibility))]
    [NotifyPropertyChangedFor(nameof(OnboardingVisibility))]
    public partial ImageSource? OriginalImageSource { get; set; }

    [ObservableProperty]
    public partial ImageSource? ProcessedImageSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileSummary))]
    [NotifyPropertyChangedFor(nameof(AdapterSummary))]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(OnboardingVisibility))]
    public partial GameProfile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial DrawingMode SelectedDrawingMode { get; set; } = DrawingMode.Scanline;

    [ObservableProperty]
    public partial int SelectedDrawingModeIndex { get; set; }

    [ObservableProperty]
    public partial ColorAdapterKind SelectedColorAdapterKind { get; set; } = ColorAdapterKind.Manual;

    [ObservableProperty]
    public partial int SelectedColorAdapterIndex { get; set; }

    [ObservableProperty]
    public partial int ColorCount { get; set; } = 12;

    [ObservableProperty]
    public partial double Precision { get; set; } = 0.78d;

    [ObservableProperty]
    public partial double Speed { get; set; } = 0.65d;

    [ObservableProperty]
    public partial string ImageName { get; set; } = "이미지를 선택하세요";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "이미지를 선택하고 게임 프로필을 만들어 보세요.";

    [ObservableProperty]
    public partial string StatisticsText { get; set; } = "변환 결과를 준비하면 통계가 표시됩니다.";

    [ObservableProperty]
    public partial string EstimatedTimeText { get; set; } = "예상 소요 시간 —";

    [ObservableProperty]
    public partial string DryRunText { get; set; } = "사전 점검을 실행하면 예상 선과 시간이 표시됩니다.";

    [ObservableProperty]
    public partial string CountdownText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentColorText { get; set; } = "—";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool IsDrawing { get; set; }

    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial bool CanStartDrawing { get; set; }

    [ObservableProperty]
    public partial bool CanDryRun { get; set; }

    [ObservableProperty]
    public partial bool IsDryRunComplete { get; set; }

    [ObservableProperty]
    public partial string StartAvailabilityText { get; set; } = "1단계: 이미지를 선택해 주세요.";

    public Visibility WelcomeVisibility => OriginalImageSource is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasImageVisibility => OriginalImageSource is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OnboardingVisibility => OriginalImageSource is null || SelectedProfile is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool HasSelectedProfile => SelectedProfile is not null;

    public string ProfileSummary => SelectedProfile is null
        ? "프로필을 선택하거나 새로 만드세요"
        : string.IsNullOrWhiteSpace(SelectedProfile.GameName)
            ? SelectedProfile.Name
            : $"{SelectedProfile.Name} · {SelectedProfile.GameName}";

    public string AdapterSummary => SelectedProfile is null
        ? "프로필을 선택하세요"
        : SelectedProfile.ColorAdapter.Kind switch
        {
            ColorAdapterKind.Manual => "수동 색상 선택",
            ColorAdapterKind.HexInput => "HEX 입력",
            ColorAdapterKind.FixedPalette => "고정 팔레트",
            ColorAdapterKind.HsvPicker => "HSV 선택기",
            _ => "색상 설정"
        };

    public string SelectedModeText => SelectedDrawingMode switch
    {
        DrawingMode.Scanline => "연속 선 (Scanline)",
        DrawingMode.Pixel => "픽셀 단위 (Pixel)",
        DrawingMode.LineArt => "선화 (Line Art)",
        _ => "그리기 방식"
    };

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var profiles = await _services.ProfileStore.LoadAllAsync(_lifetimeCancellation.Token);
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        if (Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
            StatusMessage = $"{Profiles.Count}개의 게임 프로필을 불러왔습니다.";
        }
        else
        {
            StatusMessage = "1단계: 이미지를 선택한 뒤 2단계에서 새 프로필을 만들어 주세요.";
        }

        UpdateWorkflow();
    }

    [RelayCommand]
    private async Task SelectImageAsync()
    {
        try
        {
            var path = await _services.FilePicker.PickImageAsync(_services.WindowHandle);
            if (!string.IsNullOrWhiteSpace(path))
            {
                await LoadImageAsync(path);
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"이미지를 열 수 없습니다: {exception.Message}";
        }
    }

    public async Task LoadImageAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var source = await _services.ImageProcessor.LoadAsync(path, cancellationToken: _lifetimeCancellation.Token);
            _sourceImage = source;
            OriginalImageSource = _services.PreviewRenderer.FromFile(path);
            ImageName = Path.GetFileName(path);
            StatusMessage = $"이미지를 불러왔습니다 · {ImageName} · {source.Width}×{source.Height}px";
            await RefreshPreviewAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"이미지 처리에 실패했습니다: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateDefaultProfileAsync()
    {
        await CreateProfileAsync("새 게임 프로필", "");
    }

    [RelayCommand]
    private async Task CalibrateCanvasAsync()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = "먼저 게임 프로필을 만들어 주세요.";
            UpdateWorkflow();
            return;
        }

        var bounds = await _services.Calibration.SelectCanvasAsync();
        if (bounds is { } canvas)
        {
            await ApplyCanvasCalibrationAsync(canvas);
        }
    }

    public async Task CreateProfileAsync(string name, string gameName)
    {
        var profile = GameProfile.CreateDefault(name, gameName);
        await _services.ProfileStore.SaveAsync(profile, _lifetimeCancellation.Token);
        Profiles.Add(profile);
        SelectedProfile = profile;
        StatusMessage = $"'{profile.Name}' 프로필을 만들었습니다. 이제 캔버스 위치를 설정해 주세요.";
        await RefreshPreviewAsync();
    }

    public async Task UpdateProfileIdentityAsync(string name, string gameName)
    {
        if (SelectedProfile is null)
        {
            await CreateProfileAsync(name, gameName);
            return;
        }

        var updated = SelectedProfile with
        {
            Name = string.IsNullOrWhiteSpace(name) ? SelectedProfile.Name : name.Trim(),
            GameName = gameName?.Trim() ?? string.Empty,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveUpdatedProfileAsync(updated);
        StatusMessage = $"'{updated.Name}' 프로필을 저장했습니다.";
    }

    [RelayCommand]
    private async Task DeleteSelectedProfileAsync()
    {
        if (SelectedProfile is not { } profile)
        {
            StatusMessage = "삭제할 프로필이 없습니다.";
            return;
        }

        await _services.ProfileStore.DeleteAsync(profile.Id, _lifetimeCancellation.Token);
        Profiles.Remove(profile);
        SelectedProfile = Profiles.FirstOrDefault();
        StatusMessage = $"'{profile.Name}' 프로필을 삭제했습니다.";
        await RefreshPreviewAsync();
    }

    public async Task ApplyCanvasCalibrationAsync(CanvasRect bounds)
    {
        if (SelectedProfile is not { } profile)
        {
            StatusMessage = "먼저 게임 프로필을 만들어 주세요.";
            return;
        }

        if (!bounds.IsValid)
        {
            StatusMessage = "캔버스 선택 영역이 올바르지 않습니다.";
            return;
        }

        var logicalWidth = Math.Clamp((int)Math.Round(bounds.Width / 8d), 16, 256);
        var logicalHeight = Math.Clamp((int)Math.Round(bounds.Height / 8d), 16, 256);
        var updated = profile with
        {
            Canvas = profile.Canvas with
            {
                Bounds = bounds,
                LogicalWidth = logicalWidth,
                LogicalHeight = logicalHeight,
                IsCalibrated = true
            },
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveUpdatedProfileAsync(updated);
        StatusMessage = $"캔버스 위치를 저장했습니다 · {bounds.Width}×{bounds.Height}px · 논리 해상도 {logicalWidth}×{logicalHeight}";
        await RefreshPreviewAsync();
    }

    public async Task SetColorAdapterAsync(ColorAdapterKind kind)
    {
        SelectedColorAdapterKind = kind;
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        var updated = profile with
        {
            ColorAdapter = profile.ColorAdapter with { Kind = kind },
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveUpdatedProfileAsync(updated);
        StatusMessage = $"색상 방식을 {GetColorAdapterLabel(kind)}(으)로 설정했습니다. 필요한 위치 설정을 진행해 주세요.";
        await RefreshPreviewAsync();
    }

    public async Task ApplyHexInputCalibrationAsync(ScreenPoint inputPosition)
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        var updated = profile with
        {
            ColorAdapter = profile.ColorAdapter with
            {
                Kind = ColorAdapterKind.HexInput,
                InputPosition = inputPosition
            },
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveUpdatedProfileAsync(updated);
        StatusMessage = $"HEX 입력 위치를 ({inputPosition.X}, {inputPosition.Y})로 저장했습니다.";
        UpdateWorkflow();
    }

    public async Task ApplyFixedPaletteCalibrationAsync(IReadOnlyList<PaletteEntry> palette)
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        var updated = profile with
        {
            ColorAdapter = profile.ColorAdapter with
            {
                Kind = ColorAdapterKind.FixedPalette,
                Palette = palette.ToArray()
            },
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveUpdatedProfileAsync(updated);
        StatusMessage = $"고정 팔레트 {palette.Count}개 버튼 위치를 저장했습니다.";
        UpdateWorkflow();
    }

    [RelayCommand]
    private async Task DryRunAsync()
    {
        await RefreshPreviewAsync();
        if (_drawingPlan is null)
        {
            DryRunText = "먼저 이미지를 선택하고 프로필을 준비해 주세요.";
            UpdateWorkflow();
            return;
        }

        var profile = GetEffectiveProfile(SelectedProfile ?? GameProfile.CreateDefault("사전 점검", ""));
        var estimate = DrawingEstimator.Estimate(_drawingPlan, profile);
        DryRunText = $"사전 점검 결과 · 색상 {_drawingPlan.Statistics.ColorCount}개 · 예상 선 {estimate.StrokeCount:N0}개 · 이동 {estimate.TravelDistancePixels:N0}px · 약 {FormatDuration(estimate.EstimatedDuration)}";
        IsDryRunComplete = true;
        StatusMessage = "사전 점검을 완료했습니다. 실제 게임 입력은 실행하지 않았습니다.";
        UpdateWorkflow();
    }

    [RelayCommand]
    private async Task StartDrawingAsync()
    {
        if (IsDrawing)
        {
            return;
        }

        if (!CanStartDrawing || SelectedProfile is not { } selectedProfile)
        {
            StatusMessage = StartAvailabilityText;
            UpdateWorkflow();
            return;
        }

        if (_drawingPlan is null || _drawingPlan.Statistics.StrokeCount == 0)
        {
            StatusMessage = "그릴 수 있는 픽셀이 없습니다. 설정을 바꿔 다시 미리보기를 확인해 주세요.";
            UpdateWorkflow();
            return;
        }

        var profile = GetEffectiveProfile(selectedProfile);
        var adapter = _services.ColorAdapters.Get(profile.ColorAdapter.Kind);
        var validation = profile.Validate();
        if (!validation.IsValid)
        {
            StatusMessage = $"프로필을 먼저 확인해 주세요: {string.Join("; ", validation.Errors)}";
            UpdateWorkflow();
            return;
        }

        _drawingCancellation?.Dispose();
        _drawingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _drawingCancellation.Token;
        var pauseGate = new PauseGate();
        IsDrawing = true;
        IsPaused = false;
        ProgressValue = 0;
        StatusMessage = "자동 그리기를 준비하고 있습니다. 3초 후 시작합니다.";
        try
        {
            for (var second = 3; second > 0; second--)
            {
                CountdownText = second.ToString(CultureInfo.InvariantCulture);
                await Task.Delay(1000, cancellationToken);
            }

            CountdownText = string.Empty;
            var progress = new Progress<DrawingProgress>(UpdateProgress);
            _activePauseGate = pauseGate;
            await _services.DrawingExecutor.ExecuteAsync(_drawingPlan, profile, adapter, _services.InputController, pauseGate, progress, cancellationToken);
            StatusMessage = "자동 그리기가 완료되었습니다.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "자동 그리기를 중지했습니다. 마우스 입력은 안전하게 해제되었습니다.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"자동 그리기 오류: {exception.Message}";
        }
        finally
        {
            CountdownText = string.Empty;
            IsDrawing = false;
            IsPaused = false;
            _activePauseGate = null;
            _drawingCancellation?.Dispose();
            _drawingCancellation = null;
            UpdateWorkflow();
        }
    }

    private PauseGate? _activePauseGate;

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsDrawing || _activePauseGate is null)
        {
            return;
        }

        if (IsPaused)
        {
            _activePauseGate.Resume();
            IsPaused = false;
            StatusMessage = "자동 그리기를 재개했습니다.";
        }
        else
        {
            _activePauseGate.Pause();
            IsPaused = true;
            StatusMessage = "일시정지됨 · F7로 재개할 수 있습니다.";
        }
    }

    [RelayCommand]
    private void StopDrawing()
    {
        _drawingCancellation?.Cancel();
    }

    private async Task RefreshPreviewAsync(bool invalidateDryRun = true)
    {
        if (invalidateDryRun)
        {
            IsDryRunComplete = false;
        }

        if (_sourceImage is null)
        {
            UpdateWorkflow();
            return;
        }

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _previewCancellation.Token;
        var profile = GetEffectiveProfile(SelectedProfile ?? GameProfile.CreateDefault("Preview", ""));
        var options = new ImageProcessingOptions
        {
            TargetWidth = GetPreviewDimension(profile.Canvas.LogicalWidth),
            TargetHeight = GetPreviewDimension(profile.Canvas.LogicalHeight),
            ColorCount = Math.Clamp(ColorCount, 1, 32),
            AdapterKind = SelectedColorAdapterKind,
            Palette = profile.ColorAdapter.Palette,
            Background = BackgroundMode.IgnoreTransparent,
            Dithering = false
        };

        try
        {
            var processed = await Task.Run(() => _services.ImageProcessor.Process(_sourceImage, options, cancellationToken), cancellationToken);
            var plan = await Task.Run(() => _services.DrawingPlanner.CreatePlan(processed, SelectedDrawingMode, cancellationToken: cancellationToken), cancellationToken);
            var preview = await _services.PreviewRenderer.RenderAsync(processed, cancellationToken);
            _processedImage = processed;
            _drawingPlan = plan;
            ProcessedImageSource = preview;
            StatisticsText = $"사용 색상 {plan.Statistics.ColorCount}개 · 예상 선 {plan.Statistics.StrokeCount:N0}개 · 점 {plan.Statistics.PointCount:N0}개";
            var estimate = DrawingEstimator.Estimate(plan, profile);
            EstimatedTimeText = $"예상 소요 시간 약 {FormatDuration(estimate.EstimatedDuration)} · 이동 {estimate.TravelDistancePixels:N0}px";
            DryRunText = "변환 결과가 준비되었습니다. 사전 점검을 실행해 실제 실행 전 결과를 확인하세요.";
            UpdateWorkflow();
        }
        catch (OperationCanceledException)
        {
            // A newer setting change owns the next preview.
        }
        catch (Exception exception)
        {
            StatusMessage = $"변환 미리보기 처리에 실패했습니다: {exception.Message}";
            UpdateWorkflow();
        }
    }

    private void UpdateProgress(DrawingProgress progress)
    {
        ProgressValue = Math.Clamp(progress.Percentage, 0d, 1d);
        CurrentColorText = progress.CurrentColor?.ToHex() ?? "—";
        ProgressText = progress.StrokeCount == 0
            ? GetProgressStateLabel(progress.State)
            : $"색상 {progress.ColorIndex}/{progress.ColorCount} · 선 {progress.StrokeIndex:N0}/{progress.StrokeCount:N0} · {progress.Percentage:P0} · 남은 시간 {FormatDuration(progress.EstimatedRemaining)}";
    }

    private async Task SaveUpdatedProfileAsync(GameProfile updated)
    {
        await _services.ProfileStore.SaveAsync(updated, _lifetimeCancellation.Token);
        var index = Profiles.IndexOf(Profiles.First(profile => profile.Id == updated.Id));
        if (index >= 0)
        {
            Profiles[index] = updated;
        }

        SelectedProfile = updated;
    }

    partial void OnOriginalImageSourceChanged(ImageSource? value) => UpdateWorkflow();

    partial void OnSelectedProfileChanged(GameProfile? value)
    {
        if (value is null)
        {
            OnPropertyChanged(nameof(ProfileSummary));
            OnPropertyChanged(nameof(AdapterSummary));
            UpdateWorkflow();
            return;
        }

        SelectedColorAdapterKind = value.ColorAdapter.Kind;
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(AdapterSummary));
        _ = RefreshPreviewAsync();
        UpdateWorkflow();
    }

    partial void OnSelectedDrawingModeChanged(DrawingMode value)
    {
        var index = (int)value;
        if (SelectedDrawingModeIndex != index)
        {
            SelectedDrawingModeIndex = index;
        }

        OnPropertyChanged(nameof(SelectedModeText));
        _ = RefreshPreviewAsync();
    }

    partial void OnSelectedDrawingModeIndexChanged(int value)
    {
        var mode = (DrawingMode)Math.Clamp(value, 0, Enum.GetValues<DrawingMode>().Length - 1);
        if (SelectedDrawingMode != mode)
        {
            SelectedDrawingMode = mode;
        }
    }

    partial void OnSelectedColorAdapterKindChanged(ColorAdapterKind value)
    {
        var index = (int)value;
        if (SelectedColorAdapterIndex != index)
        {
            SelectedColorAdapterIndex = index;
        }

        OnPropertyChanged(nameof(AdapterSummary));
        UpdateWorkflow();
    }

    partial void OnSelectedColorAdapterIndexChanged(int value)
    {
        var kind = (ColorAdapterKind)Math.Clamp(value, 0, Enum.GetValues<ColorAdapterKind>().Length - 1);
        if (SelectedColorAdapterKind != kind)
        {
            SelectedColorAdapterKind = kind;
        }
    }

    partial void OnColorCountChanged(int value)
    {
        _ = RefreshPreviewAsync();
        UpdateWorkflow();
    }

    partial void OnPrecisionChanged(double value)
    {
        _ = RefreshPreviewAsync();
        UpdateWorkflow();
    }

    partial void OnSpeedChanged(double value)
    {
        _ = RefreshPreviewAsync();
        UpdateWorkflow();
    }

    partial void OnIsDrawingChanged(bool value) => UpdateWorkflow();

    partial void OnIsDryRunCompleteChanged(bool value) => UpdateWorkflow();

    private void UpdateWorkflow()
    {
        if (WorkflowSteps.Count == 0)
        {
            return;
        }

        var hasImage = _sourceImage is not null;
        var hasProfile = SelectedProfile is not null;
        var hasCanvas = SelectedProfile?.Canvas.IsCalibrated == true;
        var hasColors = SelectedProfile is { } profile && HasValidColorConfiguration(profile);
        var hasPreview = _drawingPlan is { Statistics.StrokeCount: > 0 };
        var readyForStart = hasImage && hasProfile && hasCanvas && hasColors && hasPreview && IsDryRunComplete && !IsDrawing;

        CanDryRun = hasImage && hasProfile && !IsDrawing;
        CanStartDrawing = readyForStart;
        StartAvailabilityText = GetStartAvailabilityText(hasImage, hasProfile, hasCanvas, hasColors, hasPreview);

        var completed = new[] { hasImage, hasProfile, hasCanvas, hasColors, hasPreview, IsDryRunComplete, false };
        var currentIndex = Array.FindIndex(completed, isComplete => !isComplete);
        if (currentIndex < 0)
        {
            currentIndex = completed.Length - 1;
        }

        for (var index = 0; index < WorkflowSteps.Count; index++)
        {
            var isCompleted = completed[index];
            var isCurrent = index == currentIndex;
            var state = isCompleted
                ? "완료"
                : isCurrent
                    ? index == 6 && readyForStart ? "준비됨" : "지금 필요"
                    : "대기";
            WorkflowSteps[index].UpdateState(isCompleted, isCurrent, state);
        }
    }

    private bool HasValidColorConfiguration(GameProfile profile) => profile.ColorAdapter.Kind switch
    {
        ColorAdapterKind.Manual => true,
        ColorAdapterKind.HexInput => profile.ColorAdapter.InputPosition is not null,
        ColorAdapterKind.FixedPalette => profile.ColorAdapter.Palette.Count > 0,
        ColorAdapterKind.HsvPicker => profile.ColorAdapter.Hsv is { HueRegion.IsValid: true, SaturationValueRegion.IsValid: true },
        _ => false
    };

    private string GetStartAvailabilityText(bool hasImage, bool hasProfile, bool hasCanvas, bool hasColors, bool hasPreview)
    {
        if (IsDrawing)
        {
            return "자동 그리기 진행 중입니다.";
        }

        if (!hasImage)
        {
            return "1단계: 이미지를 선택해 주세요.";
        }

        if (!hasProfile)
        {
            return "2단계: 게임 프로필을 선택하거나 만들어 주세요.";
        }

        if (!hasCanvas)
        {
            return "3단계: 캔버스 위치를 설정해 주세요.";
        }

        if (!hasColors)
        {
            return "4단계: 색상 설정에 필요한 위치를 등록해 주세요.";
        }

        if (!hasPreview)
        {
            return "5단계: 변환 미리보기를 준비 중입니다.";
        }

        if (!IsDryRunComplete)
        {
            return "6단계: 사전 점검을 먼저 실행해 주세요.";
        }

        return "준비 완료. 자동 그리기를 시작할 수 있습니다.";
    }

    private static string GetColorAdapterLabel(ColorAdapterKind kind) => kind switch
    {
        ColorAdapterKind.Manual => "수동 색상 선택",
        ColorAdapterKind.HexInput => "HEX 입력",
        ColorAdapterKind.FixedPalette => "고정 팔레트",
        ColorAdapterKind.HsvPicker => "HSV 선택기",
        _ => "색상 설정"
    };

    private static string GetProgressStateLabel(DrawingExecutionState state) => state switch
    {
        DrawingExecutionState.Completed => "완료",
        DrawingExecutionState.Stopping => "중지 중",
        DrawingExecutionState.Error => "오류",
        DrawingExecutionState.Preparing => "준비 중",
        _ => "진행 중"
    };

    private void OnPauseRequested(object? sender, EventArgs args)
    {
        App.DispatcherQueue?.TryEnqueue(() => TogglePauseCommand.Execute(null));
    }

    private void OnEmergencyStopRequested(object? sender, EventArgs args)
    {
        App.DispatcherQueue?.TryEnqueue(() => StopDrawingCommand.Execute(null));
    }

    public void Dispose()
    {
        _services.Hotkeys.PauseRequested -= OnPauseRequested;
        _services.Hotkeys.EmergencyStopRequested -= OnEmergencyStopRequested;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _drawingCancellation?.Cancel();
        _drawingCancellation?.Dispose();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private GameProfile GetEffectiveProfile(GameProfile profile) => profile with
    {
        DrawingSpeed = Math.Clamp(Speed, 0.2d, 1d)
    };

    private int GetPreviewDimension(int logicalDimension)
    {
        var resolutionScale = 0.5d + (Math.Clamp(Precision, 0.2d, 1d) * 0.5d);
        return Math.Clamp((int)Math.Round(logicalDimension * resolutionScale), 16, 256);
    }
}
