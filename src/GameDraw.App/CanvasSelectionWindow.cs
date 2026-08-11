using System.Runtime.InteropServices;
using GameDraw.Automation.Windows;
using GameDraw.Core.Geometry;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace GameDraw_App;

/// <summary>
/// A borderless overlay placed exactly over the target client. The selected
/// rectangle is returned in target-client normalized coordinates.
/// </summary>
public sealed class CanvasSelectionWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long LayeredWindowStyle = 0x00080000L;
    private const uint LayeredAlpha = 0x00000002;
    private readonly Canvas _surface;
    private readonly Rectangle _selection;
    private readonly TextBlock _selectionSize;
    private readonly double? _aspectRatio;
    private TaskCompletionSource<NormalizedRect?>? _completion;
    private Point _start;
    private bool _dragging;
    private bool _completing;
    private bool _closed;

    public CanvasSelectionWindow(string presetLabel, double? aspectRatio)
    {
        Title = "GameDraw 캔버스 영역 선택";
        _aspectRatio = aspectRatio.HasValue && aspectRatio.Value > 0d && double.IsFinite(aspectRatio.Value)
            ? aspectRatio.Value
            : null;

        _selection = new Rectangle
        {
            Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 112, 155, 255)),
            StrokeThickness = 4,
            Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(38, 112, 155, 255)),
            RadiusX = 8,
            RadiusY = 8,
            Visibility = Visibility.Collapsed
        };
        _selectionSize = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Visibility = Visibility.Collapsed
        };
        _surface = new Canvas
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 17, 24, 39))
        };
        _surface.Children.Add(_selection);
        _surface.Children.Add(_selectionSize);
        _surface.PointerPressed += Surface_PointerPressed;
        _surface.PointerMoved += Surface_PointerMoved;
        _surface.PointerReleased += Surface_PointerReleased;
        _surface.PointerCanceled += Surface_PointerCanceled;

        var cancel = new Button
        {
            Content = "취소 (Esc)",
            Padding = new Thickness(14, 7, 14, 7)
        };
        cancel.Click += (_, _) => Complete(null);

        var instructions = new StackPanel { Spacing = 3 };
        instructions.Children.Add(new TextBlock
        {
            Text = "흰색 그림 영역을 드래그하세요",
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        instructions.Children.Add(new TextBlock
        {
            Text = $"비율: {presetLabel} · 마우스를 놓으면 바로 저장됩니다.",
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(220, 226, 232, 240)),
            FontSize = 13
        });

        var instructionRow = new Grid { ColumnSpacing = 18 };
        instructionRow.ColumnDefinitions.Add(new ColumnDefinition());
        instructionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        instructionRow.Children.Add(instructions);
        Grid.SetColumn(cancel, 1);
        instructionRow.Children.Add(cancel);

        var instructionCard = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(20),
            Padding = new Thickness(18, 12, 12, 12),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(245, 22, 29, 43)),
            Child = instructionRow
        };

        var root = new Grid { IsTabStop = true };
        root.Children.Add(_surface);
        root.Children.Add(instructionCard);
        root.KeyDown += Root_KeyDown;
        Content = root;

        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        Closed += (_, _) =>
        {
            _closed = true;
            if (!_completing)
            {
                _completion?.TrySetResult(null);
            }
        };
    }

    public Task<NormalizedRect?> SelectAsync(TargetWindowGeometry target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsValid)
        {
            throw new ArgumentException("대상 창 영역이 올바르지 않습니다.", nameof(target));
        }

        if (_completion is not null)
        {
            throw new InvalidOperationException("캔버스 선택 창은 한 번만 사용할 수 있습니다.");
        }

        _completion = new TaskCompletionSource<NormalizedRect?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bounds = target.ClientBounds;
        AppWindow.Resize(new SizeInt32(bounds.Width, bounds.Height));
        AppWindow.Move(new PointInt32(bounds.X, bounds.Y));
        Activate();
        ApplyWindowOpacity(175);
        if (Content is Grid root)
        {
            root.Focus(FocusState.Programmatic);
        }

        return _completion.Task;
    }

    public void CloseAfterSelection()
    {
        if (!_closed)
        {
            Close();
        }
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_surface);
        if (point.Properties.IsRightButtonPressed)
        {
            Complete(null);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _start = Clamp(point.Position);
        _dragging = _surface.CapturePointer(e.Pointer);
        _selection.Visibility = Visibility.Visible;
        _selectionSize.Visibility = Visibility.Visible;
        UpdateSelection(_start);
        e.Handled = true;
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        UpdateSelection(Clamp(e.GetCurrentPoint(_surface).Position));
        e.Handled = true;
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _surface.ReleasePointerCapture(e.Pointer);
        _dragging = false;
        var rectangle = SelectionFrom(_start, Clamp(e.GetCurrentPoint(_surface).Position));
        if (rectangle.Width < 24d || rectangle.Height < 24d || _surface.ActualWidth <= 0d || _surface.ActualHeight <= 0d)
        {
            _selection.Visibility = Visibility.Collapsed;
            _selectionSize.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        Complete(new NormalizedRect(
            rectangle.X / _surface.ActualWidth,
            rectangle.Y / _surface.ActualHeight,
            rectangle.Width / _surface.ActualWidth,
            rectangle.Height / _surface.ActualHeight));
        e.Handled = true;
    }

    private void Surface_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            _surface.ReleasePointerCapture(e.Pointer);
            _dragging = false;
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            Complete(null);
            e.Handled = true;
        }
    }

    private void UpdateSelection(Point current)
    {
        var rectangle = SelectionFrom(_start, current);
        Canvas.SetLeft(_selection, rectangle.X);
        Canvas.SetTop(_selection, rectangle.Y);
        _selection.Width = rectangle.Width;
        _selection.Height = rectangle.Height;
        _selectionSize.Text = $"{rectangle.Width:0} × {rectangle.Height:0}";
        Canvas.SetLeft(_selectionSize, rectangle.X + 10d);
        Canvas.SetTop(_selectionSize, Math.Max(8d, rectangle.Y + rectangle.Height - 30d));
    }

    private Rect SelectionFrom(Point start, Point current)
    {
        var deltaX = current.X - start.X;
        var deltaY = current.Y - start.Y;
        var width = Math.Abs(deltaX);
        var height = Math.Abs(deltaY);
        if (_aspectRatio is { } ratio && width > 0d && height > 0d)
        {
            if (width / height > ratio)
            {
                width = height * ratio;
            }
            else
            {
                height = width / ratio;
            }
        }

        var x = deltaX >= 0d ? start.X : start.X - width;
        var y = deltaY >= 0d ? start.Y : start.Y - height;
        return new Rect(x, y, width, height);
    }

    private Point Clamp(Point point)
        => new(
            Math.Clamp(point.X, 0d, Math.Max(0d, _surface.ActualWidth)),
            Math.Clamp(point.Y, 0d, Math.Max(0d, _surface.ActualHeight)));

    private void Complete(NormalizedRect? result)
    {
        if (_completion is null || !_completion.TrySetResult(result))
        {
            return;
        }

        _completing = true;
        AppWindow.Hide();
    }

    private void ApplyWindowOpacity(byte alpha)
    {
        var handle = WindowNative.GetWindowHandle(this);
        var style = NativeMethods.GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        _ = NativeMethods.SetWindowLongPtr(handle, ExtendedStyleIndex, new nint(style | LayeredWindowStyle));
        _ = NativeMethods.SetLayeredWindowAttributes(handle, 0, alpha, LayeredAlpha);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern nint GetWindowLongPtr(nint windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static extern nint SetWindowLongPtr(nint windowHandle, int index, nint value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetLayeredWindowAttributes(
            nint windowHandle,
            uint colorKey,
            byte alpha,
            uint flags);
    }
}
