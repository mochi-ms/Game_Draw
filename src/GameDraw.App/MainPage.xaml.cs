using System.ComponentModel;
using GameDraw.Core.Presentation;
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
        ApplyTheme(ViewModel.ThemeMode);
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
        ViewModel.BeginLoading("이미지를 불러오는 중…");
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

        finally
        {
            ViewModel.EndLoading();
        }
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

        if (e.PropertyName == nameof(MainPageViewModel.ThemeMode))
        {
            ApplyTheme(ViewModel.ThemeMode);
        }

        if (e.PropertyName == nameof(MainPageViewModel.IsExecutionPanelPinned))
        {
            ExecutionOverlay.Opacity = ViewModel.IsExecutionPanelPinned ? 1d : 0.88d;
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
        ViewModel.PauseOrResumeExecutionCommand.Execute(null);
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
        ViewModel.StopExecutionCommand.Execute(null);
        args.Handled = true;
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
