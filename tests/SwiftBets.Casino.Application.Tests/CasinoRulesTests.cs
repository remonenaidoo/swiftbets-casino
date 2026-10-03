using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Contracts.Casino;
using SwiftBets.Contracts.Compliance;

namespace SwiftBets.Casino.Application.Tests;

public sealed class CasinoRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_active_self_exclusion_keeps_a_player_out_of_the_casino() =>
        CasinoRestrictionRules.IsRestricted(State(new Restriction(RestrictionKind.SelfExclusion, Now.AddDays(-1), null, "player asked")), Now).ShouldBeTrue();

    [Fact]
    public void An_expired_cooling_off_or_a_deposit_block_does_not() =>
        CasinoRestrictionRules.IsRestricted(State(
            new Restriction(RestrictionKind.CoolingOff, Now.AddDays(-3), Now.AddDays(-1), "over"),
            new Restriction(RestrictionKind.NoDeposits, Now.AddDays(-1), null, "affordability")), Now).ShouldBeFalse();

    [Fact]
    public async Task A_launch_opens_a_session_with_a_signed_token_in_the_provider_url()
    {
        var store = new FakeStore();
        var handler = Launcher(store, ready: true);

        var result = await handler.HandleAsync(Guid.NewGuid(), "sim-seamless", "sun-temple", CancellationToken.None);

        result.Status.ShouldBe(LaunchStatus.Launched);
        result.LaunchUrl.ShouldBe($"http://provider.test/play?session={Uri.EscapeDataString(result.SessionToken!)}&game=sun-temple");
        store.Sessions.ShouldHaveSingleItem().ExpiresAt.ShouldBe(Now.AddHours(2));
    }

    [Fact]
    public async Task Launch_fails_closed_while_restrictions_are_loading_and_refuses_unknown_providers()
    {
        (await Launcher(new FakeStore(), ready: false).HandleAsync(Guid.NewGuid(), "sim-seamless", "sun-temple", CancellationToken.None)).Status.ShouldBe(LaunchStatus.RestrictionsUnavailable);
        (await Launcher(new FakeStore(), ready: true).HandleAsync(Guid.NewGuid(), "nobody", "sun-temple", CancellationToken.None)).Status.ShouldBe(LaunchStatus.ProviderNotFound);
        (await Launcher(new FakeStore(), ready: true).HandleAsync(Guid.NewGuid(), "sim-seamless", "Bad Game!", CancellationToken.None)).Status.ShouldBe(LaunchStatus.Invalid);
    }

    [Fact]
    public async Task An_operator_grants_free_spins_on_a_game()
    {
        var (grant, error) = await new FreeSpinsHandler(new FakeStore(), new FakeTimeProvider(Now)).GrantAsync(new(Guid.NewGuid(), "sun-temple", 10, Now.AddDays(7)), Guid.NewGuid(), CancellationToken.None);

        (error, grant!.Remaining).ShouldBe((null, 10));
    }

    [Fact]
    public async Task A_grant_with_no_spins_or_a_past_expiry_is_refused()
    {
        var handler = new FreeSpinsHandler(new FakeStore(), new FakeTimeProvider(Now));

        (await handler.GrantAsync(new(Guid.NewGuid(), "sun-temple", 0, Now.AddDays(7)), Guid.NewGuid(), CancellationToken.None)).Error.ShouldBe("spins_out_of_range");
        (await handler.GrantAsync(new(Guid.NewGuid(), "sun-temple", 5, Now.AddDays(-1)), Guid.NewGuid(), CancellationToken.None)).Error.ShouldBe("expiry_in_past");
    }

    [Fact]
    public async Task A_pragmatic_launch_asks_the_provider_api_for_the_game_url_with_our_session_token()
    {
        var store = new FakeStore();

        var result = await Launcher(store, ready: true).HandleAsync(Guid.NewGuid(), "pragmatic", "vs20sunwolf", CancellationToken.None);

        result.Status.ShouldBe(LaunchStatus.Launched);
        result.LaunchUrl.ShouldBe($"http://pp.test/game?token={result.SessionToken}&symbol=vs20sunwolf");
        store.Sessions.ShouldHaveSingleItem().ProviderId.ShouldBe("pragmatic");
    }

    [Fact]
    public async Task Demo_play_opens_without_a_session_and_is_refused_for_a_provider_without_a_demo_address()
    {
        var store = new FakeStore();

        (await Launcher(store, ready: true).DemoAsync("pragmatic", "vs20sunwolf", null, CancellationToken.None)).LaunchUrl.ShouldBe("http://demo.test/demo?symbol=vs20sunwolf");
        (await Launcher(store, ready: true).DemoAsync("sim-seamless", "sun-temple", null, CancellationToken.None)).Status.ShouldBe(LaunchStatus.NoDemo);
        store.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_catalogue_sync_sends_the_providers_list_and_reports_what_was_new()
    {
        var options = Pragmatic();

        var result = await new SyncCatalogueHandler(new Directory(options), new GameApi(), new Sink(2)).HandleAsync("pragmatic", CancellationToken.None);

        (result.Listed, result.Added, result.Error).ShouldBe((1, 2, null));
    }

    [Fact]
    public async Task A_catalogue_sync_names_what_failed_for_an_unknown_provider_or_an_unreachable_catalogue()
    {
        var options = Pragmatic();

        (await new SyncCatalogueHandler(new Directory(options), new GameApi(), new Sink(2)).HandleAsync("sim-seamless", CancellationToken.None)).Error.ShouldBe(SyncResult.ProviderUnknown);
        (await new SyncCatalogueHandler(new Directory(options), new GameApi(), new Sink(null)).HandleAsync("pragmatic", CancellationToken.None)).Error.ShouldBe(SyncResult.CatalogueUnreachable);
    }

    [Fact]
    public void Our_refusals_map_onto_pragmatic_codes_with_retry_only_when_it_can_help()
    {
        Code(PragmaticWalletHandler.Refused(CallbackReply.InsufficientFunds)).ShouldBe(PragmaticError.InsufficientBalance);
        Code(PragmaticWalletHandler.Refused(CallbackReply.LimitExceeded)).ShouldBe(PragmaticError.RegulatoryLimit);
        Code(PragmaticWalletHandler.Refused(CallbackReply.WalletUnavailable)).ShouldBe(PragmaticError.InternalRetry);
        Code(PragmaticWalletHandler.Refused(CallbackReply.SessionInvalid)).ShouldBe(PragmaticError.TokenExpired);
    }

    [Fact]
    public void An_unrecognised_refusal_never_asks_the_provider_to_retry() =>
        Code(PragmaticWalletHandler.Refused("something_new")).ShouldBe(PragmaticError.InternalNoRetry);

    private static int Code(string reply) => System.Text.Json.JsonDocument.Parse(reply).RootElement.GetProperty("error").GetInt32();

    private static CasinoOptions Pragmatic()
    {
        var options = new CasinoOptions();
        options.Providers["sim-seamless"] = new ProviderOptions { Secret = "s", LaunchBaseUrl = "http://provider.test/" };
        options.Providers["pragmatic"] = new ProviderOptions { Secret = "s", Protocol = ProviderProtocols.Pragmatic, ApiBaseUrl = "http://pp.test" };
        return options;
    }

    private sealed class Sink(int? added) : ICatalogSink
    {
        public Task<int?> SyncAsync(string providerId, IReadOnlyList<ProviderGame> games, CancellationToken cancellationToken) => Task.FromResult(added);
    }

    private static LaunchGameHandler Launcher(FakeStore store, bool ready)
    {
        var options = new CasinoOptions();
        options.Providers["sim-seamless"] = new ProviderOptions { Secret = "s", LaunchBaseUrl = "http://provider.test/" };
        options.Providers["pragmatic"] = new ProviderOptions { Secret = "s", Protocol = ProviderProtocols.Pragmatic, ApiBaseUrl = "http://pp.test", DemoBaseUrl = "http://demo.test" };
        return new LaunchGameHandler(store, new Directory(options), new GameApi(), new Ready(ready), Options.Create(options), new FakeTimeProvider(Now));
    }

    private static RestrictionsChangedV1 State(params Restriction[] restrictions) =>
        new(Guid.NewGuid(), 1, [], restrictions, null, null, KycStatus.Verified, Now);

    [Fact]
    public async Task A_day_whose_report_matches_our_ledger_reconciles_as_matched()
    {
        var store = new FakeStore();
        store.Ledger.AddRange([new("b-1", CasinoTransactionKind.Bet, 500), new("w-1", CasinoTransactionKind.Win, 1_200)]);

        var (run, _) = await Reconciler(store, [.. store.Ledger]).HandleAsync("sim-seamless", new DateOnly(2026, 10, 2), CancellationToken.None);

        (run!.Status, run.OurNet, run.Drift).ShouldBe((ReconciliationStatus.Matched, 700L, 0L));
        store.Runs.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_report_missing_a_transaction_reconciles_as_drift_with_its_money()
    {
        var store = new FakeStore();
        store.Ledger.AddRange([new("b-1", CasinoTransactionKind.Bet, 500), new("w-1", CasinoTransactionKind.Win, 1_200)]);

        var (run, _) = await Reconciler(store, [new("b-1", CasinoTransactionKind.Bet, 500)]).HandleAsync("sim-seamless", new DateOnly(2026, 10, 2), CancellationToken.None);

        (run!.Status, run.MissingOnProviderSide, run.MissingOnOurSide, run.Drift).ShouldBe((ReconciliationStatus.Drift, 1, 0, 1_200L));
    }

    private static ReconcileProviderHandler Reconciler(FakeStore store, List<ReportedTransaction> report) =>
        new(store, new FixedReport(report), Options.Create(new CasinoOptions()), new FakeTimeProvider(Now));

    private sealed class FixedReport(List<ReportedTransaction> report) : IProviderReports
    {
        public Task<IReadOnlyList<ReportedTransaction>?> GetAsync(string providerId, DateOnly businessDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportedTransaction>?>(report);
    }

    private sealed class Directory(CasinoOptions options) : IProviderDirectory
    {
        public Task<ProviderSettings?> GetAsync(string providerId, CancellationToken cancellationToken) =>
            Task.FromResult(options.Providers.TryGetValue(providerId, out var p)
                ? new ProviderSettings(providerId, true, WalletModels.Seamless, p.Protocol, p.Secret, p.SecureLogin, p.LaunchBaseUrl, p.ApiBaseUrl, p.DemoBaseUrl, p.ImageBaseUrl, p.AllowedAddresses, null)
                : null);

        public Task<IReadOnlyList<ProviderSettings>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UpdateAsync(string providerId, ProviderUpdate update, Guid operatorId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> SetCredentialsAsync(string providerId, string? secureLogin, string? secret, Guid operatorId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>A provider launch API that answers with a URL carrying the token, and a demo URL builder.</summary>
    private sealed class GameApi : IProviderGameApi
    {
        public Task<string?> GetGameUrlAsync(ProviderSettings provider, GameLaunch launch, CancellationToken cancellationToken) =>
            Task.FromResult<string?>($"{provider.ApiBaseUrl}/game?token={launch.Token}&symbol={launch.Symbol}");

        public Task<IReadOnlyList<ProviderGame>?> GetGamesAsync(ProviderSettings provider, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderGame>?>([new ProviderGame("vs20sunwolf", "Sun Wolf", "slots", null, true)]);

        public string DemoUrl(ProviderSettings provider, string symbol, string currency, string lobbyUrl) => $"{provider.DemoBaseUrl}/demo?symbol={symbol}";
    }

    private sealed class Ready(bool ready) : IRestrictions
    {
        public Task<RestrictionCheck> CheckAsync(Guid punterId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(ready ? RestrictionCheck.Clear : RestrictionCheck.Unavailable);
    }

    private sealed class FakeStore : ICasinoStore
    {
        public List<GameSession> Sessions { get; } = [];

        public Task<bool> IsProviderEnabledAsync(string providerId, CancellationToken cancellationToken) => Task.FromResult(providerId == "sim-seamless");

        public Task CreateSessionAsync(GameSession session, CancellationToken cancellationToken)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<GameSession?> FindSessionAsync(byte[] tokenHash, CancellationToken cancellationToken) => Task.FromResult<GameSession?>(null);

        public Task<GameSession?> FindLatestSessionAsync(Guid punterId, string providerId, CancellationToken cancellationToken) => Task.FromResult<GameSession?>(null);

        public Task<IReadOnlyList<RecentGame>> ListRecentGamesAsync(Guid punterId, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RecentGame>>([]);

        public Task<StoredTransaction?> FindTransactionAsync(string providerId, string providerTransactionId, CancellationToken cancellationToken) => Task.FromResult<StoredTransaction?>(null);

        public Task<bool> IsRolledBackAsync(string providerId, string betProviderTransactionId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<RecordOutcome> RecordAsync(StoredTransaction transaction, bool takeFreeSpin, string? markRolledBack, CancellationToken cancellationToken) => Task.FromResult(RecordOutcome.Recorded);

        public Task<FreeSpinGrant> GrantFreeSpinsAsync(Guid punterId, string gameId, int spins, DateTimeOffset expiresAt, Guid operatorId, CancellationToken cancellationToken) =>
            Task.FromResult(new FreeSpinGrant(Guid.NewGuid(), punterId, gameId, spins, spins, expiresAt));

        public Task<IReadOnlyList<FreeSpinGrant>> ListFreeSpinsAsync(Guid punterId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FreeSpinGrant>>([]);

        public List<ReportedTransaction> Ledger { get; } = [];

        public List<ReconciliationRun> Runs { get; } = [];

        public Task<string?> GetWalletModelAsync(string providerId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(providerId == "sim-seamless" ? WalletModels.Seamless : null);

        public Task<IReadOnlyList<ReportedTransaction>> ListTransactionsAsync(string providerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportedTransaction>>(Ledger);

        public Task RecordReconciliationAsync(ReconciliationRun run, CancellationToken cancellationToken)
        {
            Runs.Add(run);
            return Task.CompletedTask;
        }

        public Task<bool> HasReconciliationAsync(string providerId, DateOnly businessDate, CancellationToken cancellationToken) => Task.FromResult(Runs.Count > 0);

        public Task<IReadOnlyList<ReconciliationRun>> ListReconciliationsAsync(string? providerId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReconciliationRun>>(Runs);
    }
}
