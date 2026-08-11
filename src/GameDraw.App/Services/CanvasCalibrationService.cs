using GameDraw.Core.Geometry;
using GameDraw_App.Dialogs;

namespace GameDraw_App.Services;

public sealed class CanvasCalibrationService
{
    public async Task<CanvasRect?> SelectCanvasAsync()
    {
        var overlay = new CanvasSelectionOverlayWindow();
        return await overlay.SelectAsync();
    }

    public async Task<ScreenPoint?> SelectPointAsync(string instruction)
    {
        var overlay = new PointSelectionOverlayWindow(instruction);
        return await overlay.SelectAsync();
    }
}
