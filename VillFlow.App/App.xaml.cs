// VillFlow.App/App.xaml.cs
// Composition root: wires all services, manages system tray, launches setup wizard or main overlay.
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using VillFlow.Core.Orchestration;
using VillFlow.Core.Services;
using VillFlow.Core.Settings;
using VillFlow.App.Views;

namespace VillFlow.App;

public partial class App : Application
{
    // ── Services ────────────────────────────────────────────────────────────
    private SettingsService _settingsService = null!;
    private HotkeyService _hotkeyService = null!;
    private AudioCaptureService _audioCapture = null!;
    private SpeechBuffer _speechBuffer = null!;
    private PipelineOrchestrator _orchestrator = null!;
    private OverlayWindow _overlay = null!;
    private TaskbarIcon _trayIcon = null!;
    private volatile bool _pipelineRunning;

    // Log file for crash diagnostics
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VillFlow", "villflow.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Global exception handlers ────────────────────────────────────
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log($"FATAL UNHANDLED: {args.ExceptionObject}");
            MessageBox.Show($"VillFlow crashed:\n{args.ExceptionObject}",
                "VillFlow Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            Log($"DISPATCHER EXCEPTION: {args.Exception}");
            MessageBox.Show($"VillFlow error:\n{args.Exception.Message}\n\nCheck log at:\n{LogPath}",
                "VillFlow Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true; // Don't crash — keep running
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log($"TASK EXCEPTION: {args.Exception}");
            args.SetObserved(); // Don't crash
        };

        Log("=== VillFlow starting ===");

        try
        {
            // ── Initialize services ─────────────────────────────────────
            _settingsService = new SettingsService();
            _speechBuffer = new SpeechBuffer();
            _hotkeyService = new HotkeyService();
            _orchestrator = new PipelineOrchestrator(_settingsService)
            {
                LogInfo = Log,
                DispatcherInvoke = action => Dispatcher.Invoke(action)
            };

            // Wire TextInjection logger
            TextInjectionService.LogInfo = Log;

            // Wire AudioCapture logger
            AudioCaptureService.LogInfo = Log;

            // Wire CustomSTT logger
            CustomSttService.LogInfo = Log;

            // Wire TextPolish logger
            TextPolishService.LogInfo = Log;

            // Audio capture — no VAD, all audio goes directly to speech buffer
            _audioCapture = new AudioCaptureService(_speechBuffer);

            // ── Wire events ─────────────────────────────────────────────
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _hotkeyService.HotkeyReleased += OnHotkeyReleased;

            // ── Overlay window ──────────────────────────────────────────
            _overlay = new OverlayWindow();
            _overlay.Loaded += OnOverlayLoaded;
            _overlay.Show();
            _overlay.Hide();
            Log("Overlay created");

            // Wire overlay state updates
            _orchestrator.StateChanged += state =>
            {
                _overlay.Dispatcher.Invoke(() => _overlay.SetState(state));
            };

            // ── System Tray ─────────────────────────────────────────────
            SetupTrayIcon();
            Log("Tray icon created");

            // ── First-run check ─────────────────────────────────────────
            if (!_settingsService.Current.SetupComplete)
            {
                Log("First run — showing setup wizard");
                ShowSetupWizard();
            }
            else
            {
                Log($"Setup already complete. Hotkey: mod={_settingsService.Current.HotkeyModifiers} vk={_settingsService.Current.HotkeyKey}");
            }
        }
        catch (Exception ex)
        {
            Log($"STARTUP CRASH: {ex}");
            MessageBox.Show($"VillFlow failed to start:\n{ex.Message}\n\nCheck log:\n{LogPath}",
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Overlay Loaded — register hotkey now that HWND exists ────────────────
    private void OnOverlayLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var hwndSource = PresentationSource.FromVisual(_overlay) as HwndSource;
            if (hwndSource != null)
            {
                hwndSource.AddHook(WndProc);
                RegisterConfiguredHotkey(hwndSource.Handle);
                Log($"Hotkey registered on HWND {hwndSource.Handle}");
            }
            else
            {
                Log("ERROR: No HwndSource after Loaded!");
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR in OnOverlayLoaded: {ex}");
        }
    }



    // ── Hotkey Registration ─────────────────────────────────────────────────
    private void RegisterConfiguredHotkey(IntPtr hwnd)
    {
        var settings = _settingsService.Current;
        bool success = _hotkeyService.Register(hwnd, (uint)settings.HotkeyModifiers, (uint)settings.HotkeyKey);
        Log($"RegisterHotKey(mod=0x{settings.HotkeyModifiers:X}, vk=0x{settings.HotkeyKey:X}) = {success}");
        if (!success)
        {
            Log($"  Win32 error: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        }
    }

    /// <summary>Re-registers the hotkey after settings change.</summary>
    public void ReregisterHotkey()
    {
        var hwndSource = PresentationSource.FromVisual(_overlay) as HwndSource;
        if (hwndSource != null)
        {
            RegisterConfiguredHotkey(hwndSource.Handle);
        }
    }

    // ── WndProc Hook ────────────────────────────────────────────────────────
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyService.WM_HOTKEY)
        {
            Log("WM_HOTKEY received!");
            _hotkeyService.HandleHotkeyMessage();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ── Hotkey Event Handlers ───────────────────────────────────────────────
    private void OnHotkeyPressed()
    {
        _overlay.Dispatcher.Invoke(() =>
        {
            try
            {
                var settings = _settingsService.Current;
                if (!settings.SetupComplete || _pipelineRunning) return;

                Log("HotkeyPressed — starting recording");
                _speechBuffer.Reset();
                _orchestrator.NotifyListeningStarted();
                _audioCapture.Start(settings.SelectedMicDeviceNumber);
            }
            catch (Exception ex)
            {
                Log($"ERROR in OnHotkeyPressed: {ex}");
            }
        });
    }

    private void OnHotkeyReleased()
    {
        _overlay.Dispatcher.Invoke(() =>
        {
            try
            {
                Log("HotkeyReleased — stopping recording");
                _audioCapture.Stop();

                if (!_speechBuffer.HasData)
                {
                    Log("No speech data captured");
                    _overlay.SetState(PipelineOrchestrator.PipelineState.Idle);
                    return;
                }

                var wavBytes = _speechBuffer.ToWavBytes();
                Log($"Speech buffer: {wavBytes.Length} bytes, starting pipeline");
                _pipelineRunning = true;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _orchestrator.ProcessAsync(wavBytes);
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR in pipeline: {ex}");
                    }
                    finally
                    {
                        _pipelineRunning = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"ERROR in OnHotkeyReleased: {ex}");
            }
        });
    }

    // ── System Tray ─────────────────────────────────────────────────────────
    private void SetupTrayIcon()
    {
        try
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "VillFlow — Voice Dictation",
                Icon = CreateTrayIcon(),
            };

            var contextMenu = new System.Windows.Controls.ContextMenu();

            var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
            settingsItem.Click += (_, _) => ShowSettings();
            contextMenu.Items.Add(settingsItem);

            var setupItem = new System.Windows.Controls.MenuItem { Header = "Run Setup Wizard" };
            setupItem.Click += (_, _) => ShowSetupWizard();
            contextMenu.Items.Add(setupItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
            quitItem.Click += (_, _) =>
            {
                _trayIcon.Dispose();
                Shutdown();
            };
            contextMenu.Items.Add(quitItem);

            _trayIcon.ContextMenu = contextMenu;
        }
        catch (Exception ex)
        {
            Log($"ERROR creating tray icon: {ex}");
        }
    }

    /// <summary>Creates a simple 16x16 tray icon programmatically.</summary>
    private static System.Drawing.Icon CreateTrayIcon()
    {
        try
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(30, 30, 60));
            using var brush = new SolidBrush(Color.FromArgb(0, 212, 255));
            g.FillEllipse(brush, 3, 2, 10, 10);
            g.FillRectangle(brush, 6, 11, 4, 3);
            var hIcon = bmp.GetHicon();
            var icon = System.Drawing.Icon.FromHandle(hIcon);
            // Clone because FromHandle doesn't take ownership of the handle
            var cloned = (System.Drawing.Icon)icon.Clone();
            DestroyIcon(hIcon);
            return cloned;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // ── Window Launchers ────────────────────────────────────────────────────
    private void ShowSettings()
    {
        var settings = new SettingsWindow(_settingsService, this);
        settings.ShowDialog();
    }

    private void ShowSetupWizard()
    {
        var wizard = new SetupWizard(_settingsService, this);
        wizard.ShowDialog();
    }

    // ── Cleanup ─────────────────────────────────────────────────────────────
    protected override void OnExit(ExitEventArgs e)
    {
        Log("VillFlow shutting down");
        _hotkeyService?.Dispose();
        _audioCapture?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    // ── Logging ─────────────────────────────────────────────────────────────
    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
            File.AppendAllText(LogPath, line);
            Debug.WriteLine($"[VillFlow] {message}");
        }
        catch { /* Logging should never crash the app */ }
    }
}
