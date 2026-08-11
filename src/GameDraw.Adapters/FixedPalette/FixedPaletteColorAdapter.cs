using GameDraw.Adapters.Common;
using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;

namespace GameDraw.Adapters.FixedPalette;

public sealed class FixedPaletteColorAdapter : ColorAdapterBase
{
    public override ColorAdapterKind Kind => ColorAdapterKind.FixedPalette;

    public override string DisplayName => "Fixed Palette";

    public override AdapterCapabilities Capabilities => AdapterCapabilities.SelectColor | AdapterCapabilities.RequiresCalibration | AdapterCapabilities.SupportsPaletteMapping;

    public override ProfileValidationResult Validate(ColorAdapterProfile profile)
    {
        var baseResult = base.Validate(profile);
        var errors = new List<string>();
        if (profile.Palette.Count == 0)
        {
            errors.Add("팔레트 버튼을 하나 이상 등록해야 합니다.");
        }

        if (profile.Palette.Any(entry => string.IsNullOrWhiteSpace(entry.Name)))
        {
            errors.Add("모든 팔레트 항목에 이름이 필요합니다.");
        }

        return Combine(baseResult, errors);
    }

    public override async ValueTask SelectColorAsync(RgbColor color, ColorAdapterProfile profile, IInputController input, CancellationToken cancellationToken = default)
    {
        var entry = profile.Palette
            .OrderBy(candidate => ColorMath.DeltaE76(color, candidate.Color))
            .FirstOrDefault();
        if (entry is null)
        {
            throw new InvalidOperationException("팔레트 항목이 등록되지 않았습니다.");
        }

        await input.ClickAsync(entry.Position, InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
        if (profile.ClickDelayMs > 0)
        {
            await Task.Delay(profile.ClickDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }
}
