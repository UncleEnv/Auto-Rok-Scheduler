using System;
using System.Security.Cryptography;
using System.Text;

namespace AutoRokScheduler.Services;

/// <summary>
/// DPAPI-based per-user encryption. Ciphertext produced here can only be
/// decrypted by the SAME Windows user on the SAME machine (CurrentUser scope).
/// </summary>
public static class CredentialProtector
{
    // Extra entropy mixed into the protection; ties the blob to this app.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutoRokScheduler.v1");

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    public static string Decrypt(string base64Cipher)
    {
        if (string.IsNullOrEmpty(base64Cipher)) return string.Empty;
        try
        {
            var cipher = Convert.FromBase64String(base64Cipher);
            var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Wrong user/machine, or corrupt/tampered blob.
            return string.Empty;
        }
    }
}
