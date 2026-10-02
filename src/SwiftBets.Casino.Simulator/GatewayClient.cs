using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Simulator;

public sealed record WalletReply(string Status, long? Balance, Guid? TransactionId);

/// <summary>
/// How a provider talks to the casino gateway: JSON bodies signed with the provider's secret (lower-case hex
/// HMAC-SHA256 of the exact bytes in <c>X-Provider-Signature</c>), one endpoint per wallet action.
/// </summary>
public sealed class GatewayClient(HttpClient http, IOptions<SimulatorOptions> options, Faults faults)
{
    public const string SignatureHeader = "X-Provider-Signature";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WalletReply> CallAsync(string providerId, string action, object body, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, Json);
        var reply = await SendAsync(providerId, action, bytes, cancellationToken);
        // Fault injection: a provider that retries a win it already sent, to prove the gateway credits it once.
        if (action == "win" && faults.TakeDuplicate(providerId))
        {
            reply = await SendAsync(providerId, action, bytes, cancellationToken);
        }

        return reply;
    }

    public HttpRequestMessage Sign(string providerId, string action, byte[] body)
    {
        var secret = options.Value.Providers.TryGetValue(providerId, out var provider) ? provider.Secret : string.Empty;
        var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.GatewayAddress.TrimEnd('/')}/providers/{Uri.EscapeDataString(providerId)}/wallet/{action}")
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new("application/json") } },
        };
        request.Headers.Add(SignatureHeader, ProviderSignature.Compute(secret, body));
        return request;
    }

    private async Task<WalletReply> SendAsync(string providerId, string action, byte[] body, CancellationToken cancellationToken)
    {
        using var request = Sign(providerId, action, body);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return await response.Content.ReadFromJsonAsync<WalletReply>(Json, cancellationToken) ?? new WalletReply("gateway_error", null, null);
        }
        catch (HttpRequestException)
        {
            return new WalletReply("gateway_unreachable", null, null);
        }
    }

    /// <summary>What both sides sign for a report request.</summary>
    public static string ReportRequestLine(string providerId, string date) => $"GET /reports/{providerId}/{date}";

    public static bool VerifyReport(string? secret, string providerId, string date, string? signature) =>
        ProviderSignature.Verify(secret, Encoding.UTF8.GetBytes(ReportRequestLine(providerId, date)), signature);
}
