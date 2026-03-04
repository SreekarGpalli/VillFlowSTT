// VillFlow.Core/Services/HotkeyService.cs
// Registers a global hotkey (default Ctrl+Space) via Win32 RegisterHotKey.
// Raises HotkeyPressed / HotkeyReleased events for hold-to-record.
using System.Runtime.InteropServices;

namespace VillFlow.Core.Services;

/// <summary>
/// Global hotkey service using RegisterHotKey/UnregisterHotKey Win32 APIs.
/// Requires a window handle (HWND) to receive WM_HOTKEY messages.
/// Hold-to-record: fires <see cref="HotkeyPressed"/> on keydown, <see cref="HotkeyReleased"/> on keyup.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    // ── Win32 Imports ───────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // ── Constants ───────────────────────────────────────────────────────────
    private const int HOTKEY_ID = 0x1001;
    public const int WM_HOTKEY = 0x0312;

    // Modifier flags
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // ── State ───────────────────────────────────────────────────────────────
    private IntPtr _hwnd;
    private uint _modifiers;
    private uint _vk;
    private bool _registered;
    private volatile bool _isHeld;
    private System.Threading.Timer? _releasePoller;
    private readonly object _pollerLock = new();
    private bool _disposed;

    // ── Events ──────────────────────────────────────────────────────────────
    /// <summary>Fires once when the hotkey is first pressed down.</summary>
    public event Action? HotkeyPressed;
    /// <summary>Fires once when the hotkey is released.</summary>
    public event Action? HotkeyReleased;

    /// <summary>
    /// Registers the hotkey on the given window handle.
    /// Must be called from the UI thread (WPF dispatcher thread).
    /// </summary>
    public bool Register(IntPtr hwnd, uint modifiers, uint vk)
    {
        Unregister();

        _hwnd = hwnd;
        _modifiers = modifiers;
        _vk = vk;

        // MOD_NOREPEAT prevents repeated WM_HOTKEY while key is held
        _registered = RegisterHotKey(hwnd, HOTKEY_ID, modifiers | MOD_NOREPEAT, vk);
        return _registered;
    }

    /// <summary>
    /// Call this from the WndProc / HwndSource hook when WM_HOTKEY is received.
    /// Starts a polling timer to detect key release for hold-to-record.
    /// </summary>
    public void HandleHotkeyMessage()
    {
        if (_isHeld) return; // Already recording

        _isHeld = true;
        HotkeyPressed?.Invoke();

        // Poll every 30ms to detect key release (GetAsyncKeyState returns 0 when released)
        lock (_pollerLock)
        {
            _releasePoller = new System.Threading.Timer(CheckKeyRelease, null, 30, 30);
        }
    }

    private void CheckKeyRelease(object? state)
    {
        // Check if the primary key (non-modifier) is still held
        short keyState = GetAsyncKeyState((int)_vk);
        bool isDown = (keyState & 0x8000) != 0;

        if (!isDown && _isHeld)
        {
            _isHeld = false;
            lock (_pollerLock)
            {
                _releasePoller?.Dispose();
                _releasePoller = null;
            }
            HotkeyReleased?.Invoke();
        }
    }

    /// <summary>Unregisters the current hotkey.</summary>
    public void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _registered = false;
        }
        _releasePoller?.Dispose();
        _releasePoller = null;
        _isHeld = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
