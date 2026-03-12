// VillFlow.Core/Services/CustomSttService.cs
// Robust custom OpenAI-compatible STT endpoint.
// Handles: Cloudflare Workers, local Whisper servers, self-hosted APIs, etc.
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VillFlow.Core.Services;

/// <summary>
/// Transcribes audio using any OpenAI-compatible /audio/transcriptions endpoint.
/// Built for maximum compatibility with diverse server implementations.
/// </summary>
public sealed class CustomSttService : ISttService
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly string _model;

    // Maximum length for error log messages
    private const int MAX_ERROR_LOG_LENGTH = 500;

    /// <summary>Optional logger.</summary>
    public static Action<string>? LogInfo { get; set; }

    public CustomSttService(string apiKey, string baseUrl, string model, int timeoutSeconds)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _model = model;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken ct)
    {
        // Build the endpoint URL intelligently
        var url = BuildTranscriptionUrl(_baseUrl);

        // Try twice: first with strict multipart headers, fallback with standard multipart
        string? result = null;
        Exception? lastEx = null;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var content = attempt == 1
                    ? BuildStrictMultipart(wavBytes)
                    : BuildStandardMultipart(wavBytes);

                var response = await _client.PostAsync(url, content, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    var truncatedError = TruncateErrorLog(errorBody);
                    LogInfo?.Invoke($"[CustomSTT] Attempt {attempt} failed: HTTP {(int)response.StatusCode} — {truncatedError}");

                    // Don't retry on auth errors or client errors (including 400 which indicates bad request)
                    if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                        throw new HttpRequestException($"Authentication failed ({(int)response.StatusCode})");
                    if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        throw new HttpRequestException($"Client error ({(int)response.StatusCode}): {truncatedError}");

                    lastEx = new HttpRequestException($"HTTP {(int)response.StatusCode}: {truncatedError}");
                    continue; // Try fallback format
                }

                result = await ParseResponse(response, ct);
                if (result != null) return result;

                LogInfo?.Invoke($"[CustomSTT] Attempt {attempt}: response parsed but no text found");
                lastEx = new InvalidOperationException("API returned success but no transcript text");
            }
            catch (OperationCanceledException) { throw; } // Don't retry timeouts
            catch (HttpRequestException) { throw; } // Don't retry HTTP errors (including 4xx client errors)
            catch (Exception ex) when (attempt == 1)
            {
                LogInfo?.Invoke($"[CustomSTT] Attempt {attempt} error: {ex.Message}");
                lastEx = ex;
                // Continue to fallback
            }
        }

        if (lastEx != null) throw lastEx;
        return null;
    }

    /// <summary>
    /// Strict multipart — explicit Content-Disposition with quoted names.
    /// Required by Cloudflare Workers and some strict parsers.
    /// </summary>
    private MultipartFormDataContent BuildStrictMultipart(byte[] wavBytes)
    {
        var boundary = $"----VillFlow{Guid.NewGuid():N}";
        var content = new MultipartFormDataContent(boundary);

        var audioContent = new ByteArrayContent(wavBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        audioContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"file\"",
            FileName = "\"audio.wav\""
        };
        content.Add(audioContent);

        var modelContent = new StringContent(_model, Encoding.UTF8, "text/plain");
        modelContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"model\""
        };
        content.Add(modelContent);

        return content;
    }

    /// <summary>
    /// Standard multipart — uses .NET's default Content-Disposition formatting.
    /// Works with most standard OpenAI-compatible servers.
    /// </summary>
    private MultipartFormDataContent BuildStandardMultipart(byte[] wavBytes)
    {
        var content = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(wavBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");
        content.Add(new StringContent(_model), "model");

        return content;
    }

    /// <summary>
    /// Flexibly parses the API response — handles JSON with various key names
    /// and plain text responses.
    /// </summary>
    private static async Task<string?> ParseResponse(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return null;

        body = body.Trim();

        // Try JSON parsing first
        if (body.StartsWith("{") || body.StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Standard OpenAI format: { "text": "..." }
                if (root.TryGetProperty("text", out var textProp))
                    return textProp.GetString();

                // Some servers: { "result": "..." }
                if (root.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.String)
                    return resultProp.GetString();

                // Some servers: { "transcription": "..." }
                if (root.TryGetProperty("transcription", out var transProp) && transProp.ValueKind == JsonValueKind.String)
                    return transProp.GetString();

                // Some servers: { "data": { "text": "..." } }
                if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Object)
                {
                    if (dataProp.TryGetProperty("text", out var dataTextProp))
                        return dataTextProp.GetString();
                }

                // Some servers: { "results": [{ "text": "..." }] } or { "segments": [...] }
                if (root.TryGetProperty("results", out var resultsProp) && resultsProp.ValueKind == JsonValueKind.Array)
                {
                    var texts = new List<string>();
                    foreach (var item in resultsProp.EnumerateArray())
                    {
                        if (item.TryGetProperty("text", out var itemText))
                            texts.Add(itemText.GetString() ?? "");
                    }
                    if (texts.Count > 0) return string.Join(" ", texts);
                }

                // Segments format (Whisper verbose_json): { "segments": [{ "text": "..." }, ...] }
                if (root.TryGetProperty("segments", out var segProp) && segProp.ValueKind == JsonValueKind.Array)
                {
                    var texts = new List<string>();
                    foreach (var seg in segProp.EnumerateArray())
                    {
                        if (seg.TryGetProperty("text", out var segText))
                            texts.Add(segText.GetString()?.Trim() ?? "");
                    }
                    if (texts.Count > 0) return string.Join(" ", texts);
                }

                LogInfo?.Invoke($"[CustomSTT] Unknown JSON structure, keys: {string.Join(", ", EnumerateKeys(root))}");
                return null;
            }
            catch (JsonException ex)
            {
                LogInfo?.Invoke($"[CustomSTT] JSON parse failed: {ex.Message}");
            }
        }

        // Plain text response — some lightweight servers return just the text
        if (!body.StartsWith("<")) // Not HTML error page
        {
            return body;
        }

        LogInfo?.Invoke("[CustomSTT] Response appears to be HTML, not valid transcript");
        return null;
    }

    /// <summary>Builds the transcription endpoint URL, handling various base URL formats.</summary>
    private static string BuildTranscriptionUrl(string baseUrl)
    {
        // If URL already contains the transcription path, use as-is
        if (baseUrl.Contains("/audio/transcriptions", StringComparison.OrdinalIgnoreCase))
            return baseUrl;

        // If URL ends with /v1 or similar version path, append the endpoint
        return $"{baseUrl}/audio/transcriptions";
    }

    /// <summary>Normalizes the base URL — trims whitespace, trailing slashes, etc.</summary>
    private static string NormalizeBaseUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');

        // Ensure https:// or http:// prefix
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        return url;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length > maxLen ? text[..maxLen] + "..." : text;

    private static string TruncateErrorLog(string errorBody) =>
        Truncate(errorBody, MAX_ERROR_LOG_LENGTH);

    private static IEnumerable<string> EnumerateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
                yield return prop.Name;
        }
    }

    public void Dispose() => _client?.Dispose();
}
