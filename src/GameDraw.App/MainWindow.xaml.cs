using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using WinRT.Interop;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GameDraw_App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private RectInt32? _normalBounds;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        ApplyCornerPreference(DwmWindowCornerPreference.Round);
        FitToCurrentDisplay();

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private void FitToCurrentDisplay()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (display is null)
        {
            return;
        }

        var workArea = display.WorkArea;
        var width = Math.Min(workArea.Width, Math.Max(900, (int)Math.Round(workArea.Width * 0.84)));
        var height = Math.Min(workArea.Height, Math.Max(680, (int)Math.Round(workArea.Height * 0.86)));
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + ((workArea.Height - height) / 2)));
    }

    public void SetFloatingMode(bool enabled)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (enabled)
        {
            _normalBounds ??= new RectInt32(
                AppWindow.Position.X,
                AppWindow.Position.Y,
                AppWindow.Size.Width,
                AppWindow.Size.Height);
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (display is not null)
            {
                var work = display.WorkArea;
                var width = Math.Min(work.Width, Math.Clamp((int)Math.Round(work.Width * 0.36d), 520, 660));
                var height = Math.Min(work.Height, Math.Clamp((int)Math.Round(work.Height * 0.72d), 560, 740));
                AppWindow.Resize(new SizeInt32(width, height));
                AppWindow.Move(new PointInt32(work.X + work.Width - width - 24, work.Y + 24));
            }
        }
        else if (_normalBounds is { } bounds)
        {
            AppWindow.Resize(new SizeInt32(bounds.Width, bounds.Height));
            AppWindow.Move(new PointInt32(bounds.X, bounds.Y));
            _normalBounds = null;
        }

        presenter.IsAlwaysOnTop = enabled;
        ApplyCornerPreference(enabled
            ? DwmWindowCornerPreference.RoundSmall
            : DwmWindowCornerPreference.Round);
    }

    private void ApplyCornerPreference(DwmWindowCornerPreference preference)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var handle = WindowNative.GetWindowHandle(this);
        var value = (int)preference;
        _ = DwmSetWindowAttribute(handle, 33, ref value, sizeof(int));
    }

    private enum DwmWindowCornerPreference
    {
        Round = 2,
        RoundSmall = 3
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);
}
