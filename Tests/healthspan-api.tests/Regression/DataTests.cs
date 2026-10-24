using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
    public async Task ImportCsv_WithOversizedFile_Returns_BadRequest()
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(
            new byte[(int)CsvService.MaxImportBytes + 1]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "import.csv");

        var response = await Client.PostAsync("/data/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportCsv_WithMissingHeaderColumns_Returns_BadRequest()
    {
        var content = BuildCsvContent("Foo,Bar\n1,2");

        var response = await Client.PostAsync("/data/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportCsv_Semicolon_Separator_Returns_BadRequest_With_Template_Hint()
    {
        const string csv =
            "Date;ExerciseType;Series;Reps;Weight;AdditionalComments\n" +
            "01/06/25;Bench Press;4;10;80;Semicolon row";

        var response = await Client.PostAsync("/data/import", BuildCsvContent(csv));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("/data/import/template");

        // The hinted template really is downloadable.
        var template = await Client.GetAsync("/data/import/template");
        template.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ImportCsv_Semicolon_Separator_With_Utf8_Bom_Returns_BadRequest_With_Template_Hint()
    {
        var content = new MultipartFormDataContent();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(
                "Date;ExerciseType;Series\r\n01/06/25;Bench Press;4"))
            .ToArray();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "import.csv");

        var response = await Client.PostAsync("/data/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("/data/import/template");
    }

    [Fact]
    public async Task ImportCsv_Comma_Separator_With_Utf8_Bom_Still_Imports()
    {
        var content = new MultipartFormDataContent();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(
                "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
                "01/06/49,Bench Press,4,10,80,Bom comma row"))
            .ToArray();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "import.csv");

        var response = await Client.PostAsync("/data/import", content);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ImportCsv_Returns_202_With_JobId_And_Completes()
    {
        const string csv =
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "01/06/25,Bench Press,4,10,80,Test row";

        var job = await ImportAndWaitAsync(csv);

        job.StatusCode.Should().Be(HttpStatusCode.Accepted);
        job.Status.Should().Be("completed");
        job.Total.Should().Be(1);
        job.Processed.Should().Be(1);
        job.Skipped.Should().Be(0);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var exercise = await db.Exercises
            .IgnoreQueryFilters()
            .Include(e => e.Workout)
            .SingleAsync(e =>
                e.Workout.UserId == TestDataSeeder.TestUserId
                && e.Comments == "Test row");

        exercise.Series.Should().Be(4);
        exercise.Repetitions.Should().Be(10);
        exercise.Weight.Should().Be(80);
        // Epley 1RM: 80 * (1 + 0.0333*10) = 106.6
        exercise.MaxRepetition.Should().Be(106.6);
        exercise.Volume.Should().Be(4 * 10 * 80);
        exercise.ClientEntityId.Should().NotBeNull();
    }

    [Fact]
    public async Task Reimporting_The_Same_Csv_Is_Idempotent()
    {
        const string csv =
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "01/06/46,Bench Press,4,10,80,Idempotent row";

        var first = await ImportAndWaitAsync(csv);
        var second = await ImportAndWaitAsync(csv);

        first.Status.Should().Be("completed");
        first.Processed.Should().Be(1);

        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.Status.Should().Be("completed");
        second.Processed.Should().Be(0);
        second.Skipped.Should().Be(1);

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
        workouts[0].Exercises.Should().HaveCount(1);
        workouts[0].DailySessionKey.Should().HaveLength(64);
    }

    [Fact]
    public async Task ImportCsv_Bad_Rows_Report_Errors_Without_Aborting()
    {
        const string csv =
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "31/31/25,Bench Press,4,10,80,Bad date\n" +
            "02/06/47,Bench Press,4,ten,80,Bad reps\n" +
            "02/06/47,,4,10,80,Missing type\n" +
            "02/06/47,Squat,3,8,100,Good row";

        var job = await ImportAndWaitAsync(csv);

        job.Status.Should().Be("completed_with_errors");
        job.ErrorCount.Should().Be(3);
        job.Processed.Should().Be(1);
        job.Errors.Should().HaveCount(3);
        // Physical line numbers (header = line 1).
        job.Errors.Select(e => e.Row).Should().BeEquivalentTo([2, 3, 4]);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var imported = await db.Exercises
            .IgnoreQueryFilters()
            .Include(e => e.Workout)
            .Where(e =>
                e.Workout.UserId == TestDataSeeder.TestUserId
                && e.Comments == "Good row")
            .ToListAsync();
        imported.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportCsv_Unpadded_Historical_Dates_Are_Parsed()
    {
        const string csv =
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "15/4/18,Deadlift,3,5,120,Historical row";

        var job = await ImportAndWaitAsync(csv);

        job.Status.Should().Be("completed");
        job.Processed.Should().Be(1);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var workout = await db.Workouts
            .IgnoreQueryFilters()
            .Include(w => w.Exercises)
            .SingleAsync(w =>
                w.UserId == TestDataSeeder.TestUserId
                && w.Date == new DateTime(2018, 4, 15));
        workout.Exercises.Should().ContainSingle();
    }

    [Fact]
    public async Task BulkImport_2000_Rows_Completes_In_Batches()
    {
        var builder = new StringBuilder(
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n");
        for (var i = 0; i < 2_000; i++)
        {
            var day = 1 + i % 28;
            var month = 1 + (i / 28) % 11;
            builder.Append($"{day}/{month:D2}/44,Bulk Type {i % 10},{1 + i % 5},{5 + i % 10},{40 + i % 120},bulk-{i}\n");
        }

        var job = await ImportAndWaitAsync(builder.ToString(), timeoutMs: 120_000);

        job.Status.Should().Be("completed");
        job.Total.Should().Be(2_000);
        job.Processed.Should().Be(2_000);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var count = await db.Exercises
            .IgnoreQueryFilters()
            .CountAsync(e =>
                e.Workout.UserId == TestDataSeeder.TestUserId
                && e.Comments!.StartsWith("bulk-"));
        count.Should().Be(2_000);

        // One daily workout per date, not per row.
        var workouts = await db.Workouts
            .IgnoreQueryFilters()
            .CountAsync(w =>
                w.UserId == TestDataSeeder.TestUserId
                && w.Date >= new DateTime(2044, 1, 1)
                && w.Date < new DateTime(2045, 1, 1));
        workouts.Should().Be(11 * 28);
    }

    [Fact]
    public async Task GetImportJobStatus_For_Your_Job_Returns_Ok()
    {
        var job = await ImportAndWaitAsync(
            "Date,ExerciseType,Series,Reps,Weight,AdditionalComments\n" +
            "01/06/48,Bench Press,4,10,80,Ownership row");

        var response = await Client.GetAsync($"/data/import/{job.JobId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record JobStatus(
        HttpStatusCode StatusCode,
        string JobId,
        string Status,
        int Total,
        int Processed,
        int Skipped,
        int ErrorCount,
        List<JobError> Errors);

    private sealed record JobError(int Row, string Message);

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private async Task<JobStatus> ImportAndWaitAsync(
        string csv,
        int timeoutMs = 60_000)
    {
        using var content = BuildCsvContent(csv);
        var response = await Client.PostAsync("/data/import", content);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var posted = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions);
        var jobId = posted.GetProperty("jobId").GetString()!;

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var statusResponse = await Client.GetAsync($"/data/import/{jobId}");
            statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = JsonSerializer.Deserialize<JsonElement>(
                await statusResponse.Content.ReadAsStringAsync(),
                JsonOptions);
            var status = json.GetProperty("status").GetString();
            if (status is "completed" or "completed_with_errors" or "failed")
            {
                return new JobStatus(
                    response.StatusCode,
                    jobId,
                    status,
                    json.GetProperty("total").GetInt32(),
                    json.GetProperty("processed").GetInt32(),
                    json.GetProperty("skipped").GetInt32(),
                    json.GetProperty("errorCount").GetInt32(),
                    json.GetProperty("errors").Deserialize<List<JobError>>(JsonOptions)!);
            }
            await Task.Delay(50);
        }

        throw new TimeoutException($"Import job {jobId} did not finish in {timeoutMs} ms");
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
