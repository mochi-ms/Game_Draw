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
    }

    public ObservableCollection<GameProfile> Profiles { get; } = new();

    public IReadOnlyList<DrawingMode> DrawingModes { get; } = Enum.GetValues<DrawingMode>();

    public IReadOnlyList<ColorAdapterKind> ColorAdapterKinds { get; } = Enum.GetValues<ColorAdapterKind>();

    public IReadOnlyList<int> ColorCounts { get; } = new[] { 2, 4, 8, 12, 16, 24, 32 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WelcomeVisibility))]
    [NotifyPropertyChangedFor(nameof(HasImageVisibility))]
    public partial ImageSource? OriginalImageSource { get; set; }

    [ObservableProperty]
    public partial ImageSource? ProcessedImageSource { get; set; }

    [ObservableProperty]
    public partial GameProfile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial DrawingMode SelectedDrawingMode { get; set; } = DrawingMode.Scanline;

    [ObservableProperty]
    public partial ColorAdapterKind SelectedColorAdapterKind { get; set; } = ColorAdapterKind.Manual;

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
    public partial string StatisticsText { get; set; } = "아직 DrawingPlan이 없습니다.";

    [ObservableProperty]
    public partial string EstimatedTimeText { get; set; } = "Estimated —";

    [ObservableProperty]
    public partial string DryRunText { get; set; } = "Dry Run을 실행하면 예상 stroke와 시간이 표시됩니다.";

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

    public Visibility WelcomeVisibility => OriginalImageSource is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasImageVisibility => OriginalImageSource is null ? Visibility.Collapsed : Visibility.Visible;

    public string ProfileSummary => SelectedProfile is null
        ? "프로필을 선택하거나 새로 만드세요"
        : $"{SelectedProfile.Name} · {SelectedProfile.GameName}";

    public string AdapterSummary => SelectedProfile is null
        ? "캘리브레이션 전"
        : SelectedProfile.ColorAdapter.Kind.ToString();

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
            StatusMessage = $"{Profiles.Count}개의 Game Profile을 불러왔습니다.";
        }
        else
        {
            StatusMessage = "첫 번째 단계: 이미지를 선택한 뒤 새 Game Profile을 만드세요.";
        }
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
            StatusMessage = $"{ImageName} · {source.Width}×{source.Height}";
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

    public async Task CreateProfileAsync(string name, string gameName)
    {
        var profile = GameProfile.CreateDefault(name, gameName);
        await _services.ProfileStore.SaveAsync(profile, _lifetimeCancellation.Token);
        Profiles.Add(profile);
        SelectedProfile = profile;
        StatusMessage = $"'{profile.Name}' 프로필을 만들었습니다. 이제 캔버스를 캘리브레이션하세요.";
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
            StatusMessage = "먼저 Game Profile을 만드세요.";
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
                LogicalHeight = logicalHeight
            },
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveUpdatedProfileAsync(updated);
        StatusMessage = $"Canvas 캘리브레이션 완료 · {bounds.Width}×{bounds.Height}px · 논리 해상도 {logicalWidth}×{logicalHeight}";
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
        StatusMessage = $"Color Adapter를 {kind}(으)로 설정했습니다. 필요한 캘리브레이션을 진행하세요.";
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
        StatusMessage = $"Fixed Palette {palette.Count}개 버튼 위치를 저장했습니다.";
    }

    [RelayCommand]
    private async Task DryRunAsync()
    {
        await RefreshPreviewAsync();
        if (_drawingPlan is null)
        {
            DryRunText = "먼저 이미지를 선택하세요.";
            return;
        }

        var profile = GetEffectiveProfile(SelectedProfile ?? GameProfile.CreateDefault("Dry Run", ""));
        var estimate = DrawingEstimator.Estimate(_drawingPlan, profile);
        DryRunText = $"Dry Run · {_drawingPlan.Statistics.ColorCount} colors · {estimate.StrokeCount:N0} strokes · 예상 {FormatDuration(estimate.EstimatedDuration)} · 이동 {estimate.TravelDistancePixels:N0}px";
        StatusMessage = "Dry Run은 실제 SendInput을 실행하지 않았습니다.";
    }

    [RelayCommand]
    private async Task StartDrawingAsync()
    {
        if (IsDrawing)
        {
            return;
        }

        if (SelectedProfile is not { } selectedProfile)
        {
            StatusMessage = "Drawing 전에 Game Profile을 선택하세요.";
            return;
        }

        await RefreshPreviewAsync();
        if (_drawingPlan is null || _drawingPlan.Statistics.StrokeCount == 0)
        {
            StatusMessage = "그릴 수 있는 픽셀이 없습니다.";
            return;
        }

        var profile = GetEffectiveProfile(selectedProfile);
        var adapter = _services.ColorAdapters.Get(profile.ColorAdapter.Kind);
        var validation = profile.Validate();
        if (!validation.IsValid)
        {
            StatusMessage = $"프로필을 먼저 확인하세요: {string.Join("; ", validation.Errors)}";
            return;
        }

        _drawingCancellation?.Dispose();
        _drawingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _drawingCancellation.Token;
        var pauseGate = new PauseGate();
        IsDrawing = true;
        IsPaused = false;
        ProgressValue = 0;
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
            StatusMessage = "Drawing이 완료되었습니다.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Drawing을 중지했습니다. 마우스는 안전하게 해제되었습니다.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Drawing 오류: {exception.Message}";
        }
        finally
        {
            CountdownText = string.Empty;
            IsDrawing = false;
            IsPaused = false;
            _activePauseGate = null;
            _drawingCancellation?.Dispose();
            _drawingCancellation = null;
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
            StatusMessage = "Drawing을 재개했습니다.";
        }
        else
        {
            _activePauseGate.Pause();
            IsPaused = true;
            StatusMessage = "일시정지됨 · F7로 재개";
        }
    }

    [RelayCommand]
    private void StopDrawing()
    {
        _drawingCancellation?.Cancel();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_sourceImage is null)
        {
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
            StatisticsText = $"{plan.Statistics.ColorCount} colors · {plan.Statistics.StrokeCount:N0} strokes · {plan.Statistics.PointCount:N0} points";
            var estimate = DrawingEstimator.Estimate(plan, profile);
            EstimatedTimeText = $"Estimated {FormatDuration(estimate.EstimatedDuration)}";
        }
        catch (OperationCanceledException)
        {
            // A newer setting change owns the next preview.
        }
        catch (Exception exception)
        {
            StatusMessage = $"Preview 처리에 실패했습니다: {exception.Message}";
        }
    }

    private void UpdateProgress(DrawingProgress progress)
    {
        ProgressValue = Math.Clamp(progress.Percentage, 0d, 1d);
        CurrentColorText = progress.CurrentColor?.ToHex() ?? "—";
        ProgressText = progress.StrokeCount == 0
            ? progress.State.ToString()
            : $"Color {progress.ColorIndex}/{progress.ColorCount} · Stroke {progress.StrokeIndex:N0}/{progress.StrokeCount:N0} · {progress.Percentage:P0} · 남은 시간 {FormatDuration(progress.EstimatedRemaining)}";
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

    partial void OnSelectedProfileChanged(GameProfile? value)
    {
        if (value is null)
        {
            OnPropertyChanged(nameof(ProfileSummary));
            OnPropertyChanged(nameof(AdapterSummary));
            return;
        }

        SelectedColorAdapterKind = value.ColorAdapter.Kind;
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(AdapterSummary));
        _ = RefreshPreviewAsync();
    }

    partial void OnSelectedDrawingModeChanged(DrawingMode value) => _ = RefreshPreviewAsync();

    partial void OnSelectedColorAdapterKindChanged(ColorAdapterKind value) => OnPropertyChanged(nameof(AdapterSummary));

    partial void OnColorCountChanged(int value) => _ = RefreshPreviewAsync();

    partial void OnPrecisionChanged(double value) => _ = RefreshPreviewAsync();

    partial void OnSpeedChanged(double value) => _ = RefreshPreviewAsync();

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
