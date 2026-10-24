#nullable enable

/// <summary>
/// Record of a checkout-session creation keyed by the client's Idempotency-Key.
/// Replaying the same key returns the original session instead of creating a
/// second Stripe session.
/// </summary>
public class CheckoutSessionRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PlanSlug { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
