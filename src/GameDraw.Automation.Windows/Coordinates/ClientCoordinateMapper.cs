using GameDraw.Core.Geometry;

namespace GameDraw.Automation.Windows.Coordinates;

/// <summary>
/// Maps the planner's normalized canvas coordinates to the current physical
/// client rectangle. The geometry can be refreshed when a window moves, is
/// resized, or changes monitor/DPI.
/// </summary>
public sealed class ClientCoordinateMapper
{
    private readonly NormalizedRect _canvasBounds;
    private TargetWindowGeometry _geometry;

    public ClientCoordinateMapper(
        TargetWindowGeometry geometry,
        NormalizedRect canvasBounds = default)
    {
        if (!geometry.IsValid)
        {
            throw new ArgumentException("대상 창 클라이언트 영역이 유효하지 않습니다.", nameof(geometry));
        }

        if (canvasBounds == default)
        {
            canvasBounds = new NormalizedRect(0d, 0d, 1d, 1d);
        }

        if (!canvasBounds.IsWithinUnitSquare || canvasBounds.Width <= 0d || canvasBounds.Height <= 0d)
        {
            throw new ArgumentException("캔버스 영역은 정규화된 유효한 사각형이어야 합니다.", nameof(canvasBounds));
        }

        _geometry = geometry;
        _canvasBounds = canvasBounds;
    }

    public TargetWindowGeometry Geometry => _geometry;

    public NormalizedRect CanvasBounds => _canvasBounds;

    public double DpiScale => (_geometry.Dpi == 0 ? 96u : _geometry.Dpi) / 96d;

    public void UpdateGeometry(TargetWindowGeometry geometry)
    {
        if (!geometry.IsValid)
        {
            throw new ArgumentException("대상 창 클라이언트 영역이 유효하지 않습니다.", nameof(geometry));
        }

        if (geometry.Snapshot.Handle != _geometry.Snapshot.Handle)
        {
            throw new InvalidOperationException("바인딩된 창 핸들이 변경되었습니다.");
        }

        _geometry = geometry;
    }

    public ScreenPoint Map(NormalizedPoint point)
    {
        if (!point.IsWithinUnitSquare)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "정규화된 좌표는 0~1 범위여야 합니다.");
        }

        var canvasX = _canvasBounds.X + (point.X * _canvasBounds.Width);
        var canvasY = _canvasBounds.Y + (point.Y * _canvasBounds.Height);
        var x = _geometry.ClientBounds.X + ToPixel(canvasX, _geometry.ClientBounds.Width);
        var y = _geometry.ClientBounds.Y + ToPixel(canvasY, _geometry.ClientBounds.Height);
        return new ScreenPoint(x, y);
    }

    private static int ToPixel(double normalized, int length)
        => Math.Clamp(
            (int)Math.Round(normalized * Math.Max(0, length - 1), MidpointRounding.AwayFromZero),
            0,
            Math.Max(0, length - 1));
}
