using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;
using SwiftBets.Casino.Infrastructure.Persistence;
using SwiftBets.Contracts.Casino;

namespace SwiftBets.Casino.Infrastructure.Tests;

public sealed class ReconciliationStoreTests(SqlServerFixture sql)
{
    [Fact]
    public async Task A_day_that_matches_the_provider_report_is_stored_and_published_once()
    {
        var (store, wallet, token, connectionString) = await ArrangeAsync();
        var handler = new WalletCallbackHandler(store, wallet, new AllowAll(), Options.Create(new CasinoOptions()), TimeProvider.System);
        await handler.HandleAsync("sim-seamless", WalletAction.Bet, new WalletCallback(token, "b-1", "r-1", "sun-temple", 500, "ZAR"), CancellationToken.None);
        await handler.HandleAsync("sim-seamless", WalletAction.Win, new WalletCallback(token, "w-1", "r-1", "sun-temple", 900, "ZAR"), CancellationToken.None);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = new Report([new("b-1", CasinoTransactionKind.Bet, 500), new("w-1", CasinoTransactionKind.Win, 900)]);

        var (run, _) = await new ReconcileProviderHandler(store, report, Options.Create(new CasinoOptions()), TimeProvider.System).HandleAsync("sim-seamless", today, CancellationToken.None);

        (run!.Status, run.OurNet).ShouldBe((ReconciliationStatus.Matched, 400L));
        (await store.HasReconciliationAsync("sim-seamless", today, CancellationToken.None)).ShouldBeTrue();
        (await store.ListReconciliationsAsync("sim-seamless", 10, CancellationToken.None)).ShouldHaveSingleItem().RunId.ShouldBe(run.RunId);
        await using var connection = new SqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM outbox.Messages WHERE Topic LIKE '%provider-reconciliation%'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_day_with_no_report_records_nothing()
    {
        var (store, _, _, _) = await ArrangeAsync();

        var (run, error) = await new ReconcileProviderHandler(store, new Report(null), Options.Create(new CasinoOptions()), TimeProvider.System)
            .HandleAsync("sim-seamless", DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        (run, error).ShouldBe((null, ReconcileProviderHandler.ReportUnavailable));
        (await store.ListReconciliationsAsync(null, 10, CancellationToken.None)).ShouldBeEmpty();
    }

    private async Task<(SqlCasinoStore, FakeWallet, string, string)> ArrangeAsync()
    {
        var connectionString = await sql.CreateDatabaseAsync("casino_" + Guid.NewGuid().ToString("N")[..10]);
        var entry = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbCasino={connectionString}" }]);
        (entry is Task<int> task ? await task : (int)entry!).ShouldBe(0);
        var kafka = Options.Create(new KafkaOptions { BootstrapServers = "unused:9092", Environment = "test", ClientId = "test" });
        var store = new SqlCasinoStore(new SqlServerConnectionFactory(connectionString), new SqlServerOutbox(kafka, TimeProvider.System), TimeProvider.System);
        var (token, hash) = SessionToken.New();
        var now = DateTimeOffset.UtcNow;
        await store.CreateSessionAsync(new GameSession(Guid.NewGuid(), hash, Guid.NewGuid(), "sim-seamless", "sun-temple", "ZAR", now, now.AddHours(2)), CancellationToken.None);
        return (store, new FakeWallet(10_000), token, connectionString);
    }

    private sealed class Report(IReadOnlyList<ReportedTransaction>? transactions) : IProviderReports
    {
        public Task<IReadOnlyList<ReportedTransaction>?> GetAsync(string providerId, DateOnly businessDate, CancellationToken cancellationToken) => Task.FromResult(transactions);
    }
}
