using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using GameDraw.Automation.Windows;
using GameDraw.Automation.Windows.Hotkeys;
using GameDraw.Core.Colors;
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
using Microsoft.UI.Xaml.Media.Animation;
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
    private bool _hexCaptureOnly;
    private bool _resetRequested;
    private bool _recordingPageVisible;

    public MainPage()
    {
        InitializeComponent();
        BuildVersionText.Text = $"설치 빌드 · {CurrentBuildVersion()}";
        RecordingLibraryPage.BackRequested += RecordingLibraryPage_BackRequested;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private static string CurrentBuildVersion()
    {
        var assembly = typeof(MainPage).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadata = informational.IndexOf('+');
            return metadata > 0 ? informational[..metadata] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "개발 빌드";
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

        RecordingLibraryPage.BackRequested -= RecordingLibraryPage_BackRequested;
        RecordingLibraryPage.Dispose();

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
        content.Children.Add(CreateHelpSection("자동 영상 기록", "F5로 그리기를 시작하면 지정한 캔버스 영역만 자동 녹화합니다. 100% 완료 직후의 캔버스가 썸네일로 저장되며, 상단의 영상 기록에서 재생·이름 변경·삭제 또는 저장 폴더 열기를 할 수 있습니다."));
        content.Children.Add(CreateHelpSection("빠른 시작", "① 이미지 선택 → ② 표현 방식·품질 선택 → ③ 이미지 분석 → ④ 캔버스 영역 드래그 → ⑤ F5로 시작합니다. F7은 일시정지, F8은 즉시 중지입니다."));
        content.Children.Add(CreateHelpSection("미리보기 기준", "분석 뒤 보이는 ‘실행 경로 미리보기’가 실제 마우스가 따라갈 선입니다. 원본 픽셀이 아니라 실행 스트로크를 직접 렌더링하므로 결과를 시작 전에 확인할 수 있습니다."));
        content.Children.Add(CreateHelpSection("AI 밑그림·안전 채색", "색상 사진 권장 모드입니다. 피사체의 실제 바깥 윤곽만 먼저 그린 다음, 원본 픽셀 컬러를 짧은 안전 점묘로 전부 덮습니다. 색 조각마다 검은 테두리를 남기거나 페인트 통을 쓰지 않으므로 최종 결과는 픽셀 컬러 미리보기에 가깝고 전체 번짐도 없습니다."));
        content.Children.Add(CreateHelpSection("원본 팔레트 256색 · AI 가속", "색상 수 입력값은 상한입니다. 최대 속도에서는 지각적으로 가까운 색만 최대 128색으로 자동 통합해 HEX 왕복을 절반으로 줄이고, 같은 색의 인접 점은 원본 색 영역 안에서만 짧게 묶습니다. 색을 바꾸기 전에는 제자리 펜 래치 리셋과 네 번의 프레임 분산 MouseUp을 실행해 캔버스를 가로지르는 연결선을 막습니다. 속도를 낮추면 요청한 최대 256색을 그대로 사용할 수 있습니다."));
        content.Children.Add(CreateHelpSection("작가식 정밀 선화", "사진의 단순 외곽선만 따지 않고 눈·입·코·머리카락 결·옷 주름 같은 세부 대비를 얇은 특징선으로 보존합니다. 머리카락과 그림자의 어두운 면은 점이 아니라 연결된 방향성 해칭으로 표현해 흑백 펜화처럼 묘사합니다."));
        content.Children.Add(CreateHelpSection("자연스러운 펜선", "인물·애니 캐릭터 권장 모드입니다. 굵은 원본 선의 양쪽 테두리가 아니라 암부의 중심선을 뽑아, 작가가 한 번씩 선을 긋는 것처럼 이중선과 털선을 줄입니다."));
        content.Children.Add(CreateHelpSection("1점 안전 점묘", "스트로크당 한 점만 찍습니다. 누른 상태에서는 포인터를 전혀 움직이지 않고, 제자리에서 MouseUp을 보낸 뒤 게임 프레임 대기가 끝나야만 다음 점으로 옮깁니다."));
        content.Children.Add(CreateHelpSection("정밀 윤곽선", "색·밝기 경계를 세밀하게 따는 모드입니다. 로고, 도형, 이미 완성된 흑백 선화에 적합하지만 사진에서는 작은 질감도 선으로 잡힐 수 있습니다."));
        content.Children.Add(CreateHelpSection("원본 색상 · 픽셀 컬러", "원본 색상은 같은 색 구간을 빠른 가로 획으로 채우고, 픽셀 컬러는 작은 도트 그림을 점 단위로 보존합니다. 연결 탭에서 도구·HEX 위치를 먼저 저장하세요. 펜 굵기는 게임에서 수동으로 맞추며 GameDraw는 슬라이더를 건드리지 않습니다. 색 변경은 HEX 입력란 클릭 → Ctrl+A → Delete → 재선택 → 클립보드 붙여넣기 → Enter 순서로 자동 실행됩니다."));
        content.Children.Add(CreateHelpSection("그림 품질", "속도 우선·추천·정밀·최고 정밀 순으로 가상 픽셀 수가 늘어납니다. 추천은 인물 선화의 비율과 작업 시간을 맞춘 기본값입니다."));
        content.Children.Add(CreateHelpSection("스마트 피사체 · 로컬 AI", "테두리에 연결된 배경을 투명하게 제거하고 피사체 중심으로 크롭합니다. 인물 구도로 판단되면 얼굴 영역을 찾아 디테일 순서에 반영합니다. 원본 펜선 보존 모드는 큰 외곽선을 먼저 그리고 얼굴 특징으로 넘어갑니다. 모든 분석은 PC 안에서 처리되며 이미지가 업로드되지 않습니다."));
        content.Children.Add(CreateHelpSection("속도와 정확도", "안전 확인은 MouseUp을 두 프레임 확인하고, 고속 점묘은 한 프레임, 최대 속도는 짧은 프레임 간격을 사용합니다. 모든 점묘 프리셋은 누른 채 이동하지 않습니다."));
        content.Children.Add(CreateHelpSection("연결과 안전", "캔버스 비율을 고르고 흰 그림판만 드래그해 지정합니다. 이 사각형이 실행 내내 고정된 가상 캔버스가 되며 자동 감지는 안전 확인에만 사용됩니다. F5 시작, F7 일시정지/재개, F8 즉시 마우스 해제·중지입니다."));
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

    private void QuickStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string description })
        {
            return;
        }

        QuickStepDetailText.Text = description;
        var slide = new DoubleAnimation
        {
            From = 22d,
            To = 0d,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, QuickStepDetailTransform);
        Storyboard.SetTargetProperty(slide, "X");
        var fade = new DoubleAnimation
        {
            From = 0.25d,
            To = 1d,
            Duration = TimeSpan.FromMilliseconds(200)
        };
        Storyboard.SetTarget(fade, QuickStepDetail);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        storyboard.Begin();
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

    private async void ShowRecordingLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingPageVisible)
        {
            CloseRecordingLibraryPage();
            return;
        }

        try
        {
            _recordingPageVisible = true;
            WorkspaceLayout.Visibility = Visibility.Collapsed;
            StatusBanner.Visibility = Visibility.Collapsed;
            RecordingLibraryPage.Visibility = Visibility.Visible;
            RecordingLibraryButton.Content = "그리기로 돌아가기";
            HeaderSubtitle.Text = "자동 그리기 영상과 완성 썸네일을 재생하고 관리하세요.";
            await RecordingLibraryPage.OpenAsync();
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = $"영상 기록 라이브러리를 열지 못했습니다: {exception.Message}";
        }
    }

    private void RecordingLibraryPage_BackRequested(object? sender, EventArgs e)
        => CloseRecordingLibraryPage();

    private void CloseRecordingLibraryPage()
    {
        if (!_recordingPageVisible)
        {
            return;
        }

        RecordingLibraryPage.Pause();
        RecordingLibraryPage.Visibility = Visibility.Collapsed;
        WorkspaceLayout.Visibility = Visibility.Visible;
        StatusBanner.Visibility = Visibility.Visible;
        _recordingPageVisible = false;
        RecordingLibraryButton.Content = ActualWidth < 900d ? "기록" : "영상 기록";
        HeaderSubtitle.Text = "이미지 선택부터 게임 자동 그리기까지 한 화면에서 진행하세요.";
    }

    private void ExecutionWindow_ResetRequested(object? sender, EventArgs e)
        => RequestWorkspaceReset();

    private void RequestWorkspaceReset()
    {
        _resetRequested = true;
        _preparationCancellation?.Cancel();
        if (_calibration is not null || _hexCaptureOnly)
        {
            _calibration?.Cancel();
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

    private async void HexOnlySetup_Click(object sender, RoutedEventArgs e)
        => await BeginHexOnlySetupAsync();

    private async void HexTest_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || ViewModel.IsCalibrating || App.DrawingSession.IsRunning)
        {
            return;
        }

        if (!ViewModel.IsColorToolsCalibrated)
        {
            ViewModel.StatusMessage = "먼저 HEX 입력칸 위치를 설정하세요.";
            return;
        }

        ViewModel.BeginLoading("HEX 자동 입력을 시험하는 중…");
        try
        {
            var status = new Progress<string>(message => ViewModel.StatusMessage = message);
            _ = await App.DrawingSession.TestHexColorAsync(new RgbColor(0xFF, 0x3B, 0x82), status);
            ViewModel.StatusMessage = "HEX 테스트 완료 · 게임의 현재 색상이 #FF3B82로 바뀌면 자동 색상 전환이 정상입니다.";
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = $"HEX 테스트 실패: {exception.Message}";
        }
        finally
        {
            App.Window.Activate();
            ViewModel.EndLoading();
        }
    }

    private async void BrushMeasure_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || ViewModel.IsCalibrating || App.DrawingSession.IsRunning ||
            !App.DrawingSession.CurrentProfile.Canvas.IsCalibrated)
        {
            return;
        }

        ViewModel.BeginLoading("Roblox와 펜 테스트 영역을 준비하는 중…");
        try
        {
            var target = await App.DrawingSession.FindPodiumsTargetAsync()
                ?? throw new InvalidOperationException("열려 있는 Roblox Podiums 창을 찾지 못했습니다.");
            _ = await App.DrawingSession.ActivateTargetAsync(target);
            var geometry = await App.DrawingSession.GetTargetGeometryAsync(target)
                ?? throw new InvalidOperationException("Roblox 창 좌표를 읽지 못했습니다.");
            ViewModel.EndLoading();
            App.Window.AppWindow.Hide();
            NormalizedRect? testRegion;
            var selector = new CanvasSelectionWindow(
                "펜 테스트용 빈 영역",
                null,
                "화이트보드 안의 빈 테스트 영역을 드래그하세요",
                "선택한 곳에 검정 점 3개를 찍어 실제 펜 지름을 자동 측정합니다.");
            try
            {
                testRegion = await selector.SelectAsync(geometry);
            }
            finally
            {
                selector.CloseAfterSelection();
            }

            if (testRegion is null)
            {
                App.Window.Activate();
                ViewModel.StatusMessage = "펜 굵기 측정을 취소했습니다.";
                return;
            }

            ViewModel.BeginLoading("테스트 점 3개를 찍고 실제 펜 지름을 측정하는 중…");
            var status = new Progress<string>(message => ViewModel.StatusMessage = message);
            var result = await App.DrawingSession.MeasureCurrentBrushAsync(testRegion.Value, status);
            ViewModel.BrushStatusLabel =
                $"측정됨 · 화면 {result.ScreenDiameterPixels:0.#}px · 가상 {result.LogicalDiameterPixels:0.##}px · 점 {result.SuccessfulDots}/3";
            _preparedDrawing = null;
            ViewModel.Stage = WorkspaceStage.Configure;
            ViewModel.StatusMessage =
                $"펜 굵기 측정 완료: {ViewModel.BrushStatusLabel}. 이제 이미지를 다시 분석하면 이 지름이 적용됩니다.";
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = $"자동 측정 실패: {exception.Message} 오른쪽의 펜 굵기 숫자를 입력하고 '직접 보정 적용'을 사용하세요.";
        }
        finally
        {
            App.Window.Activate();
            ViewModel.EndLoading();
        }
    }

    private async void ManualBrushApply_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || ViewModel.IsCalibrating || App.DrawingSession.IsRunning ||
            !App.DrawingSession.CurrentProfile.Canvas.IsCalibrated)
        {
            return;
        }

        ViewModel.BeginLoading("게임 펜 굵기를 가상 캔버스 칸으로 환산하는 중…");
        try
        {
            var target = await App.DrawingSession.FindPodiumsTargetAsync()
                ?? throw new InvalidOperationException("열려 있는 Roblox Podiums 창을 찾지 못했습니다.");
            var geometry = await App.DrawingSession.GetTargetGeometryAsync(target)
                ?? throw new InvalidOperationException("Roblox 창 좌표를 읽지 못했습니다.");
            var result = await App.DrawingSession.ApplyManualBrushDiameterAsync(
                ViewModel.ManualBrushDiameter,
                geometry);
            ViewModel.BrushStatusLabel =
                $"직접 보정됨 · 게임 {result.ScreenDiameterPixels:0.#} · 가상 캔버스 {result.LogicalDiameterPixels:0.##}칸";
            _preparedDrawing = null;
            ViewModel.Stage = WorkspaceStage.Configure;
            ViewModel.StatusMessage =
                $"펜 굵기 직접 보정 완료: {ViewModel.BrushStatusLabel}. 이미지를 다시 분석하면 이 값이 적용됩니다.";
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = $"펜 굵기 직접 보정 실패: {exception.Message}";
        }
        finally
        {
            ViewModel.EndLoading();
        }
    }

    private async Task BeginHexOnlySetupAsync()
    {
        if (ViewModel.IsBusy || ViewModel.IsCalibrating || App.DrawingSession.IsRunning ||
            !App.DrawingSession.CurrentProfile.Canvas.IsCalibrated)
        {
            return;
        }

        var existing = PodiumsProfileSettings.ReadControlLayout(App.DrawingSession.CurrentProfile);
        if (!existing.IsConfigured)
        {
            ViewModel.StatusMessage = "먼저 '도구 · HEX 위치 설정'으로 연필·지우개·채우기 도구를 한 번 저장하세요.";
            return;
        }

        ViewModel.BeginLoading("Roblox Podiums 창을 찾는 중…");
        try
        {
            var target = await App.DrawingSession.FindPodiumsTargetAsync();
            if (target is null)
            {
                ViewModel.StatusMessage = "열려 있는 Roblox Podiums 창을 찾지 못했습니다.";
                return;
            }

            _ = await App.DrawingSession.ActivateTargetAsync(target);
            _calibrationTarget = target;
            _calibration = null;
            _hexCaptureOnly = true;
            _hotkeys?.Register(InputKey.F6);
            ViewModel.IsCalibrating = true;
            ViewModel.Stage = WorkspaceStage.Configure;
            ViewModel.CalibrationMessage = "HEX 입력칸의 '#000000' 문자 가운데에 마우스를 올리고 F6을 누르세요.";
            ViewModel.StatusMessage = ViewModel.CalibrationMessage;
            ShowCalibrationPanel(ViewModel.CalibrationMessage);
        }
        catch (Exception exception)
        {
            FinishCalibration(false);
            ViewModel.StatusMessage = $"HEX 위치 설정을 시작하지 못했습니다: {exception.Message}";
        }
        finally
        {
            ViewModel.EndLoading();
        }
    }

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
            _hexCaptureOnly = false;
            ViewModel.IsCalibrating = true;
            ViewModel.Stage = WorkspaceStage.Configure;
            UpdateCalibrationMessage();
            ViewModel.StatusMessage = "도구 설정을 시작했습니다. 안내 위치에 마우스를 놓고 F6을 누르세요.";
            ShowCalibrationPanel(ViewModel.CalibrationMessage);
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

            var selectedPixelWidth = selectedCanvas.Value.Width * geometry.ClientBounds.Width;
            var selectedPixelHeight = selectedCanvas.Value.Height * geometry.ClientBounds.Height;
            var selectedAspect = selectedPixelWidth / Math.Max(1d, selectedPixelHeight);
            var logicalMaximum = Math.Max(
                SafeWholeNumber(ViewModel.LogicalWidth, 512, 1, 4096),
                SafeWholeNumber(ViewModel.LogicalHeight, 512, 1, 4096));
            var logicalWidth = selectedAspect >= 1d
                ? logicalMaximum
                : Math.Max(1, (int)Math.Round(logicalMaximum * selectedAspect));
            var logicalHeight = selectedAspect >= 1d
                ? Math.Max(1, (int)Math.Round(logicalMaximum / selectedAspect))
                : logicalMaximum;
            var canvas = new CanvasProfile
            {
                IsCalibrated = true,
                Bounds = selectedCanvas.Value,
                LogicalWidth = logicalWidth,
                LogicalHeight = logicalHeight
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

        if (!App.DrawingSession.CurrentProfile.Canvas.IsCalibrated)
        {
            ViewModel.StatusMessage = "이미지 분석 전에 먼저 Podiums 캔버스 영역을 연결하세요.";
            return false;
        }

        if (!App.DrawingSession.CurrentProfile.Brush.IsMeasured)
        {
            ViewModel.StatusMessage = "이미지 분석 전에 자동 측정을 하거나 게임의 펜 굵기 숫자를 입력해 직접 보정을 적용하세요.";
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
            var requestedColors = ViewModel.SelectedMode == "원본 팔레트 256색"
                ? 256
                : SafeWholeNumber(ViewModel.MaximumColors, 128, 2, 256);
            _preparedDrawing = await App.DrawingSession.PrepareAsync(
                ViewModel.SelectedImagePath,
                SelectedDrawingMode(),
                requestedColors,
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

        if (ViewModel.SelectedMode is "원본 색상" or "픽셀 컬러" or "AI 밑그림·안전 채색" or "AI 흑백 사진" or "스마트 윤곽·채우기" or "원본 팔레트 256색" && !ViewModel.IsColorToolsCalibrated)
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
        CanvasRecordingSession? recordingSession = null;
        DrawingExecutionResult? executionResult = null;
        string? recordingError = null;
        try
        {
            RegisterExecutionHotkeys();
            if (_executionWindow is null || _executionWindow.IsDisposed)
            {
                _executionWindow = new ExecutionPanelWindow();
                _executionWindow.ResetRequested += ExecutionWindow_ResetRequested;
            }
            _executionWindow.Update("Roblox Podiums 창을 자동으로 활성화하는 중입니다.", 0d);
            var protectedRegions = await App.DrawingSession.GetExecutionProtectedRegionsAsync(
                _executionCancellation.Token);
            if (protectedRegions.Count == 0)
            {
                throw new InvalidOperationException("Roblox Podiums 창의 캔버스 위치를 확인하지 못했습니다. 게임 창을 연 뒤 다시 시작하세요.");
            }

            _ = _executionWindow.ShowAvoidingProtectedRegions(protectedRegions);
            // Cross-process click-through is not guaranteed by
            // WS_EX_TRANSPARENT. The panel therefore avoids the canvas and
            // every calibrated control; this style is only a secondary guard.
            _executionWindow.SetInputPassThrough(true);
            ViewModel.Stage = WorkspaceStage.Ready;
            ViewModel.StatusMessage = "Roblox Podiums 창을 자동으로 활성화합니다. F8은 즉시 중지입니다.";
            App.Window.AppWindow.Hide();
            try
            {
                recordingSession = await App.RecordingLibrary.StartRecordingAsync(
                    protectedRegions[0],
                    _preparedDrawing!.SourcePath,
                    ViewModel.SelectedMode,
                    _executionCancellation.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Recording is an accessory feature and must never change the
                // established drawing/HEX timing if capture is unavailable.
                recordingError = exception.Message;
            }

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
            executionResult = await App.DrawingSession.ExecuteAsync(
                _preparedDrawing!,
                _executionWindow.Handle,
                progress,
                status,
                _executionCancellation.Token);
            ViewModel.SetExecutionState(
                executionResult.State,
                executionResult.ErrorMessage ?? (executionResult.State == DrawingExecutionState.Completed
                    ? "그리기가 완료되었습니다."
                    : "그리기가 중지되었습니다."));
            if (executionResult.State == DrawingExecutionState.Completed)
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
            if (recordingSession is not null)
            {
                try
                {
                    await recordingSession.StopAsync(
                        executionResult?.State == DrawingExecutionState.Completed,
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    recordingError = exception.Message;
                }

                await recordingSession.DisposeAsync();
            }

            if (_executionWindow is { IsDisposed: false })
            {
                _executionWindow.SetInputPassThrough(false);
            }
            _executionWindow?.Hide();
            App.Window.Activate();
            ViewModel.IsExecutionPanelOpen = false;
            UnregisterExecutionHotkeys();
            _executionCancellation?.Dispose();
            _executionCancellation = null;
            if (!string.IsNullOrWhiteSpace(recordingError))
            {
                ViewModel.StatusMessage += $" · 영상 저장 실패: {recordingError}";
            }
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
        RecordingLibraryButton.Content = _recordingPageVisible
            ? compact ? "돌아가기" : "그리기로 돌아가기"
            : compact ? "기록" : "영상 기록";
        RecordingLibraryButton.Padding = compact ? new Thickness(10, 8, 10, 8) : new Thickness(14, 8, 14, 8);
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
        if ((_calibration is null && !_hexCaptureOnly) ||
            _calibrationTarget is null ||
            !ViewModel.IsCalibrating)
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

            if (_hexCaptureOnly)
            {
                try
                {
                    var existing = PodiumsProfileSettings.ReadControlLayout(App.DrawingSession.CurrentProfile);
                    var updated = existing with
                    {
                        HexInput = point.Value,
                        HasColorControls = true
                    };
                    var result = PodiumsCalibrationSession.CreateManual(
                        App.DrawingSession.CurrentProfile.Canvas,
                        updated);
                    var profile = await App.DrawingSession.SaveCalibrationAsync(result, ViewModel.ProfileName);
                    SetProfileState(profile);
                    ViewModel.StatusMessage = "HEX 입력칸 위치를 저장했습니다. 색상 모드에서 해당 좌표를 직접 클릭합니다.";
                    FinishCalibration(true);
                }
                catch (Exception exception)
                {
                    ViewModel.CalibrationMessage = $"HEX 좌표 저장에 실패했습니다: {exception.Message}";
                    ShowCalibrationPanel(ViewModel.CalibrationMessage);
                }

                return;
            }

            var state = _calibration!.Capture(point.Value);
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
        _hexCaptureOnly = false;
        ViewModel.IsCalibrating = false;
        _executionWindow?.Hide();
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
        ShowCalibrationPanel(ViewModel.CalibrationMessage);
    }

    private void ShowCalibrationPanel(string message)
    {
        if (_executionWindow is null || _executionWindow.IsDisposed)
        {
            _executionWindow = new ExecutionPanelWindow();
            _executionWindow.ResetRequested += ExecutionWindow_ResetRequested;
        }

        _executionWindow.Update(message, 0d);
        _executionWindow.ShowNearTopRight();
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
        "AI 밑그림·안전 채색" or "스마트 윤곽·채우기" => DrawingMode.SmartFill,
        "AI 흑백 사진" => DrawingMode.SafeStamp,
        "원본 팔레트 256색" => DrawingMode.SafeStamp,
        "작가식 정밀 선화" => DrawingMode.ArtistStroke,
        "원본 색상" => DrawingMode.HorizontalScanline,
        "원본 펜선 보존" => DrawingMode.ArtistStroke,
        "안전 픽셀 스탬프" => DrawingMode.SafeStamp,
        "1점 안전 점묘" or "초고속 안전 점묘" or "자동 안전 점묘" => DrawingMode.SafeStamp,
        "고품질 망점 사진" => DrawingMode.HalftoneStamp,
        "픽셀 컬러" => DrawingMode.SafeStamp,
        _ => DrawingMode.CleanStroke
    };

    private DrawingRenderStyle SelectedRenderStyle() => ViewModel.SelectedMode switch
    {
        "AI 밑그림·안전 채색" or "스마트 윤곽·채우기" => DrawingRenderStyle.SmartPaint,
        "AI 흑백 사진" => DrawingRenderStyle.GrayscalePhoto,
        "원본 팔레트 256색" => DrawingRenderStyle.FullPalette,
        "작가식 정밀 선화" => DrawingRenderStyle.ArtistLineArt,
        "정밀 윤곽선" => DrawingRenderStyle.LineArt,
        "원본 펜선 보존" => DrawingRenderStyle.NaturalLineArt,
        "안전 픽셀 스탬프" => DrawingRenderStyle.NaturalLineArt,
        "1점 안전 점묘" or "초고속 안전 점묘" or "자동 안전 점묘" => DrawingRenderStyle.NaturalLineArt,
        "고품질 망점 사진" => DrawingRenderStyle.PhotoHalftone,
        "원본 색상" or "픽셀 컬러" => DrawingRenderStyle.AutoColor,
        _ => DrawingRenderStyle.NaturalLineArt
    };

    private DrawingQualityPreset SelectedQuality() => ViewModel.SelectedQuality switch
    {
        "빠른 초안" or "속도 우선" => DrawingQualityPreset.FastDraft,
        "고품질" or "정밀" => DrawingQualityPreset.High,
        "원본 우선" or "최고 정밀" => DrawingQualityPreset.OriginalPriority,
        _ => DrawingQualityPreset.Balanced
    };

    private double SelectedSpeedMultiplier() => ViewModel.SelectedSpeed switch
    {
        "안정" or "안전 확인" => 2d,
        "빠르게" or "고속 점묘" => 8d,
        _ => 10d
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
        ViewModel.BrushStatusLabel = profile.Brush.IsMeasured
            ? $"측정됨 · 화면 {profile.Brush.ScreenDiameterPixels:0.#}px · 가상 {profile.Brush.DiameterPixels:0.##}px"
            : "펜 굵기 미측정 · 자동 측정 또는 숫자 직접 보정을 사용하세요";
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
