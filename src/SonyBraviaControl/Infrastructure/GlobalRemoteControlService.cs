using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace SonyBraviaControl.Infrastructure;

/// <summary>
/// Registers Ctrl+Alt+Space as a system-wide toggle and, only while the mode is active,
/// captures the remote-control keys through a low-level keyboard hook.
/// </summary>
public sealed class GlobalRemoteControlService : IDisposable
{
    private const int HotKeyId = 0x4252;
    private const int WmHotKey = 0x0312;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    private readonly HwndSource _source;
    private readonly Func<Key, bool> _shouldCapture;
    private readonly Action<Key> _keyPressed;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly HwndSourceHook _windowHook;
    private IntPtr _keyboardHook;
    private bool _hotKeyRegistered;
    private bool _disposed;

    public GlobalRemoteControlService(
        HwndSource source,
        Func<Key, bool> shouldCapture,
        Action<Key> keyPressed)
    {
        _source = source;
        _shouldCapture = shouldCapture;
        _keyPressed = keyPressed;
        _keyboardProc = KeyboardHookCallback;
        _windowHook = WindowMessageHook;
    }

    public bool IsActive { get; private set; }

    public event Action<bool>? ActiveChanged;

    public bool Start()
    {
        if (_disposed)
            return false;

        _source.AddHook(_windowHook);
        _hotKeyRegistered = RegisterHotKey(
            _source.Handle,
            HotKeyId,
            ModControl | ModAlt | ModNoRepeat,
            (uint)KeyInterop.VirtualKeyFromKey(Key.Space));

        return _hotKeyRegistered;
    }

    public void Toggle()
    {
        if (IsActive)
            Deactivate();
        else
            Activate();
    }

    public void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;
        RemoveKeyboardHook();
        ActiveChanged?.Invoke(false);
    }

    private void Activate()
    {
        if (IsActive || _disposed)
            return;

        // Keep the hook callback extremely small. The actual TV command is queued by MainWindow.
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, GetModuleHandle(null), 0);
        if (_keyboardHook == IntPtr.Zero)
            return;

        IsActive = true;
        ActiveChanged?.Invoke(true);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            Toggle();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !IsActive)
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        var message = wParam.ToInt32();
        var isKeyDown = message is WmKeyDown or WmSysKeyDown;
        var isKeyUp = message is WmKeyUp or WmSysKeyUp;
        if (!isKeyDown && !isKeyUp)
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var key = KeyInterop.KeyFromVirtualKey(unchecked((int)data.VkCode));

        // Never swallow the Ctrl+Alt+Space chord itself; Windows must still receive it
        // so RegisterHotKey can toggle the mode off while the low-level hook is installed.
        if (key == Key.Space && IsControlDown() && IsAltDown())
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        if (!_shouldCapture(key))
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        if (isKeyDown)
            _keyPressed(key);

        // Consume both key-down and key-up for remote keys so the focused PC app never
        // sees half of a keystroke while global remote mode is active.
        return new IntPtr(1);
    }

    private static bool IsControlDown() => (GetAsyncKeyState(VkControl) & 0x8000) != 0;
    private static bool IsAltDown() => (GetAsyncKeyState(VkMenu) & 0x8000) != 0;

    private void RemoveKeyboardHook()
    {
        if (_keyboardHook == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        RemoveKeyboardHook();

        if (_hotKeyRegistered)
        {
            UnregisterHotKey(_source.Handle, HotKeyId);
            _hotKeyRegistered = false;
        }

        _source.RemoveHook(_windowHook);
        GC.SuppressFinalize(this);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
