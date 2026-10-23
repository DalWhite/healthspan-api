#nullable enable

namespace Services;

/// <summary>
/// Internal abstraction over the payment provider. The rest of the application
/// only talks to this interface; Stripe is just one implementation and can be
/// replaced without touching controllers, services or the DB model.
/// No card data ever crosses this boundary.
/// </summary>
public interface IBillingGateway
{
    bool IsConfigured { get; }

    /// <summary>Creates a hosted checkout session for a subscription plan.</summary>
    Task<GatewayCheckoutSession> CreateCheckoutSessionAsync(
        string userId,
        string planSlug,
        string priceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<GatewaySubscriptionSnapshot?> GetSubscriptionAsync(
        string gatewaySubscriptionId,
        CancellationToken cancellationToken = default);

    Task<GatewaySubscriptionSnapshot> CancelSubscriptionAsync(
        string gatewaySubscriptionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GatewayReceipt>> ListReceiptsAsync(
        string gatewayCustomerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the webhook signature and parses the event. Throws
    /// <see cref="GatewayWebhookException"/> when the signature is invalid or the
    /// timestamp is outside tolerance.
    /// </summary>
    ParsedGatewayEvent ParseWebhookEvent(
        string payload,
        string? signatureHeader);
}

public sealed record GatewayCheckoutSession(string SessionId, string CheckoutUrl);

public sealed record GatewaySubscriptionSnapshot(
    string GatewaySubscriptionId,
    string? GatewayCustomerId,
    string Status,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

public sealed record GatewayReceipt(
    string Id,
    DateTime Date,
    int AmountCents,
    string? ReceiptUrl);

public sealed record ParsedGatewayEvent(
    string EventId,
    string EventType,
    string Payload,
    GatewaySubscriptionSnapshot? Subscription,
    string? SessionId = null,
    string? MetadataUserId = null,
    string? MetadataPlanSlug = null);

/// <summary>Signature verification failed or the event was malformed.</summary>
public class GatewayWebhookException : Exception
{
    public GatewayWebhookException(string message) : base(message) { }
}
