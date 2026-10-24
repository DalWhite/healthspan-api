#nullable enable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Services;

/// <summary>
/// Trainer payment links (Fase 1): the trainer publishes an external payment
/// link (Stripe Payment Links / PayPal, allowlisted) so trainees pay the trainer
/// directly. The platform never touches that money. See
/// docs/billing-api-t_c0e8a770.md.
/// </summary>
[ApiController]
[Route("api/trainers")]
public class TrainersController : ControllerBase
{
    private readonly MainDbContext _db;
    private readonly CurrentUserProvider _userProvider;
    private readonly ILogger<TrainersController> _logger;

    public TrainersController(
        MainDbContext db,
        CurrentUserProvider userProvider,
        ILogger<TrainersController> logger)
    {
        _db = db;
        _userProvider = userProvider;
        _logger = logger;
    }

    private string? CurrentUserId => _userProvider.GetCurrentUserId();

    /// <summary>
    /// Creates or replaces the authenticated trainer's published payment link.
    /// The URL must be HTTPS and belong to the allowlist (checkout.stripe.com,
    /// buy.stripe.com, paypal.me, paypal.com).
    /// </summary>
    [Authorize]
    [HttpPut("me/payment-link")]
    public async Task<IActionResult> PutPaymentLink(
        [FromBody] PaymentLinkRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        // QA-B7 (spec B3): PUT with an empty url removes the published link
        // (same effect as DELETE; 204 on success).
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            var existingLink = await _db.TrainerPaymentLinks
                .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);

            if (existingLink is null)
            {
                return ApiProblemResult.NotFound("payment_link_not_found", "No payment link has been published for this trainer.");
            }

            _db.TrainerPaymentLinks.Remove(existingLink);
            await _db.SaveChangesAsync(cancellationToken);

            return NoContent();
        }

        var verifiedDomain = TrainerPaymentLinkValidator.Validate(request.Url);

        if (verifiedDomain is null)
        {
            return ApiProblemResult.Validation(
                "payment_link_not_allowed",
                "The payment link must be an https:// URL on an allowed domain (checkout.stripe.com, buy.stripe.com, paypal.me, paypal.com).");
        }

        try
        {
            var link = await _db.TrainerPaymentLinks
                .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);

            if (link is null)
            {
                link = new TrainerPaymentLink { UserId = userId };
                _db.TrainerPaymentLinks.Add(link);
            }

            link.Url = request.Url.Trim();
            link.VerifiedDomain = verifiedDomain;
            link.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(MapLink(link));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the trainer payment link.");
            return ApiProblemResult.Unexpected("payment_link_error");
        }
    }

    /// <summary>Returns the authenticated trainer's published payment link.</summary>
    [Authorize]
    [HttpGet("me/payment-link")]
    public async Task<IActionResult> GetMyPaymentLink(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var link = await _db.TrainerPaymentLinks
            .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);

        if (link is null)
        {
            return ApiProblemResult.NotFound("payment_link_not_found", "No payment link has been published for this trainer.");
        }

        return Ok(MapLink(link));
    }

    /// <summary>Removes the authenticated trainer's published payment link.</summary>
    [Authorize]
    [HttpDelete("me/payment-link")]
    public async Task<IActionResult> DeletePaymentLink(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var link = await _db.TrainerPaymentLinks
            .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);

        if (link is null)
        {
            return ApiProblemResult.NotFound("payment_link_not_found", "No payment link has been published for this trainer.");
        }

        _db.TrainerPaymentLinks.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Public read of a trainer's payment link. Exposed only when the trainer has
    /// published one; returns 404 otherwise. No other trainer data is returned.
    /// </summary>
    [HttpGet("{id}/payment-link")]
    public async Task<IActionResult> GetTrainerPaymentLink(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return ApiProblemResult.Validation("trainer_id_required", "Trainer id is required.");
        }

        var link = await _db.TrainerPaymentLinks
            .FirstOrDefaultAsync(l => l.UserId == id, cancellationToken);

        if (link is null)
        {
            return ApiProblemResult.NotFound("payment_link_not_found", "This trainer has not published a payment link.");
        }

        return Ok(MapLink(link));
    }

    private static PaymentLinkResource MapLink(TrainerPaymentLink link) => new()
    {
        Url = link.Url,
        VerifiedDomain = link.VerifiedDomain,
        UpdatedAt = link.UpdatedAt,
    };
}

public class PaymentLinkRequest
{
    public string Url { get; set; } = string.Empty;
}

public class PaymentLinkResource
{
    public string Url { get; set; } = string.Empty;
    public string VerifiedDomain { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
