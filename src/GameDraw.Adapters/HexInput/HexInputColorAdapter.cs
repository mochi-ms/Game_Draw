using GameDraw.Adapters.Common;
using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;

namespace GameDraw.Adapters.HexInput;

public sealed class HexInputColorAdapter : ColorAdapterBase
{
    public override ColorAdapterKind Kind => ColorAdapterKind.HexInput;

    public override string DisplayName => "HEX Input";

    public override AdapterCapabilities Capabilities => AdapterCapabilities.SelectColor | AdapterCapabilities.RequiresCalibration | AdapterCapabilities.SupportsDirectInput;

    public override ProfileValidationResult Validate(ColorAdapterProfile profile)
    {
        var baseResult = base.Validate(profile);
        var errors = new List<string>();
        if (profile.InputPosition is not ScreenPoint)
        {
            errors.Add("HEX 입력 위치를 캘리브레이션해야 합니다.");
        }

        if (profile.ClickDelayMs < 0 || profile.TypingDelayMs < 0 || profile.ApplyDelayMs < 0)
        {
            errors.Add("HEX 입력 지연 시간은 음수가 될 수 없습니다.");
        }

        return Combine(baseResult, errors);
    }

    public override async ValueTask SelectColorAsync(RgbColor color, ColorAdapterProfile profile, IInputController input, CancellationToken cancellationToken = default)
    {
        var position = profile.InputPosition ?? throw new InvalidOperationException("HEX 입력 위치가 설정되지 않았습니다.");
        await input.ClickAsync(position, InputMouseButton.Left, cancellationToken).ConfigureAwait(false);
        await DelayAsync(profile.ClickDelayMs, cancellationToken).ConfigureAwait(false);

        if (profile.SelectAllBeforeTyping)
        {
            await input.KeyDownAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
            await input.KeyDownAsync(InputKey.A, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.A, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
        }

        await input.TypeTextAsync(color.ToHex(), cancellationToken).ConfigureAwait(false);
        await DelayAsync(profile.TypingDelayMs, cancellationToken).ConfigureAwait(false);
        if (profile.PressEnter)
        {
            await input.KeyDownAsync(InputKey.Enter, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.Enter, cancellationToken).ConfigureAwait(false);
        }

        await DelayAsync(profile.ApplyDelayMs, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
        }
    }
}
