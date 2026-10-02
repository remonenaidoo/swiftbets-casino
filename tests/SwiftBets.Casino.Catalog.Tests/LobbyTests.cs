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
