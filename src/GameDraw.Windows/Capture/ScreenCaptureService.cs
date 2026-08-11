using GameDraw.Core.Geometry;

namespace GameDraw.Windows.Capture;

public interface IScreenCaptureService
{
    ScreenRect GetVirtualScreenBounds();
}

public sealed class ScreenCaptureService : IScreenCaptureService
{
    public ScreenRect GetVirtualScreenBounds() => Dpi.ScreenMetrics.GetVirtualScreenBounds();
}
