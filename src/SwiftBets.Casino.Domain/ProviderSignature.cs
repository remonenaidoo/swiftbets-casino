using System.Security.Cryptography;
using System.Text;

namespace SwiftBets.Casino.Domain;

/// <summary>
/// A provider signs every callback: lower-case hex HMAC-SHA256 of the exact request body with its shared secret,
/// sent in <c>X-Provider-Signature</c>. Comparison is constant-time so timing reveals nothing about the secret.
/// </summary>
public static class ProviderSignature
{
    public static string Compute(string secret, ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
    }

    public static bool Verify(string? secret, ReadOnlySpan<byte> body, string? signature)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature) || signature.Length != 64)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Compute(secret, body));
        var given = Encoding.ASCII.GetBytes(signature.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }
}
