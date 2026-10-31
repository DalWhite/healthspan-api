using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public sealed class HealthProfileController(
    CurrentUserProvider currentUserProvider,
    HealthProfileService healthProfileService,
    ILogger<HealthProfileController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HealthProfileResource>> Get(
        CancellationToken cancellationToken)
    {
        try
        {
            var userId =
                currentUserProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            return Ok(await healthProfileService.GetAsync(
                userId,
                cancellationToken));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to load health profile");
            return ApiProblemResult.Unexpected(
                "health_profile_load_failed");
        }
    }

    [HttpPut]
    public async Task<ActionResult<HealthProfileResource>> Update(
        UpdateHealthProfileResource resource,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(resource.BiologicalSex))
        {
            return ApiProblemResult.Validation(
                "biological_sex_invalid",
                "Biological sex is not valid.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (resource.DateOfBirth > today
            || resource.DateOfBirth < today.AddYears(-120))
        {
            return ApiProblemResult.Validation(
                "date_of_birth_invalid",
                "Date of birth must be between 120 years ago and today.");
        }

        // Ingest validation for the weight time series (spec §2 K2): out-of-range
        // weights are an explicit 400, never a silent store.
        if (resource.WeightKg is < 25 or > 400)
        {
            return ApiProblemResult.Validation(
                "weight_kg_invalid",
                "Weight must be between 25 and 400 kg.");
        }

        try
        {
            var userId =
                currentUserProvider.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            return Ok(await healthProfileService.UpdateAsync(
                userId,
                resource,
                cancellationToken));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to update health profile");
            return ApiProblemResult.Unexpected(
                "health_profile_update_failed");
        }
    }
}
