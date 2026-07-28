using healthspan_api.Persistence.Repositories;
using Controllers.ApiContracts.ExerciseTypes;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class ExercisesController : ControllerBase
{
    private readonly ExercisesRepository _exerciseRepository;
    private readonly WorkoutsRepository _workoutsRepository;
    private readonly ExerciseTypesRepository _exerciseTypesRepository;
    private readonly ExerciseMetricsService _exercisesService;
    private readonly CurrentUserProvider _userProvider;
    private readonly ILogger<ExercisesController> _logger;

    public ExercisesController(ExercisesRepository exerciseRepository, WorkoutsRepository workoutsRepository, ExerciseTypesRepository exerciseTypesRepository, ExerciseMetricsService exercisesService, CurrentUserProvider userProvider, ILogger<ExercisesController> logger)
    {
        _exerciseRepository = exerciseRepository;
        _workoutsRepository = workoutsRepository;
        _exerciseTypesRepository = exerciseTypesRepository;
        _exercisesService = exercisesService;
        _userProvider = userProvider;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<object> GetExercises(
        int? exerciseTypeId,
        string userId = "",
        [FromQuery(Name = "muscleGroup")] List<string> muscleGroup = null,
        int? page = null,
        int? pageSize = null,
        string sortBy = "date",
        bool descending = true,
        string tag = "")
    {
        try
        {
            userId = ResolveUserId(userId);

            if (page.HasValue != pageSize.HasValue)
            {
                return BadRequest("Both page and pageSize must be specified together.");
            }

            if ((page.HasValue && page <= 0) || (pageSize.HasValue && pageSize <= 0))
            {
                return BadRequest("page and pageSize must be greater than zero.");
            }

            var exercises = exerciseTypeId.HasValue
                ? _exerciseRepository.GetExercisesByType(
                    exerciseTypeId.Value,
                    userId,
                    tag)
                : _exerciseRepository.GetExercises(userId, tag);

            var selectedGroups = ParseMuscleGroups(muscleGroup);
            if (selectedGroups.Count > 0)
            {
                exercises = exercises
                    .Where(exercise => exercise.Type?.MuscleAssignments.Any(assignment =>
                        assignment.Muscle is not null &&
                        selectedGroups.Contains(assignment.Muscle.MuscleGroup)) == true)
                    .ToList();
            }

            var mappedExercises = exercises.Select(MapExercise).ToList();
            var normalizedSortBy = sortBy?.Trim().ToLowerInvariant();

            IEnumerable<GetExerciseResource> sortedExercises = normalizedSortBy switch
            {
                "date" => descending
                    ? mappedExercises.OrderByDescending(GetWorkoutDateSortKey).ThenByDescending(GetExerciseTypeNameSortKey)
                    : mappedExercises.OrderBy(GetWorkoutDateSortKey).ThenBy(GetExerciseTypeNameSortKey),
                "name" => descending
                    ? mappedExercises.OrderByDescending(GetExerciseTypeNameSortKey).ThenByDescending(GetWorkoutDateSortKey)
                    : mappedExercises.OrderBy(GetExerciseTypeNameSortKey).ThenBy(GetWorkoutDateSortKey),
                "type" => descending
                    ? mappedExercises.OrderByDescending(GetExerciseTypeNameSortKey).ThenByDescending(GetWorkoutDateSortKey)
                    : mappedExercises.OrderBy(GetExerciseTypeNameSortKey).ThenBy(GetWorkoutDateSortKey),
                _ => descending
                    ? mappedExercises.OrderByDescending(GetWorkoutDateSortKey).ThenByDescending(GetExerciseTypeNameSortKey)
                    : mappedExercises.OrderBy(GetWorkoutDateSortKey).ThenBy(GetExerciseTypeNameSortKey)
            };

            var usePagination = page.HasValue && pageSize.HasValue;
            if (usePagination)
            {
                var currentPage = page!.Value;
                var currentPageSize = pageSize!.Value;
                var totalCount = sortedExercises.Count();
                var totalPages = (int)Math.Ceiling(totalCount / (double)currentPageSize);

                var pagedExercises = sortedExercises
                    .Skip((currentPage - 1) * currentPageSize)
                    .Take(currentPageSize)
                    .ToList();

                return Ok(new PagedResult<GetExerciseResource>
                {
                    Items = pagedExercises,
                    TotalCount = totalCount,
                    PageSize = currentPageSize,
                    CurrentPage = currentPage,
                    TotalPages = totalPages
                });
            }

            return Ok(sortedExercises.ToList());
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load exercises.", ex);
        }
    }

    [HttpPost]
    public IActionResult AddExercise(AddExerciseResource exerciseResource, string userId = "")
    {
        try
        {
            userId = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId() : userId;
            var domainExercise = exerciseResource.Adapt<Exercise>();
            domainExercise.TypeId = exerciseResource.TypeId;
            domainExercise.WorkoutId = exerciseResource.WorkoutId;
            domainExercise.Tags = ExerciseTagNormalizer.Normalize(
                exerciseResource.Tags);
            var exerciseId = _exerciseRepository.AddExercise(domainExercise, userId);
            return CreatedAtAction(nameof(GetExercise), new { id = exerciseId }, new { Id = exerciseId });
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to create exercise.", ex);
        }
    }

    [HttpGet("{id}")]
    public ActionResult<GetExerciseResource> GetExercise(Guid id)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var domainExercise = _exerciseRepository.GetExercise(id, userId);
            if (domainExercise is not null)
            {
                return Ok(MapExercise(domainExercise));
            }

            return Problem(
                title: "Exercise not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to load exercise {id}.", ex);
        }
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteExercise(Guid id)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            if (!_exerciseRepository.DeleteExercise(id, userId))
            {
                return Problem(
                    title: "Exercise not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to delete exercise {id}.", ex);
        }
    }

    [HttpGet("{id}/metrics")]
    public ActionResult<GetExerciseMetrics> GetExerciseMetrics(Guid id, string userId = "")
    {
        try
        {
            userId = ResolveUserId(userId);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var domainExercise = _exerciseRepository.GetExercise(id, userId);
            if (domainExercise is null)
            {
                return Problem(
                    title: "Exercise not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var exerciseMetrics = domainExercise.Adapt<GetExerciseMetrics>();
            return Ok(exerciseMetrics);
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to load exercise metrics for {id}.", ex);
        }
    }

    [HttpPut]
    public ActionResult<GetExerciseResource> UpdateExercise(
        UpdateExerciseResource exerciseResource)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var result = _exerciseRepository.UpdateExercise(
                new ExerciseUpdateCommand(
                    exerciseResource.Id,
                    exerciseResource.Series,
                    exerciseResource.Repetitions,
                    exerciseResource.Weight.HasValue
                        ? (double?)exerciseResource.Weight.Value
                        : null,
                    exerciseResource.Comments,
                    exerciseResource.Tags,
                    exerciseResource.Type?.Id,
                    exerciseResource.Workout?.Id),
                userId);

            return result.Status switch
            {
                ExerciseUpdateStatus.Updated => Ok(MapExercise(result.Exercise)),
                ExerciseUpdateStatus.NotFound => Problem(
                    title: "Exercise not found.",
                    statusCode: StatusCodes.Status404NotFound),
                ExerciseUpdateStatus.WorkoutNotFound => Problem(
                    title: "The selected workout is unavailable.",
                    detail: "Reload the exercise and try again.",
                    statusCode: StatusCodes.Status409Conflict),
                ExerciseUpdateStatus.ExerciseTypeNotFound => Problem(
                    title: "The selected exercise type is unavailable.",
                    detail: "Choose another exercise type and try again.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Problem(
                    title: "The exercise could not be updated.",
                    statusCode: StatusCodes.Status409Conflict)
            };
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to update exercise {exerciseResource.Id}.", ex);
        }
    }

    [HttpGet("{exerciseTypeId}/kpis")]
    public ActionResult<GetExerciseTypeResource> GetExerciseTypeKpis(
        int exerciseTypeId,
        string userId = "",
        string tag = "")
    {
        try
        {
            userId = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId() : userId;
            var exerciseTypeKpis = _exercisesService.GetExerciseTypeKpisOfUser(
                exerciseTypeId,
                userId,
                tag);
            if (exerciseTypeKpis is not null)
            {
                return Ok(exerciseTypeKpis);
            }

            return NotFound($"ExerciseType with ID {exerciseTypeId} not found");
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to load KPIs for exercise type {exerciseTypeId}.", ex);
        }
    }

    [HttpGet("{exerciseTypeId}/trends")]
    public ActionResult<GetExerciseTypeResource> GetExerciseTypetrends(
        int exerciseTypeId,
        TrendKpiType type,
        string userId = "",
        string tag = "")
    {
        try
        {
            userId = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId() : userId;
            var exerciseTypeTrends = _exercisesService
                .GetExerciseTypeTrendsOfUser(
                    exerciseTypeId,
                    type,
                    userId,
                    tag);
            if (exerciseTypeTrends is not null)
            {
                return Ok(exerciseTypeTrends);
            }

            return NotFound($"ExerciseType with ID {exerciseTypeId} not found");
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to load trends for exercise type {exerciseTypeId}.", ex);
        }
    }

    private string ResolveUserId(string userId)
        => string.IsNullOrWhiteSpace(userId)
            ? _userProvider.GetCurrentUserId() ?? string.Empty
            : userId;

    private static HashSet<MuscleGroup> ParseMuscleGroups(IEnumerable<string> muscleGroups)
        => (muscleGroups ?? [])
            .Select(value => Enum.TryParse<MuscleGroup>(value, ignoreCase: true, out var group) ? group : (MuscleGroup?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToHashSet();

    private static GetExerciseResource MapExercise(Exercise exercise)
        => new()
        {
            Id = exercise.Id,
            Series = exercise.Series,
            Repetitions = exercise.Repetitions,
            Weight = exercise.Weight.HasValue ? (decimal?)exercise.Weight.Value : null,
            Comments = exercise.Comments,
            Tags = ExerciseTagNormalizer.Normalize(exercise.Tags),
            MaxRepetition = exercise.MaxRepetition,
            Volume = exercise.Volume,
            Intensity = exercise.Intensity,
            Type = exercise.Type is null ? null : MapExerciseType(exercise.Type),
            Workout = exercise.Workout?.Adapt<GetWorkoutResource>()
        };

    private static GetExerciseTypeResource MapExerciseType(ExerciseType exerciseType)
        => new()
        {
            Id = exerciseType.Id,
            Name = exerciseType.Name,
            Description = exerciseType.Description,
            ImageRoute = exerciseType.ImageRoute,
            WeightType = exerciseType.WeightType,
            ParentExerciseTypeId = exerciseType.ParentExerciseTypeId,
            Tags = (exerciseType.Tags ?? [])
                .Select(tag => new ExerciseTagResource
                {
                    Category = tag.Category ?? string.Empty,
                    Name = tag.Name ?? string.Empty
                })
                .ToList(),
            MuscleAssignments = (exerciseType.MuscleAssignments ?? [])
                .OrderBy(assignment => assignment.Role)
                .ThenBy(assignment => assignment.SortOrder)
                .ThenBy(assignment => assignment.MuscleId)
                .Select(assignment => new ExerciseTypeMuscleAssignmentResource
                {
                    MuscleId = assignment.MuscleId,
                    Role = assignment.Role.ToString(),
                    SortOrder = assignment.SortOrder
                })
                .ToList()
        };

    private static DateTime GetWorkoutDateSortKey(GetExerciseResource exercise)
        => exercise.Workout?.Date ?? DateTime.MinValue;

    private static string GetExerciseTypeNameSortKey(GetExerciseResource exercise)
        => exercise.Type?.Name ?? string.Empty;

    private ObjectResult Unexpected(string title, Exception ex)
    {
        _logger.LogError(ex, "{Title}", title);
        return Problem(title: title, statusCode: StatusCodes.Status500InternalServerError);
    }

    public enum TrendKpiType
    {
        RM,
        Volume,
        Intensity
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; }
        public int TotalCount { get; set; }
        public int PageSize { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
    }
}
