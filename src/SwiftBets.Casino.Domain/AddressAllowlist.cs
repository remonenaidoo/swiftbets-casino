using System.Net;

namespace SwiftBets.Casino.Domain;

/// <summary>
/// The networks a provider may call us from, as CIDR blocks or single addresses. Anything unparsable is ignored,
/// so a typo narrows the list rather than opening it; an empty list allows nobody.
/// </summary>
public sealed class AddressAllowlist
{
    private readonly IReadOnlyList<IPNetwork> _networks;

    public AddressAllowlist(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _networks = [.. entries.Select(Parse).OfType<IPNetwork>()];
    }

    public static IPNetwork? Parse(string entry)
    {
        var text = entry?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (IPNetwork.TryParse(text, out var network))
        {
            return network;
        }

        return IPAddress.TryParse(text, out var address) ? new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128) : null;
    }

    public bool Allows(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return _networks.Any(n => n.Contains(plain));
    }
}
