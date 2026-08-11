namespace GameDraw.Core.Colors;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Black => new(0, 0, 0);
    public static RgbColor White => new(255, 255, 255);

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

        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], System.Globalization.NumberStyles.HexNumber, null, out var red) ||
            !byte.TryParse(normalized[2..4], System.Globalization.NumberStyles.HexNumber, null, out var green) ||
            !byte.TryParse(normalized[4..6], System.Globalization.NumberStyles.HexNumber, null, out var blue))
        {
            return false;
        }

        color = new RgbColor(red, green, blue);
        return true;
    }

    public double DistanceSquared(RgbColor other)
    {
        var red = R - other.R;
        var green = G - other.G;
        var blue = B - other.B;
        return (red * red) + (green * green) + (blue * blue);
    }
}
