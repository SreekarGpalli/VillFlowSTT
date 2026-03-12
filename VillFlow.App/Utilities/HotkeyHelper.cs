// VillFlow.App/Utilities/HotkeyHelper.cs
// Shared utility class for hotkey-related functionality

using System.Collections.Generic;

namespace VillFlow.App.Utilities;

public static class HotkeyHelper
{
    /// <summary>
    /// Formats a hotkey combination from modifier flags and virtual key code
    /// into a human-readable string (e.g., "Ctrl + Alt + Space").
    /// </summary>
    /// <param name="modifiers">Modifier flags: 0x0002=Ctrl, 0x0001=Alt, 0x0004=Shift</param>
    /// <param name="vk">Virtual key code</param>
    /// <returns>Formatted hotkey string</returns>
    public static string FormatHotkey(int modifiers, int vk)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");

        string keyName;
        if (vk == 0x20)
            keyName = "Space";
        else
        {
            try
            {
                keyName = ((System.Windows.Input.Key)System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk)).ToString();
            }
            catch
            {
                keyName = $"Key{vk}";
            }
        }
        parts.Add(keyName);

        return string.Join(" + ", parts);
    }
}