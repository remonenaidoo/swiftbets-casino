using System.Security.Cryptography;
using System.Text;

namespace SwiftBets.Casino.Domain;

/// <summary>A game session token: 32 random bytes, base64url. Only its SHA-256 is ever stored.</summary>
public static class SessionToken
{
    public static (string Token, byte[] Hash) New()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
