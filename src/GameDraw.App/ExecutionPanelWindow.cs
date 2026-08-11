using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace GameDraw_App;

/// <summary>
/// Read-only always-on-top execution status. Controls intentionally remain on
/// global F7/F8 hotkeys so clicking this window cannot steal Roblox focus in
/// the middle of a stroke.
/// </summary>
public sealed class ExecutionPanelWindow : Window, IDisposable
{
    private readonly TextBlock _status;
    private readonly TextBlock _percentage;
    private readonly ProgressBar _progress;
    private bool _disposed;

    public ExecutionPanelWindow()
    {
        Title = "GameDraw 실행 상태";
        _status = new TextBlock
        {
            Text = "Roblox로 전환하세요.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };
        _percentage = new TextBlock
        {
            Text = "0%",
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        _progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0 };

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "자동 그리기",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        Grid.SetColumn(_percentage, 1);
        header.Children.Add(_percentage);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(header);
        content.Children.Add(_status);
        content.Children.Add(_progress);
        content.Children.Add(new TextBlock
        {
            Text = "F7  일시 정지/재개     F8  즉시 중지",
            FontSize = 13,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });
        Content = new Border
        {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(20),
            RequestedTheme = ElementTheme.Light,
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Child = content
        };

        AppWindow.Resize(new SizeInt32(390, 220));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

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
            AppWindow.Move(new PointInt32(work.X + work.Width - 414, work.Y + 24));
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
}
