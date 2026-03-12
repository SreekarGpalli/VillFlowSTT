// VillFlow.App/Views/SetupWizard.xaml.cs
// First-run setup wizard code-behind — walks user through mic, hotkey, STT, and polish config.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.Wave;
using VillFlow.App;
using VillFlow.App.Constants;
using VillFlow.App.Services;
using VillFlow.App.Utilities;
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



    public SetupWizard(SettingsService settingsService, App app)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _app = app;

        // Load current hotkey from settings
        var settings = _settingsService.Current;
        _hotkeyMods = settings.HotkeyModifiers;
        _hotkeyVk = settings.HotkeyKey;
        
        // Update UI with current hotkey
        WizHotkeyDisplay.Text = HotkeyHelper.FormatHotkey(_hotkeyMods, _hotkeyVk);

        PopulateDropdowns();
        UpdateStepUI();
    }

    private void OnGroqLinkClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://console.groq.com/keys") { UseShellExecute = true }); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[VillFlow] OnGroqLinkClick failed: {ex.Message}"); }
    }

    private void PopulateDropdowns()
    {
        // Microphones (specific to SetupWizard)
        WizMicCombo.Items.Clear();
        WizMicCombo.Items.Add("System Default");
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            WizMicCombo.Items.Add(caps.ProductName);
        }
        WizMicCombo.SelectedIndex = 0;

        // Use shared helper for provider dropdowns
        UiHelper.PopulateProviderDropdowns(WizSttProvider, WizPolishProvider);

        // Initialize polish controls state
        UpdatePolishControlsState();
    }

    private void UpdatePolishControlsState()
    {
        bool isEnabled = WizPolishToggle.IsChecked == true;
        foreach (var child in WizPolishGrid.Children)
        {
            if (child is Control control)
            {
                control.IsEnabled = isEnabled;
            }
        }
    }

    private void OnPolishToggleChanged(object sender, RoutedEventArgs e)
    {
        UpdatePolishControlsState();
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
        UpdateStepDots();
    }

    private void UpdateStepDots()
    {
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
        WizHotkeyDisplay.Text = HotkeyHelper.FormatHotkey(_hotkeyMods, _hotkeyVk);
    }

    private void OnWizSttProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WizSttProvider.SelectedItem is string provStr &&
            Enum.TryParse<SttProvider>(provStr, out var prov))
        {
            WizSttUrl.Text = ProviderConstants.SttDefaultUrls.GetValueOrDefault(prov, "");

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
            WizPolishUrl.Text = ProviderConstants.PolishDefaultUrls.GetValueOrDefault(prov, "");
        }
    }

    private async void OnWizFetchSttModels(object sender, RoutedEventArgs e)
    {
        await ModelFetchUiService.FetchSttModelsAndPopulateDropdownAsync(
            WizSttProvider,
            WizSttKey,
            WizSttUrl,
            WizSttModel,
            null, // No button to disable in wizard
            WizSttNote);
    }

    private async void OnWizFetchPolishModels(object sender, RoutedEventArgs e)
    {
        await ModelFetchUiService.FetchPolishModelsAndPopulateDropdownAsync(
            WizPolishProvider,
            WizPolishKey,
            WizPolishUrl,
            WizPolishModel);
    }

    // ── Validation ──────────────────────────────────────────────────────────

    private bool ValidateSttStep()
    {
        bool isValid = true;
        ClearValidationErrors();

        if (string.IsNullOrWhiteSpace(WizSttKey.Text))
        {
            WizSttKey.BorderBrush = Brushes.Red;
            isValid = false;
        }
        if (string.IsNullOrWhiteSpace(WizSttUrl.Text))
        {
            WizSttUrl.BorderBrush = Brushes.Red;
            isValid = false;
        }
        if (WizSttModel.SelectedItem == null)
        {
            WizSttModel.BorderBrush = Brushes.Red;
            isValid = false;
        }

        if (!isValid)
        {
            MessageBox.Show("Please configure at least the primary STT provider with API key and model.",
                "Incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return isValid;
    }

    private void ClearValidationErrors()
    {
        WizSttKey.BorderBrush = null;
        WizSttUrl.BorderBrush = null;
        WizSttModel.BorderBrush = null;
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
        else
        {
            // Clear polish config when polish is disabled
            s.Polish = null;
        }

        s.SetupComplete = true;
        _settingsService.Save(s);
        _app.ReregisterHotkey();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
}
