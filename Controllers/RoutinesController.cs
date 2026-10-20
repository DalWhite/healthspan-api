using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class RoutinesController : ControllerBase
{
    private readonly RoutinesRepository _routinesRepository;
    private readonly ExerciseTypesRepository _exerciseTypesRepository;
    private readonly CurrentUserProvider _userProvider;
    private readonly TrainingAccessService _trainingAccessService;
    private readonly RoutinePlanService _routinePlanService;
    private readonly ILogger<RoutinesController> _logger;

    public RoutinesController(
        RoutinesRepository routineRepository,
        ExerciseTypesRepository exerciseTypesRepository,
        CurrentUserProvider userProvider,
        TrainingAccessService trainingAccessService,
        RoutinePlanService routinePlanService,
        ILogger<RoutinesController> logger)
    {
        _routinesRepository = routineRepository;
        _exerciseTypesRepository = exerciseTypesRepository;
        _userProvider = userProvider;
        _trainingAccessService = trainingAccessService;
        _routinePlanService = routinePlanService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetRoutines(
        string traineeId,
        CancellationToken cancellationToken)
    {
        var (requesterUserId, userId, accessError) = ResolveTargetUser(traineeId);
        if (accessError is not null)
        {
            return accessError;
        }

        try
        {
            var routines = await _routinesRepository.GetRoutinesAsync(
                userId,
                cancellationToken);
            return Ok(routines.Select(MapRoutine).ToList());
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load routines.", ex);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRoutine(
        Guid id,
        string traineeId,
        CancellationToken cancellationToken)
    {
        var (requesterUserId, userId, accessError) = ResolveTargetUser(traineeId);
        if (accessError is not null)
        {
            return accessError;
        }

        try
        {
            var routine = await _routinesRepository.GetRoutineAsync(
                id,
                userId,
                cancellationToken);
            if (routine is null)
            {
                return NotFound();
            }

            Response.Headers.ETag = ETagOf(routine.Version);
            return Ok(MapRoutine(routine));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load the routine.", ex);
        }
    }

    /// <summary>
    /// Routine workouts scheduled for a day (Today flow). date = YYYY-MM-DD,
    /// defaults to today.
    /// </summary>
    [HttpGet("plan")]
    public async Task<IActionResult> GetPlan(
        string traineeId,
        string? date,
        CancellationToken cancellationToken)
    {
        var (requesterUserId, userId, accessError) = ResolveTargetUser(traineeId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (!TryParseDate(date, out var planDate))
        {
            return DateError();
        }

        try
        {
            var plan = await _routinePlanService.GetPlanAsync(
                userId,
                planDate,
                cancellationToken);
            return Ok(plan);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load the training plan.", ex);
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateRoutine(
        RoutineResource routineResource,
        CancellationToken cancellationToken)
    {
        var (requesterUserId, userId, accessError) = ResolveTargetUser(routineResource.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var validation = ValidateRoutine(routineResource);
        if (validation is not null)
        {
            return validation;
        }

        if (await _routinesRepository.NameExistsAsync(
                userId,
                routineResource.Name,
                cancellationToken: cancellationToken))
        {
            return NameConflictError();
        }

        try
        {
            var routine = new Routine
            {
                Name = routineResource.Name.Trim(),
                Description = routineResource.Description,
                UserId = userId,
                // A routine a trainer creates for a trainee is owned by the
                // trainee but remembers which coach assigned it, so the trainee
                // view can label it. Self-created routines keep TrainerId null.
                TrainerId = requesterUserId == userId ? null : requesterUserId,
                WorkoutTemplates = BuildTemplates(routineResource.WorkoutTemplates)
            };

            var created = await _routinesRepository.AddRoutineAsync(
                routine,
                cancellationToken);

            Response.Headers.ETag = ETagOf(created.Version);
            return CreatedAtAction(
                nameof(GetRoutine),
                new { id = created.Id },
                MapRoutine(created));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to create the routine.", ex);
        }
    }

    /// <summary>
    /// Full structural replacement. If-Match with the routine's current version
    /// (from GET's ETag) prevents silent overwrites: a mismatch returns 409.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateRoutine(
        Guid id,
        RoutineResource routineResource,
        CancellationToken cancellationToken)
    {
        var (requesterUserId, userId, accessError) = ResolveTargetUser(routineResource.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var validation = ValidateRoutine(routineResource);
        if (validation is not null)
        {
            return validation;
        }

        if (await _routinesRepository.NameExistsAsync(
                userId,
                routineResource.Name,
                excludeRoutineId: id,
                cancellationToken: cancellationToken))
        {
            return NameConflictError();
        }

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            var current = await _routinesRepository.GetRoutineAsync(
                id,
                userId,
                cancellationToken);
            if (current is null)
            {
                return NotFound();
            }

            if (ParseETagVersion(ifMatch) != current.Version)
            {
                return Problem(
                    title: "This routine changed since you loaded it. Reload it to apply your edits.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        try
        {
            var replacement = new Routine
            {
                Name = routineResource.Name.Trim(),
                Description = routineResource.Description,
                WorkoutTemplates = BuildTemplates(routineResource.WorkoutTemplates)
            };

            var result = await _routinesRepository.UpdateRoutineAsync(
                id,
                userId,
                replacement,
                cancellationToken);

            if (result == RoutineUpdateResult.NotFound)
            {
                return NotFound();
            }

            var updated = await _routinesRepository.GetRoutineAsync(
                id,
                userId,
                cancellationToken);
            Response.Headers.ETag = ETagOf(updated.Version);
            return Ok(MapRoutine(updated));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to update the routine.", ex);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRoutine(
        Guid id,
        string traineeId,
        CancellationToken cancellationToken)
    {
        var (requesterUserId, userId, accessError) = ResolveTargetUser(traineeId);
        if (accessError is not null)
        {
            return accessError;
        }

        try
        {
            var deleted = await _routinesRepository.DeleteRoutineAsync(
                id,
                userId,
                cancellationToken);
            if (!deleted)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to delete the routine.", ex);
        }
    }

    private (string RequesterUserId, string TargetUserId, IActionResult Error) ResolveTargetUser(
        string requestedUserId)
    {
        var requesterUserId = _userProvider.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(requesterUserId))
        {
            return (null, null, Unauthorized());
        }

        var userId = string.IsNullOrWhiteSpace(requestedUserId)
            ? requesterUserId
            : requestedUserId;

        if (!_trainingAccessService.CanAccessUser(requesterUserId, userId))
        {
            return (null, null, Forbid());
        }

        return (requesterUserId, userId, null);
    }

    private IActionResult ValidateRoutine(RoutineResource routineResource)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(routineResource.Name))
        {
            errors["name"] = ["Give the routine a name."];
        }
        else
        {
            var nameLength = routineResource.Name.Trim().Length;
            if (nameLength < 2 || nameLength > 100)
            {
                errors["name"] =
                    ["The routine name must be between 2 and 100 characters."];
            }
        }

        var templates = routineResource.WorkoutTemplates ?? [];
        var allTypeIds = templates
            .SelectMany(template => template.Exercises ?? [])
            .Select(exercise => exercise.ExerciseTypeId)
            .Distinct()
            .ToList();

        HashSet<int> knownTypeIds = [];
        if (allTypeIds.Count > 0)
        {
            knownTypeIds = _exerciseTypesRepository
                .GetExerciseTypesByIds(allTypeIds)
                .Select(type => type.Id)
                .ToHashSet();
        }

        for (var templateIndex = 0; templateIndex < templates.Count; templateIndex++)
        {
            var template = templates[templateIndex];
            if (string.IsNullOrWhiteSpace(template.Type))
            {
                errors[$"workoutTemplates[{templateIndex}].type"] =
                    ["Give this workout a name, for example \"Leg day\"."];
            }

            if (template.FrequencyPerWeek < 1 || template.FrequencyPerWeek > 7)
            {
                errors[$"workoutTemplates[{templateIndex}].frequencyPerWeek"] =
                    ["How many days per week? Choose a number between 1 and 7."];
            }

            var exercises = template.Exercises ?? [];

            var orders = exercises.Select(exercise => exercise.Order).OrderBy(value => value).ToList();
            for (var position = 0; position < orders.Count; position++)
            {
                if (orders[position] != position + 1)
                {
                    errors[$"workoutTemplates[{templateIndex}].exercises"] =
                        [$"Exercises must be numbered 1 to {exercises.Count} in order, without gaps or repeats."];
                    break;
                }
            }

            for (var exerciseIndex = 0; exerciseIndex < exercises.Count; exerciseIndex++)
            {
                var exercise = exercises[exerciseIndex];
                var prefix = $"workoutTemplates[{templateIndex}].exercises[{exerciseIndex}]";

                if (!knownTypeIds.Contains(exercise.ExerciseTypeId))
                {
                    errors[$"{prefix}.exerciseTypeId"] =
                        ["Choose an exercise from the library."];
                }

                AppendAtLeastOne(errors, $"{prefix}.sets", exercise.Sets);
                AppendAtLeastOne(errors, $"{prefix}.reps", exercise.Reps);
                AppendNonNegative(errors, $"{prefix}.restSeconds", exercise.RestSeconds);
                if (exercise.LoadKg.HasValue && exercise.LoadKg.Value < 0)
                {
                    errors[$"{prefix}.loadKg"] = ["The load can't be negative."];
                }
            }
        }

        if (errors.Count == 0)
        {
            return null;
        }

        return new ObjectResult(new ValidationProblemDetails(errors))
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
    }

    private static void AppendNonNegative(
        Dictionary<string, string[]> errors,
        string field,
        int? value)
    {
        if (value.HasValue && value.Value < 0)
        {
            errors[field] = ["This value can't be negative."];
        }
    }

    private static void AppendAtLeastOne(
        Dictionary<string, string[]> errors,
        string field,
        int? value)
    {
        if (value.HasValue && value.Value < 1)
        {
            errors[field] = ["This target must be at least 1, or left empty."];
        }
    }

    private static List<WorkoutTemplate> BuildTemplates(List<WorkoutTemplateResource> resources)
        => (resources ?? []).Select(templateResource => new WorkoutTemplate
        {
            Type = templateResource.Type.Trim(),
            DaysOfWeek = templateResource.FrequencyPerWeek,
            Exercises = (templateResource.Exercises ?? [])
                .Select(exerciseResource => new WorkoutTemplateExercise
                {
                    ExerciseTypeId = exerciseResource.ExerciseTypeId,
                    Order = exerciseResource.Order,
                    Sets = exerciseResource.Sets,
                    Reps = exerciseResource.Reps,
                    LoadKg = exerciseResource.LoadKg,
                    RestSeconds = exerciseResource.RestSeconds,
                    Notes = string.IsNullOrWhiteSpace(exerciseResource.Notes)
                        ? null
                        : exerciseResource.Notes.Trim()
                })
                .ToList()
        }).ToList();

    private static RoutineResource MapRoutine(Routine routine) => RoutineMapper.Map(routine);

    private static string ETagOf(int version) => RoutineMapper.ETagOf(version);

    private static int? ParseETagVersion(string ifMatch) => RoutineMapper.ParseETagVersion(ifMatch);

    private static ObjectResult NameConflictError()
        => new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["name"] = ["You already have a routine with this name."]
        }))
        {
            StatusCode = StatusCodes.Status400BadRequest
        };

    private static bool TryParseDate(string date, out DateOnly parsed)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            parsed = DateOnly.FromDateTime(DateTime.UtcNow);
            return true;
        }

        return DateOnly.TryParseExact(date, "yyyy-MM-dd", out parsed);
    }

    private IActionResult FieldError(string field, string message)
        => new ObjectResult(
            new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }))
        {
            StatusCode = StatusCodes.Status400BadRequest
        };

    private IActionResult DateError()
        => FieldError("date", "Use a date like 2026-10-20.");

    private ObjectResult Unexpected(string title, Exception ex)
    {
        _logger.LogError(ex, "{Title}", title);
        return Problem(
            title: title,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
