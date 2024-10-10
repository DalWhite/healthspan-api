using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class UsersController : ControllerBase
{
    private readonly UsersRepository _userRepository;
    private readonly CurrentUserProvider _userProvider;

    public UsersController(UsersRepository userRepository, CurrentUserProvider userProvider)
    {
        _userRepository = userRepository;
        _userProvider = userProvider;
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

    [HttpGet("trainees")]
    public ActionResult<GetUserResource> GetTrainees()
    {
        try
        {
            var domainUsers = _userRepository.GetTrainees();
            var response = domainUsers.Adapt<List<GetUserResource>>();
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
    [HttpGet("roles")]
    public ActionResult<GetUserResource> GetUserRoles()
    {
        try
        {
            var roles = _userRepository.GetUserRoles(_userProvider.GetCurrentUserId());
            var response = roles.Adapt<List<string>>();
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
}
