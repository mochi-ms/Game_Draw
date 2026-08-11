using GameDraw.Core.Colors;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;

namespace GameDraw.Core.Imaging;

public sealed record ImageProcessingOptions
{
    public int TargetWidth { get; init; } = 64;
    public int TargetHeight { get; init; } = 48;
    public FitMode Fit { get; init; } = FitMode.Contain;
    public int ColorCount { get; init; } = 12;
    public bool Dithering { get; init; }
    public BackgroundMode Background { get; init; } = BackgroundMode.IgnoreTransparent;
    public RgbColor CustomIgnoreColor { get; init; } = new(255, 255, 255);
    public ColorAdapterKind AdapterKind { get; init; } = ColorAdapterKind.Manual;
    public IReadOnlyList<PaletteEntry> Palette { get; init; } = Array.Empty<PaletteEntry>();
}
