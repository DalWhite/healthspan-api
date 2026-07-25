using Azure.AI.DocumentIntelligence;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// Unit tests for OcrMarkerExtractor — marker extraction/merging logic
/// from Azure DI analysis results including tables, lines, and merged lines.
/// </summary>
public class OcrMarkerExtractorTests
{
    // ── Helper builders ─────────────────────────────────────────────────────

    private static DocumentSpan Span(int offset, int length) =>
        DocumentIntelligenceModelFactory.DocumentSpan(offset, length);

    private static DocumentWord Word(string content, int offset, float confidence = 0.95f) =>
        DocumentIntelligenceModelFactory.DocumentWord(content, Array.Empty<float>(), Span(offset, content.Length), confidence);

    private static DocumentLine Line(string content, int offset) =>
        DocumentIntelligenceModelFactory.DocumentLine(content, Array.Empty<float>(), new[] { Span(offset, content.Length) });

    private static DocumentPage BuildPage(int pageNumber, IEnumerable<string> lineContents)
    {
        var lines = new List<DocumentLine>();
        var words = new List<DocumentWord>();
        var offset = 0;

        foreach (var text in lineContents)
        {
            lines.Add(Line(text, offset));
            foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add(Word(w, offset));
                offset += w.Length + 1;
            }
            offset += 1;
        }

        return DocumentIntelligenceModelFactory.DocumentPage(
            pageNumber: pageNumber, angle: 0, width: 612, height: 792, unit: null,
            spans: new[] { Span(0, offset) }, words: words,
            selectionMarks: Array.Empty<DocumentSelectionMark>(),
            lines: lines,
            barcodes: Array.Empty<DocumentBarcode>(),
            formulas: Array.Empty<DocumentFormula>());
    }

    private static DocumentTableCell Cell(int row, int col, string content, int pageNumber, int spanOffset = 0) =>
        DocumentIntelligenceModelFactory.DocumentTableCell(
            kind: null, rowIndex: row, columnIndex: col, rowSpan: 1, columnSpan: 1,
            content: content,
            boundingRegions: new[] { DocumentIntelligenceModelFactory.BoundingRegion(pageNumber, Array.Empty<float>()) },
            spans: new[] { Span(spanOffset, content.Length) },
            elements: Array.Empty<string>());

    private static DocumentTable BuildTable(int pageNumber, List<DocumentTableCell> cells, int rowCount, int columnCount) =>
        DocumentIntelligenceModelFactory.DocumentTable(
            rowCount: rowCount, columnCount: columnCount, cells: cells,
            boundingRegions: new[] { DocumentIntelligenceModelFactory.BoundingRegion(pageNumber, Array.Empty<float>()) },
            spans: new[] { Span(0, 200) },
            caption: null, footnotes: Array.Empty<DocumentFootnote>());

    private static AnalyzeResult BuildAnalyzeResult(IEnumerable<DocumentPage> pages, IEnumerable<DocumentTable>? tables = null) =>
        DocumentIntelligenceModelFactory.AnalyzeResult(
            apiVersion: "2024-02-29-preview", modelId: "prebuilt-layout",
            contentFormat: null, content: "",
            pages: pages,
            paragraphs: Array.Empty<DocumentParagraph>(),
            tables: tables ?? Array.Empty<DocumentTable>(),
            figures: Array.Empty<DocumentFigure>(),
            sections: Array.Empty<DocumentSection>(),
            keyValuePairs: Array.Empty<DocumentKeyValuePair>(),
            styles: Array.Empty<DocumentStyle>(),
            languages: Array.Empty<DocumentLanguage>(),
            documents: Array.Empty<AnalyzedDocument>(),
            warnings: Array.Empty<DocumentIntelligenceWarning>());

    // ── ExtractMarkers from lines ───────────────────────────────────────────

    [Fact]
    public void ExtractMarkers_From_Lines_Parses_Standard_Lab_Lines()
    {
        var page = BuildPage(1, new[]
        {
            "Glucosa 95 mg/dL 70-110",
            "Colesterol Total 210 mg/dL 0-200",
            "Hemoglobina 14.5 g/dL 12.0-17.0"
        });
        var result = BuildAnalyzeResult(new[] { page });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        markers.Should().HaveCountGreaterThanOrEqualTo(3);
        markers.Should().Contain(m => m.Name == "Glucosa" && m.Value == 95);
        markers.Should().Contain(m => m.Name == "Colesterol Total" && m.Value == 210);
        markers.Should().Contain(m => m.Name == "Hemoglobina" && m.Value == 14.5m);
    }

    [Fact]
    public void ExtractMarkers_Preserves_Reference_Ranges()
    {
        var page = BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110" });
        var result = BuildAnalyzeResult(new[] { page });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        var glucosa = markers.First(m => m.Name == "Glucosa");
        glucosa.ReferenceMinValue.Should().Be(70);
        glucosa.ReferenceMaxValue.Should().Be(110);
    }

    [Fact]
    public void ExtractMarkers_Preserves_Units()
    {
        var page = BuildPage(1, new[] { "Hemoglobina 14.5 g/dL 12.0-17.0" });
        var result = BuildAnalyzeResult(new[] { page });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        markers.First(m => m.Name == "Hemoglobina").Unit.Should().Be("g/dL");
    }

    [Fact]
    public void ExtractMarkers_Returns_Empty_For_Non_Lab_Content()
    {
        var page = BuildPage(1, new[] { "Empresa Médica S.L.", "Dirección fiscal registrada", "CIF empresa asociada" });
        var result = BuildAnalyzeResult(new[] { page });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        markers.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMarkers_Ignores_Header_Lines()
    {
        var page = BuildPage(1, new[]
        {
            "resultado",
            "unidades",
            "Glucosa 95 mg/dL 70-110"
        });
        var result = BuildAnalyzeResult(new[] { page });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        markers.Should().OnlyContain(m => m.Name != "resultado" && m.Name != "unidades");
    }

    // ── ExtractMarkers from tables ──────────────────────────────────────────

    [Fact]
    public void ExtractMarkers_From_Table_Rows()
    {
        var cells = new List<DocumentTableCell>
        {
            Cell(0, 0, "Parámetro", 1), Cell(0, 1, "Resultado", 1), Cell(0, 2, "Unidades", 1), Cell(0, 3, "Referencia", 1),
            Cell(1, 0, "Glucosa", 1, 50), Cell(1, 1, "95", 1, 60), Cell(1, 2, "mg/dL", 1, 65), Cell(1, 3, "70-110", 1, 72),
            Cell(2, 0, "Colesterol", 1, 80), Cell(2, 1, "210", 1, 92), Cell(2, 2, "mg/dL", 1, 97), Cell(2, 3, "0-200", 1, 104),
        };

        var table = BuildTable(1, cells, rowCount: 3, columnCount: 4);
        var page = BuildPage(1, new[] { "Analítica completa" });
        var result = BuildAnalyzeResult(new[] { page }, new[] { table });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        markers.Should().Contain(m => m.Name == "Glucosa");
        markers.Should().Contain(m => m.Name == "Colesterol");
    }

    // ── Deduplication ───────────────────────────────────────────────────────

    [Fact]
    public void ExtractMarkers_Deduplicates_Same_Marker()
    {
        var page = BuildPage(1, new[]
        {
            "Glucosa 95 mg/dL 70-110",
            "Glucosa 95 mg/dL 70-110"
        });
        var result = BuildAnalyzeResult(new[] { page });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        markers.Where(m => m.Name == "Glucosa").Should().HaveCount(1);
    }

    // ── ExtractMarkersFromSpecificPages ──────────────────────────────────────

    [Fact]
    public void ExtractMarkersFromSpecificPages_Only_Extracts_From_Allowed_Pages()
    {
        var page1 = BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110" });
        var page2 = BuildPage(2, new[] { "Colesterol 210 mg/dL 0-200" });
        var result = BuildAnalyzeResult(new[] { page1, page2 });

        var markers = OcrMarkerExtractor.ExtractMarkersFromSpecificPages(result, new HashSet<int> { 1 });

        markers.Should().Contain(m => m.Name == "Glucosa");
        markers.Should().NotContain(m => m.Name == "Colesterol");
    }

    [Fact]
    public void ExtractMarkersFromSpecificPages_Returns_Empty_When_No_Markers_On_Allowed_Pages()
    {
        var page1 = BuildPage(1, new[] { "Non-marker content here" });
        var page2 = BuildPage(2, new[] { "Glucosa 95 mg/dL 70-110" });
        var result = BuildAnalyzeResult(new[] { page1, page2 });

        var markers = OcrMarkerExtractor.ExtractMarkersFromSpecificPages(result, new HashSet<int> { 1 });

        markers.Should().BeEmpty();
    }

    // ── MergeMarkersAcrossResults ───────────────────────────────────────────

    [Fact]
    public void MergeMarkersAcrossResults_Combines_Markers_From_Multiple_Results()
    {
        var result1 = BuildAnalyzeResult(new[] {
            BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110" })
        });
        var result2 = BuildAnalyzeResult(new[] {
            BuildPage(2, new[] { "Colesterol 210 mg/dL 0-200" })
        });

        var merged = OcrMarkerExtractor.MergeMarkersAcrossResults(new List<AnalyzeResult> { result1, result2 });

        merged.Should().Contain(m => m.Name == "Glucosa");
        merged.Should().Contain(m => m.Name == "Colesterol");
    }

    [Fact]
    public void MergeMarkersAcrossResults_Deduplicates_Across_Results()
    {
        var result1 = BuildAnalyzeResult(new[] {
            BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110" })
        });
        var result2 = BuildAnalyzeResult(new[] {
            BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110" })
        });

        var merged = OcrMarkerExtractor.MergeMarkersAcrossResults(new List<AnalyzeResult> { result1, result2 });

        merged.Where(m => m.Name == "Glucosa").Should().HaveCount(1);
    }

    // ── MergeMarkersAcrossResultsStrict ─────────────────────────────────────

    [Fact]
    public void MergeMarkersAcrossResultsStrict_Only_From_Selected_Pages()
    {
        var result1 = BuildAnalyzeResult(new[] {
            BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110" }),
            BuildPage(2, new[] { "Colesterol 210 mg/dL 0-200" })
        });

        var merged = OcrMarkerExtractor.MergeMarkersAcrossResultsStrict(
            new List<AnalyzeResult> { result1 },
            new HashSet<int> { 1 });

        merged.Should().Contain(m => m.Name == "Glucosa");
        merged.Should().NotContain(m => m.Name == "Colesterol");
    }

    // ── Confidence values ───────────────────────────────────────────────────

    [Fact]
    public void ExtractMarkers_Includes_Confidence_From_Words()
    {
        var page = BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110" });
        var result = BuildAnalyzeResult(new[] { page });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        var glucosa = markers.FirstOrDefault(m => m.Name == "Glucosa");
        glucosa.Should().NotBeNull();
        // Confidence should be populated from word-level confidence
        glucosa!.Confidence.Should().NotBeNull();
    }

    // ── Multi-page extraction ───────────────────────────────────────────────

    [Fact]
    public void ExtractMarkers_Across_Multiple_Pages()
    {
        var page1 = BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110", "Colesterol 210 mg/dL 0-200" });
        var page2 = BuildPage(2, new[] { "Hemoglobina 14.5 g/dL 12.0-17.0", "Leucocitos 7.2 miles/uL 4.0-11.0" });
        var result = BuildAnalyzeResult(new[] { page1, page2 });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        markers.Should().HaveCountGreaterThanOrEqualTo(4);
    }

    // ── Fallback extraction ─────────────────────────────────────────────────

    [Fact]
    public void ExtractMarkers_Falls_Back_To_All_Pages_When_No_Lab_Pages_Found()
    {
        // Build a page that doesn't score as a "lab page" but has valid markers
        var page = BuildPage(1, new[] { "Creatinina 1.1 mg/dL 0.6-1.2" });
        var result = BuildAnalyzeResult(new[] { page });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        // Should still find the marker via fallback to all pages
        markers.Should().Contain(m => m.Name.Contains("Creatinina"));
    }

    // ── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public void ExtractMarkers_Handles_Empty_AnalyzeResult()
    {
        var result = BuildAnalyzeResult(Array.Empty<DocumentPage>());

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        markers.Should().BeEmpty();
    }

    [Fact]
    public void MergeMarkersAcrossResults_Handles_Empty_List()
    {
        var merged = OcrMarkerExtractor.MergeMarkersAcrossResults(new List<AnalyzeResult>());

        merged.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMarkersFromSpecificPages_Handles_Empty_Allowed_Set()
    {
        var page = BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110" });
        var result = BuildAnalyzeResult(new[] { page });

        // Empty set means all pages are allowed
        var markers = OcrMarkerExtractor.ExtractMarkersFromSpecificPages(result, new HashSet<int>());

        markers.Should().Contain(m => m.Name == "Glucosa");
    }

    // ── Merged-line extraction on primary path ──────────────────────────────

    [Fact]
    public void ExtractMarkers_Finds_Merged_Line_Markers_On_Lab_Pages()
    {
        // A lab page with a single-line marker and a split marker (name on one line, value on next).
        // The primary path should find both via merged-line extraction.
        var page = BuildPage(1, new[]
        {
            "Bioquímica general",
            "Glucosa 95 mg/dL 70-110",
            "Colesterol Total",
            "210 mg/dL 0-200"
        });
        var result = BuildAnalyzeResult(new[] { page });

        var markers = OcrMarkerExtractor.ExtractMarkers(result);

        markers.Should().Contain(m => m.Name == "Glucosa", "single-line marker on lab page");
        markers.Should().Contain(m => m.Name == "Colesterol Total" && m.Value == 210, "merged-line marker on lab page");
    }
}
