using GameDraw.Adapters.Common;
using GameDraw.Core.Colors;
using GameDraw.Core.Drawing;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;

namespace GameDraw.Adapters.Manual;

public sealed class ManualColorAdapter : ColorAdapterBase
{
    public override ColorAdapterKind Kind => ColorAdapterKind.Manual;

    public override string DisplayName => "Manual";

    public override AdapterCapabilities Capabilities => AdapterCapabilities.None;

    public override ValueTask SelectColorAsync(RgbColor color, ColorAdapterProfile profile, IInputController input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
