using GameDraw.Core.Geometry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace GameDraw_App;

public sealed partial class ManualCropEditor : UserControl
{
    private readonly BitmapImage _bitmap = new();
    private Rect _imageBounds;
    private Point _dragStart;
    private uint _pointerId;
    private bool _dragging;

    public ManualCropEditor(string sourcePath, NormalizedRect? initialCrop = null)
    {
        InitializeComponent();
        InitialCrop = initialCrop is { IsWithinUnitSquare: true, Width: > 0.001d, Height: > 0.001d }
            ? initialCrop.Value
            : new NormalizedRect(0d, 0d, 1d, 1d);
        _bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        _bitmap.UriSource = new Uri(sourcePath, UriKind.Absolute);
        SourceImage.Source = _bitmap;
    }

    public NormalizedRect InitialCrop { get; }

    public NormalizedRect CurrentCrop
    {
        get
        {
            if (!_imageBounds.IsEmpty && SelectionRectangle.Width > 0d && SelectionRectangle.Height > 0d)
            {
                return new NormalizedRect(
                    Math.Clamp((Canvas.GetLeft(SelectionRectangle) - _imageBounds.X) / _imageBounds.Width, 0d, 1d),
                    Math.Clamp((Canvas.GetTop(SelectionRectangle) - _imageBounds.Y) / _imageBounds.Height, 0d, 1d),
                    Math.Clamp(SelectionRectangle.Width / _imageBounds.Width, 0d, 1d),
                    Math.Clamp(SelectionRectangle.Height / _imageBounds.Height, 0d, 1d));
            }

            return InitialCrop;
        }
    }

    private void SourceImage_ImageOpened(object sender, RoutedEventArgs e)
        => UpdateImageBounds(InitialCrop);

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateImageBounds(CurrentCrop);

    private void UpdateImageBounds(NormalizedRect crop)
    {
        if (_bitmap.PixelWidth == 0 || _bitmap.PixelHeight == 0 || Surface.ActualWidth <= 0d || Surface.ActualHeight <= 0d)
        {
            return;
        }

        var scale = Math.Min(Surface.ActualWidth / _bitmap.PixelWidth, Surface.ActualHeight / _bitmap.PixelHeight);
        var width = _bitmap.PixelWidth * scale;
        var height = _bitmap.PixelHeight * scale;
        _imageBounds = new Rect(
            (Surface.ActualWidth - width) / 2d,
            (Surface.ActualHeight - height) / 2d,
            width,
            height);
        SetSelection(new Rect(
            _imageBounds.X + (crop.X * width),
            _imageBounds.Y + (crop.Y * height),
            Math.Max(1d, crop.Width * width),
            Math.Max(1d, crop.Height * height)));
    }

    private void Overlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Overlay);
        if (!_imageBounds.Contains(point.Position))
        {
            return;
        }

        _dragStart = Clamp(point.Position);
        _pointerId = point.PointerId;
        _dragging = Overlay.CapturePointer(e.Pointer);
        SetSelection(new Rect(_dragStart.X, _dragStart.Y, 1d, 1d));
        e.Handled = true;
    }

    private void Overlay_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        var current = Clamp(e.GetCurrentPoint(Overlay).Position);
        SetSelection(CreateRect(_dragStart, current));
        e.Handled = true;
    }

    private void Overlay_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        var current = Clamp(e.GetCurrentPoint(Overlay).Position);
        var selection = CreateRect(_dragStart, current);
        if (selection.Width < 8d || selection.Height < 8d)
        {
            selection = _imageBounds;
        }

        SetSelection(selection);
        Overlay.ReleasePointerCapture(e.Pointer);
        _dragging = false;
        e.Handled = true;
    }

    private void Overlay_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        Overlay.ReleasePointerCapture(e.Pointer);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
        => SetSelection(_imageBounds);

    private Point Clamp(Point point)
        => new(
            Math.Clamp(point.X, _imageBounds.Left, _imageBounds.Right),
            Math.Clamp(point.Y, _imageBounds.Top, _imageBounds.Bottom));

    private static Rect CreateRect(Point first, Point second)
        => new(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Abs(first.X - second.X),
            Math.Abs(first.Y - second.Y));

    private void SetSelection(Rect selection)
    {
        if (_imageBounds.IsEmpty)
        {
            return;
        }

        Canvas.SetLeft(SelectionRectangle, selection.X);
        Canvas.SetTop(SelectionRectangle, selection.Y);
        SelectionRectangle.Width = selection.Width;
        SelectionRectangle.Height = selection.Height;
        var crop = CurrentCrop;
        CropStatus.Text = $"선택 영역 · {crop.Width:P0} × {crop.Height:P0} · 시작 {crop.X:P0}, {crop.Y:P0}";
    }
}
