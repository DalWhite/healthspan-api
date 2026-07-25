using Azure.AI.DocumentIntelligence;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// Unit tests for OcrPageScorer — page scoring and selection logic
/// used to identify lab-relevant pages from OCR analysis results.
/// </summary>
public class OcrPageScorerTests
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

    private static DocumentTable BuildLabTable(int pageNumber, int rowCount = 4, int columnCount = 4)
    {
        var cells = new List<DocumentTableCell>
        {
            Cell(0, 0, "Parámetro", pageNumber),
            Cell(0, 1, "Resultado", pageNumber),
            Cell(0, 2, "Unidades", pageNumber),
            Cell(0, 3, "Referencia", pageNumber),
            Cell(1, 0, "Glucosa", pageNumber, 50),
            Cell(1, 1, "95", pageNumber, 60),
            Cell(1, 2, "mg/dL", pageNumber, 65),
            Cell(1, 3, "70-110", pageNumber, 72),
            Cell(2, 0, "Colesterol", pageNumber, 80),
            Cell(2, 1, "210", pageNumber, 92),
            Cell(2, 2, "mg/dL", pageNumber, 97),
            Cell(2, 3, "0-200", pageNumber, 104),
            Cell(3, 0, "Hemoglobina", pageNumber, 110),
            Cell(3, 1, "14.5", pageNumber, 123),
            Cell(3, 2, "g/dL", pageNumber, 129),
            Cell(3, 3, "12.0-17.0", pageNumber, 135),
        };

        return DocumentIntelligenceModelFactory.DocumentTable(
            rowCount: rowCount, columnCount: columnCount, cells: cells,
            boundingRegions: new[] { DocumentIntelligenceModelFactory.BoundingRegion(pageNumber, Array.Empty<float>()) },
            spans: new[] { Span(0, 150) },
            caption: null, footnotes: Array.Empty<DocumentFootnote>());
    }

    private static DocumentTable BuildEmptyTable(int pageNumber) =>
        DocumentIntelligenceModelFactory.DocumentTable(
            rowCount: 0, columnCount: 0, cells: Array.Empty<DocumentTableCell>(),
            boundingRegions: new[] { DocumentIntelligenceModelFactory.BoundingRegion(pageNumber, Array.Empty<float>()) },
            spans: Array.Empty<DocumentSpan>(),
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

    // ── GetLikelyLabPages ───────────────────────────────────────────────────

    [Fact]
    public void GetLikelyLabPages_Returns_Pages_With_Lab_Content()
    {
        var page1 = BuildPage(1, new[] { "Empresa Médica S.L.", "Informe del paciente", "Nombre: David Alonso" });
        var page2 = BuildPage(2, new[] { "Hematología y hemostasia", "Glucosa 95 mg/dL 70-110", "Hemoglobina 14.5 g/dL 12.0-17.0" });
        var result = BuildAnalyzeResult(new[] { page1, page2 });

        var labPages = OcrPageScorer.GetLikelyLabPages(result);

        labPages.Should().Contain(2, "page 2 has lab data lines");
    }

    [Fact]
    public void GetLikelyLabPages_Returns_Empty_When_No_Lab_Content()
    {
        var page1 = BuildPage(1, new[] { "Empresa Médica S.L.", "Dirección fiscal", "CIF: B12345678" });
        var result = BuildAnalyzeResult(new[] { page1 });

        var labPages = OcrPageScorer.GetLikelyLabPages(result);

        labPages.Should().BeEmpty();
    }

    [Fact]
    public void GetLikelyLabPages_Includes_Pages_With_Tables()
    {
        var page1 = BuildPage(1, new[] { "Informe analítica general" });
        var table = BuildLabTable(1);
        var result = BuildAnalyzeResult(new[] { page1 }, new[] { table });

        var labPages = OcrPageScorer.GetLikelyLabPages(result);

        labPages.Should().Contain(1);
    }

    [Fact]
    public void GetLikelyLabPages_Detects_BiomarkerHint_Keywords()
    {
        var page1 = BuildPage(1, new[] { "Glucosa en sangre", "Colesterol total", "Trigliceridos séricos" });
        var result = BuildAnalyzeResult(new[] { page1 });

        var labPages = OcrPageScorer.GetLikelyLabPages(result);

        labPages.Should().Contain(1, "page contains biomarker hint keywords");
    }

    [Fact]
    public void GetLikelyLabPages_Adds_Adjacent_Pages_With_Score()
    {
        var page1 = BuildPage(1, new[] { "Bioquímica general", "Glucosa 95 mg/dL 70-110", "Colesterol 210 mg/dL 0-200" });
        var page2 = BuildPage(2, new[] { "Hemoglobina 14.5 g/dL 12.0-17.0", "Hematocrito 42 % 37-52" });
        var result = BuildAnalyzeResult(new[] { page1, page2 });

        var labPages = OcrPageScorer.GetLikelyLabPages(result);

        labPages.Should().Contain(1);
        // page 2 should be included either directly or as adjacent
        labPages.Should().Contain(2);
    }

    // ── GetCombinedPageScores ───────────────────────────────────────────────

    [Fact]
    public void GetCombinedPageScores_Aggregates_Across_Multiple_Results()
    {
        var result1 = BuildAnalyzeResult(new[] {
            BuildPage(1, new[] { "Glucosa 95 mg/dL 70-110", "Hemoglobina 14.5 g/dL 12.0-17.0" })
        });
        var result2 = BuildAnalyzeResult(new[] {
            BuildPage(1, new[] { "Colesterol 210 mg/dL 0-200" })
        });

        var scores = OcrPageScorer.GetCombinedPageScores(new List<AnalyzeResult> { result1, result2 });

        scores.Should().ContainKey(1);
        scores[1].Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetCombinedPageScores_Handles_Empty_Results()
    {
        var result = BuildAnalyzeResult(new[] {
            BuildPage(1, new[] { "Just some text without markers" })
        });

        var scores = OcrPageScorer.GetCombinedPageScores(new List<AnalyzeResult> { result });

        // Should not throw, may or may not contain page 1 with score 0
        scores.Should().NotBeNull();
    }

    // ── SelectBestPages ─────────────────────────────────────────────────────

    [Fact]
    public void SelectBestPages_Returns_Top_Pages_Above_Threshold()
    {
        var scores = new Dictionary<int, int>
        {
            [1] = 10, [2] = 8, [3] = 2, [4] = 1
        };

        var selected = OcrPageScorer.SelectBestPages(scores, topPageCount: 3, minPageScore: 3);

        selected.Should().Contain(1);
        selected.Should().Contain(2);
        selected.Should().NotContain(3, "score 2 < minPageScore 3");
        selected.Should().NotContain(4);
    }

    [Fact]
    public void SelectBestPages_Respects_TopPageCount_Limit()
    {
        var scores = new Dictionary<int, int>
        {
            [1] = 10, [2] = 8, [3] = 6, [4] = 4
        };

        var selected = OcrPageScorer.SelectBestPages(scores, topPageCount: 2, minPageScore: 0);

        selected.Should().HaveCount(2);
        selected.Should().Contain(1);
        selected.Should().Contain(2);
    }

    [Fact]
    public void SelectBestPages_Returns_Empty_When_All_Below_Threshold()
    {
        var scores = new Dictionary<int, int>
        {
            [1] = 1, [2] = 2
        };

        var selected = OcrPageScorer.SelectBestPages(scores, topPageCount: 5, minPageScore: 5);

        selected.Should().BeEmpty();
    }

    [Fact]
    public void SelectBestPages_Handles_Empty_Scores()
    {
        var selected = OcrPageScorer.SelectBestPages(new Dictionary<int, int>(), topPageCount: 3, minPageScore: 0);

        selected.Should().BeEmpty();
    }

    [Fact]
    public void SelectBestPages_Ensures_At_Least_One_When_TopCount_Zero()
    {
        var scores = new Dictionary<int, int> { [1] = 5 };

        var selected = OcrPageScorer.SelectBestPages(scores, topPageCount: 0, minPageScore: 0);

        // topPageCount of 0 → Math.Max(1, 0) = 1
        selected.Should().HaveCount(1);
    }

    // ── GetTableScoresByPage ────────────────────────────────────────────────

    [Fact]
    public void GetTableScoresByPage_Scores_Lab_Tables_Higher()
    {
        var labTable = BuildLabTable(1);
        var emptyTable = BuildEmptyTable(2);
        var result = BuildAnalyzeResult(
            new[] { BuildPage(1, Array.Empty<string>()), BuildPage(2, Array.Empty<string>()) },
            new[] { labTable, emptyTable });

        var scores = OcrPageScorer.GetTableScoresByPage(result);

        scores.Should().ContainKey(1);
        scores[1].Should().BeGreaterThan(0, "lab table has header hints and reference ranges");
    }

    [Fact]
    public void GetTableScoresByPage_Returns_Empty_For_No_Tables()
    {
        var result = BuildAnalyzeResult(new[] { BuildPage(1, new[] { "Some text" }) });

        var scores = OcrPageScorer.GetTableScoresByPage(result);

        scores.Should().BeEmpty();
    }

    // ── Scoring keywords ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bioquímica general", true)]
    [InlineData("Hematología y hemostasia", true)]
    [InlineData("Análisis de orina", true)]
    [InlineData("Empresa consultora", false)]
    public void LabSection_Keywords_Are_Detected(string lineContent, bool expectsHighScore)
    {
        var page = BuildPage(1, new[] { lineContent });
        var result = BuildAnalyzeResult(new[] { page });

        var labPages = OcrPageScorer.GetLikelyLabPages(result);

        if (expectsHighScore)
            labPages.Should().Contain(1);
        else
            labPages.Should().BeEmpty();
    }
}
