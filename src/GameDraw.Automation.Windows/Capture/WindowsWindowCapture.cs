using System.Runtime.InteropServices;
using GameDraw.Core.Geometry;
using GameDraw.Core.Imaging;
using GameDraw.Core.Targeting;

namespace GameDraw.Automation.Windows.Capture;

/// <summary>
/// Captures the visible, desktop-composited client area through BitBlt. Using
/// the desktop DC is important for Direct3D games: GetDC(hwnd) can return a
/// stale backing surface even though newly drawn pixels are visible onscreen.
/// The implementation is guarded for non-Windows hosts so recognition tests
/// can use synthetic frames without loading user32/gdi32.
/// </summary>
public sealed class WindowsWindowCapture : ITargetWindowCapture
{
    public Task<CapturedWindowFrame?> CaptureAsync(
        TargetWindowSnapshot target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || target.Handle == 0)
        {
            return Task.FromResult<CapturedWindowFrame?>(null);
        }

        return Task.Run(() => CaptureCore(target, cancellationToken), cancellationToken);
    }

    private static CapturedWindowFrame? CaptureCore(
        TargetWindowSnapshot target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hwnd = (nint)target.Handle;
        if (!NativeMethods.GetClientRect(hwnd, out var clientRect))
        {
            return null;
        }

        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var clientOrigin = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(hwnd, ref clientOrigin))
        {
            return null;
        }

        var desktopWindow = nint.Zero;
        var sourceDc = NativeMethods.GetDC(desktopWindow);
        if (sourceDc == nint.Zero)
        {
            return null;
        }

        var memoryDc = nint.Zero;
        var bitmap = nint.Zero;
        var previous = nint.Zero;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(sourceDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(sourceDc, width, height);
            if (memoryDc == nint.Zero || bitmap == nint.Zero)
            {
                return null;
            }

            previous = NativeMethods.SelectObject(memoryDc, bitmap);
            if (previous == nint.Zero ||
                !NativeMethods.BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    sourceDc,
                    clientOrigin.X,
                    clientOrigin.Y,
                    NativeMethods.RasterOperation.SourceCopy | NativeMethods.RasterOperation.CaptureBlt))
            {
                return null;
            }

            var bytes = new byte[checked(width * height * 4)];
            var info = new NativeMethods.BITMAPINFO
            {
                Header = new NativeMethods.BITMAPINFOHEADER
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0
                }
            };
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var copied = NativeMethods.GetDIBits(
                    memoryDc,
                    bitmap,
                    0,
                    (uint)height,
                    pinned.AddrOfPinnedObject(),
                    ref info,
                    0);
                if (copied == 0)
                {
                    return null;
                }
            }
            finally
            {
                pinned.Free();
            }

            return new CapturedWindowFrame(
                target with { ClientWidth = width, ClientHeight = height },
                new PixelSize(width, height),
                DateTimeOffset.UtcNow,
                bytes);
        }
        finally
        {
            if (previous != nint.Zero && memoryDc != nint.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previous);
            }

            if (bitmap != nint.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            _ = NativeMethods.ReleaseDC(desktopWindow, sourceDc);
        }
    }

    private static class NativeMethods
    {
        [Flags]
        internal enum RasterOperation : uint
        {
            SourceCopy = 0x00CC0020,
            CaptureBlt = 0x40000000
        }

        [DllImport("user32.dll")]
        internal static extern nint GetDC(nint windowHandle);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(nint windowHandle, nint deviceContext);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(nint windowHandle, ref POINT point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(nint windowHandle, out RECT rectangle);

        [DllImport("gdi32.dll")]
        internal static extern nint CreateCompatibleDC(nint deviceContext);

        [DllImport("gdi32.dll")]
        internal static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

        [DllImport("gdi32.dll")]
        internal static extern nint SelectObject(nint deviceContext, nint objectHandle);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(nint objectHandle);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(nint deviceContext);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BitBlt(
            nint destination,
            int x,
            int y,
            int width,
            int height,
            nint source,
            int sourceX,
            int sourceY,
            RasterOperation operation);

        [DllImport("gdi32.dll")]
        internal static extern int GetDIBits(
            nint deviceContext,
            nint bitmap,
            uint startScan,
            uint scanLines,
            nint bits,
            ref BITMAPINFO bitmapInfo,
            uint usage);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BITMAPINFO
        {
            public BITMAPINFOHEADER Header;
            public uint Color;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BITMAPINFOHEADER
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint ImageSize;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }
    }
}

public static class CapturedWindowFrameExtensions
{
    public static ImageFrame ToImageFrame(this CapturedWindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var expected = checked(frame.Size.Width * frame.Size.Height * 4);
        if (frame.BgraPixels.Length < expected)
        {
            throw new ArgumentException("Captured BGRA buffer is smaller than the frame dimensions.", nameof(frame));
        }

        var bytes = frame.BgraPixels.Span;
        var pixels = new RgbaPixel[checked(frame.Size.Width * frame.Size.Height)];
        for (var index = 0; index < pixels.Length; index++)
        {
            var offset = index * 4;
            var alpha = bytes[offset + 3] == 0 ? byte.MaxValue : bytes[offset + 3];
            pixels[index] = new RgbaPixel(
                new GameDraw.Core.Colors.RgbColor(bytes[offset + 2], bytes[offset + 1], bytes[offset]),
                alpha);
        }

        return new ImageFrame(frame.Size.Width, frame.Size.Height, pixels);
    }
}
