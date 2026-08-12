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

public interface IWindowsInputController : IPointerCaptureResetController
{
    /// <summary>
    /// Releases every mouse button and moves in one ordered native input
    /// batch. Game canvases must observe the release before the positioning
    /// move, even when both are consumed in the same rendered frame.
    /// </summary>
    async ValueTask MoveWithButtonsReleasedAsync(
        ScreenPoint point,
        CancellationToken cancellationToken = default)
    {
        await ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
        await MoveToAsync(point, cancellationToken).ConfigureAwait(false);
    }
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
