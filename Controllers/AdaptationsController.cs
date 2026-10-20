using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class AdaptationsController : ControllerBase
{
    private readonly TrainingTrackerAnalyticsService _analyticsService;
    private readonly CurrentUserProvider _userProvider;
    private readonly TrainingAccessService _trainingAccessService;
    private readonly ILogger<AdaptationsController> _logger;

    public AdaptationsController(
        TrainingTrackerAnalyticsService analyticsService,
        CurrentUserProvider userProvider,
        TrainingAccessService trainingAccessService,
        ILogger<AdaptationsController> logger)
    {
        _analyticsService = analyticsService;
        _userProvider = userProvider;
        _trainingAccessService = trainingAccessService;
        _logger = logger;
    }

    /// <summary>The 9 Galpin adaptations in canonical order.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAdaptations(
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_userProvider.GetCurrentUserId()))
            {
                return Unauthorized();
            }

            return Ok(await _analyticsService.GetCatalogAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load the adaptation catalog.", ex);
        }
    }

    /// <summary>Volume and sessions per adaptation for a date range.</summary>
    [HttpGet("coverage")]
    public async Task<IActionResult> GetCoverage(
        DateOnly from,
        DateOnly to,
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

            if (to == default)
            {
                to = from == default ? DateOnly.FromDateTime(DateTime.Today) : from;
            }

            if (from == default)
            {
                from = to.AddDays(-27);
            }

            return Ok(await _analyticsService.GetCoverageAsync(
                userId,
                from.ToDateTime(TimeOnly.MinValue),
                to.ToDateTime(TimeOnly.MinValue).AddDays(1),
                cancellationToken));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load adaptation coverage.", ex);
        }
    }

    /// <summary>Canonical adaptations an exercise type trains.</summary>
    [HttpGet("exercise-types/{typeId:int}/mapping")]
    public async Task<IActionResult> GetExerciseTypeMapping(
        int typeId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_userProvider.GetCurrentUserId()))
            {
                return Unauthorized();
            }

            var mapping = await _analyticsService.GetTypeMappingAsync(typeId, cancellationToken);
            if (mapping is null)
            {
                return Problem(
                    title: "Couldn't find that exercise.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Ok(mapping);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load the exercise type mapping.", ex);
        }
    }

    /// <summary>Edits the canonical mapping of an exercise type (global catalog).</summary>
    [HttpPut("exercise-types/{typeId:int}/mapping")]
    public async Task<IActionResult> SetExerciseTypeMapping(
        int typeId,
        AdaptationMappingInputResource resource,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_userProvider.GetCurrentUserId()))
            {
                return Unauthorized();
            }

            var mapping = await _analyticsService.SetTypeMappingAsync(
                typeId,
                resource ?? new AdaptationMappingInputResource(),
                cancellationToken);
            if (mapping is null)
            {
                return Problem(
                    title: "Couldn't find that exercise.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Ok(mapping);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to update the exercise type mapping.", ex);
        }
    }

    private IActionResult Unexpected(string title, Exception ex)
    {
        _logger.LogError(ex, "{Title}", title);
        return Problem(
            title: title,
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
