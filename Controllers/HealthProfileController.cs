using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public sealed class HealthProfileController(
    CurrentUserProvider currentUserProvider,
    HealthProfileService healthProfileService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HealthProfileResource>> Get(
        CancellationToken cancellationToken)
    {
        var userId = currentUserProvider.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        return Ok(await healthProfileService.GetAsync(
            userId,
            cancellationToken));
    }

    [HttpPut]
    public async Task<ActionResult<HealthProfileResource>> Update(
        UpdateHealthProfileResource resource,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(resource.BiologicalSex))
        {
            return Problem(
                title: "Biological sex is not valid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (resource.DateOfBirth > today
            || resource.DateOfBirth < today.AddYears(-120))
        {
            return Problem(
                title: "Date of birth must be between 120 years ago and today.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = currentUserProvider.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        return Ok(await healthProfileService.UpdateAsync(
            userId,
            resource,
            cancellationToken));
    }
}
