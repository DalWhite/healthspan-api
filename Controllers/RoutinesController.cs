using Mapster;
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
    private readonly ILogger<RoutinesController> _logger;

    public RoutinesController(
        RoutinesRepository routineRepository,
        CurrentUserProvider userProvider,
        TrainingAccessService trainingAccessService,
        ExerciseTypesRepository exerciseTypesRepository,
        ILogger<RoutinesController> logger)
    {
        _routinesRepository = routineRepository;
        _userProvider = userProvider;
        _trainingAccessService = trainingAccessService;
        _exerciseTypesRepository = exerciseTypesRepository;
        _logger = logger;
    }
    [HttpGet]
    public ActionResult<List<GetRoutineResource>> GetRoutines(string traineeId)
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

            var domainRoutines = _routinesRepository.GetRoutines(userId);
            var response = MapToGetRoutineResources(domainRoutines);

            return Ok(response);
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to load routines.", ex);
        }
    }

    [HttpPost]
    public IActionResult AddRoutine(AddRoutineResource routineResource)
    {
        try
        {
            var requesterUserId = _userProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(requesterUserId))
            {
                return Unauthorized();
            }

            routineResource.UserId = string.IsNullOrWhiteSpace(
                routineResource.UserId)
                    ? requesterUserId
                    : routineResource.UserId;
            if (!_trainingAccessService.CanAccessUser(
                    requesterUserId,
                    routineResource.UserId))
            {
                return Forbid();
            }

            var workoutTemplates = routineResource.WorkoutTemplates
                .Select(templateResource =>
                {
                    var exerciseTypes = _exerciseTypesRepository.GetExerciseTypesByIds(templateResource.ExerciseTypesIds);

                    return new WorkoutTemplate
                    {
                        Type = templateResource.Type,
                        DaysOfWeek = templateResource.Frequency,
                        ExerciseTypes = exerciseTypes
                    };
                }).ToList();

            var domainRoutine = new Routine
            {
                Name = routineResource.Name,
                Description = routineResource.Description,
                UserId = routineResource.UserId,
                WorkoutTemplates = workoutTemplates
            };

            var routineId = _routinesRepository.AddRoutine(domainRoutine);

            return Created();
        }
        catch (Exception ex)
        {
            return Unexpected("Failed to create the routine.", ex);
        }
    }
    private List<GetRoutineResource> MapToGetRoutineResources(List<Routine> routines)
    {
        var routineResources = new List<GetRoutineResource>();

        foreach (var routine in routines)
        {
            var routineResource = new GetRoutineResource
            {
                Name = routine.Name,
                Description = routine.Description,
                UserId = routine.UserId,
                WorkoutTemplates = routine.WorkoutTemplates.Select(wt => new WorkoutTemplateResource
                {
                    Type = wt.Type,
                    Frequency = wt.DaysOfWeek,
                    ExerciseTypesIds = wt.ExerciseTypes.Select(et => et.Id).ToList()
                }).ToList()
            };

            routineResources.Add(routineResource);
        }

        return routineResources;
    }

    private ObjectResult Unexpected(string title, Exception ex)
    {
        _logger.LogError(ex, "{Title}", title);
        return Problem(
            title: title,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
