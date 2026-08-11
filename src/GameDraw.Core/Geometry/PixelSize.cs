namespace GameDraw.Core.Geometry;

public readonly record struct PixelSize
{
    public PixelSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
}
