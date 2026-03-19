using System.Security.Cryptography;
using System.Text;

namespace IpspoolAutomation.Services;

/// <summary>
/// RSA encryption for password fields per api.md (GET /api/public-key).
/// </summary>
public static class RsaHelper
{
    /// <summary>
    /// Encrypt plain text with PEM-encoded RSA public key. Returns base64-encoded cipher text.
    /// </summary>
    public static string Encrypt(string plainText, string pemPublicKey)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentNullException(nameof(plainText));
        if (string.IsNullOrEmpty(pemPublicKey))
            throw new ArgumentNullException(nameof(pemPublicKey));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pemPublicKey.Trim());
        var data = Encoding.UTF8.GetBytes(plainText);
        // Server (node-rsa) uses OAEP with SHA-1 by default; PKCS#1 v1.5 would cause "Decryption failed".
        var encrypted = rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA1);
        return Convert.ToBase64String(encrypted);
    }
}
