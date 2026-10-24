#nullable enable

/// <summary>
/// A manually pre-approved affiliate offer (supplements / equipment).
/// There is no self-serve: rows are curated by the team (whitelist policy,
/// docs/decision-monetizacion-t_f625fb1c.md). DisplayName/Description must
/// never contain health or nutrition claims (Reg. CE 1924/2006, RD 1907/1996).
/// </summary>
public class SponsoredOffer
{
    public int Id { get; set; }

    /// <summary>Normalized slug exposed to clients (e.g. "creatina-monohidrato").</summary>
    public string Slug { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Neutral commercial copy only — curated, never a claim.</summary>
    public string Description { get; set; } = string.Empty;

    public SponsoredOfferCategory Category { get; set; }

    /// <summary>Affiliate destination URL; its domain must be in the configured allowlist.</summary>
    public string ExternalUrl { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public List<SponsoredEvent> Events { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum SponsoredOfferCategory
{
    Supplement,
    Equipment
}
