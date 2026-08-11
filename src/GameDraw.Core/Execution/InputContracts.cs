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
    Delete = 8
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
