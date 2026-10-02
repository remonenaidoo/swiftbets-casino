using Microsoft.Extensions.Options;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Simulator.Tests;

public sealed class SimulatorTests
{
    private const string Secret = "sim-seamless-test-secret";

    [Fact]
    public async Task A_callback_the_simulator_signs_verifies_with_the_gateway_check()
    {
        using var request = Client().Sign("sim-seamless", "bet", """{"amount":500}"""u8.ToArray());

        var body = await request.Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        request.RequestUri!.AbsolutePath.ShouldBe("/providers/sim-seamless/wallet/bet");
        ProviderSignature.Verify(Secret, body, request.Headers.GetValues(GatewayClient.SignatureHeader).Single()).ShouldBeTrue();
    }

    [Fact]
    public void A_tampered_body_no_longer_verifies()
    {
        using var request = Client().Sign("sim-seamless", "win", """{"amount":500}"""u8.ToArray());

        ProviderSignature.Verify(Secret, """{"amount":50000}"""u8, request.Headers.GetValues(GatewayClient.SignatureHeader).Single()).ShouldBeFalse();
    }

    [Fact]
    public void Three_sevens_pay_fifty_times_and_two_cherries_pay_double() =>
        (Games.SlotMultiplier(["7", "7", "7"]), Games.SlotMultiplier(["CHERRY", "LEMON", "CHERRY"])).ShouldBe((50, 2));

    [Fact]
    public void A_mixed_line_pays_nothing() => Games.SlotMultiplier(["7", "BAR", "LEMON"]).ShouldBe(0);

    private static GatewayClient Client() =>
        new(new HttpClient(), Options.Create(new SimulatorOptions { GatewayAddress = "http://casino:8080", Providers = { ["sim-seamless"] = new SimulatedProvider { Secret = Secret } } }), new Faults());
}

public sealed class PlayPageTests
{
    private static readonly string Page = Read();

    [Fact]
    public void The_page_calls_its_endpoints_relative_to_where_it_is_served()
    {
        Page.ShouldContain("post('play/start'");
        Page.ShouldContain("post('play/spin'");
        Page.ShouldContain("post('play/cashout'");
    }

    [Fact]
    public void The_page_sends_the_gateway_csrf_header_because_the_browser_sends_the_session_cookie() =>
        Page.ShouldContain("'X-SwiftBets-Csrf': '1'");

    [Fact]
    public void The_page_never_calls_a_root_path_or_another_origin()
    {
        Page.ShouldNotContain("'/play");
        Page.ShouldNotContain("http://");
        Page.ShouldNotContain("https://");
        Page.ShouldNotContain("window.top");
        Page.ShouldNotContain("window.open");
    }

    [Fact]
    public void The_site_can_frame_the_page_and_its_own_script_runs()
    {
        var (html, policy) = GamePage.Render(Page, "n0nce");

        html.ShouldContain("<script nonce=\"n0nce\">");
        policy.ShouldContain("script-src 'nonce-n0nce'");
        policy.ShouldContain("frame-ancestors 'self'");
    }

    [Fact]
    public void No_other_origin_can_frame_it_and_no_injected_script_runs()
    {
        var (_, policy) = GamePage.Render(Page, "n0nce");

        policy.Split(';').Single(d => d.Trim().StartsWith("frame-ancestors", StringComparison.Ordinal)).Trim().ShouldBe("frame-ancestors 'self'");
        policy.Split(';').Single(d => d.Trim().StartsWith("script-src", StringComparison.Ordinal)).ShouldNotContain("unsafe-inline");
    }

    private static string Read()
    {
        using var stream = typeof(Faults).Assembly.GetManifestResourceStream("SwiftBets.Casino.Simulator.wwwroot.play.html")!;
        return new StreamReader(stream).ReadToEnd();
    }
}
