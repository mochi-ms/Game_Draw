using GameDraw.Core.Geometry;
using GameDraw.Windows.Dpi;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace GameDraw_App.Dialogs;

public sealed class CanvasSelectionOverlayWindow : Window
{
    private readonly Canvas _canvas;
    private readonly Border _selectionBorder;
    private readonly TextBlock _coordinatesText;
    private readonly TaskCompletionSource<CanvasRect?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Point? _startPoint;
    private bool _completed;
    private ScreenRect _virtualBounds;

    public CanvasSelectionOverlayWindow()
    {
        _canvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 74, 126, 255)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(45, 74, 126, 255)),
            Visibility = Visibility.Collapsed
        };
        _coordinatesText = new TextBlock
        {
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 14,
            Margin = new Thickness(12, 4, 12, 4)
        };

        var instruction = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 17, 24, 39)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 28, 0, 0)
        };
        var instructionPanel = new StackPanel { Spacing = 8 };
        instructionPanel.Children.Add(new TextBlock
        {
            Text = "Canvas Calibration",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        instructionPanel.Children.Add(new TextBlock
        {
            Text = "그림 영역을 드래그하세요 · Enter 확정 · Esc 취소",
            Foreground = new SolidColorBrush(Color.FromArgb(220, 226, 232, 240))
        });
        instructionPanel.Children.Add(_coordinatesText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8 };
        var confirmButton = new Button { Content = "완료" };
        confirmButton.Click += (_, _) => Confirm();
        var cancelButton = new Button { Content = "취소" };
        cancelButton.Click += (_, _) => Cancel();
        buttons.Children.Add(confirmButton);
        buttons.Children.Add(cancelButton);
        instructionPanel.Children.Add(buttons);
        instruction.Child = instructionPanel;

        var root = new Grid { IsTabStop = true };
        root.Children.Add(_canvas);
        root.Children.Add(instruction);
        Content = root;
        root.KeyDown += Root_KeyDown;
        _canvas.PointerPressed += Canvas_PointerPressed;
        _canvas.PointerMoved += Canvas_PointerMoved;
        _canvas.PointerReleased += Canvas_PointerReleased;
        Closed += OnClosed;
    }

    public Task<CanvasRect?> SelectAsync()
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

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _startPoint = e.GetCurrentPoint(_canvas).Position;
        _selectionBorder.Visibility = Visibility.Visible;
        _canvas.Children.Clear();
        _canvas.Children.Add(_selectionBorder);
        UpdateSelection(_startPoint.Value, _startPoint.Value);
        _canvas.CapturePointer(e.Pointer);
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_startPoint is { } start)
        {
            UpdateSelection(start, e.GetCurrentPoint(_canvas).Position);
        }
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_startPoint is { } start)
        {
            UpdateSelection(start, e.GetCurrentPoint(_canvas).Position);
        }

        _canvas.ReleasePointerCapture(e.Pointer);
        _startPoint = null;
    }

    private void UpdateSelection(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);
        Canvas.SetLeft(_selectionBorder, left);
        Canvas.SetTop(_selectionBorder, top);
        _selectionBorder.Width = width;
        _selectionBorder.Height = height;
        var scale = _canvas.XamlRoot?.RasterizationScale ?? 1d;
        _coordinatesText.Text = $"X {(int)Math.Round(_virtualBounds.Left + (left * scale))}, Y {(int)Math.Round(_virtualBounds.Top + (top * scale))} · {(int)Math.Round(width * scale)}×{(int)Math.Round(height * scale)} px";
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            Confirm();
            e.Handled = true;
        }
    }

    private void Confirm()
    {
        var scale = _canvas.XamlRoot?.RasterizationScale ?? 1d;
        var left = Canvas.GetLeft(_selectionBorder);
        var top = Canvas.GetTop(_selectionBorder);
        var width = _selectionBorder.Width;
        var height = _selectionBorder.Height;
        if (_selectionBorder.Visibility != Visibility.Visible || width < 2 || height < 2)
        {
            return;
        }

        _completed = true;
        _completion.TrySetResult(new CanvasRect(
            _virtualBounds.Left + (int)Math.Round(left * scale),
            _virtualBounds.Top + (int)Math.Round(top * scale),
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale))));
        Close();
    }

    private void Cancel()
    {
        _completed = true;
        _completion.TrySetResult(null);
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!_completed)
        {
            _completion.TrySetResult(null);
        }
    }
}
