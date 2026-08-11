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
        await ViewModel.InitializeAsync();
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

    private async void CalibrateCanvas_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProfile is null)
        {
            await ShowMessageAsync("프로필을 먼저 만들어 주세요.", "Canvas Calibration");
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
            await ShowMessageAsync("프로필을 먼저 만들어 주세요.", "Adapter Calibration");
            return;
        }

        switch (profile.ColorAdapter.Kind)
        {
            case ColorAdapterKind.Manual:
                await ShowMessageAsync("Manual 모드에서는 게임에서 색상을 직접 선택한 뒤 Drawing을 시작합니다.", "Manual Adapter");
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
                    ("Black", "#1B1D21"),
                    ("White", "#FFFFFF"),
                    ("Red", "#E5484D"),
                    ("Orange", "#F08C46"),
                    ("Yellow", "#F2C94C"),
                    ("Green", "#43A047"),
                    ("Blue", "#4A7CFF"),
                    ("Purple", "#8E5BD9")
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
                await ShowMessageAsync("HSV Picker는 현재 프로필 JSON 구조와 기본 어댑터만 제공합니다. Hue/SV 영역 캘리브레이션은 다음 단계입니다.", "HSV Picker");
                break;
        }
    }

    private async void ColorAdapter_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (ColorAdapterComboBox.SelectedItem is ColorAdapterKind kind && ViewModel.SelectedProfile is not null)
        {
            await ViewModel.SetColorAdapterAsync(kind);
        }
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
