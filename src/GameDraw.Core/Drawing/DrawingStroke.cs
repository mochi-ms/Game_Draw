using GameDraw.Core.Geometry;

namespace GameDraw.Core.Drawing;

public enum DrawingToolAction
{
    Pencil = 0,
    Fill = 1
}

/// <summary>
/// A single continuous path in normalized canvas coordinates.
/// </summary>
public sealed class DrawingStroke
{
    private readonly NormalizedPoint[] _points;

    public DrawingStroke(
        IEnumerable<NormalizedPoint> points,
        bool isClosed = false,
        DrawingToolAction toolAction = DrawingToolAction.Pencil)
    {
        ArgumentNullException.ThrowIfNull(points);

        _points = points.ToArray();
        if (_points.Length == 0)
        {
            throw new ArgumentException("A stroke must contain at least one point.", nameof(points));
        }

        if (_points.Any(point => !point.IsWithinUnitSquare))
        {
            throw new ArgumentException("All stroke points must be finite and within the unit square.", nameof(points));
        }

        if (!Enum.IsDefined(toolAction))
        {
            throw new ArgumentOutOfRangeException(nameof(toolAction));
        }

        IsClosed = isClosed;
        ToolAction = toolAction;
    }

    public IReadOnlyList<NormalizedPoint> Points => _points;

    public bool IsClosed { get; }

    public DrawingToolAction ToolAction { get; }

    public double TravelDistance
    {
        get
        {
            if (_points.Length < 2)
            {
                return 0;
            }

            var distance = 0d;
            for (var index = 1; index < _points.Length; index++)
            {
                distance += Distance(_points[index - 1], _points[index]);
            }

            if (IsClosed)
            {
                distance += Distance(_points[^1], _points[0]);
            }

            return distance;
        }
    }

    private static double Distance(NormalizedPoint first, NormalizedPoint second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}
