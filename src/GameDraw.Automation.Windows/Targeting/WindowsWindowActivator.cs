using System.Runtime.InteropServices;

namespace GameDraw.Automation.Windows.Targeting;

/// <summary>
/// Restores and activates a user-selected target window. This is used only at
/// workflow boundaries; the executor still verifies foreground ownership
/// before every input sequence.
/// </summary>
public static class WindowsWindowActivator
{
    public static bool TryRestoreAndActivate(long handle)
    {
        if (!OperatingSystem.IsWindows() || handle == 0 || !NativeMethods.IsWindow((nint)handle))
        {
            return false;
        }

        var window = (nint)handle;
        if (NativeMethods.IsIconic(window))
        {
            _ = NativeMethods.ShowWindowAsync(window, NativeMethods.Restore);
        }

        return NativeMethods.SetForegroundWindow(window);
    }

    private static class NativeMethods
    {
        internal const int Restore = 9;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindowAsync(nint windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(nint windowHandle);
    }
}
