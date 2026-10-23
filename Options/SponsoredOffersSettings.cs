using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace healthspan_api.Options;

/// <summary>
/// Sponsored-offer policy (docs/decision-monetizacion-t_f625fb1c.md §3).
/// Feature flag is OFF by default: activation only when real pre-approved
/// affiliates exist. All limits are configurable.
/// </summary>
public class SponsoredOffersSettings
{
    /// <summary>Master feature flag. OFF by default (P2, must not block anything).</summary>
    [ConfigurationKeyName("SponsoredOffersEnabled")]
    public bool Enabled { get; set; }

    /// <summary>Minimum organic recommendations delivered since the last sponsored slot.</summary>
    [ConfigurationKeyName("SponsoredOffersSlotEveryRecommendations")]
    public int SlotEveryRecommendations { get; set; } = 10;

    /// <summary>Hard daily cap of sponsored impressions per user.</summary>
    [ConfigurationKeyName("SponsoredOffersMaxPerUserPerDay")]
    public int MaxPerUserPerDay { get; set; } = 1;

    /// <summary>Minimum age to receive any sponsored slot (minors are excluded).</summary>
    [ConfigurationKeyName("SponsoredOffersMinAge")]
    public int MinAge { get; set; } = 18;

    /// <summary>Allowlist of affiliate destination domains (exact host match, case-insensitive).</summary>
    [ConfigurationKeyName("SponsoredOffersAllowedDomains")]
    public List<string> AllowedDomains { get; set; } = new();

    /// <summary>Emails allowed to read aggregated sponsored metrics. Empty = nobody.</summary>
    [ConfigurationKeyName("SponsoredOffersMetricsEmails")]
    public List<string> MetricsEmails { get; set; } = new();
}
