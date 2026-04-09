#nullable enable

using System.Text.Json;
using System.Text.RegularExpressions;
using healthspan_api.Events;
using healthspan_api.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

public class BloodAnalyticsService
{
    private static readonly Regex SuspiciousNameUnitPattern = new(@"(?:x10\^?\d+|10\^\d+|/mm|/µl|/ul|/ml|g/dl|mg/dl)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] RejectedCandidateNameFragments =
    {
        "audiometr", "electrocard", "agudeza visual", "vision", "visión", "aumento", "anamnesis", "exploración", "exploracion",
        "david", "alonso", "blan", "dni", "pasaporte", "nombre", "apellidos", "sexo", "edad", "telefono", "teléfono",
        "oído", "oido", "campo", "pérdida", "perdida", "klockhoff", "sedimento", "sinusal", "ritmo", "indicio",
        "romberg", "eje eléctrico", "eje electrico"
    };
    private readonly BloodTestsRepository _bloodTestsRepository;
    private readonly MainDbContext _context;
    private readonly IBloodTestDocumentStorage _documentStorage;
    private readonly IOcrExtractionProvider _ocrExtractionProvider;
    private readonly IMarkerNormalizationProvider _markerNormalizationProvider;
    private readonly IMediator _mediator;
    private readonly BloodTestProcessingChannel _processingChannel;

    public BloodAnalyticsService(BloodTestsRepository bloodTestsRepository, MainDbContext context, IBloodTestDocumentStorage documentStorage, IOcrExtractionProvider ocrExtractionProvider, IMarkerNormalizationProvider markerNormalizationProvider, IMediator mediator, BloodTestProcessingChannel processingChannel)
    {
        _bloodTestsRepository = bloodTestsRepository;
        _context = context;
        _documentStorage = documentStorage;
        _ocrExtractionProvider = ocrExtractionProvider;
        _markerNormalizationProvider = markerNormalizationProvider;
        _mediator = mediator;
        _processingChannel = processingChannel;
    }

    public List<BloodTest> GetBloodTests(string userId)
    {
        return _bloodTestsRepository.GetBloodTests(userId);
    }

    public BloodTest? GetBloodTest(Guid id, string userId)
    {
        return _bloodTestsRepository.GetBloodTest(id, userId);
    }

    public List<BloodTest> GetPendingReviewBloodTests(string userId)
    {
        return _bloodTestsRepository.GetPendingReviewBloodTests(userId);
    }

    public async Task<Guid> CreateManualBloodTestAsync(string userId, DateTime testDate, string? labName, List<ManualBloodTestMarkerInput> markers)
    {
        var profile = _context.HealthProfiles.IgnoreQueryFilters().FirstOrDefault(item => item.UserId == userId);
        var allDefinitions = _context.HealthMetricDefinitions.IgnoreQueryFilters().Include(item => item.ReferenceRanges).ToList();

        var bloodTest = new BloodTest
        {
            UserId = userId,
            TestDate = testDate,
            LabName = labName,
            ProcessingStatus = markers.Count == 0 ? BloodTestProcessingStatus.PendingReview : BloodTestProcessingStatus.Processed,
            MarkerResults = markers.Select(marker => BuildMarkerResult(profile, allDefinitions, marker.Name, marker.Name, marker.Unit, marker.Value, marker.Value?.ToString(), 1)).ToList()
        };

        var id = _bloodTestsRepository.AddBloodTest(bloodTest);

        try
        {
            await _mediator.Publish(new BloodTestProcessedEvent(id, userId));
        }
        catch (Exception)
        {
            // Non-critical: event handlers may fail without impacting the created blood test
        }

        return id;
    }

    public async Task<Guid> CreateUploadedBloodTestAsync(string userId, IFormFile file, DateTime testDate, string? labName, CancellationToken cancellationToken)
    {
        var storedDocument = await _documentStorage.StoreAsync(userId, file, cancellationToken);

        await using var memStream = new MemoryStream();
        await file.CopyToAsync(memStream, cancellationToken);
        var fileBytes = memStream.ToArray();

        var bloodTest = new BloodTest
        {
            UserId = userId,
            TestDate = testDate,
            LabName = labName,
            SourceFileName = file.FileName,
            SourceContentType = file.ContentType,
            SourceBlobContainer = storedDocument?.ContainerName,
            SourceBlobName = storedDocument?.BlobName,
            SourceBlobUri = storedDocument?.BlobUri,
            ProcessingStatus = BloodTestProcessingStatus.Processing,
            ProcessingNotes = "Upload accepted. OCR extraction and marker normalization are running in the background."
        };

        var id = _bloodTestsRepository.AddBloodTest(bloodTest);

        await _processingChannel.Writer.WriteAsync(new BloodTestProcessingJob
        {
            BloodTestId = id,
            UserId = userId,
            FileBytes = fileBytes,
            FileName = file.FileName,
            ContentType = file.ContentType
        }, cancellationToken);

        return id;
    }

    public async Task ProcessBloodTestAsync(Guid bloodTestId, string userId, byte[] fileBytes, string fileName, string contentType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(fileBytes);
        var extraction = await _ocrExtractionProvider.ExtractAsync(stream, fileName, contentType, cancellationToken);
        var normalization = await _markerNormalizationProvider.NormalizeAsync(new MarkerNormalizationRequest
        {
            FileName = fileName,
            ContentType = contentType,
            RawExtractionPayload = extraction.RawPayload,
            CandidateMarkers = extraction.Markers
        }, cancellationToken);

        var profile = _context.HealthProfiles.IgnoreQueryFilters().FirstOrDefault(item => item.UserId == userId);
        var allDefinitions = _context.HealthMetricDefinitions.IgnoreQueryFilters().Include(item => item.ReferenceRanges).ToList();

        var selectedMarkers = SelectMarkers(extraction.Markers, normalization);
        var curatedMarkers = CurateMarkerCandidates(selectedMarkers);
        var extractedMarkers = curatedMarkers
            .Select(marker => BuildMarkerResult(profile, allDefinitions, marker.Name, marker.OriginalName ?? marker.Name, marker.Unit, marker.Value, marker.ValueText, marker.Confidence))
            .ToList();

        var bloodTest = _context.BloodTests
            .IgnoreQueryFilters()
            .FirstOrDefault(test => test.Id == bloodTestId && test.UserId == userId);

        if (bloodTest is null)
        {
            return;
        }

        bloodTest.RawExtractionPayload = BuildCombinedExtractionPayload(extraction, normalization);
        bloodTest.ProcessingNotes = BuildProcessingNotes(extraction.ErrorMessage, normalization, null, extraction.Markers.Count, extractedMarkers.Count, selectedMarkers.Count - curatedMarkers.Count);
        bloodTest.ProcessingStatus = BloodTestProcessingStatus.PendingReview;
        bloodTest.UpdatedAtUtc = DateTime.UtcNow;
        bloodTest.MarkerResults = extractedMarkers;

        _context.SaveChanges();

        try
        {
            await _mediator.Publish(new BloodTestProcessedEvent(bloodTestId, userId), cancellationToken);
        }
        catch (Exception)
        {
            // Non-critical: event handlers may fail in background context (no HttpContext)
        }
    }

    public void MarkBloodTestFailed(Guid bloodTestId, string userId, string errorMessage)
    {
        var bloodTest = _context.BloodTests
            .IgnoreQueryFilters()
            .FirstOrDefault(test => test.Id == bloodTestId && test.UserId == userId);

        if (bloodTest is null)
        {
            return;
        }

        bloodTest.ProcessingStatus = BloodTestProcessingStatus.ExtractionFailed;
        bloodTest.ProcessingNotes = errorMessage;
        bloodTest.UpdatedAtUtc = DateTime.UtcNow;
        _context.SaveChanges();
    }

    public bool ReviewBloodTest(string userId, Guid bloodTestId, ReviewBloodTestInput review)
    {
        var bloodTest = _context.BloodTests
            .IgnoreQueryFilters()
            .Include(test => test.MarkerResults)
            .FirstOrDefault(test => test.Id == bloodTestId && test.UserId == userId);

        if (bloodTest is null)
        {
            return false;
        }

        var profile = _context.HealthProfiles.IgnoreQueryFilters().FirstOrDefault(item => item.UserId == userId);
        var allDefinitions = _context.HealthMetricDefinitions.IgnoreQueryFilters().Include(item => item.ReferenceRanges).ToList();

        bloodTest.TestDate = review.TestDate;
        bloodTest.LabName = review.LabName;
        bloodTest.UpdatedAtUtc = DateTime.UtcNow;
        bloodTest.ProcessingStatus = review.Markers.Count == 0 ? BloodTestProcessingStatus.PendingReview : BloodTestProcessingStatus.Processed;
        bloodTest.ProcessingNotes = BuildReviewProcessingNotes(bloodTest.ProcessingNotes, review.Markers.Count);

        _context.BloodTestMarkerResults.RemoveRange(bloodTest.MarkerResults);
        bloodTest.MarkerResults = review.Markers
            .Select(marker => BuildMarkerResult(profile, allDefinitions, marker.Name, marker.Name, marker.Unit, marker.Value, marker.RawValueText, marker.Confidence))
            .ToList();

        _context.SaveChanges();
        return true;
    }

    private static List<OcrMarkerResult> SelectMarkers(List<OcrMarkerResult> ocrMarkers, MarkerNormalizationResult normalization)
    {
        return normalization.Success && normalization.Markers.Count > 0
            ? normalization.Markers
            : ocrMarkers;
    }

    private static List<OcrMarkerResult> CurateMarkerCandidates(List<OcrMarkerResult> markers)
    {
        var curated = new List<OcrMarkerResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var marker in markers)
        {
            if (!IsClinicallyUsefulMarkerCandidate(marker))
            {
                continue;
            }

            var fingerprint = BuildCandidateFingerprint(marker);
            if (!seen.Add(fingerprint))
            {
                if (!string.IsNullOrWhiteSpace(marker.Unit))
                {
                    var existing = curated.FirstOrDefault(m => BuildCandidateFingerprint(m).Equals(fingerprint, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null && string.IsNullOrWhiteSpace(existing.Unit))
                    {
                        existing.Unit = marker.Unit;
                    }
                }
                continue;
            }

            curated.Add(marker);
        }

        return curated;
    }

    private static bool IsClinicallyUsefulMarkerCandidate(OcrMarkerResult marker)
    {
        var normalizedName = NormalizeCandidateText(marker.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        if (char.IsDigit(normalizedName[0]))
        {
            return false;
        }

        if (normalizedName.StartsWith("normal.", StringComparison.OrdinalIgnoreCase)
            || normalizedName.StartsWith("negativo ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (RejectedCandidateNameFragments.Any(fragment => normalizedName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (SuspiciousNameUnitPattern.IsMatch(normalizedName))
        {
            return false;
        }

        if (CountLetters(normalizedName) < 3)
        {
            return false;
        }

        if (normalizedName.Contains(' ') && LooksLikeMergedMarkerNames(normalizedName))
        {
            return false;
        }

        if (LooksLikeIdentityText(marker.Unit) || LooksLikeIdentityText(marker.ValueText)
            || LooksLikeSectionHeader(marker.Unit))
        {
            return false;
        }

        if (marker.Value.HasValue && marker.Value.Value >= 100000)
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeMergedMarkerNames(string normalizedName)
    {
        var tokens = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return false;
        }

        var knownMarkerWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "got", "gpt", "ggt", "hdl", "ldl", "vldl", "hcm", "vcm", "plaquetas",
            "hematies", "hemoglobina", "hematocrito", "leucocitos", "linfocitos",
            "monocitos", "eosinofilos", "basofilos", "granulocitos", "glucosa",
            "colesterol", "trigliceridos", "creatinina", "urea", "bilirrubina",
            "proteinas", "urobilinogeno", "eritrocitos", "densidad", "ph"
        };

        var matchCount = tokens.Count(t => knownMarkerWords.Contains(t));
        return matchCount >= 2;
    }

    private static bool LooksLikeIdentityText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeCandidateText(value);
        if (normalized.Length > 24 && CountLetters(normalized) >= 8)
        {
            return true;
        }

        return RejectedCandidateNameFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeSectionHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith('.') || trimmed.Contains("OBSERVACION", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Normal.", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCandidateFingerprint(OcrMarkerResult marker)
    {
        var name = string.IsNullOrWhiteSpace(marker.OriginalName) ? marker.Name : marker.OriginalName;
        return $"{name?.Trim().ToLowerInvariant()}|{marker.Value}";
    }

    private static string NormalizeCandidateText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
    }

    private static int CountLetters(string value)
    {
        return value.Count(char.IsLetter);
    }

    private static string BuildCombinedExtractionPayload(OcrExtractionResult extraction, MarkerNormalizationResult normalization)
    {
        object? parsedOcrPayload = extraction.RawPayload;
        if (!string.IsNullOrWhiteSpace(extraction.RawPayload))
        {
            try
            {
                parsedOcrPayload = JsonSerializer.Deserialize<JsonElement>(extraction.RawPayload);
            }
            catch
            {
            }
        }

        object? parsedNormalizationResponse = normalization.RawResponse;
        if (!string.IsNullOrWhiteSpace(normalization.RawResponse))
        {
            try
            {
                parsedNormalizationResponse = JsonSerializer.Deserialize<JsonElement>(normalization.RawResponse);
            }
            catch
            {
            }
        }

        return JsonSerializer.Serialize(new
        {
            ocr = new
            {
                extraction.Success,
                extraction.ErrorMessage,
                markerCount = extraction.Markers.Count,
                payload = parsedOcrPayload
            },
            llmNormalization = new
            {
                normalization.Success,
                normalization.ProviderName,
                normalization.Model,
                normalization.PromptVersion,
                normalization.ErrorMessage,
                normalization.LatencyMs,
                normalization.InputTokenEstimate,
                normalization.OutputTokenEstimate,
                markerCount = normalization.Markers.Count,
                response = parsedNormalizationResponse
            }
        });
    }

    private static string? BuildProcessingNotes(string? extractionError, MarkerNormalizationResult normalization, StoredBloodTestDocument? storedDocument, int ocrMarkerCount, int savedMarkerCount, int filteredMarkerCount)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(extractionError))
        {
            notes.Add(extractionError);
        }

        if (normalization.Success && savedMarkerCount > 0)
        {
            notes.Add($"{normalization.ProviderName} normalized {savedMarkerCount} marker candidates using model {normalization.Model}. Review required before finalizing.");
        }
        else if (!string.IsNullOrWhiteSpace(normalization.ErrorMessage))
        {
            notes.Add($"LLM normalization skipped or failed. Using deterministic OCR candidates only. {normalization.ErrorMessage}");
        }

        if (ocrMarkerCount > 0)
        {
            notes.Add($"OCR extracted {ocrMarkerCount} raw marker candidates before review.");
        }

        if (filteredMarkerCount > 0)
        {
            notes.Add($"Discarded {filteredMarkerCount} non-clinical or duplicate rows before review.");
        }

        if (savedMarkerCount > 0)
        {
            notes.Add($"Saved {savedMarkerCount} marker candidates for review. Review required before finalizing.");
        }
        else
        {
            notes.Add("No reliable marker candidates were extracted. Manual review required.");
        }

        if (storedDocument is null)
        {
            notes.Add("Source document was not archived because no document storage provider is enabled or configured.");
        }

        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    private static string BuildReviewProcessingNotes(string? existingNotes, int markerCount)
    {
        var reviewedNote = markerCount == 0
            ? "Review saved without finalized markers. Additional review is still required."
            : "Blood test markers were reviewed and finalized manually.";

        return string.IsNullOrWhiteSpace(existingNotes)
            ? reviewedNote
            : $"{existingNotes} {reviewedNote}";
    }

    public List<HealthTrendPoint> GetMarkerTrends(string userId, string metricKey, DateTime? fromUtc, DateTime? toUtc)
    {
        var normalizedMetricKey = NormalizeMetricKey(metricKey);
        var query = _context.BloodTestMarkerResults
            .IgnoreQueryFilters()
            .Include(result => result.BloodTest)
            .Where(result => result.BloodTest.UserId == userId)
            .Where(result => result.BloodTest.ProcessingStatus == BloodTestProcessingStatus.Processed)
            .Where(result => result.MetricKey == normalizedMetricKey)
            .Where(result => result.NormalizedValue != null)
            .AsQueryable();

        if (fromUtc.HasValue)
        {
            query = query.Where(result => result.BloodTest.TestDate >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(result => result.BloodTest.TestDate <= toUtc.Value);
        }

        return query
            .OrderBy(result => result.BloodTest.TestDate)
            .Select(result => new HealthTrendPoint
            {
                Time = result.BloodTest.TestDate,
                Value = result.NormalizedValue ?? 0,
                Unit = result.NormalizedUnit ?? result.Unit,
                Status = result.Status
            })
            .ToList();
    }

    public List<LatestMarkerSnapshot> GetLatestMarkerSnapshots(string userId)
    {
        return _context.BloodTestMarkerResults
            .IgnoreQueryFilters()
            .Include(result => result.BloodTest)
            .Where(result => result.BloodTest.UserId == userId)
            .Where(result => result.BloodTest.ProcessingStatus == BloodTestProcessingStatus.Processed)
            .AsEnumerable()
            .GroupBy(result => result.MetricKey)
            .Select(group => group.OrderByDescending(result => result.BloodTest.TestDate).First())
            .OrderBy(result => result.DisplayName)
            .Select(result => new LatestMarkerSnapshot
            {
                MetricKey = result.MetricKey,
                DisplayName = result.DisplayName,
                Value = result.NormalizedValue ?? result.Value,
                Unit = result.NormalizedUnit ?? result.Unit,
                Status = result.Status,
                TestDate = result.BloodTest.TestDate,
                ReferenceMinValue = result.ReferenceMinValue,
                ReferenceMaxValue = result.ReferenceMaxValue
            })
            .ToList();
    }

    private static BloodTestMarkerResult BuildMarkerResult(HealthProfile? profile, List<HealthMetricDefinition> allDefinitions, string displayName, string rawMarkerName, string? unit, decimal? value, string? rawValueText, double? confidence)
    {
        var normalizedUnit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        var metricKey = NormalizeMetricKey(displayName);
        var metricDefinition = allDefinitions.FirstOrDefault(item => item.Key == metricKey);

        var referenceRange = ResolveReferenceRange(metricDefinition, profile, normalizedUnit);
        var status = ClassifyValue(value, referenceRange);

        return new BloodTestMarkerResult
        {
            MetricKey = metricDefinition?.Key ?? metricKey,
            DisplayName = metricDefinition?.DisplayName ?? displayName,
            RawMarkerName = rawMarkerName,
            Unit = unit,
            NormalizedUnit = normalizedUnit ?? metricDefinition?.DefaultUnit,
            RawValueText = rawValueText,
            Value = value,
            NormalizedValue = value,
            Confidence = confidence,
            Status = status,
            ReferenceMinValue = referenceRange?.MinValue,
            ReferenceMaxValue = referenceRange?.MaxValue
        };
    }

    private static MarkerStatus ClassifyValue(decimal? value, ReferenceRange? referenceRange)
    {
        if (value is null)
        {
            return MarkerStatus.RequiresReview;
        }

        if (referenceRange is null)
        {
            return MarkerStatus.Unknown;
        }

        if (referenceRange.MinValue.HasValue && value.Value < referenceRange.MinValue.Value)
        {
            return MarkerStatus.Low;
        }

        if (referenceRange.MaxValue.HasValue && value.Value > referenceRange.MaxValue.Value)
        {
            return MarkerStatus.High;
        }

        return MarkerStatus.InRange;
    }

    private static ReferenceRange? ResolveReferenceRange(HealthMetricDefinition? metricDefinition, HealthProfile? profile, string? unit)
    {
        if (metricDefinition is null)
        {
            return null;
        }

        var age = profile?.DateOfBirth is null
            ? (int?)null
            : CalculateAge(profile.DateOfBirth.Value, DateOnly.FromDateTime(DateTime.UtcNow));

        return metricDefinition.ReferenceRanges
            .Where(range => string.IsNullOrWhiteSpace(unit) || range.Unit == unit)
            .Where(range => !range.BiologicalSex.HasValue || range.BiologicalSex == profile?.BiologicalSex)
            .Where(range => !range.MinAge.HasValue || (age.HasValue && age.Value >= range.MinAge.Value))
            .Where(range => !range.MaxAge.HasValue || (age.HasValue && age.Value <= range.MaxAge.Value))
            .OrderBy(range => range.BiologicalSex.HasValue ? 0 : 1)
            .ThenBy(range => range.MinAge.HasValue ? 0 : 1)
            .FirstOrDefault();
    }

    private static int CalculateAge(DateOnly dateOfBirth, DateOnly asOf)
    {
        var age = asOf.Year - dateOfBirth.Year;
        if (asOf < dateOfBirth.AddYears(age))
        {
            age--;
        }

        return Math.Max(0, age);
    }

    public static string NormalizeMetricKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, "[^a-z0-9]+", "-");
        return normalized.Trim('-');
    }
}

public class ManualBloodTestMarkerInput
{
    public string Name { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
}

public class ReviewBloodTestInput
{
    public DateTime TestDate { get; set; }
    public string? LabName { get; set; }
    public List<ReviewBloodTestMarkerInput> Markers { get; set; } = new();
}

public class ReviewBloodTestMarkerInput
{
    public string Name { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    public string? RawValueText { get; set; }
    public double? Confidence { get; set; }
}

public class HealthTrendPoint
{
    public DateTime Time { get; set; }
    public decimal Value { get; set; }
    public string? Unit { get; set; }
    public MarkerStatus Status { get; set; }
}

public class LatestMarkerSnapshot
{
    public string MetricKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    public MarkerStatus Status { get; set; }
    public DateTime TestDate { get; set; }
    public decimal? ReferenceMinValue { get; set; }
    public decimal? ReferenceMaxValue { get; set; }
}