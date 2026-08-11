using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;

namespace GameDraw.Core.Colors;

[Flags]
public enum AdapterCapabilities
{
    None = 0,
    SelectColor = 1,
    RequiresCalibration = 2,
    SupportsPaletteMapping = 4,
    SupportsDirectInput = 8
}

public interface IColorAdapter
{
    ColorAdapterKind Kind { get; }
    string DisplayName { get; }
    AdapterCapabilities Capabilities { get; }
    ProfileValidationResult Validate(ColorAdapterProfile profile);
    ValueTask SelectColorAsync(RgbColor color, ColorAdapterProfile profile, IInputController input, CancellationToken cancellationToken = default);
}
