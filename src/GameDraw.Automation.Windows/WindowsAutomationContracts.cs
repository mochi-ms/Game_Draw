using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Targeting;

namespace GameDraw.Automation.Windows;

public interface IWindowLocator
{
    Task<IReadOnlyList<TargetWindowSnapshot>> GetCandidatesAsync(
        CancellationToken cancellationToken = default);
}

public interface ITargetWindowCapture
{
    Task<CapturedWindowFrame?> CaptureAsync(
        TargetWindowSnapshot target,
        CancellationToken cancellationToken = default);
}

public interface IWindowGeometryProvider
{
    ValueTask<TargetWindowGeometry?> GetGeometryAsync(
        long handle,
        CancellationToken cancellationToken = default);
}

public interface ICursorPositionProvider
{
    bool TryGetScreenPosition(out ScreenPoint point);
}

public sealed record TargetWindowGeometry(
    TargetWindowSnapshot Snapshot,
    ScreenRect ClientBounds,
    uint Dpi)
{
    public bool IsValid
        => Snapshot.Handle != 0
            && ClientBounds.IsValid
            && Snapshot.ClientWidth > 0
            && Snapshot.ClientHeight > 0;
}

public sealed record CapturedWindowFrame(
    TargetWindowSnapshot Target,
    PixelSize Size,
    DateTimeOffset CapturedAt,
    ReadOnlyMemory<byte> BgraPixels);

public interface IWindowsInputController : IInputSafetyController
{
}

public interface IWindowsHotkeyService
{
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    void Register(InputKey key);

    void Unregister(InputKey key);

    void UnregisterAll();
}

public sealed class HotkeyPressedEventArgs(InputKey key) : EventArgs
{
    public InputKey Key { get; } = key;
}
