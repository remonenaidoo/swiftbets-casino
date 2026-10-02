using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Infrastructure.Compliance;
using SwiftBets.Contracts.Compliance;

namespace SwiftBets.Casino.Infrastructure.Tests;

public sealed class ComplianceRestrictionsTests
{
    [Fact]
    public async Task A_break_compliance_holds_but_the_topic_has_not_delivered_refuses_launch()
    {
        var restrictions = Restrictions(HttpStatusCode.OK, """{"excluded":true,"restrictions":[{"kind":"coolingOff","endsAt":"2030-01-01T00:00:00Z"}]}""");

        (await restrictions.CheckAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).ShouldBe(RestrictionCheck.Restricted);
    }

    [Fact]
    public async Task Compliance_being_down_fails_closed()
    {
        var restrictions = Restrictions(HttpStatusCode.ServiceUnavailable, "{}");

        (await restrictions.CheckAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).ShouldBe(RestrictionCheck.Unavailable);
    }

    private static ComplianceRestrictions Restrictions(HttpStatusCode compliance, string body)
    {
        var handler = new Answer(compliance, body);
        var tokens = new ClientCredentialsTokenProvider(new HttpClient(handler), Options.Create(new ClientCredentialsOptions { TokenEndpoint = "http://identity/connect/token", ClientId = "casino" }), TimeProvider.System);
        return new ComplianceRestrictions(new EmptyTopic(), new Factory(handler), tokens, NullLogger<ComplianceRestrictions>.Instance);
    }

    private sealed class Answer(HttpStatusCode compliance, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"accessToken":"t","expiresIn":600}""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(compliance) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("http://compliance/") };
    }

    private sealed class EmptyTopic : ICompactedState<RestrictionsChangedV1>
    {
        public bool IsReady => true;

        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool TryGet(string key, out RestrictionsChangedV1 value)
        {
            value = null!;
            return false;
        }

        public IReadOnlyDictionary<string, RestrictionsChangedV1> Snapshot() => new Dictionary<string, RestrictionsChangedV1>();
    }
}
