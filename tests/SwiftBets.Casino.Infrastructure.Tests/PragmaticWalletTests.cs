using System.Text.Json;
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

namespace SwiftBets.Casino.Infrastructure.Tests;

public sealed class PragmaticWalletTests(SqlServerFixture sql)
{
    private const string Secret = "pp-secret";
    private static readonly ProviderSettings Provider = new("pragmatic", true, WalletModels.Seamless, ProviderProtocols.Pragmatic, Secret, "login", string.Empty,
        "http://pp.test", string.Empty, string.Empty, [], null);

    [Fact]
    public async Task A_repeated_bet_gets_the_original_reply_byte_for_byte_and_debits_once()
    {
        var (handler, wallet, punter, _) = await ArrangeAsync(10_000);

        var first = await Call(handler, "bet", punter, "ref-1", "5.00");
        var again = await Call(handler, "bet", punter, "ref-1", "5.00");

        again.ShouldBe(first);
        (Error(first), Cash(first), wallet.Balance, wallet.Postings).ShouldBe((0, 95.00m, 9_500L, 1));
    }

    [Fact]
    public async Task A_refund_before_its_bet_leaves_a_marker_and_the_late_bet_is_refused_without_a_debit()
    {
        var (handler, wallet, punter, _) = await ArrangeAsync(10_000);

        var refund = await Call(handler, "refund", punter, "ref-late", "5.00");
        var late = await Call(handler, "bet", punter, "ref-late", "5.00");

        (Error(refund), Error(late), wallet.Balance).ShouldBe((0, PragmaticError.BetNotAllowed, 10_000L));
    }

    [Fact]
    public async Task A_refund_pays_back_the_recorded_stake_whatever_amount_it_names()
    {
        var (handler, wallet, punter, _) = await ArrangeAsync(10_000);
        await Call(handler, "bet", punter, "ref-2", "7.00");

        await Call(handler, "refund", punter, "ref-2", "700.00");
        await Call(handler, "refund", punter, "ref-2", "700.00");

        wallet.Balance.ShouldBe(10_000);
    }

    [Fact]
    public async Task A_self_excluded_player_or_a_bad_hash_is_refused_with_its_code_and_moves_nothing()
    {
        var (handler, wallet, punter, _) = await ArrangeAsync(10_000, restricted: true);

        var excluded = await Call(handler, "bet", punter, "ref-3", "5.00");
        var forged = await handler.HandleAsync(Provider, "bet", new Dictionary<string, string> { ["userId"] = punter.ToString(), ["reference"] = "ref-4", ["amount"] = "5.00", ["hash"] = new string('a', 32) },
            CancellationToken.None);

        (Error(excluded), Error(forged), wallet.Balance).ShouldBe((PragmaticError.PlayerFrozen, PragmaticError.InvalidHash, 10_000L));
    }

    [Fact]
    public async Task Authenticate_names_the_player_and_their_cash_and_a_result_credits_the_win()
    {
        var (handler, wallet, punter, token) = await ArrangeAsync(10_000);

        var auth = await handler.HandleAsync(Provider, "authenticate", Signed(new() { ["token"] = token }), CancellationToken.None);
        var win = await Call(handler, "result", punter, "res-1", "12.50");
        var end = await handler.HandleAsync(Provider, "endRound", Signed(new() { ["userId"] = punter.ToString(), ["gameId"] = "vs20sunwolf", ["roundId"] = "r" }), CancellationToken.None);

        JsonDocument.Parse(auth).RootElement.GetProperty("userId").GetString().ShouldBe(punter.ToString());
        (Cash(auth), Cash(win), Cash(end), wallet.Balance).ShouldBe((100m, 112.50m, 112.50m, 11_250L));
    }

    [Fact]
    public async Task An_unknown_token_or_player_a_fractional_cent_or_a_disabled_provider_is_refused_with_its_code()
    {
        var (handler, wallet, punter, _) = await ArrangeAsync(10_000);

        var token = await handler.HandleAsync(Provider, "authenticate", Signed(new() { ["token"] = "nope" }), CancellationToken.None);
        var player = await Call(handler, "bet", Guid.NewGuid(), "ref-5", "1.00");
        var fraction = await Call(handler, "bet", punter, "ref-6", "1.005");
        var off = await handler.HandleAsync(Provider with { Enabled = false }, "balance", Signed(new() { ["userId"] = punter.ToString() }), CancellationToken.None);

        (Error(token), Error(player), Error(fraction), Error(off), wallet.Balance)
            .ShouldBe((PragmaticError.TokenInvalid, PragmaticError.PlayerNotFound, PragmaticError.BadParameters, PragmaticError.InternalNoRetry, 10_000L));
    }

    [Fact]
    public async Task A_negative_adjustment_debits_and_a_positive_one_credits()
    {
        var (handler, wallet, punter, _) = await ArrangeAsync(10_000);

        await Call(handler, "adjustment", punter, "adj-1", "-3.00");
        await Call(handler, "adjustment", punter, "adj-2", "1.00");

        wallet.Balance.ShouldBe(9_800);
    }

    private static Dictionary<string, string> Signed(Dictionary<string, string> fields)
    {
        fields["hash"] = PragmaticHash.Compute(fields, Secret);
        return fields;
    }

    private static Task<string> Call(PragmaticWalletHandler handler, string method, Guid punter, string reference, string amount)
    {
        var fields = new Dictionary<string, string> { ["userId"] = punter.ToString(), ["gameId"] = "vs20sunwolf", ["roundId"] = "round-" + reference, ["reference"] = reference, ["amount"] = amount };
        fields["hash"] = PragmaticHash.Compute(fields, Secret);
        return handler.HandleAsync(Provider, method, fields, CancellationToken.None);
    }

    private static int Error(string reply) => JsonDocument.Parse(reply).RootElement.GetProperty("error").GetInt32();

    private static decimal Cash(string reply) => JsonDocument.Parse(reply).RootElement.GetProperty("cash").GetDecimal();

    private async Task<(PragmaticWalletHandler Handler, FakeWallet Wallet, Guid Punter, string Token)> ArrangeAsync(long opening, bool restricted = false)
    {
        var connectionString = await sql.CreateDatabaseAsync("casino_" + Guid.NewGuid().ToString("N")[..10]);
        var entry = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbCasino={connectionString}" }]);
        (entry is Task<int> task ? await task : (int)entry!).ShouldBe(0);
        var kafka = Options.Create(new KafkaOptions { BootstrapServers = "unused:9092", Environment = "test", ClientId = "test" });
        var store = new SqlCasinoStore(new SqlServerConnectionFactory(connectionString), new SqlServerOutbox(kafka, TimeProvider.System), TimeProvider.System);
        var punter = Guid.NewGuid();
        var (token, hash) = SessionToken.New();
        var now = DateTimeOffset.UtcNow;
        await store.CreateSessionAsync(new GameSession(Guid.NewGuid(), hash, punter, "pragmatic", "vs20sunwolf", "ZAR", now, now.AddHours(2)), CancellationToken.None);
        var wallet = new FakeWallet(opening);
        var callbacks = new WalletCallbackHandler(store, wallet, restricted ? new AllowAll(punter) : new AllowAll(), Options.Create(new CasinoOptions()), TimeProvider.System);
        return (new PragmaticWalletHandler(store, callbacks, wallet, TimeProvider.System), wallet, punter, token);
    }
}
