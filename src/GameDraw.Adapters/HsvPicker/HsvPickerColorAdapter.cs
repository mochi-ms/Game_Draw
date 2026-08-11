using GameDraw.Adapters.Common;
using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;

namespace GameDraw.Adapters.HsvPicker;

public sealed class HsvPickerColorAdapter : ColorAdapterBase
{
    public override ColorAdapterKind Kind => ColorAdapterKind.HsvPicker;

    public override string DisplayName => "HSV Picker";

    public override AdapterCapabilities Capabilities => AdapterCapabilities.SelectColor | AdapterCapabilities.RequiresCalibration | AdapterCapabilities.SupportsDirectInput;

    public override ProfileValidationResult Validate(ColorAdapterProfile profile)
    {
        var baseResult = base.Validate(profile);
        var errors = new List<string>();
        if (profile.Hsv is null || !profile.Hsv.HueRegion.IsValid || !profile.Hsv.SaturationValueRegion.IsValid)
        {
            errors.Add("Hue 영역과 Saturation/Value 영역을 등록해야 합니다.");
        }

        return Combine(baseResult, errors);
    }

    public override async ValueTask SelectColorAsync(RgbColor color, ColorAdapterProfile profile, IInputController input, CancellationToken cancellationToken = default)
    {
        var calibration = profile.Hsv ?? throw new InvalidOperationException("HSV 캘리브레이션이 설정되지 않았습니다.");
        var hsv = color.ToHsv();
        var huePoint = calibration.HueRegion.MapNormalized(new GameDraw.Core.Geometry.NormalizedPoint(hsv.Hue / 360d, 0.5d));
        var svPoint = calibration.SaturationValueRegion.MapNormalized(new GameDraw.Core.Geometry.NormalizedPoint(hsv.Saturation, 1d - hsv.Value));

        await input.ClickAsync(huePoint, InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
        await input.ClickAsync(svPoint, InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
        if (profile.ApplyDelayMs > 0)
        {
            await Task.Delay(profile.ApplyDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }
}
