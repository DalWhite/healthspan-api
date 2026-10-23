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
    private readonly ILogger<ExercisesController> _logger;

    public ExercisesController(ExercisesRepository exerciseRepository, WorkoutsRepository workoutsRepository, ExerciseTypesRepository exerciseTypesRepository, ExerciseMetricsService exercisesService, ILogger<ExercisesController> logger)
    {
        _exerciseRepository = exerciseRepository;
        _workoutsRepository = workoutsRepository;
        _exerciseTypesRepository = exerciseTypesRepository;
        _exercisesService = exercisesService;
        _logger = logger;
    }

    [HttpGet]
    [TraineeAccess]
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
            if (page.HasValue != pageSize.HasValue)
            {
                return Problem(
                    title: "Page and page size must be specified together.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if ((page.HasValue && page <= 0) || (pageSize.HasValue && pageSize <= 0))
            {
                return Problem(
                    title: "Page and page size must be greater than zero.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (pageSize > 100)
            {
                return Problem(
                    title: "Page size must be between 1 and 100.",
                    statusCode: StatusCodes.Status400BadRequest);
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
    [TraineeAccess]
    public async Task<IActionResult> AddExercise(
        AddExerciseResource exerciseResource,
        string userId = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var workout = await _workoutsRepository
                .GetWorkoutSessionAsync(
                    exerciseResource.WorkoutId,
                    userId,
                    cancellationToken);
            if (workout is null)
            {
                return Problem(
                    title: "The selected workout is unavailable.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var domainExercise = exerciseResource.Adapt<Exercise>();
            domainExercise.TypeId = exerciseResource.TypeId;
            domainExercise.WorkoutId = exerciseResource.WorkoutId;
            domainExercise.BodyWeight = exerciseResource.BodyWeight.HasValue
                ? (double?)exerciseResource.BodyWeight.Value
                : null;
            domainExercise.AddedWeight = exerciseResource.AddedWeight.HasValue
                ? (double?)exerciseResource.AddedWeight.Value
                : null;
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
    [TraineeAccess]
    public ActionResult<GetExerciseResource> GetExercise(
        Guid id,
        string userId = "")
    {
        try
        {
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
    [TraineeAccess]
    public IActionResult DeleteExercise(
        Guid id,
        string userId = "")
    {
        try
        {
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
    [TraineeAccess]
    public ActionResult<GetExerciseMetrics> GetExerciseMetrics(Guid id, string userId = "")
    {
        try
        {
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
    [TraineeAccess]
    public ActionResult<GetExerciseResource> UpdateExercise(
        UpdateExerciseResource exerciseResource,
        string userId = "")
    {
        try
        {
            var result = _exerciseRepository.UpdateExercise(
                new ExerciseUpdateCommand(
                    exerciseResource.Id,
                    exerciseResource.Series,
                    exerciseResource.Repetitions,
                    exerciseResource.Weight.HasValue
                        ? (double?)exerciseResource.Weight.Value
                        : null,
                    exerciseResource.BodyWeight.HasValue
                        ? (double?)exerciseResource.BodyWeight.Value
                        : null,
                    exerciseResource.AddedWeight.HasValue
                        ? (double?)exerciseResource.AddedWeight.Value
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
                    title: "The selected exercise is unavailable.",
                    detail: "Choose another exercise and try again.",
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
    [TraineeAccess]
    public ActionResult<GetExerciseTypeResource> GetExerciseTypeKpis(
        int exerciseTypeId,
        string userId = "",
        string tag = "")
    {
        try
        {
            var exerciseTypeKpis = _exercisesService.GetExerciseTypeKpisOfUser(
                exerciseTypeId,
                userId,
                tag);
            if (exerciseTypeKpis is not null)
            {
                return Ok(exerciseTypeKpis);
            }

            return Problem(
                title: "Couldn't find that exercise.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to load KPIs for exercise type {exerciseTypeId}.", ex);
        }
    }

    [HttpGet("{exerciseTypeId}/trends")]
    [TraineeAccess]
    public ActionResult<GetExerciseTypeResource> GetExerciseTypetrends(
        int exerciseTypeId,
        TrendKpiType type,
        string userId = "",
        string tag = "")
    {
        try
        {
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

            return Problem(
                title: "Couldn't find that exercise.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to load trends for exercise type {exerciseTypeId}.", ex);
        }
    }

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
            BodyWeight = exercise.BodyWeight.HasValue
                ? (decimal?)exercise.BodyWeight.Value
                : null,
            AddedWeight = exercise.AddedWeight.HasValue
                ? (decimal?)exercise.AddedWeight.Value
                : null,
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
