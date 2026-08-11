namespace GameDraw.Core.Geometry;

public readonly record struct PixelPoint(int X, int Y)
{
    public bool IsNonNegative => X >= 0 && Y >= 0;
}
