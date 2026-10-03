using System.Security.Cryptography;
using System.Text;

namespace SwiftBets.Casino.Domain;

/// <summary>
/// Pragmatic Play signs every request: lower-case hex MD5 of its parameters, sorted by name and joined as
/// <c>key=value&amp;key=value</c> without the <c>hash</c> itself, with the secret key appended. Comparison is constant-time.
/// </summary>
public static class PragmaticHash
{
    public const string Field = "hash";

    public static string Compute(IEnumerable<KeyValuePair<string, string>> parameters, string secret)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var joined = string.Join('&', parameters.Where(p => p.Key != Field).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
#pragma warning disable CA5351 // MD5 is the provider's protocol, not our choice; the secret makes it a keyed check.
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(joined + secret)));
#pragma warning restore CA5351
    }

    /// <summary>False when the secret or the hash is missing, or the hash does not match.</summary>
    public static bool Verify(IReadOnlyDictionary<string, string> parameters, string? secret)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrEmpty(secret) || !parameters.TryGetValue(Field, out var given) || given.Length != 32)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Compute(parameters, secret));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(given.ToLowerInvariant()));
    }
}
