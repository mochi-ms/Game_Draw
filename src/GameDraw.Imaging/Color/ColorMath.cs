using GameDraw.Core.Colors;

namespace GameDraw.Imaging.Color;

/// <summary>
/// Color-space conversion helpers used by resampling, palette generation, and
/// perceptual color matching. All XYZ/Lab calculations use the D65 sRGB white
/// point.
/// </summary>
public static class ColorMath
{
    private const double Xn = 0.95047;
    private const double Yn = 1.00000;
    private const double Zn = 1.08883;

    public static LinearRgb ToLinear(RgbColor color)
        => new(
            SrgbToLinear(color.R / 255d),
            SrgbToLinear(color.G / 255d),
            SrgbToLinear(color.B / 255d));

    public static RgbColor FromLinear(LinearRgb color)
        => new(
            LinearToSrgbByte(color.R),
            LinearToSrgbByte(color.G),
            LinearToSrgbByte(color.B));

    public static LabColor ToLab(RgbColor color)
    {
        var linear = ToLinear(color);
        var x = (linear.R * 0.4124564) + (linear.G * 0.3575761) + (linear.B * 0.1804375);
        var y = (linear.R * 0.2126729) + (linear.G * 0.7151522) + (linear.B * 0.0721750);
        var z = (linear.R * 0.0193339) + (linear.G * 0.1191920) + (linear.B * 0.9503041);

        var fx = LabPivot(x / Xn);
        var fy = LabPivot(y / Yn);
        var fz = LabPivot(z / Zn);

        return new LabColor(
            (116 * fy) - 16,
            500 * (fx - fy),
            200 * (fy - fz));
    }

    public static double DeltaE(LabColor first, LabColor second, ColorDistanceMetric metric = ColorDistanceMetric.Cie76)
        => metric switch
        {
            ColorDistanceMetric.Cie76 => DeltaE76(first, second),
            ColorDistanceMetric.Cie2000 => DeltaE2000(first, second),
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown color distance metric.")
        };

    public static double DeltaE76(LabColor first, LabColor second)
    {
        var deltaL = first.L - second.L;
        var deltaA = first.A - second.A;
        var deltaB = first.B - second.B;
        return Math.Sqrt((deltaL * deltaL) + (deltaA * deltaA) + (deltaB * deltaB));
    }

    /// <summary>
    /// CIEDE2000 distance. The implementation follows the CIE publication's
    /// neutral, hue, chroma, and lightness weighting terms with kL=kC=kH=1.
    /// </summary>
    public static double DeltaE2000(LabColor first, LabColor second)
    {
        const double twentyFivePow7 = 6103515625d; // 25^7

        var c1 = Math.Sqrt((first.A * first.A) + (first.B * first.B));
        var c2 = Math.Sqrt((second.A * second.A) + (second.B * second.B));
        var meanC = (c1 + c2) / 2d;
        var g = 0.5 * (1d - Math.Sqrt(Math.Pow(meanC, 7) / (Math.Pow(meanC, 7) + twentyFivePow7)));

        var a1Prime = (1d + g) * first.A;
        var a2Prime = (1d + g) * second.A;
        var c1Prime = Math.Sqrt((a1Prime * a1Prime) + (first.B * first.B));
        var c2Prime = Math.Sqrt((a2Prime * a2Prime) + (second.B * second.B));
        var h1Prime = HueAngle(first.B, a1Prime);
        var h2Prime = HueAngle(second.B, a2Prime);

        var deltaLPrime = second.L - first.L;
        var deltaCPrime = c2Prime - c1Prime;
        var deltahPrime = HueDifference(h1Prime, h2Prime);
        var deltaHPrime = 2d * Math.Sqrt(c1Prime * c2Prime) * SinDegrees(deltahPrime / 2d);

        var meanL = (first.L + second.L) / 2d;
        var meanCPrime = (c1Prime + c2Prime) / 2d;
        var meanHPrime = MeanHue(h1Prime, h2Prime, c1Prime, c2Prime);

        var t = 1d
            - (0.17 * CosDegrees(meanHPrime - 30d))
            + (0.24 * CosDegrees(2d * meanHPrime))
            + (0.32 * CosDegrees((3d * meanHPrime) + 6d))
            - (0.20 * CosDegrees((4d * meanHPrime) - 63d));
        var deltaTheta = 30d * Math.Exp(-Math.Pow((meanHPrime - 275d) / 25d, 2d));
        var rC = 2d * Math.Sqrt(Math.Pow(meanCPrime, 7) / (Math.Pow(meanCPrime, 7) + twentyFivePow7));
        var sL = 1d + ((0.015 * Math.Pow(meanL - 50d, 2d)) / Math.Sqrt(20d + Math.Pow(meanL - 50d, 2d)));
        var sC = 1d + (0.045 * meanCPrime);
        var sH = 1d + (0.015 * meanCPrime * t);
        var rT = -SinDegrees(2d * deltaTheta) * rC;

        var lightnessTerm = deltaLPrime / sL;
        var chromaTerm = deltaCPrime / sC;
        var hueTerm = deltaHPrime / sH;

        return Math.Sqrt(
            (lightnessTerm * lightnessTerm)
            + (chromaTerm * chromaTerm)
            + (hueTerm * hueTerm)
            + (rT * chromaTerm * hueTerm));
    }

    public static RgbColor CompositeOver(RgbColor foreground, byte alpha, RgbColor background)
    {
        if (alpha == byte.MaxValue)
        {
            return foreground;
        }

        if (alpha == 0)
        {
            return background;
        }

        var amount = alpha / 255d;
        var fg = ToLinear(foreground);
        var bg = ToLinear(background);
        return FromLinear(new LinearRgb(
            (fg.R * amount) + (bg.R * (1d - amount)),
            (fg.G * amount) + (bg.G * (1d - amount)),
            (fg.B * amount) + (bg.B * (1d - amount))));
    }

    public static double SrgbToLinear(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value <= 0.04045d
            ? value / 12.92d
            : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
    }

    public static double LinearToSrgb(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value <= 0.0031308d
            ? 12.92d * value
            : (1.055d * Math.Pow(value, 1d / 2.4d)) - 0.055d;
    }

    public static byte LinearToSrgbByte(double value)
        => (byte)Math.Clamp(Math.Round(LinearToSrgb(value) * 255d, MidpointRounding.ToEven), 0d, 255d);

    private static double LabPivot(double value)
        => value > 0.008856451679d
            ? Math.Pow(value, 1d / 3d)
            : (7.787037037d * value) + (16d / 116d);

    private static double HueAngle(double b, double a)
    {
        if (a == 0d && b == 0d)
        {
            return 0d;
        }

        var angle = Math.Atan2(b, a) * 180d / Math.PI;
        return angle < 0d ? angle + 360d : angle;
    }

    private static double HueDifference(double first, double second)
    {
        var difference = second - first;
        if (Math.Abs(difference) <= 180d)
        {
            return difference;
        }

        return difference > 0d ? difference - 360d : difference + 360d;
    }

    private static double MeanHue(double first, double second, double firstChroma, double secondChroma)
    {
        if (firstChroma == 0d)
        {
            return second;
        }

        if (secondChroma == 0d)
        {
            return first;
        }

        if (Math.Abs(first - second) <= 180d)
        {
            return (first + second) / 2d;
        }

        return first + second < 360d
            ? (first + second + 360d) / 2d
            : (first + second - 360d) / 2d;
    }

    private static double SinDegrees(double degrees) => Math.Sin(degrees * Math.PI / 180d);

    private static double CosDegrees(double degrees) => Math.Cos(degrees * Math.PI / 180d);
}

public readonly record struct LinearRgb(double R, double G, double B);

public readonly record struct LabColor(double L, double A, double B);

public enum ColorDistanceMetric
{
    Cie76 = 0,
    Cie2000 = 1
}
