#nullable enable

using Azure.AI.DocumentIntelligence;

/// <summary>
/// Extracts and merges biomarker candidates from Azure DI analysis results.
/// Delegates parsing/validation to <see cref="OcrMarkerParser"/> and page selection to <see cref="OcrPageScorer"/>.
/// </summary>
public static class OcrMarkerExtractor
{
    // ── Public API ──────────────────────────────────────────────────────────

    public static List<OcrMarkerResult> ExtractMarkers(AnalyzeResult result)
    {
        var markers = new List<OcrMarkerResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var likelyLabPages = OcrPageScorer.GetLikelyLabPages(result);

        ExtractMarkersFromTables(result, markers, seen, likelyLabPages);
        ExtractMarkersFromMergedLines(result, markers, seen, likelyLabPages);
        ExtractMarkersFromLines(result, markers, seen, likelyLabPages);

        if (markers.Count > 0)
            return markers;

        var allPages = result.Pages.Select(p => p.PageNumber).ToHashSet();
        ExtractMarkersFromTables(result, markers, seen, allPages);
        ExtractMarkersFromMergedLines(result, markers, seen, allPages);
        ExtractMarkersFromLines(result, markers, seen, allPages);

        return markers;
    }

    public static List<OcrMarkerResult> ExtractMarkersFromSpecificPages(AnalyzeResult result, HashSet<int> allowedPages)
    {
        var markers = new List<OcrMarkerResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ExtractMarkersFromTables(result, markers, seen, allowedPages);
        ExtractMarkersFromMergedLines(result, markers, seen, allowedPages);
        ExtractMarkersFromLines(result, markers, seen, allowedPages);

        return markers;
    }

    public static List<OcrMarkerResult> MergeMarkersAcrossResults(List<AnalyzeResult> results)
    {
        var merged = new List<OcrMarkerResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allMarkers = results.SelectMany(r => ExtractMarkers(r));
        foreach (var marker in allMarkers)
        {
            if (!seen.Add(BuildMergeFingerprint(marker)))
                continue;
            merged.Add(marker);
        }

        return merged;
    }

    public static List<OcrMarkerResult> MergeMarkersAcrossResultsStrict(List<AnalyzeResult> results, HashSet<int> selectedPages)
    {
        var merged = new List<OcrMarkerResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allMarkers = results.SelectMany(r => ExtractMarkersFromSpecificPages(r, selectedPages));
        foreach (var marker in allMarkers)
        {
            if (!seen.Add(BuildMergeFingerprint(marker)))
                continue;
            merged.Add(marker);
        }

        return merged;
    }

    // ── Table extraction ────────────────────────────────────────────────────

    private static void ExtractMarkersFromTables(AnalyzeResult result, List<OcrMarkerResult> markers, HashSet<string> seen, HashSet<int> labPages)
    {
        foreach (var table in result.Tables)
        {
            if (!IsTableOnLabPage(table, labPages))
                continue;
            ExtractMarkersFromTableRows(result, table, markers, seen);
        }
    }

    private static void ExtractMarkersFromTableRows(AnalyzeResult result, DocumentTable table, List<OcrMarkerResult> markers, HashSet<string> seen)
    {
        var rows = table.Cells.GroupBy(c => c.RowIndex).OrderBy(g => g.Key);
        foreach (var row in rows)
        {
            var cells = row.OrderBy(c => c.ColumnIndex).ToList();
            if (cells.Count < 2)
                continue;
            TryExtractMarkerFromTableRow(result, cells, markers, seen);
        }
    }

    private static void TryExtractMarkerFromTableRow(AnalyzeResult result, List<DocumentTableCell> cells, List<OcrMarkerResult> markers, HashSet<string> seen)
    {
        var confidence = ComputeTableRowConfidence(result, cells);
        var rowContent = OcrMarkerParser.NormalizeWhitespace(string.Join(" ", cells.Select(c => c.Content)));

        if (OcrMarkerParser.TryParseMarkerLine(rowContent, out var parsed))
        {
            TryAddMarker(markers, seen, parsed.Name, parsed.ValueText, parsed.Unit, parsed.ReferenceMinValue, parsed.ReferenceMaxValue, confidence);
            return;
        }

        var (name, value, unit, refMin, refMax) = ParseTableRowCells(cells);
        TryAddMarker(markers, seen, name, value, unit, refMin, refMax, confidence);
    }

    private static (string? name, string? valueText, string? unit, decimal? refMin, decimal? refMax) ParseTableRowCells(IReadOnlyList<DocumentTableCell> cells)
    {
        var nameParts = new List<string>();
        string? valueText = null, unit = null;
        decimal? refMin = null, refMax = null;

        foreach (var cell in cells)
        {
            var content = OcrMarkerParser.NormalizeWhitespace(cell.Content);
            ClassifyTableCell(content, nameParts, ref valueText, ref unit, ref refMin, ref refMax);
        }

        return (OcrMarkerParser.NormalizeMarkerName(string.Join(" ", nameParts)), valueText, unit, refMin, refMax);
    }

    private static void ClassifyTableCell(string content, List<string> nameParts, ref string? valueText, ref string? unit, ref decimal? refMin, ref decimal? refMax)
    {
        if (string.IsNullOrWhiteSpace(content) || OcrMarkerParser.LooksLikeHeader(content))
            return;

        if (valueText is null && OcrMarkerParser.TryExtractValueFromCell(content, out var extracted))
        { valueText = extracted; return; }

        if (valueText is not null && unit is null && OcrMarkerParser.LooksLikeUnit(content))
        { unit = content; return; }

        if (valueText is not null && !refMin.HasValue && !refMax.HasValue && OcrMarkerParser.TryExtractReferenceRange(content, out refMin, out refMax))
            return;

        if (valueText is null)
            nameParts.Add(content);
    }

    private static bool IsTableOnLabPage(DocumentTable table, HashSet<int> labPages)
    {
        if (labPages.Count == 0)
            return true;

        var pageNumber = table.BoundingRegions.Count > 0 ? table.BoundingRegions[0].PageNumber : 0;
        return labPages.Contains(pageNumber);
    }

    // ── Line extraction ─────────────────────────────────────────────────────

    private static void ExtractMarkersFromLines(AnalyzeResult result, List<OcrMarkerResult> markers, HashSet<string> seen, HashSet<int> labPages)
    {
        foreach (var page in result.Pages)
        {
            if (labPages.Count > 0 && !labPages.Contains(page.PageNumber))
                continue;
            ExtractMarkersFromPageLines(page, markers, seen);
        }
    }

    private static void ExtractMarkersFromPageLines(DocumentPage page, List<OcrMarkerResult> markers, HashSet<string> seen)
    {
        foreach (var line in page.Lines)
        {
            var content = OcrMarkerParser.NormalizeWhitespace(line.Content);
            if (string.IsNullOrWhiteSpace(content) || OcrMarkerParser.LooksLikeHeader(content))
                continue;
            if (!OcrMarkerParser.TryParseMarkerLine(content, out var parsed))
                continue;

            var confidence = ComputeLineConfidence(page, line);
            TryAddMarker(markers, seen, parsed.Name, parsed.ValueText, parsed.Unit, parsed.ReferenceMinValue, parsed.ReferenceMaxValue, confidence);
        }
    }

    // ── Merged-line extraction ──────────────────────────────────────────────

    private static void ExtractMarkersFromMergedLines(AnalyzeResult result, List<OcrMarkerResult> markers, HashSet<string> seen, HashSet<int> allowedPages)
    {
        foreach (var page in result.Pages)
        {
            if (allowedPages.Count > 0 && !allowedPages.Contains(page.PageNumber))
                continue;
            ExtractMergedMarkersFromPage(page, markers, seen);
        }
    }

    private static void ExtractMergedMarkersFromPage(DocumentPage page, List<OcrMarkerResult> markers, HashSet<string> seen)
    {
        var entries = page.Lines
            .Select(l => (Line: l, Content: OcrMarkerParser.NormalizeWhitespace(l.Content)))
            .Where(e => !string.IsNullOrWhiteSpace(e.Content) && !OcrMarkerParser.LooksLikeHeader(e.Content))
            .ToList();

        for (var i = 0; i < entries.Count; i++)
        {
            TryMergePairAndExtract(page, entries, i, markers, seen);
            TryMergeTripleAndExtract(page, entries, i, markers, seen);
        }
    }

    private static void TryMergePairAndExtract(DocumentPage page, List<(DocumentLine Line, string Content)> entries, int index, List<OcrMarkerResult> markers, HashSet<string> seen)
    {
        if (index + 1 >= entries.Count)
            return;

        var merged = OcrMarkerParser.NormalizeWhitespace($"{entries[index].Content} {entries[index + 1].Content}");
        if (!OcrMarkerParser.TryParseMarkerLine(merged, out var parsed))
            return;

        var confidence = MinConfidence(
            ComputeLineConfidence(page, entries[index].Line),
            ComputeLineConfidence(page, entries[index + 1].Line));
        TryAddMarker(markers, seen, parsed.Name, parsed.ValueText, parsed.Unit, parsed.ReferenceMinValue, parsed.ReferenceMaxValue, confidence);
    }

    private static void TryMergeTripleAndExtract(DocumentPage page, List<(DocumentLine Line, string Content)> entries, int index, List<OcrMarkerResult> markers, HashSet<string> seen)
    {
        if (index + 2 >= entries.Count)
            return;

        var merged = OcrMarkerParser.NormalizeWhitespace($"{entries[index].Content} {entries[index + 1].Content} {entries[index + 2].Content}");
        if (!OcrMarkerParser.TryParseMarkerLine(merged, out var parsed))
            return;

        var confidence = MinConfidence(
            ComputeLineConfidence(page, entries[index].Line),
            MinConfidence(
                ComputeLineConfidence(page, entries[index + 1].Line),
                ComputeLineConfidence(page, entries[index + 2].Line)));
        TryAddMarker(markers, seen, parsed.Name, parsed.ValueText, parsed.Unit, parsed.ReferenceMinValue, parsed.ReferenceMaxValue, confidence);
    }

    // ── Marker dedup & enrichment ───────────────────────────────────────────

    private static void TryAddMarker(List<OcrMarkerResult> markers, HashSet<string> seen, string? name, string? valueText, string? unit, decimal? refMin = null, decimal? refMax = null, double? confidence = null)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(valueText))
            return;
        if (!OcrMarkerParser.IsLikelyMarkerName(name))
            return;
        if (!OcrMarkerParser.TryParseNumericToken(valueText, out var parsedValue))
            return;

        var normalizedUnit = NormalizeUnit(unit);
        var fingerprint = $"{name.Trim().ToLowerInvariant()}|{parsedValue}";

        if (!seen.Add(fingerprint))
        {
            EnrichExistingMarker(markers, fingerprint, normalizedUnit, refMin, refMax, confidence);
            return;
        }

        markers.Add(new OcrMarkerResult
        {
            Name = name.Trim(),
            Value = parsedValue,
            ValueText = valueText.Trim(),
            Unit = normalizedUnit,
            Confidence = confidence,
            ReferenceMinValue = refMin,
            ReferenceMaxValue = refMax
        });
    }

    private static void EnrichExistingMarker(List<OcrMarkerResult> markers, string fingerprint, string? unit, decimal? refMin, decimal? refMax, double? confidence)
    {
        var existing = markers.FirstOrDefault(m => $"{m.Name.Trim().ToLowerInvariant()}|{m.Value}" == fingerprint);
        if (existing is null)
            return;

        if (!string.IsNullOrWhiteSpace(unit) && string.IsNullOrWhiteSpace(existing.Unit))
            existing.Unit = unit;
        if (refMin.HasValue && !existing.ReferenceMinValue.HasValue)
            existing.ReferenceMinValue = refMin;
        if (refMax.HasValue && !existing.ReferenceMaxValue.HasValue)
            existing.ReferenceMaxValue = refMax;
        if (confidence.HasValue && !existing.Confidence.HasValue)
            existing.Confidence = confidence;
    }

    private static string? NormalizeUnit(string? unit)
    {
        var normalized = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        if (normalized is not null && normalized.Length == 1 && !normalized.Equals("%", StringComparison.Ordinal))
            return null;
        return normalized;
    }

    private static string BuildMergeFingerprint(OcrMarkerResult marker)
    {
        var unit = string.IsNullOrWhiteSpace(marker.Unit) ? null : marker.Unit.Trim();
        return $"{marker.Name.Trim()}|{marker.Value}|{unit}";
    }

    // ── Confidence computation ──────────────────────────────────────────────

    private static double? ComputeLineConfidence(DocumentPage page, DocumentLine line)
    {
        if (page.Words.Count == 0 || line.Spans.Count == 0)
            return null;

        var words = GetWordsInSpans(page.Words, line.Spans);
        return words.Count == 0 ? null : (double)words.Min(w => w.Confidence);
    }

    private static double? ComputeTableRowConfidence(AnalyzeResult result, IReadOnlyList<DocumentTableCell> cells)
    {
        float? min = null;
        foreach (var cell in cells)
        {
            var cellMin = GetCellMinConfidence(result, cell);
            if (cellMin.HasValue && (!min.HasValue || cellMin.Value < min.Value))
                min = cellMin;
        }
        return min.HasValue ? (double)min.Value : null;
    }

    private static float? GetCellMinConfidence(AnalyzeResult result, DocumentTableCell cell)
    {
        var pageNumber = cell.BoundingRegions.Count > 0 ? cell.BoundingRegions[0].PageNumber : 0;
        var page = result.Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
        if (page is null)
            return null;

        var words = GetWordsInSpans(page.Words, cell.Spans);
        return words.Count == 0 ? null : words.Min(w => w.Confidence);
    }

    private static List<DocumentWord> GetWordsInSpans(IReadOnlyList<DocumentWord> words, IReadOnlyList<DocumentSpan> spans)
    {
        return spans
            .SelectMany(span => words.Where(w =>
                w.Span.Offset >= span.Offset && w.Span.Offset + w.Span.Length <= span.Offset + span.Length))
            .ToList();
    }

    private static double? MinConfidence(double? a, double? b)
    {
        if (a.HasValue && b.HasValue)
            return Math.Min(a.Value, b.Value);
        return a ?? b;
    }
}
