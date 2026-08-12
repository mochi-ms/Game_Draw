using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GameDraw.Core.Execution;
using GameDraw.Core.Geometry;

namespace GameDraw.Automation.Windows.Input;

public sealed record WindowsInputOptions
{
    public double MaxEventsPerSecond { get; init; } = 600d;

    public int MinimumIntervalMilliseconds { get; init; }

    /// <summary>
    /// A visible GameDraw-owned window used to force a verified focus boundary
    /// away from Roblox before disconnected cursor travel.
    /// </summary>
    public long FocusSinkWindowHandle { get; init; }

    public void Validate()
    {
        if (!double.IsFinite(MaxEventsPerSecond) || MaxEventsPerSecond <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEventsPerSecond));
        }

        if (MinimumIntervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumIntervalMilliseconds));
        }
    }
}

public sealed class WindowsInputController : IWindowsInputController, IClipboardInputController, IPointerCaptureResetController, IDisposable
{
    private readonly InputRateLimiter _rateLimiter;
    private readonly nint _focusSinkWindow;
    private readonly object _stateLock = new();
    private readonly HashSet<InputMouseButton> _pressedButtons = new();
    private readonly HashSet<InputKey> _pressedKeys = new();
    private bool _disposed;

    public WindowsInputController(WindowsInputOptions? options = null)
    {
        options ??= new WindowsInputOptions();
        options.Validate();
        _rateLimiter = new InputRateLimiter(options);
        _focusSinkWindow = (nint)options.FocusSinkWindowHandle;
    }

    public async ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        SendMouse(point, MouseFlags.Move | MouseFlags.MoveNoCoalesce | MouseFlags.Absolute | MouseFlags.VirtualDesktop);
    }

    public async ValueTask MoveWithButtonsReleasedAsync(
        ScreenPoint point,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // Keep release and motion in distinct native deliveries. Roblox can
        // sample one SendInput array as a single frame and observe the move
        // before its Lua-side pen latch processes the up event.
        Send(
            CreateMouse(default, MouseFlags.LeftUp),
            CreateMouse(default, MouseFlags.RightUp),
            CreateMouse(default, MouseFlags.MiddleUp));
        lock (_stateLock)
        {
            _pressedButtons.Clear();
        }

        await Task.Delay(2, cancellationToken).ConfigureAwait(false);
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        SendMouse(point, MouseFlags.Move | MouseFlags.MoveNoCoalesce | MouseFlags.Absolute | MouseFlags.VirtualDesktop);
        await Task.Delay(2, cancellationToken).ConfigureAwait(false);
        Send(
            CreateMouse(default, MouseFlags.LeftUp),
            CreateMouse(default, MouseFlags.RightUp),
            CreateMouse(default, MouseFlags.MiddleUp));
    }

    public async ValueTask MouseDownAsync(
        InputMouseButton button = InputMouseButton.Left,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        SendMouse(default, ButtonDownFlag(button));
        lock (_stateLock)
        {
            _pressedButtons.Add(button);
        }
    }

    public async ValueTask MouseUpAsync(
        InputMouseButton button = InputMouseButton.Left,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        SendMouse(default, ButtonUpFlag(button));
        lock (_stateLock)
        {
            _pressedButtons.Remove(button);
        }
    }

    public async ValueTask ClickAsync(
        ScreenPoint point,
        InputMouseButton button = InputMouseButton.Left,
        CancellationToken cancellationToken = default)
    {
        await MoveToAsync(point, cancellationToken).ConfigureAwait(false);
        await MouseDownAsync(button, cancellationToken).ConfigureAwait(false);
        try
        {
            await MouseUpAsync(button, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await ReleaseAllButtonsAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask KeyDownAsync(InputKey key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        SendPhysicalKey(key, released: false);
        lock (_stateLock)
        {
            _pressedKeys.Add(key);
        }
    }

    public async ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        SendPhysicalKey(key, released: true);
        lock (_stateLock)
        {
            _pressedKeys.Remove(key);
        }
    }

    public async ValueTask TypeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(text);
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            SendKeyboard(0, KeyFlags.Unicode, character);
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            SendKeyboard(0, KeyFlags.Unicode | KeyFlags.KeyUp, character);
        }
    }

    public async ValueTask SetClipboardTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(text);
        EnsureWindows();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TrySetClipboardText(text))
            {
                return;
            }

            await Task.Delay(35, cancellationToken).ConfigureAwait(false);
        }

        throw new Win32Exception(Marshal.GetLastWin32Error(), "HEX 값을 Windows 클립보드에 저장하지 못했습니다.");
    }

    public async ValueTask<string?> GetClipboardTextAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureWindows();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetClipboardText(out var text))
            {
                return text;
            }

            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }

        throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 클립보드에서 HEX 값을 확인하지 못했습니다.");
    }

    public ValueTask ReleaseAllButtonsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            _pressedButtons.Clear();
        }

        // Safety releases intentionally bypass both the normal rate limiter
        // and tracked state. Roblox may have missed a previous up event after
        // our local state was already cleared, so always emit all releases.
        Send(
            CreateMouse(default, MouseFlags.LeftUp),
            CreateMouse(default, MouseFlags.RightUp),
            CreateMouse(default, MouseFlags.MiddleUp));

        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAllKeysAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        InputKey[] keys;
        lock (_stateLock)
        {
            keys = _pressedKeys.ToArray();
            _pressedKeys.Clear();
        }

        foreach (var key in keys)
        {
            SendPhysicalKey(key, released: true);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask ResetPointerCaptureAsync(
        long targetWindowHandle,
        CancellationToken cancellationToken = default)
        => await ResetOrRepositionPointerAsync(
            targetWindowHandle,
            null,
            cancellationToken).ConfigureAwait(false);

    public async ValueTask RepositionWithCaptureResetAsync(
        long targetWindowHandle,
        ScreenPoint destination,
        CancellationToken cancellationToken = default)
        => await ResetOrRepositionPointerAsync(
            targetWindowHandle,
            destination,
            cancellationToken).ConfigureAwait(false);

    private async ValueTask ResetOrRepositionPointerAsync(
        long targetWindowHandle,
        ScreenPoint? destination,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var target = (nint)targetWindowHandle;
        if (target == nint.Zero || !NativeMethods.IsWindow(target))
        {
            throw new InvalidOperationException("그리기 대상 창이 사라져 드래그 상태를 안전하게 초기화할 수 없습니다.");
        }

        await ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
        await ReleaseAllKeysAsync(cancellationToken).ConfigureAwait(false);

        // Podiums occasionally retains its own pointer capture even though
        // SendInput already delivered button-up. Cancel target-side tracking
        // and post one final release at the current client coordinate.
        _ = NativeMethods.PostMessage(target, NativeMethods.CancelMode, nint.Zero, nint.Zero);
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            var client = cursor;
            if (NativeMethods.ScreenToClient(target, ref client))
            {
                var packed = (nint)((client.X & 0xFFFF) | ((client.Y & 0xFFFF) << 16));
                _ = NativeMethods.PostMessage(target, NativeMethods.LeftButtonUp, nint.Zero, packed);
            }
        }

        // A genuine focus boundary makes Roblox dispatch InputEnded and drop
        // any Lua-side drag latch. Return focus to the selected target before
        // the cursor is allowed to travel toward the HEX control.
        var focusSink = _focusSinkWindow;
        if (focusSink == nint.Zero || focusSink == target || !NativeMethods.IsWindow(focusSink))
        {
            throw new InvalidOperationException("안전 이동용 GameDraw 실행 패널을 찾지 못해 잘못된 선을 막기 위해 실행을 중단했습니다.");
        }

        _ = NativeMethods.SetForegroundWindow(focusSink);
        var sinkDeadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 0.45d);
        while (NativeMethods.GetForegroundWindow() != focusSink && Stopwatch.GetTimestamp() < sinkDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            _ = NativeMethods.SetForegroundWindow(focusSink);
        }

        if (NativeMethods.GetForegroundWindow() != focusSink)
        {
            throw new InvalidOperationException("Roblox 드래그 해제를 확인하지 못해 다음 위치로 이동하지 않고 중단했습니다.");
        }

        // Critical ordering: move only while GameDraw owns the foreground.
        // Restoring Roblox before this move lets its stale Lua drag latch see
        // the destination and creates the long diagonal connector.
        await Task.Delay(28, cancellationToken).ConfigureAwait(false);
        if (destination is { } safeDestination)
        {
            await MoveWithButtonsReleasedAsync(safeDestination, cancellationToken).ConfigureAwait(false);
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            await ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
        }

        _ = NativeMethods.SetForegroundWindow(target);
        var foregroundDeadline = Stopwatch.GetTimestamp() +
            (long)(Stopwatch.Frequency * 0.45d);
        while (NativeMethods.GetForegroundWindow() != target &&
               Stopwatch.GetTimestamp() < foregroundDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            _ = NativeMethods.SetForegroundWindow(target);
        }

        if (NativeMethods.GetForegroundWindow() != target)
        {
            throw new InvalidOperationException("Roblox 포커스를 다시 확보하지 못해 잘못된 선을 막기 위해 실행을 중단했습니다.");
        }

        await Task.Delay(28, cancellationToken).ConfigureAwait(false);
        await ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
        _ = NativeMethods.PostMessage(target, NativeMethods.CancelMode, nint.Zero, nint.Zero);
        await Task.Delay(18, cancellationToken).ConfigureAwait(false);
        await ReleaseAllButtonsAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            ReleaseAllButtonsAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            ReleaseAllKeysAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            _disposed = true;
            _rateLimiter.Dispose();
        }
    }

    private static void SendMouse(ScreenPoint point, MouseFlags flags)
    {
        EnsureWindows();
        Send(CreateMouse(point, flags));
    }

    private static NativeMethods.INPUT CreateMouse(ScreenPoint point, MouseFlags flags)
        => new()
        {
            Type = NativeMethods.InputType.Mouse,
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MOUSEINPUT
                {
                    X = flags.HasFlag(MouseFlags.Move) ? ToAbsoluteX(point.X) : 0,
                    Y = flags.HasFlag(MouseFlags.Move) ? ToAbsoluteY(point.Y) : 0,
                    Flags = (uint)flags
                }
            }
        };

    private static void SendPhysicalKey(InputKey key, bool released)
    {
        var virtualKey = VirtualKey(key);
        var scanCode = (ushort)NativeMethods.MapVirtualKey(virtualKey, NativeMethods.MapVirtualKeyToScanCode);
        var flags = KeyFlags.ScanCode;
        if (released)
        {
            flags |= KeyFlags.KeyUp;
        }

        if (key == InputKey.Delete)
        {
            flags |= KeyFlags.ExtendedKey;
        }

        SendKeyboard(0, flags, scanCode: scanCode);
    }

    private static void SendKeyboard(
        ushort virtualKey,
        KeyFlags flags,
        char? unicodeCharacter = null,
        ushort scanCode = 0)
    {
        EnsureWindows();
        var input = new NativeMethods.INPUT
        {
            Type = NativeMethods.InputType.Keyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    ScanCode = unicodeCharacter is { } character ? character : scanCode,
                    Flags = (uint)flags
                }
            }
        };
        Send(input);
    }

    private static void Send(params NativeMethods.INPUT[] inputs)
    {
        EnsureWindows();
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput 호출에 실패했습니다.");
        }
    }

    private static ushort VirtualKey(InputKey key)
        => key switch
        {
            InputKey.Control => 0x11,
            InputKey.Enter => 0x0D,
            InputKey.Escape => 0x1B,
            InputKey.Delete => 0x2E,
            InputKey.Backspace => 0x08,
            InputKey.F5 => 0x74,
            InputKey.F6 => 0x75,
            InputKey.F7 => 0x76,
            InputKey.F8 => 0x77,
            InputKey.A => 0x41,
            InputKey.V => 0x56,
            InputKey.C => 0x43,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };

    private static bool TrySetClipboardText(string text)
    {
        if (!NativeMethods.OpenClipboard(nint.Zero))
        {
            return false;
        }

        nint memory = nint.Zero;
        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                return false;
            }

            var bytes = System.Text.Encoding.Unicode.GetBytes(text + '\0');
            memory = NativeMethods.GlobalAlloc(NativeMethods.GlobalMemoryMoveable, (nuint)bytes.Length);
            if (memory == nint.Zero)
            {
                return false;
            }

            var destination = NativeMethods.GlobalLock(memory);
            if (destination == nint.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, destination, bytes.Length);
            }
            finally
            {
                _ = NativeMethods.GlobalUnlock(memory);
            }

            if (NativeMethods.SetClipboardData(NativeMethods.UnicodeTextClipboardFormat, memory) == nint.Zero)
            {
                return false;
            }

            // The system owns the allocation after SetClipboardData succeeds.
            memory = nint.Zero;
            return true;
        }
        finally
        {
            if (memory != nint.Zero)
            {
                _ = NativeMethods.GlobalFree(memory);
            }

            _ = NativeMethods.CloseClipboard();
        }
    }

    private static bool TryGetClipboardText(out string? text)
    {
        text = null;
        if (!NativeMethods.OpenClipboard(nint.Zero))
        {
            return false;
        }

        try
        {
            var memory = NativeMethods.GetClipboardData(NativeMethods.UnicodeTextClipboardFormat);
            if (memory == nint.Zero)
            {
                return false;
            }

            var source = NativeMethods.GlobalLock(memory);
            if (source == nint.Zero)
            {
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(source);
                return text is not null;
            }
            finally
            {
                _ = NativeMethods.GlobalUnlock(memory);
            }
        }
        finally
        {
            _ = NativeMethods.CloseClipboard();
        }
    }

    private static MouseFlags ButtonDownFlag(InputMouseButton button)
        => button switch
        {
            InputMouseButton.Left => MouseFlags.LeftDown,
            InputMouseButton.Right => MouseFlags.RightDown,
            InputMouseButton.Middle => MouseFlags.MiddleDown,
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };

    private static MouseFlags ButtonUpFlag(InputMouseButton button)
        => button switch
        {
            InputMouseButton.Left => MouseFlags.LeftUp,
            InputMouseButton.Right => MouseFlags.RightUp,
            InputMouseButton.Middle => MouseFlags.MiddleUp,
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };

    private static int ToAbsoluteX(int x)
        => ToAbsolute(x, NativeMethods.GetSystemMetrics(NativeMethods.Metric.VirtualScreenX), NativeMethods.GetSystemMetrics(NativeMethods.Metric.VirtualScreenWidth));

    private static int ToAbsoluteY(int y)
        => ToAbsolute(y, NativeMethods.GetSystemMetrics(NativeMethods.Metric.VirtualScreenY), NativeMethods.GetSystemMetrics(NativeMethods.Metric.VirtualScreenHeight));

    private static int ToAbsolute(int value, int origin, int length)
    {
        if (length <= 1)
        {
            return 0;
        }

        var normalized = (value - origin) * 65535d / (length - 1d);
        return Math.Clamp((int)Math.Round(normalized, MidpointRounding.AwayFromZero), 0, 65535);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows SendInput은 Windows 환경에서만 지원됩니다.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [Flags]
    private enum MouseFlags : uint
    {
        Move = 0x0001,
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        MiddleDown = 0x0020,
        MiddleUp = 0x0040,
        MoveNoCoalesce = 0x2000,
        Absolute = 0x8000,
        VirtualDesktop = 0x4000
    }

    [Flags]
    private enum KeyFlags : uint
    {
        None = 0,
        ExtendedKey = 0x0001,
        KeyUp = 0x0002,
        Unicode = 0x0004,
        ScanCode = 0x0008
    }

    private sealed class InputRateLimiter : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly double _intervalSeconds;
        private long _nextTimestamp;

        public InputRateLimiter(WindowsInputOptions options)
        {
            var configuredInterval = options.MinimumIntervalMilliseconds / 1000d;
            _intervalSeconds = Math.Max(configuredInterval, 1d / options.MaxEventsPerSecond);
        }

        public async ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            long delayTicks;
            try
            {
                var now = Stopwatch.GetTimestamp();
                var intervalTicks = (long)Math.Ceiling(_intervalSeconds * Stopwatch.Frequency);
                var scheduled = Math.Max(now, _nextTimestamp);
                _nextTimestamp = scheduled + intervalTicks;
                delayTicks = scheduled - now;
            }
            finally
            {
                _gate.Release();
            }

            if (delayTicks > 0)
            {
                var delay = TimeSpan.FromSeconds(delayTicks / (double)Stopwatch.Frequency);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose() => _gate.Dispose();
    }

    private static class NativeMethods
    {
        internal const uint CancelMode = 0x001F;
        internal const uint LeftButtonUp = 0x0202;
        internal const uint UnicodeTextClipboardFormat = 13;
        internal const uint GlobalMemoryMoveable = 0x0002;
        internal const uint MapVirtualKeyToScanCode = 0;

        internal enum InputType : uint
        {
            Mouse = 0,
            Keyboard = 1
        }

        internal enum Metric
        {
            VirtualScreenX = 76,
            VirtualScreenY = 77,
            VirtualScreenWidth = 78,
            VirtualScreenHeight = 79
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint windowHandle);

        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(nint windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(nint windowHandle, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ScreenToClient(nint windowHandle, ref POINT point);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(Metric index);

        [DllImport("user32.dll")]
        internal static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenClipboard(nint windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint SetClipboardData(uint format, nint memory);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint GetClipboardData(uint format);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GlobalAlloc(uint flags, nuint bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GlobalLock(nint memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(nint memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GlobalFree(nint memory);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public InputType Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT Mouse;

            [FieldOffset(0)]
            public KEYBDINPUT Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public nint ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public nint ExtraInfo;
        }
    }
}
