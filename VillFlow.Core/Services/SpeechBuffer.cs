// VillFlow.Core/Services/SpeechBuffer.cs
// Thread-safe in-memory accumulator for raw PCM audio frames.
// Produces WAV bytes (with header) for HTTP upload. Zero disk I/O.

namespace VillFlow.Core.Services;

/// <summary>
/// Accumulates raw PCM audio bytes in memory. Thread-safe.
/// Call <see cref="Append"/> for each speech frame, then <see cref="ToWavBytes"/>
/// to get a complete WAV file as a byte array — no temp files, no disk writes.
/// </summary>
public sealed class SpeechBuffer
{
    private readonly object _lock = new();
    private List<byte> _buffer = new(capacity: 512_000); // ~16 seconds at 16kHz/16bit

    /// <summary>Number of PCM bytes accumulated so far.</summary>
    public int Length
    {
        get { lock (_lock) return _buffer.Count; }
    }

    /// <summary>True if the buffer contains any speech data.</summary>
    public bool HasData
    {
        get { lock (_lock) return _buffer.Count > 0; }
    }

    /// <summary>Appends a raw PCM frame to the buffer.</summary>
    public void Append(byte[] data, int offset, int count)
    {
        lock (_lock)
        {
            if (count <= 0) return;
            // Use AddRange with ArraySegment for efficient bulk copy
            _buffer.AddRange(new ArraySegment<byte>(data, offset, count));
        }
    }

    /// <summary>Clears all buffered data. Call at the start of every new recording.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _buffer.Clear();
        }
    }

    /// <summary>
    /// Returns the buffered PCM data wrapped in a minimal 44-byte WAV header.
    /// Format: 16kHz, 16-bit, mono PCM.
    /// </summary>
    public byte[] ToWavBytes()
    {
        byte[] pcm;
        lock (_lock)
        {
            pcm = _buffer.ToArray();
        }
        return CreateWav(pcm, sampleRate: 16000, bitsPerSample: 16, channels: 1);
    }

    /// <summary>Builds a valid WAV file byte array from raw PCM data.</summary>
    private static byte[] CreateWav(byte[] pcm, int sampleRate, short bitsPerSample, short channels)
    {
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));

        using var ms = new MemoryStream(44 + pcm.Length);
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36 + pcm.Length);               // File size - 8
        bw.Write("WAVE"u8);

        // fmt sub-chunk
        bw.Write("fmt "u8);
        bw.Write(16);                             // Sub-chunk size (PCM = 16)
        bw.Write((short)1);                       // Audio format (1 = PCM)
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);

        // data sub-chunk
        bw.Write("data"u8);
        bw.Write(pcm.Length);
        bw.Write(pcm);

        bw.Flush();
        return ms.ToArray();
    }
}
