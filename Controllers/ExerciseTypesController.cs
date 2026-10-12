using Controllers.ApiContracts.ExerciseTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class ExerciseTypesController : ControllerBase
{
    private readonly ExerciseTypesRepository _exerciseTypeRepository;
    private readonly CurrentUserProvider _userProvider;
    private readonly TrainingAccessService _trainingAccessService;
    private readonly UsersRepository _usersRepository;
    private readonly MuscleRepository _muscleRepository;
    private readonly ILogger<ExerciseTypesController> _logger;

    public ExerciseTypesController(
        ExerciseTypesRepository exerciseTypeRepository,
        CurrentUserProvider userProvider,
        TrainingAccessService trainingAccessService,
        UsersRepository usersRepository,
        MuscleRepository muscleRepository,
        ILogger<ExerciseTypesController> logger)
    {
        _exerciseTypeRepository = exerciseTypeRepository;
        _userProvider = userProvider;
        _trainingAccessService = trainingAccessService;
        _usersRepository = usersRepository;
        _muscleRepository = muscleRepository;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<List<GetExerciseTypeResource>> GetExerciseTypes(bool? favourite, string userId = "")
    {
        try
        {
            var requesterUserId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(requesterUserId))
            {
                return Unauthorized();
            }

            var resolvedUserId = string.IsNullOrWhiteSpace(userId)
                ? requesterUserId
                : userId;
            if (!_trainingAccessService.CanAccessUser(
                    requesterUserId,
                    resolvedUserId))
            {
                return Forbid();
            }
            var domainExerciseTypes = _exerciseTypeRepository.GetExerciseTypes();
            var response = domainExerciseTypes
                .Select(exerciseType => MapExerciseType(exerciseType, resolvedUserId, includeVariations: true))
                .ToList();

            if (favourite is not null)
            {
                response = response.Where(resource => resource.IsFavourite == favourite).ToList();
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load exercise types.", ex);
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddExerciseType(AddExerciseTypeResource exerciseTypeResource)
    {
        try
        {
            var validationResult = await ValidateMuscleAssignmentsAsync(exerciseTypeResource.MuscleAssignments);
            if (validationResult is not null)
            {
                return validationResult;
            }

            var domainExerciseType = MapDomainExerciseType(exerciseTypeResource);
            var exerciseTypeId = _exerciseTypeRepository.AddExerciseType(domainExerciseType);
            return CreatedAtAction(nameof(GetExerciseType), new { id = exerciseTypeId }, MapExerciseType(domainExerciseType, null, includeVariations: false));
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to create exercise type.", ex);
        }
    }

    [HttpGet("{id}")]
    public ActionResult<GetExerciseTypeResource> GetExerciseType(int id)
    {
        try
        {
            var domainExerciseType = _exerciseTypeRepository.GetExerciseType(id);
            if (domainExerciseType is not null)
            {
                return Ok(MapExerciseType(domainExerciseType, null, includeVariations: true));
            }

            return Problem(
                title: "Couldn't find that exercise.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to load exercise type {id}.", ex);
        }
    }

    [HttpPut]
    public async Task<IActionResult> UpdateExerciseType(UpdateExerciseTypeResource exerciseTypeResource)
    {
        try
        {
            var validationResult = await ValidateMuscleAssignmentsAsync(exerciseTypeResource.MuscleAssignments);
            if (validationResult is not null)
            {
                return validationResult;
            }

            var existingExerciseType = _exerciseTypeRepository.GetExerciseType(exerciseTypeResource.Id);
            if (existingExerciseType is null)
            {
                return Problem(
                    title: "Couldn't find that exercise.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var domainExerciseType = MapDomainExerciseType(exerciseTypeResource);
            domainExerciseType.FavoritedUsers = existingExerciseType.FavoritedUsers ?? [];

            var userId = _userProvider.GetCurrentUserId();
            var existingFavourite = domainExerciseType.FavoritedUsers.FirstOrDefault(user => user.Id == userId);

            if (exerciseTypeResource.IsFavourite && existingFavourite is null)
            {
                var user = _usersRepository.GetUser(userId);
                domainExerciseType.FavoritedUsers.Add(user);
            }
            else if (!exerciseTypeResource.IsFavourite && existingFavourite is not null)
            {
                domainExerciseType.FavoritedUsers.Remove(existingFavourite);
            }

            _exerciseTypeRepository.UpdateExerciseType(domainExerciseType);
            return Ok("ExerciseType updated successfully");
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to update exercise type {exerciseTypeResource.Id}.", ex);
        }
    }

    [HttpPut("{id:int}/favourite")]
    public IActionResult SetFavourite(int id, SetExerciseTypeFavouriteResource resource)
    {
        try
        {
            var userId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var user = _usersRepository.GetUser(userId);
            if (user is null)
            {
                return Unauthorized();
            }

            if (!_exerciseTypeRepository.SetFavourite(id, user, resource.IsFavourite))
            {
                return Problem(
                    title: "Couldn't find that exercise.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to update favourite for exercise type {id}.", ex);
        }
    }

    [HttpDelete("{id}")]
    public ActionResult DeleteExerciseType(int id)
    {
        try
        {
            return _exerciseTypeRepository.DeleteExerciseType(id) switch
            {
                DeleteExerciseTypeResult.Deleted => NoContent(),
                DeleteExerciseTypeResult.NotFound => Problem(
                    title: "Couldn't find that exercise.",
                    statusCode: StatusCodes.Status404NotFound),
                DeleteExerciseTypeResult.HasVariations => Problem(
                    title: "This exercise has variations you train",
                    detail: "Reassign or delete the variations of this exercise first.",
                    statusCode: StatusCodes.Status409Conflict),
                DeleteExerciseTypeResult.InUseByExercises => Problem(
                    title: "This exercise has workout history",
                    detail: "Keep this exercise in the catalog because of existing workout history.",
                    statusCode: StatusCodes.Status409Conflict),
                DeleteExerciseTypeResult.InUseByWorkoutTemplates => Problem(
                    title: "This exercise is used in a workout template",
                    detail: "Remove this exercise from the workout template before deleting it.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Unexpected($"Failed to delete exercise type {id}.", new InvalidOperationException($"Unknown delete result for exercise type {id}."))
            };
        }
        catch (Exception ex)
        {
            return Unexpected($"Failed to delete exercise type {id}.", ex);
        }
    }

    private async Task<ActionResult> ValidateMuscleAssignmentsAsync(IEnumerable<ExerciseTypeMuscleAssignmentResource> assignments)
    {
        var normalizedAssignments = (assignments ?? [])
            .Where(assignment => assignment.MuscleId > 0)
            .Select(assignment => new ExerciseTypeMuscleAssignmentResource
            {
                MuscleId = assignment.MuscleId,
                Role = NormalizeRole(assignment.Role),
                SortOrder = assignment.SortOrder
            })
            .ToList();

        if (normalizedAssignments.Count == 0)
        {
            return ValidationProblem(
                "At least one muscle assignment is required.");
        }

        if (!normalizedAssignments.Any(assignment => string.Equals(assignment.Role, nameof(ExerciseMuscleRole.Primary), StringComparison.OrdinalIgnoreCase)))
        {
            return ValidationProblem(
                "At least one primary muscle assignment is required.");
        }

        if (normalizedAssignments.Any(assignment => !string.Equals(assignment.Role, nameof(ExerciseMuscleRole.Primary), StringComparison.OrdinalIgnoreCase) &&
                                                    !string.Equals(assignment.Role, nameof(ExerciseMuscleRole.Secondary), StringComparison.OrdinalIgnoreCase)))
        {
            return ValidationProblem(
                "Muscle assignment role must be Primary or Secondary.");
        }

        var duplicateMuscleIds = normalizedAssignments
            .GroupBy(assignment => assignment.MuscleId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateMuscleIds.Count > 0)
        {
            return ValidationProblem(
                "An exercise cannot assign the same muscle more than once.");
        }

        var allExist = await _muscleRepository.AllExistAsync(normalizedAssignments.Select(assignment => assignment.MuscleId));
        if (!allExist)
        {
            return ValidationProblem(
                "One or more selected muscles do not exist.");
        }

        return null;
    }

    private ObjectResult ValidationProblem(string title)
        => Problem(
            title: title,
            statusCode: StatusCodes.Status400BadRequest);

    private static bool IsFavouriteExType(ExerciseType exerciseType, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || exerciseType.FavoritedUsers is null)
        {
            return false;
        }

        return exerciseType.FavoritedUsers.Any(user => user.Id == userId);
    }

    private GetExerciseTypeResource MapExerciseType(ExerciseType exerciseType, string userId, bool includeVariations)
    {
        var resource = new GetExerciseTypeResource
        {
            Id = exerciseType.Id,
            Name = exerciseType.Name,
            Description = exerciseType.Description,
            ImageRoute = exerciseType.ImageRoute,
            WeightType = exerciseType.WeightType,
            Tags = MapTags(exerciseType.Tags),
            ParentExerciseTypeId = exerciseType.ParentExerciseTypeId,
            IsFavourite = IsFavouriteExType(exerciseType, userId ?? string.Empty),
            MuscleAssignments = MapAssignments(exerciseType.MuscleAssignments)
        };

        if (includeVariations)
        {
            resource.Variations = (exerciseType.Variations ?? [])
                .OrderBy(variation => variation.Name)
                .Select(variation => MapExerciseType(variation, userId, includeVariations: false))
                .ToList();
        }

        return resource;
    }

    private static ExerciseType MapDomainExerciseType(AddExerciseTypeResource resource)
        => new()
        {
            Name = resource.Name,
            Description = resource.Description,
            ImageRoute = resource.ImageRoute,
            WeightType = resource.WeightType,
            ParentExerciseTypeId = resource.ParentExerciseTypeId,
            Tags = MapTags(resource.Tags),
            MuscleAssignments = MapAssignments(resource.MuscleAssignments)
        };

    private static ExerciseType MapDomainExerciseType(UpdateExerciseTypeResource resource)
        => new()
        {
            Id = resource.Id,
            Name = resource.Name,
            Description = resource.Description,
            ImageRoute = resource.ImageRoute,
            WeightType = resource.WeightType,
            ParentExerciseTypeId = resource.ParentExerciseTypeId,
            Tags = MapTags(resource.Tags),
            MuscleAssignments = MapAssignments(resource.MuscleAssignments)
        };

    private static List<ExerciseTypeMuscleAssignment> MapAssignments(IEnumerable<ExerciseTypeMuscleAssignmentResource> assignments)
    {
        var normalizedAssignments = (assignments ?? [])
            .Where(assignment => assignment.MuscleId > 0)
            .OrderBy(assignment => NormalizeRole(assignment.Role))
            .ThenBy(assignment => assignment.SortOrder)
            .ThenBy(assignment => assignment.MuscleId)
            .ToList();

        var roleIndex = new Dictionary<ExerciseMuscleRole, int>();

        return normalizedAssignments
            .Select(assignment =>
            {
                var role = ParseRole(assignment.Role);
                if (assignment.SortOrder <= 0)
                {
                    roleIndex[role] = roleIndex.GetValueOrDefault(role) + 1;
                }

                return new ExerciseTypeMuscleAssignment
                {
                    MuscleId = assignment.MuscleId,
                    Role = role,
                    SortOrder = assignment.SortOrder > 0 ? assignment.SortOrder : roleIndex[role]
                };
            })
            .ToList();
    }

    private static List<ExerciseTagResource> MapTags(IEnumerable<ExerciseTag> tags)
        => (tags ?? [])
            .Select(MapTag)
            .ToList();

    private static List<ExerciseTag> MapTags(IEnumerable<ExerciseTagResource> tags)
        => (tags ?? [])
            .Select(MapTag)
            .ToList();

    private static List<ExerciseTypeMuscleAssignmentResource> MapAssignments(IEnumerable<ExerciseTypeMuscleAssignment> assignments)
        => (assignments ?? [])
            .OrderBy(assignment => assignment.Role)
            .ThenBy(assignment => assignment.SortOrder)
            .ThenBy(assignment => assignment.MuscleId)
            .Select(MapAssignment)
            .ToList();

    private static ExerciseTagResource MapTag(ExerciseTag tag)
        => new()
        {
            Category = tag.Category ?? string.Empty,
            Name = tag.Name ?? string.Empty
        };

    private static ExerciseTag MapTag(ExerciseTagResource tag)
        => new()
        {
            Category = tag.Category ?? string.Empty,
            Name = tag.Name ?? string.Empty
        };

    private static ExerciseTypeMuscleAssignmentResource MapAssignment(ExerciseTypeMuscleAssignment assignment)
        => new()
        {
            MuscleId = assignment.MuscleId,
            Role = assignment.Role.ToString(),
            SortOrder = assignment.SortOrder
        };

    private static ExerciseMuscleRole ParseRole(string role)
        => string.Equals(role, nameof(ExerciseMuscleRole.Secondary), StringComparison.OrdinalIgnoreCase)
            ? ExerciseMuscleRole.Secondary
            : ExerciseMuscleRole.Primary;

    private static string NormalizeRole(string role)
        => string.Equals(role, nameof(ExerciseMuscleRole.Secondary), StringComparison.OrdinalIgnoreCase)
            ? nameof(ExerciseMuscleRole.Secondary)
            : nameof(ExerciseMuscleRole.Primary);

    private ObjectResult Unexpected(string title, Exception ex)
    {
        _logger.LogError(ex, "{Title}", title);
        return Problem(title: title, statusCode: StatusCodes.Status500InternalServerError);
    }
}
