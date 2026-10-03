namespace SwiftBets.Casino.Application.Ports;

/// <summary>A provider's working settings: configuration, overridden by what an operator saved in the console.</summary>
public sealed record ProviderSettings(
    string ProviderId,
    bool Enabled,
    string WalletModel,
    string Protocol,
    string Secret,
    string SecureLogin,
    string LaunchBaseUrl,
    string ApiBaseUrl,
    string DemoBaseUrl,
    string ImageBaseUrl,
    IReadOnlyList<string> AllowedAddresses,
    DateTimeOffset? UpdatedAt);

/// <summary>What an operator may change; null leaves a field as it is.</summary>
public sealed record ProviderUpdate(bool? Enabled, IReadOnlyList<string>? AllowedAddresses, string? ApiBaseUrl, string? DemoBaseUrl);

public interface IProviderDirectory
{
    Task<ProviderSettings?> GetAsync(string providerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderSettings>> ListAsync(CancellationToken cancellationToken);

    /// <summary>False for an unknown provider.</summary>
    Task<bool> UpdateAsync(string providerId, ProviderUpdate update, Guid operatorId, CancellationToken cancellationToken);

    /// <summary>Stores the credentials encrypted; they are never read back out through the API. False for an unknown provider.</summary>
    Task<bool> SetCredentialsAsync(string providerId, string? secureLogin, string? secret, Guid operatorId, CancellationToken cancellationToken);
}

/// <summary>A game as the provider lists it.</summary>
public sealed record ProviderGame(string Symbol, string Name, string Category, string? ImageUrl, bool DemoAvailable);

/// <summary>The provider's server API: launch URLs and the game list.</summary>
public interface IProviderGameApi
{
    /// <summary>The real-money game URL for a session token, or null when the provider refused or could not be reached.</summary>
    Task<string?> GetGameUrlAsync(ProviderSettings provider, GameLaunch launch, CancellationToken cancellationToken);

    /// <summary>The provider's games, or null when it could not be reached.</summary>
    Task<IReadOnlyList<ProviderGame>?> GetGamesAsync(ProviderSettings provider, CancellationToken cancellationToken);

    /// <summary>A free-play URL: no session, no wallet.</summary>
    string DemoUrl(ProviderSettings provider, string symbol, string currency, string lobbyUrl);
}

public sealed record GameLaunch(string Token, Guid PunterId, string Symbol, string Currency, string Language, string LobbyUrl, string Platform);

/// <summary>The casino catalogue service, which the lobby is served from.</summary>
public interface ICatalogSink
{
    /// <summary>Adds new games and refreshes names and artwork; operator choices (shown, category, order) are kept. Returns how many were new.</summary>
    Task<int?> SyncAsync(string providerId, IReadOnlyList<ProviderGame> games, CancellationToken cancellationToken);
}
