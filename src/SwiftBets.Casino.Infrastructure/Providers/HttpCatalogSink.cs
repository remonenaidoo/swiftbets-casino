using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application.Ports;

namespace SwiftBets.Casino.Infrastructure.Providers;

/// <summary>Sends a provider's game list to the catalogue service, which owns what the lobby shows.</summary>
public sealed partial class HttpCatalogSink(IHttpClientFactory http, ClientCredentialsTokenProvider tokens, ILogger<HttpCatalogSink> logger) : ICatalogSink
{
    public const string ClientName = "casino-catalog";

    public async Task<int?> SyncAsync(string providerId, IReadOnlyList<ProviderGame> games, CancellationToken cancellationToken)
    {
        try
        {
            var token = await tokens.GetTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"internal/casino/providers/{Uri.EscapeDataString(providerId)}/games")
            {
                Content = JsonContent.Create(new { games = games.Select(g => new { gameId = g.Symbol, g.Name, g.Category, g.ImageUrl, g.DemoAvailable }) }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.CreateClient(ClientName).SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                tokens.Invalidate(token);
            }

            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<Synced>(cancellationToken))?.Added;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(ex, providerId);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The catalogue could not take {ProviderId}'s games")]
    private partial void LogUnreachable(Exception exception, string providerId);

    private sealed record Synced(int Added);
}
