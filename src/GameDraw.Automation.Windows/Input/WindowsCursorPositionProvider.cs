using System.Runtime.InteropServices;
using GameDraw.Core.Geometry;

namespace GameDraw.Automation.Windows.Input;

public sealed class WindowsCursorPositionProvider : ICursorPositionProvider
{
    public bool TryGetScreenPosition(out ScreenPoint point)
    {
        point = default;
        if (!OperatingSystem.IsWindows() || !NativeMethods.GetCursorPos(out var nativePoint))
        {
            return false;
        }

        point = new ScreenPoint(nativePoint.X, nativePoint.Y);
        return true;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out Point point);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X;
            public int Y;
        }
    }
}
