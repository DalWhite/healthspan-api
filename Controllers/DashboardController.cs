#nullable enable

using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class DashboardController : ControllerBase
{
    private readonly CurrentUserProvider _currentUserProvider;
    private readonly HealthAccessService _healthAccessService;
    private readonly HealthDashboardService _dashboardService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        CurrentUserProvider currentUserProvider,
        HealthAccessService healthAccessService,
        HealthDashboardService dashboardService,
        ILogger<DashboardController> logger)
    {
        _currentUserProvider = currentUserProvider;
        _healthAccessService = healthAccessService;
        _dashboardService = dashboardService;
        _logger = logger;
    }

    [HttpGet("summary")]
    public ActionResult<GetHealthDashboardSummaryResource> GetSummary(string? userId)
    {
        try
        {
            var currentUserId = _currentUserProvider.GetCurrentUserId();
            var targetUserId = string.IsNullOrWhiteSpace(userId) ? currentUserId : userId;
            if (!_healthAccessService.CanAccessBloodAnalytics(currentUserId, targetUserId) && !_healthAccessService.CanAccessAssessments(currentUserId, targetUserId))
            {
                return Forbid();
            }

            return Ok(_dashboardService.GetSummary(targetUserId).Adapt<GetHealthDashboardSummaryResource>());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load the health overview");
            return ApiProblemResult.Unexpected(
                "health_overview_load_failed");
        }
    }
}
