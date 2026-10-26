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
    private const int MaxCycles = 50;
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
    public IActionResult GetActivities(
        string? type,
        WellnessDimension? dimension,
        DateTime? from,
        DateTime? to,
        int page = 1,
        int pageSize = 10,
        string? fields = null)
    {
        try
        {
            if (page < 1)
            {
                return ApiProblemResult.Validation(
                    "activities_page_invalid",
                    "Page must be 1 or greater.",
                    errors: new Dictionary<string, string[]>
                    {
                        ["page"] = ["Page must be 1 or greater."]
                    });
            }

            if (pageSize < 1 || pageSize > 100)
            {
                return ApiProblemResult.Validation(
                    "activities_page_size_invalid",
                    "Page size must be between 1 and 100.",
                    errors: new Dictionary<string, string[]>
                    {
                        ["pageSize"] = ["Page size must be between 1 and 100."]
                    });
            }

            var userId = _currentUserProvider.GetCurrentUserId();
            var (activities, total) = _activityService.GetActivities(
                userId, type, dimension, from, to, page, pageSize);

            // t_101e2b0a: ?fields=minimal returns the list projection the
            // history screen actually renders; the full resource is fetched
            // per row on demand (detail sheet / edit), never 100 times per page.
            if (string.Equals(fields, "minimal", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new PagedActivitiesSummaryResource
                {
                    Items = activities.Select(MapActivitySummary).ToList(),
                    Total = total,
                    Page = page,
                    PageSize = pageSize,
                    HasMore = page * pageSize < total
                });
            }

            return Ok(new PagedActivitiesResource
            {
                Items = activities.Select(MapActivity).ToList(),
                Total = total,
                Page = page,
                PageSize = pageSize,
                HasMore = page * pageSize < total
            });
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
                "Activity type is required and must be a short name (letters, numbers, spaces, '-', '_' or '.').",
                errors: FieldErrors("type", "Activity type is required and must be a short name (letters, numbers, spaces, '-', '_' or '.')."));
        }

        if (resource.Dimension is null)
        {
            return ApiProblemResult.Validation(
                "activity_dimension_required",
                "Dimension is required: strength, health or mobility.",
                errors: FieldErrors("dimension", "Dimension is required: strength, health or mobility."));
        }

        if (!Enum.IsDefined(resource.Dimension.Value))
        {
            return ApiProblemResult.Validation(
                "activity_dimension_invalid",
                "Dimension must be one of: strength, health, mobility.",
                errors: FieldErrors("dimension", "Dimension must be one of: strength, health, mobility."));
        }

        if (resource.OccurredOnUtc == default
            || resource.OccurredOnUtc.Date > DateTime.UtcNow.Date)
        {
            return ApiProblemResult.Validation(
                "activity_date_invalid",
                "Activity date is required and cannot be in the future.",
                errors: FieldErrors("occurredOnUtc", "Activity date is required and cannot be in the future."));
        }

        if (resource.DurationMinutes.HasValue && resource.DurationMinutes < 1)
        {
            return ApiProblemResult.Validation(
                "activity_duration_invalid",
                "Duration in minutes must be at least 1.",
                errors: FieldErrors("durationMinutes", "Duration in minutes must be at least 1."));
        }

        if (resource.FrequencyPerWeek.HasValue
            && (resource.FrequencyPerWeek < 1 || resource.FrequencyPerWeek > 7))
        {
            return ApiProblemResult.Validation(
                "activity_frequency_invalid",
                "Frequency per week must be between 1 and 7.",
                errors: FieldErrors("frequencyPerWeek", "Frequency per week must be between 1 and 7."));
        }

        if (resource.Intensity.HasValue
            && (resource.Intensity < 0 || resource.Intensity > 10))
        {
            return ApiProblemResult.Validation(
                "activity_intensity_invalid",
                "Intensity must be between 0 and 10.",
                errors: FieldErrors("intensity", "Intensity must be between 0 and 10."));
        }

        // Kept wider than the register prototype's -100..60: sauna sessions run
        // at 80-100 C. The FE cap is the outlier to fix, not the API range.
        if (resource.TemperatureCelsius.HasValue
            && (resource.TemperatureCelsius < -100 || resource.TemperatureCelsius > 200))
        {
            return ApiProblemResult.Validation(
                "activity_temperature_invalid",
                "Temperature must be between -100 and 200 degrees Celsius.",
                errors: FieldErrors("temperatureCelsius", "Temperature must be between -100 and 200 degrees Celsius."));
        }

        if (resource.Cycles.HasValue && (resource.Cycles < 0 || resource.Cycles > MaxCycles))
        {
            return ApiProblemResult.Validation(
                "activity_cycles_invalid",
                $"Cycles must be between 0 and {MaxCycles}.",
                errors: FieldErrors("cycles", $"Cycles must be between 0 and {MaxCycles}."));
        }

        if (resource.Notes is not null && resource.Notes.Length > MaxNotesLength)
        {
            return ApiProblemResult.Validation(
                "activity_notes_too_long",
                $"Notes cannot exceed {MaxNotesLength} characters.",
                errors: FieldErrors("notes", $"Notes cannot exceed {MaxNotesLength} characters."));
        }

        return null;
    }

    private static Dictionary<string, string[]> FieldErrors(string field, string message)
        => new() { [field] = new[] { message } };

    /// <summary>
    /// Normalizes any accepted date representation to UTC: date-only or
    /// offset-less strings are treated as UTC, offset-bearing values are
    /// converted, so responses always carry an explicit Z.
    /// </summary>
    private static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    /// <summary>
    /// Read-path normalization for the decimal activity columns (t_e395b7be).
    /// Pomelo's reader reconstructs decimal(65,30) values with double-derived
    /// noise (stored 85.5 is handed back as a scale-28 decimal equal to
    /// 85.4999999999999999999999999999), and a direct (double?) cast then emits
    /// 85.49999999999999 / 33.300000000000004 / 80.49999999999999 in JSON.
    /// Rounding to the domain's meaningful precision before the cast absorbs
    /// that noise and also strips the scale tail (7 -> 7, not 7.00000...),
    /// so the double exposure from PR #22 stays. Write path untouched.
    /// </summary>
    public static double? ToCleanDouble(decimal? value)
        => value.HasValue ? (double)Math.Round(value.Value, 6) : null;

    private static WellnessActivityUpdateInput MapInput(WellnessActivityWriteResource resource)
        => new()
        {
            Type = resource.Type,
            Dimension = resource.Dimension!.Value,
            OccurredOnUtc = ToUtc(resource.OccurredOnUtc),
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
            // ToUtc fixes Kind=Unspecified coming from MySQL/EF before the
            // DateTimeOffset cast, so GET emits '...Z' with no timezone drift.
            OccurredOnUtc = ToUtc(activity.OccurredOnUtc).ToUniversalTime(),
            DurationMinutes = activity.DurationMinutes,
            FrequencyPerWeek = ToCleanDouble(activity.FrequencyPerWeek),
            Intensity = ToCleanDouble(activity.Intensity),
            TemperatureCelsius = ToCleanDouble(activity.TemperatureCelsius),
            Cycles = activity.Cycles,
            Notes = activity.Notes,
            Metadata = activity.Metadata,
            CreatedAtUtc = ToUtc(activity.CreatedAtUtc).ToUniversalTime()
        };

    private static WellnessActivitySummaryResource MapActivitySummary(
        WellnessActivity activity)
        => new()
        {
            Id = activity.Id,
            Type = activity.Type,
            Dimension = activity.Dimension,
            OccurredOnUtc = ToUtc(activity.OccurredOnUtc).ToUniversalTime(),
            DurationMinutes = activity.DurationMinutes,
            FrequencyPerWeek = (double?)activity.FrequencyPerWeek,
            Intensity = (double?)activity.Intensity,
            TemperatureCelsius = (double?)activity.TemperatureCelsius,
            Cycles = activity.Cycles
        };

    private static ObjectResult ActivityNotFound()
        => ApiProblemResult.NotFound(
            "activity_not_found",
            "This activity no longer exists.");
}
