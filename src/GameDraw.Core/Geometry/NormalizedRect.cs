namespace GameDraw.Core.Geometry;

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public bool IsFinite =>
        double.IsFinite(X) &&
        double.IsFinite(Y) &&
        double.IsFinite(Width) &&
        double.IsFinite(Height);

    public bool IsWithinUnitSquare =>
        IsFinite &&
        X >= 0 &&
        Y >= 0 &&
        Width >= 0 &&
        Height >= 0 &&
        X + Width <= 1 &&
        Y + Height <= 1;

    public NormalizedPoint Center => new(X + (Width / 2d), Y + (Height / 2d));
}
