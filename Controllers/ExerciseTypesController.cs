using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize]
[ApiController]
[Route("[controller]")]
public class ExerciseTypesController : ControllerBase
{
    private readonly ExerciseTypesRepository _exerciseTypeRepository;

    public ExerciseTypesController(ExerciseTypesRepository exerciseTypeRepository)
    {
        _exerciseTypeRepository = exerciseTypeRepository;
    }

    [HttpGet]
    public ActionResult<List<GetExerciseTypeResource>> GetExerciseTypes()
    {
        try
        {
            var domainExerciseTypes = _exerciseTypeRepository.GetExerciseTypes();
            var response = domainExerciseTypes.Adapt<List<GetExerciseTypeResource>>();
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
    [HttpPost]
    public IActionResult AddExerciseType(AddExerciseTypeResource exerciseTypeResource)
    {
        try
        {
            var domainExerciseType = exerciseTypeResource.Adapt<ExerciseType>();
            var exerciseTypeId = _exerciseTypeRepository.AddExerciseType(domainExerciseType);
            return CreatedAtAction(nameof(GetExerciseType), new { id = exerciseTypeId }, domainExerciseType);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpGet("{id}")]
    public ActionResult<GetExerciseTypeResource> GetExerciseType(Guid id)
    {
        try
        {
            var domainExerciseType = _exerciseTypeRepository.GetExerciseType(id);
            if (domainExerciseType is not null)
            {
                var response = domainExerciseType.Adapt<GetExerciseTypeResource>();
                return Ok(response);
            }
            else
            {
                return NotFound($"ExerciseType with ID {id} not found");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpPut]
    public IActionResult UpdateExerciseType(UpdateExerciseTypeResource exerciseTypeResource)
    {
        try
        {
            var domainExerciseType = exerciseTypeResource.Adapt<ExerciseType>();
            var existingExerciseType = _exerciseTypeRepository.GetExerciseType(domainExerciseType.Id);
            if (existingExerciseType is not null)
            {
                _exerciseTypeRepository.UpdateExerciseType(domainExerciseType);
                return Ok("ExerciseType updated successfully");
            }
            else
            {
                return NotFound($"ExerciseType with ID {domainExerciseType.Id} not found");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
}
