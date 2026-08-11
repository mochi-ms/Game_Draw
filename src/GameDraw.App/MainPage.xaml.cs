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
    private CancellationTokenSource? _preparationCancellation;
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
        UpdateResponsiveLayout(ActualWidth, ActualHeight);
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
        _preparationCancellation?.Cancel();
        _preparationCancellation?.Dispose();
        _preparationCancellation = null;
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
        UpdateResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
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

    private async void ShowHelp_Click(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(CreateHelpStep("1. 이미지 선택", "PNG, JPG, WEBP 또는 BMP 파일을 고릅니다."));
        content.Children.Add(CreateHelpStep("2. 이미지 분석", "자동 채색은 원본 색을 줄여 순서대로 칠하고, 검정 선화는 외곽선만 추출합니다. 가로 스캔라인과 ‘빠르게’가 일반적인 권장값입니다."));
        content.Children.Add(CreateHelpStep("3. Podiums 연결", "처음 한 번만 Roblox 화면에서 안내되는 8개 위치를 차례로 가리키고 F6을 누릅니다."));
        content.Children.Add(CreateHelpStep("4. 그리기 시작", "필요하면 ‘게임 위에 띄우기’를 켠 뒤 시작 버튼을 누르고 15초 안에 Roblox로 전환합니다. F7은 일시정지, F8은 즉시 중지입니다."));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "GameDraw 사용법",
            Content = content,
            CloseButtonText = "확인",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private void ToggleFloating_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsFloating = !ViewModel.IsFloating;
        if (App.Window is MainWindow window)
        {
            window.SetFloatingMode(ViewModel.IsFloating);
        }

        ViewModel.StatusMessage = ViewModel.IsFloating
            ? "GameDraw를 게임 화면 위에 고정했습니다. 그리기 중에는 앱을 클릭하지 말고 F7/F8을 사용하세요."
            : "플로팅을 해제하고 원래 창 크기로 돌아왔습니다.";
    }

    private static StackPanel CreateHelpStep(string title, string description)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".gamedrawprofile");
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            ViewModel.BeginLoading("프로필을 가져오는 중…");
            var profile = await App.DrawingSession.ImportProfileAsync(file.Path);
            _preparedDrawing = null;
            ViewModel.SetProfileState(profile.Name, profile.Canvas.IsCalibrated);
            if (profile.Canvas.IsCalibrated)
            {
                ViewModel.LogicalWidth = profile.Canvas.LogicalWidth;
                ViewModel.LogicalHeight = profile.Canvas.LogicalHeight;
            }

            ViewModel.Stage = ViewModel.HasImage ? WorkspaceStage.Configure : WorkspaceStage.SelectImage;
            ViewModel.StatusMessage = $"'{profile.Name}' 프로필을 가져왔습니다. 이미지가 있다면 다시 분석하세요.";
        }
        catch (Exception exception)
        {
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"프로필을 가져오지 못했습니다: {exception.Message}";
        }
        finally
        {
            ViewModel.EndLoading();
        }
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "GameDraw-Podiums",
                DefaultFileExtension = ".gamedrawprofile"
            };
            picker.FileTypeChoices.Add("GameDraw 프로필", new List<string> { ".gamedrawprofile" });
            InitializeWithWindow.Initialize(picker, App.WindowHandle);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            ViewModel.BeginLoading("프로필을 내보내는 중…");
            await App.DrawingSession.ExportCurrentProfileAsync(file.Path);
            ViewModel.StatusMessage = $"프로필을 '{file.Name}' 파일로 내보냈습니다.";
        }
        catch (Exception exception)
        {
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"프로필을 내보내지 못했습니다: {exception.Message}";
        }
        finally
        {
            ViewModel.EndLoading();
        }
    }

    private void CancelWork_Click(object sender, RoutedEventArgs e)
    {
        _preparationCancellation?.Cancel();
        _executionCancellation?.Cancel();
        App.DrawingSession.Stop();
        ViewModel.BusyMessage = "작업을 취소하는 중…";
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

        _preparationCancellation?.Cancel();
        _preparationCancellation?.Dispose();
        var preparationCancellation = new CancellationTokenSource();
        _preparationCancellation = preparationCancellation;
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
                SelectedRenderStyle(),
                SelectedSpeedMultiplier(),
                status,
                preparationCancellation.Token);
            ShowProcessedPreview(_preparedDrawing.Image.Quantized.Rendered);
            ViewModel.PlanSummary = _preparedDrawing.Summary;
            ViewModel.Stage = WorkspaceStage.Ready;
            ViewModel.StatusMessage = $"분석이 완료되었습니다. {_preparedDrawing.Summary}";
            return true;
        }
        catch (OperationCanceledException)
        {
            _preparedDrawing = null;
            ViewModel.Stage = ViewModel.HasImage ? WorkspaceStage.Configure : WorkspaceStage.SelectImage;
            ViewModel.StatusMessage = "이미지 분석을 취소했습니다.";
            return false;
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
            if (ReferenceEquals(_preparationCancellation, preparationCancellation))
            {
                _preparationCancellation.Dispose();
                _preparationCancellation = null;
                ViewModel.EndLoading();
            }
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
            ViewModel.IsExecutionPanelOpen = false;
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
        if (e.PropertyName is nameof(MainPageViewModel.SelectedMode)
            or nameof(MainPageViewModel.SelectedRenderStyle)
            or nameof(MainPageViewModel.SelectedSpeed)
            or nameof(MainPageViewModel.MaximumColors)
            or nameof(MainPageViewModel.LogicalWidth)
            or nameof(MainPageViewModel.LogicalHeight))
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

    private void UpdateResponsiveLayout(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        ViewModel.SetLayoutWidth(width);
        var mode = ViewModel.LayoutMode;
        var showStepRail = width >= 1180;
        var shortHeight = height < 760;
        StepRail.Visibility = showStepRail ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceLayout.ColumnDefinitions[0].Width = showStepRail
            ? new GridLength(mode == ResponsiveLayoutMode.Expanded ? 230 : 205)
            : new GridLength(0);
        WorkspaceLayout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        WorkspaceLayout.ColumnDefinitions[2].Width = new GridLength(width switch
        {
            >= 1350 => 370,
            >= 980 => 350,
            _ => 310
        });
        RootLayout.Padding = mode switch
        {
            ResponsiveLayoutMode.Expanded => new Thickness(24, 18, 24, 22),
            ResponsiveLayoutMode.Medium => new Thickness(18, 14, 18, 18),
            _ => new Thickness(12)
        };
        RootLayout.RowSpacing = shortHeight ? 8 : 12;
        WorkspaceLayout.ColumnSpacing = shortHeight ? 8 : 12;
        PreviewCard.Padding = shortHeight ? new Thickness(12) : new Thickness(18);
        HeaderSubtitle.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        StepRailHint.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        SafetyHint.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        PreviewDescription.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        FileHint.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        CalibrationHint.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        AnalysisDescriptionPanel.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        PreviewBadge.Visibility = width < 900 ? Visibility.Collapsed : Visibility.Visible;
        ExecutionOverlay.Margin = mode == ResponsiveLayoutMode.Compact ? new Thickness(10) : new Thickness(18);
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

    private DrawingRenderStyle SelectedRenderStyle()
        => ViewModel.SelectedRenderStyle == "검정 선화"
            ? DrawingRenderStyle.LineArt
            : DrawingRenderStyle.AutoColor;

    private double SelectedSpeedMultiplier()
        => ViewModel.SelectedSpeed switch
        {
            "안전하게" => 1d,
            "매우 빠르게" => 4d,
            _ => 2d
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
            bytes[offset] = CompositeOnWhite(pixel.Color.B, pixel.Alpha);
            bytes[offset + 1] = CompositeOnWhite(pixel.Color.G, pixel.Alpha);
            bytes[offset + 2] = CompositeOnWhite(pixel.Color.R, pixel.Alpha);
            bytes[offset + 3] = byte.MaxValue;
        }

        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Write(bytes, 0, bytes.Length);
        bitmap.Invalidate();
        PreviewImage.Source = bitmap;
        PreviewBadgeLabel.Text = "변환 미리보기";
    }

    private static byte CompositeOnWhite(byte channel, byte alpha)
        => (byte)(((channel * alpha) + (byte.MaxValue * (byte.MaxValue - alpha)) + 127) / byte.MaxValue);

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
