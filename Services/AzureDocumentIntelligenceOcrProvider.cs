#nullable enable

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;
using healthspan_api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Orchestrates OCR extraction via Azure Document Intelligence.
/// Delegates parsing to <see cref="OcrMarkerParser"/>, page scoring to <see cref="OcrPageScorer"/>,
/// and marker extraction to <see cref="OcrMarkerExtractor"/>.
/// </summary>
public class AzureDocumentIntelligenceOcrProvider : IOcrExtractionProvider
{
    private const int FreeTierPageChunkSize = 2;
    private const int MaxChunkedPageScan = 60;

    private readonly AzureDocumentIntelligenceSettings _settings;
    private readonly DocumentIntelligenceClient? _client;
    private readonly ILogger<AzureDocumentIntelligenceOcrProvider> _logger;

    public AzureDocumentIntelligenceOcrProvider(IOptions<AzureDocumentIntelligenceSettings> settings, ILogger<AzureDocumentIntelligenceOcrProvider> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        if (_settings.Enabled && !string.IsNullOrWhiteSpace(_settings.Endpoint) && !string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _client = new DocumentIntelligenceClient(new Uri(_settings.Endpoint), new AzureKeyCredential(_settings.ApiKey));
        }
    }

    public async Task<OcrExtractionResult> ExtractAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogWarning("OCR extraction skipped because Azure Document Intelligence is disabled.");
            return FailResult("Azure Document Intelligence is not enabled.");
        }

        if (_client is null)
        {
            _logger.LogError("OCR extraction failed: client not configured. Endpoint: {HasEndpoint}, API key: {HasApiKey}",
                !string.IsNullOrWhiteSpace(_settings.Endpoint), !string.IsNullOrWhiteSpace(_settings.ApiKey));
            return FailResult("Azure Document Intelligence is enabled but endpoint or API key is missing.");
        }

        try
        {
            return await ExecuteExtractionAsync(stream, fileName, contentType, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure DI request failed. Status: {Status}, ErrorCode: {ErrorCode}, Message: {Message}", ex.Status, ex.ErrorCode, ex.Message);
            return FailResult($"Azure Document Intelligence request failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected OCR extraction failure for file {FileName}.", fileName);
            return FailResult($"Unexpected OCR extraction failure: {ex.Message}");
        }
    }

    // ── Core orchestration ──────────────────────────────────────────────────

    private async Task<OcrExtractionResult> ExecuteExtractionAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
            stream.Position = 0;

        var configuredPages = NormalizeConfiguredPages(_settings.Pages);
        _logger.LogInformation(
            "Sending document to Azure DI. Endpoint: {Endpoint}, ModelId: {ModelId}, Pages: {Pages}, File: {FileName}, ContentType: {ContentType}, SizeBytes: {SizeBytes}",
            _settings.Endpoint, _settings.ModelId, configuredPages ?? "<all>", fileName, contentType, TryGetStreamLength(stream));

        var binaryData = await BinaryData.FromStreamAsync(stream, cancellationToken);
        var operation = await AnalyzeWithConfiguredPagesAsync(binaryData, configuredPages, cancellationToken);
        var result = operation.Value;

        var analysisResults = new List<AnalyzeResult> { result };
        var extractedMarkers = OcrMarkerExtractor.ExtractMarkers(result);
        HashSet<int>? selectedPages = null;

        if (ShouldAttemptChunkedPageScan(result, extractedMarkers.Count, configuredPages))
        {
            _logger.LogWarning(
                "Possible page-limited analysis detected (Pages: {PageCount}, Tables: {TableCount}, Markers: {MarkerCount}). Attempting chunked page scan.",
                result.Pages.Count, result.Tables.Count, extractedMarkers.Count);

            var additionalResults = await AnalyzeAdditionalPageChunksAsync(binaryData, cancellationToken);
            analysisResults.AddRange(additionalResults);

            if (additionalResults.Count > 0)
            {
                extractedMarkers = OcrMarkerExtractor.MergeMarkersAcrossResults(analysisResults);
                _logger.LogInformation("Chunked scan completed. Chunks: {ChunkCount}, CombinedMarkers: {MarkerCount}",
                    additionalResults.Count, extractedMarkers.Count);
            }
        }

        if (string.IsNullOrWhiteSpace(configuredPages))
        {
            var pageScores = OcrPageScorer.GetCombinedPageScores(analysisResults);
            selectedPages = OcrPageScorer.SelectBestPages(pageScores, _settings.AutoTopPageCount, _settings.AutoMinPageScore);

            if (selectedPages.Count > 0)
            {
                extractedMarkers = OcrMarkerExtractor.MergeMarkersAcrossResultsStrict(analysisResults, selectedPages);
                _logger.LogInformation("Auto strict page selection. SelectedPages: {SelectedPages}, MarkerCount: {MarkerCount}",
                    string.Join(",", selectedPages.OrderBy(p => p)), extractedMarkers.Count);
                _logger.LogInformation("Auto strict top page scores: {TopScores}",
                    string.Join(" | ", pageScores.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(12).Select(kv => $"p{kv.Key}={kv.Value}")));
            }
        }

        LogExtractionResults(result, analysisResults, extractedMarkers);
        return BuildExtractionResult(analysisResults, extractedMarkers, selectedPages);
    }

    // ── Azure DI communication ──────────────────────────────────────────────

    private async Task<Operation<AnalyzeResult>> AnalyzeWithConfiguredPagesAsync(BinaryData binaryData, string? configuredPages, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredPages))
            return await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, _settings.ModelId, binaryData, cancellationToken: cancellationToken);

        var options = new AnalyzeDocumentOptions(_settings.ModelId, binaryData) { Pages = configuredPages };
        return await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
    }

    private async Task<List<AnalyzeResult>> AnalyzeAdditionalPageChunksAsync(BinaryData binaryData, CancellationToken cancellationToken)
    {
        var results = new List<AnalyzeResult>();
        for (var startPage = FreeTierPageChunkSize + 1; startPage <= MaxChunkedPageScan; startPage += FreeTierPageChunkSize)
        {
            var chunkResult = await TryAnalyzePageChunkAsync(binaryData, startPage, cancellationToken);
            if (chunkResult is null)
                break;
            results.Add(chunkResult);
            if (chunkResult.Pages.Count < FreeTierPageChunkSize)
                break;
        }
        return results;
    }

    private async Task<AnalyzeResult?> TryAnalyzePageChunkAsync(BinaryData binaryData, int startPage, CancellationToken cancellationToken)
    {
        var endPage = startPage + FreeTierPageChunkSize - 1;
        var pageRange = $"{startPage}-{endPage}";

        try
        {
            var options = new AnalyzeDocumentOptions(_settings.ModelId, binaryData) { Pages = pageRange };
            _logger.LogInformation("Sending additional Azure DI page chunk request. Pages: {Pages}", pageRange);
            var chunkOperation = await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
            var result = chunkOperation.Value;

            _logger.LogInformation("Azure DI chunk response received. Pages: {Pages}, ReturnedPages: {ReturnedPages}, Tables: {TableCount}",
                pageRange, result.Pages.Count, result.Tables.Count);

            return result.Pages.Count == 0 ? null : result;
        }
        catch (RequestFailedException ex) when (IsLikelyOutOfRange(ex))
        {
            _logger.LogInformation("Stopping chunked page scan at {Pages}. Status: {Status}, ErrorCode: {ErrorCode}",
                pageRange, ex.Status, ex.ErrorCode);
            return null;
        }
    }

    // ── Decision helpers ────────────────────────────────────────────────────

    private bool ShouldAttemptChunkedPageScan(AnalyzeResult result, int extractedMarkerCount, string? configuredPages)
    {
        if (!_settings.Enabled || !_settings.ChunkScanEnabled)
            return false;
        if (!string.IsNullOrWhiteSpace(configuredPages))
            return false;
        if (result.Pages.Count > FreeTierPageChunkSize)
            return false;

        return result.Tables.Count == 0 || extractedMarkerCount <= 5;
    }

    private static string? NormalizeConfiguredPages(string? pages)
    {
        if (string.IsNullOrWhiteSpace(pages))
            return null;

        return Regex.Replace(pages.Trim(), @"\s+", string.Empty);
    }

    private static bool IsLikelyOutOfRange(RequestFailedException ex)
    {
        if (ex.Status != 400)
            return false;

        var message = ex.Message.ToLowerInvariant();
        return message.Contains("page") && (message.Contains("range") || message.Contains("invalid"));
    }

    private static long? TryGetStreamLength(Stream stream)
    {
        try { return stream.CanSeek ? stream.Length : null; }
        catch { return null; }
    }

    // ── Result building ─────────────────────────────────────────────────────

    private static OcrExtractionResult FailResult(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
        RawPayload = "{}"
    };

    private static OcrExtractionResult BuildExtractionResult(List<AnalyzeResult> analysisResults, List<OcrMarkerResult> markers, HashSet<int>? selectedPages)
    {
        return new OcrExtractionResult
        {
            Success = markers.Count > 0,
            ErrorMessage = markers.Count > 0 ? null : "Document analyzed successfully but no biomarker-like values were detected.",
            RawPayload = JsonSerializer.Serialize(new
            {
                ModelId = analysisResults[0].ModelId,
                SelectedPages = selectedPages?.OrderBy(p => p).ToList(),
                Content = string.Join("\n", analysisResults
                    .SelectMany(r => r.Pages).OrderBy(p => p.PageNumber)
                    .SelectMany(p => p.Lines).Select(l => l.Content)),
                FilteredContent = BuildFilteredContent(analysisResults, selectedPages),
                Pages = analysisResults
                    .SelectMany(r => r.Pages)
                    .GroupBy(p => p.PageNumber).OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        PageNumber = g.Key,
                        Lines = g.SelectMany(p => p.Lines)
                            .Select(l => l.Content)
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Distinct().ToList()
                    }).ToList()
            }),
            Markers = markers
        };
    }

    // ── Diagnostics ──────────────────────────────────────────────────────────

    private void LogExtractionResults(AnalyzeResult result, List<AnalyzeResult> analysisResults, List<OcrMarkerResult> extractedMarkers)
    {
        _logger.LogInformation(
            "Azure DI response received. ResultModelId: {ResultModelId}, Pages: {PageCount}, Tables: {TableCount}, ExtractedMarkers: {MarkerCount}",
            result.ModelId, result.Pages.Count, result.Tables.Count, extractedMarkers.Count);
        _logger.LogInformation("Azure DI page summary: {PageSummary}", BuildPageSummary(result));
        _logger.LogInformation("Azure DI line preview: {LinePreview}", BuildLinePreview(result));

        if (analysisResults.Count > 1)
            _logger.LogInformation("Azure DI chunked page summary: {ChunkedPageSummary}", BuildCombinedPageSummary(analysisResults));

        if (extractedMarkers.Count > 0)
            _logger.LogInformation("OCR marker preview: {MarkerPreview}", BuildMarkerPreview(extractedMarkers));
        else
            _logger.LogWarning("No biomarker candidates extracted by parser. Check line/table preview for parser tuning.");
    }

    private static string BuildPageSummary(AnalyzeResult result)
    {
        if (result.Pages.Count == 0)
            return "no-pages";

        return string.Join(" | ", result.Pages.Select(p =>
            $"p{p.PageNumber}:lines={p.Lines.Count},words={p.Words.Count},selectionMarks={p.SelectionMarks.Count}"));
    }

    private static string BuildCombinedPageSummary(List<AnalyzeResult> results)
    {
        var allPages = results
            .SelectMany(r => r.Pages)
            .GroupBy(p => p.PageNumber).OrderBy(g => g.Key)
            .Select(g => g.First())
            .ToList();

        if (allPages.Count == 0)
            return "no-pages";

        return string.Join(" | ", allPages.Select(p =>
            $"p{p.PageNumber}:lines={p.Lines.Count},words={p.Words.Count},selectionMarks={p.SelectionMarks.Count}"));
    }

    private static string BuildFilteredContent(List<AnalyzeResult> results, HashSet<int>? selectedPages)
    {
        var pagesToUse = selectedPages is { Count: > 0 }
            ? results.SelectMany(r => r.Pages).Where(p => selectedPages.Contains(p.PageNumber))
            : results.SelectMany(r => r.Pages);

        return string.Join("\n", pagesToUse
            .OrderBy(p => p.PageNumber)
            .SelectMany(p => p.Lines).Select(l => l.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c)));
    }

    private static string BuildLinePreview(AnalyzeResult result, int maxPages = 4, int maxLinesPerPage = 8)
    {
        var builder = new StringBuilder();
        foreach (var page in result.Pages.OrderBy(p => p.PageNumber).Take(maxPages))
        {
            builder.Append($"[p{page.PageNumber}] ");
            var lines = page.Lines
                .Select(l => OcrMarkerParser.NormalizeWhitespace(l.Content))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Take(maxLinesPerPage).ToList();
            builder.Append(lines.Count == 0 ? "(no-lines)" : string.Join(" || ", lines));
            builder.Append(' ');
        }
        return builder.ToString().Trim();
    }

    private static string BuildMarkerPreview(List<OcrMarkerResult> markers, int maxMarkers = 12)
    {
        return string.Join(" | ", markers.Take(maxMarkers).Select(m =>
        {
            var refRange = m.ReferenceMinValue.HasValue || m.ReferenceMaxValue.HasValue
                ? $" [{m.ReferenceMinValue?.ToString(CultureInfo.InvariantCulture) ?? "?"}-{m.ReferenceMaxValue?.ToString(CultureInfo.InvariantCulture) ?? "?"}]"
                : string.Empty;
            var conf = m.Confidence.HasValue ? $" c={m.Confidence.Value:F2}" : string.Empty;
            return $"{m.Name}={m.ValueText}{(string.IsNullOrWhiteSpace(m.Unit) ? string.Empty : $" {m.Unit}")}{refRange}{conf}";
        }));
    }
}
