using GameDraw.Core.Geometry;

namespace GameDraw.Core.Vision;

public sealed record CanvasRegistrationResult(
    bool IsCompatible,
    double IntersectionOverUnion,
    double CenterShiftPixels,
    double ScaleDelta);

public static class CanvasRegistration
{
    public static CanvasRegistrationResult Compare(
        NormalizedRect calibrated,
        NormalizedRect detected,
        PixelSize frameSize,
        double minimumOverlap = 0.45d,
        double maximumCenterShiftPixels = 64d,
        double maximumScaleDelta = 0.25d)
    {
        if (!calibrated.IsWithinUnitSquare || calibrated.Width <= 0d || calibrated.Height <= 0d)
        {
            throw new ArgumentException("보정 캔버스 영역이 올바르지 않습니다.", nameof(calibrated));
        }

        if (!detected.IsWithinUnitSquare || detected.Width <= 0d || detected.Height <= 0d)
        {
            throw new ArgumentException("감지 캔버스 영역이 올바르지 않습니다.", nameof(detected));
        }

        if (frameSize.Width <= 0 || frameSize.Height <= 0)
        {
            throw new ArgumentException("화면 크기가 올바르지 않습니다.", nameof(frameSize));
        }

        var left = Math.Max(calibrated.X, detected.X);
        var top = Math.Max(calibrated.Y, detected.Y);
        var right = Math.Min(calibrated.X + calibrated.Width, detected.X + detected.Width);
        var bottom = Math.Min(calibrated.Y + calibrated.Height, detected.Y + detected.Height);
        var intersection = Math.Max(0d, right - left) * Math.Max(0d, bottom - top);
        var union = (calibrated.Width * calibrated.Height) +
            (detected.Width * detected.Height) - intersection;
        var overlap = union <= double.Epsilon ? 0d : intersection / union;
        var x = (calibrated.Center.X - detected.Center.X) * frameSize.Width;
        var y = (calibrated.Center.Y - detected.Center.Y) * frameSize.Height;
        var shift = Math.Sqrt((x * x) + (y * y));
        var scale = Math.Max(
            RelativeDelta(calibrated.Width, detected.Width),
            RelativeDelta(calibrated.Height, detected.Height));
        return new CanvasRegistrationResult(
            overlap >= minimumOverlap && shift <= maximumCenterShiftPixels && scale <= maximumScaleDelta,
            overlap,
            shift,
            scale);
    }

    private static double RelativeDelta(double first, double second)
        => Math.Abs(first) < double.Epsilon
            ? Math.Abs(second)
            : Math.Abs(second - first) / Math.Abs(first);
}
