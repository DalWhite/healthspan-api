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
    public ActionResult<PagedResult<GetExerciseResource>> GetExercises(
        int? exerciseTypeId, 
        string userId, 
        int page = 1, 
        int pageSize = 10, 
        string sortBy = "date", 
        bool descending = true)
    {
        try
        {
            userId = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId() : userId;
            var exercises = new List<Exercise>();
            
            if (exerciseTypeId.HasValue)
            {   
                exercises = _exerciseRepository.GetExercisesByType(exerciseTypeId.Value, userId);
            }
            else
            {
                exercises = _exerciseRepository.GetExercises(userId);
            }
            
            var mappedExercises = exercises.Adapt<List<GetExerciseResource>>();
            
            // Apply sorting
            IEnumerable<GetExerciseResource> sortedExercises = sortBy.ToLower() switch
            {
                "date" => descending 
                    ? mappedExercises.OrderByDescending(e => e.Workout.Date)
                    : mappedExercises.OrderBy(e => e.Workout.Date),
                "name" => descending 
                    ? mappedExercises.OrderByDescending(e => e.Type.Name)
                    : mappedExercises.OrderBy(e => e.Type.Name),
                "type" => descending 
                    ? mappedExercises.OrderByDescending(e => e.Type.Name)
                    : mappedExercises.OrderBy(e => e.Type.Name),
                _ => descending 
                    ? mappedExercises.OrderByDescending(e => e.Workout.Date)
                    : mappedExercises.OrderBy(e => e.Workout.Date)
            };
            
            // Apply pagination
            var totalCount = sortedExercises.Count();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            
            var pagedExercises = sortedExercises
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            
            var result = new PagedResult<GetExerciseResource>
            {
                Items = pagedExercises,
                TotalCount = totalCount,
                PageSize = pageSize,
                CurrentPage = page,
                TotalPages = totalPages
            };
            
            return Ok(result);
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
            _exercisesService.CalculateComputedValues(domainExercise, userId);

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

    public class PagedResult<T>
    {
        public List<T> Items { get; set; }
        public int TotalCount { get; set; }
        public int PageSize { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
    }
}
