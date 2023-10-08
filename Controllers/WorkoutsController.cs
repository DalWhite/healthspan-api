using Mapster;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
public class WorkoutsController : ControllerBase
{
    private readonly WorkoutsRepository _workoutsRepository;

    public WorkoutsController(WorkoutsRepository workoutRepository)
    {
        _workoutsRepository = workoutRepository;
    }

    [HttpPost]
    public IActionResult AddWorkout(AddWorkoutResource workoutResource)
    {
        try
        {
            var domainWorkout = workoutResource.Adapt<Workout>();
            _workoutsRepository.AddWorkout(domainWorkout);
            return Ok("Workout added successfully");
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
