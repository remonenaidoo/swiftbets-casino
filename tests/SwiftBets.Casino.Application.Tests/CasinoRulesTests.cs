using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Casino.Application.Ports;
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

    private static LaunchGameHandler Launcher(FakeStore store, bool ready)
    {
        var options = new CasinoOptions();
        options.Providers["sim-seamless"] = new ProviderOptions { Secret = "s", LaunchBaseUrl = "http://provider.test/" };
        return new LaunchGameHandler(store, new Ready(ready), Options.Create(options), new FakeTimeProvider(Now));
    }

    private static RestrictionsChangedV1 State(params Restriction[] restrictions) =>
        new(Guid.NewGuid(), 1, [], restrictions, null, null, KycStatus.Verified, Now);

    private sealed class Ready(bool ready) : IRestrictions
    {
        public bool IsReady => ready;

        public bool IsCasinoRestricted(Guid punterId, DateTimeOffset now) => false;
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

        public Task<StoredTransaction?> FindTransactionAsync(string providerId, string providerTransactionId, CancellationToken cancellationToken) => Task.FromResult<StoredTransaction?>(null);

        public Task<bool> IsRolledBackAsync(string providerId, string betProviderTransactionId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<RecordOutcome> RecordAsync(StoredTransaction transaction, bool takeFreeSpin, string? markRolledBack, CancellationToken cancellationToken) => Task.FromResult(RecordOutcome.Recorded);

        public Task<FreeSpinGrant> GrantFreeSpinsAsync(Guid punterId, string gameId, int spins, DateTimeOffset expiresAt, Guid operatorId, CancellationToken cancellationToken) =>
            Task.FromResult(new FreeSpinGrant(Guid.NewGuid(), punterId, gameId, spins, spins, expiresAt));

        public Task<IReadOnlyList<FreeSpinGrant>> ListFreeSpinsAsync(Guid punterId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FreeSpinGrant>>([]);
    }
}
