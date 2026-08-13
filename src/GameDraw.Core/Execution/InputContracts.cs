using GameDraw.Core.Geometry;

namespace GameDraw.Core.Execution;

public enum InputMouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2
}

public enum InputKey
{
    Control = 0,
    Enter = 1,
    Escape = 2,
    F6 = 3,
    F7 = 4,
    F8 = 5,
    A = 6,
    F5 = 7,
    Delete = 8,
    Backspace = 9,
    V = 10,
    C = 11
}

public interface IInputController
{
    ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default);

    ValueTask MouseDownAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default);

    ValueTask MouseUpAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default);

    ValueTask ClickAsync(
        ScreenPoint point,
        InputMouseButton button = InputMouseButton.Left,
        CancellationToken cancellationToken = default);

    ValueTask KeyDownAsync(InputKey key, CancellationToken cancellationToken = default);

    ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default);

    ValueTask TypeTextAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional safety surface implemented by input backends that can release any
/// held state without requiring a matching caller-side sequence.
/// </summary>
public interface IInputSafetyController : IInputController
{
    ValueTask ReleaseAllButtonsAsync(CancellationToken cancellationToken = default);

    ValueTask ReleaseAllKeysAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional native recovery surface for targets that can keep an internal
/// drag capture even after Windows has received a normal mouse-up event.
/// Implementations must return with the target foreground and every mouse
/// button released, or throw instead of allowing cursor travel to continue.
/// </summary>
public interface IPointerCaptureResetController : IInputSafetyController
{
    bool CanRepositionWithCaptureReset => true;

    ValueTask ResetPointerCaptureAsync(
        long targetWindowHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves to a disconnected destination while the target cannot observe
    /// pointer movement, then restores the target with every button released.
    /// </summary>
    async ValueTask RepositionWithCaptureResetAsync(
        long targetWindowHandle,
        ScreenPoint destination,
        CancellationToken cancellationToken = default)
    {
        await ResetPointerCaptureAsync(targetWindowHandle, cancellationToken).ConfigureAwait(false);
        await MoveToAsync(destination, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Optional input capability for controls that require a real clipboard paste
/// rather than a stream of synthetic Unicode key events.
/// </summary>
public interface IClipboardInputController : IInputController
{
    ValueTask SetClipboardTextAsync(
        string text,
        CancellationToken cancellationToken = default);

    ValueTask<string?> GetClipboardTextAsync(
        CancellationToken cancellationToken = default);
}
