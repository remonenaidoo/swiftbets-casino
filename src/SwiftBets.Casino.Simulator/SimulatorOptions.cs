using System.ComponentModel.DataAnnotations;

namespace SwiftBets.Casino.Simulator;

public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    /// <summary>The casino gateway's base address, where the wallet API lives.</summary>
    [Required]
    public string GatewayAddress { get; set; } = string.Empty;

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "ZAR";

    /// <summary>Keyed by provider id: the secret each simulated provider signs its calls with.</summary>
    public Dictionary<string, SimulatedProvider> Providers { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SimulatedProvider
{
    public string Secret { get; set; } = string.Empty;
}
