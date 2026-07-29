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
    private readonly ILogger<BloodTestsController> _logger;

    public BloodTestsController(
        BloodAnalyticsService bloodAnalyticsService,
        CurrentUserProvider currentUserProvider,
        HealthAccessService healthAccessService,
        ILogger<BloodTestsController> logger)
    {
        _bloodAnalyticsService = bloodAnalyticsService;
        _currentUserProvider = currentUserProvider;
        _healthAccessService = healthAccessService;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<List<BloodTestSummaryResource>>
        GetBloodTests(string? userId)
    {
        try
        {
            var targetUserId = ResolveTargetUserId(userId, false);
            return Ok(_bloodAnalyticsService
                .GetBloodTestSummaries(targetUserId)
                .Select(MapBloodTestSummary)
                .ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load blood-test history");
            return ApiProblemResult.Unexpected(
                "blood_tests_load_failed");
        }
    }

    [HttpGet("pending-review")]
    public ActionResult<List<BloodTestSummaryResource>>
        GetPendingReviewBloodTests()
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            return Ok(_bloodAnalyticsService
                .GetPendingReviewBloodTestSummaries(userId)
                .Select(MapBloodTestSummary)
                .ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load pending blood tests");
            return ApiProblemResult.Unexpected(
                "pending_blood_tests_load_failed");
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
                return ApiProblemResult.NotFound(
                    "blood_test_not_found",
                    "This blood test no longer exists.");
            }

            return Ok(MapBloodTest(
                bloodTest,
                includeMarkers: true));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load blood test {BloodTestId}",
                id);
            return ApiProblemResult.Unexpected(
                "blood_test_load_failed");
        }
    }

    [HttpPost("manual")]
    public async Task<IActionResult> AddManualBloodTest(AddBloodTestResource resource)
    {
        try
        {
            var markers = resource.Markers ?? [];
            var validationProblem = ValidateBloodTestInput(
                resource.TestDate,
                markers,
                allowEmptyMarkers: true);
            if (validationProblem is not null)
            {
                return validationProblem;
            }

            var userId = _currentUserProvider.GetCurrentUserId();
            var id = await _bloodAnalyticsService.CreateManualBloodTestAsync(
                userId,
                resource.TestDate,
                resource.LabName,
                markers.Select(marker => new ManualBloodTestMarkerInput
                {
                    Name = marker.Name,
                    Unit = marker.Unit,
                    Value = marker.Value,
                    ReferenceMinValue =
                        marker.ReferenceMinValue,
                    ReferenceMaxValue =
                        marker.ReferenceMaxValue
                }).ToList());

            return CreatedAtAction(nameof(GetBloodTest), new { id }, new { Id = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create a manual blood test");
            return ApiProblemResult.Unexpected(
                "manual_blood_test_create_failed");
        }
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadBloodTest(IFormFile file, DateTime testDate, string? labName, CancellationToken cancellationToken)
    {
        try
        {
            if (file is null || file.Length == 0)
            {
                return ApiProblemResult.Validation(
                    "blood_test_file_missing",
                    "Choose a report to upload.");
            }

            const long maxFileSizeBytes = 20 * 1024 * 1024; // 20 MB
            if (file.Length > maxFileSizeBytes)
            {
                return ApiProblemResult.Validation(
                    "blood_test_file_too_large",
                    "The report must be 20 MB or smaller.");
            }

            if (testDate == default
                || testDate.Date > DateTime.UtcNow.Date)
            {
                return ApiProblemResult.Validation(
                    "blood_test_date_invalid",
                    "Test date is required and cannot be in the future.");
            }

            var uploadProblem = await ValidateUploadAsync(
                file,
                cancellationToken);
            if (uploadProblem is not null)
            {
                return uploadProblem;
            }

            var userId = _currentUserProvider.GetCurrentUserId();
            var result = await _bloodAnalyticsService
                .CreateUploadedBloodTestAsync(
                    userId,
                    file,
                    testDate,
                    labName,
                    cancellationToken);
            return AcceptedAtAction(
                nameof(GetBloodTest),
                new { id = result.Id },
                new BloodTestUploadResource
                {
                    Id = result.Id,
                    ProcessingStatus =
                        BloodTestProcessingStatus.Processing,
                    PossibleDuplicateBloodTestId =
                        result.PossibleDuplicateBloodTestId
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to accept blood-test upload {FileName}",
                file?.FileName);
            return ApiProblemResult.Unexpected(
                "blood_test_upload_failed");
        }
    }

    [HttpPost("{id}/review")]
    public IActionResult ReviewBloodTest(Guid id, ReviewBloodTestResource resource)
    {
        try
        {
            var markers = resource.Markers ?? [];
            var validationProblem = ValidateBloodTestInput(
                resource.TestDate,
                markers,
                allowEmptyMarkers: true);
            if (validationProblem is not null)
            {
                return validationProblem;
            }

            var userId = _currentUserProvider.GetCurrentUserId();
            var updated = _bloodAnalyticsService.ReviewBloodTest(userId, id, new ReviewBloodTestInput
            {
                TestDate = resource.TestDate,
                LabName = resource.LabName,
                Markers = markers.Select(marker => new ReviewBloodTestMarkerInput
                {
                    Id = marker.Id,
                    Name = marker.Name,
                    Value = marker.Value,
                    Unit = marker.Unit,
                    RawValueText = marker.RawValueText,
                    Confidence = marker.Confidence,
                    ReferenceMinValue = marker.ReferenceMinValue,
                    ReferenceMaxValue = marker.ReferenceMaxValue,
                    ReferenceRangeFromReport =
                        marker.ReferenceRangeFromReport
                }).ToList()
            });

            if (!updated)
            {
                return ApiProblemResult.NotFound(
                    "blood_test_not_found",
                    "This blood test no longer exists.");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to review blood test {BloodTestId}",
                id);
            return ApiProblemResult.Unexpected(
                "blood_test_review_failed");
        }
    }

    [HttpGet("{id}/document")]
    public async Task<IActionResult> GetBloodTestDocument(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            var document =
                await _bloodAnalyticsService.GetBloodTestDocumentAsync(
                    userId,
                    id,
                    cancellationToken);

            return document is null
                ? ApiProblemResult.NotFound(
                    "blood_test_document_not_found",
                    "The original report is not available.")
                : File(document.Content, document.ContentType);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load document for blood test {BloodTestId}",
                id);
            return ApiProblemResult.Unexpected(
                "blood_test_document_load_failed");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBloodTest(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _currentUserProvider.GetCurrentUserId();
            var deleted =
                await _bloodAnalyticsService.DeleteBloodTestAsync(
                    userId,
                    id,
                    cancellationToken);

            return deleted
                ? NoContent()
                : ApiProblemResult.NotFound(
                    "blood_test_not_found",
                    "This blood test no longer exists.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete blood test {BloodTestId}",
                id);
            return ApiProblemResult.Unexpected(
                "blood_test_delete_failed");
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
            _logger.LogError(
                ex,
                "Failed to load latest biomarkers");
            return ApiProblemResult.Unexpected(
                "biomarker_snapshot_load_failed");
        }
    }

    [HttpGet("markers/{metricKey}/trends")]
    public ActionResult<GetMarkerTrendResource> GetMarkerTrends(string metricKey, DateTime? fromUtc, DateTime? toUtc, string? userId)
    {
        try
        {
            var targetUserId = ResolveTargetUserId(userId, false);
            var points = _bloodAnalyticsService.GetMarkerTrends(
                targetUserId,
                metricKey,
                fromUtc,
                toUtc);
            var units = points
                .Select(point => point.Unit?.Trim())
                .Where(unit => !string.IsNullOrWhiteSpace(unit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .OrderBy(unit => unit)
                .ToList();
            var hasIncompatibleUnits = units.Count > 1;

            return Ok(new GetMarkerTrendResource
            {
                MetricKey = BloodAnalyticsService.NormalizeMetricKey(metricKey),
                Unit = units.Count == 1 ? units[0] : null,
                HasIncompatibleUnits = hasIncompatibleUnits,
                AvailableUnits = units,
                Points = (hasIncompatibleUnits
                    ? Enumerable.Empty<HealthTrendPoint>()
                    : points)
                    .Select(point => new GetMarkerTrendPointResource
                    {
                        BloodTestId = point.BloodTestId,
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
            _logger.LogError(
                ex,
                "Failed to load trend for marker {MetricKey}",
                metricKey);
            return ApiProblemResult.Unexpected(
                "biomarker_trend_load_failed");
        }
    }

    private async Task<ObjectResult?> ValidateUploadAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var extension = Path
            .GetExtension(file.FileName)
            .ToLowerInvariant();
        var expectedContentTypes =
            extension switch
            {
                ".pdf" => new[] { "application/pdf" },
                ".png" => new[] { "image/png" },
                ".jpg" or ".jpeg" =>
                    new[] { "image/jpeg" },
                ".tif" or ".tiff" =>
                    new[] { "image/tiff" },
                ".bmp" => new[]
                {
                    "image/bmp",
                    "image/x-ms-bmp"
                },
                _ => []
            };

        if (expectedContentTypes.Length == 0)
        {
            return ApiProblemResult.Validation(
                "blood_test_file_extension_invalid",
                "Choose a PDF, PNG, JPG, TIFF or BMP file.");
        }

        var contentType = file.ContentType?
            .Split(';', 2)[0]
            .Trim();
        if (string.IsNullOrWhiteSpace(contentType)
            || !expectedContentTypes.Contains(
                contentType,
                StringComparer.OrdinalIgnoreCase))
        {
            return ApiProblemResult.Validation(
                "blood_test_file_type_invalid",
                $"The file content type does not match {extension}.");
        }

        var header = new byte[8];
        await using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAsync(
            header.AsMemory(),
            cancellationToken);
        if (!HasExpectedSignature(
                extension,
                header.AsSpan(0, bytesRead)))
        {
            return ApiProblemResult.Validation(
                "blood_test_file_signature_invalid",
                "The file contents do not match its extension.");
        }

        return null;
    }

    private static bool HasExpectedSignature(
        string extension,
        ReadOnlySpan<byte> header)
        => extension switch
        {
            ".pdf" => header.StartsWith(
                "%PDF-"u8),
            ".png" => header.StartsWith(
                new byte[]
                {
                    0x89, 0x50, 0x4E, 0x47,
                    0x0D, 0x0A, 0x1A, 0x0A
                }),
            ".jpg" or ".jpeg" => header.StartsWith(
                new byte[] { 0xFF, 0xD8, 0xFF }),
            ".tif" or ".tiff" =>
                header.StartsWith(
                    new byte[] { 0x49, 0x49, 0x2A, 0x00 })
                || header.StartsWith(
                    new byte[] { 0x4D, 0x4D, 0x00, 0x2A }),
            ".bmp" => header.StartsWith(
                new byte[] { 0x42, 0x4D }),
            _ => false
        };

    private static ObjectResult? ValidateBloodTestInput(
        DateTime testDate,
        IReadOnlyCollection<BloodTestMarkerResource> markers,
        bool allowEmptyMarkers)
    {
        if (testDate == default
            || testDate.Date > DateTime.UtcNow.Date)
        {
            return ApiProblemResult.Validation(
                "blood_test_date_invalid",
                "Test date is required and cannot be in the future.");
        }

        if (!allowEmptyMarkers && markers.Count == 0)
        {
            return ApiProblemResult.Validation(
                "blood_test_markers_missing",
                "Add at least one marker.");
        }

        var canonicalNames =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var marker in markers)
        {
            if (string.IsNullOrWhiteSpace(marker.Name))
            {
                return ApiProblemResult.Validation(
                    "blood_test_marker_name_missing",
                    "Every marker needs a name.");
            }

            if (!marker.Value.HasValue)
            {
                return ApiProblemResult.Validation(
                    "blood_test_marker_value_missing",
                    $"{marker.Name} needs a numeric value.");
            }

            if (marker.ReferenceMinValue.HasValue
                && marker.ReferenceMaxValue.HasValue
                && marker.ReferenceMinValue
                    > marker.ReferenceMaxValue)
            {
                return ApiProblemResult.Validation(
                    "blood_test_marker_range_invalid",
                    $"{marker.Name} has a minimum above its maximum.");
            }

            var canonicalName =
                BloodAnalyticsService.NormalizeMetricKey(
                    marker.Name);
            if (!canonicalNames.Add(canonicalName))
            {
                return ApiProblemResult.Validation(
                    "blood_test_marker_duplicate",
                    $"{marker.Name} appears more than once.");
            }
        }

        return null;
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

    private static GetBloodTestResource MapBloodTest(
        BloodTest bloodTest,
        bool includeMarkers)
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
            MarkerCount = bloodTest.MarkerResults.Count,
            UpdatedAtUtc = bloodTest.UpdatedAtUtc,
            Markers = includeMarkers
                ? bloodTest.MarkerResults.Select(marker => new BloodTestMarkerResource
            {
                Id = marker.Id,
                Name = marker.DisplayName,
                Value = marker.NormalizedValue ?? marker.Value,
                Unit = marker.NormalizedUnit ?? marker.Unit,
                RawValueText = marker.RawValueText,
                Confidence = marker.Confidence,
                Status = marker.Status,
                ReferenceMinValue = marker.ReferenceMinValue,
                ReferenceMaxValue = marker.ReferenceMaxValue,
                ReferenceRangeFromReport =
                    marker.ReferenceRangeFromReport
            }).ToList()
                : []
        };
    }

    private static BloodTestSummaryResource
        MapBloodTestSummary(BloodTestSummary bloodTest)
        => new()
        {
            Id = bloodTest.Id,
            TestDate = bloodTest.TestDate,
            LabName = bloodTest.LabName,
            SourceFileName = bloodTest.SourceFileName,
            ProcessingStatus = bloodTest.ProcessingStatus,
            ProcessingNotes = bloodTest.ProcessingNotes,
            RequiresReview =
                bloodTest.ProcessingStatus
                == BloodTestProcessingStatus.PendingReview,
            MarkerCount = bloodTest.MarkerCount,
            UpdatedAtUtc = bloodTest.UpdatedAtUtc
        };
}
