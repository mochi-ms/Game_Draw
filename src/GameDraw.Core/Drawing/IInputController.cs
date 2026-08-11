using GameDraw.Core.Geometry;

namespace GameDraw.Core.Drawing;

public enum InputMouseButton
{
    Left,
    Right,
    Middle
}

public enum InputKey
{
    Control,
    A,
    Enter,
    Escape
}

public interface IInputController
{
    ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default);
    ValueTask MouseDownAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default);
    ValueTask MouseUpAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default);
    ValueTask ClickAsync(ScreenPoint point, InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default);
    ValueTask KeyDownAsync(InputKey key, CancellationToken cancellationToken = default);
    ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default);
    ValueTask TypeTextAsync(string text, CancellationToken cancellationToken = default);
}
