// VillFlow.Core/Services/TextInjectionService.cs
// Types text at the active cursor via Win32 SendInput + clipboard paste.
using System.Runtime.InteropServices;

namespace VillFlow.Core.Services;

/// <summary>
/// Injects text at the current cursor position using clipboard paste (Ctrl+V).
/// Always uses clipboard paste for maximum reliability across all apps.
/// </summary>
public static class TextInjectionService
{
    // ── Win32 Imports ───────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);


    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    // ── Constants ───────────────────────────────────────────────────────────
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    // VK codes
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_V = 0x56;

    // ── Structs ─────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    /// <summary>Optional logger — set from App.xaml.cs.</summary>
    public static Action<string>? LogInfo { get; set; }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Types the given text at the current cursor position via clipboard paste.
    /// </summary>
    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Release any held modifier keys first
        ReleaseModifiers();

        // Use clipboard paste via STA thread
        PasteViaClipboardSta(text);
    }

    /// <summary>Runs clipboard paste on an STA thread.</summary>
    private static void PasteViaClipboardSta(string text)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try { PasteViaClipboard(text); }
            catch (Exception ex) { threadEx = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool joined = thread.Join(5000);
        if (!joined)
        {
            LogInfo?.Invoke("[TextInjection] WARNING: STA thread timed out after 5s");
        }
        if (threadEx != null)
        {
            LogInfo?.Invoke($"[TextInjection] ERROR: {threadEx.Message}");
            throw threadEx;
        }
    }

    /// <summary>Sets clipboard content and sends Ctrl+V (must run on STA thread).</summary>
    private static void PasteViaClipboard(string text)
    {
        // Open clipboard
        bool opened = OpenClipboard(IntPtr.Zero);
        if (!opened)
        {
            LogInfo?.Invoke("[TextInjection] Failed to open clipboard");
            return;
        }

        try
        {
            EmptyClipboard();

            int byteCount = (text.Length + 1) * 2; // UTF-16, null-terminated
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero)
            {
                LogInfo?.Invoke("[TextInjection] GlobalAlloc failed");
                return;
            }

            var ptr = GlobalLock(hGlobal);
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                Marshal.WriteInt16(ptr + text.Length * 2, 0); // null-terminate
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            SetClipboardData(CF_UNICODETEXT, hGlobal);
        }
        finally
        {
            CloseClipboard();
        }

        // Delay to let clipboard propagate
        Thread.Sleep(100);

        // Release modifiers again before sending Ctrl+V
        ReleaseModifiers();

        // Simulate Ctrl+V
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_CONTROL } } },
            new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_V } } },
            new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_V, dwFlags = KEYEVENTF_KEYUP } } },
            new() { type = INPUT_KEYBOARD, u = new() { ki = new() { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
        };

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            LogInfo?.Invoke($"[TextInjection] SendInput failed: {sent}/{inputs.Length} events");
        }
    }

    /// <summary>Forces release of modifier keys.</summary>
    private static void ReleaseModifiers()
    {
        ushort[] modifiers = {
            VK_LCONTROL, VK_RCONTROL, VK_CONTROL,
            VK_LSHIFT, VK_RSHIFT, VK_SHIFT,
            VK_LMENU, VK_RMENU, VK_MENU,
            VK_LWIN, VK_RWIN
        };

        var releaseInputs = new List<INPUT>();

        foreach (var vk in modifiers)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                releaseInputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new INPUTUNION
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = vk,
                            wScan = 0,
                            dwFlags = KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = UIntPtr.Zero
                        }
                    }
                });
            }
        }

        if (releaseInputs.Count > 0)
        {
            var arr = releaseInputs.ToArray();
            SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
            Thread.Sleep(50);
        }
    }
}
