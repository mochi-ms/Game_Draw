using GameDraw.Core.Geometry;
using GameDraw.Windows.Dpi;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace GameDraw_App.Dialogs;

public sealed class PointSelectionOverlayWindow : Window
{
    private readonly Grid _root;
    private readonly TextBlock _instructionText;
    private readonly TaskCompletionSource<ScreenPoint?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScreenRect _virtualBounds;
    private bool _completed;

    public PointSelectionOverlayWindow(string instruction)
    {
        _instructionText = new TextBlock
        {
            Text = instruction,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 720
        };
        var hint = new TextBlock
        {
            Text = "화면에서 한 번 클릭하세요 · Esc 취소",
            Foreground = new SolidColorBrush(Color.FromArgb(220, 226, 232, 240)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(_instructionText);
        panel.Children.Add(hint);
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 17, 24, 39)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 0, 0),
            Child = panel
        };

        _root = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
            IsTabStop = true
        };
        _root.Children.Add(card);
        Content = _root;
        _root.PointerPressed += Root_PointerPressed;
        _root.KeyDown += Root_KeyDown;
        Closed += OnClosed;
    }

    public Task<ScreenPoint?> SelectAsync()
    {
        _virtualBounds = ScreenMetrics.GetVirtualScreenBounds();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.MoveAndResize(new RectInt32(_virtualBounds.Left, _virtualBounds.Top, _virtualBounds.Width, _virtualBounds.Height));
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        appWindow.SetPresenter(presenter);
        Activate();
        return _completion.Task;
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root).Position;
        var scale = _root.XamlRoot?.RasterizationScale ?? 1d;
        _completed = true;
        _completion.TrySetResult(new ScreenPoint(
            _virtualBounds.Left + (int)Math.Round(point.X * scale),
            _virtualBounds.Top + (int)Math.Round(point.Y * scale)));
        Close();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _completed = true;
            _completion.TrySetResult(null);
            Close();
            e.Handled = true;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!_completed)
        {
            _completion.TrySetResult(null);
        }
    }
}
