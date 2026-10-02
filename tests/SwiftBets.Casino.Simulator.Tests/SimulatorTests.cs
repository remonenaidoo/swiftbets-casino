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
