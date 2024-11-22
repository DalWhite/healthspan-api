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

    public RoutinesController(RoutinesRepository routineRepository, CurrentUserProvider userProvider, ExerciseTypesRepository exerciseTypesRepository)
    {
        _routinesRepository = routineRepository;
        _userProvider = userProvider;
        _exerciseTypesRepository = exerciseTypesRepository;
    }
    [HttpGet]
    public ActionResult<List<GetRoutineResource>> GetRoutines(string traineeId)
    {
        try
        {
            var userId = string.IsNullOrEmpty(traineeId) ? _userProvider.GetCurrentUserId() : traineeId;
            var domainRoutines = _routinesRepository.GetRoutines(userId);
            var response = MapToGetRoutineResources(domainRoutines);

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpPost]
    public IActionResult AddRoutine(AddRoutineResource routineResource)
    {
        try
        {
            routineResource.UserId ??= _userProvider.GetCurrentUserId();

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
            return BadRequest($"Error: {ex.Message}");
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

}
