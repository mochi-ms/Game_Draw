namespace GameDraw.Core.Geometry;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public bool IsValid => X >= 0 && Y >= 0 && Width > 0 && Height > 0;

    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public PixelPoint TopLeft => new(X, Y);

    public PixelPoint Center => new(X + (Width / 2), Y + (Height / 2));

    public bool Contains(PixelPoint point)
        => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top
            ? default
            : new PixelRect(left, top, right - left, bottom - top);
    }
}
