#nullable enable

using Azure.AI.DocumentIntelligence;

/// <summary>
/// Scores and selects lab-relevant pages from Azure DI analysis results.
/// </summary>
public static class OcrPageScorer
{
    private static readonly string[] LabSectionKeywords =
    {
        "analitica", "analítica", "hematologia", "hematología", "hemostasia", "bioquimica", "bioquímica", "orina"
    };

    private static readonly string[] LabTableHeaderHints =
    {
        "parámetro", "parametro", "resultado", "valor", "unidad", "unidades", "referencia", "val.referencia", "rango"
    };

    // ── Public API ──────────────────────────────────────────────────────────

    public static HashSet<int> GetLikelyLabPages(AnalyzeResult result)
    {
        var pageScores = ScoreAllPages(result);
        var likelyPages = pageScores.Where(kv => kv.Value >= 3).Select(kv => kv.Key).ToHashSet();
        AddPagesWithStrongCandidateLines(result, likelyPages, GetTableScoresByPage(result));
        AddAdjacentPages(likelyPages, pageScores);
        return likelyPages;
    }

    public static Dictionary<int, int> GetCombinedPageScores(List<AnalyzeResult> results)
    {
        var scores = new Dictionary<int, int>();
        foreach (var result in results)
            AccumulatePageScores(result, scores);
        return scores;
    }

    public static HashSet<int> SelectBestPages(Dictionary<int, int> pageScores, int topPageCount, int minPageScore)
    {
        var top = Math.Max(1, topPageCount);
        var min = Math.Max(0, minPageScore);

        return pageScores
            .Where(kv => kv.Value >= min)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(top)
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    public static Dictionary<int, int> GetTableScoresByPage(AnalyzeResult result)
    {
        var scores = new Dictionary<int, int>();
        foreach (var table in result.Tables)
        {
            var (pageNumber, tableScore) = ScoreTable(table);
            if (pageNumber > 0)
                scores[pageNumber] = scores.GetValueOrDefault(pageNumber) + tableScore;
        }
        return scores;
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static Dictionary<int, int> ScoreAllPages(AnalyzeResult result)
    {
        var tableScores = GetTableScoresByPage(result);
        var scores = new Dictionary<int, int>();

        foreach (var page in result.Pages)
        {
            var score = tableScores.GetValueOrDefault(page.PageNumber) + ScorePageLines(page);
            if (score > 0)
                scores[page.PageNumber] = score;
        }

        return scores;
    }

    private static void AccumulatePageScores(AnalyzeResult result, Dictionary<int, int> scores)
    {
        var tableScores = GetTableScoresByPage(result);
        foreach (var page in result.Pages)
        {
            var score = tableScores.GetValueOrDefault(page.PageNumber) + ScorePageLines(page);
            scores[page.PageNumber] = scores.GetValueOrDefault(page.PageNumber) + score;
        }
    }

    private static int ScorePageLines(DocumentPage page)
    {
        return page.Lines.Sum(line =>
            ScoreLine(OcrMarkerParser.NormalizeWhitespace(line.Content).ToLowerInvariant()));
    }

    private static int ScoreLine(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var score = 0;
        if (LabSectionKeywords.Any(content.Contains))
            score += 3;
        if (OcrMarkerParser.BiomarkerHints.Any(content.Contains))
            score += 2;
        if (OcrMarkerParser.ReferenceRange.IsMatch(content))
            score += 1;
        if (OcrMarkerParser.LooksLikeLabDataLine(content))
            score += 1;
        return score;
    }

    private static (int pageNumber, int score) ScoreTable(DocumentTable table)
    {
        var pageNumber = table.BoundingRegions.Count > 0 ? table.BoundingRegions[0].PageNumber : 0;
        if (pageNumber <= 0)
            return (0, 0);

        var cells = table.Cells
            .Select(c => OcrMarkerParser.NormalizeWhitespace(c.Content).ToLowerInvariant())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();

        var score = 0;
        if (cells.Any(c => LabTableHeaderHints.Any(c.Contains)))
            score += 2;
        score += Math.Min(4, cells.Count(c => OcrMarkerParser.ReferenceRange.IsMatch(c)));
        score += Math.Min(4, cells.Count(c => OcrMarkerParser.TryExtractValueFromCell(c, out _)) / 3);
        if (table.ColumnCount >= 3)
            score += 1;

        return (pageNumber, score);
    }

    private static void AddPagesWithStrongCandidateLines(AnalyzeResult result, HashSet<int> likelyPages, Dictionary<int, int> tableScores)
    {
        foreach (var page in result.Pages)
        {
            if (likelyPages.Contains(page.PageNumber))
                continue;
            if (CountCandidateLines(page) >= 3 || tableScores.GetValueOrDefault(page.PageNumber) >= 3)
                likelyPages.Add(page.PageNumber);
        }
    }

    private static int CountCandidateLines(DocumentPage page)
    {
        return page.Lines
            .Select(l => OcrMarkerParser.NormalizeWhitespace(l.Content))
            .Count(c => OcrMarkerParser.ReferenceRange.IsMatch(c)
                || OcrMarkerParser.BiomarkerHints.Any(h => c.Contains(h, StringComparison.OrdinalIgnoreCase))
                || OcrMarkerParser.LooksLikeLabDataLine(c));
    }

    private static void AddAdjacentPages(HashSet<int> likelyPages, Dictionary<int, int> pageScores)
    {
        foreach (var pageNumber in likelyPages.ToList())
        {
            if (pageScores.GetValueOrDefault(pageNumber - 1) >= 2)
                likelyPages.Add(pageNumber - 1);
            if (pageScores.GetValueOrDefault(pageNumber + 1) >= 2)
                likelyPages.Add(pageNumber + 1);
        }
    }
}
