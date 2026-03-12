// VillFlow.Core/Settings/SettingsService.cs
// JSON-based settings persistence to %LOCALAPPDATA%\VillFlow\settings.json
// API keys are encrypted with DPAPI before storage.
using System.Text.Json;

namespace VillFlow.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as a JSON file.
/// Thread-safe via lock. API keys are encrypted with DPAPI before storage.
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VillFlow");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly string BackupPath =
        Path.Combine(SettingsDir, "settings.json.bak");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _lock = new();
    private AppSettings _cached;

    public SettingsService()
    {
        _cached = Load();
    }

    /// <summary>Returns the current in-memory settings. Call <see cref="Reload"/> to re-read from disk.</summary>
    public AppSettings Current
    {
        get { lock (_lock) return _cached; }
    }

    /// <summary>Loads settings from disk. Returns default settings if file does not exist or is corrupt.</summary>
    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _cached = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                    if (_cached.SttSlots == null)
                        _cached.SttSlots = new List<SttSlotConfig>();
                }
                else
                {
                    _cached = new AppSettings();
                }
            }
            catch
            {
                // Corrupt file — try to restore from backup
                if (File.Exists(BackupPath))
                {
                    try
                    {
                        var backupJson = File.ReadAllText(BackupPath);
                        _cached = JsonSerializer.Deserialize<AppSettings>(backupJson, JsonOptions) ?? new AppSettings();
                    }
                    catch
                    {
                        // Backup also corrupt — start fresh
                        _cached = new AppSettings();
                    }
                }
                else
                {
                    // No backup — start fresh
                    _cached = new AppSettings();
                }
            }
            if (_cached.SttSlots == null)
                _cached.SttSlots = new List<SttSlotConfig>();
            DecryptApiKeys(_cached);
            return _cached;
        }
    }

    /// <summary>Re-reads the file from disk.</summary>
    public AppSettings Reload() => Load();

    /// <summary>Writes the provided settings to disk and updates the in-memory cache.</summary>
    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            _cached = settings;
            Directory.CreateDirectory(SettingsDir);

            if (File.Exists(SettingsPath))
                File.Copy(SettingsPath, BackupPath, overwrite: true);

            var toSerialize = CloneForStorage(settings);
            EncryptApiKeys(toSerialize);
            var json = JsonSerializer.Serialize(toSerialize, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
    }

    private static AppSettings CloneForStorage(AppSettings s)
    {
        var json = JsonSerializer.Serialize(s, JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    private static void EncryptApiKeys(AppSettings s)
    {
        if (s.SttSlots != null)
        {
            foreach (var slot in s.SttSlots)
            {
                if (!string.IsNullOrEmpty(slot.ApiKey))
                    slot.ApiKey = ApiKeyProtection.Encrypt(slot.ApiKey);
            }
        }
        if (s.Polish != null && !string.IsNullOrEmpty(s.Polish.ApiKey))
            s.Polish.ApiKey = ApiKeyProtection.Encrypt(s.Polish.ApiKey);
    }

    private static void DecryptApiKeys(AppSettings s)
    {
        if (s.SttSlots != null)
        {
            foreach (var slot in s.SttSlots)
            {
                if (!string.IsNullOrEmpty(slot.ApiKey))
                    slot.ApiKey = ApiKeyProtection.Decrypt(slot.ApiKey);
            }
        }
        if (s.Polish != null && !string.IsNullOrEmpty(s.Polish.ApiKey))
            s.Polish.ApiKey = ApiKeyProtection.Decrypt(s.Polish.ApiKey);
    }
}
