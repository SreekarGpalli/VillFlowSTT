// VillFlow.Core/Services/ISttService.cs
// Interface for speech-to-text providers.

namespace VillFlow.Core.Services;

/// <summary>
/// Contract for STT providers. Each implementation handles its own API format.
/// Implementations own an HttpClient and must dispose it.
/// </summary>
public interface ISttService : IDisposable
{
    /// <summary>
    /// Transcribes WAV audio bytes to text.
    /// </summary>
    /// <param name="wavBytes">Complete WAV file as byte array (with header).</param>
    /// <param name="cancellationToken">Cancellation token (typically tied to timeout).</param>
    /// <returns>Transcribed text, or null on failure.</returns>
    Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken);
}
