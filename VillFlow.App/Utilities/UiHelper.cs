// VillFlow.App/Utilities/UiHelper.cs
// Shared UI helper methods to eliminate code duplication.
using System.Windows.Controls;
using VillFlow.Core.Settings;

namespace VillFlow.App.Utilities;

/// <summary>
/// Shared UI helper methods to eliminate code duplication across windows.
/// </summary>
public static class UiHelper
{
    /// <summary>
    /// Populates STT and Polish provider dropdowns with enum values.
    /// Eliminates duplicate dropdown population logic in SettingsWindow and SetupWizard.
    /// </summary>
    /// <param name="sttCombo">ComboBox for STT providers</param>
    /// <param name="polishCombo">ComboBox for Polish providers</param>
    public static void PopulateProviderDropdowns(ComboBox sttCombo, ComboBox polishCombo)
    {
        // Clear existing items
        sttCombo.Items.Clear();
        polishCombo.Items.Clear();

        // Populate STT providers
        foreach (var provider in Enum.GetValues<SttProvider>())
        {
            sttCombo.Items.Add(provider.ToString());
        }

        // Populate Polish providers
        foreach (var provider in Enum.GetValues<PolishProvider>())
        {
            polishCombo.Items.Add(provider.ToString());
        }
    }

    /// <summary>
    /// Populates a single STT provider dropdown with enum values.
    /// Used for dynamically created fallback slot provider dropdowns.
    /// </summary>
    /// <param name="sttCombo">ComboBox for STT providers</param>
    public static void PopulateSttProviderDropdown(ComboBox sttCombo)
    {
        sttCombo.Items.Clear();
        foreach (var provider in Enum.GetValues<SttProvider>())
        {
            sttCombo.Items.Add(provider.ToString());
        }
    }
}