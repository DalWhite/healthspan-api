using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize]
[ApiController]
[Route("[controller]")]
public class ExercisesController : ControllerBase
{
    private readonly ExercisesRepository _exerciseRepository;
    private readonly WorkoutsRepository _workoutsRepository;
    private readonly ExerciseTypesRepository _exerciseTypesRepository;

    public ExercisesController(ExercisesRepository exerciseRepository, WorkoutsRepository workoutsRepository, ExerciseTypesRepository exerciseTypesRepository)
    {
        _exerciseRepository = exerciseRepository;
        _workoutsRepository = workoutsRepository;
        _exerciseTypesRepository = exerciseTypesRepository;
    }

    [HttpGet]
    public ActionResult<List<GetExerciseResource>> GetExercises()
    {
        try
        {
            var domainExercises = _exerciseRepository.GetExercises();
            var response = domainExercises.Adapt<List<GetExerciseResource>>();
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
    [HttpPost]
    public IActionResult AddExercise(AddExerciseResource exerciseResource)
    {
        try
        {
            var domainExercise = exerciseResource.Adapt<Exercise>();
            domainExercise.TypeId = exerciseResource.TypeId;
            domainExercise.WorkoutId = exerciseResource.WorkoutId;
            var exerciseId = _exerciseRepository.AddExercise(domainExercise);
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
