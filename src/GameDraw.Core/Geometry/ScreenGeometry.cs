namespace GameDraw.Core.Geometry;

public readonly record struct NormalizedPoint(double X, double Y)
{
    public NormalizedPoint Clamp() => new(Math.Clamp(X, 0d, 1d), Math.Clamp(Y, 0d, 1d));

    public double DistanceTo(NormalizedPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

public readonly record struct ScreenPoint(int X, int Y);

public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;

    public ScreenPoint MapNormalized(NormalizedPoint point)
    {
        var clamped = point.Clamp();
        return new ScreenPoint(
            Left + (int)Math.Round(clamped.X * Math.Max(0, Width - 1)),
            Top + (int)Math.Round(clamped.Y * Math.Max(0, Height - 1)));
    }
}

public readonly record struct CanvasRect(int Left, int Top, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;

    public ScreenPoint Map(NormalizedPoint point) => new ScreenRect(Left, Top, Width, Height).MapNormalized(point);

    public NormalizedPoint Normalize(ScreenPoint point)
    {
        if (Width <= 1 || Height <= 1)
        {
            return new NormalizedPoint(0d, 0d);
        }

        return new NormalizedPoint(
            (point.X - Left) / (double)(Width - 1),
            (point.Y - Top) / (double)(Height - 1)).Clamp();
    }
}

public static class CoordinateMapper
{
    public static ScreenPoint ToPhysical(CanvasRect canvas, NormalizedPoint logicalPoint) => canvas.Map(logicalPoint);

    public static NormalizedPoint ToNormalized(CanvasRect canvas, ScreenPoint physicalPoint) => canvas.Normalize(physicalPoint);
}
