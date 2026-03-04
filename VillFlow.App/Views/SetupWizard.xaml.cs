// VillFlow.App/Views/SetupWizard.xaml.cs
// First-run setup wizard code-behind — walks user through mic, hotkey, STT, and polish config.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.Wave;
using VillFlow.Core.Services;
using VillFlow.Core.Settings;

namespace VillFlow.App.Views;

public partial class SetupWizard : Window
{
    private readonly SettingsService _settingsService;
    private readonly App _app;
    private int _currentStep = 1;
    private const int TotalSteps = 6;

    private bool _isCapturingHotkey;
    private int _hotkeyMods = 0x0002; // MOD_CONTROL
    private int _hotkeyVk = 0x20;     // VK_SPACE

    // Known URLs
    private static readonly Dictionary<SttProvider, string> SttUrls = new()
    {
        { SttProvider.Groq, "https://api.groq.com/openai/v1" },
        { SttProvider.Custom, "" },
    };
    private static readonly Dictionary<PolishProvider, string> PolishUrls = new()
    {
        { PolishProvider.Groq, "https://api.groq.com/openai/v1" },
        { PolishProvider.Custom, "" },
    };

    public SetupWizard(SettingsService settingsService, App app)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _app = app;

        PopulateDropdowns();
        UpdateStepUI();
    }

    private void OnGroqLinkClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://console.groq.com/keys") { UseShellExecute = true }); }
        catch { }
    }

    private void PopulateDropdowns()
    {
        // Microphones
        WizMicCombo.Items.Clear();
        WizMicCombo.Items.Add("System Default");
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            WizMicCombo.Items.Add(caps.ProductName);
        }
        WizMicCombo.SelectedIndex = 0;

        // STT providers
        WizSttProvider.Items.Clear();
        foreach (var p in Enum.GetValues<SttProvider>())
            WizSttProvider.Items.Add(p.ToString());

        // Polish providers
        WizPolishProvider.Items.Clear();
        foreach (var p in Enum.GetValues<PolishProvider>())
            WizPolishProvider.Items.Add(p.ToString());
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    private void OnNext(object sender, RoutedEventArgs e)
    {
        // Validate current step
        if (_currentStep == 4 && !ValidateSttStep()) return;

        if (_currentStep < TotalSteps)
        {
            _currentStep++;
            UpdateStepUI();
        }
        else
        {
            // Final step — save and close
            SaveWizardSettings();
            Close();
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateStepUI();
        }
    }

    private void UpdateStepUI()
    {
        // Hide all steps
        Step1.Visibility = Visibility.Collapsed;
        Step2.Visibility = Visibility.Collapsed;
        Step3.Visibility = Visibility.Collapsed;
        Step4.Visibility = Visibility.Collapsed;
        Step5.Visibility = Visibility.Collapsed;
        Step6.Visibility = Visibility.Collapsed;

        // Show current step
        var currentPanel = _currentStep switch
        {
            1 => Step1,
            2 => Step2,
            3 => Step3,
            4 => Step4,
            5 => Step5,
            6 => Step6,
            _ => Step1,
        };
        currentPanel.Visibility = Visibility.Visible;

        // Update navigation buttons
        BackBtn.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content = _currentStep == TotalSteps ? "Finish" : "Next";

        // Update step dots
        var dots = new[] { Dot1, Dot2, Dot3, Dot4, Dot5, Dot6 };
        var activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4FF"));
        var inactiveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A4A7F"));

        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].Fill = (i + 1) <= _currentStep ? activeBrush : inactiveBrush;
        }
    }

    // ── Step Handlers ───────────────────────────────────────────────────────

    private void OnWizChangeHotkey(object sender, RoutedEventArgs e)
    {
        _isCapturingHotkey = true;
        WizHotkeyDisplay.Text = "Press hotkey...";
        WizHotkeyDisplay.Focus();
        WizHotkeyDisplay.PreviewKeyDown += CaptureWizHotkey;
    }

    private void CaptureWizHotkey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isCapturingHotkey) return;
        e.Handled = true;
        _isCapturingHotkey = false;
        WizHotkeyDisplay.PreviewKeyDown -= CaptureWizHotkey;

        _hotkeyMods = 0;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            _hotkeyMods |= 0x0002;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
            _hotkeyMods |= 0x0001;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
            _hotkeyMods |= 0x0004;

        _hotkeyVk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.Key);
        WizHotkeyDisplay.Text = FormatHotkey(_hotkeyMods, _hotkeyVk);
    }

    private void OnWizSttProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WizSttProvider.SelectedItem is string provStr &&
            Enum.TryParse<SttProvider>(provStr, out var prov))
        {
            WizSttUrl.Text = SttUrls.GetValueOrDefault(prov, "");

            WizSttNote.Text = prov == SttProvider.Custom
                ? "Enter your OpenAI-compatible endpoint. After fetching, pick an audio/STT model (e.g., whisper-1)."
                : "";
        }
    }

    private void OnWizPolishProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WizPolishProvider.SelectedItem is string provStr &&
            Enum.TryParse<PolishProvider>(provStr, out var prov))
        {
            WizPolishUrl.Text = PolishUrls.GetValueOrDefault(prov, "");
        }
    }

    private async void OnWizFetchSttModels(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Enum.TryParse<SttProvider>(WizSttProvider.SelectedItem?.ToString(), out var prov)) return;

            var models = await ModelFetchService.FetchSttModelsAsync(
                prov, WizSttKey.Text.Trim(), WizSttUrl.Text.Trim());

            WizSttModel.Items.Clear();
            foreach (var m in models)
                WizSttModel.Items.Add(m.Id);

            if (models.Count == 0)
                WizSttNote.Text = "No models found. Enter a model ID manually in Settings after setup.";

            if (WizSttModel.Items.Count > 0)
                WizSttModel.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to fetch models: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnWizFetchPolishModels(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Enum.TryParse<PolishProvider>(WizPolishProvider.SelectedItem?.ToString(), out var prov)) return;

            var models = await ModelFetchService.FetchPolishModelsAsync(
                prov, WizPolishKey.Text.Trim(), WizPolishUrl.Text.Trim());

            WizPolishModel.Items.Clear();
            foreach (var m in models)
                WizPolishModel.Items.Add(m.Id);

            if (WizPolishModel.Items.Count > 0)
                WizPolishModel.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to fetch models: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Validation ──────────────────────────────────────────────────────────

    private bool ValidateSttStep()
    {
        if (string.IsNullOrWhiteSpace(WizSttKey.Text) ||
            string.IsNullOrWhiteSpace(WizSttUrl.Text) ||
            WizSttModel.SelectedItem == null)
        {
            MessageBox.Show("Please configure at least the primary STT provider with API key and model.",
                "Incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    // ── Save ────────────────────────────────────────────────────────────────

    private void SaveWizardSettings()
    {
        var s = _settingsService.Current;

        // Mic
        s.SelectedMicDeviceNumber = WizMicCombo.SelectedIndex - 1;

        // Hotkey
        s.HotkeyModifiers = _hotkeyMods;
        s.HotkeyKey = _hotkeyVk;

        // STT Primary
        s.SttSlots.Clear();
        s.SttSlots.Add(new SttSlotConfig
        {
            Provider = Enum.TryParse<SttProvider>(WizSttProvider.SelectedItem?.ToString(), out var sp) ? sp : SttProvider.Custom,
            ApiKey = WizSttKey.Text.Trim(),
            BaseUrl = WizSttUrl.Text.Trim(),
            Model = WizSttModel.SelectedItem?.ToString() ?? "",
        });

        // Polish
        s.PolishEnabled = WizPolishToggle.IsChecked == true;
        if (s.PolishEnabled)
        {
            s.Polish = new PolishConfig
            {
                Provider = Enum.TryParse<PolishProvider>(WizPolishProvider.SelectedItem?.ToString(), out var pp) ? pp : PolishProvider.Custom,
                ApiKey = WizPolishKey.Text.Trim(),
                BaseUrl = WizPolishUrl.Text.Trim(),
                Model = WizPolishModel.SelectedItem?.ToString() ?? "",
                SystemPrompt = PolishConfig.DefaultSystemPrompt,
            };
        }

        s.SetupComplete = true;
        _settingsService.Save(s);
        _app.ReregisterHotkey();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string FormatHotkey(int modifiers, int vk)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");

        string keyName = vk switch
        {
            0x20 => "Space",
            _ => ((System.Windows.Input.Key)System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk)).ToString()
        };
        parts.Add(keyName);
        return string.Join(" + ", parts);
    }
}
