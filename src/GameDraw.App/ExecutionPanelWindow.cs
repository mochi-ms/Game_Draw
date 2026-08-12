using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using WinRT.Interop;
using Windows.Graphics;

namespace GameDraw_App;

/// <summary>
/// Compact always-on-top execution status. F7/F8 remain the fastest controls;
/// the reset button provides a visible recovery path and requests an immediate
/// input release before the workspace is cleared.
/// </summary>
public sealed class ExecutionPanelWindow : Window, IDisposable
{
    private readonly Border _surface;
    private readonly TextBlock _status;
    private readonly TextBlock _percentage;
    private readonly ProgressBar _progress;
    private const int PanelWidth = 420;
    private const int PanelHeight = 190;
    private bool _disposed;

    public event EventHandler? ResetRequested;

    public ExecutionPanelWindow()
    {
        Title = "GameDraw 실행 상태";
        // This window has no title bar. Extending content into a hidden title
        // bar leaves an additional WinUI backing surface which can flash as a
        // white square around the rounded XAML card.
        ExtendsContentIntoTitleBar = false;
        _status = new TextBlock
        {
            Text = "Roblox로 전환하세요.",
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            FontSize = 13,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 226, 232, 240))
        };
        _percentage = new TextBlock
        {
            Text = "0%",
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 142, 167, 255))
        };
        _progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0, Height = 4 };

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "GameDraw 실행 중",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        });
        Grid.SetColumn(_percentage, 1);
        header.Children.Add(_percentage);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(header);
        content.Children.Add(_status);
        content.Children.Add(_progress);
        content.Children.Add(new TextBlock
        {
            Text = "F5 시작  ·  F7 일시 정지/재개  ·  F8 즉시 중지",
            FontSize = 12,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(210, 203, 213, 225)),
            TextWrapping = TextWrapping.NoWrap
        });
        var reset = new Button
        {
            Content = "중지 후 작업 초기화",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 7, 12, 7)
        };
        reset.Click += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);
        content.Children.Add(reset);
        _surface = new Border
        {
            Padding = new Thickness(18, 14, 18, 12),
            CornerRadius = new CornerRadius(15),
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 22, 29, 43)),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 67, 78, 99)),
            BorderThickness = new Thickness(1),
            Child = content
        };
        _surface.Loaded += (_, _) => RefreshRoundedSurfaceAfterShow();
        _surface.SizeChanged += (_, _) => ReapplyRoundedSurface();
        Content = _surface;

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(PanelWidth, PanelHeight));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // Retaining the one-pixel non-client border lets DWM own the real
            // Windows 11 rounded silhouette. A fully borderless HWND is not
            // eligible for reliable DWM corner rounding and exposed the white
            // rectangular host behind the rounded XAML Border.
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        AppWindow.Changed += AppWindow_Changed;
        Activated += (_, _) => RefreshRoundedSurfaceAfterShow();

        Closed += (_, _) => _disposed = true;
    }

    public bool IsDisposed => _disposed;

    public long Handle => WindowNative.GetWindowHandle(this).ToInt64();

    public void ShowNearTopRight()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Activate();
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (display is not null)
        {
            var work = display.WorkArea;
            AppWindow.Move(new PointInt32(work.X + work.Width - 444, work.Y + 24));
        }

        // Activation and Move can each recreate the WinUI top-level surface.
        // Clip only after both operations, then repeat on the next dispatcher
        // turn so the square backing window never becomes the final shape.
        RefreshRoundedSurfaceAfterShow();
    }

    public void Update(string status, double progress)
    {
        if (_disposed)
        {
            return;
        }

        var value = double.IsFinite(progress) ? Math.Clamp(progress, 0d, 1d) : 0d;
        _status.Text = status;
        _progress.Value = value;
        _percentage.Text = $"{value * 100d:0}%";
    }

    public void Hide()
    {
        if (!_disposed)
        {
            AppWindow.Hide();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_disposed && (args.DidSizeChange || args.DidPositionChange))
        {
            ReapplyRoundedSurface();
        }
    }

    private void ReapplyRoundedSurface()
    {
        if (_disposed)
        {
            return;
        }

        var roundedByDwm = ApplyRoundedCorners();
        if (!roundedByDwm)
        {
            ClipWindowToRoundedRectangle();
        }
    }

    private async void RefreshRoundedSurfaceAfterShow()
    {
        // WinUI can recreate its top-level island after Activate/Move. Apply
        // once immediately and twice after composition has settled so a stale
        // rectangular region cannot become the final visible surface.
        ReapplyRoundedSurface();
        foreach (var delay in new[] { 60, 220 })
        {
            await Task.Delay(delay).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            ReapplyRoundedSurface();
        }
    }

    private bool ApplyRoundedCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return false;
        }

        var handle = WindowNative.GetWindowHandle(this);
        var darkMode = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        var preference = 2;
        var cornerResult = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        // COLORREF is BGR. Match the card's #434E63 stroke instead of relying
        // on the system's light default border, which caused the white square.
        var borderColor = 0x00634E43;
        _ = DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int));
        return cornerResult == 0;
    }

    private void ClipWindowToRoundedRectangle()
    {
        var handle = WindowNative.GetWindowHandle(this);
        if (!GetWindowRect(handle, out var bounds))
        {
            return;
        }

        var width = Math.Max(1, bounds.Right - bounds.Left);
        var height = Math.Max(1, bounds.Bottom - bounds.Top);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, 28, 28);
        if (region != nint.Zero && SetWindowRgn(handle, region, true) == 0)
        {
            _ = DeleteObject(region);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint window, nint region, bool redraw);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRect bounds);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint value);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
