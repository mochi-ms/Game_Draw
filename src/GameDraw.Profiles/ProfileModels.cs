using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;

namespace GameDraw.Profiles;

public enum ColorAdapterKind
{
    Manual = 0,
    HexInput = 1,
    FixedPalette = 2,
    HsvPicker = 3
}

public sealed record WindowMatcher(
    string? ProcessName = null,
    string? TitleContains = null)
{
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(ProcessName)
            || !string.IsNullOrWhiteSpace(TitleContains);
}

public sealed record CanvasProfile
{
    public bool IsCalibrated { get; init; }

    public NormalizedRect Bounds { get; init; }

    public int LogicalWidth { get; init; }

    public int LogicalHeight { get; init; }

    public static CanvasProfile Uncalibrated => new();
}

public sealed record ColorAdapterProfile
{
    public ColorAdapterKind Kind { get; init; } = ColorAdapterKind.Manual;

    public bool SupportsExactColor { get; init; }

    public int? PaletteSize { get; init; }
}

public sealed record BrushProfile
{
    public double DiameterPixels { get; init; } = 1d;

    public double PixelPitchPixels { get; init; } = 1d;

    public bool SupportsFillTool { get; init; }
}

public sealed record TimingProfile
{
    public double MovementPixelsPerSecond { get; init; } = 500d;

    public int InterStrokeDelayMilliseconds { get; init; } = 25;

    public int ColorChangeDelayMilliseconds { get; init; } = 100;
}

public sealed record GameProfile
{
    public int SchemaVersion { get; init; } = 1;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "새 프로필";

    public string GameName { get; init; } = "알 수 없는 게임";

    public WindowMatcher Window { get; init; } = new();

    public CanvasProfile Canvas { get; init; } = CanvasProfile.Uncalibrated;

    public ColorAdapterProfile ColorAdapter { get; init; } = new();

    public BrushProfile Brush { get; init; } = new();

    public TimingProfile Timing { get; init; } = new();

    public VisualVerificationProfile VisualVerification { get; init; } = new();

    /// <summary>
    /// Adapter-specific settings kept as string values so profiles remain
    /// portable and can be extended without changing the core schema for
    /// every supported game.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdapterSettings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DrawingMode> SupportedModes { get; init; } =
        new[]
        {
            DrawingMode.Auto,
            DrawingMode.Pixel,
            DrawingMode.HorizontalScanline,
            DrawingMode.VerticalScanline,
            DrawingMode.Contour,
            DrawingMode.Fill,
            DrawingMode.Hybrid,
            DrawingMode.CleanStroke
        };

    public static GameProfile CreateDefault(
        string name = "새 프로필",
        string gameName = "알 수 없는 게임")
        => new()
        {
            Name = name,
            GameName = gameName
        };

    public ProfileValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (SchemaVersion != 1)
        {
            errors.Add($"지원하지 않는 프로필 스키마 버전입니다: {SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("프로필 이름은 비워 둘 수 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(GameName))
        {
            errors.Add("게임 이름은 비워 둘 수 없습니다.");
        }

        if (!Canvas.IsCalibrated)
        {
            warnings.Add("캔버스가 아직 캘리브레이션되지 않았습니다.");
        }
        else
        {
            if (!Canvas.Bounds.IsWithinUnitSquare)
            {
                errors.Add("캔버스 영역은 정규화된 0~1 범위 안에 있어야 합니다.");
            }

            if (Canvas.LogicalWidth <= 0 || Canvas.LogicalHeight <= 0)
            {
                errors.Add("캔버스 논리 크기는 0보다 커야 합니다.");
            }
        }

        if (SupportedModes is null || SupportedModes.Count == 0)
        {
            errors.Add("최소 하나의 그리기 모드를 지원해야 합니다.");
        }

        if (ColorAdapter.PaletteSize is <= 0)
        {
            errors.Add("팔레트 크기는 0보다 커야 합니다.");
        }

        if (!double.IsFinite(Brush.DiameterPixels) || Brush.DiameterPixels <= 0)
        {
            errors.Add("브러시 지름은 유한한 양수여야 합니다.");
        }

        if (!double.IsFinite(Brush.PixelPitchPixels) || Brush.PixelPitchPixels <= 0)
        {
            errors.Add("픽셀 간격은 유한한 양수여야 합니다.");
        }

        if (!double.IsFinite(Timing.MovementPixelsPerSecond) || Timing.MovementPixelsPerSecond <= 0)
        {
            errors.Add("이동 속도는 유한한 양수여야 합니다.");
        }

        if (Timing.InterStrokeDelayMilliseconds < 0 || Timing.ColorChangeDelayMilliseconds < 0)
        {
            errors.Add("대기 시간은 음수가 될 수 없습니다.");
        }

        if (VisualVerification is null)
        {
            errors.Add("Visual verification settings are missing.");
        }
        else
        {
            errors.AddRange(VisualVerification.Validate());
        }

        return new ProfileValidationResult(errors, warnings);
    }
}

public sealed record ProfileValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

public interface IGameProfileStore
{
    Task<IReadOnlyList<GameProfile>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(GameProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}
