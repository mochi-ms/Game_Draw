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
    private readonly TextBlock _status;
    private readonly TextBlock _percentage;
    private readonly ProgressBar _progress;
    private bool _disposed;

    public event EventHandler? ResetRequested;

    public ExecutionPanelWindow()
    {
        Title = "GameDraw 실행 상태";
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
            Text = "F7  일시 정지/재개     F8  즉시 중지",
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
        Content = new Border
        {
            Padding = new Thickness(18, 14, 18, 12),
            CornerRadius = new CornerRadius(14),
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 22, 29, 43)),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 67, 78, 99)),
            BorderThickness = new Thickness(1),
            Child = content
        };

        AppWindow.Resize(new SizeInt32(420, 190));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        ApplyRoundedCorners();

        Closed += (_, _) => _disposed = true;
    }

    public bool IsDisposed => _disposed;

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

    private void ApplyRoundedCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var handle = WindowNative.GetWindowHandle(this);
        var preference = 3;
        _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);
}
