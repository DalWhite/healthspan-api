#nullable enable

using healthspan_api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Services;

/// <summary>
/// Orchestrates billing state for the current user: checkout sessions (idempotent
/// per Idempotency-Key), the local subscription mirror, receipts, and webhook
/// processing (deduped by gateway event id). All provider specifics go through
/// <see cref="IBillingGateway"/>.
/// </summary>
public sealed class BillingService(
    MainDbContext db,
    IBillingGateway gateway,
    IOptions<StripeSettings> stripeSettings,
    ILogger<BillingService> logger)
{
    public async Task<CheckoutSessionRequest> CreateCheckoutSessionAsync(
        string userId,
        string planSlug,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await db.CheckoutSessionRequests
            .Where(c => c.UserId == userId && c.IdempotencyKey == idempotencyKey)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            // QA-B2: a replayed key must match the original payload. Returning the
            // original session for a different plan would charge a plan the user
            // never asked for.
            if (!string.Equals(existing.PlanSlug, planSlug, StringComparison.Ordinal))
            {
                throw new IdempotencyConflictException(idempotencyKey, existing.PlanSlug, planSlug);
            }

            return existing;
        }

        var plan = await db.Plans
            .Where(p => p.Slug == planSlug && p.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            throw new PlanNotFoundException(planSlug);
        }

        // QA-B3 (product decision): one live subscription per user. A second
        // checkout while the mirror is trialing/active/past_due would orphan the
        // previous Stripe subscription (double billing). The user must cancel
        // first; in-app plan switching is Fase 2.
        var liveSubscription = await db.Subscriptions
            .FirstOrDefaultAsync(
                s => s.UserId == userId
                     && (s.Status == SubscriptionStatus.Trialing
                         || s.Status == SubscriptionStatus.Active
                         || s.Status == SubscriptionStatus.PastDue),
                cancellationToken);

        if (liveSubscription is not null)
        {
            throw new ActiveSubscriptionException(liveSubscription.Status);
        }

        if (!gateway.IsConfigured)
        {
            throw new BillingConfigurationException();
        }

        stripeSettings.Value.PriceIds.TryGetValue(plan.Slug, out var priceId);

        var session = await gateway.CreateCheckoutSessionAsync(
            userId,
            plan.Slug,
            priceId ?? string.Empty,
            idempotencyKey,
            cancellationToken);

        var record = new CheckoutSessionRequest
        {
            UserId = userId,
            PlanSlug = plan.Slug,
            IdempotencyKey = idempotencyKey,
            SessionId = session.SessionId,
            CheckoutUrl = session.CheckoutUrl,
        };

        db.CheckoutSessionRequests.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        return record;
    }

    public async Task<Subscription?> GetSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (subscription is null || !gateway.IsConfigured || string.IsNullOrWhiteSpace(subscription.GatewaySubscriptionId))
        {
            return subscription;
        }

        // Best-effort refresh so the UI never shows a stale period end after a
        // missed webhook. A gateway failure falls back to the local mirror.
        try
        {
            var snapshot = await gateway.GetSubscriptionAsync(
                subscription.GatewaySubscriptionId,
                cancellationToken);

            if (snapshot is null)
            {
                // QA-B14: a gateway lookup miss (e.g. a test/live API-key mismatch)
                // is NOT evidence of cancellation. Never mutate local state on a
                // miss — log and serve the local mirror. Only an explicit
                // customer.subscription.deleted webhook cancels the mirror.
                logger.LogWarning(
                    "Billing gateway lookup missed subscription {SubscriptionId}; serving local state without mutation.",
                    subscription.GatewaySubscriptionId);
                return subscription;
            }

            ApplySnapshot(subscription, snapshot);

            subscription.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Billing gateway refresh failed for subscription {SubscriptionId}; serving local state.", subscription.GatewaySubscriptionId);
        }

        return subscription;
    }

    public async Task<Subscription> CancelSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (subscription is null)
        {
            throw new SubscriptionNotFoundException();
        }

        if (gateway.IsConfigured && !string.IsNullOrWhiteSpace(subscription.GatewaySubscriptionId))
        {
            var snapshot = await gateway.CancelSubscriptionAsync(
                subscription.GatewaySubscriptionId,
                cancellationToken);

            ApplySnapshot(subscription, snapshot);
        }
        else
        {
            subscription.CancelAtPeriodEnd = true;
        }

        subscription.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyList<GatewayReceipt>> ListReceiptsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (subscription is null || string.IsNullOrWhiteSpace(subscription.GatewayCustomerId))
        {
            return [];
        }

        if (!gateway.IsConfigured)
        {
            throw new BillingConfigurationException();
        }

        return await gateway.ListReceiptsAsync(subscription.GatewayCustomerId, cancellationToken);
    }

    /// <summary>
    /// Processes one webhook delivery. Returns "duplicate" when the same gateway
    /// event was already processed (Stripe retry), "processed" when it changed
    /// state, and "ignored" for event types the MVP does not act on.
    /// </summary>
    public async Task<string> HandleWebhookAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken)
    {
        var parsed = gateway.ParseWebhookEvent(payload, signatureHeader);

        var alreadyProcessed = await db.BillingEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.GatewayEventId == parsed.EventId, cancellationToken);

        if (alreadyProcessed)
        {
            return "duplicate";
        }

        var userId = ResolveWebhookUserId(parsed);

        var billingEvent = new BillingEvent
        {
            GatewayEventId = parsed.EventId,
            EventType = parsed.EventType,
            PayloadHash = BillingEvent.HashPayload(payload),
            UserId = userId,
        };

        db.BillingEvents.Add(billingEvent);

        var applied = parsed.EventType switch
        {
            "checkout.session.completed" => await ApplyCheckoutCompletedAsync(parsed, cancellationToken),
            "customer.subscription.created" or "customer.subscription.updated" or "customer.subscription.deleted" => await ApplySubscriptionUpdateAsync(parsed, cancellationToken),
            "charge.refunded" => await ApplyChargeRefundedAsync(parsed, cancellationToken),
            "invoice.payment_failed" => await ApplyInvoicePaymentFailedAsync(parsed, cancellationToken),
            _ => false,
        };

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent delivery of the same event id.
            return "duplicate";
        }

        return applied ? "processed" : "ignored";
    }

    private string? ResolveWebhookUserId(ParsedGatewayEvent parsed)
    {
        if (parsed.MetadataUserId is not null)
        {
            return parsed.MetadataUserId;
        }

        if (parsed.Subscription is not null)
        {
            return db.Subscriptions
                .IgnoreQueryFilters()
                .Where(s => s.GatewaySubscriptionId == parsed.Subscription.GatewaySubscriptionId)
                .Select(s => s.UserId)
                .FirstOrDefault();
        }

        return null;
    }

    private async Task<bool> ApplyCheckoutCompletedAsync(
        ParsedGatewayEvent parsed,
        CancellationToken cancellationToken)
    {
        if (parsed.Subscription is null)
        {
            return false;
        }

        var userId = parsed.MetadataUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("Checkout webhook {EventId} has no userId metadata.", parsed.EventId);
            return false;
        }

        var planSlug = parsed.MetadataPlanSlug ?? string.Empty;
        var plan = await db.Plans
            .Where(p => p.Slug == planSlug)
            .FirstOrDefaultAsync(cancellationToken);

        var subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                s => s.GatewaySubscriptionId == parsed.Subscription.GatewaySubscriptionId
                     || s.UserId == userId,
                cancellationToken);

        if (subscription is null)
        {
            subscription = new Subscription
            {
                UserId = userId,
                GatewaySubscriptionId = parsed.Subscription.GatewaySubscriptionId,
            };
            db.Subscriptions.Add(subscription);
        }

        // A re-subscription after cancellation reuses the user's single row and
        // points it at the new gateway subscription.
        subscription.GatewaySubscriptionId = parsed.Subscription.GatewaySubscriptionId;
        subscription.GatewayCustomerId = parsed.Subscription.GatewayCustomerId;
        subscription.PlanSlug = planSlug;
        subscription.Plan = plan;
        subscription.UpdatedAt = DateTime.UtcNow;

        return true;
    }

    private async Task<bool> ApplySubscriptionUpdateAsync(
        ParsedGatewayEvent parsed,
        CancellationToken cancellationToken)
    {
        if (parsed.Subscription is null)
        {
            return false;
        }

        var subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                s => s.GatewaySubscriptionId == parsed.Subscription.GatewaySubscriptionId,
                cancellationToken);

        if (subscription is null)
        {
            logger.LogWarning(
                "Subscription webhook {EventId} references unknown subscription {SubscriptionId}.",
                parsed.EventId,
                parsed.Subscription.GatewaySubscriptionId);
            return false;
        }

        ApplySnapshot(subscription, parsed.Subscription);
        subscription.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// QA-B4: a refunded charge revokes Premium. The local mirror goes to
    /// canceled with the Refunded flag set; access must not stay active.
    /// </summary>
    private async Task<bool> ApplyChargeRefundedAsync(
        ParsedGatewayEvent parsed,
        CancellationToken cancellationToken)
    {
        var subscription = await FindSubscriptionForEventAsync(parsed, cancellationToken);

        if (subscription is null)
        {
            logger.LogWarning(
                "Refund webhook {EventId} references unknown subscription/customer {CustomerId}.",
                parsed.EventId,
                parsed.GatewayCustomerId);
            return false;
        }

        subscription.Status = SubscriptionStatus.Canceled;
        subscription.Refunded = true;
        subscription.CancelAtPeriodEnd = true;
        subscription.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// QA-B5: a failed renewal moves the mirror to past_due (UX state 'pago
    /// fallado', spec A9). The subscription stays recoverable — Stripe retries
    /// the payment; a later customer.subscription.updated mirrors the outcome.
    /// </summary>
    private async Task<bool> ApplyInvoicePaymentFailedAsync(
        ParsedGatewayEvent parsed,
        CancellationToken cancellationToken)
    {
        var subscription = await FindSubscriptionForEventAsync(parsed, cancellationToken);

        if (subscription is null)
        {
            logger.LogWarning(
                "Payment-failed webhook {EventId} references unknown subscription/customer {CustomerId}.",
                parsed.EventId,
                parsed.GatewayCustomerId);
            return false;
        }

        subscription.Status = SubscriptionStatus.PastDue;
        subscription.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private async Task<Subscription?> FindSubscriptionForEventAsync(
        ParsedGatewayEvent parsed,
        CancellationToken cancellationToken)
    {
        if (parsed.Subscription is not null)
        {
            var byGatewayId = await db.Subscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    s => s.GatewaySubscriptionId == parsed.Subscription.GatewaySubscriptionId,
                    cancellationToken);

            if (byGatewayId is not null)
            {
                return byGatewayId;
            }
        }

        if (!string.IsNullOrWhiteSpace(parsed.GatewayCustomerId))
        {
            return await db.Subscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    s => s.GatewayCustomerId == parsed.GatewayCustomerId,
                    cancellationToken);
        }

        return null;
    }

    private static void ApplySnapshot(Subscription subscription, GatewaySubscriptionSnapshot snapshot)
    {
        subscription.Status = ParseStatus(snapshot.Status);
        subscription.CurrentPeriodEnd = snapshot.CurrentPeriodEnd;
        subscription.CancelAtPeriodEnd = snapshot.CancelAtPeriodEnd;

        if (!string.IsNullOrWhiteSpace(snapshot.GatewayCustomerId))
        {
            subscription.GatewayCustomerId = snapshot.GatewayCustomerId;
        }
    }

    private static SubscriptionStatus ParseStatus(string status) => status switch
    {
        "active" => SubscriptionStatus.Active,
        "trialing" => SubscriptionStatus.Trialing,
        "past_due" => SubscriptionStatus.PastDue,
        "canceled" => SubscriptionStatus.Canceled,
        "unpaid" => SubscriptionStatus.Unpaid,
        "paused" => SubscriptionStatus.Paused,
        "incomplete_expired" => SubscriptionStatus.IncompleteExpired,
        _ => SubscriptionStatus.Incomplete,
    };
}

public class PlanNotFoundException(string planSlug)
    : Exception($"Plan '{planSlug}' does not exist.");

public class IdempotencyConflictException(string idempotencyKey, string originalPlan, string requestedPlan)
    : Exception($"The Idempotency-Key '{idempotencyKey}' was already used for plan '{originalPlan}'; '{requestedPlan}' requires a new key.");

public class ActiveSubscriptionException(SubscriptionStatus status)
    : Exception($"This account already has a Premium subscription ({status.ToString().ToLowerInvariant()}). Cancel it before starting a new checkout.");

public class SubscriptionNotFoundException()
    : Exception("No subscription found for this user.");

public class BillingConfigurationException()
    : Exception("Billing provider is not configured.");
