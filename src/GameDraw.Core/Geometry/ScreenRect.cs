namespace GameDraw.Core.Geometry;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;

    public ScreenPoint TopLeft => new(X, Y);

    public ScreenPoint BottomRight => new(X + Width - 1, Y + Height - 1);
}
