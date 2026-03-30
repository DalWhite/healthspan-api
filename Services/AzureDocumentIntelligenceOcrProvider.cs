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

public class AzureDocumentIntelligenceOcrProvider : IOcrExtractionProvider
{
    private const int FreeTierPageChunkSize = 2;
    private const int MaxChunkedPageScan = 60;
    private static readonly Regex NumericTokenPattern = new(@"[<>]?[+-]?\d+(?:[\.,]\d+)?", RegexOptions.Compiled);
    private static readonly Regex ReferenceRangePattern = new(@"(?<min>\d+(?:[\.,]\d+)?)\s*-\s*(?<max>\d+(?:[\.,]\d+)?)", RegexOptions.Compiled);
    private static readonly Regex MarkerLinePattern = new(@"^(?<name>[\p{L}][\p{L}\p{N}\-\/%()\.,:&ºªµμ\s]{1,120}?)\s+\.??\s*(?<value>[<>]?[+-]?\d+(?:[\.,]\d+)?)\s*(?<tail>.*)$", RegexOptions.Compiled);
    private static readonly Regex MetricPrefixPattern = new(@"^(?<name>[\p{L}\p{N}][\p{L}\p{N}\-\/%()\.,:&ºªµμ\s]{1,140}?)\s+(?<rest>.+)$", RegexOptions.Compiled);
    private static readonly Regex PageCounterPattern = new(@"^\d+\s+de$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] LabSectionKeywords =
    {
        "analitica", "analítica", "hematologia", "hematología", "hemostasia", "bioquimica", "bioquímica", "orina"
    };
    private static readonly string[] BiomarkerHints =
    {
        "glucosa", "colesterol", "triglicer", "hemoglob", "hematoc", "hematies", "hematíes", "leucoc", "linfoc", "monoc",
        "eosin", "basof", "plaqueta", "creatin", "urea", "got", "gpt", "ggt", "ácido úrico", "acido urico", "ph", "densidad"
    };
    private static readonly string[] LabTableHeaderHints =
    {
        "parámetro", "parametro", "resultado", "valor", "unidad", "unidades", "referencia", "val.referencia", "rango"
    };
    private static readonly string[] RejectedNameFragments =
    {
        "fecha", "edición", "ref", "índice", "indice", "anamnesis", "exploración", "exploracion", "recomendaciones",
        "electrocardiograma", "preving", "cas training", "david alonso", "48687238x", "madrid", "basilica"
    };
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
            return new OcrExtractionResult
            {
                Success = false,
                ErrorMessage = "Azure Document Intelligence is not enabled.",
                RawPayload = "{}"
            };
        }

        if (_client is null)
        {
            _logger.LogError("OCR extraction failed to start because Document Intelligence client is not configured. Endpoint configured: {HasEndpoint}, API key configured: {HasApiKey}",
                !string.IsNullOrWhiteSpace(_settings.Endpoint),
                !string.IsNullOrWhiteSpace(_settings.ApiKey));
            return new OcrExtractionResult
            {
                Success = false,
                ErrorMessage = "Azure Document Intelligence is enabled but endpoint or API key is missing.",
                RawPayload = "{}"
            };
        }

        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var streamLength = TryGetStreamLength(stream);
            var configuredPages = NormalizeConfiguredPages(_settings.Pages);
            _logger.LogInformation(
                "Sending document to Azure DI. Endpoint: {Endpoint}, ModelId: {ModelId}, Pages: {Pages}, File: {FileName}, ContentType: {ContentType}, SizeBytes: {SizeBytes}",
                _settings.Endpoint,
                _settings.ModelId,
                configuredPages ?? "<all>",
                fileName,
                contentType,
                streamLength);

            var binaryData = await BinaryData.FromStreamAsync(stream, cancellationToken);
            var operation = await AnalyzeWithConfiguredPagesAsync(binaryData, configuredPages, cancellationToken);
            var result = operation.Value;

            var analysisResults = new List<AnalyzeResult> { result };
            var extractedMarkers = ExtractMarkers(result);
            HashSet<int>? selectedPages = null;

            if (ShouldAttemptChunkedPageScan(result, extractedMarkers.Count, configuredPages))
            {
                _logger.LogWarning(
                    "Possible page-limited analysis detected (Pages: {PageCount}, Tables: {TableCount}, Markers: {MarkerCount}). Attempting chunked page scan.",
                    result.Pages.Count,
                    result.Tables.Count,
                    extractedMarkers.Count);

                var additionalResults = await AnalyzeAdditionalPageChunksAsync(binaryData, cancellationToken);
                analysisResults.AddRange(additionalResults);

                if (additionalResults.Count > 0)
                {
                    extractedMarkers = MergeMarkersAcrossResults(analysisResults);
                    _logger.LogInformation(
                        "Chunked scan completed. Chunks: {ChunkCount}, CombinedMarkers: {MarkerCount}",
                        additionalResults.Count,
                        extractedMarkers.Count);
                }
            }

            if (string.IsNullOrWhiteSpace(configuredPages))
            {
                var pageScores = GetCombinedPageScores(analysisResults);
                selectedPages = SelectBestPagesFromCombinedResults(pageScores);
                if (selectedPages.Count > 0)
                {
                    extractedMarkers = MergeMarkersAcrossResultsStrict(analysisResults, selectedPages);
                    _logger.LogInformation(
                        "Auto strict page selection applied. SelectedPages: {SelectedPages}, MarkerCount: {MarkerCount}",
                        string.Join(",", selectedPages.OrderBy(page => page)),
                        extractedMarkers.Count);

                    _logger.LogInformation(
                        "Auto strict top page scores: {TopScores}",
                        string.Join(" | ", pageScores
                            .OrderByDescending(item => item.Value)
                            .ThenBy(item => item.Key)
                            .Take(12)
                            .Select(item => $"p{item.Key}={item.Value}")));
                }
            }

            _logger.LogInformation(
                "Azure DI response received. ResultModelId: {ResultModelId}, Pages: {PageCount}, Tables: {TableCount}, ExtractedMarkers: {MarkerCount}",
                result.ModelId,
                result.Pages.Count,
                result.Tables.Count,
                extractedMarkers.Count);

            _logger.LogInformation("Azure DI page summary: {PageSummary}", BuildPageSummary(result));
            _logger.LogInformation("Azure DI line preview: {LinePreview}", BuildLinePreview(result));
            if (analysisResults.Count > 1)
            {
                _logger.LogInformation("Azure DI chunked page summary: {ChunkedPageSummary}", BuildCombinedPageSummary(analysisResults));
            }

            if (extractedMarkers.Count > 0)
            {
                _logger.LogInformation("OCR marker preview: {MarkerPreview}", BuildMarkerPreview(extractedMarkers));
            }
            else
            {
                _logger.LogWarning("No biomarker candidates extracted by parser. Check line/table preview for parser tuning.");
            }

            return new OcrExtractionResult
            {
                Success = extractedMarkers.Count > 0,
                ErrorMessage = extractedMarkers.Count > 0 ? null : "Document analyzed successfully but no biomarker-like values were detected.",
                RawPayload = JsonSerializer.Serialize(new
                {
                    result.ModelId,
                    SelectedPages = selectedPages?.OrderBy(page => page).ToList(),
                    Content = string.Join("\n", analysisResults
                        .SelectMany(item => item.Pages)
                        .OrderBy(page => page.PageNumber)
                        .SelectMany(page => page.Lines)
                        .Select(line => line.Content)),
                    FilteredContent = BuildFilteredContent(analysisResults, selectedPages),
                    Pages = analysisResults
                        .SelectMany(item => item.Pages)
                        .GroupBy(page => page.PageNumber)
                        .OrderBy(group => group.Key)
                        .Select(group => new
                        {
                            PageNumber = group.Key,
                            Lines = group
                                .SelectMany(page => page.Lines)
                                .Select(line => line.Content)
                                .Where(content => !string.IsNullOrWhiteSpace(content))
                                .Distinct()
                                .ToList()
                        }).ToList()
                }),
                Markers = extractedMarkers
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure DI request failed. Status: {Status}, ErrorCode: {ErrorCode}, Message: {Message}", ex.Status, ex.ErrorCode, ex.Message);
            return new OcrExtractionResult
            {
                Success = false,
                ErrorMessage = $"Azure Document Intelligence request failed: {ex.Message}",
                RawPayload = "{}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected OCR extraction failure for file {FileName}.", fileName);
            return new OcrExtractionResult
            {
                Success = false,
                ErrorMessage = $"Unexpected OCR extraction failure: {ex.Message}",
                RawPayload = "{}"
            };
        }
    }

    private static long? TryGetStreamLength(Stream stream)
    {
        try
        {
            return stream.CanSeek ? stream.Length : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<Operation<AnalyzeResult>> AnalyzeWithConfiguredPagesAsync(BinaryData binaryData, string? configuredPages, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredPages))
        {
            return await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, _settings.ModelId, binaryData, cancellationToken: cancellationToken);
        }

        var options = new AnalyzeDocumentOptions(_settings.ModelId, binaryData)
        {
            Pages = configuredPages
        };

        return await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
    }

    private static string? NormalizeConfiguredPages(string? pages)
    {
        if (string.IsNullOrWhiteSpace(pages))
        {
            return null;
        }

        return Regex.Replace(pages.Trim(), @"\s+", string.Empty);
    }

    private bool ShouldAttemptChunkedPageScan(AnalyzeResult result, int extractedMarkerCount, string? configuredPages)
    {
        if (!_settings.Enabled || !_settings.ChunkScanEnabled)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(configuredPages))
        {
            return false;
        }

        if (result.Pages.Count > FreeTierPageChunkSize)
        {
            return false;
        }

        return result.Tables.Count == 0 || extractedMarkerCount <= 5;
    }

    private async Task<List<AnalyzeResult>> AnalyzeAdditionalPageChunksAsync(BinaryData binaryData, CancellationToken cancellationToken)
    {
        var results = new List<AnalyzeResult>();

        for (var startPage = FreeTierPageChunkSize + 1; startPage <= MaxChunkedPageScan; startPage += FreeTierPageChunkSize)
        {
            var endPage = startPage + FreeTierPageChunkSize - 1;
            var pageRange = $"{startPage}-{endPage}";

            try
            {
                var options = new AnalyzeDocumentOptions(_settings.ModelId, binaryData)
                {
                    Pages = pageRange
                };

                _logger.LogInformation("Sending additional Azure DI page chunk request. Pages: {Pages}", pageRange);
                var chunkOperation = await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
                var chunkResult = chunkOperation.Value;

                _logger.LogInformation(
                    "Azure DI chunk response received. Pages: {Pages}, ReturnedPages: {ReturnedPages}, Tables: {TableCount}",
                    pageRange,
                    chunkResult.Pages.Count,
                    chunkResult.Tables.Count);

                if (chunkResult.Pages.Count == 0)
                {
                    break;
                }

                results.Add(chunkResult);

                if (chunkResult.Pages.Count < FreeTierPageChunkSize)
                {
                    break;
                }
            }
            catch (RequestFailedException ex) when (IsLikelyOutOfRange(ex))
            {
                _logger.LogInformation(
                    "Stopping chunked page scan at {Pages}. Azure DI reported out-of-range or invalid page range. Status: {Status}, ErrorCode: {ErrorCode}",
                    pageRange,
                    ex.Status,
                    ex.ErrorCode);
                break;
            }
        }

        return results;
    }

    private static bool IsLikelyOutOfRange(RequestFailedException ex)
    {
        if (ex.Status != 400)
        {
            return false;
        }

        var message = ex.Message.ToLowerInvariant();
        return message.Contains("page") && (message.Contains("range") || message.Contains("invalid"));
    }

    private static List<OcrMarkerResult> MergeMarkersAcrossResults(List<AnalyzeResult> results)
    {
        var merged = new List<OcrMarkerResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var analyzeResult in results)
        {
            foreach (var marker in ExtractMarkers(analyzeResult))
            {
                var unit = string.IsNullOrWhiteSpace(marker.Unit) ? null : marker.Unit.Trim();
                var fingerprint = $"{marker.Name.Trim()}|{marker.Value}|{unit}";
                if (!seen.Add(fingerprint))
                {
                    continue;
                }

                merged.Add(marker);
            }
        }

        return merged;
    }

    private Dictionary<int, int> GetCombinedPageScores(List<AnalyzeResult> results)
    {
        var combinedScores = new Dictionary<int, int>();

        foreach (var analyzeResult in results)
        {
            var tableScores = GetTableScoresByPage(analyzeResult);
            foreach (var page in analyzeResult.Pages)
            {
                var score = tableScores.GetValueOrDefault(page.PageNumber);

                foreach (var line in page.Lines)
                {
                    var content = NormalizeWhitespace(line.Content).ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    if (LabSectionKeywords.Any(content.Contains))
                    {
                        score += 3;
                    }

                    if (BiomarkerHints.Any(content.Contains))
                    {
                        score += 2;
                    }

                    if (ReferenceRangePattern.IsMatch(content))
                    {
                        score += 1;
                    }

                    if (LooksLikeLabDataLine(content))
                    {
                        score += 1;
                    }
                }

                combinedScores[page.PageNumber] = combinedScores.GetValueOrDefault(page.PageNumber) + score;
            }
        }

        return combinedScores;
    }

    private HashSet<int> SelectBestPagesFromCombinedResults(Dictionary<int, int> pageScores)
    {
        var topPageCount = Math.Max(1, _settings.AutoTopPageCount);
        var minPageScore = Math.Max(0, _settings.AutoMinPageScore);

        return pageScores
            .Where(item => item.Value >= minPageScore)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key)
            .Take(topPageCount)
            .Select(item => item.Key)
            .ToHashSet();
    }

    private static List<OcrMarkerResult> ExtractMarkersFromSpecificPages(AnalyzeResult result, HashSet<int> allowedPages)
    {
        var markers = new List<OcrMarkerResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ExtractMarkersFromTables(result, markers, seen, allowedPages);
        ExtractMarkersFromMergedLines(result, markers, seen, allowedPages);
        ExtractMarkersFromLines(result, markers, seen, allowedPages);

        return markers;
    }

    private static List<OcrMarkerResult> MergeMarkersAcrossResultsStrict(List<AnalyzeResult> results, HashSet<int> selectedPages)
    {
        var merged = new List<OcrMarkerResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var analyzeResult in results)
        {
            foreach (var marker in ExtractMarkersFromSpecificPages(analyzeResult, selectedPages))
            {
                var unit = string.IsNullOrWhiteSpace(marker.Unit) ? null : marker.Unit.Trim();
                var fingerprint = $"{marker.Name.Trim()}|{marker.Value}|{unit}";
                if (!seen.Add(fingerprint))
                {
                    continue;
                }

                merged.Add(marker);
            }
        }

        return merged;
    }

    private static string BuildPageSummary(AnalyzeResult result)
    {
        if (result.Pages.Count == 0)
        {
            return "no-pages";
        }

        return string.Join(" | ", result.Pages.Select(page =>
            $"p{page.PageNumber}:lines={page.Lines.Count},words={page.Words.Count},selectionMarks={page.SelectionMarks.Count}"));
    }

    private static string BuildCombinedPageSummary(List<AnalyzeResult> results)
    {
        var allPages = results
            .SelectMany(item => item.Pages)
            .GroupBy(page => page.PageNumber)
            .OrderBy(group => group.Key)
            .Select(group => group.First())
            .ToList();

        if (allPages.Count == 0)
        {
            return "no-pages";
        }

        return string.Join(" | ", allPages.Select(page =>
            $"p{page.PageNumber}:lines={page.Lines.Count},words={page.Words.Count},selectionMarks={page.SelectionMarks.Count}"));
    }

    private static string BuildFilteredContent(List<AnalyzeResult> results, HashSet<int>? selectedPages)
    {
        var pagesToUse = selectedPages is { Count: > 0 }
            ? results.SelectMany(result => result.Pages).Where(page => selectedPages.Contains(page.PageNumber))
            : results.SelectMany(result => result.Pages);

        return string.Join("\n", pagesToUse
            .OrderBy(page => page.PageNumber)
            .SelectMany(page => page.Lines)
            .Select(line => line.Content)
            .Where(content => !string.IsNullOrWhiteSpace(content)));
    }

    private static string BuildLinePreview(AnalyzeResult result, int maxPages = 4, int maxLinesPerPage = 8)
    {
        var builder = new StringBuilder();
        foreach (var page in result.Pages.OrderBy(page => page.PageNumber).Take(maxPages))
        {
            builder.Append($"[p{page.PageNumber}] ");
            var lines = page.Lines
                .Select(line => NormalizeWhitespace(line.Content))
                .Where(content => !string.IsNullOrWhiteSpace(content))
                .Take(maxLinesPerPage)
                .ToList();

            builder.Append(lines.Count == 0 ? "(no-lines)" : string.Join(" || ", lines));
            builder.Append(" ");
        }

        return builder.ToString().Trim();
    }

    private static string BuildMarkerPreview(List<OcrMarkerResult> markers, int maxMarkers = 12)
    {
        return string.Join(" | ", markers
            .Take(maxMarkers)
            .Select(marker => $"{marker.Name}={marker.ValueText}{(string.IsNullOrWhiteSpace(marker.Unit) ? string.Empty : $" {marker.Unit}")}"));
    }

    private static List<OcrMarkerResult> ExtractMarkers(AnalyzeResult result)
    {
        var markers = new List<OcrMarkerResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var likelyLabPages = GetLikelyLabPages(result);

        ExtractMarkersFromTables(result, markers, seen, likelyLabPages);
        ExtractMarkersFromLines(result, markers, seen, likelyLabPages);

        if (markers.Count == 0)
        {
            var allPages = result.Pages.Select(page => page.PageNumber).ToHashSet();
            ExtractMarkersFromTables(result, markers, seen, allPages);
            ExtractMarkersFromMergedLines(result, markers, seen, allPages);
            ExtractMarkersFromLines(result, markers, seen, allPages);
        }

        return markers;
    }

    private static HashSet<int> GetLikelyLabPages(AnalyzeResult result)
    {
        var pageScores = new Dictionary<int, int>();
        var tableScores = GetTableScoresByPage(result);

        foreach (var page in result.Pages)
        {
            var score = 0;
            foreach (var line in page.Lines)
            {
                var content = NormalizeWhitespace(line.Content).ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                if (LabSectionKeywords.Any(content.Contains))
                {
                    score += 3;
                }

                if (BiomarkerHints.Any(content.Contains))
                {
                    score += 2;
                }

                if (ReferenceRangePattern.IsMatch(content))
                {
                    score += 1;
                }

                if (LooksLikeLabDataLine(content))
                {
                    score += 1;
                }
            }

            if (tableScores.TryGetValue(page.PageNumber, out var tableScore))
            {
                score += tableScore;
            }

            if (score > 0)
            {
                pageScores[page.PageNumber] = score;
            }
        }

        var likelyPages = pageScores
            .Where(item => item.Value >= 3)
            .Select(item => item.Key)
            .ToHashSet();

        foreach (var page in result.Pages)
        {
            if (likelyPages.Contains(page.PageNumber))
            {
                continue;
            }

            var candidateLineCount = page.Lines
                .Select(line => NormalizeWhitespace(line.Content))
                .Count(content => ReferenceRangePattern.IsMatch(content)
                    || BiomarkerHints.Any(hint => content.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    || LooksLikeLabDataLine(content));

            if (candidateLineCount >= 3 || tableScores.GetValueOrDefault(page.PageNumber) >= 3)
            {
                likelyPages.Add(page.PageNumber);
            }
        }

        foreach (var pageNumber in likelyPages.ToList())
        {
            if (pageScores.TryGetValue(pageNumber - 1, out var previousScore) && previousScore >= 2)
            {
                likelyPages.Add(pageNumber - 1);
            }

            if (pageScores.TryGetValue(pageNumber + 1, out var nextScore) && nextScore >= 2)
            {
                likelyPages.Add(pageNumber + 1);
            }
        }

        return likelyPages;
    }

    private static Dictionary<int, int> GetTableScoresByPage(AnalyzeResult result)
    {
        var scores = new Dictionary<int, int>();

        foreach (var table in result.Tables)
        {
            var pageNumber = table.BoundingRegions.Count > 0 ? table.BoundingRegions[0].PageNumber : 0;
            if (pageNumber <= 0)
            {
                continue;
            }

            var tableScore = 0;
            var sampleCells = table.Cells
                .Select(cell => NormalizeWhitespace(cell.Content).ToLowerInvariant())
                .Where(content => !string.IsNullOrWhiteSpace(content))
                .ToList();

            if (sampleCells.Any(content => LabTableHeaderHints.Any(content.Contains)))
            {
                tableScore += 2;
            }

            tableScore += Math.Min(4, sampleCells.Count(content => ReferenceRangePattern.IsMatch(content)));
            tableScore += Math.Min(4, sampleCells.Count(content => TryExtractValueFromCell(content, out _)) / 3);

            if (table.ColumnCount >= 3)
            {
                tableScore += 1;
            }

            scores[pageNumber] = scores.GetValueOrDefault(pageNumber) + tableScore;
        }

        return scores;
    }

    private static bool LooksLikeLabDataLine(string content)
    {
        var normalized = NormalizeWhitespace(content);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var numericCount = NumericTokenPattern.Matches(normalized).Count;
        return numericCount >= 2 && normalized.Any(char.IsLetter);
    }

    private static void ExtractMarkersFromTables(AnalyzeResult result, List<OcrMarkerResult> markers, HashSet<string> seen, HashSet<int> likelyLabPages)
    {
        foreach (var table in result.Tables)
        {
            int? tablePageNumber = table.BoundingRegions.Count > 0 ? table.BoundingRegions[0].PageNumber : null;
            if (tablePageNumber.HasValue && likelyLabPages.Count > 0 && !likelyLabPages.Contains(tablePageNumber.Value))
            {
                continue;
            }

            var rows = table.Cells
                .GroupBy(cell => cell.RowIndex)
                .OrderBy(group => group.Key);

            foreach (var row in rows)
            {
                var orderedCells = row.OrderBy(cell => cell.ColumnIndex).ToList();
                if (orderedCells.Count < 2)
                {
                    continue;
                }

                var rowContent = NormalizeWhitespace(string.Join(" ", orderedCells.Select(cell => cell.Content)));
                if (TryParseMarkerLine(rowContent, out var mergedParsedLine))
                {
                    TryAddMarker(markers, seen, mergedParsedLine.Name, mergedParsedLine.ValueText, mergedParsedLine.Unit);
                    continue;
                }

                var nameParts = new List<string>();
                string? valueText = null;
                string? unit = null;

                foreach (var cell in orderedCells)
                {
                    var content = NormalizeWhitespace(cell.Content);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    if (LooksLikeHeader(content))
                    {
                        continue;
                    }

                    if (valueText is null && TryExtractValueFromCell(content, out var extractedValueText))
                    {
                        valueText = extractedValueText;
                        continue;
                    }

                    if (valueText is not null && unit is null && LooksLikeUnit(content))
                    {
                        unit = content;
                        break;
                    }

                    if (valueText is null)
                    {
                        nameParts.Add(content);
                    }
                }

                var name = NormalizeMarkerName(string.Join(" ", nameParts));
                TryAddMarker(markers, seen, name, valueText, unit);
            }
        }
    }

    private static void ExtractMarkersFromLines(AnalyzeResult result, List<OcrMarkerResult> markers, HashSet<string> seen, HashSet<int> likelyLabPages)
    {
        foreach (var page in result.Pages)
        {
            if (likelyLabPages.Count > 0 && !likelyLabPages.Contains(page.PageNumber))
            {
                continue;
            }

            foreach (var line in page.Lines)
            {
                var content = NormalizeWhitespace(line.Content);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                if (LooksLikeHeader(content) || !TryParseMarkerLine(content, out var parsedLine))
                {
                    continue;
                }

                TryAddMarker(markers, seen, parsedLine.Name, parsedLine.ValueText, parsedLine.Unit);
            }
        }
    }

    private static void ExtractMarkersFromMergedLines(AnalyzeResult result, List<OcrMarkerResult> markers, HashSet<string> seen, HashSet<int> allowedPages)
    {
        foreach (var page in result.Pages)
        {
            if (allowedPages.Count > 0 && !allowedPages.Contains(page.PageNumber))
            {
                continue;
            }

            var lines = page.Lines
                .Select(line => NormalizeWhitespace(line.Content))
                .Where(content => !string.IsNullOrWhiteSpace(content) && !LooksLikeHeader(content))
                .ToList();

            for (var index = 0; index < lines.Count; index++)
            {
                if (index + 1 < lines.Count)
                {
                    var mergedPair = NormalizeWhitespace($"{lines[index]} {lines[index + 1]}");
                    if (TryParseMarkerLine(mergedPair, out var pairParsedLine))
                    {
                        TryAddMarker(markers, seen, pairParsedLine.Name, pairParsedLine.ValueText, pairParsedLine.Unit);
                    }
                }

                if (index + 2 < lines.Count)
                {
                    var mergedTriple = NormalizeWhitespace($"{lines[index]} {lines[index + 1]} {lines[index + 2]}");
                    if (TryParseMarkerLine(mergedTriple, out var tripleParsedLine))
                    {
                        TryAddMarker(markers, seen, tripleParsedLine.Name, tripleParsedLine.ValueText, tripleParsedLine.Unit);
                    }
                }
            }
        }
    }

    private static bool TryParseMarkerLine(string content, out ParsedMarkerLine parsedLine)
    {
        parsedLine = default;

        if (TryParseMarkerLineWithPrimaryPattern(content, out parsedLine))
        {
            return true;
        }

        if (TryParseMarkerLineWithReferenceRange(content, out parsedLine))
        {
            return true;
        }

        return TryParseMarkerLineWithTokenFallback(content, out parsedLine);
    }

    private static bool TryParseMarkerLineWithPrimaryPattern(string content, out ParsedMarkerLine parsedLine)
    {
        parsedLine = default;

        var match = MarkerLinePattern.Match(content);
        if (!match.Success)
        {
            return false;
        }

        var name = NormalizeMarkerName(match.Groups["name"].Value);
        if (string.IsNullOrWhiteSpace(name) || LooksLikeHeader(name))
        {
            return false;
        }

        var valueText = match.Groups["value"].Value.Trim();
        if (!TryParseNumericToken(valueText, out _))
        {
            return false;
        }

        var tail = NormalizeWhitespace(match.Groups["tail"].Value);
        var unit = ExtractUnitFromTail(tail);
        if (LooksLikeNarrativeOrCompositeLine(name, tail))
        {
            return false;
        }

        parsedLine = new ParsedMarkerLine
        {
            Name = name,
            ValueText = valueText,
            Unit = unit
        };

        return true;
    }

    private static bool TryParseMarkerLineWithReferenceRange(string content, out ParsedMarkerLine parsedLine)
    {
        parsedLine = default;

        var referenceMatch = ReferenceRangePattern.Match(content);
        if (!referenceMatch.Success)
        {
            return false;
        }

        var beforeRange = NormalizeWhitespace(content[..referenceMatch.Index]);
        if (string.IsNullOrWhiteSpace(beforeRange))
        {
            return false;
        }

        var numericMatches = NumericTokenPattern.Matches(beforeRange);
        if (numericMatches.Count == 0)
        {
            return false;
        }

        var valueMatch = numericMatches[^1];
        var name = NormalizeMarkerName(beforeRange[..valueMatch.Index]);
        if (string.IsNullOrWhiteSpace(name) || LooksLikeHeader(name))
        {
            return false;
        }

        var valueText = valueMatch.Value.Trim();
        var tail = NormalizeWhitespace(beforeRange[(valueMatch.Index + valueMatch.Length)..]);
        var unit = ExtractUnitFromTail(tail);
        if (LooksLikeNarrativeOrCompositeLine(name, tail))
        {
            return false;
        }

        parsedLine = new ParsedMarkerLine
        {
            Name = name,
            ValueText = valueText,
            Unit = unit
        };

        return true;
    }

    private static bool TryParseMarkerLineWithTokenFallback(string content, out ParsedMarkerLine parsedLine)
    {
        parsedLine = default;

        var prefixMatch = MetricPrefixPattern.Match(content);
        if (!prefixMatch.Success)
        {
            return false;
        }

        var name = NormalizeMarkerName(prefixMatch.Groups["name"].Value);
        if (string.IsNullOrWhiteSpace(name) || LooksLikeHeader(name))
        {
            return false;
        }

        var rest = prefixMatch.Groups["rest"].Value;
        var numericMatches = NumericTokenPattern.Matches(rest);
        if (numericMatches.Count == 0)
        {
            return false;
        }

        var valueMatch = numericMatches[0];
        var valueText = valueMatch.Value;
        var tail = NormalizeWhitespace(rest[(valueMatch.Index + valueMatch.Length)..]);
        var unit = ExtractUnitFromTail(tail);
        if (LooksLikeNarrativeOrCompositeLine(name, tail))
        {
            return false;
        }

        parsedLine = new ParsedMarkerLine
        {
            Name = name,
            ValueText = valueText,
            Unit = unit
        };

        return true;
    }

    private static void TryAddMarker(List<OcrMarkerResult> markers, HashSet<string> seen, string? name, string? valueText, string? unit)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(valueText))
        {
            return;
        }

        if (!IsLikelyMarkerName(name))
        {
            return;
        }

        if (!TryParseNumericToken(valueText, out var parsedValue))
        {
            return;
        }

        var normalizedUnit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        if (normalizedUnit is not null && normalizedUnit.Length == 1 && !normalizedUnit.Equals("%", StringComparison.Ordinal))
        {
            normalizedUnit = null;
        }

        var fingerprint = $"{name.Trim().ToLowerInvariant()}|{parsedValue}";
        if (!seen.Add(fingerprint))
        {
            if (!string.IsNullOrWhiteSpace(normalizedUnit))
            {
                var existing = markers.FirstOrDefault(m => $"{m.Name.Trim().ToLowerInvariant()}|{m.Value}" == fingerprint);
                if (existing is not null && string.IsNullOrWhiteSpace(existing.Unit))
                {
                    existing.Unit = normalizedUnit;
                }
            }
            return;
        }

        markers.Add(new OcrMarkerResult
        {
            Name = name.Trim(),
            Value = parsedValue,
            ValueText = valueText.Trim(),
            Unit = normalizedUnit,
            Confidence = null
        });
    }

    private static bool TryParseNumericToken(string valueText, out decimal parsedValue)
    {
        parsedValue = 0;
        if (string.IsNullOrWhiteSpace(valueText))
        {
            return false;
        }

        var normalized = valueText.Trim();
        normalized = normalized.Trim('<', '>', '=', '*');
        normalized = normalized.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out parsedValue);
    }

    private static bool TryExtractValueFromCell(string content, out string? valueText)
    {
        valueText = null;

        if (TryParseNumericToken(content, out _))
        {
            valueText = content;
            return true;
        }

        var numericMatch = NumericTokenPattern.Match(content);
        if (!numericMatch.Success)
        {
            return false;
        }

        valueText = numericMatch.Value;
        return true;
    }

    private static string NormalizeMarkerName(string value)
    {
        return NormalizeWhitespace(value)
            .Trim(':', '-', '.', '*')
            .Trim();
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static string? ExtractUnitFromTail(string? tail)
    {
        if (string.IsNullOrWhiteSpace(tail))
        {
            return null;
        }

        var workingTail = tail;
        var referenceMatch = ReferenceRangePattern.Match(workingTail);
        if (referenceMatch.Success)
        {
            workingTail = workingTail[..referenceMatch.Index].Trim();
        }

        if (string.IsNullOrWhiteSpace(workingTail))
        {
            return null;
        }

        var firstNumericMatch = NumericTokenPattern.Match(workingTail);
        if (firstNumericMatch.Success)
        {
            workingTail = workingTail[..firstNumericMatch.Index].Trim();
        }

        if (string.IsNullOrWhiteSpace(workingTail))
        {
            return null;
        }

        var tokens = workingTail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var unitTokens = new List<string>();
        foreach (var token in tokens)
        {
            if (!LooksLikeUnit(token))
            {
                break;
            }

            unitTokens.Add(token);
        }

        return unitTokens.Count == 0 ? null : string.Join(" ", unitTokens);
    }

    private static string? InferUnitFromTail(string? tail)
    {
        if (string.IsNullOrWhiteSpace(tail))
        {
            return null;
        }

        var firstToken = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return LooksLikeUnit(firstToken) ? firstToken : null;
    }

    private static bool LooksLikeUnit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(value.Trim(), @"^[A-Za-zµμ/%0-9^.,&³²_-]{1,25}$");
    }

    private static bool LooksLikeHeader(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "test" or "marker" or "analyte" or "value" or "result" or "units" or "unit" or "reference" or "range"
            or "parámetro" or "resultado" or "unidades" or "val.referencia" or "hematología y hemostasia" or "bioquímica" or "análisis de orina"
            or "analítica" or "6.analitica" or "6.analítica";
    }

    private static bool LooksLikeNarrativeOrCompositeLine(string name, string? tail)
    {
        if (name.Length < 2 || !name.Any(char.IsLetter))
        {
            return true;
        }

        var normalizedName = name.ToLowerInvariant();
        if (normalizedName.Contains("sedimento en orina") || normalizedName.Contains("observaciones") || normalizedName.Contains("resultado de los parámetros"))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(tail))
        {
            return false;
        }

        var normalizedTail = tail.ToLowerInvariant();
        if (normalizedTail.Contains(":") && !ReferenceRangePattern.IsMatch(normalizedTail))
        {
            return true;
        }

        return normalizedTail.Contains("/ campo")
            || normalizedTail.Contains("campo")
            || normalizedTail.Contains("edición:")
            || normalizedTail.Contains("ref. :");
    }

    private static bool IsLikelyMarkerName(string name)
    {
        var normalized = NormalizeMarkerName(name).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 2)
        {
            return false;
        }

        if (PageCounterPattern.IsMatch(normalized))
        {
            return false;
        }

        if (RejectedNameFragments.Any(normalized.Contains))
        {
            return false;
        }

        if (normalized.Count(char.IsLetter) < 2)
        {
            return false;
        }

        return true;
    }

    private readonly record struct ParsedMarkerLine(string Name, string ValueText, string? Unit);
}