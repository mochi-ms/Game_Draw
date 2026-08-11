using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using GameDraw.Automation.Windows;
using GameDraw.Automation.Windows.Hotkeys;
using GameDraw.Core.Execution;
using GameDraw.Core.Models;
using GameDraw.Core.Presentation;
using GameDraw.Core.Targeting;
using GameDraw.GameAdapters.Podiums.Calibration;
using GameDraw_App.Services;
using GameDraw_App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GameDraw_App;

/// <summary>
/// Responsive workspace shell. It owns image selection, theme/layout policy,
/// and presentation feedback; planning and native input execution remain in
/// the core/automation layers.
/// </summary>
public sealed partial class MainPage : Page, IDisposable
{
    private static readonly string[] SupportedExtensions =
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp"
    };

    public MainPageViewModel ViewModel { get; } = new();

    private WindowsHotkeyService? _hotkeys;
    private PodiumsCalibrationSession? _calibration;
    private TargetWindowSnapshot? _calibrationTarget;
    private PreparedDrawing? _preparedDrawing;
    private CancellationTokenSource? _executionCancellation;
    private ExecutionPanelWindow? _executionWindow;
    private bool _initialized;
    private bool _disposed;
    private bool _calibrationCaptureInProgress;

    public MainPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme(ViewModel.ThemeMode);
        UpdateResponsiveLayout(ActualWidth);
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            _hotkeys = new WindowsHotkeyService(App.WindowHandle);
            _hotkeys.HotkeyPressed += Hotkeys_HotkeyPressed;
            await App.DrawingSession.InitializeAsync();
            var profile = App.DrawingSession.CurrentProfile;
            ViewModel.SetProfileState(profile.Name, profile.Canvas.IsCalibrated);
            ViewModel.LogicalWidth = profile.Canvas.IsCalibrated ? profile.Canvas.LogicalWidth : 512;
            ViewModel.LogicalHeight = profile.Canvas.IsCalibrated ? profile.Canvas.LogicalHeight : 512;
        }
        catch (Exception exception)
        {
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"초기화 중 문제가 발생했습니다: {exception.Message}";
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
        => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _executionCancellation?.Cancel();
        _executionCancellation?.Dispose();
        _executionCancellation = null;
        if (_hotkeys is not null)
        {
            _hotkeys.HotkeyPressed -= Hotkeys_HotkeyPressed;
            _hotkeys.Dispose();
            _hotkeys = null;
        }

        _executionWindow?.Dispose();
        _executionWindow = null;

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items
            .OfType<StorageFile>()
            .FirstOrDefault(item => IsSupportedImage(item.Path));

        if (file is not null)
        {
            await LoadImageAsync(file.Path);
        }
    }

    private async void SelectImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };

        foreach (var extension in SupportedExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await LoadImageAsync(file.Path);
        }
    }

    private async Task LoadImageAsync(string path)
    {
        ViewModel.BeginLoading("이미지를 불러오는 중…");
        await Task.Yield();
        try
        {
            if (!IsSupportedImage(path))
            {
                ViewModel.StatusMessage = "지원하는 이미지 형식이 아닙니다.";
                return;
            }

            ViewModel.SetImage(path);
            _preparedDrawing = null;

            var bitmap = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache
            };
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            PreviewImage.Source = bitmap;
            PreviewBadgeLabel.Text = "원본 미리보기";
            PreviewImage.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            PreviewBadgeLabel.Text = "원본 미리보기";
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"이미지를 불러오지 못했습니다: {exception.Message}";
        }

        finally
        {
            ViewModel.EndLoading();
        }
    }

    private async void ProfileSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_hotkeys is null || ViewModel.IsBusy || ViewModel.IsCalibrating || App.DrawingSession.IsRunning)
        {
            return;
        }

        ViewModel.BeginLoading("Roblox Podiums 창을 찾는 중…");
        try
        {
            var target = await App.DrawingSession.FindPodiumsTargetAsync();
            if (target is null)
            {
                ViewModel.Stage = WorkspaceStage.Failed;
                ViewModel.StatusMessage = "열려 있는 Roblox Podiums 창을 찾지 못했습니다. 게임을 연 뒤 다시 시도하세요.";
                return;
            }

            _calibrationTarget = target;
            _calibration = new PodiumsCalibrationSession(new PodiumsCalibrationOptions
            {
                LogicalWidth = SafeWholeNumber(ViewModel.LogicalWidth, 512, 1, 4096),
                LogicalHeight = SafeWholeNumber(ViewModel.LogicalHeight, 512, 1, 4096)
            });
            _hotkeys.Register(InputKey.F6);
            ViewModel.IsCalibrating = true;
            ViewModel.Stage = WorkspaceStage.Configure;
            UpdateCalibrationMessage();
            ViewModel.StatusMessage = "Roblox로 전환해 안내된 위치에 마우스를 놓고 F6을 누르세요.";
        }
        catch (Exception exception)
        {
            FinishCalibration(false);
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"캘리브레이션을 시작하지 못했습니다: {exception.Message}";
        }
        finally
        {
            ViewModel.EndLoading();
        }
    }

    private void CancelCalibration_Click(object sender, RoutedEventArgs e)
    {
        _calibration?.Cancel();
        FinishCalibration(false);
        ViewModel.StatusMessage = "Podiums 캘리브레이션을 취소했습니다.";
    }

    private async void PrepareDrawing_Click(object sender, RoutedEventArgs e)
    {
        await PrepareDrawingAsync();
    }

    private async Task<bool> PrepareDrawingAsync()
    {
        if (string.IsNullOrWhiteSpace(ViewModel.SelectedImagePath) || ViewModel.IsBusy)
        {
            return false;
        }

        ViewModel.BeginLoading("그림을 분석하는 중…");
        try
        {
            var status = new Progress<string>(message =>
            {
                ViewModel.BusyMessage = message;
                ViewModel.StatusMessage = message;
            });
            _preparedDrawing = await App.DrawingSession.PrepareAsync(
                ViewModel.SelectedImagePath,
                SelectedDrawingMode(),
                SafeWholeNumber(ViewModel.MaximumColors, 128, 2, 256),
                status);
            ShowProcessedPreview(_preparedDrawing.Image.Quantized.Rendered);
            ViewModel.PlanSummary = _preparedDrawing.Summary;
            ViewModel.Stage = WorkspaceStage.Ready;
            ViewModel.StatusMessage = $"분석이 완료되었습니다. {_preparedDrawing.Summary}";
            return true;
        }
        catch (Exception exception)
        {
            _preparedDrawing = null;
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"이미지 분석에 실패했습니다: {exception.Message}";
            return false;
        }
        finally
        {
            ViewModel.EndLoading();
        }
    }

    private async void StartDrawing_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanStart || App.DrawingSession.IsRunning)
        {
            return;
        }

        if (_preparedDrawing is null && !await PrepareDrawingAsync())
        {
            return;
        }

        _executionCancellation?.Dispose();
        _executionCancellation = new CancellationTokenSource();
        try
        {
            RegisterExecutionHotkeys();
            if (_executionWindow is null || _executionWindow.IsDisposed)
            {
                _executionWindow = new ExecutionPanelWindow();
            }
            _executionWindow.Update("15초 안에 Roblox Podiums 창으로 전환하세요.", 0d);
            _executionWindow.ShowNearTopRight();
            ViewModel.IsExecutionPanelOpen = true;
            ViewModel.Stage = WorkspaceStage.Ready;
            ViewModel.StatusMessage = "15초 안에 Roblox Podiums 창으로 전환하세요. F8은 즉시 중지입니다.";
            var progress = new Progress<DrawingProgress>(item =>
            {
                ViewModel.SetProgress(item.ClampedFraction);
                ViewModel.SetExecutionState(item.State, item.Message);
                _executionWindow?.Update(item.Message, item.ClampedFraction);
            });
            var status = new Progress<string>(message =>
            {
                ViewModel.StatusMessage = message;
                _executionWindow?.Update(message, ViewModel.Progress);
            });
            var result = await App.DrawingSession.ExecuteAsync(
                _preparedDrawing!,
                progress,
                status,
                _executionCancellation.Token);
            ViewModel.SetExecutionState(
                result.State,
                result.ErrorMessage ?? (result.State == DrawingExecutionState.Completed
                    ? "그리기가 완료되었습니다."
                    : "그리기가 중지되었습니다."));
            if (result.State == DrawingExecutionState.Completed)
            {
                ViewModel.SetProgress(1d);
            }
        }
        catch (OperationCanceledException)
        {
            ViewModel.Stage = WorkspaceStage.Ready;
            ViewModel.StatusMessage = "그리기를 중지하고 모든 입력을 해제했습니다.";
            ViewModel.SetProgress(0d);
        }
        catch (Exception exception)
        {
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"그리기를 실행하지 못했습니다: {exception.Message}";
        }
        finally
        {
            _executionWindow?.Hide();
            UnregisterExecutionHotkeys();
            _executionCancellation?.Dispose();
            _executionCancellation = null;
        }
    }

    private void PauseResume_Click(object sender, RoutedEventArgs e)
        => ToggleExecutionPause();

    private void StopDrawing_Click(object sender, RoutedEventArgs e)
        => StopExecution();

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainPageViewModel.SelectedMode) or nameof(MainPageViewModel.MaximumColors))
        {
            _preparedDrawing = null;
            ViewModel.PlanSummary = "설정이 변경되었습니다. 이미지를 다시 분석하세요.";
            if (ViewModel.HasImage && ViewModel.Stage is not WorkspaceStage.Running and not WorkspaceStage.Paused)
            {
                ViewModel.Stage = WorkspaceStage.Configure;
            }
        }

        if (e.PropertyName == nameof(MainPageViewModel.SelectedImagePath)
            && string.IsNullOrWhiteSpace(ViewModel.SelectedImagePath))
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }

        if (e.PropertyName == nameof(MainPageViewModel.ThemeMode))
        {
            ApplyTheme(ViewModel.ThemeMode);
        }

    }

    private void UpdateResponsiveLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        ViewModel.SetLayoutWidth(width);
        var mode = ViewModel.LayoutMode;
        var compact = mode == ResponsiveLayoutMode.Compact;
        WorkspaceLayout.ColumnDefinitions[0].Width = mode switch
        {
            ResponsiveLayoutMode.Expanded => new GridLength(2, GridUnitType.Star),
            ResponsiveLayoutMode.Medium => new GridLength(1.35, GridUnitType.Star),
            _ => new GridLength(1, GridUnitType.Star)
        };
        WorkspaceLayout.ColumnDefinitions[1].Width = compact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        WorkspaceLayout.RowDefinitions[0].Height = compact
            ? GridLength.Auto
            : new GridLength(1, GridUnitType.Star);
        WorkspaceLayout.RowDefinitions[1].Height = compact
            ? GridLength.Auto
            : new GridLength(0);
        RootLayout.Padding = mode switch
        {
            ResponsiveLayoutMode.Expanded => new Thickness(32, 28, 32, 36),
            ResponsiveLayoutMode.Medium => new Thickness(24, 22, 24, 28),
            _ => new Thickness(16, 18, 16, 24)
        };

        Grid.SetColumn(ControlPanel, compact ? 0 : 1);
        Grid.SetRow(ControlPanel, compact ? 1 : 0);
        ExecutionOverlay.Margin = compact ? new Thickness(12) : new Thickness(24);
        PreviewBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyTheme(AppThemeMode mode)
    {
        RequestedTheme = mode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void PauseResume_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ToggleExecutionPause();
        args.Handled = true;
    }

    private void Theme_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ToggleThemeCommand.Execute(null);
        args.Handled = true;
    }

    private void StopExecution_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        StopExecution();
        args.Handled = true;
    }

    private async void Hotkeys_HotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        try
        {
            switch (e.Key)
            {
                case InputKey.F6:
                    await CaptureCalibrationPointAsync();
                    break;
                case InputKey.F7:
                    ToggleExecutionPause();
                    break;
                case InputKey.F8:
                    StopExecution();
                    break;
            }
        }
        catch (Exception exception)
        {
            App.DrawingSession.Stop();
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"안전 단축키 처리 중 오류가 발생해 실행을 중지했습니다: {exception.Message}";
        }
    }

    private async Task CaptureCalibrationPointAsync()
    {
        if (_calibration is null || _calibrationTarget is null || !ViewModel.IsCalibrating)
        {
            return;
        }

        if (_calibrationCaptureInProgress)
        {
            return;
        }

        _calibrationCaptureInProgress = true;
        try
        {

            var point = await App.DrawingSession.CaptureCursorInTargetAsync(_calibrationTarget);
            if (point is null)
            {
                ViewModel.CalibrationMessage = "마우스가 Roblox 창 안에 있지 않습니다. 위치를 다시 맞춘 뒤 F6을 누르세요.";
                return;
            }

            var state = _calibration.Capture(point.Value);
            if (state.Step == PodiumsCalibrationStep.Completed)
            {
                try
                {
                    var result = _calibration.Complete();
                    var profile = await App.DrawingSession.SaveCalibrationAsync(result, ViewModel.ProfileName);
                    ViewModel.SetProfileState(profile.Name, true);
                    ViewModel.Stage = ViewModel.HasImage ? WorkspaceStage.Configure : WorkspaceStage.SelectImage;
                    ViewModel.StatusMessage = "Podiums 캘리브레이션을 저장했습니다. 이미지를 분석한 뒤 실행할 수 있습니다.";
                    FinishCalibration(true);
                }
                catch (Exception exception)
                {
                    ViewModel.CalibrationMessage = $"캘리브레이션 저장에 실패했습니다: {exception.Message}";
                }

                return;
            }

            UpdateCalibrationMessage();
        }
        finally
        {
            _calibrationCaptureInProgress = false;
        }
    }

    private void FinishCalibration(bool completed)
    {
        _hotkeys?.Unregister(InputKey.F6);
        ViewModel.IsCalibrating = false;
        if (!completed)
        {
            ViewModel.Stage = ViewModel.HasImage ? WorkspaceStage.Configure : WorkspaceStage.SelectImage;
        }

        _calibration = null;
        _calibrationTarget = null;
    }

    private void UpdateCalibrationMessage()
    {
        if (_calibration is null)
        {
            return;
        }

        ViewModel.CalibrationMessage = _calibration.State.Step switch
        {
            PodiumsCalibrationStep.CaptureCanvasTopLeft => "1/8 · 흰색 캔버스의 왼쪽 위 모서리에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureCanvasBottomRight => "2/8 · 흰색 캔버스의 오른쪽 아래 모서리에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CapturePencilTool => "3/8 · 연필 도구 가운데에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureEraserTool => "4/8 · 지우개 도구 가운데에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureFillTool => "5/8 · 채우기 도구 가운데에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureBrushSizeMinimum => "6/8 · 굵기 슬라이더의 최소값 위치에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureBrushSizeMaximum => "7/8 · 굵기 슬라이더의 최대값 위치에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureHexInput => "8/8 · HEX 입력란 가운데에 마우스를 놓고 F6",
            _ => _calibration.State.Message
        };
    }

    private void ToggleExecutionPause()
    {
        if (!App.DrawingSession.IsRunning)
        {
            return;
        }

        App.DrawingSession.TogglePause();
        ViewModel.Stage = App.DrawingSession.IsPaused ? WorkspaceStage.Paused : WorkspaceStage.Running;
        ViewModel.StatusMessage = App.DrawingSession.IsPaused
            ? "그리기를 일시 정지하고 마우스를 해제했습니다. F7로 재개하세요."
            : "그리기를 재개했습니다.";
    }

    private void StopExecution()
    {
        App.DrawingSession.Stop();
        _executionCancellation?.Cancel();
        if (ViewModel.Stage is WorkspaceStage.Running or WorkspaceStage.Paused or WorkspaceStage.Ready)
        {
            ViewModel.StatusMessage = "중지 요청을 처리하고 모든 입력을 해제하는 중입니다…";
        }
    }

    private void RegisterExecutionHotkeys()
    {
        try
        {
            _hotkeys?.Register(InputKey.F7);
            _hotkeys?.Register(InputKey.F8);
        }
        catch (Exception exception)
        {
            _hotkeys?.Unregister(InputKey.F7);
            _hotkeys?.Unregister(InputKey.F8);
            throw new InvalidOperationException($"안전 단축키를 등록하지 못했습니다: {exception.Message}", exception);
        }
    }

    private void UnregisterExecutionHotkeys()
    {
        _hotkeys?.Unregister(InputKey.F7);
        _hotkeys?.Unregister(InputKey.F8);
    }

    private DrawingMode SelectedDrawingMode()
        => ViewModel.SelectedMode switch
        {
            "픽셀 점찍기" => DrawingMode.Pixel,
            "가로 스캔라인" => DrawingMode.HorizontalScanline,
            "세로 스캔라인" => DrawingMode.VerticalScanline,
            "윤곽선" => DrawingMode.Contour,
            "면 채우기" => DrawingMode.Fill,
            "하이브리드" => DrawingMode.Hybrid,
            _ => DrawingMode.Auto
        };

    private static int SafeWholeNumber(double value, int fallback, int minimum, int maximum)
        => double.IsFinite(value)
            ? Math.Clamp((int)Math.Round(value), minimum, maximum)
            : fallback;

    private void ShowProcessedPreview(GameDraw.Core.Imaging.ImageFrame frame)
    {
        var bitmap = new WriteableBitmap(frame.Width, frame.Height);
        var bytes = new byte[checked(frame.PixelCount * 4)];
        for (var index = 0; index < frame.PixelCount; index++)
        {
            var pixel = frame.Pixels[index];
            var offset = index * 4;
            bytes[offset] = (byte)((pixel.Color.B * pixel.Alpha + 127) / 255);
            bytes[offset + 1] = (byte)((pixel.Color.G * pixel.Alpha + 127) / 255);
            bytes[offset + 2] = (byte)((pixel.Color.R * pixel.Alpha + 127) / 255);
            bytes[offset + 3] = pixel.Alpha;
        }

        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Write(bytes, 0, bytes.Length);
        bitmap.Invalidate();
        PreviewImage.Source = bitmap;
        PreviewBadgeLabel.Text = "변환 미리보기";
    }

    private static bool IsSupportedImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
