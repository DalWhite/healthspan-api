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
            Slug = m.Slug,
            Name = m.Name,
            Side = m.Side.ToString(),
            MuscleGroup = m.MuscleGroup.ToString(),
            SortOrder = m.SortOrder
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
            Slug = muscle.Slug,
            Name = muscle.Name,
            Side = muscle.Side.ToString(),
            MuscleGroup = muscle.MuscleGroup.ToString(),
            SortOrder = muscle.SortOrder
        };

        return Ok(response);
    }
} 
