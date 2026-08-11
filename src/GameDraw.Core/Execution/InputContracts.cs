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
    F7 = 3,
    F8 = 4,
    A = 5
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
