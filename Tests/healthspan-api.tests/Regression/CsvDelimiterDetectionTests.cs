using System.Text;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// Semicolon-delimiter detection in ParseCsvRows (t_020e68a5): the error must
/// name the template endpoint, BOM must not break detection, and comma CSVs
/// (even with ';' inside a header field) must parse as before.
/// </summary>
public class CsvDelimiterDetectionTests
{
    private const string TemplateHint = "/data/import/template";

    private static CsvImportParseResult Parse(string csvWithOptionalBom)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvWithOptionalBom));
        return new CsvService().ParseCsvRows(stream);
    }

    [Fact]
    public void Semicolon_Csv_Reports_Structural_Error_With_Template_Hint()
    {
        var result = Parse(
            "Date;ExerciseType;Series;Reps;Weight;AdditionalComments\n" +
            "01/06/25;Bench Press;4;10;80;Semicolon row");

        result.Rows.Should().BeEmpty();
        result.StructuralErrors.Should().ContainSingle();
        result.StructuralErrors[0].Should().Contain(";").And.Contain(TemplateHint);
    }

    [Fact]
    public void Semicolon_Csv_With_Utf8_Bom_Is_Still_Detected()
    {
        var result = Parse(
            "\uFEFFDate;ExerciseType;Series\n" +
            "01/06/25;Bench Press;4");

        result.StructuralErrors.Should().ContainSingle();
        result.StructuralErrors[0].Should().Contain(TemplateHint);
    }

    [Fact]
    public void Comma_Csv_With_Utf8_Bom_Parses_As_Before()
    {
        var result = Parse(
            "\uFEFFDate,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "01/06/25,Bench Press,4,10,80,Bom row");

        result.StructuralErrors.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        result.Rows.Should().ContainSingle();
        result.Rows[0].Comments.Should().Be("Bom row");
    }

    [Fact]
    public void Comma_Csv_With_Semicolon_Inside_Header_Field_Is_Not_Flagged()
    {
        var result = Parse(
            "Date,ExerciseType;legacy,Series\n" +
            "01/06/25,Bench Press,4");

        result.StructuralErrors.Should().NotContain(e => e.Contains(TemplateHint));
    }

    [Fact]
    public void Semicolon_Csv_Fails_Fast_Before_Row_Errors()
    {
        // Without detection this used to surface as "no header row" with no
        // hint; with detection the template message wins.
        var result = Parse("01/06/25;Bench Press;4");

        result.StructuralErrors.Should().ContainSingle();
        result.StructuralErrors[0].Should().Contain(TemplateHint);
    }
}
