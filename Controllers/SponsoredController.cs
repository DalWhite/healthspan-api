#nullable enable

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class SponsoredController : ControllerBase
{
    private readonly SponsoredOfferService _sponsoredService;
    private readonly CurrentUserProvider _userProvider;
    private readonly ILogger<SponsoredController> _logger;

    public SponsoredController(
        SponsoredOfferService sponsoredService,
        CurrentUserProvider userProvider,
        ILogger<SponsoredController> logger)
    {
        _sponsoredService = sponsoredService;
        _userProvider = userProvider;
        _logger = logger;
    }

    /// <summary>
    /// Records a minimum sponsored metric (click / dismiss / conversion) for a
    /// slot the client rendered. Impressions are server-side only.
    /// </summary>
    [HttpPost("events")]
    public IActionResult RecordEvent([FromBody] SponsoredEventRequest request)
    {
        var userId = _userProvider.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!TryParseEventType(request.Type, out var eventType))
        {
            return ApiProblemResult.Validation(
                "sponsored_event_type_invalid",
                "Type must be one of: click, dismiss, conversion.");
        }

        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            return ApiProblemResult.Validation(
                "sponsored_slug_required",
                "Slug is required.");
        }

        if (!_sponsoredService.RecordEvent(userId, request.Slug.Trim().ToLowerInvariant(), eventType))
        {
            return ApiProblemResult.NotFound(
                "sponsored_offer_not_found",
                "The sponsored offer was not found or is no longer active.");
        }

        return NoContent();
    }

    /// <summary>
    /// Aggregated minimum metrics per offer (impressions, clicks, dismisses,
    /// conversions). Restricted to the configured metrics allowlist.
    /// </summary>
    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (!_sponsoredService.CanReadMetrics(email))
        {
            return Forbid();
        }

        var metrics = _sponsoredService.GetMetrics()
            .Select(row => new SponsoredMetricsResource
            {
                Slug = row.Slug,
                DisplayName = row.DisplayName,
                Active = row.Active,
                Impressions = row.Impressions,
                Clicks = row.Clicks,
                Dismisses = row.Dismisses,
                Conversions = row.Conversions,
            });

        return Ok(metrics);
    }

    private static bool TryParseEventType(string type, out SponsoredEventType eventType)
    {
        eventType = SponsoredEventType.Impression;

        return type?.ToLowerInvariant() switch
        {
            "click" => TryAssign(SponsoredEventType.Click, out eventType),
            "dismiss" => TryAssign(SponsoredEventType.Dismiss, out eventType),
            "conversion" => TryAssign(SponsoredEventType.Conversion, out eventType),
            _ => false,
        };
    }

    private static bool TryAssign(SponsoredEventType value, out SponsoredEventType target)
    {
        target = value;
        return true;
    }
}
