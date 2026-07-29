#nullable enable

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

internal static class BloodMarkerValueNormalizer
{
    private static readonly IReadOnlyDictionary<string, string>
        MetricAliases =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["glucosa"] = "glucose",
                ["glycemia"] = "glucose",
                ["glycaemia"] = "glucose",
                ["colesterol"] = "total-cholesterol",
                ["colesterol-total"] = "total-cholesterol",
                ["total-cholesterol"] = "total-cholesterol",
                ["hdl"] = "hdl-cholesterol",
                ["colesterol-hdl"] = "hdl-cholesterol",
                ["hdl-cholesterol"] = "hdl-cholesterol",
                ["ldl"] = "ldl-cholesterol",
                ["colesterol-ldl"] = "ldl-cholesterol",
                ["ldl-cholesterol"] = "ldl-cholesterol",
                ["trigliceridos"] = "triglycerides",
                ["triglycerides"] = "triglycerides",
                ["creatinina"] = "creatinine",
                ["creatinine"] = "creatinine",
                ["vitamina-d"] = "vitamin-d",
                ["25-oh-vitamina-d"] = "vitamin-d",
                ["25-hydroxy-vitamin-d"] = "vitamin-d",
                ["vitamin-d"] = "vitamin-d",
                ["hemoglobina"] = "hemoglobin",
                ["hemoglobin"] = "hemoglobin",
                ["ferritina"] = "ferritin",
                ["ferritin"] = "ferritin",
                ["tsh"] = "tsh"
            };

    public static string NormalizeMetricKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new string(
            decomposed
                .Where(character =>
                    CharUnicodeInfo.GetUnicodeCategory(character)
                    != UnicodeCategory.NonSpacingMark)
                .ToArray())
            .Normalize(NormalizationForm.FormC);
        var key = Regex.Replace(
                withoutDiacritics,
                "[^a-z0-9]+",
                "-")
            .Trim('-');

        return MetricAliases.TryGetValue(key, out var canonical)
            ? canonical
            : key;
    }

    public static NormalizedMarkerMeasurement Normalize(
        string metricKey,
        string? inputUnit,
        decimal? value,
        decimal? referenceMin,
        decimal? referenceMax,
        string? configuredDefaultUnit)
    {
        var canonicalInput = CanonicalizeUnit(inputUnit);
        var targetUnit = string.IsNullOrWhiteSpace(
            configuredDefaultUnit)
            ? GetKnownTargetUnit(metricKey)
            : configuredDefaultUnit.Trim();

        if (string.IsNullOrWhiteSpace(canonicalInput))
        {
            return new(
                value,
                string.IsNullOrWhiteSpace(targetUnit)
                    ? null
                    : targetUnit,
                referenceMin,
                referenceMax);
        }

        if (string.IsNullOrWhiteSpace(targetUnit)
            || UnitsMatch(canonicalInput, targetUnit))
        {
            return new(
                value,
                string.IsNullOrWhiteSpace(targetUnit)
                    ? canonicalInput
                    : targetUnit,
                referenceMin,
                referenceMax);
        }

        if (!TryGetFactor(
                metricKey,
                canonicalInput,
                targetUnit,
                out var factor))
        {
            return new(
                value,
                canonicalInput,
                referenceMin,
                referenceMax);
        }

        return new(
            Multiply(value, factor),
            targetUnit,
            Multiply(referenceMin, factor),
            Multiply(referenceMax, factor));
    }

    private static string? GetKnownTargetUnit(string metricKey)
        => metricKey switch
        {
            "glucose"
                or "total-cholesterol"
                or "hdl-cholesterol"
                or "ldl-cholesterol"
                or "triglycerides"
                or "creatinine" => "mg/dL",
            "vitamin-d" => "ng/mL",
            "hemoglobin" => "g/dL",
            "ferritin" => "ng/mL",
            "tsh" => "mIU/L",
            _ => null
        };

    private static bool TryGetFactor(
        string metricKey,
        string sourceUnit,
        string targetUnit,
        out decimal factor)
    {
        factor = 1m;
        var source = UnitKey(sourceUnit);
        var target = UnitKey(targetUnit);

        if (target == "mg/dl" && source == "mmol/l")
        {
            factor = metricKey switch
            {
                "glucose" => 18.0182m,
                "total-cholesterol"
                    or "hdl-cholesterol"
                    or "ldl-cholesterol" => 38.67m,
                "triglycerides" => 88.57m,
                _ => 0m
            };
            return factor > 0;
        }

        if (metricKey == "creatinine"
            && target == "mg/dl"
            && source == "umol/l")
        {
            factor = 1m / 88.4m;
            return true;
        }

        if (metricKey == "vitamin-d"
            && target == "ng/ml"
            && source == "nmol/l")
        {
            factor = 1m / 2.496m;
            return true;
        }

        if (metricKey == "hemoglobin"
            && target == "g/dl"
            && source == "g/l")
        {
            factor = 0.1m;
            return true;
        }

        if (metricKey == "ferritin"
            && target == "ng/ml"
            && source == "ug/l")
        {
            return true;
        }

        if (metricKey == "tsh"
            && target == "miu/l"
            && source == "uiu/ml")
        {
            return true;
        }

        return false;
    }

    private static decimal? Multiply(
        decimal? value,
        decimal factor)
        => value.HasValue
            ? decimal.Round(
                value.Value * factor,
                6,
                MidpointRounding.AwayFromZero)
            : null;

    private static bool UnitsMatch(string left, string right)
        => string.Equals(
            UnitKey(left),
            UnitKey(right),
            StringComparison.OrdinalIgnoreCase);

    private static string? CanonicalizeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return null;
        }

        return UnitKey(unit) switch
        {
            "mg/dl" => "mg/dL",
            "mmol/l" => "mmol/L",
            "umol/l" => "µmol/L",
            "nmol/l" => "nmol/L",
            "ng/ml" => "ng/mL",
            "ug/l" => "µg/L",
            "g/dl" => "g/dL",
            "g/l" => "g/L",
            "miu/l" => "mIU/L",
            "uiu/ml" => "µIU/mL",
            var normalized => normalized
        };
    }

    private static string UnitKey(string value)
        => value
            .Trim()
            .Replace("μ", "u", StringComparison.Ordinal)
            .Replace("µ", "u", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}

internal sealed record NormalizedMarkerMeasurement(
    decimal? Value,
    string? Unit,
    decimal? ReferenceMin,
    decimal? ReferenceMax);
