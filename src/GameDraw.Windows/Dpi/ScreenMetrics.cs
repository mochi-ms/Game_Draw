using System.Runtime.InteropServices;
using GameDraw.Core.Geometry;

namespace GameDraw.Windows.Dpi;

public static class ScreenMetrics
{
    private const int SystemMetricVirtualScreenLeft = 76;
    private const int SystemMetricVirtualScreenTop = 77;
    private const int SystemMetricVirtualScreenWidth = 78;
    private const int SystemMetricVirtualScreenHeight = 79;

    public static ScreenRect GetVirtualScreenBounds() => new(
        GetSystemMetrics(SystemMetricVirtualScreenLeft),
        GetSystemMetrics(SystemMetricVirtualScreenTop),
        GetSystemMetrics(SystemMetricVirtualScreenWidth),
        GetSystemMetrics(SystemMetricVirtualScreenHeight));

    public static uint GetWindowDpi(nint windowHandle) => GetDpiForWindow(windowHandle);

    public static ScreenPoint DipToPhysical(double x, double y, double rasterizationScale, ScreenPoint origin = default) => new(
        origin.X + (int)Math.Round(x * rasterizationScale),
        origin.Y + (int)Math.Round(y * rasterizationScale));

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);
}
