// VillFlow.App/Views/SettingsWindow.xaml.cs
// Code-behind for the Settings window — loads/saves AppSettings, fetches models.
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
using VillFlow.Core.Services;
using VillFlow.Core.Settings;

namespace VillFlow.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly App _app;
    private bool _isCapturingHotkey;

    // Known STT provider URLs
    private static readonly Dictionary<SttProvider, string> SttDefaultUrls = new()
    {
        { SttProvider.Groq, "https://api.groq.com/openai/v1" },
        { SttProvider.Custom, "" },
    };

    // Known Polish provider URLs
    private static readonly Dictionary<PolishProvider, string> PolishDefaultUrls = new()
    {
        { PolishProvider.Groq, "https://api.groq.com/openai/v1" },
        { PolishProvider.Custom, "" },
    };

    public SettingsWindow(SettingsService settingsService, App app)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _app = app;

        PopulateProviderDropdowns();
        LoadSettings();
    }

    // ── Population ──────────────────────────────────────────────────────────

    private void PopulateProviderDropdowns()
    {
        // STT providers
        SttPrimaryProvider.Items.Clear();
        foreach (var p in Enum.GetValues<SttProvider>())
            SttPrimaryProvider.Items.Add(p.ToString());

        // Polish providers
        PolishProviderCombo.Items.Clear();
        foreach (var p in Enum.GetValues<PolishProvider>())
            PolishProviderCombo.Items.Add(p.ToString());
    }

    private void LoadSettings()
    {
        var s = _settingsService.Current;

        // ── Mic ─────────────────────────────────────────────────────────
        MicCombo.Items.Clear();
        MicCombo.Items.Add("System Default");
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            MicCombo.Items.Add(caps.ProductName);
        }
        MicCombo.SelectedIndex = s.SelectedMicDeviceNumber + 1; // -1 maps to 0 (system default)

        // ── Hotkey ──────────────────────────────────────────────────────
        HotkeyDisplay.Text = FormatHotkey(s.HotkeyModifiers, s.HotkeyKey);



        // ── STT Primary ─────────────────────────────────────────────────
        if (s.SttSlots.Count > 0)
        {
            var slot = s.SttSlots[0];
            SttPrimaryProvider.SelectedItem = slot.Provider.ToString();
            SttPrimaryKey.Text = slot.ApiKey;
            SttPrimaryUrl.Text = slot.BaseUrl;
            if (!string.IsNullOrEmpty(slot.Model))
            {
                SttPrimaryModel.Items.Add(slot.Model);
                SttPrimaryModel.SelectedItem = slot.Model;
            }
        }

        // ── STT Fallbacks ───────────────────────────────────────────────
        for (int i = 1; i < s.SttSlots.Count; i++)
        {
            AddFallbackSlotUI(s.SttSlots[i], i);
        }

        // ── Polish ──────────────────────────────────────────────────────
        PolishToggle.IsChecked = s.PolishEnabled;
        if (s.Polish != null)
        {
            PolishProviderCombo.SelectedItem = s.Polish.Provider.ToString();
            PolishKeyBox.Text = s.Polish.ApiKey;
            PolishUrlBox.Text = s.Polish.BaseUrl;
            SystemPromptBox.Text = s.Polish.SystemPrompt;
            if (!string.IsNullOrEmpty(s.Polish.Model))
            {
                PolishModelCombo.Items.Add(s.Polish.Model);
                PolishModelCombo.SelectedItem = s.Polish.Model;
            }
        }
        else
        {
            SystemPromptBox.Text = PolishConfig.DefaultSystemPrompt;
        }
    }

    // ── Event Handlers ──────────────────────────────────────────────────────

    private void OnSttProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is string providerStr)
        {
            if (Enum.TryParse<SttProvider>(providerStr, out var provider) && SttDefaultUrls.ContainsKey(provider))
            {
                SttPrimaryUrl.Text = SttDefaultUrls[provider];
            }
        }
    }

    private void OnPolishProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PolishProviderCombo.SelectedItem is string providerStr)
        {
            if (Enum.TryParse<PolishProvider>(providerStr, out var provider) && PolishDefaultUrls.ContainsKey(provider))
            {
                PolishUrlBox.Text = PolishDefaultUrls[provider];
            }
        }
    }

    private async void OnFetchSttModels(object sender, RoutedEventArgs e)
    {
        try
        {
            var providerStr = SttPrimaryProvider.SelectedItem?.ToString() ?? "";
            if (!Enum.TryParse<SttProvider>(providerStr, out var provider)) return;

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;

            var models = await ModelFetchService.FetchSttModelsAsync(
                provider, SttPrimaryKey.Text.Trim(), SttPrimaryUrl.Text.Trim());

            SttPrimaryModel.Items.Clear();
            foreach (var m in models)
            {
                SttPrimaryModel.Items.Add(m.Id);
            }

            if (models.Count == 0)
            {
                SttPrimaryModel.Items.Add("(No models found — enter model ID manually)");
            }

            if (SttPrimaryModel.Items.Count > 0)
                SttPrimaryModel.SelectedIndex = 0;

            if (btn != null) btn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to fetch models: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnFetchPolishModels(object sender, RoutedEventArgs e)
    {
        try
        {
            var providerStr = PolishProviderCombo.SelectedItem?.ToString() ?? "";
            if (!Enum.TryParse<PolishProvider>(providerStr, out var provider)) return;

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;

            var models = await ModelFetchService.FetchPolishModelsAsync(
                provider, PolishKeyBox.Text.Trim(), PolishUrlBox.Text.Trim());

            PolishModelCombo.Items.Clear();
            foreach (var m in models)
            {
                PolishModelCombo.Items.Add(m.Id);
            }

            if (models.Count == 0)
            {
                PolishModelCombo.Items.Add("(No models found — enter model ID manually)");
            }

            if (PolishModelCombo.Items.Count > 0)
                PolishModelCombo.SelectedIndex = 0;

            if (btn != null) btn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to fetch models: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnChangeHotkey(object sender, RoutedEventArgs e)
    {
        _isCapturingHotkey = true;
        HotkeyDisplay.Text = "Press new hotkey...";
        HotkeyDisplay.Focus();

        HotkeyDisplay.PreviewKeyDown += CaptureHotkey;
    }

    private void CaptureHotkey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isCapturingHotkey) return;

        e.Handled = true;
        _isCapturingHotkey = false;
        HotkeyDisplay.PreviewKeyDown -= CaptureHotkey;

        // Get modifiers
        int mods = 0;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            mods |= 0x0002;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
            mods |= 0x0001;
        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
            mods |= 0x0004;

        int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.Key);

        HotkeyDisplay.Text = FormatHotkey(mods, vk);
        HotkeyDisplay.Tag = (mods, vk); // Store for saving
    }

    private void OnAddFallback(object sender, RoutedEventArgs e)
    {
        var fallbackCount = FallbacksPanel.Children.Count;
        if (fallbackCount >= 2)
        {
            MessageBox.Show("Maximum 2 fallbacks allowed.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AddFallbackSlotUI(null, fallbackCount + 1);
    }

    private void AddFallbackSlotUI(SttSlotConfig? slot, int index)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8), Tag = index };

        var header = new TextBlock
        {
            Text = $"Fallback {index}",
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var providerCombo = new ComboBox { Tag = $"fb_provider_{index}", Margin = new Thickness(0, 0, 0, 4) };
        foreach (var p in Enum.GetValues<SttProvider>()) providerCombo.Items.Add(p.ToString());
        if (slot != null) providerCombo.SelectedItem = slot.Provider.ToString();

        var keyBox = new TextBox { Tag = $"fb_key_{index}", Margin = new Thickness(0, 0, 0, 4) };
        if (slot != null) keyBox.Text = slot.ApiKey;

        var urlBox = new TextBox { Tag = $"fb_url_{index}", Margin = new Thickness(0, 0, 0, 4) };
        if (slot != null) urlBox.Text = slot.BaseUrl;

        var modelCombo = new ComboBox { Tag = $"fb_model_{index}", Margin = new Thickness(0, 0, 0, 4) };
        if (slot != null && !string.IsNullOrEmpty(slot.Model))
        {
            modelCombo.Items.Add(slot.Model);
            modelCombo.SelectedItem = slot.Model;
        }

        panel.Children.Add(header);
        panel.Children.Add(providerCombo);
        panel.Children.Add(keyBox);
        panel.Children.Add(urlBox);
        panel.Children.Add(modelCombo);

        FallbacksPanel.Children.Add(panel);
    }

    // ── Save ────────────────────────────────────────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var s = _settingsService.Current;

        // Mic
        s.SelectedMicDeviceNumber = MicCombo.SelectedIndex - 1; // 0 → -1 (system default)

        // Hotkey
        if (HotkeyDisplay.Tag is (int mods, int vk))
        {
            s.HotkeyModifiers = mods;
            s.HotkeyKey = vk;
        }



        // STT Primary
        s.SttSlots.Clear();
        s.SttSlots.Add(new SttSlotConfig
        {
            Provider = Enum.TryParse<SttProvider>(SttPrimaryProvider.SelectedItem?.ToString(), out var sp) ? sp : SttProvider.Custom,
            ApiKey = SttPrimaryKey.Text.Trim(),
            BaseUrl = SttPrimaryUrl.Text.Trim(),
            Model = SttPrimaryModel.SelectedItem?.ToString() ?? "",
        });

        // STT Fallbacks
        foreach (StackPanel panel in FallbacksPanel.Children)
        {
            var providerCombo = panel.Children.OfType<ComboBox>().FirstOrDefault();
            var keyBox = panel.Children.OfType<TextBox>().FirstOrDefault();
            var urlBox = panel.Children.OfType<TextBox>().Skip(1).FirstOrDefault();
            var modelCombo = panel.Children.OfType<ComboBox>().Skip(1).FirstOrDefault();

            if (providerCombo != null)
            {
                s.SttSlots.Add(new SttSlotConfig
                {
                    Provider = Enum.TryParse<SttProvider>(providerCombo.SelectedItem?.ToString(), out var fp) ? fp : SttProvider.Custom,
                    ApiKey = keyBox?.Text.Trim() ?? "",
                    BaseUrl = urlBox?.Text.Trim() ?? "",
                    Model = modelCombo?.SelectedItem?.ToString() ?? "",
                });
            }
        }

        // Polish
        s.PolishEnabled = PolishToggle.IsChecked == true;
        s.Polish = new PolishConfig
        {
            Provider = Enum.TryParse<PolishProvider>(PolishProviderCombo.SelectedItem?.ToString(), out var pp) ? pp : PolishProvider.Custom,
            ApiKey = PolishKeyBox.Text.Trim(),
            BaseUrl = PolishUrlBox.Text.Trim(),
            Model = PolishModelCombo.SelectedItem?.ToString() ?? "",
            SystemPrompt = SystemPromptBox.Text,
        };

        s.SetupComplete = s.SttSlots.Any(x => x.IsConfigured);

        _settingsService.Save(s);
        _app.ReregisterHotkey();

        MessageBox.Show("Settings saved.", "VillFlow", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
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
