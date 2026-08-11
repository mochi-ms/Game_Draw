using System.Runtime.InteropServices;
using GameDraw.Core.Drawing;
using GameDraw.Core.Geometry;

namespace GameDraw.Windows.Input;

public sealed class Win32InputController : IInputController
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventAbsolute = 0x8000;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint KeyboardEventKeyUp = 0x0002;
    private const uint KeyboardEventUnicode = 0x0004;
    private const int SystemMetricVirtualScreenLeft = 76;
    private const int SystemMetricVirtualScreenTop = 77;
    private const int SystemMetricVirtualScreenWidth = 78;
    private const int SystemMetricVirtualScreenHeight = 79;

    public ValueTask MoveToAsync(ScreenPoint point, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = GetVirtualScreenBounds();
        var x = (uint)Math.Clamp(
            Math.Round((point.X - bounds.Left) * 65535d / Math.Max(1, bounds.Width - 1)),
            0d,
            65535d);
        var y = (uint)Math.Clamp(
            Math.Round((point.Y - bounds.Top) * 65535d / Math.Max(1, bounds.Height - 1)),
            0d,
            65535d);

        Send(new INPUT
        {
            Type = InputMouse,
            Data = new INPUTUNION
            {
                Mouse = new MOUSEINPUT
                {
                    Dx = (int)x,
                    Dy = (int)y,
                    Flags = MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk
                }
            }
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask MouseDownAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendMouseButton(button, true);
        return ValueTask.CompletedTask;
    }

    public ValueTask MouseUpAsync(InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendMouseButton(button, false);
        return ValueTask.CompletedTask;
    }

    public async ValueTask ClickAsync(ScreenPoint point, InputMouseButton button = InputMouseButton.Left, CancellationToken cancellationToken = default)
    {
        await MoveToAsync(point, cancellationToken).ConfigureAwait(false);
        await MouseDownAsync(button, cancellationToken).ConfigureAwait(false);
        await MouseUpAsync(button, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask KeyDownAsync(InputKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendKeyboard(MapKey(key), 0);
        return ValueTask.CompletedTask;
    }

    public ValueTask KeyUpAsync(InputKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendKeyboard(MapKey(key), KeyboardEventKeyUp);
        return ValueTask.CompletedTask;
    }

    public ValueTask TypeTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendKeyboard(0, KeyboardEventUnicode, character);
            SendKeyboard(0, KeyboardEventUnicode | KeyboardEventKeyUp, character);
        }

        return ValueTask.CompletedTask;
    }

    private static void SendMouseButton(InputMouseButton button, bool down)
    {
        var flags = (button, down) switch
        {
            (InputMouseButton.Left, true) => MouseEventLeftDown,
            (InputMouseButton.Left, false) => MouseEventLeftUp,
            (InputMouseButton.Right, true) => MouseEventRightDown,
            (InputMouseButton.Right, false) => MouseEventRightUp,
            (InputMouseButton.Middle, true) => MouseEventMiddleDown,
            _ => MouseEventMiddleUp
        };

        Send(new INPUT
        {
            Type = InputMouse,
            Data = new INPUTUNION { Mouse = new MOUSEINPUT { Flags = flags } }
        });
    }

    private static void SendKeyboard(ushort virtualKey, uint flags, ushort unicodeCharacter = 0)
    {
        Send(new INPUT
        {
            Type = InputKeyboard,
            Data = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    ScanCode = unicodeCharacter,
                    Flags = flags
                }
            }
        });
    }

    private static void Send(INPUT input)
    {
        var inputs = new[] { input };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
        {
            throw new InvalidOperationException($"SendInput failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    private static ushort MapKey(InputKey key) => key switch
    {
        InputKey.Control => 0x11,
        InputKey.A => 0x41,
        InputKey.Enter => 0x0D,
        InputKey.Escape => 0x1B,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private static ScreenRect GetVirtualScreenBounds() => new(
        GetSystemMetrics(SystemMetricVirtualScreenLeft),
        GetSystemMetrics(SystemMetricVirtualScreenTop),
        GetSystemMetrics(SystemMetricVirtualScreenWidth),
        GetSystemMetrics(SystemMetricVirtualScreenHeight));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInput);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
