using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class WorkoutsController : ControllerBase
{
    private readonly WorkoutsRepository _workoutsRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CurrentUserProvider _userProvider;

    public WorkoutsController(WorkoutsRepository workoutRepository, IHttpContextAccessor httpContextAccessor, CurrentUserProvider userProvider)
    {
        _workoutsRepository = workoutRepository;
        _httpContextAccessor = httpContextAccessor;
        _userProvider = userProvider;
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
            return CreatedAtAction(nameof(GetWorkout), new { id = workoutId }, domainWorkout);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
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
}
