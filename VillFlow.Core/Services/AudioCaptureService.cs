// VillFlow.Core/Services/AudioCaptureService.cs
// NAudio-based microphone capture: 16kHz, 16-bit, mono, 30ms frames.
// All audio is directly appended to the SpeechBuffer (no VAD filtering).
using NAudio.Wave;

namespace VillFlow.Core.Services;

/// <summary>
/// Captures microphone audio using NAudio <see cref="WaveInEvent"/>.
/// All captured audio is appended directly to the <see cref="SpeechBuffer"/> — no VAD filtering.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    // ── Audio format: 16kHz, 16-bit, mono, 30ms buffers ────────────────────
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int BufferMs = 30;

    private WaveInEvent? _waveIn;
    private readonly SpeechBuffer _speechBuffer;
    private bool _isRecording;
    private readonly object _recordLock = new();
    private int _totalFrames;

    /// <summary>Optional logger for debug trace.</summary>
    public static Action<string>? LogInfo { get; set; }

    /// <summary>Fires for each raw audio frame. Useful for level meters or debug.</summary>
    public event Action<byte[], int>? RawFrameAvailable;

    public AudioCaptureService(SpeechBuffer speechBuffer)
    {
        _speechBuffer = speechBuffer;
    }

    /// <summary>
    /// Begins capturing audio from the specified device.
    /// Creates a fresh WaveInEvent each time (clean state, no lingering buffers).
    /// </summary>
    public void Start(int deviceNumber)
    {
        lock (_recordLock)
        {
            if (_isRecording) return;

            _totalFrames = 0;

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber, // -1 is WAVE_MAPPER (System Default)
                WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
                BufferMilliseconds = BufferMs,
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
            _isRecording = true;
            LogInfo?.Invoke($"[AudioCapture] Started recording on device {deviceNumber}");
        }
    }

    /// <summary>Stops recording and disposes the WaveInEvent immediately.</summary>
    public void Stop()
    {
        lock (_recordLock)
        {
            if (!_isRecording) return;
            _isRecording = false;

            try { _waveIn?.StopRecording(); }
            catch { /* WaveIn may already be disposed if device disconnected */ }

            DisposeWaveIn();

            LogInfo?.Invoke($"[AudioCapture] Stopped. Total frames={_totalFrames}, SpeechBuffer={_speechBuffer.Length} bytes");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        _totalFrames++;

        // Notify raw frame listeners (e.g., level meter)
        RawFrameAvailable?.Invoke(e.Buffer, e.BytesRecorded);

        // Append ALL audio directly — no VAD filtering
        _speechBuffer.Append(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            LogInfo?.Invoke($"[AudioCapture] Recording error: {e.Exception.Message}");
        }
    }

    private void DisposeWaveIn()
    {
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    /// <summary>
    /// Returns a list of available input devices with their indices.
    /// </summary>
    public static List<(int DeviceNumber, string Name)> GetInputDevices()
    {
        var devices = new List<(int, string)>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add((i, caps.ProductName));
        }
        return devices;
    }

    public void Dispose()
    {
        Stop();
    }
}
