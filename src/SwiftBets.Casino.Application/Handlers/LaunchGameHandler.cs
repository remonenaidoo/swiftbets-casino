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
}

public sealed record LaunchResult(LaunchStatus Status, string? SessionToken = null, string? LaunchUrl = null, DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Opens a game session. A self-excluded, cooling-off or betting-blocked player is refused, and so is everyone while
/// compliance cannot be asked: launching fails closed and never relies on an eventually consistent copy alone.
/// </summary>
public sealed partial class LaunchGameHandler(ICasinoStore store, IRestrictions restrictions, IOptions<CasinoOptions> options, TimeProvider time)
{
    public async Task<LaunchResult> HandleAsync(Guid punterId, string? providerId, string? gameId, CancellationToken cancellationToken)
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

        if (!options.Value.Providers.TryGetValue(providerId, out var provider) || string.IsNullOrEmpty(provider.LaunchBaseUrl)
            || !await store.IsProviderEnabledAsync(providerId, cancellationToken))
        {
            return new(LaunchStatus.ProviderNotFound);
        }

        var (token, hash) = SessionToken.New();
        var expiresAt = now.AddHours(options.Value.SessionHours);
        await store.CreateSessionAsync(new GameSession(Guid.CreateVersion7(), hash, punterId, providerId, gameId, options.Value.Currency, now, expiresAt), cancellationToken);
        var url = $"{provider.LaunchBaseUrl.TrimEnd('/')}/play?session={Uri.EscapeDataString(token)}&game={Uri.EscapeDataString(gameId)}";
        return new(LaunchStatus.Launched, token, url, expiresAt);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,79}$")]
    private static partial Regex Id();
}
