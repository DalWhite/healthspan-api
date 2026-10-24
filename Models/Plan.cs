#nullable enable

/// <summary>
/// A purchasable Healthspan Premium plan. Prices are stored in the smallest
/// currency unit (cents); Stripe price ids live in configuration, never in the DB.
/// </summary>
public class Plan
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int PriceCents { get; set; }
    public string Currency { get; set; } = "eur";
    public BillingInterval Interval { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum BillingInterval
{
    Monthly = 0,
    Annual = 1,
}
