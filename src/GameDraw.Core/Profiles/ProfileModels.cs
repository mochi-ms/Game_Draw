using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;

namespace GameDraw.Core.Profiles;

public sealed record CanvasProfile
{
    public CanvasRect Bounds { get; init; } = new(0, 0, 800, 600);
    public int LogicalWidth { get; init; } = 64;
    public int LogicalHeight { get; init; } = 48;
}

public sealed record PaletteEntry
{
    public string Name { get; init; } = string.Empty;
    public RgbColor Color { get; init; }
    public ScreenPoint Position { get; init; }
}

public sealed record HsvCalibration
{
    public ScreenRect HueRegion { get; init; }
    public ScreenRect SaturationValueRegion { get; init; }
}

public sealed record ColorAdapterProfile
{
    public ColorAdapterKind Kind { get; init; } = ColorAdapterKind.Manual;
    public ScreenPoint? InputPosition { get; init; }
    public bool SelectAllBeforeTyping { get; init; } = true;
    public bool PressEnter { get; init; } = true;
    public int ClickDelayMs { get; init; } = 70;
    public int TypingDelayMs { get; init; } = 20;
    public int ApplyDelayMs { get; init; } = 100;
    public IReadOnlyList<PaletteEntry> Palette { get; init; } = Array.Empty<PaletteEntry>();
    public HsvCalibration? Hsv { get; init; }
}

public sealed record BrushProfile
{
    public BrushStrategy Strategy { get; init; } = BrushStrategy.Manual;
    public double LogicalPixelPitch { get; init; } = 1d;
    public double RecommendedSpacing { get; init; } = 6d;
}

public sealed record DelayProfile
{
    public int ClickDelayMs { get; init; } = 30;
    public int ColorChangeDelayMs { get; init; } = 120;
    public int InterStrokeDelayMs { get; init; } = 20;
}

public sealed record InputSamplingProfile
{
    public double MovementSpeedPixelsPerSecond { get; init; } = 900d;
    public double SampleSpacingPixels { get; init; } = 6d;
    public int MinimumStrokeDurationMs { get; init; } = 40;
}

public sealed record ScreenMetadataProfile
{
    public string MonitorDeviceName { get; init; } = string.Empty;
    public uint Dpi { get; init; } = 96;
    public double RasterizationScale { get; init; } = 1d;
    public string Notes { get; init; } = string.Empty;
}

public sealed record GameProfile
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "새 게임 프로필";
    public string GameName { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public CanvasProfile Canvas { get; init; } = new();
    public ColorAdapterProfile ColorAdapter { get; init; } = new();
    public BrushProfile Brush { get; init; } = new();
    public DelayProfile Delays { get; init; } = new();
    public double DrawingSpeed { get; init; } = 0.65d;
    public InputSamplingProfile InputSampling { get; init; } = new();
    public ScreenMetadataProfile ScreenMetadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static GameProfile CreateDefault(string name, string gameName) => new()
    {
        Id = Guid.NewGuid(),
        Name = string.IsNullOrWhiteSpace(name) ? "새 게임 프로필" : name.Trim(),
        GameName = gameName?.Trim() ?? string.Empty,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public ProfileValidationResult Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion <= 0)
        {
            errors.Add("schemaVersion은 1 이상이어야 합니다.");
        }

        if (Id == Guid.Empty)
        {
            errors.Add("프로필 ID가 비어 있습니다.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("프로필 이름을 입력하세요.");
        }

        if (!Canvas.Bounds.IsValid || Canvas.LogicalWidth <= 0 || Canvas.LogicalHeight <= 0)
        {
            errors.Add("Canvas 크기가 올바르지 않습니다.");
        }

        if (DrawingSpeed <= 0 || double.IsNaN(DrawingSpeed) || double.IsInfinity(DrawingSpeed))
        {
            errors.Add("DrawingSpeed는 0보다 커야 합니다.");
        }

        if (ColorAdapter.Kind == ColorAdapterKind.HexInput && ColorAdapter.InputPosition is null)
        {
            errors.Add("HEX Input 프로필에는 입력 위치가 필요합니다.");
        }

        if (ColorAdapter.Kind == ColorAdapterKind.FixedPalette && ColorAdapter.Palette.Count == 0)
        {
            errors.Add("Fixed Palette 프로필에는 하나 이상의 팔레트 항목이 필요합니다.");
        }

        if (ColorAdapter.Kind == ColorAdapterKind.HsvPicker && (ColorAdapter.Hsv is null || !ColorAdapter.Hsv.HueRegion.IsValid || !ColorAdapter.Hsv.SaturationValueRegion.IsValid))
        {
            errors.Add("HSV Picker 프로필에는 Hue와 SV 영역이 필요합니다.");
        }

        if (InputSampling.MovementSpeedPixelsPerSecond <= 0 || InputSampling.SampleSpacingPixels <= 0)
        {
            errors.Add("입력 샘플링 속성과 간격은 0보다 커야 합니다.");
        }

        return new ProfileValidationResult(errors);
    }
}

public sealed record ProfileValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
