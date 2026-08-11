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

public sealed class WindowsInputController : IWindowsInputController, IDisposable
{
    private readonly InputRateLimiter _rateLimiter;
    private readonly object _stateLock = new();
    private readonly HashSet<InputMouseButton> _pressedButtons = new();
    private readonly HashSet<InputKey> _pressedKeys = new();
    private bool _disposed;

    public WindowsInputController(WindowsInputOptions? options = null)
    {
        options ??= new WindowsInputOptions();
        options.Validate();
        _rateLimiter = new InputRateLimiter(options);
    }

    public async ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        SendMouse(point, MouseFlags.Move | MouseFlags.Absolute | MouseFlags.VirtualDesktop);
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
        SendKeyboard(VirtualKey(key), KeyFlags.None);
        lock (_stateLock)
        {
            _pressedKeys.Add(key);
        }
    }

    public async ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        SendKeyboard(VirtualKey(key), KeyFlags.KeyUp);
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

    public ValueTask ReleaseAllButtonsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        InputMouseButton[] buttons;
        lock (_stateLock)
        {
            buttons = _pressedButtons.ToArray();
            _pressedButtons.Clear();
        }

        // Safety releases intentionally bypass the normal rate limiter.
        foreach (var button in buttons)
        {
            SendMouse(default, ButtonUpFlag(button));
        }

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
            SendKeyboard(VirtualKey(key), KeyFlags.KeyUp);
        }

        return ValueTask.CompletedTask;
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
        var input = new NativeMethods.INPUT
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
        Send(input);
    }

    private static void SendKeyboard(ushort virtualKey, KeyFlags flags, char? unicodeCharacter = null)
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
                    ScanCode = unicodeCharacter is { } character ? character : (ushort)0,
                    Flags = (uint)flags
                }
            }
        };
        Send(input);
    }

    private static void Send(NativeMethods.INPUT input)
    {
        var inputs = new[] { input };
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
            InputKey.F6 => 0x75,
            InputKey.F7 => 0x76,
            InputKey.F8 => 0x77,
            InputKey.A => 0x41,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };

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
        Absolute = 0x8000,
        VirtualDesktop = 0x4000
    }

    [Flags]
    private enum KeyFlags : uint
    {
        None = 0,
        KeyUp = 0x0002,
        Unicode = 0x0004
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
        internal static extern int GetSystemMetrics(Metric index);

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
