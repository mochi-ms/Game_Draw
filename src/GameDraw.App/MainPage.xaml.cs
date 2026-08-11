using System.ComponentModel;
using GameDraw_App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GameDraw_App;

/// <summary>
/// Phase-one workspace shell. It owns image selection and responsive layout;
/// planning and input execution are supplied by later layers.
/// </summary>
public sealed partial class MainPage : Page
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

    public MainPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth);
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
        try
        {
            if (!IsSupportedImage(path))
            {
                ViewModel.StatusMessage = "지원하는 이미지 형식이 아닙니다.";
                return;
            }

            ViewModel.SetImage(path);

            var bitmap = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache
            };
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            PreviewImage.Source = bitmap;
            PreviewImage.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ViewModel.Stage = WorkspaceStage.Failed;
            ViewModel.StatusMessage = $"이미지를 불러오지 못했습니다: {exception.Message}";
        }

        await Task.CompletedTask;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainPageViewModel.SelectedImagePath)
            && string.IsNullOrWhiteSpace(ViewModel.SelectedImagePath))
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
    }

    private void UpdateResponsiveLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var compact = width < 900;
        WorkspaceLayout.ColumnDefinitions[0].Width = compact
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(2, GridUnitType.Star);
        WorkspaceLayout.ColumnDefinitions[1].Width = compact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        WorkspaceLayout.RowDefinitions[0].Height = compact
            ? GridLength.Auto
            : new GridLength(1, GridUnitType.Star);
        WorkspaceLayout.RowDefinitions[1].Height = compact
            ? GridLength.Auto
            : new GridLength(0);

        Grid.SetColumn(ControlPanel, compact ? 0 : 1);
        Grid.SetRow(ControlPanel, compact ? 1 : 0);
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
