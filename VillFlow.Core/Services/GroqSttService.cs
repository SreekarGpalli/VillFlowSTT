// VillFlow.Core/Services/GroqSttService.cs
// Groq Whisper STT via their OpenAI-compatible /audio/transcriptions endpoint.
// Tuned for maximum accuracy: temperature=0, language=en, hallucination filter.
using System.Net.Http.Headers;
using System.Text.Json;

namespace VillFlow.Core.Services;

/// <summary>
/// Transcribes audio using the Groq API (OpenAI-compatible format).
/// Endpoint: POST {baseUrl}/audio/transcriptions
/// </summary>
public sealed class GroqSttService : ISttService
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly string _model;

    public GroqSttService(string apiKey, string baseUrl, string model, int timeoutSeconds)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(wavBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");
        content.Add(new StringContent(_model), "model");
        content.Add(new StringContent("en"), "language");

        // Temperature 0 = deterministic, no random sampling = fewer hallucinations
        content.Add(new StringContent("0"), "temperature");

        // NOTE: Do NOT add a "prompt" parameter — Whisper treats it as previous context
        // and will swallow the beginning of the actual audio, dropping the first sentence.

        var response = await _client.PostAsync($"{_baseUrl}/audio/transcriptions", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var text = doc.RootElement.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;

        // Filter out common Whisper hallucinations on near-silent audio
        if (!string.IsNullOrWhiteSpace(text) && IsLikelyHallucination(text))
        {
            return null;
        }

        return text;
    }

    /// <summary>
    /// Detects common Whisper hallucination patterns that occur on short/quiet audio.
    /// These are well-known artifacts of the Whisper model family.
    /// </summary>
    private static bool IsLikelyHallucination(string text)
    {
        var trimmed = text.Trim().TrimEnd('.').Trim();

        // Whitelist of valid single words that should not be filtered
        string[] validSingleWords = { "Yes", "No", "Okay", "Hello", "Stop", "Go", "Next", "Back" };
        if (validSingleWords.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return false;

        // Known Whisper hallucination phrases (exact or near-exact matches)
        string[] hallucinations =
        {
            "Thank you", "Thanks for watching", "Thanks for listening",
            "Subscribe", "Like and subscribe", "See you next time",
            "Bye", "Goodbye", "You", "I", "The", "Okay",
            "Thank you for watching", "Please subscribe",
            "So", "Yeah", "Hmm", "Uh", "Um"
        };

        foreach (var h in hallucinations)
        {
            if (trimmed.Equals(h, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Very short output (1-2 words) from audio > 1 second is usually a hallucination
        var wordCount = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount <= 1 && trimmed.Length < 2)
            return true;

        return false;
    }

    public void Dispose() => _client?.Dispose();
}
