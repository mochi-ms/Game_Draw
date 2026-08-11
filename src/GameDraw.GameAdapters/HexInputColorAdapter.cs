using GameDraw.Core.Colors;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Profiles;

namespace GameDraw.GameAdapters;

/// <summary>
/// Reusable exact-color interaction for games that expose a HEX text field.
/// The adapter SDK only needs a profile-specific coordinate resolver.
/// </summary>
public sealed class HexInputColorAdapter : IColorAdapter
{
    private readonly Func<GameProfile, NormalizedPoint?> _coordinateResolver;

    public HexInputColorAdapter(
        string displayName,
        Func<GameProfile, NormalizedPoint?> coordinateResolver)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("표시 이름은 비워 둘 수 없습니다.", nameof(displayName))
            : displayName;
        _coordinateResolver = coordinateResolver ?? throw new ArgumentNullException(nameof(coordinateResolver));
    }

    public ColorAdapterKind Kind => ColorAdapterKind.HexInput;

    public string DisplayName { get; }

    public async ValueTask<AdapterActionResult> SelectColorAsync(
        RgbColor color,
        GameProfile profile,
        IGameAdapterExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        var coordinate = _coordinateResolver(profile);
        if (coordinate is null || !coordinate.Value.IsWithinUnitSquare)
        {
            return new(false, "HEX 색상 입력 위치가 보정되지 않았습니다.");
        }

        var input = context.Input;
        try
        {
            await input.ClickAsync(context.Map(coordinate.Value), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await input.KeyDownAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
            await input.KeyDownAsync(InputKey.A, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.A, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.Control, cancellationToken).ConfigureAwait(false);
            await input.TypeTextAsync(color.ToHex(), cancellationToken).ConfigureAwait(false);
            await input.KeyDownAsync(InputKey.Enter, cancellationToken).ConfigureAwait(false);
            await input.KeyUpAsync(InputKey.Enter, cancellationToken).ConfigureAwait(false);
            return new(true, $"{color.ToHex()} 색상을 선택했습니다.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, $"HEX 색상 선택에 실패했습니다: {exception.Message}");
        }
        finally
        {
            if (input is IInputSafetyController safety)
            {
                try
                {
                    await safety.ReleaseAllKeysAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort safety cleanup must not mask the original result.
                }
            }
        }
    }
}
