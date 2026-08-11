using GameDraw.Core.Colors;

namespace GameDraw.Core.Imaging;

public readonly record struct RgbaPixel(RgbColor Color, byte Alpha)
{
    public static RgbaPixel Transparent => new(RgbColor.Black, 0);

    public bool IsTransparent => Alpha == 0;
    public bool IsOpaque => Alpha == byte.MaxValue;

    public static RgbaPixel Opaque(RgbColor color) => new(color, byte.MaxValue);
}
