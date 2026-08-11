using GameDraw.Automation.Windows.Coordinates;
using GameDraw.Core.Geometry;
using GameDraw.Core.Targeting;

namespace GameDraw.Automation.Windows.Targeting;

public sealed class TargetWindowBinding
{
    private readonly IWindowGeometryProvider _geometryProvider;
    private readonly object _lock = new();
    private readonly ClientCoordinateMapper _mapper;
    private TargetWindowGeometry _geometry;

    public TargetWindowBinding(
        TargetWindowGeometry geometry,
        IWindowGeometryProvider geometryProvider,
        NormalizedRect canvasBounds = default)
    {
        ArgumentNullException.ThrowIfNull(geometryProvider);
        if (!geometry.IsValid)
        {
            throw new ArgumentException("대상 창 지오메트리가 유효하지 않습니다.", nameof(geometry));
        }

        _geometry = geometry;
        _geometryProvider = geometryProvider;
        _mapper = new ClientCoordinateMapper(geometry, canvasBounds);
    }

    public long Handle
    {
        get
        {
            lock (_lock)
            {
                return _geometry.Snapshot.Handle;
            }
        }
    }

    public TargetWindowSnapshot Snapshot
    {
        get
        {
            lock (_lock)
            {
                return _geometry.Snapshot;
            }
        }
    }

    public TargetWindowGeometry Geometry
    {
        get
        {
            lock (_lock)
            {
                return _geometry;
            }
        }
    }

    public ScreenPoint Map(GameDraw.Core.Geometry.NormalizedPoint point)
    {
        lock (_lock)
        {
            return _mapper.Map(point);
        }
    }

    public ScreenPoint MapClient(GameDraw.Core.Geometry.NormalizedPoint point)
    {
        if (!point.IsWithinUnitSquare)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "클라이언트 좌표는 0~1 범위여야 합니다.");
        }

        lock (_lock)
        {
            var bounds = _geometry.ClientBounds;
            var x = bounds.X + (int)Math.Round(point.X * Math.Max(0, bounds.Width - 1), MidpointRounding.AwayFromZero);
            var y = bounds.Y + (int)Math.Round(point.Y * Math.Max(0, bounds.Height - 1), MidpointRounding.AwayFromZero);
            return new ScreenPoint(x, y);
        }
    }

    public async ValueTask<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var refreshed = await _geometryProvider.GetGeometryAsync(Handle, cancellationToken).ConfigureAwait(false);
        if (refreshed is null || !refreshed.IsValid)
        {
            return false;
        }

        lock (_lock)
        {
            if (refreshed.Snapshot.Handle != _geometry.Snapshot.Handle)
            {
                return false;
            }

            _geometry = refreshed;
            _mapper.UpdateGeometry(refreshed);
            return true;
        }
    }
}
