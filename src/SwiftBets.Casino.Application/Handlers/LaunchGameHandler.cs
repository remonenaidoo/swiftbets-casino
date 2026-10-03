using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Application.Handlers;

public enum LaunchStatus
{
    Launched,
    Invalid,
    Restricted,
    RestrictionsUnavailable,
    ProviderNotFound,

    /// <summary>The provider's launch API refused or could not be reached.</summary>
    ProviderUnavailable,

    /// <summary>The provider offers no free demo of this game.</summary>
    NoDemo,
}

public sealed record LaunchResult(LaunchStatus Status, string? SessionToken = null, string? LaunchUrl = null, DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Opens a game session. A self-excluded, cooling-off or betting-blocked player is refused, and so is everyone while
/// compliance cannot be asked: launching fails closed and never relies on an eventually consistent copy alone.
/// Pragmatic games get their URL from the provider's launch API with our session token; demo play needs neither.
/// </summary>
public sealed partial class LaunchGameHandler(ICasinoStore store, IProviderDirectory providers, IProviderGameApi games, IRestrictions restrictions, IOptions<CasinoOptions> options, TimeProvider time)
{
    public async Task<LaunchResult> HandleAsync(Guid punterId, string? providerId, string? gameId, CancellationToken cancellationToken, string? lobbyUrl = null, string platform = "WEB")
    {
        if (providerId is null || gameId is null || !Id().IsMatch(providerId) || !Id().IsMatch(gameId))
        {
            return new(LaunchStatus.Invalid);
        }

        var now = time.GetUtcNow();
        switch (await restrictions.CheckAsync(punterId, now, cancellationToken))
        {
            case RestrictionCheck.Restricted:
                return new(LaunchStatus.Restricted);
            case RestrictionCheck.Unavailable:
                return new(LaunchStatus.RestrictionsUnavailable);
        }

        var provider = await providers.GetAsync(providerId, cancellationToken);
        var pragmatic = provider?.Protocol == ProviderProtocols.Pragmatic;
        if (provider is not { Enabled: true } || (pragmatic ? string.IsNullOrEmpty(provider.ApiBaseUrl) : string.IsNullOrEmpty(provider.LaunchBaseUrl)))
        {
            return new(LaunchStatus.ProviderNotFound);
        }

        var (token, hash) = SessionToken.New();
        var expiresAt = now.AddHours(options.Value.SessionHours);
        await store.CreateSessionAsync(new GameSession(Guid.CreateVersion7(), hash, punterId, providerId, gameId, options.Value.Currency, now, expiresAt), cancellationToken);
        if (!pragmatic)
        {
            var url = $"{provider.LaunchBaseUrl.TrimEnd('/')}/play?session={Uri.EscapeDataString(token)}&game={Uri.EscapeDataString(gameId)}";
            return new(LaunchStatus.Launched, token, url, expiresAt);
        }

        var gameUrl = await games.GetGameUrlAsync(provider, new GameLaunch(token, punterId, gameId, options.Value.Currency, "en", lobbyUrl ?? string.Empty, platform), cancellationToken);
        return gameUrl is null ? new(LaunchStatus.ProviderUnavailable) : new(LaunchStatus.Launched, token, gameUrl, expiresAt);
    }

    /// <summary>Free play: no account, no session, no wallet. Only providers with a demo address offer it.</summary>
    public async Task<LaunchResult> DemoAsync(string? providerId, string? gameId, string? lobbyUrl, CancellationToken cancellationToken)
    {
        if (providerId is null || gameId is null || !Id().IsMatch(providerId) || !Id().IsMatch(gameId))
        {
            return new(LaunchStatus.Invalid);
        }

        var provider = await providers.GetAsync(providerId, cancellationToken);
        if (provider is not { Enabled: true })
        {
            return new(LaunchStatus.ProviderNotFound);
        }

        return string.IsNullOrEmpty(provider.DemoBaseUrl)
            ? new(LaunchStatus.NoDemo)
            : new(LaunchStatus.Launched, LaunchUrl: games.DemoUrl(provider, gameId, options.Value.Currency, lobbyUrl ?? string.Empty));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,79}$")]
    private static partial Regex Id();
}
