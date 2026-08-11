using GameDraw.Adapters.Common;
using GameDraw.Core.Drawing;
using GameDraw.Core.Imaging;
using GameDraw.Windows.Capture;
using GameDraw.Windows.Hotkeys;
using GameDraw.Windows.Input;
using Microsoft.UI.Xaml;

namespace GameDraw_App.Services;

public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        ImageProcessor = new ImageProcessor();
        DrawingPlanner = new DrawingPlanner();
        DrawingExecutor = new DrawingExecutor();
        ColorAdapters = new ColorAdapterRegistry();
        ProfileStore = new JsonProfileStore();
        FilePicker = new WinUiFilePickerService();
        PreviewRenderer = new PreviewRenderer();
        Calibration = new CanvasCalibrationService();
        InputController = new Win32InputController();
        Hotkeys = new GlobalHotkeyService();
        ScreenCapture = new ScreenCaptureService();
    }

    public ImageProcessor ImageProcessor { get; }

    public DrawingPlanner DrawingPlanner { get; }

    public DrawingExecutor DrawingExecutor { get; }

    public ColorAdapterRegistry ColorAdapters { get; }

    public JsonProfileStore ProfileStore { get; }

    public WinUiFilePickerService FilePicker { get; }

    public PreviewRenderer PreviewRenderer { get; }

    public CanvasCalibrationService Calibration { get; }

    public Win32InputController InputController { get; }

    public GlobalHotkeyService Hotkeys { get; }

    public ScreenCaptureService ScreenCapture { get; }

    public nint WindowHandle { get; private set; }

    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        Hotkeys.Attach(WindowHandle);
    }

    public void Dispose()
    {
        Hotkeys.Dispose();
        PreviewRenderer.Dispose();
    }
}
