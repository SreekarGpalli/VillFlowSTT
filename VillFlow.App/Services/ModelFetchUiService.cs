// VillFlow.App/Services/ModelFetchUiService.cs
// Shared UI service for model fetching operations, eliminating duplicate logic
// between SettingsWindow and SetupWizard.
using System.Windows;
using System.Windows.Controls;
using VillFlow.Core.Services;
using VillFlow.Core.Settings;

namespace VillFlow.App.Services;

/// <summary>
/// Provides shared UI logic for fetching models and updating dropdowns.
/// Eliminates duplicate code between SettingsWindow and SetupWizard.
/// </summary>
public static class ModelFetchUiService
{
    /// <summary>
    /// Fetches STT models and populates a dropdown with the results.
    /// Handles UI updates and error messages consistently.
    /// </summary>
    /// <param name="providerComboBox">ComboBox containing the selected STT provider</param>
    /// <param name="apiKeyTextBox">TextBox containing the API key</param>
    /// <param name="urlTextBox">TextBox containing the base URL</param>
    /// <param name="modelComboBox">ComboBox to populate with fetched models</param>
    /// <param name="fetchButton">Optional button to disable during fetch</param>
    /// <param name="statusTextBlock">Optional TextBlock to show status messages</param>
    public static async Task FetchSttModelsAndPopulateDropdownAsync(
        ComboBox providerComboBox,
        TextBox apiKeyTextBox,
        TextBox urlTextBox,
        ComboBox modelComboBox,
        Button? fetchButton = null,
        TextBlock? statusTextBlock = null)
    {
        try
        {
            var providerStr = providerComboBox.SelectedItem?.ToString() ?? "";
            if (!Enum.TryParse<SttProvider>(providerStr, out var provider))
            {
                ShowErrorMessage("Please select a valid STT provider.");
                return;
            }

            // Disable button during fetch
            if (fetchButton != null)
                fetchButton.IsEnabled = false;

            // Clear status
            if (statusTextBlock != null)
                statusTextBlock.Text = "";

            // Fetch models
            var models = await ModelFetchService.FetchSttModelsAsync(
                provider, apiKeyTextBox.Text.Trim(), urlTextBox.Text.Trim());

            // Update dropdown
            modelComboBox.Items.Clear();
            foreach (var model in models)
            {
                modelComboBox.Items.Add(model.Id);
            }

            // Handle empty results
            if (models.Count == 0)
            {
                modelComboBox.Items.Add("(No models found — enter model ID manually)");
                if (statusTextBlock != null)
                    statusTextBlock.Text = "No models found. Enter a model ID manually in Settings after setup.";
            }

            // Select first item if available
            if (modelComboBox.Items.Count > 0)
                modelComboBox.SelectedIndex = 0;

            // Re-enable button
            if (fetchButton != null)
                fetchButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Failed to fetch models: {ex.Message}");
            
            // Re-enable button on error
            if (fetchButton != null)
                fetchButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Fetches polish models and populates a dropdown with the results.
    /// Handles UI updates and error messages consistently.
    /// </summary>
    /// <param name="providerComboBox">ComboBox containing the selected polish provider</param>
    /// <param name="apiKeyTextBox">TextBox containing the API key</param>
    /// <param name="urlTextBox">TextBox containing the base URL</param>
    /// <param name="modelComboBox">ComboBox to populate with fetched models</param>
    /// <param name="fetchButton">Optional button to disable during fetch</param>
    public static async Task FetchPolishModelsAndPopulateDropdownAsync(
        ComboBox providerComboBox,
        TextBox apiKeyTextBox,
        TextBox urlTextBox,
        ComboBox modelComboBox,
        Button? fetchButton = null)
    {
        try
        {
            var providerStr = providerComboBox.SelectedItem?.ToString() ?? "";
            if (!Enum.TryParse<PolishProvider>(providerStr, out var provider))
            {
                ShowErrorMessage("Please select a valid polish provider.");
                return;
            }

            // Disable button during fetch
            if (fetchButton != null)
                fetchButton.IsEnabled = false;

            // Fetch models
            var models = await ModelFetchService.FetchPolishModelsAsync(
                provider, apiKeyTextBox.Text.Trim(), urlTextBox.Text.Trim());

            // Update dropdown
            modelComboBox.Items.Clear();
            foreach (var model in models)
            {
                modelComboBox.Items.Add(model.Id);
            }

            // Handle empty results
            if (models.Count == 0)
            {
                modelComboBox.Items.Add("(No models found — enter model ID manually)");
            }

            // Select first item if available
            if (modelComboBox.Items.Count > 0)
                modelComboBox.SelectedIndex = 0;

            // Re-enable button
            if (fetchButton != null)
                fetchButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Failed to fetch models: {ex.Message}");
            
            // Re-enable button on error
            if (fetchButton != null)
                fetchButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Shows a consistent error message dialog.
    /// </summary>
    private static void ShowErrorMessage(string message)
    {
        MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}