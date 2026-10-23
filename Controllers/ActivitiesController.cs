#nullable enable

using System.Text.RegularExpressions;
using healthspan_api.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("api/activities")]
public class ActivitiesController : ControllerBase
{
    private const int MaxTypeLength = 64;
    private const int MaxNotesLength = 2000;
    private static readonly Regex TypeSlugPattern =
        new(@"^[a-z0-9]+(?:[ _.-][a-z0-9]+)*$", RegexOptions.Compiled);

    private readonly WellnessActivityService _activityService;
    private readonly CurrentUserProvider _currentUserProvider;
    private readonly ILogger<ActivitiesController> _logger;

    public ActivitiesController(
        WellnessActivityService activityService,
        CurrentUserProvider currentUserProvider,
        ILogger<ActivitiesController> logger)
    {
        _activityService = activityService;
        _currentUserProvider = currentUserProvider;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult CreateActivity(WellnessActivityWriteResource resource)
    {
        try
        {
            var validationProblem = ValidateResource(resource);
            if (validationProblem is not null)
            {
                return validationProblem;
            }

            var userId = _currentUserProvider.GetCurrentUserId();
            var activity = _activityService.CreateActivity(userId, MapInput(resource));

            return CreatedAtAction(
                nameof(GetActivity),
                new { id = activity.Id },
                MapActivity(activity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create a wellness activity");
            return ApiProblemResult.Unexpected("activity_create_failed");
        }
    }

    [HttpGet]
    public ActionResult<List<WellnessActivityResource>> GetActivities(
        string? type,
        WellnessDimension? dimension,
        DateTime? from,
        DateTime? to)
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            var activities = _activityService.GetActivities(
                userId, type, dimension, from, to);

            return Ok(activities.Select(MapActivity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load wellness activities");
            return ApiProblemResult.Unexpected("activities_load_failed");
        }
    }

    [HttpGet("{id}")]
    public IActionResult GetActivity(Guid id)
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            var activity = _activityService.GetActivity(userId, id);
            if (activity is null)
            {
                return ActivityNotFound();
            }

            return Ok(MapActivity(activity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load wellness activity {ActivityId}", id);
            return ApiProblemResult.Unexpected("activity_load_failed");
        }
    }

    [HttpPut("{id}")]
    public IActionResult UpdateActivity(Guid id, WellnessActivityWriteResource resource)
    {
        try
        {
            var validationProblem = ValidateResource(resource);
            if (validationProblem is not null)
            {
                return validationProblem;
            }

            var userId = _currentUserProvider.GetCurrentUserId();
            var updated = _activityService.UpdateActivity(
                userId, id, MapInput(resource));

            return updated
                ? NoContent()
                : ActivityNotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update wellness activity {ActivityId}", id);
            return ApiProblemResult.Unexpected("activity_update_failed");
        }
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteActivity(Guid id)
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            return _activityService.DeleteActivity(userId, id)
                ? NoContent()
                : ActivityNotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete wellness activity {ActivityId}", id);
            return ApiProblemResult.Unexpected("activity_delete_failed");
        }
    }

    private static ObjectResult? ValidateResource(WellnessActivityWriteResource resource)
    {
        var normalizedType = resource.Type?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedType.Length == 0
            || normalizedType.Length > MaxTypeLength
            || !TypeSlugPattern.IsMatch(normalizedType))
        {
            return ApiProblemResult.Validation(
                "activity_type_invalid",
                "Activity type is required and must be a short name (letters, numbers, spaces, '-', '_' or '.').");
        }

        if (!Enum.IsDefined(resource.Dimension))
        {
            return ApiProblemResult.Validation(
                "activity_dimension_invalid",
                "Dimension must be one of: strength, health, mobility.");
        }

        if (resource.OccurredOnUtc == default
            || resource.OccurredOnUtc.Date > DateTime.UtcNow.Date)
        {
            return ApiProblemResult.Validation(
                "activity_date_invalid",
                "Activity date is required and cannot be in the future.");
        }

        if (resource.DurationMinutes.HasValue && resource.DurationMinutes < 0)
        {
            return ApiProblemResult.Validation(
                "activity_duration_invalid",
                "Duration in minutes cannot be negative.");
        }

        if (resource.FrequencyPerWeek.HasValue && resource.FrequencyPerWeek < 0)
        {
            return ApiProblemResult.Validation(
                "activity_frequency_invalid",
                "Frequency per week cannot be negative.");
        }

        if (resource.Intensity.HasValue
            && (resource.Intensity < 0 || resource.Intensity > 10))
        {
            return ApiProblemResult.Validation(
                "activity_intensity_invalid",
                "Intensity must be between 0 and 10.");
        }

        if (resource.TemperatureCelsius.HasValue
            && (resource.TemperatureCelsius < -100 || resource.TemperatureCelsius > 200))
        {
            return ApiProblemResult.Validation(
                "activity_temperature_invalid",
                "Temperature must be between -100 and 200 degrees Celsius.");
        }

        if (resource.Cycles.HasValue && resource.Cycles < 0)
        {
            return ApiProblemResult.Validation(
                "activity_cycles_invalid",
                "Cycles cannot be negative.");
        }

        if (resource.Notes is not null && resource.Notes.Length > MaxNotesLength)
        {
            return ApiProblemResult.Validation(
                "activity_notes_too_long",
                $"Notes cannot exceed {MaxNotesLength} characters.");
        }

        return null;
    }

    private static WellnessActivityUpdateInput MapInput(WellnessActivityWriteResource resource)
        => new()
        {
            Type = resource.Type,
            Dimension = resource.Dimension,
            OccurredOnUtc = resource.OccurredOnUtc,
            DurationMinutes = resource.DurationMinutes,
            FrequencyPerWeek = resource.FrequencyPerWeek,
            Intensity = resource.Intensity,
            TemperatureCelsius = resource.TemperatureCelsius,
            Cycles = resource.Cycles,
            Notes = resource.Notes,
            Metadata = resource.Metadata
        };

    private static WellnessActivityResource MapActivity(WellnessActivity activity)
        => new()
        {
            Id = activity.Id,
            Type = activity.Type,
            Dimension = activity.Dimension,
            OccurredOnUtc = activity.OccurredOnUtc,
            DurationMinutes = activity.DurationMinutes,
            FrequencyPerWeek = activity.FrequencyPerWeek,
            Intensity = activity.Intensity,
            TemperatureCelsius = activity.TemperatureCelsius,
            Cycles = activity.Cycles,
            Notes = activity.Notes,
            Metadata = activity.Metadata,
            CreatedAtUtc = activity.CreatedAtUtc
        };

    private static ObjectResult ActivityNotFound()
        => ApiProblemResult.NotFound(
            "activity_not_found",
            "This activity no longer exists.");
}
