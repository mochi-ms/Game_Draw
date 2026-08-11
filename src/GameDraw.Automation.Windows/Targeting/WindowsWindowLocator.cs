using System.Diagnostics;
using System.Runtime.InteropServices;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;
using GameDraw.Core.Targeting;

namespace GameDraw.Automation.Windows.Targeting;

public sealed class WindowsWindowLocator : IWindowLocator
{
    public Task<IReadOnlyList<TargetWindowSnapshot>> GetCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyList<TargetWindowSnapshot>>(Array.Empty<TargetWindowSnapshot>());
        }

        var candidates = new List<TargetWindowSnapshot>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!NativeMethods.IsWindowVisible(handle))
            {
                return true;
            }

            var snapshot = NativeMethods.TryReadSnapshot(handle);
            if (snapshot is not null)
            {
                candidates.Add(snapshot);
            }

            return true;
        }, nint.Zero);

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TargetWindowSnapshot>>(candidates);
    }
}

public sealed class WindowsWindowGeometryProvider : IWindowGeometryProvider
{
    public ValueTask<TargetWindowGeometry?> GetGeometryAsync(
        long handle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || handle == 0)
        {
            return ValueTask.FromResult<TargetWindowGeometry?>(null);
        }

        var nativeHandle = (nint)handle;
        var snapshot = NativeMethods.TryReadSnapshot(nativeHandle);
        if (snapshot is null || !NativeMethods.GetClientRect(nativeHandle, out var clientRect))
        {
            return ValueTask.FromResult<TargetWindowGeometry?>(null);
        }

        var topLeft = new NativeMethods.POINT { X = clientRect.Left, Y = clientRect.Top };
        if (!NativeMethods.ClientToScreen(nativeHandle, ref topLeft))
        {
            return ValueTask.FromResult<TargetWindowGeometry?>(null);
        }

        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        var dpi = NativeMethods.GetDpiForWindow(nativeHandle);
        var geometry = new TargetWindowGeometry(
            snapshot with
            {
                ClientWidth = width,
                ClientHeight = height,
                Dpi = dpi == 0 ? 96u : dpi
            },
            new ScreenRect(topLeft.X, topLeft.Y, width, height),
            dpi == 0 ? 96u : dpi);
        return ValueTask.FromResult<TargetWindowGeometry?>(geometry.IsValid ? geometry : null);
    }
}

public sealed class WindowsTargetVerifier : ITargetVerifier
{
    public ValueTask<TargetVerificationResult> VerifyAsync(
        TargetWindowSnapshot target,
        GameDraw.Core.Models.DrawingMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issues = new List<TargetVerificationIssue>();
        if (!OperatingSystem.IsWindows())
        {
            issues.Add(new TargetVerificationIssue(
                TargetVerificationSeverity.Error,
                "WINDOWS_REQUIRED",
                "Windows 자동화는 Windows 환경에서만 사용할 수 있습니다."));
            return ValueTask.FromResult(new TargetVerificationResult(false, issues));
        }

        if (target.Handle == 0 || !NativeMethods.IsWindow((nint)target.Handle))
        {
            issues.Add(new TargetVerificationIssue(
                TargetVerificationSeverity.Error,
                "TARGET_HANDLE_INVALID",
                "대상 창 핸들이 유효하지 않습니다."));
        }

        if (target.ClientWidth <= 0 || target.ClientHeight <= 0)
        {
            issues.Add(new TargetVerificationIssue(
                TargetVerificationSeverity.Error,
                "TARGET_CLIENT_EMPTY",
                "대상 창의 클라이언트 영역이 비어 있습니다."));
        }

        if (!target.IsForeground)
        {
            issues.Add(new TargetVerificationIssue(
                TargetVerificationSeverity.Warning,
                "TARGET_NOT_FOREGROUND",
                "대상 창이 현재 포그라운드가 아닙니다."));
        }

        if (mode == GameDraw.Core.Models.DrawingMode.Auto)
        {
            issues.Add(new TargetVerificationIssue(
                TargetVerificationSeverity.Error,
                "MODE_NOT_RESOLVED",
                "실행 전 자동 모드를 구체적인 DrawingPlan으로 확정해야 합니다."));
        }

        var safe = issues.All(issue => issue.Severity != TargetVerificationSeverity.Error);
        return ValueTask.FromResult(new TargetVerificationResult(safe, issues));
    }
}

internal static class NativeMethods
{
    internal delegate bool EnumWindowsCallback(nint handle, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(nint handle, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint handle, out RECT rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint handle, ref POINT point);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint handle);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    internal static TargetWindowSnapshot? TryReadSnapshot(nint handle)
    {
        if (handle == 0 || !IsWindow(handle) || !GetClientRect(handle, out var rectangle))
        {
            return null;
        }

        var titleLength = GetWindowTextLength(handle);
        var titleBuffer = new char[Math.Max(1, titleLength + 1)];
        var copied = GetWindowText(handle, titleBuffer, titleBuffer.Length);
        var title = new string(titleBuffer, 0, Math.Max(0, copied));
        var threadId = GetWindowThreadProcessId(handle, out var processId);
        if (threadId == 0)
        {
            processId = 0;
        }
        var processName = string.Empty;
        try
        {
            processName = processId == 0 ? string.Empty : Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception) when (processId != 0)
        {
            processName = string.Empty;
        }

        var dpi = GetDpiForWindow(handle);
        return new TargetWindowSnapshot(
            handle,
            processName,
            title,
            Math.Max(0, rectangle.Right - rectangle.Left),
            Math.Max(0, rectangle.Bottom - rectangle.Top),
            dpi == 0 ? 96u : dpi,
            GetForegroundWindow() == handle);
    }

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
}
