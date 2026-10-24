#nullable enable

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Services;

namespace healthspan_api.tests;

/// <summary>
/// In-process stand-in for Stripe used by the regression tests. It behaves like
/// the real gateway for the MVP surface: it records checkout-session calls,
/// keeps a subscription store fed by webhook events, and verifies webhook
/// signatures with a fixed test secret so signature tests exercise the real
/// verification code path.
/// </summary>
public sealed class FakeBillingGateway : IBillingGateway
{
    public const string WebhookSecret = "whsec_test_0123456789";

    private readonly Dictionary<string, GatewaySubscriptionSnapshot> _subscriptions = new();
    private int _sessionCounter;

    public List<(string UserId, string PlanSlug, string IdempotencyKey)> CheckoutCalls { get; } = [];
    public bool Configured { get; set; } = true;

    public bool IsConfigured => Configured;

    public Task<GatewayCheckoutSession> CreateCheckoutSessionAsync(
        string userId,
        string planSlug,
        string priceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        CheckoutCalls.Add((userId, planSlug, idempotencyKey));
        _sessionCounter++;

        var sessionId = $"cs_test_{_sessionCounter:x4}";
        return Task.FromResult(new GatewayCheckoutSession(sessionId, $"https://checkout.stripe.com/c/pay/{sessionId}"));
    }

    public Task<GatewaySubscriptionSnapshot?> GetSubscriptionAsync(
        string gatewaySubscriptionId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(
            _subscriptions.TryGetValue(gatewaySubscriptionId, out var snapshot) ? snapshot : null);

    public Task<GatewaySubscriptionSnapshot> CancelSubscriptionAsync(
        string gatewaySubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var current = _subscriptions.TryGetValue(gatewaySubscriptionId, out var snapshot)
            ? snapshot
            : new GatewaySubscriptionSnapshot(gatewaySubscriptionId, null, "active", DateTime.UtcNow.AddDays(30), false);

        var canceled = current with { CancelAtPeriodEnd = true };
        _subscriptions[gatewaySubscriptionId] = canceled;

        return Task.FromResult(canceled);
    }

    public Task<IReadOnlyList<GatewayReceipt>> ListReceiptsAsync(
        string gatewayCustomerId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GatewayReceipt> receipts =
        [
            new("in_test_1", new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), 999, "https://pay.stripe.com/receipts/in_test_1"),
            new("in_test_2", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), 999, "https://pay.stripe.com/receipts/in_test_2"),
        ];

        return Task.FromResult(receipts);
    }

    public ParsedGatewayEvent ParseWebhookEvent(string payload, string? signatureHeader)
    {
        StripeWebhookVerifier.Verify(payload, signatureHeader, WebhookSecret);

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        var eventId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var eventType = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType))
        {
            throw new GatewayWebhookException("Webhook payload is missing id or type.");
        }

        GatewaySubscriptionSnapshot? subscription = null;
        string? sessionId = null;
        string? metadataUserId = null;
        string? metadataPlanSlug = null;
        string? customerId = null;

        if (root.TryGetProperty("data", out var data) &&
            data.TryGetProperty("object", out var obj) &&
            obj.ValueKind == JsonValueKind.Object)
        {
            sessionId = obj.TryGetProperty("id", out var sessionEl) ? sessionEl.GetString() : null;

            var subscriptionId = obj.TryGetProperty("subscription", out var subEl) ? subEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(subscriptionId) && eventType.StartsWith("customer.subscription"))
            {
                subscriptionId = sessionId;
            }

            customerId = obj.TryGetProperty("customer", out var custEl) ? custEl.GetString() : null;

            if (!string.IsNullOrWhiteSpace(subscriptionId))
            {
                var status = obj.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "incomplete" : "incomplete";
                DateTime? periodEnd = null;
                if (obj.TryGetProperty("current_period_end", out var periodEl) && periodEl.TryGetInt64(out var periodUnix))
                {
                    periodEnd = DateTimeOffset.FromUnixTimeSeconds(periodUnix).UtcDateTime;
                }

                var cancelAtPeriodEnd = obj.TryGetProperty("cancel_at_period_end", out var cancelEl) && cancelEl.GetBoolean();

                subscription = new GatewaySubscriptionSnapshot(subscriptionId, customerId, status, periodEnd, cancelAtPeriodEnd);

                if (eventType.StartsWith("customer.subscription") || eventType == "checkout.session.completed")
                {
                    _subscriptions[subscriptionId] = subscription;
                }
                else if (eventType == "invoice.payment_failed" && _subscriptions.TryGetValue(subscriptionId, out var failedSub))
                {
                    // Real Stripe: a failed renewal flips the subscription object to past_due.
                    _subscriptions[subscriptionId] = failedSub with { Status = "past_due" };
                }
                else if (eventType == "charge.refunded" && _subscriptions.TryGetValue(subscriptionId, out var refundedSub))
                {
                    _subscriptions[subscriptionId] = refundedSub with { Status = "canceled" };
                }
            }

            if (eventType == "charge.refunded" && string.IsNullOrWhiteSpace(subscriptionId) && !string.IsNullOrWhiteSpace(customerId))
            {
                // Stripe charges reference the customer; refunding revokes the customer's
                // stored subscription too.
                foreach (var key in _subscriptions.Keys.ToList())
                {
                    if (_subscriptions[key].GatewayCustomerId == customerId)
                    {
                        _subscriptions[key] = _subscriptions[key] with { Status = "canceled" };
                    }
                }
            }

            if (obj.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
            {
                metadataUserId = metadata.TryGetProperty("userId", out var userIdEl) ? userIdEl.GetString() : null;
                metadataPlanSlug = metadata.TryGetProperty("planSlug", out var planSlugEl) ? planSlugEl.GetString() : null;
            }
        }

        return new ParsedGatewayEvent(eventId, eventType, payload, subscription, sessionId, metadataUserId, metadataPlanSlug, customerId);
    }

    /// <summary>Builds a valid Stripe-Signature header for a payload (t + HMAC v1).</summary>
    public static string Sign(string payload, string secret = WebhookSecret, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        // Stripe emits v1 as lowercase hex; the fake gateway must match the real format.
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{payload}"))).ToLowerInvariant();
        return $"t={ts},v1={signature}";
    }
}
