using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Infrastructure.Reconciliation;

/// <summary>
/// Fetches a provider's daily report. The request is signed like callbacks, but over the request line
/// (<c>GET /reports/{provider}/{date}</c>), so the provider knows it is us.
/// </summary>
public sealed class HttpProviderReports(IHttpClientFactory http, IOptions<CasinoOptions> options) : IProviderReports
{
    public const string ClientName = "provider-reports";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public async Task<IReadOnlyList<ReportedTransaction>?> GetAsync(string providerId, DateOnly businessDate, CancellationToken cancellationToken)
    {
        if (!options.Value.Providers.TryGetValue(providerId, out var provider) || provider.ReportUrl.Length == 0 || provider.Secret.Length == 0)
        {
            return null;
        }

        var date = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{provider.ReportUrl.TrimEnd('/')}/{Uri.EscapeDataString(providerId)}/{date}");
        request.Headers.Add("X-Provider-Signature", ProviderSignature.Compute(provider.Secret, Encoding.UTF8.GetBytes(RequestLine(providerId, date))));
        try
        {
            using var response = await http.CreateClient(ClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var report = await response.Content.ReadFromJsonAsync<Report>(Json, cancellationToken);
            return report?.Transactions;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>What both sides sign for a report request.</summary>
    public static string RequestLine(string providerId, string date) => $"GET /reports/{providerId}/{date}";

    private sealed record Report(IReadOnlyList<ReportedTransaction> Transactions);
}
