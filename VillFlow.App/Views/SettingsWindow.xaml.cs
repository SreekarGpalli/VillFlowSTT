// VillFlow.App/Views/SettingsWindow.xaml.cs
// Code-behind for the Settings window — loads/saves AppSettings, fetches models.
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
using VillFlow.App.Constants;
using VillFlow.App.Services;
using VillFlow.App.Utilities;
using VillFlow.Core.Services;
using VillFlow.Core.Settings;

namespace VillFlow.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly App _app;
    private bool _isCapturingHotkey;

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
        // Use shared helper for provider dropdowns
        UiHelper.PopulateProviderDropdowns(SttPrimaryProvider, PolishProviderCombo);
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
        // Clamp in case saved device was unplugged (e.g. USB mic) — fall back to System Default
        int requestedIndex = s.SelectedMicDeviceNumber + 1; // -1 maps to 0 (system default)
        MicCombo.SelectedIndex = Math.Clamp(requestedIndex, 0, Math.Max(0, MicCombo.Items.Count - 1));

        // ── Hotkey ──────────────────────────────────────────────────────
        HotkeyDisplay.Text = HotkeyHelper.FormatHotkey(s.HotkeyModifiers, s.HotkeyKey);



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

    private void OnPolishToggleChanged(object sender, RoutedEventArgs e)
    {
        bool isEnabled = PolishToggle.IsChecked == true;
        foreach (var child in PolishPanel.Children)
        {
            if (child is Control control)
            {
                control.IsEnabled = isEnabled;
            }
        }
    }

    private void OnSttProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is string providerStr)
        {
            if (Enum.TryParse<SttProvider>(providerStr, out var provider) && ProviderConstants.SttDefaultUrls.ContainsKey(provider))
            {
                SttPrimaryUrl.Text = ProviderConstants.SttDefaultUrls[provider];
            }
        }
    }

    private void OnPolishProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PolishProviderCombo.SelectedItem is string providerStr)
        {
            if (Enum.TryParse<PolishProvider>(providerStr, out var provider) && ProviderConstants.PolishDefaultUrls.ContainsKey(provider))
            {
                PolishUrlBox.Text = ProviderConstants.PolishDefaultUrls[provider];
            }
        }
    }

    private async void OnFetchSttModels(object sender, RoutedEventArgs e)
    {
        await ModelFetchUiService.FetchSttModelsAndPopulateDropdownAsync(
            SttPrimaryProvider,
            SttPrimaryKey,
            SttPrimaryUrl,
            SttPrimaryModel,
            sender as Button);
    }

    private async void OnFetchPolishModels(object sender, RoutedEventArgs e)
    {
        await ModelFetchUiService.FetchPolishModelsAndPopulateDropdownAsync(
            PolishProviderCombo,
            PolishKeyBox,
            PolishUrlBox,
            PolishModelCombo,
            sender as Button);
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

        HotkeyDisplay.Text = HotkeyHelper.FormatHotkey(mods, vk);
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

    private void OnRemoveFallbackSlot(object sender, RoutedEventArgs e)
    {
        if (sender is Button removeButton && removeButton.Tag is int slotIndex)
        {
            // Find the panel to remove
            var panelToRemove = FallbacksPanel.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => panel.Tag is int tag && tag == slotIndex);
            
            if (panelToRemove != null)
            {
                FallbacksPanel.Children.Remove(panelToRemove);
                
                // Re-index the remaining slots
                ReindexFallbackSlots();
            }
        }
    }

    private void ReindexFallbackSlots()
    {
        var newIndex = 1;
        foreach (var child in FallbacksPanel.Children)
        {
            if (child is not StackPanel panel) continue;

            panel.Tag = newIndex;

            // Update header text
            if (panel.Children[0] is StackPanel headerPanel && headerPanel.Children.Count > 0)
            {
                if (headerPanel.Children[0] is TextBlock headerTextBlock)
                    headerTextBlock.Text = $"Fallback {newIndex}";
                if (headerPanel.Children.Count > 1 && headerPanel.Children[1] is Button removeButton)
                    removeButton.Tag = newIndex;
            }

            // Update child control Tags so OnSave finds them correctly (fb_provider_{n}, fb_key_{n}, etc.)
            foreach (var ctrl in panel.Children.OfType<System.Windows.FrameworkElement>())
            {
                var tag = ctrl.Tag?.ToString() ?? "";
                if (tag.StartsWith("fb_provider_")) ctrl.Tag = $"fb_provider_{newIndex}";
                else if (tag.StartsWith("fb_key_")) ctrl.Tag = $"fb_key_{newIndex}";
                else if (tag.StartsWith("fb_url_")) ctrl.Tag = $"fb_url_{newIndex}";
                else if (tag.StartsWith("fb_model_")) ctrl.Tag = $"fb_model_{newIndex}";
            }

            newIndex++;
        }
    }

    private void AddFallbackSlotUI(SttSlotConfig? slot, int index)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8), Tag = index };

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var header = new TextBlock
        {
            Text = $"Fallback {index}",
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush,
            Margin = new Thickness(0, 0, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        };

        var removeButton = new Button
        {
            Content = "Remove",
            Tag = index,
            Margin = new Thickness(12, 0, 0, 4),
            Padding = new Thickness(8, 2, 8, 2),
            Background = FindResource("AccentRedBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DarkRed,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        removeButton.Click += OnRemoveFallbackSlot;

        headerPanel.Children.Add(header);
        headerPanel.Children.Add(removeButton);

        var providerCombo = new ComboBox { Tag = $"fb_provider_{index}", Margin = new Thickness(0, 0, 0, 4) };
        UiHelper.PopulateSttProviderDropdown(providerCombo);
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

        panel.Children.Add(headerPanel);
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
            // Get the index from the panel's Tag
            var index = panel.Tag as int? ?? 1;
            
            // Find controls by their Tag property instead of by type and position
            var providerCombo = panel.Children.OfType<ComboBox>().FirstOrDefault(c => c.Tag?.ToString() == $"fb_provider_{index}");
            var keyBox = panel.Children.OfType<TextBox>().FirstOrDefault(c => c.Tag?.ToString() == $"fb_key_{index}");
            var urlBox = panel.Children.OfType<TextBox>().FirstOrDefault(c => c.Tag?.ToString() == $"fb_url_{index}");
            var modelCombo = panel.Children.OfType<ComboBox>().FirstOrDefault(c => c.Tag?.ToString() == $"fb_model_{index}");

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
}
