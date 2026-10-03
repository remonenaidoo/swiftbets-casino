using System.Net;

namespace SwiftBets.Casino.Domain.Tests;

public sealed class PragmaticTests
{
    private static readonly Dictionary<string, string> Request = new() { ["userId"] = "u1", ["reference"] = "r1", ["amount"] = "10.00" };

    [Fact]
    public void The_hash_is_md5_of_the_sorted_fields_with_the_secret_appended_and_verifies()
    {
        var signed = new Dictionary<string, string>(Request) { ["hash"] = "8cca69f3e6308d4b1f4adb2924c97b6e" };

        PragmaticHash.Compute(Request, "testKey").ShouldBe("8cca69f3e6308d4b1f4adb2924c97b6e");
        PragmaticHash.Verify(signed, "testKey").ShouldBeTrue();
    }

    [Fact]
    public void A_tampered_amount_a_missing_hash_or_a_missing_secret_does_not_verify()
    {
        var tampered = new Dictionary<string, string>(Request) { ["amount"] = "1000.00", ["hash"] = "8cca69f3e6308d4b1f4adb2924c97b6e" };

        PragmaticHash.Verify(tampered, "testKey").ShouldBeFalse();
        PragmaticHash.Verify(Request, "testKey").ShouldBeFalse();
        PragmaticHash.Verify(new Dictionary<string, string>(Request) { ["hash"] = "8cca69f3e6308d4b1f4adb2924c97b6e" }, null).ShouldBeFalse();
    }

    [Fact]
    public void The_allowlist_admits_addresses_inside_its_blocks_including_mapped_ipv4()
    {
        var list = new AddressAllowlist(["10.20.0.0/16", "192.0.2.7"]);

        list.Allows(IPAddress.Parse("10.20.33.4")).ShouldBeTrue();
        list.Allows(IPAddress.Parse("192.0.2.7").MapToIPv6()).ShouldBeTrue();
    }

    [Fact]
    public void The_allowlist_refuses_outsiders_and_ignores_garbage_rather_than_opening_up()
    {
        var list = new AddressAllowlist(["10.20.0.0/16", "not-an-address/99"]);

        list.Allows(IPAddress.Parse("10.21.0.1")).ShouldBeFalse();
        list.Allows(null).ShouldBeFalse();
        new AddressAllowlist([]).Allows(IPAddress.Loopback).ShouldBeFalse();
    }
}
