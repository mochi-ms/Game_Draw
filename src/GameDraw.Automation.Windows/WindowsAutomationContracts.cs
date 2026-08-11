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

public sealed record CapturedWindowFrame(
    TargetWindowSnapshot Target,
    PixelSize Size,
    DateTimeOffset CapturedAt,
    ReadOnlyMemory<byte> BgraPixels);

public interface IWindowsInputController : IInputController
{
}

public interface IWindowsHotkeyService
{
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    void Register(InputKey key);

    void UnregisterAll();
}

public sealed class HotkeyPressedEventArgs(InputKey key) : EventArgs
{
    public InputKey Key { get; } = key;
}
