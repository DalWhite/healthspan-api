using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using healthspan_api.Persistence.Repositories;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class WorkoutsController : ControllerBase
{
    private readonly WorkoutsRepository _workoutsRepository;
    private readonly ExercisesRepository _exercisesRepository;
    private readonly ExerciseTypesRepository _exerciseTypesRepository;
    private readonly CurrentUserProvider _userProvider;
    private readonly TrainingAccessService _trainingAccessService;
    private readonly WeeklyTrainingSummaryService _weeklyTrainingSummaryService;
    private readonly RoutineAdherenceService _routineAdherenceService;
    private readonly RoutinesRepository _routinesRepository;
    private readonly ILogger<WorkoutsController> _logger;

    public WorkoutsController(
        WorkoutsRepository workoutRepository,
        ExercisesRepository exercisesRepository,
        ExerciseTypesRepository exerciseTypesRepository,
        CurrentUserProvider userProvider,
        TrainingAccessService trainingAccessService,
        WeeklyTrainingSummaryService weeklyTrainingSummaryService,
        RoutineAdherenceService routineAdherenceService,
        RoutinesRepository routinesRepository,
        ILogger<WorkoutsController> logger)
    {
        _workoutsRepository = workoutRepository;
        _exercisesRepository = exercisesRepository;
        _exerciseTypesRepository = exerciseTypesRepository;
        _userProvider = userProvider;
        _trainingAccessService = trainingAccessService;
        _weeklyTrainingSummaryService = weeklyTrainingSummaryService;
        _routineAdherenceService = routineAdherenceService;
        _routinesRepository = routinesRepository;
        _logger = logger;
    }
    [HttpGet]
    public async Task<ActionResult<List<GetWorkoutResource>>> GetWorkouts(
        string traineeId,
        DateOnly? from,
        DateOnly? to,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken,
        string search = "",
        string tag = "")
    {
        try
        {
            if (from.HasValue && to.HasValue && from.Value > to.Value)
            {
                return Problem(
                    title: "The start date cannot be after the end date.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (page.HasValue && page.Value < 1)
            {
                return Problem(
                    title: "Page must be at least 1.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (pageSize.HasValue && (pageSize.Value < 1 || pageSize.Value > 100))
            {
                return Problem(
                    title: "Page size must be between 1 and 100.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var requesterUserId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(requesterUserId))
            {
                return Unauthorized();
            }

            var userId = string.IsNullOrWhiteSpace(traineeId)
                ? requesterUserId
                : traineeId;
            if (!_trainingAccessService.CanAccessUser(
                    requesterUserId,
                    userId))
            {
                return Forbid();
            }

            var shouldPaginate = page.HasValue || pageSize.HasValue;
            int? normalizedPage = shouldPaginate ? page ?? 1 : null;
            int? normalizedPageSize = shouldPaginate ? pageSize ?? 20 : null;
            var result = await _workoutsRepository.GetWorkoutHistoryAsync(
                userId,
                from,
                to,
                normalizedPage,
                normalizedPageSize,
                cancellationToken,
                search,
                tag);

            Response.Headers["X-Total-Count"] = result.TotalCount.ToString();

            var response = result.Workouts
                .Select(workout => MapWorkoutHistory(workout, includeExercises: false))
                .ToList();
            return Ok(response);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load workout history.", ex);
        }
    }

    [HttpPost]
    public async Task<ActionResult<GetWorkoutResource>> AddWorkout(
        AddWorkoutResource workoutResource,
        CancellationToken cancellationToken)
    {
        try
        {
            var requesterUserId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(requesterUserId))
            {
                return Unauthorized();
            }

            var userId = string.IsNullOrWhiteSpace(workoutResource.UserId)
                ? requesterUserId
                : workoutResource.UserId;
            if (!_trainingAccessService.CanAccessUser(
                    requesterUserId,
                    userId))
            {
                return Forbid();
            }

            var result = await _workoutsRepository
                .GetOrCreateWorkoutSessionAsync(
                    userId,
                    DateOnly.FromDateTime(workoutResource.Date),
                    workoutResource.Type,
                    cancellationToken);
            var response = MapWorkoutHistory(
                result.Workout,
                includeExercises: true);

            return result.Created
                ? CreatedAtAction(
                    nameof(GetWorkout),
                    new { id = result.Workout.Id },
                    response)
                : Ok(response);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to prepare the workout.", ex);
        }
    }

    [HttpGet("weekly-summary")]
    public async Task<ActionResult<WeeklyTrainingSummaryResource>>
        GetWeeklyTrainingSummary(
            int weeks = 12,
            CancellationToken cancellationToken = default)
    {
        if (weeks is < 1 or > 52)
        {
            return Problem(
                title: "Weeks must be between 1 and 52.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            return Ok(await _weeklyTrainingSummaryService.GetAsync(
                userId,
                weeks,
                cancellationToken));
        }
        catch (Exception ex)
        {
            return Unexpected(
                "Failed to load the weekly training summary.",
                ex);
        }
    }

    [HttpGet("today")]
    public async Task<ActionResult<WorkoutSessionResource>> GetTodayWorkout(
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var sessionDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var workout = await _workoutsRepository.GetWorkoutSessionForDateAsync(
                userId,
                sessionDate,
                cancellationToken);

            if (workout is null)
            {
                return NoContent();
            }

            return Ok(await MapSessionAsync(workout, userId, cancellationToken));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load today's workout.", ex);
        }
    }

    [HttpGet("today/sessions")]
    public async Task<ActionResult<List<WorkoutSessionResource>>>
        GetDayWorkoutSessions(
            DateOnly? date,
            CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var sessionDate =
                date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var workouts = await _workoutsRepository
                .GetWorkoutSessionsForDateAsync(
                    userId,
                    sessionDate,
                    cancellationToken);
            var response = new List<WorkoutSessionResource>();

            foreach (var workout in workouts)
            {
                response.Add(await MapSessionAsync(
                    workout,
                    userId,
                    cancellationToken));
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            return Unexpected(
                "Failed to load the day's workouts.",
                ex);
        }
    }

    [HttpPost("today")]
    public async Task<ActionResult<WorkoutSessionResource>> StartTodayWorkout(
        StartWorkoutSessionResource resource,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var result = resource.CreateNew
                ? (Workout: await _workoutsRepository
                    .CreateWorkoutSessionAsync(
                        userId,
                        resource.Date,
                        resource.Type,
                        cancellationToken), Created: true)
                : await _workoutsRepository
                    .GetOrCreateWorkoutSessionAsync(
                        userId,
                        resource.Date,
                        resource.Type,
                        cancellationToken);
            var response = await MapSessionAsync(
                result.Workout,
                userId,
                cancellationToken);

            return result.Created
                ? CreatedAtAction(nameof(GetWorkout), new { id = result.Workout.Id }, response)
                : Ok(response);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to start today's workout.", ex);
        }
    }

    /// <summary>
    /// "Start today's workout" from a routine: copies the routine workout's
    /// ordered exercises into the day's session as normal editable exercises.
    /// Idempotent — applying twice does not duplicate exercises.
    /// </summary>
    [HttpPost("apply-template")]
    public async Task<ActionResult<WorkoutSessionResource>> ApplyRoutineTemplate(
        ApplyTemplateResource resource,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            DateOnly sessionDate;
            if (string.IsNullOrWhiteSpace(resource.Date))
            {
                sessionDate = DateOnly.FromDateTime(DateTime.UtcNow);
            }
            else if (!DateOnly.TryParseExact(resource.Date, "yyyy-MM-dd", out sessionDate))
            {
                return Problem(
                    title: "Use a date like 2026-10-20.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var outcome = await _workoutsRepository.ApplyTemplateToSessionAsync(
                userId,
                resource.RoutineId,
                resource.TemplateId,
                sessionDate,
                cancellationToken);

            switch (outcome.Result)
            {
                case ApplyTemplateResult.TemplateNotFound:
                    return Problem(
                        title: "We couldn't find that routine workout.",
                        statusCode: StatusCodes.Status404NotFound);
                case ApplyTemplateResult.TemplateEmpty:
                    return Problem(
                        title: "This routine workout has no exercises yet. Add some exercises to the routine first.",
                        statusCode: StatusCodes.Status400BadRequest);
            }

            var response = await MapSessionAsync(outcome.Workout, userId, cancellationToken);
            return outcome.SessionCreated
                ? CreatedAtAction(nameof(GetWorkout), new { id = outcome.Workout.Id }, response)
                : Ok(response);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to start the routine workout.", ex);
        }
    }

    /// <summary>
    /// Explicit "I finished this workout". Idempotent. Completed-workout
    /// counting (adherence, history progress) uses this, never exercise count.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<WorkoutSessionResource>> CompleteWorkout(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var workout = await _workoutsRepository.CompleteWorkoutAsync(
                id,
                userId,
                cancellationToken);
            if (workout is null)
            {
                return Problem(
                    title: "Workout not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Ok(await MapSessionAsync(workout, userId, cancellationToken));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to complete the workout.", ex);
        }
    }

    /// <summary>
    /// "Save as routine" from History: creates a one-workday routine from a
    /// real logged workout. Exercises keep their logged order; initial targets
    /// are the values actually done that day. The FE lands on the routine
    /// editor with everything prefilled and editable.
    /// </summary>
    [HttpPost("{id:guid}/save-as-routine")]
    public async Task<IActionResult> SaveWorkoutAsRoutine(
        Guid id,
        SaveAsRoutineResource request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var name = string.IsNullOrWhiteSpace(request?.Name) ? null : request.Name.Trim();
            if (name is not null && (name.Length < 2 || name.Length > 100))
            {
                return new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["name"] = ["The routine name must be between 2 and 100 characters."]
                }))
                {
                    StatusCode = StatusCodes.Status400BadRequest
                };
            }

            if (name is not null && await _routinesRepository.NameExistsAsync(
                    userId,
                    name,
                    cancellationToken: cancellationToken))
            {
                return new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["name"] = ["You already have a routine with this name."]
                }))
                {
                    StatusCode = StatusCodes.Status400BadRequest
                };
            }

            var routine = await _routinesRepository.CreateRoutineFromWorkoutAsync(
                id,
                userId,
                name,
                cancellationToken);
            if (routine is null)
            {
                return Problem(
                    title: "We couldn't find that workout.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var created = await _routinesRepository.GetRoutineAsync(
                routine.Id,
                userId,
                cancellationToken);
            Response.Headers.ETag = RoutineMapper.ETagOf(created.Version);
            return CreatedAtAction(
                "GetRoutine",
                "Routines",
                new { id = routine.Id },
                RoutineMapper.Map(created));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to save the workout as a routine.", ex);
        }
    }

    /// <summary>
    /// Planned vs completed per routine workout day (History progress view).
    /// </summary>
    [HttpGet("adherence")]
    public async Task<ActionResult<RoutineAdherenceResource>> GetRoutineAdherence(
        Guid routineId,
        string traineeId,
        DateOnly? from,
        DateOnly? to,
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

            var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var fromDate = from ?? toDate.AddDays(-30);
            if (fromDate > toDate)
            {
                return Problem(
                    title: "The start date cannot be after the end date.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var adherence = await _routineAdherenceService.GetAdherenceAsync(
                routineId,
                userId,
                fromDate,
                toDate,
                cancellationToken);
            if (adherence is null)
            {
                return Problem(
                    title: "Routine not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Ok(adherence);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load routine progress.", ex);
        }
    }

    [HttpPost("{id:guid}/exercises")]
    public async Task<ActionResult<WorkoutSessionResource>> AddExerciseToWorkout(
        Guid id,
        AddWorkoutSessionExerciseResource resource,
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
            if (!_trainingAccessService.CanAccessUser(
                    requesterUserId,
                    userId))
            {
                return Forbid();
            }

            var workout = await _workoutsRepository.GetWorkoutSessionAsync(
                id,
                userId,
                cancellationToken);
            if (workout is null)
            {
                return Problem(
                    title: "Workout not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var exerciseType = _exerciseTypesRepository.GetExerciseType(resource.TypeId);
            if (exerciseType is null)
            {
                return Problem(
                    title: "Couldn't find that exercise.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var recentTypeIds = await _workoutsRepository.GetRecentExerciseTypeIdsAsync(
                userId,
                workout.Id,
                cancellationToken: cancellationToken);

            var exercise = new Exercise
            {
                Id = Guid.NewGuid(),
                WorkoutId = id,
                TypeId = resource.TypeId,
                Series = resource.Series,
                Repetitions = resource.Repetitions,
                Weight = (double)resource.Weight,
                BodyWeight = resource.BodyWeight.HasValue
                    ? (double?)resource.BodyWeight.Value
                    : null,
                AddedWeight = resource.AddedWeight.HasValue
                    ? (double?)resource.AddedWeight.Value
                    : null,
                Comments = resource.Comments,
                Tags = ExerciseTagNormalizer.Normalize(resource.Tags),
                Type = exerciseType
            };

            _exercisesRepository.AddExercise(exercise, userId);
            workout.Exercises.Add(exercise);

            return Ok(MapSession(workout, recentTypeIds));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to add the exercise to the workout.", ex);
        }
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<DuplicatedWorkoutResource>> DuplicateWorkout(
        Guid id,
        DuplicateWorkoutResource resource,
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
            if (!_trainingAccessService.CanAccessUser(
                    requesterUserId,
                    userId))
            {
                return Forbid();
            }

            var duplicated = await _workoutsRepository
                .DuplicateWorkoutSessionAsync(
                    id,
                    userId,
                    resource.Date,
                    resource.Type,
                    resource.ExerciseIds,
                    cancellationToken);
            if (duplicated is null)
            {
                return Problem(
                    title: "Workout not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var response = new DuplicatedWorkoutResource
            {
                SourceWorkoutId = id,
                Workout = MapWorkoutHistory(duplicated, includeExercises: true)
            };

            return CreatedAtAction(
                nameof(GetWorkout),
                new { id = duplicated.Id },
                response);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to duplicate the workout.", ex);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetWorkoutResource>> GetWorkout(
        Guid id,
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
            if (!_trainingAccessService.CanAccessUser(
                    requesterUserId,
                    userId))
            {
                return Forbid();
            }

            var domainWorkout = await _workoutsRepository.GetWorkoutHistoryDetailsAsync(
                id,
                userId,
                cancellationToken);
            if (domainWorkout is not null)
            {
                return Ok(MapWorkoutHistory(domainWorkout, includeExercises: true));
            }

            return Problem(
                title: "Workout not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load the workout.", ex);
        }
    }

    [HttpPut]
    public async Task<ActionResult<GetWorkoutResource>> UpdateWorkout(
        UpdateWorkoutResource workoutResource,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var workout = await _workoutsRepository.UpdateWorkoutHistoryAsync(
                workoutResource.Id,
                userId,
                workoutResource.Date,
                workoutResource.Type,
                cancellationToken);
            if (workout is null)
            {
                return Problem(
                    title: "Workout not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Ok(MapWorkoutHistory(workout, includeExercises: true));
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Workout {WorkoutId} could not be updated because it conflicts with existing data.",
                workoutResource.Id);
            return Problem(
                title: "A workout already exists for that date.",
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to update the workout.", ex);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteWorkout(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var deleted = await _workoutsRepository.DeleteWorkoutHistoryAsync(
                id,
                userId,
                cancellationToken);
            if (!deleted)
            {
                return Problem(
                    title: "Workout not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Workout {WorkoutId} could not be deleted because it is still in use.",
                id);
            return Problem(
                title: "The workout could not be deleted.",
                detail: "Remove dependent records and try again.",
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to delete the workout.", ex);
        }
    }

    private async Task<WorkoutSessionResource> MapSessionAsync(
        Workout workout,
        string userId,
        CancellationToken cancellationToken)
    {
        var recentTypeIds = await _workoutsRepository.GetRecentExerciseTypeIdsAsync(
            userId,
            workout.Id,
            cancellationToken: cancellationToken);

        return MapSession(workout, recentTypeIds);
    }

    private static WorkoutSessionResource MapSession(
        Workout workout,
        IReadOnlyCollection<int> recentTypeIds)
    {
        return new WorkoutSessionResource
        {
            Id = workout.Id,
            Date = DateOnly.FromDateTime(workout.Date ?? DateTime.Today),
            Type = workout.Type,
            RecentExerciseTypeIds = recentTypeIds.ToList(),
            Exercises = (workout.Exercises ?? [])
                .Select(exercise => new WorkoutSessionExerciseResource
                {
                    Id = exercise.Id,
                    TypeId = exercise.TypeId ?? 0,
                    Name = exercise.Type?.Name ?? "Exercise",
                    Series = exercise.Series,
                    Repetitions = exercise.Repetitions,
                    Weight = exercise.Weight.HasValue ? (decimal?)exercise.Weight.Value : null,
                    BodyWeight = exercise.BodyWeight.HasValue
                        ? (decimal?)exercise.BodyWeight.Value
                        : null,
                    AddedWeight = exercise.AddedWeight.HasValue
                        ? (decimal?)exercise.AddedWeight.Value
                        : null,
                    Comments = exercise.Comments,
                    Tags = ExerciseTagNormalizer.Normalize(exercise.Tags)
                })
                .ToList()
        };
    }

    private static GetWorkoutResource MapWorkoutHistory(
        Workout workout,
        bool includeExercises)
    {
        var exercises = workout.Exercises ?? [];

        return new GetWorkoutResource
        {
            Id = workout.Id,
            Date = workout.Date ?? DateTime.MinValue,
            Type = workout.Type,
            UserId = workout.UserId,
            ExerciseCount = exercises.Count,
            TotalVolume = exercises.Sum(GetExerciseVolume),
            LeadingMuscles = GetLeadingMuscles(exercises),
            Tags = exercises
                .SelectMany(exercise =>
                    ExerciseTagNormalizer.Normalize(exercise.Tags))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag)
                .ToList(),
            Exercises = includeExercises
                ? exercises.Select(MapWorkoutHistoryExercise).ToList()
                : []
        };
    }

    private static WorkoutHistoryExerciseResource MapWorkoutHistoryExercise(Exercise exercise)
        => new()
        {
            Id = exercise.Id,
            TypeId = exercise.TypeId ?? 0,
            Name = exercise.Type?.Name ?? "Exercise",
            Series = exercise.Series,
            Repetitions = exercise.Repetitions,
            Weight = exercise.Weight,
            BodyWeight = exercise.BodyWeight,
            AddedWeight = exercise.AddedWeight,
            Volume = GetExerciseVolume(exercise),
            Comments = exercise.Comments,
            Tags = ExerciseTagNormalizer.Normalize(exercise.Tags),
            Muscles = GetExerciseMuscles(exercise)
        };

    private static double GetExerciseVolume(Exercise exercise)
    {
        if (exercise.Volume.HasValue)
        {
            return exercise.Volume.Value;
        }

        return (exercise.Series ?? 0)
            * (exercise.Repetitions ?? 0)
            * (exercise.Weight ?? 0);
    }

    private static List<string> GetLeadingMuscles(IEnumerable<Exercise> exercises)
    {
        var assignments = exercises
            .Where(exercise => exercise.Type is not null)
            .SelectMany(exercise => exercise.Type.MuscleAssignments ?? [])
            .Where(assignment => assignment.Muscle is not null)
            .ToList();
        var rankedAssignments = assignments.Any(assignment =>
            assignment.Role == ExerciseMuscleRole.Primary)
            ? assignments.Where(assignment => assignment.Role == ExerciseMuscleRole.Primary)
            : assignments;

        return rankedAssignments
            .GroupBy(assignment => new
            {
                assignment.MuscleId,
                assignment.Muscle.Name,
                assignment.Muscle.SortOrder
            })
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.SortOrder)
            .Select(group => group.Key.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToList();
    }

    private static List<string> GetExerciseMuscles(Exercise exercise)
    {
        var assignments = exercise.Type?.MuscleAssignments?
            .Where(assignment => assignment.Muscle is not null)
            .ToList() ?? [];
        var selectedAssignments = assignments.Any(assignment =>
            assignment.Role == ExerciseMuscleRole.Primary)
            ? assignments.Where(assignment => assignment.Role == ExerciseMuscleRole.Primary)
            : assignments;

        return selectedAssignments
            .OrderBy(assignment => assignment.SortOrder)
            .Select(assignment => assignment.Muscle.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ObjectResult Unexpected(string title, Exception ex)
    {
        _logger.LogError(ex, "{Title}", title);
        return Problem(title: title, statusCode: StatusCodes.Status500InternalServerError);
    }
}
