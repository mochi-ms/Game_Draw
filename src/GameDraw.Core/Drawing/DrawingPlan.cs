using GameDraw.Core.Colors;
using GameDraw.Core.Geometry;
using GameDraw.Core.Models;

namespace GameDraw.Core.Drawing;

/// <summary>
/// Ordered color groups and strokes produced by the image planner.
/// </summary>
public sealed class DrawingColorGroup
{
    private readonly DrawingStroke[] _strokes;

    public DrawingColorGroup(RgbColor color, IEnumerable<DrawingStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        _strokes = strokes.ToArray();
        if (_strokes.Length == 0)
        {
            throw new ArgumentException("A color group must contain at least one stroke.", nameof(strokes));
        }

        Color = color;
    }

    public RgbColor Color { get; }

    public IReadOnlyList<DrawingStroke> Strokes => _strokes;
}

public sealed class DrawingPlan
{
    private readonly DrawingColorGroup[] _colorGroups;

    public DrawingPlan(
        DrawingMode mode,
        PixelSize logicalSize,
        IEnumerable<DrawingColorGroup> colorGroups)
    {
        ArgumentNullException.ThrowIfNull(colorGroups);

        _colorGroups = colorGroups.ToArray();
        if (_colorGroups.Any(group => group is null))
        {
            throw new ArgumentException("Color groups cannot contain null values.", nameof(colorGroups));
        }

        Mode = mode;
        LogicalSize = logicalSize;
        Statistics = DrawingStatistics.From(_colorGroups);
    }

    public DrawingMode Mode { get; }

    public PixelSize LogicalSize { get; }

    public IReadOnlyList<DrawingColorGroup> ColorGroups => _colorGroups;

    public DrawingStatistics Statistics { get; }

    public IEnumerable<(RgbColor Color, DrawingStroke Stroke)> EnumerateStrokes()
    {
        foreach (var group in _colorGroups)
        {
            foreach (var stroke in group.Strokes)
            {
                yield return (group.Color, stroke);
            }
        }
    }

    public static DrawingPlan Empty(DrawingMode mode, PixelSize logicalSize)
        => new(mode, logicalSize, Array.Empty<DrawingColorGroup>());
}
