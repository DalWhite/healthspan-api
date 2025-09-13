using Microsoft.AspNetCore.Mvc;
using Contracts.Muscles;

[ApiController]
[Route("api/[controller]")]
public class MusclesController : ControllerBase
{
    private readonly MuscleRepository _muscleRepository;

    public MusclesController(MuscleRepository muscleRepository)
    {
        _muscleRepository = muscleRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<MuscleResponse>>> GetAll()
    {
        var muscles = await _muscleRepository.GetAllMusclesAsync();
        
        var response = muscles.Select(m => new MuscleResponse
        {
            Id = m.Id,
            Name = m.Name,
            Side = m.Side.ToString(),
            MuscleGroup = m.MuscleGroup.ToString()
        }).ToList();

        return Ok(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MuscleResponse>> GetById(int id)
    {
        var muscle = await _muscleRepository.GetMuscleByIdAsync(id);

        if (muscle == null)
        {
            return NotFound();
        }

        var response = new MuscleResponse
        {
            Id = muscle.Id,
            Name = muscle.Name,
            Side = muscle.Side.ToString(),
            MuscleGroup = muscle.MuscleGroup.ToString()
        };

        return Ok(response);
    }
} 