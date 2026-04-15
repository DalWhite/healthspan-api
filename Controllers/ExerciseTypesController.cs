using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class ExerciseTypesController : ControllerBase
{
    private readonly ExerciseTypesRepository _exerciseTypeRepository;
    private readonly CurrentUserProvider _userProvider;
    private readonly UsersRepository _usersRepository;

    public ExerciseTypesController(ExerciseTypesRepository exerciseTypeRepository, CurrentUserProvider userProvider, UsersRepository usersRepository)
    {
        _exerciseTypeRepository = exerciseTypeRepository;
        _userProvider = userProvider;
        _usersRepository = usersRepository;
    }

    [HttpGet]
    public ActionResult<List<GetExerciseTypeResource>> GetExerciseTypes(bool? favourite, string userId)
    {
        //TODO: this favorite logic should not be here and should not be applied after mapping
        try
        {
            var domainExerciseTypes = _exerciseTypeRepository.GetExerciseTypes();
            var response = new List<GetExerciseTypeResource>();

            foreach (var exerciseType in domainExerciseTypes)
            {
                var mappedExType = exerciseType.Adapt<GetExerciseTypeResource>();
                mappedExType.IsFavourite = IsFavouriteExType(exerciseType, userId);
                response.Add(mappedExType);
            }

            if(favourite is not null)
            {
                response = response.Where(r => r.IsFavourite == favourite).ToList();
            }
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
    private bool IsFavouriteExType(ExerciseType exerciseType, string userId)
    {
        var userForFavFilter = string.IsNullOrEmpty(userId) ? _userProvider.GetCurrentUserId(): userId;
        if(exerciseType.FavoritedUsers is null)
        {
            return false;
        }
        if(exerciseType.FavoritedUsers.Any(u => u.Id == userForFavFilter))
        {
            return true;
        }
        return false;
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
    public ActionResult<GetExerciseTypeResource> GetExerciseType(int id)
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
                domainExerciseType.FavoritedUsers = existingExerciseType.FavoritedUsers ?? [];

                var userId = _userProvider.GetCurrentUserId();
                var existingFavourite = domainExerciseType.FavoritedUsers.FirstOrDefault(user => user.Id == userId);

                if (exerciseTypeResource.IsFavourite && existingFavourite is null)
                {
                    var user = _usersRepository.GetUser(userId);
                    domainExerciseType.FavoritedUsers.Add(user);
                }
                else if (!exerciseTypeResource.IsFavourite && existingFavourite is not null)
                {
                    domainExerciseType.FavoritedUsers.Remove(existingFavourite);
                }

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
