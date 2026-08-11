using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using GameDraw.Automation.Windows;
using GameDraw.Automation.Windows.Hotkeys;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Core.Presentation;
using GameDraw.Core.Targeting;
using GameDraw.GameAdapters.Podiums;
using GameDraw.GameAdapters.Podiums.Calibration;
using GameDraw.Profiles;
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
    private bool _resetRequested;

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
            _hotkeys.Register(InputKey.F5);
            await App.DrawingSession.InitializeAsync();
            var profile = App.DrawingSession.CurrentProfile;
            SetProfileState(profile);
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

        if (_executionWindow is not null)
        {
            _executionWindow.ResetRequested -= ExecutionWindow_ResetRequested;
            _executionWindow.Dispose();
        }
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
        var content = new StackPanel { Spacing = 10, MaxWidth = 620 };
        content.Children.Add(CreateHelpSection("빠른 시작", "① 이미지 선택 → ② 표현 방식·품질 선택 → ③ 이미지 분석 → ④ 캔버스 영역 드래그 → ⑤ F5로 시작합니다. F7은 일시정지, F8은 즉시 중지입니다."));
        content.Children.Add(CreateHelpSection("미리보기 기준", "분석 뒤 보이는 ‘실행 경로 미리보기’가 실제 마우스가 따라갈 선입니다. 원본 픽셀이 아니라 실행 스트로크를 직접 렌더링하므로 결과를 시작 전에 확인할 수 있습니다."));
        content.Children.Add(CreateHelpSection("자연스러운 펜선", "인물·애니 캐릭터 권장 모드입니다. 굵은 원본 선의 양쪽 테두리가 아니라 암부의 중심선을 뽑아, 작가가 한 번씩 선을 긋는 것처럼 이중선과 털선을 줄입니다."));
        content.Children.Add(CreateHelpSection("정밀 윤곽선", "색·밝기 경계를 세밀하게 따는 모드입니다. 로고, 도형, 이미 완성된 흑백 선화에 적합하지만 사진에서는 작은 질감도 선으로 잡힐 수 있습니다."));
        content.Children.Add(CreateHelpSection("원본 색상 · 픽셀 컬러", "원본 색상은 같은 색 구간을 빠른 가로 획으로 채우고, 픽셀 컬러는 작은 도트 그림을 점 단위로 보존합니다. 연결 탭에서 도구·HEX 위치를 먼저 저장하세요. 펜 굵기는 게임에서 수동으로 맞추며 GameDraw는 슬라이더를 건드리지 않습니다. 색 변경은 HEX 입력란 클릭 → Ctrl+A → Delete → 코드 입력 → Enter 순서로 자동 실행됩니다."));
        content.Children.Add(CreateHelpSection("그림 품질", "빠른 초안은 선 수를 크게 줄이고, 균형은 기본 권장값, 고품질은 얼굴·머리카락 세부를 늘리며, 원본 우선은 가장 촘촘한 경로를 만듭니다. 선택에 따라 미리보기와 실제 마우스 경로가 함께 바뀝니다."));
        content.Children.Add(CreateHelpSection("스마트 피사체 · 로컬 AI", "테두리에 연결된 배경을 투명하게 제거하고 피사체 중심으로 크롭합니다. 인물 구도로 판단되면 얼굴·눈이 있을 가능성이 높은 위쪽 중심 영역의 선을 먼저 그립니다. 모든 분석은 PC 안에서 처리되며 이미지가 업로드되지 않습니다."));
        content.Children.Add(CreateHelpSection("속도와 정확도", "매우 빠르게에서도 긴 벡터를 순간 이동하지 않고 게임이 인식할 수 있는 짧은 간격으로 보간합니다. 품질을 올리면 이 간격도 촘촘해져 더 정확하지만 시간이 늘어납니다."));
        content.Children.Add(CreateHelpSection("연결과 안전", "캔버스 비율을 고르고 흰 그림판만 드래그해 지정합니다. 실행 시 Roblox가 활성화되며 F5 시작, F7 일시정지/재개, F8 즉시 마우스 해제·중지입니다. ‘작업 초기화’는 진행 중 작업도 중지합니다."));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "GameDraw 사용법",
            Content = new ScrollViewer
            {
                MaxHeight = Math.Min(620, Math.Max(360, ActualHeight - 180)),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            },
            CloseButtonText = "확인",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private void ToggleFloating_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsFloating = !ViewModel.IsFloating;
        RootLayout.Visibility = ViewModel.IsFloating ? Visibility.Collapsed : Visibility.Visible;
        FloatingLayout.Visibility = ViewModel.IsFloating ? Visibility.Visible : Visibility.Collapsed;
        if (App.Window is MainWindow window)
        {
            window.SetFloatingMode(ViewModel.IsFloating);
        }

        ViewModel.StatusMessage = ViewModel.IsFloating
            ? "컴팩트 플로팅 화면으로 전환했습니다. 탭에서 이미지·연결·실행 기능을 사용할 수 있습니다."
            : "플로팅을 해제하고 원래 창 크기로 돌아왔습니다.";
        UpdateHeaderActions(ActualWidth);
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

    private static Border CreateHelpSection(string title, string description)
    {
        var panel = CreateHelpStep(title, description);
        return new Border
        {
            Padding = new Thickness(14, 11, 14, 11),
            CornerRadius = new CornerRadius(12),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(18, 112, 155, 255)),
            Child = panel
        };
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
            SetProfileState(profile);
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

    private void ResetWorkspace_Click(object sender, RoutedEventArgs e)
        => RequestWorkspaceReset();

    private void ExecutionWindow_ResetRequested(object? sender, EventArgs e)
        => RequestWorkspaceReset();

    private void RequestWorkspaceReset()
    {
        _resetRequested = true;
        _preparationCancellation?.Cancel();
        if (_calibration is not null)
        {
            _calibration.Cancel();
            FinishCalibration(false);
        }

        App.DrawingSession.Stop();
        _executionCancellation?.Cancel();
        if (_preparationCancellation is null && !App.DrawingSession.IsRunning)
        {
            CompleteWorkspaceReset();
        }
        else
        {
            ViewModel.StatusMessage = "현재 입력을 해제한 뒤 새 작업으로 초기화하는 중입니다…";
        }
    }

    private void CompleteWorkspaceReset()
    {
        _preparedDrawing = null;
        _executionWindow?.Hide();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        FloatingPreviewImage.Source = null;
        EmptyState.Visibility = Visibility.Visible;
        PreviewBadgeLabel.Text = "원본 미리보기";
        ViewModel.ResetWorkspace();
        _resetRequested = false;
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
            FloatingPreviewImage.Source = bitmap;
            PreviewBadgeLabel.Text = "원본 미리보기";
            PreviewImage.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            PreviewImage.Source = null;
            FloatingPreviewImage.Source = null;
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
        => await BeginProfileSetupAsync();

    private async void ToolSetup_Click(object sender, RoutedEventArgs e)
        => await BeginToolSetupAsync();

    private async Task BeginToolSetupAsync()
    {
        if (ViewModel.IsBusy || ViewModel.IsCalibrating || App.DrawingSession.IsRunning ||
            !App.DrawingSession.CurrentProfile.Canvas.IsCalibrated)
        {
            return;
        }

        ViewModel.BeginLoading("Roblox Podiums 창을 찾는 중…");
        try
        {
            var target = await App.DrawingSession.FindPodiumsTargetAsync();
            if (target is null)
            {
                ViewModel.StatusMessage = "열려 있는 Roblox Podiums 창을 찾지 못했습니다.";
                ViewModel.Stage = WorkspaceStage.Failed;
                return;
            }

            _ = await App.DrawingSession.ActivateTargetAsync(target);
            var canvas = App.DrawingSession.CurrentProfile.Canvas;
            _calibrationTarget = target;
            _calibration = new PodiumsCalibrationSession(new PodiumsCalibrationOptions
            {
                InitialCanvasBounds = canvas.Bounds,
                LogicalWidth = canvas.LogicalWidth,
                LogicalHeight = canvas.LogicalHeight,
                RequireControls = true,
                IncludeFillTool = true,
                IncludeBrushSize = false,
                IncludeColorControls = true
            });
            _hotkeys?.Register(InputKey.F6);
            ViewModel.IsCalibrating = true;
            ViewModel.Stage = WorkspaceStage.Configure;
            UpdateCalibrationMessage();
            ViewModel.StatusMessage = "도구 설정을 시작했습니다. 안내 위치에 마우스를 놓고 F6을 누르세요.";
        }
        catch (Exception exception)
        {
            FinishCalibration(false);
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"도구 설정을 시작하지 못했습니다: {exception.Message}";
        }
        finally
        {
            ViewModel.EndLoading();
        }
    }

    private async Task BeginProfileSetupAsync()
    {
        if (ViewModel.IsBusy || ViewModel.IsCalibrating || App.DrawingSession.IsRunning)
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

            _ = await App.DrawingSession.ActivateTargetAsync(target);
            var geometry = await App.DrawingSession.GetTargetGeometryAsync(target)
                ?? throw new InvalidOperationException("Roblox 창의 실제 화면 영역을 읽지 못했습니다.");
            ViewModel.EndLoading();
            var (presetLabel, aspectRatio) = SelectedCanvasAspect();
            App.Window.AppWindow.Hide();
            NormalizedRect? selectedCanvas;
            var selector = new CanvasSelectionWindow(presetLabel, aspectRatio);
            try
            {
                selectedCanvas = await selector.SelectAsync(geometry);
            }
            finally
            {
                App.Window.Activate();
                selector.CloseAfterSelection();
            }

            if (selectedCanvas is null)
            {
                ViewModel.Stage = _preparedDrawing is null ? WorkspaceStage.Configure : WorkspaceStage.Ready;
                ViewModel.StatusMessage = "캔버스 영역 선택을 취소했습니다.";
                return;
            }

            var canvas = new CanvasProfile
            {
                IsCalibrated = true,
                Bounds = selectedCanvas.Value,
                LogicalWidth = SafeWholeNumber(ViewModel.LogicalWidth, 512, 1, 4096),
                LogicalHeight = SafeWholeNumber(ViewModel.LogicalHeight, 512, 1, 4096)
            };
            var existingControls = PodiumsProfileSettings.ReadControlLayout(App.DrawingSession.CurrentProfile);
            var result = PodiumsCalibrationSession.CreateManual(canvas, existingControls);
            var profile = await App.DrawingSession.SaveCalibrationAsync(result, ViewModel.ProfileName);
            SetProfileState(profile);
            ViewModel.Stage = _preparedDrawing is null ? WorkspaceStage.Configure : WorkspaceStage.Ready;
            ViewModel.StatusMessage = "드래그한 캔버스 영역을 저장했습니다. 색상 모드는 도구·HEX 위치도 설정하세요.";
        }
        catch (Exception exception)
        {
            FinishCalibration(false);
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"캔버스 영역을 저장하지 못했습니다: {exception.Message}";
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
                SelectedQuality(),
                SelectedSpeedMultiplier(),
                ViewModel.SmartSubjectEnabled,
                status,
                preparationCancellation.Token);
            ShowProcessedPreview(_preparedDrawing.PlanPreview);
            ViewModel.PlanSummary = _preparedDrawing.Summary;
            ViewModel.Stage = WorkspaceStage.Ready;
            ViewModel.StatusMessage = $"분석이 완료되었습니다. {_preparedDrawing.Summary}";
            return true;
        }
        catch (OperationCanceledException)
        {
            _preparedDrawing = null;
            if (!_resetRequested)
            {
                ViewModel.Stage = ViewModel.HasImage ? WorkspaceStage.Configure : WorkspaceStage.SelectImage;
                ViewModel.StatusMessage = "이미지 분석을 취소했습니다.";
            }
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

            if (_resetRequested && !App.DrawingSession.IsRunning)
            {
                CompleteWorkspaceReset();
            }
        }
    }

    private async void StartDrawing_Click(object sender, RoutedEventArgs e)
        => await StartDrawingAsync();

    private async Task StartDrawingAsync()
    {
        if (App.DrawingSession.IsRunning || ViewModel.IsBusy || ViewModel.IsCalibrating)
        {
            return;
        }

        if (!ViewModel.HasImage)
        {
            ViewModel.StatusMessage = "먼저 그릴 이미지를 선택하세요.";
            return;
        }

        if (!ViewModel.IsProfileCalibrated)
        {
            ViewModel.StatusMessage = "먼저 Podiums 캔버스 영역을 연결하세요.";
            return;
        }

        if (ViewModel.SelectedMode is "원본 색상" or "픽셀 컬러" && !ViewModel.IsColorToolsCalibrated)
        {
            ViewModel.StatusMessage = "색상 모드는 도구·HEX 위치 설정이 필요합니다.";
            return;
        }

        if (_preparedDrawing is null && !await PrepareDrawingAsync())
        {
            return;
        }

        if (!ViewModel.CanStart)
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
                _executionWindow.ResetRequested += ExecutionWindow_ResetRequested;
            }
            _executionWindow.Update("Roblox Podiums 창을 자동으로 활성화하는 중입니다.", 0d);
            _executionWindow.ShowNearTopRight();
            ViewModel.Stage = WorkspaceStage.Ready;
            ViewModel.StatusMessage = "Roblox Podiums 창을 자동으로 활성화합니다. F8은 즉시 중지입니다.";
            App.Window.AppWindow.Hide();
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
            ViewModel.Stage = WorkspaceStage.Ready;
            ViewModel.StatusMessage = $"그리기를 실행하지 못했습니다: {exception.Message}";
            ViewModel.SetProgress(0d);
        }
        finally
        {
            _executionWindow?.Hide();
            App.Window.Activate();
            ViewModel.IsExecutionPanelOpen = false;
            UnregisterExecutionHotkeys();
            _executionCancellation?.Dispose();
            _executionCancellation = null;
            if (_resetRequested)
            {
                CompleteWorkspaceReset();
            }
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
            or nameof(MainPageViewModel.SelectedQuality)
            or nameof(MainPageViewModel.SelectedSpeed)
            or nameof(MainPageViewModel.SmartSubjectEnabled)
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
            FloatingPreviewImage.Source = null;
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
            >= 720 => 340,
            _ => Math.Clamp(width * 0.47d, 280d, 330d)
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
        HeaderSubtitle.Visibility = shortHeight || width < 980 ? Visibility.Collapsed : Visibility.Visible;
        StepRailHint.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        SafetyHint.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        PreviewDescription.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        FileHint.Visibility = shortHeight ? Visibility.Collapsed : Visibility.Visible;
        CalibrationHint.Visibility = height < 1120 ? Visibility.Collapsed : Visibility.Visible;
        AnalysisDescriptionPanel.Visibility = height < 1180 ? Visibility.Collapsed : Visibility.Visible;
        PreviewBadge.Visibility = width < 900 ? Visibility.Collapsed : Visibility.Visible;
        UpdateHeaderActions(width);
    }

    private void UpdateHeaderActions(double width)
    {
        var compact = width < 900d;
        HelpButton.Content = compact ? "?" : "사용법";
        HelpButton.Padding = compact ? new Thickness(10, 8, 10, 8) : new Thickness(14, 8, 14, 8);
        FloatingButton.Content = compact
            ? ViewModel.IsFloating ? "고정 해제" : "플로팅"
            : ViewModel.FloatingLabel;
        FloatingButton.Padding = compact ? new Thickness(10, 8, 10, 8) : new Thickness(14, 8, 14, 8);
        ResetButton.Content = compact ? "초기화" : "작업 초기화";
        ResetButton.Padding = compact ? new Thickness(10, 8, 10, 8) : new Thickness(14, 8, 14, 8);
        AdvancedSettingsButton.Content = compact ? "설정" : "고급 설정";
        AdvancedSettingsButton.Padding = compact ? new Thickness(10, 8, 10, 8) : new Thickness(14, 8, 14, 8);
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

    private async void StartExecution_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        await StartDrawingAsync();
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
                case InputKey.F5:
                    await StartDrawingAsync();
                    break;
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
                    SetProfileState(profile);
                    ViewModel.Stage = _preparedDrawing is not null
                        ? WorkspaceStage.Ready
                        : ViewModel.HasImage ? WorkspaceStage.Configure : WorkspaceStage.SelectImage;
                    ViewModel.StatusMessage = _preparedDrawing is not null
                        ? "Podiums 캔버스와 도구 위치를 저장했습니다. 바로 그리기를 시작할 수 있습니다."
                        : "Podiums 캔버스와 도구 위치를 저장했습니다. 이미지를 분석한 뒤 실행하세요.";
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
            ViewModel.Stage = _preparedDrawing is not null
                ? WorkspaceStage.Ready
                : ViewModel.HasImage ? WorkspaceStage.Configure : WorkspaceStage.SelectImage;
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
            PodiumsCalibrationStep.CapturePencilTool => "1/4 · 연필 도구 가운데에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureEraserTool => "2/4 · 지우개 도구 가운데에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureFillTool => "3/4 · 채우기 도구 가운데에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureBrushSizeMinimum => "4/6 · 굵기 슬라이더의 최소값 위치에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureBrushSizeMaximum => "5/6 · 굵기 슬라이더의 최대값 위치에 마우스를 놓고 F6",
            PodiumsCalibrationStep.CaptureHexInput => "4/4 · HEX 입력란 가운데에 마우스를 놓고 F6",
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

    private DrawingMode SelectedDrawingMode() => ViewModel.SelectedMode switch
    {
        "원본 색상" => DrawingMode.HorizontalScanline,
        "원본 펜선 보존" => DrawingMode.HorizontalScanline,
        "픽셀 컬러" => DrawingMode.Pixel,
        _ => DrawingMode.CleanStroke
    };

    private DrawingRenderStyle SelectedRenderStyle() => ViewModel.SelectedMode switch
    {
        "정밀 윤곽선" => DrawingRenderStyle.LineArt,
        "원본 펜선 보존" => DrawingRenderStyle.NaturalLineArt,
        "원본 색상" or "픽셀 컬러" => DrawingRenderStyle.AutoColor,
        _ => DrawingRenderStyle.NaturalLineArt
    };

    private DrawingQualityPreset SelectedQuality() => ViewModel.SelectedQuality switch
    {
        "빠른 초안" => DrawingQualityPreset.FastDraft,
        "고품질" => DrawingQualityPreset.High,
        "원본 우선" => DrawingQualityPreset.OriginalPriority,
        _ => DrawingQualityPreset.Balanced
    };

    private double SelectedSpeedMultiplier() => ViewModel.SelectedSpeed switch
    {
        "안정" => 2d,
        "빠르게" => 5d,
        _ => 8d
    };

    private (string Label, double? Ratio) SelectedCanvasAspect()
        => ViewModel.SelectedCanvasAspect switch
        {
            "자유 비율" => ("자유 비율", null),
            "4:3 가로형" => ("4:3 가로형", 4d / 3d),
            "16:9 가로형" => ("16:9 가로형", 16d / 9d),
            "원본 이미지 비율" when _preparedDrawing is { } prepared =>
                ("원본 이미지 비율", prepared.Image.WorkingFrame.Width / (double)prepared.Image.WorkingFrame.Height),
            "원본 이미지 비율" =>
                ("원본 이미지 비율", Math.Max(1d, ViewModel.LogicalWidth) / Math.Max(1d, ViewModel.LogicalHeight)),
            _ => ("1:1 정사각형", 1d)
        };

    private void SetProfileState(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ViewModel.SetProfileState(
            profile.Name,
            profile.Canvas.IsCalibrated,
            PodiumsProfileSettings.ReadControlLayout(profile).HasColorControls);
    }

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
        FloatingPreviewImage.Source = bitmap;
        PreviewBadgeLabel.Text = "실행 경로 미리보기";
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
