namespace GameDraw.Core.Colors;

public static class ColorMath
{
    public static RgbColor FindNearest(RgbColor source, IEnumerable<RgbColor> palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        var candidates = palette.ToArray();
        if (candidates.Length == 0)
        {
            throw new ArgumentException("The palette must contain at least one color.", nameof(palette));
        }

        var best = candidates[0];
        var bestDistance = DeltaE76(source, best);
        foreach (var candidate in candidates[1..])
        {
            var distance = DeltaE76(source, candidate);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    public static double DeltaE76(RgbColor first, RgbColor second)
    {
        var a = ToLab(first);
        var b = ToLab(second);
        var dl = a.L - b.L;
        var da = a.A - b.A;
        var db = a.B - b.B;
        return Math.Sqrt((dl * dl) + (da * da) + (db * db));
    }

    private static LabColor ToLab(RgbColor color)
    {
        var r = ToLinear(color.R / 255d);
        var g = ToLinear(color.G / 255d);
        var b = ToLinear(color.B / 255d);

        var x = ((r * 0.4124d) + (g * 0.3576d) + (b * 0.1805d)) / 0.95047d;
        var y = ((r * 0.2126d) + (g * 0.7152d) + (b * 0.0722d)) / 1.00000d;
        var z = ((r * 0.0193d) + (g * 0.1192d) + (b * 0.9505d)) / 1.08883d;

        x = Pivot(x);
        y = Pivot(y);
        z = Pivot(z);

        return new LabColor((116d * y) - 16d, 500d * (x - y), 200d * (y - z));
    }

    private static double ToLinear(double value) => value <= 0.04045d
        ? value / 12.92d
        : Math.Pow((value + 0.055d) / 1.055d, 2.4d);

    private static double Pivot(double value) => value > 0.008856d
        ? Math.Pow(value, 1d / 3d)
        : (7.787d * value) + (16d / 116d);

    private readonly record struct LabColor(double L, double A, double B);
}
