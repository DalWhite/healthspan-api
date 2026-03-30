#nullable enable

using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class BloodTestsController : ControllerBase
{
    private readonly BloodAnalyticsService _bloodAnalyticsService;
    private readonly CurrentUserProvider _currentUserProvider;
    private readonly HealthAccessService _healthAccessService;

    public BloodTestsController(BloodAnalyticsService bloodAnalyticsService, CurrentUserProvider currentUserProvider, HealthAccessService healthAccessService)
    {
        _bloodAnalyticsService = bloodAnalyticsService;
        _currentUserProvider = currentUserProvider;
        _healthAccessService = healthAccessService;
    }

    [HttpGet]
    public ActionResult<List<GetBloodTestResource>> GetBloodTests(string? userId)
    {
        try
        {
            var targetUserId = ResolveTargetUserId(userId, false);
            return Ok(_bloodAnalyticsService.GetBloodTests(targetUserId).Select(MapBloodTest).ToList());
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

    [HttpGet("pending-review")]
    public ActionResult<List<GetBloodTestResource>> GetPendingReviewBloodTests()
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            return Ok(_bloodAnalyticsService.GetPendingReviewBloodTests(userId).Select(MapBloodTest).ToList());
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpGet("{id}")]
    public ActionResult<GetBloodTestResource> GetBloodTest(Guid id, string? userId)
    {
        try
        {
            var targetUserId = ResolveTargetUserId(userId, false);
            var bloodTest = _bloodAnalyticsService.GetBloodTest(id, targetUserId);
            if (bloodTest is null)
            {
                return NotFound($"Blood test with ID {id} not found");
            }

            return Ok(MapBloodTest(bloodTest));
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

    [HttpPost("manual")]
    public IActionResult AddManualBloodTest(AddBloodTestResource resource)
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            var id = _bloodAnalyticsService.CreateManualBloodTest(
                userId,
                resource.TestDate,
                resource.LabName,
                resource.Markers.Select(marker => new ManualBloodTestMarkerInput
                {
                    Name = marker.Name,
                    Unit = marker.Unit,
                    Value = marker.Value
                }).ToList());

            return CreatedAtAction(nameof(GetBloodTest), new { id }, new { Id = id });
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadBloodTest(IFormFile file, DateTime testDate, string? labName, CancellationToken cancellationToken)
    {
        try
        {
            if (file is null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var allowedExtensions = new[] { ".pdf", ".png", ".jpg", ".jpeg", ".tiff", ".bmp" };
            if (!allowedExtensions.Any(extension => file.FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            {
                return BadRequest("File must be a PDF or supported image format.");
            }

            var userId = _currentUserProvider.GetCurrentUserId();
            var id = await _bloodAnalyticsService.CreateUploadedBloodTestAsync(userId, file, testDate, labName, cancellationToken);
            return AcceptedAtAction(nameof(GetBloodTest), new { id }, new { Id = id });
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpPost("{id}/review")]
    public IActionResult ReviewBloodTest(Guid id, ReviewBloodTestResource resource)
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            var updated = _bloodAnalyticsService.ReviewBloodTest(userId, id, new ReviewBloodTestInput
            {
                TestDate = resource.TestDate,
                LabName = resource.LabName,
                Markers = resource.Markers.Select(marker => new ReviewBloodTestMarkerInput
                {
                    Name = marker.Name,
                    Value = marker.Value,
                    Unit = marker.Unit,
                    RawValueText = marker.RawValueText,
                    Confidence = marker.Confidence
                }).ToList()
            });

            if (!updated)
            {
                return NotFound($"Blood test with ID {id} not found");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }

    [HttpGet("markers/latest")]
    public ActionResult<List<LatestMarkerSnapshotResource>> GetLatestMarkers(string? userId)
    {
        try
        {
            var targetUserId = ResolveTargetUserId(userId, false);
            return Ok(_bloodAnalyticsService.GetLatestMarkerSnapshots(targetUserId).Select(marker => marker.Adapt<LatestMarkerSnapshotResource>()).ToList());
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

    [HttpGet("markers/{metricKey}/trends")]
    public ActionResult<GetMarkerTrendResource> GetMarkerTrends(string metricKey, DateTime? fromUtc, DateTime? toUtc, string? userId)
    {
        try
        {
            var targetUserId = ResolveTargetUserId(userId, false);
            return Ok(new GetMarkerTrendResource
            {
                MetricKey = BloodAnalyticsService.NormalizeMetricKey(metricKey),
                Points = _bloodAnalyticsService.GetMarkerTrends(targetUserId, metricKey, fromUtc, toUtc)
                    .Select(point => new GetMarkerTrendPointResource
                    {
                        Time = point.Time,
                        Value = point.Value,
                        Unit = point.Unit,
                        Status = point.Status
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

    private string ResolveTargetUserId(string? userId, bool forAssessments)
    {
        var currentUserId = _currentUserProvider.GetCurrentUserId();
        var targetUserId = string.IsNullOrWhiteSpace(userId) ? currentUserId : userId;
        var allowed = forAssessments
            ? _healthAccessService.CanAccessAssessments(currentUserId, targetUserId)
            : _healthAccessService.CanAccessBloodAnalytics(currentUserId, targetUserId);

        if (!allowed)
        {
            throw new UnauthorizedAccessException();
        }

        return targetUserId;
    }

    private static GetBloodTestResource MapBloodTest(BloodTest bloodTest)
    {
        return new GetBloodTestResource
        {
            Id = bloodTest.Id,
            TestDate = bloodTest.TestDate,
            LabName = bloodTest.LabName,
            SourceFileName = bloodTest.SourceFileName,
            ProcessingStatus = bloodTest.ProcessingStatus,
            ProcessingNotes = bloodTest.ProcessingNotes,
            RequiresReview = bloodTest.ProcessingStatus == BloodTestProcessingStatus.PendingReview,
            Markers = bloodTest.MarkerResults.Select(marker => new BloodTestMarkerResource
            {
                Name = marker.DisplayName,
                Value = marker.NormalizedValue ?? marker.Value,
                Unit = marker.NormalizedUnit ?? marker.Unit,
                RawValueText = marker.RawValueText,
                Confidence = marker.Confidence,
                Status = marker.Status,
                ReferenceMinValue = marker.ReferenceMinValue,
                ReferenceMaxValue = marker.ReferenceMaxValue
            }).ToList()
        };
    }
}