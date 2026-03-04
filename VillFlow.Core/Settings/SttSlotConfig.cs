// VillFlow.Core/Settings/SttSlotConfig.cs
// Per-slot STT configuration — user picks provider, key, endpoint, and model.
using System.Text.Json.Serialization;

namespace VillFlow.Core.Settings;

/// <summary>
/// One STT endpoint slot. App tries slots in order: Primary → Fallback 1 → Fallback 2.
/// </summary>
public sealed class SttSlotConfig
{
    public SttProvider Provider { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>
    /// Base URL for the STT API. Auto-filled for known providers, editable for Custom.
    /// Groq default: https://api.groq.com/openai/v1
    /// Custom: user-supplied.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Selected model ID (e.g. "whisper-large-v3-turbo", "scribe_v1").</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>HTTP timeout in seconds for this slot.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Returns true if this slot has enough data to attempt a transcription.</summary>
    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Model);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SttProvider
{
    Groq,
    Custom
}
