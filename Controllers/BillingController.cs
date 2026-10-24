#nullable enable

using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

/// <summary>
/// Healthspan Premium billing (Fase 1). Stripe Checkout hosted sessions; the
/// platform never sees card data. All endpoints act only on the authenticated
/// user (`me`); the webhook is the single unauthenticated entry point and is
/// protected by Stripe signature verification.
/// See docs/billing-api-t_c0e8a770.md for the full contract.
/// </summary>
[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly BillingService _billingService;
    private readonly CurrentUserProvider _userProvider;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        BillingService billingService,
        CurrentUserProvider userProvider,
        ILogger<BillingController> logger)
    {
        _billingService = billingService;
        _userProvider = userProvider;
        _logger = logger;
    }

    private string? CurrentUserId => _userProvider.GetCurrentUserId();

    /// <summary>
    /// Creates a Stripe Checkout session for a Premium plan. Requires an
    /// Idempotency-Key header: replaying the same key returns the original
    /// session instead of creating a new one.
    /// </summary>
    [Authorize]
    [HttpPost("checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession(
        [FromBody] CreateCheckoutSessionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > 255)
        {
            return ApiProblemResult.Validation(
                "idempotency_key_required",
                "Send an Idempotency-Key header (8-255 characters) so retries do not create duplicate checkouts.");
        }

        if (string.IsNullOrWhiteSpace(request.Plan))
        {
            return ApiProblemResult.Validation("plan_required", "Field 'plan' is required (e.g. 'premium-monthly').");
        }

        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var session = await _billingService.CreateCheckoutSessionAsync(
                userId,
                request.Plan,
                idempotencyKey,
                cancellationToken);

            return Ok(new CheckoutSessionResource
            {
                SessionId = session.SessionId,
                CheckoutUrl = session.CheckoutUrl,
                Plan = session.PlanSlug,
            });
        }
        catch (PlanNotFoundException ex)
        {
            return ApiProblemResult.NotFound("plan_not_found", ex.Message);
        }
        catch (IdempotencyConflictException ex)
        {
            return ApiProblemResult.Conflict("idempotency_conflict", ex.Message);
        }
        catch (ActiveSubscriptionException ex)
        {
            return ApiProblemResult.Conflict("subscription_active", ex.Message);
        }
        catch (BillingConfigurationException)
        {
            return BillingNotConfigured();
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to create the checkout session.", ex);
        }
    }

    /// <summary>Returns the current user's Premium subscription state.</summary>
    [Authorize]
    [HttpGet("subscription")]
    public async Task<IActionResult> GetSubscription(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var subscription = await _billingService.GetSubscriptionAsync(userId, cancellationToken);

            if (subscription is null)
            {
                // QA-B6 (spec §30): a free account is a normal state, not an error.
                // TrialBanner/EmptyState build the screen from status "none".
                return Ok(new SubscriptionResource { Status = "none" });
            }

            return Ok(MapSubscription(subscription));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load the subscription.", ex);
        }
    }

    /// <summary>
    /// Cancels the current user's subscription at the end of the paid period.
    /// No automatic refund in the MVP; access lasts until currentPeriodEnd.
    /// </summary>
    [Authorize]
    [HttpPost("subscription/cancel")]
    public async Task<IActionResult> CancelSubscription(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var subscription = await _billingService.CancelSubscriptionAsync(userId, cancellationToken);
            return Ok(MapSubscription(subscription));
        }
        catch (SubscriptionNotFoundException ex)
        {
            return ApiProblemResult.NotFound("subscription_not_found", ex.Message);
        }
        catch (BillingConfigurationException)
        {
            return BillingNotConfigured();
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to cancel the subscription.", ex);
        }
    }

    /// <summary>
    /// Paid invoices (receipts) for the current user's subscription. Receipts are
    /// Stripe-hosted links; no payment data is stored or exposed beyond the owner.
    /// </summary>
    [Authorize]
    [HttpGet("receipts")]
    public async Task<IActionResult> GetReceipts(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var receipts = await _billingService.ListReceiptsAsync(userId, cancellationToken);

            return Ok(receipts.Select(receipt => new ReceiptResource
            {
                Id = receipt.Id,
                Date = receipt.Date,
                AmountCents = receipt.AmountCents,
                ReceiptUrl = receipt.ReceiptUrl,
            }).ToList());
        }
        catch (BillingConfigurationException)
        {
            return BillingNotConfigured();
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load receipts.", ex);
        }
    }

    /// <summary>
    /// Stripe webhook receiver. Verified with the signing secret (HMAC-SHA256 +
    /// timestamp tolerance) and deduplicated by gateway event id, so Stripe
    /// retries are safe. Always answers 2xx for well-formed events so Stripe
    /// does not retry forever.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("webhook/stripe")]
    public async Task<IActionResult> StripeWebhook(CancellationToken cancellationToken)
    {
        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return ApiProblemResult.Validation("empty_payload", "The webhook body is empty.");
        }

        try
        {
            var outcome = await _billingService.HandleWebhookAsync(
                payload,
                Request.Headers["Stripe-Signature"].FirstOrDefault(),
                cancellationToken);

            return Ok(new { received = true, outcome });
        }
        catch (GatewayWebhookException ex)
        {
            _logger.LogWarning("Rejected Stripe webhook: {Reason}", ex.Message);
            return ApiProblemResult.Validation("invalid_webhook_signature", "The webhook signature could not be verified.");
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to process the webhook.", ex);
        }
    }

    /// <summary>API status strings follow Stripe's own snake_case vocabulary
    /// (past_due, incomplete_expired) so the FE can mirror gateway states 1:1.</summary>
    private static string ToApiStatus(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Incomplete => "incomplete",
        SubscriptionStatus.IncompleteExpired => "incomplete_expired",
        SubscriptionStatus.Trialing => "trialing",
        SubscriptionStatus.Active => "active",
        SubscriptionStatus.PastDue => "past_due",
        SubscriptionStatus.Canceled => "canceled",
        SubscriptionStatus.Unpaid => "unpaid",
        SubscriptionStatus.Paused => "paused",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static SubscriptionResource MapSubscription(Subscription subscription) => new()
    {
        Status = ToApiStatus(subscription.Status),
        Plan = subscription.PlanSlug,
        CurrentPeriodEnd = subscription.CurrentPeriodEnd,
        CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
        // QA-B13: TrialBanner needs "Prueba Premium · termina el {fecha}" / "Día X de 7".
        // During the trial the period end IS the trial end.
        TrialEnd = subscription.Status == SubscriptionStatus.Trialing ? subscription.CurrentPeriodEnd : null,
        // QA-B4: expose the refund so the FE can show why access was revoked.
        Refunded = subscription.Refunded,
    };

    private ObjectResult BillingNotConfigured() => new(new ProblemDetails
    {
        Status = StatusCodes.Status503ServiceUnavailable,
        Title = "Billing is not configured.",
        Detail = "Payment provider credentials are not set up in this environment.",
        Type = "https://httpstatuses.com/503",
        Extensions = { ["code"] = "billing_not_configured" },
    })
    {
        StatusCode = StatusCodes.Status503ServiceUnavailable,
    };

    private ObjectResult Unexpected(string title, Exception ex)
    {
        _logger.LogError(ex, "{Title}", title);
        return ApiProblemResult.Unexpected("billing_error");
    }
}

public class CreateCheckoutSessionRequest
{
    public string Plan { get; set; } = string.Empty;
}

public class CheckoutSessionResource
{
    public string SessionId { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
}

public class SubscriptionResource
{
    public string Status { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? TrialEnd { get; set; }
    public bool Refunded { get; set; }
}

public class ReceiptResource
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int AmountCents { get; set; }
    public string? ReceiptUrl { get; set; }
}
