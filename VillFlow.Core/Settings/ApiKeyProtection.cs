// VillFlow.Core/Settings/ApiKeyProtection.cs
// DPAPI-based protection for API keys in settings.
using System.Security.Cryptography;
using System.Text;

namespace VillFlow.Core.Settings;

/// <summary>
/// Encrypts and decrypts API keys using Windows DPAPI (Data Protection API).
/// Keys are protected per-user; migration from plain text is automatic.
/// </summary>
internal static class ApiKeyProtection
{
    /// <summary>Encrypts a plain API key for storage. Returns base64-encoded ciphertext.</summary>
    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch { return plain; }
    }

    /// <summary>Decrypts a stored value. If decryption fails (legacy plain text), returns as-is.</summary>
    public static string Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        try
        {
            var encrypted = Convert.FromBase64String(stored);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch { return stored; }
    }
}
