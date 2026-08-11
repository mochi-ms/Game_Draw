using GameDraw.Core.Models;
using GameDraw.Core.Colors;
using GameDraw.Core.Profiles;
using GameDraw_App.Dialogs;
using GameDraw_App.Services;
using GameDraw_App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace GameDraw_App;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new(App.Services);

    public MainPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth);
        await ViewModel.InitializeAsync();
        PageScrollViewer.ChangeView(null, 0, null);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>().FirstOrDefault(item => IsSupportedImage(item.FileType));
        if (file is not null)
        {
            await ViewModel.LoadImageAsync(file.Path);
        }
    }

    private async void ConfigureProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileEditorDialog(ViewModel.SelectedProfile)
        {
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            await ViewModel.UpdateProfileIdentityAsync(dialog.ProfileName, dialog.GameName);
        }
    }

    private async void Help_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HelpDialog
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void CalibrateCanvas_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProfile is null)
        {
            await ShowMessageAsync("프로필을 먼저 만들어 주세요.", "캔버스 위치 설정");
            return;
        }

        var bounds = await App.Services.Calibration.SelectCanvasAsync();
        if (bounds is { } canvas)
        {
            await ViewModel.ApplyCanvasCalibrationAsync(canvas);
        }
    }

    private async void CalibrateAdapter_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProfile is not { } profile)
        {
            await ShowMessageAsync("프로필을 먼저 만들어 주세요.", "색상 위치 설정");
            return;
        }

        switch (profile.ColorAdapter.Kind)
        {
            case ColorAdapterKind.Manual:
                await ShowMessageAsync("수동 방식에서는 게임에서 색상을 직접 선택한 뒤 자동 그리기를 시작합니다.", "수동 색상 방식");
                break;
            case ColorAdapterKind.HexInput:
            {
                var point = await App.Services.Calibration.SelectPointAsync("HEX 입력 상자를 클릭해 위치를 등록하세요.");
                if (point is { } inputPosition)
                {
                    await ViewModel.ApplyHexInputCalibrationAsync(inputPosition);
                }

                break;
            }
            case ColorAdapterKind.FixedPalette:
            {
                var colors = new (string Name, string Hex)[]
                {
                    ("검정", "#1B1D21"),
                    ("흰색", "#FFFFFF"),
                    ("빨강", "#E5484D"),
                    ("주황", "#F08C46"),
                    ("노랑", "#F2C94C"),
                    ("초록", "#43A047"),
                    ("파랑", "#4A7CFF"),
                    ("보라", "#8E5BD9")
                };
                var palette = new List<PaletteEntry>();
                foreach (var color in colors)
                {
                    var point = await App.Services.Calibration.SelectPointAsync($"'{color.Name}' 색상 버튼을 클릭하세요.");
                    if (point is not { } buttonPosition)
                    {
                        return;
                    }

                    palette.Add(new PaletteEntry
                    {
                        Name = color.Name,
                        Color = RgbColor.Parse(color.Hex),
                        Position = buttonPosition
                    });
                }

                await ViewModel.ApplyFixedPaletteCalibrationAsync(palette);
                break;
            }
            case ColorAdapterKind.HsvPicker:
                await ShowMessageAsync("HSV 선택기는 현재 프로필 구조와 기본 어댑터만 제공합니다. Hue/SV 영역 설정은 다음 단계에서 추가됩니다.", "HSV 선택기");
                break;
        }
    }

    private async void ColorAdapter_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (ViewModel.SelectedProfile is { } profile && ColorAdapterComboBox.SelectedIndex >= 0)
        {
            var kind = (ColorAdapterKind)Math.Clamp(ColorAdapterComboBox.SelectedIndex, 0, Enum.GetValues<ColorAdapterKind>().Length - 1);
            if (profile.ColorAdapter.Kind != kind)
            {
                await ViewModel.SetColorAdapterAsync(kind);
            }
        }
    }

    private void UpdateResponsiveLayout(double width)
    {
        if (width <= 0 || MainLayout is null)
        {
            return;
        }

        var compact = width < 820;
        var stackedPreview = width < 600;

        MainLayout.ColumnDefinitions[0].Width = compact
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(2.1, GridUnitType.Star);
        MainLayout.ColumnDefinitions[1].Width = compact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        MainLayout.RowDefinitions[0].Height = compact
            ? GridLength.Auto
            : new GridLength(1, GridUnitType.Star);
        MainLayout.RowDefinitions[1].Height = compact
            ? GridLength.Auto
            : new GridLength(0);

        Grid.SetColumn(ControlRail, compact ? 0 : 1);
        Grid.SetRow(ControlRail, compact ? 1 : 0);
        PreviewArea.MinHeight = compact ? 440 : 560;

        PreviewColumns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        PreviewColumns.ColumnDefinitions[1].Width = stackedPreview
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        PreviewColumns.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        PreviewColumns.RowDefinitions[1].Height = stackedPreview
            ? GridLength.Auto
            : new GridLength(0);
        Grid.SetColumn(ProcessedCard, stackedPreview ? 0 : 1);
        Grid.SetRow(ProcessedCard, stackedPreview ? 1 : 0);
    }

    private async Task ShowMessageAsync(string message, string title)
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "확인",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private static bool IsSupportedImage(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" => true,
        _ => false
    };
}
