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

    /// <summary>Keyed by provider id: its signing secret and where its games are launched.</summary>
    public Dictionary<string, ProviderOptions> Providers { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ProviderOptions
{
    public string Secret { get; set; } = string.Empty;

    public string LaunchBaseUrl { get; set; } = string.Empty;

    /// <summary>Where the provider serves its daily report; reconciliation skips a provider without one.</summary>
    public string ReportUrl { get; set; } = string.Empty;
}
