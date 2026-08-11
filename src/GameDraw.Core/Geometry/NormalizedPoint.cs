namespace GameDraw.Core.Geometry;

public readonly record struct NormalizedPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    public bool IsWithinUnitSquare => IsFinite && X is >= 0 and <= 1 && Y is >= 0 and <= 1;

    public NormalizedPoint Clamped() => new(Math.Clamp(X, 0, 1), Math.Clamp(Y, 0, 1));
}
