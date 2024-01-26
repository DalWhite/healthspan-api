using Mapster;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
public class UsersController : ControllerBase
{
    private readonly UsersRepository _userRepository;

    public UsersController(UsersRepository userRepository)
    {
        _userRepository = userRepository;
    }

    [HttpPost]
    public IActionResult AddUser(AddUserResource userResource)
    {
        try
        {
            var domainUser = userResource.Adapt<User>();
            _userRepository.AddUser(domainUser);
            return Ok("User added successfully");
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpGet("{id}")]
    public ActionResult<GetUserResource> GetUser(string id)
    {
        try
        {
            var domainUser = _userRepository.GetUser(id);
            if (domainUser is not null)
            {
                var response = domainUser.Adapt<GetUserResource>();
                return Ok(response);
            }
            else
            {
                return NotFound($"User with ID {id} not found");
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpPut]
    public IActionResult UpdateUser(UpdateUserResource userResource)
    {
        try
        {
            var domainUser = userResource.Adapt<User>();

            _userRepository.UpdateUser(domainUser);
            return Ok("User updated successfully");

        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
}
