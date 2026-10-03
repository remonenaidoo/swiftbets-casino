using System.ComponentModel.DataAnnotations;

namespace SwiftBets.Casino.Application;

public sealed class CasinoOptions
{
    public const string SectionName = "Casino";

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "ZAR";

    [Range(1, 24)]
    public int SessionHours { get; set; } = 2;

    /// <summary>Base64 of 32 random bytes: the AES-GCM key provider credentials saved from the console are encrypted with.</summary>
    public string CredentialKey { get; set; } = string.Empty;

    /// <summary>The casino catalogue service, which provider game lists are synced into.</summary>
    public string CatalogAddress { get; set; } = string.Empty;

    /// <summary>Keyed by provider id: its signing secret and where its games are launched.</summary>
    public Dictionary<string, ProviderOptions> Providers { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ProviderOptions
{
    public string Secret { get; set; } = string.Empty;

    public string LaunchBaseUrl { get; set; } = string.Empty;

    /// <summary>Where the provider serves its daily report; reconciliation skips a provider without one.</summary>
    public string ReportUrl { get; set; } = string.Empty;

    /// <summary><see cref="ProviderProtocols.Pragmatic"/> for Pragmatic Play's seamless API; empty for our own JSON wallet API.</summary>
    public string Protocol { get; set; } = string.Empty;

    /// <summary>Pragmatic's operator login, sent with every launch and game-list request.</summary>
    public string SecureLogin { get; set; } = string.Empty;

    /// <summary>The provider's server API (launch URLs, game list), called from our side only.</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Where free demo games open: no wallet, no token, nothing at stake.</summary>
    public string DemoBaseUrl { get; set; } = string.Empty;

    /// <summary>Game artwork, as {ImageBaseUrl}/{symbol}.png.</summary>
    public string ImageBaseUrl { get; set; } = string.Empty;

    /// <summary>Who may call the wallet API: CIDR blocks, single addresses, or host names on the internal network.</summary>
    public List<string> AllowedAddresses { get; set; } = [];
}

public static class ProviderProtocols
{
    public const string Pragmatic = "pragmatic";
}
