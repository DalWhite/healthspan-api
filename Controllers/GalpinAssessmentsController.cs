#nullable enable

using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class GalpinAssessmentsController : ControllerBase
{
    private readonly GalpinAssessmentService _assessmentService;
    private readonly GalpinAssessmentCatalogService _catalogService;
    private readonly CurrentUserProvider _currentUserProvider;
    private readonly HealthAccessService _healthAccessService;

    public GalpinAssessmentsController(GalpinAssessmentService assessmentService, GalpinAssessmentCatalogService catalogService, CurrentUserProvider currentUserProvider, HealthAccessService healthAccessService)
    {
        _assessmentService = assessmentService;
        _catalogService = catalogService;
        _currentUserProvider = currentUserProvider;
        _healthAccessService = healthAccessService;
    }

    [HttpGet("catalog")]
    public ActionResult<List<GalpinAdaptationCatalogItemResource>> GetCatalog()
    {
        return Ok(_catalogService.GetCatalog().Select(item => item.Adapt<GalpinAdaptationCatalogItemResource>()).ToList());
    }

    [HttpGet]
    public ActionResult<List<GetGalpinAssessmentResource>> GetAssessments(string? userId)
    {
        try
        {
            var targetUserId = ResolveTargetUserId(userId);
            return Ok(_assessmentService.GetAssessments(targetUserId).Select(MapAssessment).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpGet("latest")]
    public ActionResult<GetGalpinAssessmentResource> GetLatestAssessment(string? userId)
    {
        try
        {
            var targetUserId = ResolveTargetUserId(userId);
            var assessment = _assessmentService.GetLatestAssessment(targetUserId);
            if (assessment is null)
            {
                return NotFound("No Galpin assessment found.");
            }

            return Ok(MapAssessment(assessment));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpPost]
    public IActionResult AddAssessment(AddGalpinAssessmentResource resource)
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            var id = _assessmentService.CreateAssessment(
                userId,
                resource.AssessedOnUtc,
                resource.Notes,
                resource.Scores.Select(score => new GalpinAssessmentScoreInput
                {
                    Adaptation = score.Adaptation,
                    Score = score.Score,
                    EvidenceNotes = score.EvidenceNotes
                }).ToList());

            return CreatedAtAction(nameof(GetLatestAssessment), new { }, new { Id = id });
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpGet("trends/{adaptation}")]
    public ActionResult<GetGalpinAdaptationTrendResource> GetAdaptationTrend(GalpinAdaptationType adaptation, string? userId)
    {
        try
        {
            var targetUserId = ResolveTargetUserId(userId);
            return Ok(new GetGalpinAdaptationTrendResource
            {
                Adaptation = adaptation,
                Points = _assessmentService.GetAdaptationTrend(targetUserId, adaptation)
                    .Select(point => new GetGalpinAdaptationTrendPointResource
                    {
                        Time = point.Time,
                        Score = point.Score
                    }).ToList()
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    private string ResolveTargetUserId(string? userId)
    {
        var currentUserId = _currentUserProvider.GetCurrentUserId();
        var targetUserId = string.IsNullOrWhiteSpace(userId) ? currentUserId : userId;
        if (!_healthAccessService.CanAccessAssessments(currentUserId, targetUserId))
        {
            throw new UnauthorizedAccessException();
        }

        return targetUserId;
    }

    private static GetGalpinAssessmentResource MapAssessment(GalpinAssessment assessment)
    {
        return new GetGalpinAssessmentResource
        {
            Id = assessment.Id,
            AssessedOnUtc = assessment.AssessedOnUtc,
            Notes = assessment.Notes,
            Scores = assessment.Scores.Select(score => new GalpinAssessmentScoreResource
            {
                Adaptation = score.Adaptation,
                Score = score.Score,
                EvidenceNotes = score.EvidenceNotes
            }).ToList()
        };
    }
}