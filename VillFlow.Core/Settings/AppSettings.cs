// VillFlow.Core/Settings/AppSettings.cs
// Root settings model — serialized to %LOCALAPPDATA%\VillFlow\settings.json

namespace VillFlow.Core.Settings;

/// <summary>
/// Top-level application settings. No defaults for STT/Polish — user must configure via setup wizard.
/// </summary>
public sealed class AppSettings
{
    // ── STT Pipeline (user-configured, up to 3 slots) ──────────────────────
    /// <summary>
    /// SttSlots[0] = Primary, [1] = Fallback 1, [2] = Fallback 2.
    /// Non-null slots are tried in order on failure.
    /// </summary>
    public List<SttSlotConfig> SttSlots { get; set; } = new();

    // ── Text Polish ─────────────────────────────────────────────────────────
    public bool PolishEnabled { get; set; }
    public PolishConfig? Polish { get; set; }

    // ── Hotkey ──────────────────────────────────────────────────────────────
    /// <summary>Win32 modifier flags (MOD_CONTROL=0x0002, MOD_ALT=0x0001, MOD_SHIFT=0x0004).</summary>
    public int HotkeyModifiers { get; set; } = 0x0002; // MOD_CONTROL
    /// <summary>Win32 virtual key code (VK_SPACE=0x20).</summary>
    public int HotkeyKey { get; set; } = 0x20; // VK_SPACE

    // ── Microphone ──────────────────────────────────────────────────────────
    /// <summary>NAudio device number. -1 = system default.</summary>
    public int SelectedMicDeviceNumber { get; set; } = -1;

    // ── Setup Gate ──────────────────────────────────────────────────────────
    /// <summary>False until the setup wizard is completed. App blocks on false.</summary>
    public bool SetupComplete { get; set; }
}
