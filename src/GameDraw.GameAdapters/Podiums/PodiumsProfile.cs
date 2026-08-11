using System.Globalization;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Profiles;

namespace GameDraw.GameAdapters.Podiums;

public enum PodiumsToolKind
{
    Pencil = 0,
    Brush = 1,
    Fill = 2
}

/// <summary>
/// Normalized client coordinates for the controls that are unique to the
/// Podiums Roblox whiteboard. Coordinates are intentionally captured instead
/// of hard-coded so the profile survives window moves, resize, and DPI changes.
/// </summary>
public sealed record PodiumsControlLayout
{
    public bool IsConfigured { get; init; }

    public bool HasColorControls { get; init; }

    public bool HasFillTool { get; init; }

    public bool HasBrushSizeControl { get; init; }

    public NormalizedPoint PencilTool { get; init; }

    public NormalizedPoint BrushTool { get; init; }

    public NormalizedPoint FillTool { get; init; }

    public NormalizedPoint BrushSizeControl { get; init; }

    public NormalizedPoint HexInput { get; init; }

    public NormalizedPoint HexApply { get; init; }

    public int DefaultBrushSizePixels { get; init; } = 1;

    public static PodiumsControlLayout Unconfigured => new();

    public bool HasTool(PodiumsToolKind tool)
        => tool switch
        {
            PodiumsToolKind.Pencil => IsConfigured && PencilTool.IsWithinUnitSquare,
            PodiumsToolKind.Brush => IsConfigured && BrushTool.IsWithinUnitSquare,
            PodiumsToolKind.Fill => IsConfigured && HasFillTool && FillTool.IsWithinUnitSquare,
            _ => false
        };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!IsConfigured)
        {
            return errors;
        }

        if (!PencilTool.IsWithinUnitSquare)
        {
            errors.Add("Podiums pencil tool coordinate is outside the target client.");
        }

        if (!BrushTool.IsWithinUnitSquare)
        {
            errors.Add("Podiums brush tool coordinate is outside the target client.");
        }

        if (HasFillTool && !FillTool.IsWithinUnitSquare)
        {
            errors.Add("Podiums fill tool coordinate is outside the target client.");
        }

        if (HasBrushSizeControl && !BrushSizeControl.IsWithinUnitSquare)
        {
            errors.Add("Podiums brush-size control coordinate is outside the target client.");
        }

        if (HasColorControls &&
            (!HexInput.IsWithinUnitSquare || !HexApply.IsWithinUnitSquare))
        {
            errors.Add("Podiums HEX color control coordinates are outside the target client.");
        }

        if (DefaultBrushSizePixels <= 0)
        {
            errors.Add("Podiums default brush size must be greater than zero.");
        }

        return errors;
    }
}

/// <summary>
/// Codec for adapter-specific profile values. Keeping the values as strings
/// preserves the generic GameProfile schema while still giving the adapter a
/// strongly typed layout at runtime.
/// </summary>
public static class PodiumsProfileSettings
{
    private const string ConfiguredKey = "podiums.controls.configured";
    private const string ColorControlsKey = "podiums.controls.color";
    private const string FillToolKey = "podiums.controls.fill";
    private const string BrushSizeControlKey = "podiums.controls.brush-size";
    private const string BrushSizeValueKey = "podiums.brush.size";

    private const string PencilKey = "podiums.tool.pencil";
    private const string BrushKey = "podiums.tool.brush";
    private const string FillKey = "podiums.tool.fill";
    private const string BrushSizeKey = "podiums.tool.brush-size";
    private const string HexInputKey = "podiums.color.hex";
    private const string HexApplyKey = "podiums.color.apply";

    public static PodiumsControlLayout ReadControlLayout(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var values = profile.AdapterSettings;
        if (values is null)
        {
            return PodiumsControlLayout.Unconfigured;
        }

        var layout = new PodiumsControlLayout
        {
            IsConfigured = ReadBoolean(values, ConfiguredKey),
            HasColorControls = ReadBoolean(values, ColorControlsKey),
            HasFillTool = ReadBoolean(values, FillToolKey),
            HasBrushSizeControl = ReadBoolean(values, BrushSizeControlKey),
            PencilTool = ReadPoint(values, PencilKey),
            BrushTool = ReadPoint(values, BrushKey),
            FillTool = ReadPoint(values, FillKey),
            BrushSizeControl = ReadPoint(values, BrushSizeKey),
            HexInput = ReadPoint(values, HexInputKey),
            HexApply = ReadPoint(values, HexApplyKey),
            DefaultBrushSizePixels = ReadPositiveInt(values, BrushSizeValueKey, 1)
        };

        return layout;
    }

    public static GameProfile ApplyControlLayout(
        GameProfile profile,
        PodiumsControlLayout layout)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(layout);

        var values = profile.AdapterSettings is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(profile.AdapterSettings, StringComparer.OrdinalIgnoreCase);

        values[ConfiguredKey] = layout.IsConfigured.ToString();
        values[ColorControlsKey] = layout.HasColorControls.ToString();
        values[FillToolKey] = layout.HasFillTool.ToString();
        values[BrushSizeControlKey] = layout.HasBrushSizeControl.ToString();
        values[BrushSizeValueKey] = layout.DefaultBrushSizePixels.ToString(CultureInfo.InvariantCulture);
        values[PencilKey] = FormatPoint(layout.PencilTool);
        values[BrushKey] = FormatPoint(layout.BrushTool);
        values[FillKey] = FormatPoint(layout.FillTool);
        values[BrushSizeKey] = FormatPoint(layout.BrushSizeControl);
        values[HexInputKey] = FormatPoint(layout.HexInput);
        values[HexApplyKey] = FormatPoint(layout.HexApply);

        return profile with { AdapterSettings = values };
    }

    public static GameProfile CreateDefaultProfile(
        string name = "Podiums 기본 프로필",
        string gameName = "Podiums")
    {
        var profile = GameProfile.CreateDefault(name, gameName) with
        {
            Window = new WindowMatcher("RobloxPlayerBeta", "Roblox"),
            Canvas = CanvasProfile.Uncalibrated,
            ColorAdapter = new ColorAdapterProfile
            {
                Kind = ColorAdapterKind.HexInput,
                SupportsExactColor = true
            },
            Brush = new BrushProfile
            {
                DiameterPixels = 1d,
                PixelPitchPixels = 1d,
                SupportsFillTool = true
            },
            SupportedModes = new[]
            {
                DrawingMode.Auto,
                DrawingMode.Pixel,
                DrawingMode.HorizontalScanline,
                DrawingMode.VerticalScanline,
                DrawingMode.Contour,
                DrawingMode.Fill,
                DrawingMode.Hybrid
            }
        };

        return ApplyControlLayout(profile, PodiumsControlLayout.Unconfigured);
    }

    private static bool ReadBoolean(
        IReadOnlyDictionary<string, string> values,
        string key)
        => values.TryGetValue(key, out var value) &&
            bool.TryParse(value, out var result) &&
            result;

    private static int ReadPositiveInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int fallback)
        => values.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) &&
            result > 0
            ? result
            : fallback;

    private static NormalizedPoint ReadPoint(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return default;
        }

        var parts = value.Split(',', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            ? new NormalizedPoint(x, y)
            : default;
    }

    private static string FormatPoint(NormalizedPoint point)
        => string.Concat(
            point.X.ToString("R", CultureInfo.InvariantCulture),
            ",",
            point.Y.ToString("R", CultureInfo.InvariantCulture));
}
