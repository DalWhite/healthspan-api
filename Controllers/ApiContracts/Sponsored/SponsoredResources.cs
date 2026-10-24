#nullable enable

/// <summary>
/// A sponsored slot inserted alongside organic recommendations. The visible
/// "Patrocinado" label is mandatory and non-removable (DSA transparency):
/// it must never be confused with an organic recommendation.
/// </summary>
public sealed class SponsoredSlotResource
{
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ExternalUrl { get; set; } = string.Empty;

    /// <summary>Mandatory visible label. Always present on a sponsored slot.</summary>
    public string Label { get; set; } = "Patrocinado";

    /// <summary>Explicit machine-readable flag so clients can never render it as organic.</summary>
    public bool Sponsored { get; set; } = true;
}

public sealed class SponsoredEventRequest
{
    public string Slug { get; set; } = string.Empty;

    /// <summary>click | dismiss | conversion. Impressions are recorded server-side only.</summary>
    public string Type { get; set; } = string.Empty;
}

public sealed class SponsoredMetricsResource
{
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Active { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public int Dismisses { get; set; }
    public int Conversions { get; set; }
}
