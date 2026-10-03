using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SwiftBets.Casino.Application;

namespace SwiftBets.Casino.Infrastructure.Providers;

/// <summary>AES-GCM for provider credentials at rest: nonce, tag and ciphertext in one blob, bound to the provider id.</summary>
public sealed class CredentialCipher(IOptions<CasinoOptions> options)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public bool IsConfigured => Key() is not null;

    public byte[] Encrypt(string providerId, string plaintext)
    {
        var key = Key() ?? throw new InvalidOperationException("Casino:CredentialKey is not configured.");
        var data = Encoding.UTF8.GetBytes(plaintext);
        var blob = new byte[NonceSize + TagSize + data.Length];
        var nonce = blob.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(providerId));
        return blob;
    }

    /// <summary>Null when there is nothing stored, no key, or the blob does not decrypt (wrong key, tampered).</summary>
    public string? Decrypt(string providerId, byte[]? blob)
    {
        if (blob is null || blob.Length < NonceSize + TagSize || Key() is not { } key)
        {
            return null;
        }

        var data = new byte[blob.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob.AsSpan(0, NonceSize), blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), data, Encoding.UTF8.GetBytes(providerId));
            return Encoding.UTF8.GetString(data);
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
    }

    private byte[]? Key()
    {
        var text = options.Value.CredentialKey;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var buffer = new byte[32];
        return Convert.TryFromBase64String(text, buffer, out var written) && written == 32 ? buffer : null;
    }
}
