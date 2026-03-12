// VillFlow.Core/Orchestration/PipelineOrchestrator.cs
// Master pipeline: cascading STT → optional polish → text injection.
using System.Diagnostics;
using VillFlow.Core.Services;
using VillFlow.Core.Settings;

namespace VillFlow.Core.Orchestration;

/// <summary>
/// Orchestrates the full voice-to-text pipeline:
/// 1. Cascading STT (primary → fallback 1 → fallback 2)
/// 2. Optional AI text polishing
/// 3. Text injection at cursor
/// 
/// Reports state transitions via <see cref="StateChanged"/> for the overlay UI.
/// </summary>
public sealed class PipelineOrchestrator
{
    private readonly SettingsService _settingsService;

    // Timing constants for pipeline state transitions
    private const int STATE_TRANSITION_DELAY_MS = 150;
    private const int CLEANUP_DELAY_MS = 300;

    /// <summary>Pipeline states for the overlay UI.</summary>
    public enum PipelineState { Idle, Listening, Processing, Typing }

    /// <summary>Raised when the pipeline state changes.</summary>
    public event Action<PipelineState>? StateChanged;

    /// <summary>Raised when an error occurs (for optional logging/debug display).</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>Optional logger for pipeline debug trace (wiring to App.Log).</summary>
    public Action<string>? LogInfo { get; set; }

    /// <summary>
    /// Delegate to invoke an action on the UI/Dispatcher thread.
    /// Must be set by the host (App.xaml.cs) so TypeText runs on a thread
    /// with a message pump (required by SendInput).
    /// </summary>
    public Action<Action>? DispatcherInvoke { get; set; }

    public PipelineOrchestrator(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>Signals the overlay that recording has started.</summary>
    public void NotifyListeningStarted() => SafeInvokeStateChanged(PipelineState.Listening);

    /// <summary>
    /// Safely invokes the StateChanged event, catching exceptions from individual subscribers
    /// to ensure all subscribers are notified even if one throws.
    /// </summary>
    private void SafeInvokeStateChanged(PipelineState state)
    {
        var handlers = StateChanged?.GetInvocationList();
        if (handlers == null) return;

        foreach (var handler in handlers)
        {
            try
            {
                ((Action<PipelineState>)handler)(state);
            }
            catch (Exception ex)
            {
                // Log the exception but continue to next subscriber
                LogInfo?.Invoke($"[Pipeline] StateChanged subscriber threw: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Runs the full pipeline: STT → Polish → Inject.
    /// Called when the user releases the hotkey and the speech buffer has data.
    /// </summary>
    public async Task ProcessAsync(byte[] wavBytes)
    {
        // Minimum ~0.3 seconds of audio (16kHz * 2 bytes * 0.3s = 9600 PCM bytes + 44 header)
        // Very short recordings produce hallucinated text from Whisper
        const int MinAudioBytes = 9644;
        if (wavBytes.Length < MinAudioBytes)
        {
            LogInfo?.Invoke($"[Pipeline] Audio too short ({wavBytes.Length} bytes), skipping");
            SafeInvokeStateChanged(PipelineState.Idle);
            return;
        }

        try
        {
            // ── Step 1: Transcription (cascading) ───────────────────────────
            SafeInvokeStateChanged(PipelineState.Processing);

            var settings = _settingsService.Current;
            string? transcript = null;

            foreach (var slot in settings.SttSlots)
            {
                if (slot == null || !slot.IsConfigured) continue;

                try
                {
                    using var sttService = SttProviderFactory.Create(slot);
                    if (sttService == null) continue;

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(slot.TimeoutSeconds));
                    transcript = await sttService.TranscribeAsync(wavBytes, cts.Token);

                    if (!string.IsNullOrWhiteSpace(transcript))
                    {
                        var preview = transcript.Length > 80 ? transcript[..80] + "..." : transcript;
                        LogInfo?.Invoke($"[Pipeline] STT success via {slot.Provider}: {preview}");
                        break; // Success — stop cascading
                    }
                }
                catch (Exception ex)
                {
                    LogInfo?.Invoke($"[Pipeline] STT failed via {slot.Provider}: {ex.Message}");
                    ErrorOccurred?.Invoke($"STT ({slot.Provider}): {ex.Message}");
                    // Continue to next fallback
                }
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                LogInfo?.Invoke("[Pipeline] All STT providers failed or returned empty text.");
                ErrorOccurred?.Invoke("All STT providers failed or returned empty text.");
                SafeInvokeStateChanged(PipelineState.Idle);
                return;
            }

            // ── Step 2: Text Polish (optional) ──────────────────────────────
            string finalText = transcript;

            if (settings.PolishEnabled && settings.Polish?.IsConfigured == true)
            {
                try
                {
                    using var polisher = new TextPolishService(settings.Polish);
                    using var cts = new CancellationTokenSource(
                        TimeSpan.FromSeconds(settings.Polish.TimeoutSeconds));
                    finalText = await polisher.PolishAsync(transcript, cts.Token);
                    LogInfo?.Invoke($"[Pipeline] Polished ({finalText.Length} chars)");
                }
                catch (Exception ex)
                {
                    LogInfo?.Invoke($"[Pipeline] Polish failed (using raw): {ex.Message}");
                    ErrorOccurred?.Invoke($"Polish: {ex.Message}");
                    // Fail-safe: use raw transcript
                    finalText = transcript;
                }
            }

            // ── Step 3: Text Injection ──────────────────────────────────────
            SafeInvokeStateChanged(PipelineState.Typing);

            // Small delay to let the overlay state update and OS settle focus
            await Task.Delay(STATE_TRANSITION_DELAY_MS);

            LogInfo?.Invoke($"[Pipeline] Injecting text ({finalText.Length} chars)");

            // CRITICAL: SendInput MUST run on a thread with a message pump.
            // Threadpool threads (Task.Run) don't have one, so SendInput silently fails.
            // Use the WPF Dispatcher thread instead.
            if (DispatcherInvoke != null)
            {
                DispatcherInvoke(() => TextInjectionService.TypeText(finalText));
            }
            else
            {
                // Fallback — try anyway (may fail on threadpool)
                TextInjectionService.TypeText(finalText);
            }

            LogInfo?.Invoke("[Pipeline] Text injection completed");

            // Brief pause for the "Typing" state to be visible on overlay
            await Task.Delay(CLEANUP_DELAY_MS);
        }
        catch (Exception ex)
        {
            LogInfo?.Invoke($"[Pipeline] Fatal error: {ex}");
            ErrorOccurred?.Invoke($"Pipeline error: {ex.Message}");
        }
        finally
        {
            SafeInvokeStateChanged(PipelineState.Idle);
        }
    }
}
