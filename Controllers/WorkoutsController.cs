using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly ILogger<WorkoutsController> _logger;

    public WorkoutsController(
        WorkoutsRepository workoutRepository,
        ExercisesRepository exercisesRepository,
        ExerciseTypesRepository exerciseTypesRepository,
        CurrentUserProvider userProvider,
        ILogger<WorkoutsController> logger)
    {
        _workoutsRepository = workoutRepository;
        _exercisesRepository = exercisesRepository;
        _exerciseTypesRepository = exerciseTypesRepository;
        _userProvider = userProvider;
        _logger = logger;
    }
    [HttpGet]
    public ActionResult<List<GetWorkoutResource>> GetWorkouts(string traineeId)
    {
        try
        {
            var userId = string.IsNullOrEmpty(traineeId) ? _userProvider.GetCurrentUserId(): traineeId;
            var domainWorkouts = _workoutsRepository.GetWorkouts(userId);
            var response = domainWorkouts.Adapt<List<GetWorkoutResource>>();
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpPost]
    public IActionResult AddWorkout(AddWorkoutResource workoutResource)
    {
        try
        {
            workoutResource.UserId ??= _userProvider.GetCurrentUserId();
            var domainWorkout = workoutResource.Adapt<Workout>();
            var workoutId = _workoutsRepository.AddWorkout(domainWorkout);
            var response = domainWorkout.Adapt<GetWorkoutResource>();
            return CreatedAtAction(nameof(GetWorkout), new { id = workoutId }, response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
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

            var result = await _workoutsRepository.GetOrCreateWorkoutSessionAsync(
                userId,
                resource.Date,
                resource.Type,
                cancellationToken);
            var response = await MapSessionAsync(result.Workout, userId, cancellationToken);

            return result.Created
                ? CreatedAtAction(nameof(GetWorkout), new { id = result.Workout.Id }, response)
                : Ok(response);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to start today's workout.", ex);
        }
    }

    [HttpPost("{id:guid}/exercises")]
    public async Task<ActionResult<WorkoutSessionResource>> AddExerciseToWorkout(
        Guid id,
        AddWorkoutSessionExerciseResource resource,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var workout = await _workoutsRepository.GetWorkoutSessionAsync(
                id,
                userId,
                cancellationToken);
            if (workout is null)
            {
                return NotFound($"Workout with ID {id} not found");
            }

            var exerciseType = _exerciseTypesRepository.GetExerciseType(resource.TypeId);
            if (exerciseType is null)
            {
                return BadRequest($"ExerciseType with ID {resource.TypeId} not found");
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
                Comments = resource.Comments,
                Type = exerciseType
            };

            _exercisesRepository.AddExercise(exercise, userId);
            workout.Exercises.Add(exercise);

            return Ok(MapSession(workout, recentTypeIds));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to add the exercise to today's workout.", ex);
        }
    }

    [HttpGet("{id}")]
    public ActionResult<GetWorkoutResource> GetWorkout(Guid id)
    {
        try
        {
            var domainWorkout = _workoutsRepository.GetWorkout(id);
            if (domainWorkout is not null)
            {
                var response = domainWorkout.Adapt<GetWorkoutResource>();
                return Ok(response);
            }
            else
            {
                return NotFound($"Workout with ID {id} not found");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpPut]
    public IActionResult UpdateWorkout(UpdateWorkoutResource workoutResource)
    {
        try
        {
            var domainWorkout = workoutResource.Adapt<Workout>();
            var existingWorkout = _workoutsRepository.GetWorkout(domainWorkout.Id);
            if (existingWorkout is not null)
            {
                _workoutsRepository.UpdateWorkout(domainWorkout);
                return Ok("Workout updated successfully");
            }
            else
            {
                return NotFound($"Workout with ID {domainWorkout.Id} not found");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
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
                    Comments = exercise.Comments
                })
                .ToList()
        };
    }

    private ObjectResult Unexpected(string title, Exception ex)
    {
        _logger.LogError(ex, "{Title}", title);
        return Problem(title: title, statusCode: StatusCodes.Status500InternalServerError);
    }
}
