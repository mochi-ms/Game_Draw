using GameDraw.Core.Colors;

namespace GameDraw.Imaging.Palettes;

public sealed class ColorPalette
{
    private readonly RgbColor[] _colors;

    public ColorPalette(IEnumerable<RgbColor> colors, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(colors);
        _colors = colors.Distinct().ToArray();
        if (_colors.Length == 0)
        {
            throw new ArgumentException("팔레트에는 최소 하나의 색상이 필요합니다.", nameof(colors));
        }

        if (_colors.Length > 256)
        {
            throw new ArgumentException("팔레트는 최대 256색까지 지원합니다.", nameof(colors));
        }

        Name = name;
    }

    public string? Name { get; }

    public IReadOnlyList<RgbColor> Colors => _colors;

    public int Count => _colors.Length;

    public RgbColor this[int index] => _colors[index];
}
