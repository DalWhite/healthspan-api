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
            return existing;
        }

        var plan = await db.Plans
            .Where(p => p.Slug == planSlug && p.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            throw new PlanNotFoundException(planSlug);
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
                subscription.Status = SubscriptionStatus.Canceled;
            }
            else
            {
                ApplySnapshot(subscription, snapshot);
            }

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

public class SubscriptionNotFoundException()
    : Exception("No subscription found for this user.");

public class BillingConfigurationException()
    : Exception("Billing provider is not configured.");
