extern alias simulator;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Api.Tests;

public sealed class GatewayApiTests : IClassFixture<GatewayApiTests.Host>
{
    private static readonly Guid Excluded = Guid.NewGuid();
    private readonly Host _host;

    public GatewayApiTests(Host host) => _host = host;

    [Fact]
    public async Task A_callback_with_a_wrong_signature_is_refused_before_anything_is_read()
    {
        using var client = _host.CreateClient();
        using var body = new StringContent("""{"sessionToken":"x","providerTransactionId":"p","roundId":"r","amount":100}""", Encoding.UTF8, "application/json");
        body.Headers.Add("X-Provider-Signature", ProviderSignature.Compute("not-the-secret", Encoding.UTF8.GetBytes("""{"sessionToken":"x","providerTransactionId":"p","roundId":"r","amount":100}""")));

        using var response = await client.PostAsync(new Uri("/providers/sim-seamless/wallet/bet", UriKind.Relative), body, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_self_excluded_punter_cannot_launch_a_game()
    {
        using var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Issue(Excluded.ToString(), "Punter"));

        using var response = await client.PostAsJsonAsync("/casino/launch", new { gameId = "sun-temple", providerId = "sim-seamless" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("casino_restricted");
    }

    [Fact]
    public async Task A_callback_signed_by_the_simulator_passes_the_gateway_signature_check()
    {
        var gateway = new simulator::SwiftBets.Casino.Simulator.GatewayClient(_host.CreateClient(),
            Options.Create(new simulator::SwiftBets.Casino.Simulator.SimulatorOptions { GatewayAddress = "http://localhost", Providers = { ["sim-seamless"] = new() { Secret = "the-secret" } } }),
            new simulator::SwiftBets.Casino.Simulator.Faults());

        var reply = await gateway.CallAsync("sim-seamless", "bet", new { sessionToken = "unknown", providerTransactionId = "p-1", roundId = "r-1", gameId = "sun-temple", amount = 100, currency = "ZAR" },
            TestContext.Current.CancellationToken);

        reply.Status.ShouldBe("session_invalid");
    }

    public sealed class Host : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:Authority", TestJwt.Issuer);
            builder.UseSetting("ConnectionStrings:SbCasino", "Server=127.0.0.1,1;Database=x;User Id=x;Password=x;Encrypt=false;Connect Timeout=1");
            builder.UseSetting("Kafka:BootstrapServers", "127.0.0.1:1");
            builder.UseSetting("Kafka:Environment", "test");
            builder.UseSetting("Kafka:ClientId", "casino.tests");
            builder.UseSetting("Wallet:GrpcAddress", "http://127.0.0.1:1");
            builder.UseSetting("ServiceIdentity:TokenEndpoint", "http://127.0.0.1:1/auth/token");
            builder.UseSetting("ServiceIdentity:ClientId", "casino");
            builder.UseSetting("ServiceIdentity:ClientSecret", "test");
            builder.UseSetting("Casino:Providers:sim-seamless:Secret", "the-secret");
            builder.UseSetting("Casino:Providers:sim-seamless:LaunchBaseUrl", "http://provider.test");
            builder.UseSetting("Casino:Reconciliation:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                services.UseTestJwt();
                services.RemoveAll<IRestrictions>();
                services.AddSingleton<IRestrictions>(new ExcludedOne(Excluded));
                services.RemoveAll<ICasinoStore>();
                services.AddSingleton<ICasinoStore, NoSessions>();
            });
        }
    }

    private sealed class ExcludedOne(Guid excluded) : IRestrictions
    {
        public Task<RestrictionCheck> CheckAsync(Guid punterId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(punterId == excluded ? RestrictionCheck.Restricted : RestrictionCheck.Clear);
    }

    /// <summary>A store that knows no sessions, so a correctly signed callback is answered with session_invalid.</summary>
    private sealed class NoSessions : ICasinoStore
    {
        public Task<bool> IsProviderEnabledAsync(string providerId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<string?> GetWalletModelAsync(string providerId, CancellationToken cancellationToken) => Task.FromResult<string?>(WalletModels.Seamless);

        public Task CreateSessionAsync(GameSession session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<GameSession?> FindSessionAsync(byte[] tokenHash, CancellationToken cancellationToken) => Task.FromResult<GameSession?>(null);

        public Task<StoredTransaction?> FindTransactionAsync(string providerId, string providerTransactionId, CancellationToken cancellationToken) => Task.FromResult<StoredTransaction?>(null);

        public Task<bool> IsRolledBackAsync(string providerId, string betProviderTransactionId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<RecordOutcome> RecordAsync(StoredTransaction transaction, bool takeFreeSpin, string? markRolledBack, CancellationToken cancellationToken) => Task.FromResult(RecordOutcome.Recorded);

        public Task<FreeSpinGrant> GrantFreeSpinsAsync(Guid punterId, string gameId, int spins, DateTimeOffset expiresAt, Guid operatorId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FreeSpinGrant>> ListFreeSpinsAsync(Guid punterId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FreeSpinGrant>>([]);

        public Task<IReadOnlyList<ReportedTransaction>> ListTransactionsAsync(string providerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportedTransaction>>([]);

        public Task RecordReconciliationAsync(ReconciliationRun run, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> HasReconciliationAsync(string providerId, DateOnly businessDate, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyList<ReconciliationRun>> ListReconciliationsAsync(string? providerId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReconciliationRun>>([]);
    }
}
