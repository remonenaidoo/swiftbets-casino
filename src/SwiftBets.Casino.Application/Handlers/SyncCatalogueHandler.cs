using SwiftBets.Casino.Application.Ports;

namespace SwiftBets.Casino.Application.Handlers;

public sealed record SyncResult(int? Listed, int? Added, string? Error)
{
    public const string ProviderUnknown = "provider_not_found";
    public const string ProviderUnreachable = "provider_unreachable";
    public const string CatalogueUnreachable = "catalogue_unreachable";
}

/// <summary>Copies the provider's game list into the casino catalogue; new games arrive hidden until an operator shows them.</summary>
public sealed class SyncCatalogueHandler(IProviderDirectory providers, IProviderGameApi games, ICatalogSink catalogue)
{
    public async Task<SyncResult> HandleAsync(string providerId, CancellationToken cancellationToken)
    {
        if (await providers.GetAsync(providerId, cancellationToken) is not { Protocol: ProviderProtocols.Pragmatic } provider || string.IsNullOrEmpty(provider.ApiBaseUrl))
        {
            return new(null, null, SyncResult.ProviderUnknown);
        }

        if (await games.GetGamesAsync(provider, cancellationToken) is not { } listed)
        {
            return new(null, null, SyncResult.ProviderUnreachable);
        }

        var added = await catalogue.SyncAsync(providerId, listed, cancellationToken);
        return added is null ? new(listed.Count, null, SyncResult.CatalogueUnreachable) : new(listed.Count, added, null);
    }
}
