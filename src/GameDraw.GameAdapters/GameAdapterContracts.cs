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

/// <summary>
/// Runtime services exposed to a game adapter while a target window is bound.
/// The mapper deliberately accepts normalized client coordinates so adapters
/// never need to know about DPI, window movement, or virtual-screen offsets.
/// </summary>
public interface IGameAdapterExecutionContext
{
    IInputController Input { get; }

    TargetWindowSnapshot Target { get; }

    ScreenPoint Map(NormalizedPoint point);
}

public sealed class GameAdapterExecutionContext : IGameAdapterExecutionContext
{
    private readonly Func<NormalizedPoint, ScreenPoint> _mapper;

    public GameAdapterExecutionContext(
        IInputController input,
        TargetWindowSnapshot target,
        Func<NormalizedPoint, ScreenPoint> mapper)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public IInputController Input { get; }

    public TargetWindowSnapshot Target { get; }

    public ScreenPoint Map(NormalizedPoint point)
        => _mapper(point);
}

public interface IColorAdapter
{
    ColorAdapterKind Kind { get; }

    string DisplayName { get; }

    ValueTask<AdapterActionResult> SelectColorAsync(
        RgbColor color,
        GameProfile profile,
        IGameAdapterExecutionContext context,
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
