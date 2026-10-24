#nullable enable

/// <summary>
/// Local mirror of the user's Premium subscription as reported by the billing
/// gateway. No payment-method data is stored — only gateway identifiers and
/// period bookkeeping.
/// </summary>
public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public string GatewaySubscriptionId { get; set; } = string.Empty;
    public string? GatewayCustomerId { get; set; }
    public string PlanSlug { get; set; } = string.Empty;
    public Plan? Plan { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Incomplete;
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    /// <summary>Set when the gateway reported a charge refund (QA-B4): access was
    /// revoked because the paid charge was refunded.</summary>
    public bool Refunded { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum SubscriptionStatus
{
    Incomplete = 0,
    IncompleteExpired = 1,
    Trialing = 2,
    Active = 3,
    PastDue = 4,
    Canceled = 5,
    Unpaid = 6,
    Paused = 7,
}
