#nullable enable

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
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
        "romberg", "eje eléctrico", "eje electrico",
        "página", "pagina", "muestra", "servicio", "origen", "adeslas", "averroes", "teléf", "telef",
        "dr./dra", "dr/dra", "certificad", "norma une", "iso 9001", "applus", "enac",
        "avda", "avenida", "nuevo mundo", "estudios analíticos", "estudios analiticos",
        "resultado %", "valores de"
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

    public List<BloodTestSummary> GetBloodTestSummaries(
        string userId)
    {
        return _bloodTestsRepository
            .GetBloodTestSummaries(userId);
    }

    public BloodTest? GetBloodTest(Guid id, string userId)
    {
        return _bloodTestsRepository.GetBloodTest(id, userId);
    }

    public List<BloodTestSummary>
        GetPendingReviewBloodTestSummaries(string userId)
    {
        return _bloodTestsRepository
            .GetPendingReviewBloodTestSummaries(userId);
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
            MarkerResults = markers.Select(marker => BuildMarkerResult(
                profile,
                allDefinitions,
                testDate,
                marker.Name,
                marker.Name,
                marker.Unit,
                marker.Value,
                marker.Value?.ToString(),
                1,
                marker.ReferenceMinValue,
                marker.ReferenceMaxValue,
                marker.ReferenceMinValue.HasValue
                || marker.ReferenceMaxValue.HasValue,
                requireConfidenceReview: false)).ToList()
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

    public async Task<BloodTestUploadCreationResult>
        CreateUploadedBloodTestAsync(
            string userId,
            IFormFile file,
            DateTime testDate,
            string? labName,
            CancellationToken cancellationToken)
    {
        await using var memStream = new MemoryStream();
        await file.CopyToAsync(memStream, cancellationToken);
        var fileBytes = memStream.ToArray();
        var contentHash = Convert.ToHexString(
            SHA256.HashData(fileBytes));
        var possibleDuplicateId = await _context.BloodTests
            .IgnoreQueryFilters()
            .Where(test =>
                test.UserId == userId
                && test.SourceContentHash == contentHash)
            .OrderByDescending(test => test.CreatedAtUtc)
            .Select(test => (Guid?)test.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var storedDocument = await _documentStorage.StoreAsync(
            userId,
            file,
            cancellationToken);

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
            SourceContentHash = contentHash,
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

        return new BloodTestUploadCreationResult(
            id,
            possibleDuplicateId);
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
        var bloodTest = _context.BloodTests
            .IgnoreQueryFilters()
            .FirstOrDefault(test =>
                test.Id == bloodTestId
                && test.UserId == userId);

        if (bloodTest is null)
        {
            return;
        }

        var selectedMarkers = SelectMarkers(extraction.Markers, normalization);
        var curatedMarkers = CurateMarkerCandidates(selectedMarkers);
        var extractedMarkers = curatedMarkers
            .Select(marker => BuildMarkerResult(
                profile,
                allDefinitions,
                bloodTest.TestDate,
                marker.Name,
                marker.OriginalName ?? marker.Name,
                marker.Unit,
                marker.Value,
                marker.ValueText,
                marker.Confidence,
                marker.ReferenceMinValue,
                marker.ReferenceMaxValue,
                requireConfidenceReview: true))
            .ToList();

        bloodTest.RawExtractionPayload = BuildCombinedExtractionPayload(extraction, normalization);
        bloodTest.ProcessingNotes = BuildProcessingNotes(
            extraction.ErrorMessage,
            normalization,
            !string.IsNullOrWhiteSpace(
                bloodTest.SourceBlobName),
            extractedMarkers.Count,
            selectedMarkers.Count
            - curatedMarkers.Count);
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
        bloodTest.ProcessingNotes =
            BuildReviewProcessingNotes(
                review.Markers.Count);

        var existingById = bloodTest.MarkerResults
            .ToDictionary(marker => marker.Id);
        var retainedIds = review.Markers
            .Where(marker => marker.Id.HasValue)
            .Select(marker => marker.Id!.Value)
            .ToHashSet();
        var removedMarkers = bloodTest.MarkerResults
            .Where(marker => !retainedIds.Contains(marker.Id))
            .ToList();
        _context.BloodTestMarkerResults.RemoveRange(
            removedMarkers);

        foreach (var marker in review.Markers)
        {
            var existing =
                marker.Id.HasValue
                && existingById.TryGetValue(
                    marker.Id.Value,
                    out var matched)
                    ? matched
                    : null;
            var computed = BuildMarkerResult(
                profile,
                allDefinitions,
                bloodTest.TestDate,
                marker.Name,
                existing?.RawMarkerName ?? marker.Name,
                marker.Unit,
                marker.Value,
                existing?.RawValueText
                    ?? marker.RawValueText,
                existing?.Confidence
                    ?? marker.Confidence,
                marker.ReferenceMinValue,
                marker.ReferenceMaxValue,
                marker.ReferenceRangeFromReport,
                requireConfidenceReview: false);

            if (existing is null)
            {
                bloodTest.MarkerResults.Add(computed);
                continue;
            }

            ApplyReviewedMarker(existing, computed);
        }

        _context.SaveChanges();
        return true;
    }

    public async Task<bool> DeleteBloodTestAsync(
        string userId,
        Guid bloodTestId,
        CancellationToken cancellationToken)
    {
        var bloodTest = await _context.BloodTests
            .FirstOrDefaultAsync(
                test => test.Id == bloodTestId
                        && test.UserId == userId,
                cancellationToken);

        if (bloodTest is null)
        {
            return false;
        }

        var storedDocument =
            string.IsNullOrWhiteSpace(bloodTest.SourceBlobName)
            || string.IsNullOrWhiteSpace(
                bloodTest.SourceBlobContainer)
                ? null
                : new StoredBloodTestDocument
                {
                    ContainerName =
                        bloodTest.SourceBlobContainer,
                    BlobName = bloodTest.SourceBlobName,
                    BlobUri = bloodTest.SourceBlobUri
                              ?? string.Empty
                };

        _context.BloodTests.Remove(bloodTest);
        await _context.SaveChangesAsync(cancellationToken);

        if (storedDocument is not null)
        {
            try
            {
                await _documentStorage.DeleteAsync(
                    storedDocument,
                    cancellationToken);
            }
            catch
            {
                // The health record is already removed. Storage cleanup is
                // best effort so a transient provider failure cannot make
                // the client report a false deletion failure.
            }
        }

        return true;
    }

    public async Task<BloodTestDocumentContent?> GetBloodTestDocumentAsync(
        string userId,
        Guid bloodTestId,
        CancellationToken cancellationToken)
    {
        var bloodTest = await _context.BloodTests
            .AsNoTracking()
            .FirstOrDefaultAsync(
                test => test.Id == bloodTestId
                        && test.UserId == userId,
                cancellationToken);

        if (bloodTest is null
            || string.IsNullOrWhiteSpace(bloodTest.SourceBlobContainer)
            || string.IsNullOrWhiteSpace(bloodTest.SourceBlobName))
        {
            return null;
        }

        var content = await _documentStorage.ReadAsync(
            new StoredBloodTestDocument
            {
                ContainerName = bloodTest.SourceBlobContainer,
                BlobName = bloodTest.SourceBlobName,
                BlobUri = bloodTest.SourceBlobUri ?? string.Empty
            },
            cancellationToken);

        return content is null
            ? null
            : new BloodTestDocumentContent
            {
                Content = content,
                ContentType = string.IsNullOrWhiteSpace(
                    bloodTest.SourceContentType)
                    ? "application/octet-stream"
                    : bloodTest.SourceContentType,
                FileName = string.IsNullOrWhiteSpace(
                    bloodTest.SourceFileName)
                    ? "blood-test-report"
                    : bloodTest.SourceFileName
            };
    }

    private static List<OcrMarkerResult> SelectMarkers(List<OcrMarkerResult> ocrMarkers, MarkerNormalizationResult normalization)
    {
        if (!normalization.Success || normalization.Markers.Count == 0)
        {
            return ocrMarkers;
        }

        var ocrLookup = ocrMarkers
            .Where(m => m.ReferenceMinValue.HasValue || m.ReferenceMaxValue.HasValue || m.Confidence.HasValue)
            .GroupBy(m => m.Name.Trim().ToLowerInvariant())
            .ToDictionary(
                g => g.Key,
                g => g.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var marker in normalization.Markers)
        {
            var lookupKey = (marker.OriginalName ?? marker.Name).Trim().ToLowerInvariant();
            if (!ocrLookup.TryGetValue(lookupKey, out var ocrMarker))
            {
                continue;
            }

            if (!marker.ReferenceMinValue.HasValue && ocrMarker.ReferenceMinValue.HasValue)
            {
                marker.ReferenceMinValue = ocrMarker.ReferenceMinValue;
            }

            if (!marker.ReferenceMaxValue.HasValue && ocrMarker.ReferenceMaxValue.HasValue)
            {
                marker.ReferenceMaxValue = ocrMarker.ReferenceMaxValue;
            }

            if (ocrMarker.Confidence.HasValue)
            {
                marker.Confidence = marker.Confidence.HasValue
                    ? Math.Min(marker.Confidence.Value, ocrMarker.Confidence.Value)
                    : ocrMarker.Confidence;
            }
        }

        return normalization.Markers;
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

    private static string? BuildProcessingNotes(
        string? extractionError,
        MarkerNormalizationResult normalization,
        bool hasStoredDocument,
        int savedMarkerCount,
        int filteredMarkerCount)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(extractionError))
        {
            notes.Add(
                "Some report content could not be read automatically.");
        }

        if (normalization.Success && savedMarkerCount > 0)
        {
            notes.Add(
                $"{savedMarkerCount} suggested markers are ready to review.");
        }
        else if (!string.IsNullOrWhiteSpace(normalization.ErrorMessage))
        {
            notes.Add(
                "Automatic normalization was unavailable; review the extracted suggestions carefully.");
        }

        if (filteredMarkerCount > 0)
        {
            notes.Add(
                $"{filteredMarkerCount} duplicate or non-marker rows were ignored.");
        }

        if (savedMarkerCount == 0)
        {
            notes.Add(
                "No reliable markers were found. Enter the results manually.");
        }

        if (!hasStoredDocument)
        {
            notes.Add("The original report is not available.");
        }

        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    private static string BuildReviewProcessingNotes(
        int markerCount)
    {
        var reviewedNote = markerCount == 0
            ? "Review saved without finalized markers. Additional review is still required."
            : "Blood test markers were reviewed and finalized manually.";

        return reviewedNote;
    }

    public List<HealthTrendPoint> GetMarkerTrends(string userId, string metricKey, DateTime? fromUtc, DateTime? toUtc)
    {
        var normalizedMetricKey = NormalizeMetricKey(metricKey);
        var query = _context.BloodTestMarkerResults
            .IgnoreQueryFilters()
            .Include(result => result.BloodTest)
            .Where(result => result.BloodTest.UserId == userId)
            .Where(result => result.BloodTest.ProcessingStatus == BloodTestProcessingStatus.Processed)
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
            .AsEnumerable()
            .Where(result =>
                NormalizeMetricKey(result.MetricKey)
                == normalizedMetricKey)
            .OrderBy(result => result.BloodTest.TestDate)
            .Select(result => new HealthTrendPoint
            {
                BloodTestId = result.BloodTestId,
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
            .GroupBy(result =>
                NormalizeMetricKey(result.MetricKey))
            .Select(group => group.OrderByDescending(result => result.BloodTest.TestDate).First())
            .OrderBy(result => result.DisplayName)
            .Select(result => new LatestMarkerSnapshot
            {
                BloodTestId = result.BloodTestId,
                MetricKey = NormalizeMetricKey(
                    result.MetricKey),
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

    public async Task ReclassifyMarkersForUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var profile = await _context.HealthProfiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.UserId == userId,
                cancellationToken);
        var definitions = await _context
            .HealthMetricDefinitions
            .IgnoreQueryFilters()
            .Include(item => item.ReferenceRanges)
            .ToListAsync(cancellationToken);
        var markers = await _context.BloodTestMarkerResults
            .IgnoreQueryFilters()
            .Include(marker => marker.BloodTest)
            .Where(marker =>
                marker.BloodTest.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var marker in markers)
        {
            var metricKey = NormalizeMetricKey(
                string.IsNullOrWhiteSpace(marker.DisplayName)
                    ? marker.MetricKey
                    : marker.DisplayName);
            var definition = FindMetricDefinition(
                definitions,
                metricKey);
            var normalized =
                BloodMarkerValueNormalizer.Normalize(
                    metricKey,
                    marker.Unit,
                    marker.Value,
                    null,
                    null,
                    definition?.DefaultUnit);
            var referenceRange = ResolveReferenceRange(
                definition,
                profile,
                normalized.Unit,
                marker.BloodTest.TestDate);

            marker.MetricKey =
                definition?.Key ?? metricKey;
            marker.DisplayName =
                definition?.DisplayName
                ?? marker.DisplayName;
            marker.NormalizedValue = normalized.Value;
            marker.NormalizedUnit = normalized.Unit;

            if (definition is not null
                && !marker.ReferenceRangeFromReport)
            {
                marker.ReferenceMinValue =
                    referenceRange?.MinValue;
                marker.ReferenceMaxValue =
                    referenceRange?.MaxValue;
            }

            marker.Status = ClassifyValue(
                marker.NormalizedValue,
                marker.ReferenceMinValue,
                marker.ReferenceMaxValue);
            if (marker.BloodTest.ProcessingStatus
                    == BloodTestProcessingStatus.PendingReview
                && marker.Confidence is < 0.8)
            {
                marker.Status = MarkerStatus.RequiresReview;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static BloodTestMarkerResult BuildMarkerResult(
        HealthProfile? profile,
        List<HealthMetricDefinition> allDefinitions,
        DateTime testDate,
        string displayName,
        string rawMarkerName,
        string? unit,
        decimal? value,
        string? rawValueText,
        double? confidence,
        decimal? ocrReferenceMinValue = null,
        decimal? ocrReferenceMaxValue = null,
        bool referenceRangeFromReport = false,
        bool requireConfidenceReview = true)
    {
        var metricKey = NormalizeMetricKey(displayName);
        var metricDefinition = FindMetricDefinition(
            allDefinitions,
            metricKey);
        var normalized =
            BloodMarkerValueNormalizer.Normalize(
                metricKey,
                unit,
                value,
                ocrReferenceMinValue,
                ocrReferenceMaxValue,
                metricDefinition?.DefaultUnit);

        var hasProvidedReference =
            normalized.ReferenceMin.HasValue
            || normalized.ReferenceMax.HasValue;
        var referenceRange = hasProvidedReference
            ? new ReferenceRange
            {
                MinValue = normalized.ReferenceMin,
                MaxValue = normalized.ReferenceMax,
                Unit = normalized.Unit ?? string.Empty
            }
            : ResolveReferenceRange(
                metricDefinition,
                profile,
                normalized.Unit,
                testDate);

        var status = ClassifyValue(
            normalized.Value,
            referenceRange?.MinValue,
            referenceRange?.MaxValue);

        if (requireConfidenceReview
            && confidence.HasValue
            && confidence.Value < 0.8
            && status != MarkerStatus.RequiresReview)
        {
            status = MarkerStatus.RequiresReview;
        }

        return new BloodTestMarkerResult
        {
            MetricKey = metricDefinition?.Key ?? metricKey,
            DisplayName = metricDefinition?.DisplayName ?? displayName,
            RawMarkerName = rawMarkerName,
            Unit = unit,
            NormalizedUnit = normalized.Unit,
            RawValueText = rawValueText,
            Value = value,
            NormalizedValue = normalized.Value,
            Confidence = confidence,
            Status = status,
            ReferenceMinValue = referenceRange?.MinValue,
            ReferenceMaxValue = referenceRange?.MaxValue,
            ReferenceRangeFromReport =
                referenceRangeFromReport
                || hasProvidedReference
        };
    }

    private static void ApplyReviewedMarker(
        BloodTestMarkerResult target,
        BloodTestMarkerResult source)
    {
        target.MetricKey = source.MetricKey;
        target.DisplayName = source.DisplayName;
        target.Unit = source.Unit;
        target.NormalizedUnit = source.NormalizedUnit;
        target.Value = source.Value;
        target.NormalizedValue = source.NormalizedValue;
        target.Status = source.Status;
        target.ReferenceMinValue =
            source.ReferenceMinValue;
        target.ReferenceMaxValue =
            source.ReferenceMaxValue;
        target.ReferenceRangeFromReport =
            source.ReferenceRangeFromReport;
    }

    private static MarkerStatus ClassifyValue(
        decimal? value,
        decimal? referenceMin,
        decimal? referenceMax)
    {
        if (value is null)
        {
            return MarkerStatus.RequiresReview;
        }

        if (!referenceMin.HasValue
            && !referenceMax.HasValue)
        {
            return MarkerStatus.Unknown;
        }

        if (referenceMin.HasValue
            && value.Value < referenceMin.Value)
        {
            return MarkerStatus.Low;
        }

        if (referenceMax.HasValue
            && value.Value > referenceMax.Value)
        {
            return MarkerStatus.High;
        }

        return MarkerStatus.InRange;
    }

    private static ReferenceRange? ResolveReferenceRange(
        HealthMetricDefinition? metricDefinition,
        HealthProfile? profile,
        string? unit,
        DateTime testDate)
    {
        if (metricDefinition is null)
        {
            return null;
        }

        var age = profile?.DateOfBirth is null
            ? (int?)null
            : CalculateAge(
                profile.DateOfBirth.Value,
                DateOnly.FromDateTime(testDate));

        return metricDefinition.ReferenceRanges
            .Where(range =>
                string.IsNullOrWhiteSpace(unit)
                || string.Equals(
                    NormalizeUnitKey(range.Unit),
                    NormalizeUnitKey(unit),
                    StringComparison.OrdinalIgnoreCase))
            .Where(range => !range.BiologicalSex.HasValue || range.BiologicalSex == profile?.BiologicalSex)
            .Where(range => !range.MinAge.HasValue || (age.HasValue && age.Value >= range.MinAge.Value))
            .Where(range => !range.MaxAge.HasValue || (age.HasValue && age.Value <= range.MaxAge.Value))
            .OrderBy(range => range.BiologicalSex.HasValue ? 0 : 1)
            .ThenBy(range => range.MinAge.HasValue ? 0 : 1)
            .FirstOrDefault();
    }

    private static HealthMetricDefinition?
        FindMetricDefinition(
            IEnumerable<HealthMetricDefinition> definitions,
            string metricKey)
        => definitions.FirstOrDefault(definition =>
            NormalizeMetricKey(definition.Key)
                == metricKey
            || NormalizeMetricKey(definition.DisplayName)
                == metricKey);

    private static string NormalizeUnitKey(string? unit)
        => (unit ?? string.Empty)
            .Trim()
            .Replace("μ", "u", StringComparison.Ordinal)
            .Replace("µ", "u", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

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
        => BloodMarkerValueNormalizer.NormalizeMetricKey(value);
}

public class ManualBloodTestMarkerInput
{
    public string Name { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    public decimal? ReferenceMinValue { get; set; }
    public decimal? ReferenceMaxValue { get; set; }
}

public class ReviewBloodTestInput
{
    public DateTime TestDate { get; set; }
    public string? LabName { get; set; }
    public List<ReviewBloodTestMarkerInput> Markers { get; set; } = new();
}

public class ReviewBloodTestMarkerInput
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    public string? RawValueText { get; set; }
    public double? Confidence { get; set; }
    public decimal? ReferenceMinValue { get; set; }
    public decimal? ReferenceMaxValue { get; set; }
    public bool ReferenceRangeFromReport { get; set; }
}

public sealed record BloodTestUploadCreationResult(
    Guid Id,
    Guid? PossibleDuplicateBloodTestId);

public class HealthTrendPoint
{
    public Guid BloodTestId { get; set; }
    public DateTime Time { get; set; }
    public decimal Value { get; set; }
    public string? Unit { get; set; }
    public MarkerStatus Status { get; set; }
}

public class LatestMarkerSnapshot
{
    public Guid BloodTestId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    public MarkerStatus Status { get; set; }
    public DateTime TestDate { get; set; }
    public decimal? ReferenceMinValue { get; set; }
    public decimal? ReferenceMaxValue { get; set; }
}

public sealed class BloodTestDocumentContent
{
    public byte[] Content { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; } = "application/octet-stream";
    public string FileName { get; init; } = "blood-test-report";
}
