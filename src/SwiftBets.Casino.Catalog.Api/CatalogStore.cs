using Dapper;
using Npgsql;
using SwiftBets.BuildingBlocks.Persistence;

namespace SwiftBets.Casino.Catalog.Api;

public sealed record Money(long MinorUnits, string Currency);

public sealed record AvailabilityRequest(bool Enabled);

public sealed record LobbyGame(string GameId, string Name, string ProviderId, string Category, string? Tag, Money MinBet);

public sealed record LobbyCategory(string Key, string Name, IReadOnlyList<LobbyGame> Games);

public sealed record Lobby(IReadOnlyList<LobbyCategory> Categories);

public sealed record GameDetail(string GameId, string Name, string ProviderId, string Category, string? Tag, Money MinBet, string WalletModel, bool Available);

/// <summary>The casino catalogue: which games exist, who provides them and where each may be offered.</summary>
public sealed class CatalogStore(NpgsqlDataSource dataSource)
{
    private static readonly SqlResources Sql = SqlResources.For<CatalogStore>();

    public async Task<Lobby> LobbyAsync(string market, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<LobbyRow>(new CommandDefinition(Sql.Get("Catalog.Lobby"), new { Market = market }, cancellationToken: cancellationToken));
        return new Lobby([.. rows.GroupBy(r => (r.CategoryKey, r.CategoryName)).Select(g => new LobbyCategory(g.Key.CategoryKey, g.Key.CategoryName,
            [.. g.Select(r => new LobbyGame(r.GameId, r.Name, r.ProviderId, r.CategoryKey, r.Tag, new Money(r.MinBet, r.Currency)))]))]);
    }

    public async Task<GameDetail?> GameAsync(string gameId, string market, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<GameRow>(new CommandDefinition(Sql.Get("Catalog.Game"), new { GameId = gameId, Market = market }, cancellationToken: cancellationToken));
        return row is null ? null : new GameDetail(row.GameId, row.Name, row.ProviderId, row.Category, row.Tag, new Money(row.MinBet, row.Currency), row.WalletModel, row.Available);
    }

    /// <summary>False when the game does not exist.</summary>
    public async Task<bool> SetAvailabilityAsync(string gameId, string market, bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Catalog.SetAvailability"), new { GameId = gameId, Market = market, Enabled = enabled }, cancellationToken: cancellationToken)) > 0;
    }

    private sealed record LobbyRow(string CategoryKey, string CategoryName, string GameId, string Name, string ProviderId, string? Tag, long MinBet, string Currency);

    private sealed record GameRow(string GameId, string Name, string ProviderId, string Category, string? Tag, long MinBet, string Currency, string WalletModel, bool Available);
}
