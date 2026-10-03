extern alias migrator;
using Npgsql;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Casino.Catalog.Api;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace SwiftBets.Casino.Catalog.Tests;

public sealed class LobbyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_lobby_lists_the_seeded_games_by_category_with_their_providers()
    {
        var store = await StoreAsync();

        var lobby = await store.LobbyAsync("ZA", CancellationToken.None);

        lobby.Categories.Select(c => c.Key).ShouldBe(["slots", "live", "crash"]);
        lobby.Categories.SelectMany(c => c.Games).Count().ShouldBe(12);
        lobby.Categories[1].Games.ShouldAllBe(g => g.ProviderId == "sim-transfer");
    }

    [Fact]
    public async Task A_game_disabled_for_the_market_leaves_the_lobby_but_can_still_be_looked_up()
    {
        var store = await StoreAsync();

        (await store.SetAvailabilityAsync("rocket", "ZA", false, CancellationToken.None)).ShouldBeTrue();

        (await store.LobbyAsync("ZA", CancellationToken.None)).Categories.SelectMany(c => c.Games).ShouldNotContain(g => g.GameId == "rocket");
        (await store.GameAsync("rocket", "ZA", CancellationToken.None))!.Available.ShouldBeFalse();
    }

    [Fact]
    public async Task A_synced_provider_game_arrives_hidden_and_a_resync_keeps_the_operators_choices()
    {
        var store = await StoreAsync();
        SyncedGame[] games = [new("vs20sunwolf", "Sun Wolf", "slots", "https://img.test/vs20sunwolf.png", true)];

        (await store.SyncAsync("pragmatic", "ZA", games, show: false, 100, "ZAR", CancellationToken.None)).ShouldBe(1);
        (await store.LobbyAsync("ZA", CancellationToken.None)).Categories.SelectMany(c => c.Games).ShouldNotContain(g => g.GameId == "vs20sunwolf");
        await store.UpdateGameAsync("vs20sunwolf", "ZA", new GameUpdate(true, "table", 3), CancellationToken.None);
        (await store.SyncAsync("pragmatic", "ZA", [games[0] with { Name = "Sun Wolf Megaways" }], show: false, 100, "ZAR", CancellationToken.None)).ShouldBe(0);

        var game = (await store.LobbyAsync("ZA", CancellationToken.None)).Categories.SelectMany(c => c.Games).Single(g => g.GameId == "vs20sunwolf");
        (game.Name, game.Category, game.ImageUrl, game.DemoAvailable).ShouldBe(("Sun Wolf Megaways", "table", "https://img.test/vs20sunwolf.png", true));
    }

    [Fact]
    public async Task A_sync_skips_ids_that_are_not_slugs_and_never_takes_over_another_providers_game()
    {
        var store = await StoreAsync();

        var added = await store.SyncAsync("pragmatic", "ZA", [new("Bad Id!", "Bad", "slots", null, false), new("sun-temple", "Hijacked", "slots", null, false)], show: true, 100, "ZAR", CancellationToken.None);

        added.ShouldBe(0);
        (await store.GameAsync("sun-temple", "ZA", CancellationToken.None))!.Name.ShouldBe("Sun Temple");
    }

    [Fact]
    public async Task A_player_favourites_a_game_and_an_unknown_game_is_refused()
    {
        var store = await StoreAsync();
        var punter = Guid.NewGuid();

        (await store.SetFavouriteAsync(punter, "rocket", true, CancellationToken.None)).ShouldBeTrue();
        (await store.SetFavouriteAsync(punter, "no-such-game", true, CancellationToken.None)).ShouldBeFalse();

        (await store.FavouritesAsync(punter, CancellationToken.None)).ShouldBe(["rocket"]);
    }

    private async Task<CatalogStore> StoreAsync()
    {
        var database = "casino_" + Guid.NewGuid().ToString("N")[..10];
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString;
        var entry = typeof(migrator::Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbCasinoCatalog={connectionString}" }]);
        (entry is Task<int> task ? await task : (int)entry!).ShouldBe(0);
        return new CatalogStore(NpgsqlDataSource.Create(connectionString));
    }
}
