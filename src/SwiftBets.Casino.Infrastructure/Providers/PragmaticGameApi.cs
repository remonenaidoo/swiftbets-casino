using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Infrastructure.Providers;

/// <summary>
/// Pragmatic Play's Casino Game API: form-encoded POSTs signed like their callbacks, with JSON replies whose
/// <c>error</c> is 0 on success. Used for real-money launch URLs and the game list; demo URLs need no call.
/// </summary>
public sealed partial class PragmaticGameApi(IHttpClientFactory http, ILogger<PragmaticGameApi> logger) : IProviderGameApi
{
    public const string ClientName = "pragmatic-api";
    private const string Base = "IntegrationService/v3/http/CasinoGameAPI";

    public async Task<string?> GetGameUrlAsync(ProviderSettings provider, GameLaunch launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(launch);
        var reply = await PostAsync(provider, "game/url/", new Dictionary<string, string>
        {
            ["secureLogin"] = provider.SecureLogin, ["symbol"] = launch.Symbol, ["language"] = launch.Language, ["token"] = launch.Token,
            ["externalPlayerId"] = launch.PunterId.ToString(), ["currency"] = launch.Currency, ["platform"] = launch.Platform, ["lobbyUrl"] = launch.LobbyUrl,
        }, cancellationToken);
        return reply is { } json && json.TryGetProperty("gameURL", out var url) && url.GetString() is { Length: > 0 } value && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http" ? value : null;
    }

    public async Task<IReadOnlyList<ProviderGame>?> GetGamesAsync(ProviderSettings provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (await PostAsync(provider, "getCasinoGames/", new Dictionary<string, string> { ["secureLogin"] = provider.SecureLogin }, cancellationToken) is not { } json
            || !json.TryGetProperty("gameList", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var games = new List<ProviderGame>();
        foreach (var game in list.EnumerateArray())
        {
            var symbol = Text(game, "gameID")?.ToLowerInvariant();
            var name = Text(game, "gameName");
            if (symbol is null || name is null)
            {
                continue;
            }

            var image = provider.ImageBaseUrl.Length > 0 ? $"{provider.ImageBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(symbol)}.png" : null;
            var demo = game.TryGetProperty("demoGameAvailable", out var d) && d.ValueKind == JsonValueKind.True;
            games.Add(new ProviderGame(symbol, name, Category(Text(game, "gameTypeID"), Text(game, "typeDescription")), image, demo));
        }

        return games;
    }

    public string DemoUrl(ProviderSettings provider, string symbol, string currency, string lobbyUrl)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return $"{provider.DemoBaseUrl.TrimEnd('/')}/gs2c/openGame.do?gameSymbol={Uri.EscapeDataString(symbol)}&lang=en&cur={Uri.EscapeDataString(currency)}"
            + $"&jurisdiction=99&lobbyUrl={Uri.EscapeDataString(lobbyUrl)}";
    }

    /// <summary>Pragmatic's game types onto our lobby's categories.</summary>
    public static string Category(string? typeId, string? description)
    {
        var text = $"{typeId} {description}".ToLowerInvariant();
        return text switch
        {
            _ when text.Contains("live", StringComparison.Ordinal) || typeId == "lg" => "live",
            _ when text.Contains("slot", StringComparison.Ordinal) || typeId is "vs" or "cs" => "slots",
            _ when text.Contains("roulette", StringComparison.Ordinal) || text.Contains("blackjack", StringComparison.Ordinal) || text.Contains("baccarat", StringComparison.Ordinal) => "table",
            _ when text.Contains("crash", StringComparison.Ordinal) => "crash",
            _ => "other",
        };
    }

    private async Task<JsonElement?> PostAsync(ProviderSettings provider, string path, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        if (provider.ApiBaseUrl.Length == 0 || provider.Secret.Length == 0)
        {
            return null;
        }

        form[PragmaticHash.Field] = PragmaticHash.Compute(form, provider.Secret);
        using var content = new FormUrlEncodedContent(form);
        try
        {
            using var response = await http.CreateClient(ClientName).PostAsync(new Uri($"{provider.ApiBaseUrl.TrimEnd('/')}/{Base}/{path}"), content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(provider.ProviderId, path, (int)response.StatusCode);
                return null;
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement.Clone();
            var error = root.TryGetProperty("error", out var e) ? (e.ValueKind == JsonValueKind.Number ? e.GetInt32().ToString(CultureInfo.InvariantCulture) : e.GetString()) : null;
            if (error != "0")
            {
                LogRefused(provider.ProviderId, path, int.TryParse(error, CultureInfo.InvariantCulture, out var code) ? code : -1);
                return null;
            }

            return root;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(ex, provider.ProviderId, path);
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider {ProviderId} refused {Path} with code {Code}")]
    private partial void LogRefused(string providerId, string path, int code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider {ProviderId} could not be reached for {Path}")]
    private partial void LogUnreachable(Exception exception, string providerId, string path);
}
