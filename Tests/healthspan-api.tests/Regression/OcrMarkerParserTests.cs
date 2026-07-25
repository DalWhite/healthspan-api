using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// Unit tests for the pure parsing/validation logic in OcrMarkerParser.
/// These verify OCR extraction regressions without any HTTP or DB dependency.
/// </summary>
public class OcrMarkerParserTests
{
    [Theory]
    [InlineData("Glucosa 95 mg/dL 70-110", "Glucosa", "95", "mg/dL")]
    [InlineData("Colesterol Total 210 mg/dL 0-200", "Colesterol Total", "210", "mg/dL")]
    [InlineData("Hemoglobina 14.5 g/dL 12.0-17.0", "Hemoglobina", "14.5", "g/dL")]
    [InlineData("Leucocitos 7.2 miles/uL 4.0-11.0", "Leucocitos", "7.2", "miles/uL")]
    public void TryParseMarkerLine_Parses_Standard_Lab_Lines(string line, string expectedName, string expectedValue, string expectedUnit)
    {
        var result = OcrMarkerParser.TryParseMarkerLine(line, out var parsed);

        result.Should().BeTrue();
        parsed.Name.Should().Be(expectedName);
        parsed.ValueText.Should().Be(expectedValue);
        parsed.Unit.Should().Be(expectedUnit);
    }

    [Theory]
    [InlineData("Glucosa 95 mg/dL 70-110", 70, 110)]
    [InlineData("Creatinina 1.1 mg/dL 0.6-1.2", 0.6, 1.2)]
    public void TryParseMarkerLine_Extracts_Reference_Range(string line, double expectedMin, double expectedMax)
    {
        OcrMarkerParser.TryParseMarkerLine(line, out var parsed);

        parsed.ReferenceMinValue.Should().Be((decimal)expectedMin);
        parsed.ReferenceMaxValue.Should().Be((decimal)expectedMax);
    }

    [Theory]
    [InlineData("95", 95)]
    [InlineData("14.5", 14.5)]
    [InlineData("1,23", 1.23)]
    [InlineData("<0.5", 0.5)]
    [InlineData(">200", 200)]
    public void TryParseNumericToken_Parses_Valid_Tokens(string input, double expected)
    {
        var result = OcrMarkerParser.TryParseNumericToken(input, out var value);

        result.Should().BeTrue();
        value.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void TryParseNumericToken_Rejects_Invalid_Tokens(string? input)
    {
        var result = OcrMarkerParser.TryParseNumericToken(input!, out _);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("70-110", 70, 110)]
    [InlineData("(0.6-1.2)", 0.6, 1.2)]
    [InlineData("12.0-17.0", 12, 17)]
    public void TryExtractReferenceRange_Extracts_Ranges(string tail, double expectedMin, double expectedMax)
    {
        var result = OcrMarkerParser.TryExtractReferenceRange(tail, out var min, out var max);

        result.Should().BeTrue();
        min.Should().Be((decimal)expectedMin);
        max.Should().Be((decimal)expectedMax);
    }

    [Theory]
    [InlineData("mg/dL", "mg/dL")]
    [InlineData("g/L 70-110", "g/L")]
    [InlineData("% 0-5", "%")]
    public void ExtractUnitFromTail_Extracts_Unit(string tail, string expectedUnit)
    {
        var unit = OcrMarkerParser.ExtractUnitFromTail(tail);
        unit.Should().Be(expectedUnit);
    }

    [Theory]
    [InlineData("mg/dL", true)]
    [InlineData("g/L", true)]
    [InlineData("%", true)]
    [InlineData("10^3/µL", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeUnit_Identifies_Units(string? input, bool expected)
    {
        OcrMarkerParser.LooksLikeUnit(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("resultado", true)]
    [InlineData("unidades", true)]
    [InlineData("Glucosa", false)]
    [InlineData("Hemoglobina", false)]
    public void LooksLikeHeader_Identifies_Headers(string input, bool expected)
    {
        OcrMarkerParser.LooksLikeHeader(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Glucosa", true)]
    [InlineData("Colesterol", true)]
    [InlineData("x", false)]
    [InlineData("fecha", false)]
    [InlineData("madrid", false)]
    [InlineData("página 2", false)]
    public void IsLikelyMarkerName_Validates_Names(string input, bool expected)
    {
        OcrMarkerParser.IsLikelyMarkerName(input).Should().Be(expected);
    }

    [Fact]
    public void NormalizeMarkerName_Strips_Decorators()
    {
        OcrMarkerParser.NormalizeMarkerName("  :Glucosa:.  ").Should().Be("Glucosa");
        OcrMarkerParser.NormalizeMarkerName("**Hemoglobina**").Should().Be("Hemoglobina");
    }

    [Fact]
    public void NormalizeWhitespace_Collapses_Spaces()
    {
        OcrMarkerParser.NormalizeWhitespace("  Hello   World  ").Should().Be("Hello World");
        OcrMarkerParser.NormalizeWhitespace(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Glucosa 95 70-110")]
    [InlineData("Hematies 4.5 4.0-5.5")]
    public void LooksLikeLabDataLine_Detects_Data_Lines(string line)
    {
        OcrMarkerParser.LooksLikeLabDataLine(line).Should().BeTrue();
    }

    [Fact]
    public void TryParseMarkerLine_Rejects_Narrative_Lines()
    {
        var result = OcrMarkerParser.TryParseMarkerLine("Observaciones generales del paciente", out _);
        result.Should().BeFalse();
    }

    // ── Extended edge case coverage ─────────────────────────────────────────

    [Theory]
    [InlineData("Creatinina 1,23 mg/dL 0,6-1,2", "Creatinina", "1,23", "mg/dL")]
    [InlineData("Ácido Úrico 5.8 mg/dL 3.4-7.0", "Ácido Úrico", "5.8", "mg/dL")]
    [InlineData("Plaquetas 250 miles/uL 150-400", "Plaquetas", "250", "miles/uL")]
    public void TryParseMarkerLine_Handles_Comma_Decimal_And_Accented_Names(string line, string expectedName, string expectedValue, string expectedUnit)
    {
        var result = OcrMarkerParser.TryParseMarkerLine(line, out var parsed);

        result.Should().BeTrue();
        parsed.Name.Should().Be(expectedName);
        parsed.ValueText.Should().Be(expectedValue);
        parsed.Unit.Should().Be(expectedUnit);
    }

    [Theory]
    [InlineData("<0.5", 0.5)]
    [InlineData(">200", 200)]
    [InlineData("=15", 15)]
    [InlineData("*3.5", 3.5)]
    public void TryParseNumericToken_Strips_Prefix_Symbols(string input, double expected)
    {
        var result = OcrMarkerParser.TryParseNumericToken(input, out var value);

        result.Should().BeTrue();
        value.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("(inf. 4.5)", null, 4.5)]
    [InlineData("(sup. 7.0)", 7.0, null)]
    public void TryExtractReferenceRange_Handles_InfSup_Bounds(string tail, double? expectedMin, double? expectedMax)
    {
        var result = OcrMarkerParser.TryExtractReferenceRange(tail, out var min, out var max);

        result.Should().BeTrue();
        if (expectedMin.HasValue)
            min.Should().Be((decimal)expectedMin.Value);
        else
            min.Should().BeNull();

        if (expectedMax.HasValue)
            max.Should().Be((decimal)expectedMax.Value);
        else
            max.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryExtractReferenceRange_Returns_False_For_Empty_Input(string? tail)
    {
        var result = OcrMarkerParser.TryExtractReferenceRange(tail, out var min, out var max);

        result.Should().BeFalse();
        min.Should().BeNull();
        max.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractUnitFromTail_Returns_Null_For_Empty_Input(string? tail)
    {
        OcrMarkerParser.ExtractUnitFromTail(tail).Should().BeNull();
    }

    [Theory]
    [InlineData("10^3/µL", true)]
    [InlineData("fL", true)]
    [InlineData("mm/h", true)]
    [InlineData("", false)]
    public void LooksLikeUnit_Additional_Medical_Units(string? input, bool expected)
    {
        OcrMarkerParser.LooksLikeUnit(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("hematología y hemostasia", true)]
    [InlineData("bioquímica", true)]
    [InlineData("análisis de orina", true)]
    [InlineData("6.analitica", true)]
    [InlineData("índices", true)]
    [InlineData("Glucosa alta", false)]
    public void LooksLikeHeader_Detects_Spanish_Lab_Section_Headers(string input, bool expected)
    {
        OcrMarkerParser.LooksLikeHeader(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("fecha consulta", false)]
    [InlineData("preving salud", false)]
    [InlineData("certificado EC", false)]
    [InlineData("norma une iso", false)]
    [InlineData("por", false)]
    [InlineData("de", false)]
    [InlineData("fórmula", false)]
    public void IsLikelyMarkerName_Rejects_Known_Non_Medical_Names(string input, bool expected)
    {
        OcrMarkerParser.IsLikelyMarkerName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Hematies", true)]
    [InlineData("Hematocrito", true)]
    [InlineData("GGT", true)]
    [InlineData("GOT", true)]
    [InlineData("GPT", true)]
    [InlineData("Urea", true)]
    public void IsLikelyMarkerName_Accepts_Common_Biomarkers(string input, bool expected)
    {
        OcrMarkerParser.IsLikelyMarkerName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("sedimento en orina detalle 5", true)]
    [InlineData("observaciones generales 3", true)]
    [InlineData("Glucosa", false)]
    public void LooksLikeNarrativeOrCompositeLine_Detects_Narratives(string name, bool expected)
    {
        OcrMarkerParser.LooksLikeNarrativeOrCompositeLine(name, null).Should().Be(expected);
    }

    [Fact]
    public void LooksLikeNarrativeOrCompositeLine_Detects_Campo_In_Tail()
    {
        OcrMarkerParser.LooksLikeNarrativeOrCompositeLine("Leucocitos", "/ campo").Should().BeTrue();
    }

    [Fact]
    public void TryParseMarkerLine_With_Parenthesized_Range()
    {
        var result = OcrMarkerParser.TryParseMarkerLine("Glucosa 95 mg/dL (70-110)", out var parsed);

        result.Should().BeTrue();
        parsed.Name.Should().Be("Glucosa");
        parsed.ReferenceMinValue.Should().Be(70);
        parsed.ReferenceMaxValue.Should().Be(110);
    }

    [Fact]
    public void LooksLikeLabDataLine_Rejects_Non_Numeric_Lines()
    {
        OcrMarkerParser.LooksLikeLabDataLine("Just some text without numbers").Should().BeFalse();
    }

    [Fact]
    public void LooksLikeLabDataLine_Rejects_Empty_And_Whitespace()
    {
        OcrMarkerParser.LooksLikeLabDataLine("").Should().BeFalse();
        OcrMarkerParser.LooksLikeLabDataLine("   ").Should().BeFalse();
    }

    [Fact]
    public void NormalizeWhitespace_Handles_Tabs_And_Newlines()
    {
        OcrMarkerParser.NormalizeWhitespace("Hello\tWorld\n!").Should().Be("Hello World !");
    }
}
