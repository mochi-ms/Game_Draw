using System.Globalization;

namespace GameDraw.Core.Colors;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static RgbColor Parse(string value)
    {
        if (!TryParse(value, out var color))
        {
            throw new FormatException($"'{value}' is not a valid RGB hex color.");
        }

        return color;
    }

    public static bool TryParse(string? value, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
        {
            normalized = normalized[1..];
        }

        if (normalized.Length != 6 || !uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return false;
        }

        color = new RgbColor(
            (byte)((packed >> 16) & 0xFF),
            (byte)((packed >> 8) & 0xFF),
            (byte)(packed & 0xFF));
        return true;
    }

    public HsvColor ToHsv()
    {
        var r = R / 255d;
        var g = G / 255d;
        var b = B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var hue = 0d;
        if (delta > double.Epsilon)
        {
            hue = max switch
            {
                var value when Math.Abs(value - r) < double.Epsilon => 60d * (((g - b) / delta) % 6d),
                var value when Math.Abs(value - g) < double.Epsilon => 60d * (((b - r) / delta) + 2d),
                _ => 60d * (((r - g) / delta) + 4d)
            };

            if (hue < 0)
            {
                hue += 360;
            }
        }

        var saturation = max <= double.Epsilon ? 0d : delta / max;
        return new HsvColor(hue, saturation, max);
    }
}

public readonly record struct HsvColor(double Hue, double Saturation, double Value)
{
    public RgbColor ToRgb()
    {
        var h = ((Hue % 360d) + 360d) % 360d;
        var s = Math.Clamp(Saturation, 0d, 1d);
        var v = Math.Clamp(Value, 0d, 1d);
        var c = v * s;
        var x = c * (1d - Math.Abs((h / 60d % 2d) - 1d));
        var m = v - c;

        var (r, g, b) = h switch
        {
            < 60d => (c, x, 0d),
            < 120d => (x, c, 0d),
            < 180d => (0d, c, x),
            < 240d => (0d, x, c),
            < 300d => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return new RgbColor(
            ToByte(r + m),
            ToByte(g + m),
            ToByte(b + m));
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0d, 1d) * 255d);
}
