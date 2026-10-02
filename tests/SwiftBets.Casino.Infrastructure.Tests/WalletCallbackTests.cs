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

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace SwiftBets.Casino.Infrastructure.Tests;

public sealed class WalletCallbackTests(SqlServerFixture sql)
{
    private const string Provider = "sim-seamless";

    [Fact]
    public async Task A_bet_debits_and_a_win_credits_each_recorded_with_its_event()
    {
        var (handler, wallet, token, connectionString) = await ArrangeAsync(10_000);

        (await Call(handler, WalletAction.Bet, token, "b-1", 500)).Status.ShouldBe("ok");
        var win = await Call(handler, WalletAction.Win, token, "w-1", 1_200);

        (win.Status, win.Balance, wallet.Balance).ShouldBe(("ok", 10_700L, 10_700L));
        (await OutboxCountAsync(connectionString)).ShouldBe(2);
    }

    [Fact]
    public async Task A_duplicate_win_callback_credits_once_and_returns_the_same_transaction()
    {
        var (handler, wallet, token, connectionString) = await ArrangeAsync(10_000);

        var first = await Call(handler, WalletAction.Win, token, "w-dup", 2_000);
        var again = await Call(handler, WalletAction.Win, token, "w-dup", 2_000);

        again.TransactionId.ShouldBe(first.TransactionId);
        (wallet.Balance, wallet.Postings).ShouldBe((12_000L, 1));
        (await OutboxCountAsync(connectionString)).ShouldBe(1);
    }

    [Fact]
    public async Task A_rollback_for_an_unseen_bet_is_stored_and_the_late_bet_is_refused_without_a_debit()
    {
        var (handler, wallet, token, connectionString) = await ArrangeAsync(10_000);

        var rollback = await Call(handler, WalletAction.Rollback, token, "rb-1", 0, reference: "b-late");
        var late = await Call(handler, WalletAction.Bet, token, "b-late", 700);

        (rollback.Status, late.Status, wallet.Balance).ShouldBe(("ok", CallbackReply.BetRolledBack, 10_000L));
        await using var connection = new SqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<byte>("SELECT Status FROM casino.Transactions WHERE ProviderTransactionId = 'rb-1'")).ShouldBe((byte)TransactionStatus.AcceptedUnseenRollback);
    }

    [Fact]
    public async Task A_seen_bet_rolled_back_twice_is_refunded_once()
    {
        var (handler, wallet, token, _) = await ArrangeAsync(10_000);
        await Call(handler, WalletAction.Bet, token, "b-2", 900);

        await Call(handler, WalletAction.Rollback, token, "rb-2", 0, reference: "b-2");
        await Call(handler, WalletAction.Rollback, token, "rb-3", 0, reference: "b-2");

        wallet.Balance.ShouldBe(10_000);
    }

    [Fact]
    public async Task A_free_spin_bet_debits_nothing_and_takes_a_spin_until_none_are_left()
    {
        var (handler, wallet, token, connectionString) = await ArrangeAsync(10_000, grantSpins: 1);

        var spin = await Call(handler, WalletAction.Bet, token, "fs-1", 0, freeSpin: true);
        var none = await Call(handler, WalletAction.Bet, token, "fs-2", 0, freeSpin: true);

        (spin.Status, none.Status, wallet.Balance).ShouldBe(("ok", CallbackReply.NoFreeSpins, 10_000L));
        await using var connection = new SqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT Remaining FROM casino.FreeSpinGrants")).ShouldBe(0);
    }

    [Fact]
    public async Task A_bet_the_balance_cannot_cover_is_refused_and_nothing_is_recorded()
    {
        var (handler, wallet, token, connectionString) = await ArrangeAsync(100);

        (await Call(handler, WalletAction.Bet, token, "b-big", 5_000)).Status.ShouldBe(CallbackReply.InsufficientFunds);

        (wallet.Balance, await OutboxCountAsync(connectionString)).ShouldBe((100L, 0));
    }

    [Fact]
    public async Task A_transfer_in_then_out_moves_money_once_each_and_a_repeated_transfer_out_credits_once()
    {
        var (handler, wallet, token, connectionString) = await ArrangeAsync(10_000, provider: Transfer);

        (await Call(handler, WalletAction.TransferIn, token, "ti-1", 4_000, provider: Transfer)).Status.ShouldBe("ok");
        await Call(handler, WalletAction.TransferOut, token, "to-1", 5_500, provider: Transfer);
        await Call(handler, WalletAction.TransferOut, token, "to-1", 5_500, provider: Transfer);

        (wallet.Balance, wallet.Postings).ShouldBe((11_500L, 2));
        (await OutboxCountAsync(connectionString)).ShouldBe(2);
    }

    [Fact]
    public async Task A_round_bet_against_a_transfer_wallet_provider_is_refused_and_moves_nothing()
    {
        var (handler, wallet, token, _) = await ArrangeAsync(10_000, provider: Transfer);

        (await Call(handler, WalletAction.Bet, token, "b-x", 500, provider: Transfer)).Status.ShouldBe(CallbackReply.WalletModelMismatch);

        wallet.Balance.ShouldBe(10_000);
    }

    private const string Transfer = "sim-transfer";

    private static Task<CallbackReply> Call(WalletCallbackHandler handler, WalletAction action, string token, string ptx, long amount, string? reference = null, bool freeSpin = false, string provider = Provider) =>
        handler.HandleAsync(provider, action, new WalletCallback(token, ptx, "round-1", "sun-temple", amount, "ZAR", freeSpin, reference), CancellationToken.None);

    private async Task<(WalletCallbackHandler, FakeWallet, string, string)> ArrangeAsync(long opening, int grantSpins = 0, string provider = Provider)
    {
        var connectionString = await sql.CreateDatabaseAsync("casino_" + Guid.NewGuid().ToString("N")[..10]);
        var entry = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbCasino={connectionString}" }]);
        (entry is Task<int> task ? await task : (int)entry!).ShouldBe(0);
        var kafka = Options.Create(new KafkaOptions { BootstrapServers = "unused:9092", Environment = "test", ClientId = "test" });
        var store = new SqlCasinoStore(new SqlServerConnectionFactory(connectionString), new SqlServerOutbox(kafka, TimeProvider.System), TimeProvider.System);
        var punter = Guid.NewGuid();
        var (token, hash) = SessionToken.New();
        var now = DateTimeOffset.UtcNow;
        await store.CreateSessionAsync(new GameSession(Guid.NewGuid(), hash, punter, provider, "sun-temple", "ZAR", now, now.AddHours(2)), CancellationToken.None);
        if (grantSpins > 0)
        {
            await store.GrantFreeSpinsAsync(punter, "sun-temple", grantSpins, now.AddDays(1), Guid.NewGuid(), CancellationToken.None);
        }

        var wallet = new FakeWallet(opening);
        return (new WalletCallbackHandler(store, wallet, Options.Create(new CasinoOptions()), TimeProvider.System), wallet, token, connectionString);
    }

    private static async Task<int> OutboxCountAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM outbox.Messages WHERE Topic LIKE '%casino.transaction%'");
    }
}
