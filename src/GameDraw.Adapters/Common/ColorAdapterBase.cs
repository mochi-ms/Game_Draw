using GameDraw.Core.Colors;
using GameDraw.Core.Models;
using GameDraw.Core.Profiles;

namespace GameDraw.Adapters.Common;

public abstract class ColorAdapterBase : IColorAdapter
{
    public abstract ColorAdapterKind Kind { get; }

    public abstract string DisplayName { get; }

    public abstract AdapterCapabilities Capabilities { get; }

    public virtual ProfileValidationResult Validate(ColorAdapterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Kind == Kind
            ? new ProfileValidationResult(Array.Empty<string>())
            : new ProfileValidationResult(new[] { $"프로필의 ColorAdapter Kind가 {Kind}와 일치하지 않습니다." });
    }

    public abstract ValueTask SelectColorAsync(
        RgbColor color,
        ColorAdapterProfile profile,
        GameDraw.Core.Drawing.IInputController input,
        CancellationToken cancellationToken = default);

    protected static ProfileValidationResult Combine(ProfileValidationResult first, IEnumerable<string> additionalErrors)
    {
        var errors = first.Errors.Concat(additionalErrors).ToArray();
        return new ProfileValidationResult(errors);
    }
}
