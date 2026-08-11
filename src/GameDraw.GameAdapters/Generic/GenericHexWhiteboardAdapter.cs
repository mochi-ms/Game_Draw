using System.Globalization;
using GameDraw.Core.Colors;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Core.Targeting;
using GameDraw.Profiles;
using GameDraw.GameAdapters.Podiums;

namespace GameDraw.GameAdapters.Generic;

public sealed record GenericHexControlLayout(
    NormalizedPoint PencilTool,
    NormalizedPoint HexInput)
{
    public bool IsConfigured =>
        PencilTool.IsWithinUnitSquare && HexInput.IsWithinUnitSquare;
}

public static class GenericHexProfileSettings
{
    private const string PencilKey = "generic.pencil";
    private const string HexKey = "generic.hexInput";

    public static GameProfile CreateDefaultProfile()
        => GameProfile.CreateDefault("범용 HEX 화이트보드", "Generic HEX Whiteboard") with
        {
            ColorAdapter = new ColorAdapterProfile
            {
                Kind = ColorAdapterKind.HexInput,
                SupportsExactColor = true,
                PaletteSize = 256
            },
            Brush = new BrushProfile
            {
                DiameterPixels = 1,
                PixelPitchPixels = 1
            },
            VisualVerification = new VisualVerificationProfile { Enabled = false },
            SupportedModes = new[]
            {
                DrawingMode.Auto,
                DrawingMode.Pixel,
                DrawingMode.HorizontalScanline,
                DrawingMode.VerticalScanline,
                DrawingMode.Contour,
                DrawingMode.CleanStroke
            }
        };

    public static GameProfile ApplyLayout(GameProfile profile, GenericHexControlLayout layout)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(layout);
        if (!layout.IsConfigured)
        {
            throw new ArgumentException("범용 HEX 도구 좌표가 올바르지 않습니다.", nameof(layout));
        }

        var settings = new Dictionary<string, string>(profile.AdapterSettings, StringComparer.OrdinalIgnoreCase)
        {
            [PencilKey] = Format(layout.PencilTool),
            [HexKey] = Format(layout.HexInput)
        };
        return profile with { AdapterSettings = settings };
    }

    public static GenericHexControlLayout? ReadLayout(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return TryRead(profile.AdapterSettings, PencilKey, out var pencil)
            && TryRead(profile.AdapterSettings, HexKey, out var hex)
                ? new GenericHexControlLayout(pencil, hex)
                : null;
    }

    private static string Format(NormalizedPoint point)
        => $"{point.X.ToString("R", CultureInfo.InvariantCulture)},{point.Y.ToString("R", CultureInfo.InvariantCulture)}";

    private static bool TryRead(
        IReadOnlyDictionary<string, string> settings,
        string key,
        out NormalizedPoint point)
    {
        point = default;
        if (!settings.TryGetValue(key, out var value))
        {
            return false;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        var parsed = new NormalizedPoint(x, y);
        if (!parsed.IsWithinUnitSquare)
        {
            return false;
        }

        point = parsed;
        return true;
    }
}

public sealed class GenericHexWhiteboardAdapter : IGameAdapter
{
    public string Id => "generic.hex-whiteboard";

    public string DisplayName => "범용 HEX 화이트보드";

    public GameAdapterCapabilities Capabilities =>
        GameAdapterCapabilities.CanvasCalibration |
        GameAdapterCapabilities.ColorSelection |
        GameAdapterCapabilities.BrushSelection |
        GameAdapterCapabilities.PortableProfiles |
        GameAdapterCapabilities.CustomWindowTarget;

    public IReadOnlyList<DrawingMode> SupportedModes { get; } = new[]
    {
        DrawingMode.Auto,
        DrawingMode.Pixel,
        DrawingMode.HorizontalScanline,
        DrawingMode.VerticalScanline,
        DrawingMode.Contour,
        DrawingMode.CleanStroke
    };

    public GameProfile CreateDefaultProfile()
        => GenericHexProfileSettings.CreateDefaultProfile();

    public ValueTask<TargetVerificationResult> VerifyAsync(
        TargetWindowSnapshot target,
        GameProfile profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(profile);
        var issues = new List<TargetVerificationIssue>();
        if (target.Handle == 0 || target.ClientWidth <= 0 || target.ClientHeight <= 0)
        {
            issues.Add(new(TargetVerificationSeverity.Error, "TARGET_INVALID", "대상 창 영역이 올바르지 않습니다."));
        }

        if (!profile.Canvas.IsCalibrated)
        {
            issues.Add(new(TargetVerificationSeverity.Error, "CANVAS_NOT_CALIBRATED", "캔버스를 먼저 보정하세요."));
        }

        if (GenericHexProfileSettings.ReadLayout(profile) is null)
        {
            issues.Add(new(TargetVerificationSeverity.Error, "CONTROLS_NOT_CALIBRATED", "연필과 HEX 입력 위치를 먼저 보정하세요."));
        }

        return ValueTask.FromResult(new TargetVerificationResult(
            issues.All(issue => issue.Severity != TargetVerificationSeverity.Error),
            issues));
    }
}

public sealed class GenericHexExecutionHooks : IDrawingExecutionHooks
{
    private readonly GameProfile _profile;
    private readonly IGameAdapterExecutionContext _context;
    private readonly HexInputColorAdapter _hexColors = new(
        "범용 HEX 입력",
        profile => GenericHexProfileSettings.ReadLayout(profile)?.HexInput);

    public GenericHexExecutionHooks(GameProfile profile, IGameAdapterExecutionContext context)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async ValueTask BeforePlanAsync(
        GameDraw.Core.Drawing.DrawingPlan plan,
        CancellationToken cancellationToken = default)
    {
        var layout = GenericHexProfileSettings.ReadLayout(_profile)
            ?? throw new InvalidOperationException("범용 HEX 도구 위치가 보정되지 않았습니다.");
        await _context.Input.ClickAsync(_context.Map(layout.PencilTool), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask BeforeColorGroupAsync(
        RgbColor color,
        int colorGroupIndex,
        CancellationToken cancellationToken = default)
    {
        var result = await _hexColors.SelectColorAsync(color, _profile, _context, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Message ?? "HEX 색상 선택에 실패했습니다.");
        }
    }
}

public sealed class GameAdapterCatalog : IGameAdapterCatalog
{
    public GameAdapterCatalog()
    {
        Adapters = new IGameAdapter[]
        {
            new PodiumsGameAdapter(),
            new GenericHexWhiteboardAdapter()
        };
    }

    public IReadOnlyList<IGameAdapter> Adapters { get; }

    public IGameAdapter? Find(string id)
        => Adapters.FirstOrDefault(adapter => string.Equals(adapter.Id, id, StringComparison.OrdinalIgnoreCase));
}
