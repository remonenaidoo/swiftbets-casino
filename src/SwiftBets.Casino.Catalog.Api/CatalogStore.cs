using Dapper;
using Npgsql;
using SwiftBets.BuildingBlocks.Persistence;

namespace SwiftBets.Casino.Catalog.Api;

public sealed record Money(long MinorUnits, string Currency);

public sealed record AvailabilityRequest(bool Enabled);

public sealed record LobbyGame(string GameId, string Name, string ProviderId, string Category, string? Tag, Money MinBet, string? ImageUrl = null, bool DemoAvailable = false);

public sealed record SyncedGame(string GameId, string Name, string Category, string? ImageUrl, bool DemoAvailable);

public sealed record SyncRequest(IReadOnlyList<SyncedGame> Games);

public sealed record AdminGame(string GameId, string Name, string ProviderId, string Category, int Position, string? ImageUrl, bool DemoAvailable, bool Enabled, DateTime? SyncedAt);

public sealed record GameUpdate(bool? Enabled, string? Category, int? Position);

public sealed record CategoryView(string Key, string Name);

public sealed record LobbyCategory(string Key, string Name, IReadOnlyList<LobbyGame> Games);

public sealed record Lobby(IReadOnlyList<LobbyCategory> Categories);

public sealed record GameDetail(string GameId, string Name, string ProviderId, string Category, string? Tag, Money MinBet, string WalletModel, bool Available);

/// <summary>The casino catalogue: which games exist, who provides them and where each may be offered.</summary>
public sealed partial class CatalogStore(NpgsqlDataSource dataSource)
{
    private static readonly SqlResources Sql = SqlResources.For<CatalogStore>();

    public async Task<Lobby> LobbyAsync(string market, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<LobbyRow>(new CommandDefinition(Sql.Get("Catalog.Lobby"), new { Market = market }, cancellationToken: cancellationToken));
        return new Lobby([.. rows.GroupBy(r => (r.CategoryKey, r.CategoryName)).Select(g => new LobbyCategory(g.Key.CategoryKey, g.Key.CategoryName,
            [.. g.Select(r => new LobbyGame(r.GameId, r.Name, r.ProviderId, r.CategoryKey, r.Tag, new Money(r.MinBet, r.Currency), r.ImageUrl, r.DemoAvailable))]))]);
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

    /// <summary>
    /// Takes a provider's game list: new games are added at the end of their category, shown or hidden per
    /// <paramref name="show"/>; known games keep the operator's choices. Ids that are not lower-case slugs, or that
    /// belong to another provider, are skipped. Returns how many were new.
    /// </summary>
    public async Task<int> SyncAsync(string providerId, string market, IReadOnlyList<SyncedGame> games, bool show, long minBet, string currency, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(games);
        var categories = (await CategoriesAsync(cancellationToken)).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var added = 0;
        foreach (var game in games.Where(g => Slug().IsMatch(g.GameId) && g.Name.Length is > 0 and <= 120).DistinctBy(g => g.GameId))
        {
            var isNew = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(Sql.Get("Catalog.UpsertProviderGame"), new
            {
                game.GameId, game.Name, Category = categories.Contains(game.Category) ? game.Category : "other", ProviderId = providerId, MinBet = minBet, Currency = currency,
                ImageUrl = game.ImageUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? url : null,
                game.DemoAvailable, Market = market, Show = show,
            }, tx, cancellationToken: cancellationToken));
            added += isNew ? 1 : 0;
        }

        await tx.CommitAsync(cancellationToken);
        return added;
    }

    public async Task<IReadOnlyList<AdminGame>> AdminGamesAsync(string market, string? providerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<AdminGame>(new CommandDefinition(Sql.Get("Catalog.AdminGames"), new { Market = market, ProviderId = providerId }, cancellationToken: cancellationToken))];
    }

    /// <summary>False when the game does not exist; an unknown category is refused by the database's key.</summary>
    public async Task<bool> UpdateGameAsync(string gameId, string market, GameUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var found = await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Catalog.UpdateGame"), new { GameId = gameId, update.Category, update.Position }, cancellationToken: cancellationToken)) > 0;
        if (found && update.Enabled is { } enabled)
        {
            await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Catalog.SetAvailability"), new { GameId = gameId, Market = market, Enabled = enabled }, cancellationToken: cancellationToken));
        }

        return found;
    }

    public async Task<IReadOnlyList<CategoryView>> CategoriesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<CategoryView>(new CommandDefinition(Sql.Get("Catalog.Categories"), cancellationToken: cancellationToken))];
    }

    public async Task<IReadOnlyList<string>> FavouritesAsync(Guid punterId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<string>(new CommandDefinition(Sql.Get("Catalog.Favourites"), new { PunterId = punterId }, cancellationToken: cancellationToken))];
    }

    /// <summary>False when the game does not exist.</summary>
    public async Task<bool> SetFavouriteAsync(Guid punterId, string gameId, bool favourite, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!favourite)
        {
            await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Catalog.RemoveFavourite"), new { PunterId = punterId, GameId = gameId }, cancellationToken: cancellationToken));
            return true;
        }

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(Sql.Get("Catalog.AddFavourite"), new { PunterId = punterId, GameId = gameId }, cancellationToken: cancellationToken));
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9][a-z0-9-]{0,79}$")]
    private static partial System.Text.RegularExpressions.Regex Slug();

    private sealed record LobbyRow(string CategoryKey, string CategoryName, string GameId, string Name, string ProviderId, string? Tag, long MinBet, string Currency, string? ImageUrl, bool DemoAvailable);

    private sealed record GameRow(string GameId, string Name, string ProviderId, string Category, string? Tag, long MinBet, string Currency, string WalletModel, bool Available);
}
