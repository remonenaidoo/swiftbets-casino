using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Simulator;

/// <summary>One Pragmatic-format callback as sent and as answered, for drills to inspect.</summary>
public sealed record PragmaticExchange(string Method, int Http, int Error, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body.Length > 0 ? Body : "{}").RootElement;
}

/// <summary>
/// Talks to the casino gateway exactly as Pragmatic Play does: form-encoded POSTs per method, an MD5 <c>hash</c> over
/// the sorted fields with the secret appended, JSON replies with an <c>error</c> code. Like the real provider it
/// retries the same request (same reference) when the reply asks it to (100) or never arrives.
/// </summary>
public sealed class PragmaticClient(HttpClient http, IOptions<SimulatorOptions> options)
{
    public const string ProviderId = "pragmatic";
    private const int Attempts = 3;

    public async Task<PragmaticExchange> SendAsync(string method, Dictionary<string, string> fields, CancellationToken cancellationToken, bool badHash = false)
    {
        var secret = options.Value.Providers.TryGetValue(ProviderId, out var provider) ? provider.Secret : string.Empty;
        fields["providerId"] = "PragmaticPlay";
        fields["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        fields[PragmaticHash.Field] = badHash ? new string('0', 32) : PragmaticHash.Compute(fields, secret.Length > 0 ? secret : "unset");
        var exchange = new PragmaticExchange(method, 0, -1, string.Empty);
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            exchange = await PostAsync(method, fields, cancellationToken);
            if (exchange.Http == 200 && exchange.Error != 100)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
        }

        return exchange;
    }

    private async Task<PragmaticExchange> PostAsync(string method, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        try
        {
            using var response = await http.PostAsync(new Uri($"{options.Value.GatewayAddress.TrimEnd('/')}/providers/{ProviderId}/pragmatic/{method}.html"), content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = -1;
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    error = JsonDocument.Parse(body).RootElement.TryGetProperty("error", out var e) && e.TryGetInt32(out var code) ? code : -1;
                }
                catch (JsonException)
                {
                    error = -1;
                }
            }

            return new PragmaticExchange(method, (int)response.StatusCode, error, body);
        }
        catch (HttpRequestException)
        {
            return new PragmaticExchange(method, 0, -1, string.Empty);
        }
    }

    public static string Amount(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
