using Mapster;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
public class ExercisesController : ControllerBase
{
    private readonly ExercisesRepository _exerciseRepository;

    public ExercisesController(ExercisesRepository exerciseRepository)
    {
        _exerciseRepository = exerciseRepository;
    }

    [HttpPost]
    public IActionResult AddExercise(AddExerciseResource exerciseResource)
    {
        try
        {
            var domainExercise = exerciseResource.Adapt<Exercise>();
            _exerciseRepository.AddExercise(domainExercise);
            return Ok("Exercise added successfully");
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

    [HttpPut]
    public IActionResult UpdateExercise(UpdateExerciseResource exerciseResource)
    {
        try
        {
            var domainExercise = exerciseResource.Adapt<Exercise>();
            var existingExercise = _exerciseRepository.GetExercise(domainExercise.Id);
            if (existingExercise is not null)
            {
                _exerciseRepository.UpdateExercise(domainExercise);
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
}
