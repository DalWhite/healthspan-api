using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class DataTests : ApiTestBase
{
    public DataTests(HealthspanApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetImportTemplate_Returns_CsvFile()
    {
        var response = await Client.GetAsync("/data/import/template");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    }

    [Fact]
    public async Task ImportCsv_WithoutFile_Returns_BadRequest()
    {
        var content = new MultipartFormDataContent();

        var response = await Client.PostAsync("/data/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportCsv_WithNonCsvFile_Returns_BadRequest()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("not a csv"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "data.txt");

        var response = await Client.PostAsync("/data/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportCsv_WithValidCsv_Returns_Ok()
    {
        const string csv =
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "01/06/25,Bench Press,4,10,80,Test row";
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "import.csv");

        var response = await Client.PostAsync("/data/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
