// VillFlow.App/Constants/ProviderConstants.cs
// Shared constants for provider URLs to eliminate code duplication.
using VillFlow.Core.Settings;

namespace VillFlow.App.Constants;

/// <summary>
/// Shared constants for provider URLs used across the application.
/// Eliminates duplicate URL dictionary definitions in SettingsWindow and SetupWizard.
/// </summary>
public static class ProviderConstants
{
    /// <summary>
    /// Default URLs for STT providers.
    /// Used to auto-fill URL fields when provider is selected.
    /// </summary>
    public static readonly Dictionary<SttProvider, string> SttDefaultUrls = new()
    {
        { SttProvider.Groq, "https://api.groq.com/openai/v1" },
        { SttProvider.Custom, "" },
    };

    /// <summary>
    /// Default URLs for Polish providers.
    /// Used to auto-fill URL fields when provider is selected.
    /// </summary>
    public static readonly Dictionary<PolishProvider, string> PolishDefaultUrls = new()
    {
        { PolishProvider.Groq, "https://api.groq.com/openai/v1" },
        { PolishProvider.Custom, "" },
    };
}