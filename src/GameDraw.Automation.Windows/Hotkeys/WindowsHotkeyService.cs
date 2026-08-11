using System.ComponentModel;
using System.Runtime.InteropServices;
using GameDraw.Core.Execution;

namespace GameDraw.Automation.Windows.Hotkeys;

/// <summary>
/// Registers system-wide keys on an existing application HWND. The window
/// subclass keeps delivery on the owning UI thread and is removed on dispose.
/// </summary>
public sealed class WindowsHotkeyService : IWindowsHotkeyService, IDisposable
{
    private const uint WindowMessageHotkey = 0x0312;
    private const uint ModifierNoRepeat = 0x4000;
    private readonly nint _windowHandle;
    private readonly NativeMethods.SubclassProcedure _subclassProcedure;
    private readonly nuint _subclassId;
    private readonly Dictionary<int, InputKey> _keysById = new();
    private bool _disposed;
    private int _nextId = 0x4700;

    public WindowsHotkeyService(nint windowHandle)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("전역 단축키는 Windows에서만 지원됩니다.");
        }

        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("전역 단축키를 연결할 창 핸들이 필요합니다.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _subclassId = unchecked((nuint)GetHashCode());
        _subclassProcedure = WindowSubclass;
        if (!NativeMethods.SetWindowSubclass(_windowHandle, _subclassProcedure, _subclassId, nint.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "전역 단축키 메시지 연결에 실패했습니다.");
        }
    }

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public void Register(InputKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_keysById.ContainsValue(key))
        {
            return;
        }

        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_windowHandle, id, ModifierNoRepeat, VirtualKey(key)))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{key} 전역 단축키 등록에 실패했습니다.");
        }

        _keysById.Add(id, key);
    }

    public void UnregisterAll()
    {
        foreach (var id in _keysById.Keys.ToArray())
        {
            _ = NativeMethods.UnregisterHotKey(_windowHandle, id);
        }

        _keysById.Clear();
    }

    public void Unregister(InputKey key)
    {
        var pair = _keysById.FirstOrDefault(item => item.Value == key);
        if (pair.Equals(default(KeyValuePair<int, InputKey>)) || !_keysById.ContainsKey(pair.Key))
        {
            return;
        }

        _ = NativeMethods.UnregisterHotKey(_windowHandle, pair.Key);
        _keysById.Remove(pair.Key);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAll();
        _ = NativeMethods.RemoveWindowSubclass(_windowHandle, _subclassProcedure, _subclassId);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private nint WindowSubclass(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nint referenceData)
    {
        if (message == WindowMessageHotkey && _keysById.TryGetValue(unchecked((int)wParam), out var key))
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(key));
            return nint.Zero;
        }

        return NativeMethods.DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private static uint VirtualKey(InputKey key)
        => key switch
        {
            InputKey.Escape => 0x1B,
            InputKey.F6 => 0x75,
            InputKey.F7 => 0x76,
            InputKey.F8 => 0x77,
            _ => throw new ArgumentException($"{key} 키는 전역 단축키로 지원하지 않습니다.", nameof(key))
        };

    private static class NativeMethods
    {
        internal delegate nint SubclassProcedure(
            nint windowHandle,
            uint message,
            nuint wParam,
            nint lParam,
            nuint subclassId,
            nint referenceData);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(nint windowHandle, int id);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowSubclass(
            nint windowHandle,
            SubclassProcedure procedure,
            nuint subclassId,
            nint referenceData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RemoveWindowSubclass(
            nint windowHandle,
            SubclassProcedure procedure,
            nuint subclassId);

        [DllImport("comctl32.dll")]
        internal static extern nint DefSubclassProc(
            nint windowHandle,
            uint message,
            nuint wParam,
            nint lParam);
    }
}
