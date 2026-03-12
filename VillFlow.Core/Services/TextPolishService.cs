// VillFlow.Core/Services/TextPolishService.cs
// AI text polishing via OpenAI-compatible chat completions endpoint.
// Supports Groq and Custom providers (both use OpenAI-compatible format).
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VillFlow.Core.Settings;

namespace VillFlow.Core.Services;

/// <summary>
/// Post-processes raw STT transcript through an LLM for grammar/accent correction.
/// Uses the OpenAI chat completions format (/chat/completions).
/// </summary>
public sealed class TextPolishService : IDisposable
{
    private readonly HttpClient _client;
    private readonly PolishConfig _config;

    /// <summary>Optional logger.</summary>
    public static Action<string>? LogInfo { get; set; }

    public TextPolishService(PolishConfig config)
    {
        _config = config;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds) };

        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }
    }

    /// <summary>
    /// Polishes the given raw transcript text. Returns the polished text, or the
    /// original text if the API call fails (fail-safe — never lose the transcript).
    /// </summary>
    public async Task<string> PolishAsync(string rawText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return rawText;

        try
        {
            var baseUrl = NormalizeUrl(_config.BaseUrl);

            var payload = new
            {
                model = _config.Model,
                messages = new[]
                {
                    new { role = "system", content = _config.SystemPrompt },
                    new { role = "user", content = $"[DICTATION]{rawText}[/DICTATION]" }
                },
                temperature = 0.0,
                max_tokens = 4096
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Build the completions URL — handle various base URL formats
            var url = BuildCompletionsUrl(baseUrl);

            var response = await _client.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                LogInfo?.Invoke($"[Polish] HTTP {(int)response.StatusCode}: {Truncate(errorBody, 200)}");
                return rawText; // Fail-safe
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var result = ParseCompletionResponse(responseJson);

            if (string.IsNullOrWhiteSpace(result))
            {
                LogInfo?.Invoke("[Polish] Response parsed but no text content found");
                return rawText; // Fail-safe
            }

            return StripThinkTags(result.Trim());
        }
        catch (OperationCanceledException)
        {
            LogInfo?.Invoke("[Polish] Request timed out");
            return rawText;
        }
        catch (Exception ex)
        {
            LogInfo?.Invoke($"[Polish] Error: {ex.Message}");
            return rawText; // Fail-safe: return raw transcript rather than losing it
        }
    }

    /// <summary>
    /// Flexibly parses chat completion responses from various OpenAI-compatible servers.
    /// </summary>
    private static string? ParseCompletionResponse(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // Standard OpenAI format: { "choices": [{ "message": { "content": "..." } }] }
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var first = choices[0];

                // Standard: choices[0].message.content
                if (first.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var msgContent))
                {
                    return msgContent.GetString();
                }

                // Some servers: choices[0].text
                if (first.TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString();
                }
            }

            // Non-standard: { "response": "..." }
            if (root.TryGetProperty("response", out var responseProp) && responseProp.ValueKind == JsonValueKind.String)
            {
                return responseProp.GetString();
            }

            // Non-standard: { "content": "..." }
            if (root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
            {
                return contentProp.GetString();
            }

            // Non-standard: { "output": "..." }
            if (root.TryGetProperty("output", out var outputProp) && outputProp.ValueKind == JsonValueKind.String)
            {
                return outputProp.GetString();
            }

            LogInfo?.Invoke($"[Polish] Unknown response structure");
            return null;
        }
        catch (JsonException ex)
        {
            LogInfo?.Invoke($"[Polish] JSON parse error: {ex.Message}");
            return null;
        }
    }

    /// <summary>Builds the chat completions URL, handling various base URL formats.</summary>
    private static string BuildCompletionsUrl(string baseUrl)
    {
        // Already contains the chat/completions path
        if (baseUrl.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return baseUrl;

        return $"{baseUrl}/chat/completions";
    }

    /// <summary>Normalizes the base URL.</summary>
    private static string NormalizeUrl(string url)
    {
        var normalized = url.Trim().TrimEnd('/');

        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "https://" + normalized;
        }

        return normalized;
    }

    /// <summary>Strips <think>...</think> tags from reasoning model output.</summary>
    private static string StripThinkTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        while (true)
        {
            int start = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            int end = text.IndexOf("</think>", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                text = text[..start];
                break;
            }
            text = text[..start] + text[(end + "</think>".Length)..];
        }

        // Also strip [DICTATION] tags in case the model echoes them back
        text = text.Replace("[DICTATION]", "").Replace("[/DICTATION]", "");

        return text.Trim();
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length > maxLen ? text[..maxLen] + "..." : text;

    public void Dispose() => _client?.Dispose();
}
