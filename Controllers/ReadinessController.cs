using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class ReadinessController : ControllerBase
{
    private readonly TrainingTrackerAnalyticsService _analyticsService;
    private readonly CurrentUserProvider _userProvider;
    private readonly TrainingAccessService _trainingAccessService;
    private readonly SponsoredOfferService _sponsoredOfferService;
    private readonly ILogger<ReadinessController> _logger;

    public ReadinessController(
        TrainingTrackerAnalyticsService analyticsService,
        CurrentUserProvider userProvider,
        TrainingAccessService trainingAccessService,
        SponsoredOfferService sponsoredOfferService,
        ILogger<ReadinessController> logger)
    {
        _analyticsService = analyticsService;
        _userProvider = userProvider;
        _trainingAccessService = trainingAccessService;
        _sponsoredOfferService = sponsoredOfferService;
        _logger = logger;
    }

    /// <summary>
    /// Derived readiness for the last 7 days: score 0-10, volume per adaptation
    /// and up to 2 suggestions compatible with the interference chart.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ReadinessResource>> GetReadiness(
        DateOnly? date,
        string traineeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var requesterUserId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(requesterUserId))
            {
                return Unauthorized();
            }

            var userId = string.IsNullOrWhiteSpace(traineeId)
                ? requesterUserId
                : traineeId;
            if (!_trainingAccessService.CanAccessUser(requesterUserId, userId))
            {
                return Forbid();
            }

            var asOf = date ?? DateOnly.FromDateTime(DateTime.Today);

            var readiness = await _analyticsService.GetReadinessAsync(
                userId,
                asOf.ToDateTime(TimeOnly.MinValue),
                cancellationToken);

            // Sponsored slot targets the viewer of the recommendations (the
            // requester), not the trainee whose data is being viewed.
            _sponsoredOfferService.LogDelivery(requesterUserId, readiness.Suggestions.Count);
            readiness.Sponsored = _sponsoredOfferService.TryBuildSlot(
                requesterUserId,
                SponsoredSlotContext.Training,
                readiness.Suggestions.Count);

            return Ok(readiness);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness could not be computed.");
            return Problem(
                title: "Failed to load readiness.",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
