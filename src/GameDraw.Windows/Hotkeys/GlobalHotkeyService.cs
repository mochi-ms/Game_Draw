using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GameDraw.Windows.Hotkeys;

public sealed class GlobalHotkeyService : IDisposable
{
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyF7 = 0x76;
    private const uint VirtualKeyF8 = 0x77;
    private const uint WindowMessageHotKey = 0x0312;
    private const int PauseHotkeyId = 0x4744;
    private const int StopHotkeyId = 0x4745;
    private const uint SubclassId = 0x4755;

    private readonly object _sync = new();
    private nint _windowHandle;
    private SubclassProc? _subclassProc;
    private bool _pauseRegistered;
    private bool _stopRegistered;
    private bool _subclassAttached;
    private bool _disposed;

    public event EventHandler? PauseRequested;

    public event EventHandler? EmergencyStopRequested;

    public event EventHandler<HotkeyRegistrationFailedEventArgs>? RegistrationFailed;

    public bool Attach(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        lock (_sync)
        {
            if (_windowHandle != nint.Zero)
            {
                return true;
            }

            _windowHandle = windowHandle;
            _subclassProc = WindowProcedure;
            _subclassAttached = SetWindowSubclass(_windowHandle, _subclassProc, SubclassId, 0);
            if (!_subclassAttached)
            {
                RegistrationFailed?.Invoke(this, new HotkeyRegistrationFailedEventArgs("window subclass", Marshal.GetLastWin32Error()));
            }

            _pauseRegistered = RegisterHotKey(_windowHandle, PauseHotkeyId, ModNoRepeat, VirtualKeyF7);
            if (!_pauseRegistered)
            {
                RegistrationFailed?.Invoke(this, new HotkeyRegistrationFailedEventArgs("F7", Marshal.GetLastWin32Error()));
            }

            _stopRegistered = RegisterHotKey(_windowHandle, StopHotkeyId, ModNoRepeat, VirtualKeyF8);
            if (!_stopRegistered)
            {
                RegistrationFailed?.Invoke(this, new HotkeyRegistrationFailedEventArgs("F8", Marshal.GetLastWin32Error()));
            }

            return _subclassAttached && _pauseRegistered && _stopRegistered;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_windowHandle != nint.Zero)
            {
                if (_pauseRegistered)
                {
                    UnregisterHotKey(_windowHandle, PauseHotkeyId);
                }

                if (_stopRegistered)
                {
                    UnregisterHotKey(_windowHandle, StopHotkeyId);
                }

                if (_subclassAttached && _subclassProc is not null)
                {
                    RemoveWindowSubclass(_windowHandle, _subclassProc, SubclassId);
                }
            }

            _disposed = true;
        }
    }

    private nint WindowProcedure(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        if (message == WindowMessageHotKey)
        {
            switch ((int)wParam.ToInt64())
            {
                case PauseHotkeyId:
                    PauseRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case StopHotkeyId:
                    EmergencyStopRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint windowHandle, SubclassProc procedure, nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint windowHandle, SubclassProc procedure, nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint windowHandle, uint message, nint wParam, nint lParam);

    private delegate nint SubclassProc(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData);
}

public sealed class HotkeyRegistrationFailedEventArgs(string hotkey, int errorCode) : EventArgs
{
    public string Hotkey { get; } = hotkey;

    public int ErrorCode { get; } = errorCode;

    public Win32Exception Exception => new(ErrorCode);
}
