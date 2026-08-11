using System.Globalization;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;
using GameDraw.Profiles;

namespace GameDraw.GameAdapters.Podiums;

public enum PodiumsToolKind
{
    Pencil = 0,
    Eraser = 1,
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

    public NormalizedPoint EraserTool { get; init; }

    public NormalizedPoint FillTool { get; init; }

    public NormalizedPoint BrushSizeMinimum { get; init; }

    public NormalizedPoint BrushSizeMaximum { get; init; }

    public NormalizedPoint HexInput { get; init; }

    public int MinimumBrushSizePixels { get; init; } = 1;

    public int MaximumBrushSizePixels { get; init; } = 50;

    public int DefaultBrushSizePixels { get; init; } = 10;

    public static PodiumsControlLayout Unconfigured => new();

    public bool HasTool(PodiumsToolKind tool)
        => tool switch
        {
            PodiumsToolKind.Pencil => IsConfigured && PencilTool.IsWithinUnitSquare,
            PodiumsToolKind.Eraser => IsConfigured && EraserTool.IsWithinUnitSquare,
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

        if (!EraserTool.IsWithinUnitSquare)
        {
            errors.Add("Podiums eraser tool coordinate is outside the target client.");
        }

        if (HasFillTool && !FillTool.IsWithinUnitSquare)
        {
            errors.Add("Podiums fill tool coordinate is outside the target client.");
        }

        if (HasBrushSizeControl &&
            (!BrushSizeMinimum.IsWithinUnitSquare || !BrushSizeMaximum.IsWithinUnitSquare))
        {
            errors.Add("Podiums brush-size slider endpoints are outside the target client.");
        }

        if (HasColorControls && !HexInput.IsWithinUnitSquare)
        {
            errors.Add("Podiums HEX color input coordinate is outside the target client.");
        }

        if (MinimumBrushSizePixels <= 0 ||
            MaximumBrushSizePixels < MinimumBrushSizePixels ||
            DefaultBrushSizePixels < MinimumBrushSizePixels ||
            DefaultBrushSizePixels > MaximumBrushSizePixels)
        {
            errors.Add("Podiums brush-size range or default value is invalid.");
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
    private const string BrushSizeMinimumValueKey = "podiums.brush.minimum";
    private const string BrushSizeMaximumValueKey = "podiums.brush.maximum";

    private const string PencilKey = "podiums.tool.pencil";
    private const string EraserKey = "podiums.tool.eraser";
    private const string LegacyBrushKey = "podiums.tool.brush";
    private const string FillKey = "podiums.tool.fill";
    private const string BrushSizeMinimumKey = "podiums.tool.brush-size-minimum";
    private const string BrushSizeMaximumKey = "podiums.tool.brush-size-maximum";
    private const string LegacyBrushSizeKey = "podiums.tool.brush-size";
    private const string HexInputKey = "podiums.color.hex";

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
            EraserTool = ReadPoint(values, EraserKey, LegacyBrushKey),
            FillTool = ReadPoint(values, FillKey),
            BrushSizeMinimum = ReadPoint(values, BrushSizeMinimumKey, LegacyBrushSizeKey),
            BrushSizeMaximum = ReadPoint(values, BrushSizeMaximumKey, LegacyBrushSizeKey),
            HexInput = ReadPoint(values, HexInputKey),
            MinimumBrushSizePixels = ReadPositiveInt(values, BrushSizeMinimumValueKey, 1),
            MaximumBrushSizePixels = ReadPositiveInt(values, BrushSizeMaximumValueKey, 50),
            DefaultBrushSizePixels = ReadPositiveInt(values, BrushSizeValueKey, 10)
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
        values[BrushSizeMinimumValueKey] = layout.MinimumBrushSizePixels.ToString(CultureInfo.InvariantCulture);
        values[BrushSizeMaximumValueKey] = layout.MaximumBrushSizePixels.ToString(CultureInfo.InvariantCulture);
        values[PencilKey] = FormatPoint(layout.PencilTool);
        values[EraserKey] = FormatPoint(layout.EraserTool);
        values[FillKey] = FormatPoint(layout.FillTool);
        values[BrushSizeMinimumKey] = FormatPoint(layout.BrushSizeMinimum);
        values[BrushSizeMaximumKey] = FormatPoint(layout.BrushSizeMaximum);
        values[HexInputKey] = FormatPoint(layout.HexInput);

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
                DrawingMode.Hybrid,
                DrawingMode.CleanStroke,
                DrawingMode.ArtistStroke
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
        string key,
        string? fallbackKey = null)
    {
        if (!values.TryGetValue(key, out var value) &&
            (fallbackKey is null || !values.TryGetValue(fallbackKey, out value)))
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
