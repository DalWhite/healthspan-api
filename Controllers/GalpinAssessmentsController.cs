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
    private readonly ILogger<GalpinAssessmentsController> _logger;

    public GalpinAssessmentsController(
        GalpinAssessmentService assessmentService,
        GalpinAssessmentCatalogService catalogService,
        CurrentUserProvider currentUserProvider,
        HealthAccessService healthAccessService,
        ILogger<GalpinAssessmentsController> logger)
    {
        _assessmentService = assessmentService;
        _catalogService = catalogService;
        _currentUserProvider = currentUserProvider;
        _healthAccessService = healthAccessService;
        _logger = logger;
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
            _logger.LogError(
                ex,
                "Failed to load Galpin assessments");
            return ApiProblemResult.Unexpected(
                "assessments_load_failed");
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
                return ApiProblemResult.NotFound(
                    "assessment_not_found",
                    "No assessment has been recorded yet.");
            }

            return Ok(MapAssessment(assessment));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load the latest Galpin assessment");
            return ApiProblemResult.Unexpected(
                "latest_assessment_load_failed");
        }
    }

    [HttpPost]
    public IActionResult AddAssessment(AddGalpinAssessmentResource resource)
    {
        try
        {
            var validationProblem =
                ValidateAssessment(resource);
            if (validationProblem is not null)
            {
                return validationProblem;
            }

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
            _logger.LogError(
                ex,
                "Failed to create a Galpin assessment");
            return ApiProblemResult.Unexpected(
                "assessment_create_failed");
        }
    }

    [HttpPut("{id}")]
    public IActionResult UpdateAssessment(
        Guid id,
        AddGalpinAssessmentResource resource)
    {
        try
        {
            var validationProblem =
                ValidateAssessment(resource);
            if (validationProblem is not null)
            {
                return validationProblem;
            }

            var userId =
                _currentUserProvider.GetCurrentUserId();
            var updated = _assessmentService.UpdateAssessment(
                userId,
                id,
                resource.AssessedOnUtc,
                resource.Notes,
                MapScores(resource));

            return updated
                ? NoContent()
                : ApiProblemResult.NotFound(
                    "assessment_not_found",
                    "This assessment no longer exists.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update assessment {AssessmentId}",
                id);
            return ApiProblemResult.Unexpected(
                "assessment_update_failed");
        }
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteAssessment(Guid id)
    {
        try
        {
            var userId =
                _currentUserProvider.GetCurrentUserId();
            return _assessmentService
                .DeleteAssessment(userId, id)
                ? NoContent()
                : ApiProblemResult.NotFound(
                    "assessment_not_found",
                    "This assessment no longer exists.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete assessment {AssessmentId}",
                id);
            return ApiProblemResult.Unexpected(
                "assessment_delete_failed");
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
            _logger.LogError(
                ex,
                "Failed to load trend for {Adaptation}",
                adaptation);
            return ApiProblemResult.Unexpected(
                "assessment_trend_load_failed");
        }
    }

    private static ObjectResult? ValidateAssessment(
        AddGalpinAssessmentResource resource)
    {
        if (resource.AssessedOnUtc == default
            || resource.AssessedOnUtc.Date
                > DateTime.UtcNow.Date)
        {
            return ApiProblemResult.Validation(
                "assessment_date_invalid",
                "Assessment date is required and cannot be in the future.");
        }

        if (resource.Scores is null
            || resource.Scores.Count == 0)
        {
            return ApiProblemResult.Validation(
                "assessment_scores_missing",
                "Add at least one assessment score.");
        }

        var adaptations =
            new HashSet<GalpinAdaptationType>();
        foreach (var score in resource.Scores)
        {
            if (!Enum.IsDefined(score.Adaptation))
            {
                return ApiProblemResult.Validation(
                    "assessment_adaptation_invalid",
                    "One of the assessment areas is not valid.");
            }

            if (!adaptations.Add(score.Adaptation))
            {
                return ApiProblemResult.Validation(
                    "assessment_adaptation_duplicate",
                    "Each assessment area can appear only once.");
            }

            if (!score.Score.HasValue
                || score.Score < 0
                || score.Score > 10)
            {
                return ApiProblemResult.Validation(
                    "assessment_score_invalid",
                    "Scores must be between 0 and 10.");
            }
        }

        return null;
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

    private static List<GalpinAssessmentScoreInput> MapScores(
        AddGalpinAssessmentResource resource)
        => resource.Scores.Select(score =>
            new GalpinAssessmentScoreInput
            {
                Adaptation = score.Adaptation,
                Score = score.Score,
                EvidenceNotes = score.EvidenceNotes
            }).ToList();
}
