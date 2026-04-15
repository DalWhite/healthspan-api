#nullable enable

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Pure parsing and validation logic for OCR marker extraction.
/// No Azure SDK dependencies — operates on strings only.
/// </summary>
public static class OcrMarkerParser
{
    internal static readonly Regex NumericToken = new(@"[<>]?[+-]?\d+(?:[\.,]\d+)?", RegexOptions.Compiled);
    internal static readonly Regex ReferenceRange = new(@"(?<min>\d+(?:[\.,]\d+)?)\s*-\s*(?<max>\d+(?:[\.,]\d+)?)", RegexOptions.Compiled);

    internal static readonly string[] BiomarkerHints =
    {
        "glucosa", "colesterol", "triglicer", "hemoglob", "hematoc", "hematies", "hematíes", "leucoc", "linfoc", "monoc",
        "eosin", "basof", "plaqueta", "creatin", "urea", "got", "gpt", "ggt", "ácido úrico", "acido urico", "ph", "densidad"
    };

    private static readonly Regex InfSupPattern = new(@"\(?\s*(?:inf\.?|sup\.?)\s*(?<bound>\d+(?:[\.,]\d+)?)\s*\)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ParenthesizedRangePattern = new(@"\(\s*(?<min>\d+(?:[\.,]\d+)?)\s*-\s*(?<max>\d+(?:[\.,]\d+)?)\s*\)", RegexOptions.Compiled);
    private static readonly Regex MarkerLinePattern = new(@"^(?<name>[\p{L}][\p{L}\p{N}\-\/%()\.,:&ºªµμ\s]{1,120}?)\s+\.??\s*(?<value>[<>]?[+-]?\d+(?:[\.,]\d+)?)\s*(?<tail>.*)$", RegexOptions.Compiled);
    private static readonly Regex MetricPrefixPattern = new(@"^(?<name>[\p{L}\p{N}][\p{L}\p{N}\-\/%()\.,:&ºªµμ\s]{1,140}?)\s+(?<rest>.+)$", RegexOptions.Compiled);
    private static readonly Regex PageCounterPattern = new(@"^\d+\s+de$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PageLikePattern = new(@"^p[aá]gina\b|^\d+\s+de\s+\d+$|\bn[ºo°]\s*muestra\b|\bservicio\b|\borigen\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] RejectedNameFragments =
    {
        "fecha", "edición", "ref", "índice", "indice", "anamnesis", "exploración", "exploracion", "recomendaciones",
        "electrocardiograma", "preving", "cas training", "david alonso", "48687238x", "madrid", "basilica",
        "avda", "avenida", "calle ", "nuevo mundo", "averroes", "página", "pagina", "teléf", "telef",
        "dr./dra", "dr/dra", "dra.", "muestra", "servicio", "origen", "adeslas", "applus",
        "certificad", "norma une", "iso 9001", "empresa", "estudios analíticos", "estudios analiticos",
        "acreditaci", "enac", "certificado ec", "nº muestra", "sup.", "(sup", "(inf",
        "inf.", "valores de", "resultado %"
    };

    // ── Public API ──────────────────────────────────────────────────────────

    public static bool TryParseMarkerLine(string content, out ParsedMarkerLine parsed)
    {
        parsed = default;
        return TryParseWithPrimaryPattern(content, out parsed)
            || TryParseWithReferenceRange(content, out parsed)
            || TryParseWithTokenFallback(content, out parsed);
    }

    public static bool TryParseNumericToken(string valueText, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(valueText))
            return false;

        var normalized = valueText.Trim().Trim('<', '>', '=', '*').Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryExtractValueFromCell(string content, out string? valueText)
    {
        valueText = null;

        if (TryParseNumericToken(content, out _))
        {
            valueText = content;
            return true;
        }

        var match = NumericToken.Match(content);
        if (!match.Success)
            return false;

        valueText = match.Value;
        return true;
    }

    public static bool TryExtractReferenceRange(string? tail, out decimal? refMin, out decimal? refMax)
    {
        refMin = null;
        refMax = null;
        if (string.IsNullOrWhiteSpace(tail))
            return false;

        if (TryExtractParenthesizedRange(tail, out refMin, out refMax))
            return true;
        if (TryExtractStandardRange(tail, out refMin, out refMax))
            return true;

        TryExtractInfSupBounds(tail, ref refMin, ref refMax);
        return refMin.HasValue || refMax.HasValue;
    }

    public static string? ExtractUnitFromTail(string? tail)
    {
        if (string.IsNullOrWhiteSpace(tail))
            return null;

        var working = StripRangeAndNumericPrefix(tail);
        if (string.IsNullOrWhiteSpace(working))
            return null;

        var tokens = working.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var unitTokens = tokens.TakeWhile(LooksLikeUnit).ToList();
        return unitTokens.Count == 0 ? null : string.Join(" ", unitTokens);
    }

    public static string NormalizeMarkerName(string value)
    {
        return NormalizeWhitespace(value).Trim(':', '-', '.', '*').Trim();
    }

    public static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    public static bool LooksLikeUnit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Regex.IsMatch(value.Trim(), @"^[A-Za-zµμ/%0-9^.,&³²_-]{1,25}$");
    }

    public static bool LooksLikeHeader(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "test" or "marker" or "analyte" or "value" or "result" or "units" or "unit" or "reference" or "range"
            or "parámetro" or "resultado" or "unidades" or "val.referencia" or "hematología y hemostasia" or "bioquímica" or "análisis de orina"
            or "analítica" or "6.analitica" or "6.analítica"
            or "hematologia-hemostasia" or "bioquimica general" or "pruebas enzimáticas" or "pruebas enzimaticas"
            or "serologia infecciosa" or "serología infecciosa" or "proteínas" or "proteinas" or "fórmula leucocitaria" or "formula leucocitaria"
            or "velocidad de sedimentación" or "velocidad de sedimentacion" or "indices" or "índices";
    }

    public static bool LooksLikeLabDataLine(string content)
    {
        var normalized = NormalizeWhitespace(content);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var numericCount = NumericToken.Matches(normalized).Count;
        return numericCount >= 2 && normalized.Any(char.IsLetter);
    }

    public static bool IsLikelyMarkerName(string name)
    {
        var normalized = NormalizeMarkerName(name).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 2)
            return false;
        if (PageCounterPattern.IsMatch(normalized))
            return false;
        if (PageLikePattern.IsMatch(normalized))
            return false;
        if (RejectedNameFragments.Any(normalized.Contains))
            return false;
        if (normalized.Count(char.IsLetter) < 2)
            return false;
        if (IsCommonNonMedicalWord(normalized))
            return false;
        if (LooksLikeUnit(normalized) && !BiomarkerHints.Any(hint => normalized.Contains(hint)))
            return false;

        return true;
    }

    public static bool LooksLikeNarrativeOrCompositeLine(string name, string? tail)
    {
        if (name.Length < 2 || !name.Any(char.IsLetter))
            return true;

        var normalizedName = name.ToLowerInvariant();
        if (normalizedName.Contains("sedimento en orina") || normalizedName.Contains("observaciones") || normalizedName.Contains("resultado de los parámetros"))
            return true;

        if (string.IsNullOrWhiteSpace(tail))
            return false;

        var normalizedTail = tail.ToLowerInvariant();
        if (normalizedTail.Contains(":") && !ReferenceRange.IsMatch(normalizedTail))
            return true;

        return normalizedTail.Contains("/ campo")
            || normalizedTail.Contains("campo")
            || normalizedTail.Contains("edición:")
            || normalizedTail.Contains("ref. :");
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static bool TryParseWithPrimaryPattern(string content, out ParsedMarkerLine parsed)
    {
        parsed = default;

        var match = MarkerLinePattern.Match(content);
        if (!match.Success)
            return false;

        var name = NormalizeMarkerName(match.Groups["name"].Value);
        if (string.IsNullOrWhiteSpace(name) || LooksLikeHeader(name))
            return false;

        var valueText = match.Groups["value"].Value.Trim();
        if (!TryParseNumericToken(valueText, out _))
            return false;

        var tail = NormalizeWhitespace(match.Groups["tail"].Value);
        var unit = ExtractUnitFromTail(tail);
        if (LooksLikeNarrativeOrCompositeLine(name, tail))
            return false;

        TryExtractReferenceRange(tail, out var refMin, out var refMax);
        parsed = new ParsedMarkerLine(name, valueText, unit, refMin, refMax);
        return true;
    }

    private static bool TryParseWithReferenceRange(string content, out ParsedMarkerLine parsed)
    {
        parsed = default;

        var referenceMatch = ReferenceRange.Match(content);
        if (!referenceMatch.Success)
            return false;

        var beforeRange = NormalizeWhitespace(content[..referenceMatch.Index]);
        if (string.IsNullOrWhiteSpace(beforeRange))
            return false;

        var numericMatches = NumericToken.Matches(beforeRange);
        if (numericMatches.Count == 0)
            return false;

        var valueMatch = numericMatches[^1];
        var name = NormalizeMarkerName(beforeRange[..valueMatch.Index]);
        if (string.IsNullOrWhiteSpace(name) || LooksLikeHeader(name))
            return false;

        var valueText = valueMatch.Value.Trim();
        var tail = NormalizeWhitespace(beforeRange[(valueMatch.Index + valueMatch.Length)..]);
        var unit = ExtractUnitFromTail(tail);
        if (LooksLikeNarrativeOrCompositeLine(name, tail))
            return false;

        decimal? refMin = null, refMax = null;
        if (TryParseNumericToken(referenceMatch.Groups["min"].Value, out var parsedMin))
            refMin = parsedMin;
        if (TryParseNumericToken(referenceMatch.Groups["max"].Value, out var parsedMax))
            refMax = parsedMax;

        parsed = new ParsedMarkerLine(name, valueText, unit, refMin, refMax);
        return true;
    }

    private static bool TryParseWithTokenFallback(string content, out ParsedMarkerLine parsed)
    {
        parsed = default;

        var prefixMatch = MetricPrefixPattern.Match(content);
        if (!prefixMatch.Success)
            return false;

        var name = NormalizeMarkerName(prefixMatch.Groups["name"].Value);
        if (string.IsNullOrWhiteSpace(name) || LooksLikeHeader(name))
            return false;

        var rest = prefixMatch.Groups["rest"].Value;
        var numericMatches = NumericToken.Matches(rest);
        if (numericMatches.Count == 0)
            return false;

        var valueMatch = numericMatches[0];
        var valueText = valueMatch.Value;
        var tail = NormalizeWhitespace(rest[(valueMatch.Index + valueMatch.Length)..]);
        var unit = ExtractUnitFromTail(tail);
        if (LooksLikeNarrativeOrCompositeLine(name, tail))
            return false;

        TryExtractReferenceRange(tail, out var refMin, out var refMax);
        parsed = new ParsedMarkerLine(name, valueText, unit, refMin, refMax);
        return true;
    }

    private static bool TryExtractParenthesizedRange(string tail, out decimal? refMin, out decimal? refMax)
    {
        refMin = null;
        refMax = null;

        var match = ParenthesizedRangePattern.Match(tail);
        if (!match.Success)
            return false;

        if (TryParseNumericToken(match.Groups["min"].Value, out var min))
            refMin = min;
        if (TryParseNumericToken(match.Groups["max"].Value, out var max))
            refMax = max;

        return refMin.HasValue || refMax.HasValue;
    }

    private static bool TryExtractStandardRange(string tail, out decimal? refMin, out decimal? refMax)
    {
        refMin = null;
        refMax = null;

        var match = ReferenceRange.Match(tail);
        if (!match.Success)
            return false;

        if (TryParseNumericToken(match.Groups["min"].Value, out var min))
            refMin = min;
        if (TryParseNumericToken(match.Groups["max"].Value, out var max))
            refMax = max;

        return refMin.HasValue || refMax.HasValue;
    }

    private static void TryExtractInfSupBounds(string tail, ref decimal? refMin, ref decimal? refMax)
    {
        // Spanish lab convention: "inf. X" = "inferior a X" = value must be below X → upper bound (refMax).
        // "sup. X" = "superior a X" = value must be above X → lower bound (refMin).
        foreach (Match match in InfSupPattern.Matches(tail))
        {
            if (!TryParseNumericToken(match.Groups["bound"].Value, out var bound))
                continue;

            var prefix = match.Value.ToLowerInvariant();
            if (prefix.Contains("inf"))
                refMax = bound;
            else if (prefix.Contains("sup"))
                refMin = bound;
        }
    }

    private static string StripRangeAndNumericPrefix(string tail)
    {
        var working = tail;

        var rangeMatch = ReferenceRange.Match(working);
        if (rangeMatch.Success)
            working = working[..rangeMatch.Index].Trim();

        if (string.IsNullOrWhiteSpace(working))
            return string.Empty;

        var numMatch = NumericToken.Match(working);
        if (numMatch.Success)
            working = working[..numMatch.Index].Trim();

        return working;
    }

    private static bool IsCommonNonMedicalWord(string normalized)
    {
        return normalized is "por" or "de" or "con" or "para" or "sin" or "fórmula" or "formula"
            or "prueba" or "valores" or "filtrado" or "técnica" or "tecnica" or "ensayo"
            or "método" or "metodo" or "detección" or "deteccion";
    }

    // ── Types ───────────────────────────────────────────────────────────────

    public readonly record struct ParsedMarkerLine(
        string Name, string ValueText, string? Unit,
        decimal? ReferenceMinValue = null, decimal? ReferenceMaxValue = null);
}
