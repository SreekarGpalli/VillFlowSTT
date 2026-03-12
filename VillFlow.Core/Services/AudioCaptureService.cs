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

    /// <summary>Fires when the microphone device is disconnected during recording.</summary>
    public event Action? DeviceDisconnected;

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

            // Validate device number (-1 = WAVE_MAPPER/system default, 0..N = specific device)
            int deviceCount = WaveInEvent.DeviceCount;
            if (deviceNumber < -1 || deviceNumber >= deviceCount)
            {
                throw new ArgumentException($"Invalid device number {deviceNumber}. Valid range is -1 (default) to {deviceCount - 1}.");
            }

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
            
            // Check if it's a device disconnection error
            if (IsDeviceDisconnectionError(e.Exception))
            {
                // Notify via event
                DeviceDisconnected?.Invoke();
                
                // Stop recording and clean up
                lock (_recordLock)
                {
                    _isRecording = false;
                    DisposeWaveIn();
                }
                
                LogInfo?.Invoke("[AudioCapture] Device disconnected during recording");
            }
        }
    }

    private bool IsDeviceDisconnectionError(Exception ex)
    {
        // Check for NAudio-specific device disconnection errors
        // NAudio might throw MmException with specific error codes
        if (ex is NAudio.MmException mmEx)
        {
            // Common NAudio error codes for device issues:
            // MMSYSERR_NODRIVER = 6 (No device driver is present)
            // MMSYSERR_BADDEVICEID = 2 (The specified device identifier is out of range)
            // WAVERR_BADFORMAT = 32 (The specified format is not supported)
            return mmEx.Result == NAudio.MmResult.NoDriver ||
                   mmEx.Result == NAudio.MmResult.BadDeviceId;
        }
        
        // Check exception message for common disconnection indicators
        string message = ex.Message.ToLowerInvariant();
        return message.Contains("device") && 
               (message.Contains("disconnect") || 
                message.Contains("not found") || 
                message.Contains("removed") ||
                message.Contains("unplugged"));
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
