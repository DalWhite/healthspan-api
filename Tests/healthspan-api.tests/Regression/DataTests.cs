using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task RepeatedCsvImports_Reuse_The_DailyWorkout()
    {
        const string csv =
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "01/06/46,Bench Press,4,10,80,Imported row";

        using var firstContent = BuildCsvContent(csv);
        using var secondContent = BuildCsvContent(csv);
        var firstResponse = await Client.PostAsync(
            "/data/import",
            firstContent);
        var secondResponse = await Client.PostAsync(
            "/data/import",
            secondContent);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var workouts = await db.Workouts
            .IgnoreQueryFilters()
            .Include(workout => workout.Exercises)
            .Where(workout =>
                workout.UserId == TestDataSeeder.TestUserId
                && workout.Date >= new DateTime(2046, 6, 1)
                && workout.Date < new DateTime(2046, 6, 2))
            .ToListAsync();

        workouts.Should().ContainSingle();
        workouts[0].Exercises.Should().HaveCount(2);
        workouts[0].DailySessionKey.Should().HaveLength(64);
    }

    private static MultipartFormDataContent BuildCsvContent(string csv)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(
            Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "import.csv");
        return content;
    }
}
