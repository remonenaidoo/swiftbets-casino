using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            builder.ConfigureServices(services =>
            {
                services.UseTestJwt();
                services.RemoveAll<IRestrictions>();
                services.AddSingleton<IRestrictions>(new ExcludedOne(Excluded));
            });
        }
    }

    private sealed class ExcludedOne(Guid excluded) : IRestrictions
    {
        public bool IsReady => true;

        public bool IsCasinoRestricted(Guid punterId, DateTimeOffset now) => punterId == excluded;
    }
}
