using healthspan_api.Persistence.Repositories;
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

    public ExercisesController(ExercisesRepository exerciseRepository, WorkoutsRepository workoutsRepository, ExerciseTypesRepository exerciseTypesRepository, ExerciseMetricsService exercisesService, CurrentUserProvider userProvider)
    {
        _exerciseRepository = exerciseRepository;
        _workoutsRepository = workoutsRepository;
        _exerciseTypesRepository = exerciseTypesRepository;
        _exercisesService = exercisesService;
        _userProvider = userProvider;
    }

    [HttpGet]
    public ActionResult<List<GetExerciseResource>> GetExercises(int? exerciseTypeId, string userId)
    {
        try
        {
            userId = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId() : userId;
            var exercises = new List<Exercise>();
            if (exerciseTypeId.HasValue)
            {   
                exercises = _exerciseRepository.GetExercisesByType(exerciseTypeId.Value, userId);
            }
            if(!exerciseTypeId.HasValue)
            {
                exercises = _exerciseRepository.GetExercises(userId);
            }
            
            var response = exercises.Adapt<List<GetExerciseResource>>().OrderBy(e => e.Workout.Date);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
    [HttpPost]
    public IActionResult AddExercise(AddExerciseResource exerciseResource, string userId)
    {
        try
        {
            userId = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId() : userId;
            var domainExercise = exerciseResource.Adapt<Exercise>();
            domainExercise.TypeId = exerciseResource.TypeId;
            domainExercise.WorkoutId = exerciseResource.WorkoutId;
            var exerciseId = _exerciseRepository.AddExercise(domainExercise, userId);
            return CreatedAtAction(nameof(GetExercise), new{id = exerciseId}, domainExercise);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpGet("{id}")]
    public ActionResult<GetExerciseResource> GetExercise(Guid id)
    {
        try
        {
            var domainExercise = _exerciseRepository.GetExercise(id);
            if (domainExercise is not null)
            {
                var response = domainExercise.Adapt<GetExerciseResource>();
                return Ok(response);
            }
            else
            {
                return NotFound($"Exercise with ID {id} not found");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
    [HttpDelete("{id}")]
    public ActionResult<GetExerciseResource> DeleteExercise(Guid id)
    {
        try
        {
            _exerciseRepository.DeleteExercise(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
    [HttpGet("{id}/metrics")]
    public ActionResult<GetExerciseMetrics> GetExerciseMetrics(Guid id, string userId)
    {
        try
        {

            var domainExercise = _exerciseRepository.GetExercise(id);

            var exerciseMetrics = domainExercise.Adapt<GetExerciseMetrics>();
            return Ok(exerciseMetrics);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpPut]
    public IActionResult UpdateExercise(UpdateExerciseResource exerciseResource, string userId)
    {
        try
        {
            userId = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId() : userId;
            var domainExercise = exerciseResource.Adapt<Exercise>();
            var existingExercise = _exerciseRepository.GetExercise(domainExercise.Id);
            if (existingExercise is not null)
            {
                _exerciseRepository.UpdateExercise(domainExercise, userId);
                return Ok("Exercise updated successfully");
            }
            else
            {
                return NotFound($"Exercise with ID {domainExercise.Id} not found");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
    
    [HttpGet("{exerciseTypeId}/kpis")]
    public ActionResult<GetExerciseTypeResource> GetExerciseTypeKpis(int exerciseTypeId, string userId)
    {
        try
        {
            userId = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId() : userId;
            var exerciseTypeKpis = _exercisesService.GetExerciseTypeKpisOfUser(exerciseTypeId, userId);
            if (exerciseTypeKpis is not null)
            {
                var response = exerciseTypeKpis;
                return Ok(response);
            }
            else
            {
                return NotFound($"ExerciseType with ID {exerciseTypeId} not found");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
    [HttpGet("{exerciseTypeId}/trends")]
    public ActionResult<GetExerciseTypeResource> GetExerciseTypetrends(int exerciseTypeId, TrendKpiType type, string userId)
    {
        try
        {
            userId = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId() : userId;
            var exerciseTypeTrends =  _exercisesService.GetExerciseTypeTrendsOfUser(exerciseTypeId, type, userId);
            if (exerciseTypeTrends is not null)
            {
                var response = exerciseTypeTrends;
                return Ok(response);
            }
            else
            {
                return NotFound($"ExerciseType with ID {exerciseTypeId} not found");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    public enum TrendKpiType
    {
        RM,
        Volume,
        Intensity
    }
}
