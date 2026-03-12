// VillFlow.Core/Services/ModelFetchService.cs
// Dynamically fetches available model lists from STT and LLM provider APIs.
// Used by the setup wizard and settings UI to populate model dropdowns.
using System.Net.Http.Headers;
using System.Text.Json;
using VillFlow.Core.Settings;

namespace VillFlow.Core.Services;

/// <summary>
/// Fetches available models from STT and LLM provider APIs.
/// Returns a list of (ModelId, DisplayName) tuples filtered by provider type.
/// </summary>
public static class ModelFetchService
{
    // Use a shared handler but set auth per-request to avoid header contamination
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // ── STT Model Fetching ──────────────────────────────────────────────────

    /// <summary>
    /// Fetches available STT models for the given provider configuration.
    /// </summary>
    public static async Task<List<ModelInfo>> FetchSttModelsAsync(SttProvider provider, string apiKey, string baseUrl)
    {
        baseUrl = baseUrl.TrimEnd('/');

        return provider switch
        {
            SttProvider.Groq => await FetchGroqModelsAsync(apiKey, baseUrl),
            SttProvider.Custom => await FetchCustomModelsAsync(apiKey, baseUrl),
            _ => new List<ModelInfo>()
        };
    }

    /// <summary>
    /// Fetches Groq models and filters to only whisper/audio models.
    /// GET {baseUrl}/models with Bearer auth.
    /// </summary>
    private static async Task<List<ModelInfo>> FetchGroqModelsAsync(string apiKey, string baseUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var models = new List<ModelInfo>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                try
                {
                    if (!item.TryGetProperty("id", out var idProp)) continue;
                    var id = idProp.GetString() ?? "";
                    if (id.Contains("whisper", StringComparison.OrdinalIgnoreCase) ||
                        id.Contains("distil-whisper", StringComparison.OrdinalIgnoreCase))
                        models.Add(new ModelInfo(id, id));
                }
                catch { /* Skip malformed model object */ }
            }
        }
        return models;
    }

    /// <summary>
    /// Fetches all models from a custom OpenAI-compatible endpoint.
    /// No filtering — shows everything.
    /// </summary>
    private static async Task<List<ModelInfo>> FetchCustomModelsAsync(string apiKey, string baseUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var models = new List<ModelInfo>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                try
                {
                    if (!item.TryGetProperty("id", out var idProp)) continue;
                    var id = idProp.GetString() ?? "";
                    models.Add(new ModelInfo(id, id));
                }
                catch { /* Skip malformed model object */ }
            }
        }
        return models;
    }

    // ── LLM / Polish Model Fetching ─────────────────────────────────────────

    /// <summary>
    /// Fetches available chat/LLM models for the given polish provider.
    /// </summary>
    public static async Task<List<ModelInfo>> FetchPolishModelsAsync(PolishProvider provider, string apiKey, string baseUrl)
    {
        baseUrl = baseUrl.TrimEnd('/');

        return provider switch
        {
            PolishProvider.Groq => await FetchGroqPolishModelsAsync(apiKey, baseUrl),
            PolishProvider.Custom => await FetchCustomModelsAsync(apiKey, baseUrl),
            _ => new List<ModelInfo>()
        };
    }

    /// <summary>Fetches Groq models for text polishing — filters to LLM chat models (excludes whisper).</summary>
    private static async Task<List<ModelInfo>> FetchGroqPolishModelsAsync(string apiKey, string baseUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var models = new List<ModelInfo>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                try
                {
                    if (!item.TryGetProperty("id", out var idProp)) continue;
                    var id = idProp.GetString() ?? "";
                    if (!id.Contains("whisper", StringComparison.OrdinalIgnoreCase) &&
                        !id.Contains("distil-whisper", StringComparison.OrdinalIgnoreCase) &&
                        !id.Contains("tool-use", StringComparison.OrdinalIgnoreCase))
                        models.Add(new ModelInfo(id, id));
                }
                catch { /* Skip malformed model object */ }
            }
        }
        return models;
    }
}

/// <summary>Simple model info record for populating dropdowns.</summary>
public record ModelInfo(string Id, string DisplayName);

