// VillFlow.Core/Settings/SettingsService.cs
// JSON-based settings persistence to %LOCALAPPDATA%\VillFlow\settings.json
using System.Text.Json;

namespace VillFlow.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as a JSON file.
/// Thread-safe via lock. No SQLite, no encryption — plaintext by design.
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VillFlow");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

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
                }
                else
                {
                    _cached = new AppSettings();
                }
            }
            catch
            {
                // Corrupt file — start fresh
                _cached = new AppSettings();
            }
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
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
    }
}
