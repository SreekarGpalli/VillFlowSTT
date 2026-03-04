// VillFlow.Core/Services/SttProviderFactory.cs
// Builds ISttService instances from user-configured SttSlotConfig.
using VillFlow.Core.Settings;

namespace VillFlow.Core.Services;

/// <summary>
/// Factory that creates the correct <see cref="ISttService"/> implementation
/// based on the user's <see cref="SttSlotConfig"/>.
/// </summary>
public static class SttProviderFactory
{
    /// <summary>
    /// Creates an ISttService from a slot configuration. Returns null if slot is unconfigured.
    /// </summary>
    public static ISttService? Create(SttSlotConfig? slot)
    {
        if (slot == null || !slot.IsConfigured) return null;

        return slot.Provider switch
        {
            SttProvider.Groq => new GroqSttService(slot.ApiKey, slot.BaseUrl, slot.Model, slot.TimeoutSeconds),
            SttProvider.Custom => new CustomSttService(slot.ApiKey, slot.BaseUrl, slot.Model, slot.TimeoutSeconds),
            _ => null,
        };
    }
}
