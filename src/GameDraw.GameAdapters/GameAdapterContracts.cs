using GameDraw.Core.Colors;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Core.Targeting;
using GameDraw.Profiles;

namespace GameDraw.GameAdapters;

[Flags]
public enum GameAdapterCapabilities
{
    None = 0,
    CanvasCalibration = 1 << 0,
    ColorSelection = 1 << 1,
    BrushSelection = 1 << 2,
    FillTool = 1 << 3,
    VisualVerification = 1 << 4
}

public sealed record AdapterActionResult(bool Succeeded, string? Message = null);

public interface IColorAdapter
{
    ColorAdapterKind Kind { get; }

    string DisplayName { get; }

    ValueTask<AdapterActionResult> SelectColorAsync(
        RgbColor color,
        GameProfile profile,
        IInputController input,
        CancellationToken cancellationToken = default);
}

public interface IGameAdapter
{
    string Id { get; }

    string DisplayName { get; }

    GameAdapterCapabilities Capabilities { get; }

    IReadOnlyList<DrawingMode> SupportedModes { get; }

    GameProfile CreateDefaultProfile();

    ValueTask<TargetVerificationResult> VerifyAsync(
        TargetWindowSnapshot target,
        GameProfile profile,
        CancellationToken cancellationToken = default);
}

public interface IGameAdapterCatalog
{
    IReadOnlyList<IGameAdapter> Adapters { get; }
}
